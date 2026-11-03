<#
.SYNOPSIS
  Server-side half of the T.A deploy. Installed by Setup-TADeployServer.ps1 into C:\TADeploy and
  invoked over SSH from the dev machine. Never runs SQL.

    Apply-TARelease.ps1 -Target portal|invoice -Package C:\TADeploy\incoming\x.zip -Sha256 <hash> [-ExpectedCommit <sha>]
    Apply-TARelease.ps1 -Target portal -Rollback        (restore the most recent backup)

  1. Verify the uploaded zip's SHA-256 and that it contains the target's main DLL.
  2. Stop the IIS app pool, back up the site folder.
  3. Copy the new build in, keeping appsettings*.json, web.config, keys\, logs\, App_Data\ and uploads untouched.
  4. Start the pool and probe the health URL (HTTP 200; if it returns JSON with "commit", it must match).
  5. On any failure after step 2, restore the backup and start the pool again.
  Exit code: 0 = DEPLOYED / ROLLED BACK on request, 1 = failed (site restored from backup).
#>
param(
    # Target name registered by Setup-TADeployServer.ps1 (portal, invoice, webhooks).
    [Parameter(Mandatory = $true)][string] $Target,
    [string] $Package,
    [string] $Sha256,
    [string] $ExpectedCommit,
    [switch] $Rollback,
    [int] $KeepBackups = 5
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Config = Get-Content (Join-Path $Root 'deploy-config.json') -Raw | ConvertFrom-Json
Import-Module WebAdministration

$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$LogFile = Join-Path $Root "logs\apply-$Target-$Stamp.log"
New-Item -ItemType Directory -Force -Path (Split-Path $LogFile) | Out-Null
function Log([string] $m) {
    $line = '[{0}] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m
    # Write-Host, not Write-Output: functions such as Test-Health return booleans and must not carry log lines.
    Write-Host $line
    Add-Content -Path $LogFile -Value $line -Encoding UTF8
}

$T = $Config.Targets.$Target
if (-not $T) { throw "Target '$Target' is not registered on this server. Run Setup-TADeployServer.bat again to add it." }
$Site = $T.SitePath
$Pool = $T.AppPool
Log "Target: $Target -> site=$($T.SiteName) folder=$Site pool=$Pool"
$Preserve = @('appsettings.json', 'appsettings.Production.json', 'appsettings.Development.json', 'web.config')
$PreserveDirs = @('keys', 'logs', 'App_Data', 'wwwroot\uploads')

function Wait-PoolState([string] $State) {
    for ($i = 0; $i -lt 30; $i++) {
        if ((Get-WebAppPoolState -Name $Pool).Value -eq $State) { return }
        Start-Sleep -Seconds 1
    }
    throw "App pool '$Pool' did not reach state $State"
}

function Stop-Pool {
    if ((Get-WebAppPoolState -Name $Pool).Value -ne 'Stopped') { Stop-WebAppPool -Name $Pool }
    Wait-PoolState 'Stopped'
}

function Start-Pool {
    if ((Get-WebAppPoolState -Name $Pool).Value -ne 'Started') { Start-WebAppPool -Name $Pool }
    Wait-PoolState 'Started'
}

function Test-Health {
    if (-not $T.HealthUrl) { Log 'Health: no URL configured, skipped'; return $true }
    for ($i = 0; $i -lt 20; $i++) {
        try {
            $r = Invoke-WebRequest -Uri $T.HealthUrl -TimeoutSec 15 -UseBasicParsing
            if ($r.StatusCode -eq 200) {
                $commit = $null
                if ($r.Headers['Content-Type'] -like 'application/json*') { try { $commit = ($r.Content | ConvertFrom-Json).commit } catch { } }
                if (-not $ExpectedCommit -or -not $commit -or $commit.StartsWith($ExpectedCommit)) {
                    Log "Health OK: HTTP 200 $(if ($commit) { "commit=$commit" })"
                    return $true
                }
                Log "Health: running commit $commit, waiting for $ExpectedCommit"
            }
            else { Log "Health: HTTP $($r.StatusCode)" }
        }
        catch { Log "Health probe failed: $($_.Exception.Message)" }
        Start-Sleep -Seconds 3
    }
    return $false
}

function Copy-Release([string] $From, [string] $To) {
    # Mirror the build but never touch production config, keys, logs or user uploads.
    $xd = $PreserveDirs | ForEach-Object { Join-Path $To $_ }
    & robocopy $From $To /MIR /R:2 /W:2 /NFL /NDL /NP /XF @Preserve /XD @xd | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
}

function Remove-OldBackups {
    Get-ChildItem $Config.BackupRoot -Directory -Filter "$Target-*" | Sort-Object Name -Descending |
        Select-Object -Skip $KeepBackups | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------- manual rollback
if ($Rollback) {
    $last = Get-ChildItem $Config.BackupRoot -Directory -Filter "$Target-*" | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $last) { throw "No backup found for $Target" }
    Log "Rollback requested -> $($last.FullName)"
    Stop-Pool
    Copy-Release $last.FullName $Site
    Start-Pool
    if (Test-Health) { Log "RESULT: ROLLED BACK to $($last.Name)"; exit 0 }
    Log 'RESULT: ROLLED BACK but health check FAILED'
    exit 1
}

# ---------------------------------------------------------------- deploy
if (-not $Package -or -not $Sha256) { throw 'Package and Sha256 are required (or use -Rollback).' }
Log "Package: $Package"
$actual = (Get-FileHash $Package -Algorithm SHA256).Hash
if ($actual -ne $Sha256.ToUpperInvariant()) { throw "Checksum mismatch. Expected $Sha256, got $actual" }
Log "Checksum OK: $actual"

$staging = Join-Path $Root "staging\$Target-$Stamp"
Expand-Archive -Path $Package -DestinationPath $staging -Force
if (-not (Test-Path (Join-Path $staging $T.MainDll))) { throw "Package does not contain $($T.MainDll) at its root - wrong project or wrong zip layout" }

$backup = Join-Path $Config.BackupRoot "$Target-$Stamp"
$stopped = $false
$deployed = $false
try {
    Log "Stopping app pool '$Pool'"
    Stop-Pool
    $stopped = $true

    Log "Backup -> $backup"
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    & robocopy $Site $backup /MIR /R:2 /W:2 /NFL /NDL /NP /XD (Join-Path $Site 'logs') (Join-Path $Site 'wwwroot\uploads') | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Backup failed with robocopy exit code $LASTEXITCODE" }

    Log 'Copying new build'
    Copy-Release $staging $Site
    # First deploy into an empty folder: take the config files from the package.
    foreach ($f in $Preserve) {
        if (-not (Test-Path (Join-Path $Site $f)) -and (Test-Path (Join-Path $staging $f))) { Copy-Item (Join-Path $staging $f) (Join-Path $Site $f) }
    }

    Log "Starting app pool '$Pool'"
    Start-Pool
    $stopped = $false

    if (-not (Test-Health)) { throw 'Health check failed after deploy' }
    Log "RESULT: DEPLOYED $Target $ExpectedCommit"
    $deployed = $true
}
catch {
    Log "ERROR: $($_.Exception.Message) -- rolling back"
    if (-not $stopped) { Stop-Pool }
    if (Test-Path $backup) { Copy-Release $backup $Site; Log 'Backup restored' }
    Start-Pool
    $ok = Test-Health
    Log "RESULT: ROLLED BACK (health after rollback: $ok)"
    exit 1
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    Remove-OldBackups
}
# Explicit exit code: otherwise the caller sees robocopy's last exit code (1 = files copied).
if ($deployed) { exit 0 }
exit 1
