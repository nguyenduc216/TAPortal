<#
.SYNOPSIS
  Dev-machine half of the T.A production deploy (policy: AGENTS.md "## AGENT PRODUCTION DEPLOY").
  Builds, tests and publishes the selected projects, then ships them over SSH to the production
  server, where C:\TADeploy\Apply-TARelease.ps1 backs up, copies, health-checks and rolls back on failure.

    .\Deploy-TARemote.ps1 -Target portal,invoice -DryRun      # everything except upload/apply
    .\Deploy-TARemote.ps1 -Target portal -ChangeId <CHANGE_ID>
    .\Deploy-TARemote.ps1 -Target invoice -Rollback           # restore the previous server backup

  Targets: portal = TAPortal.Web (TA.Portal), invoice = TACustomerPortal.Web (TA.Invoice).
  Exit code: 0 = every target DEPLOYED (or dry run OK), 1 = a check failed or a target rolled back.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('portal', 'invoice')][string[]] $Target,
    [switch] $DryRun,
    [switch] $Rollback,
    # Codec CHANGE_ID; the artifact manifests are stored under .codec/reports/<CHANGE_ID>-<PROJECT>.sha256.
    [string] $ChangeId,
    [string] $SshHost = 'ta-server'
)

$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Projects = @{ portal = 'TAPortal.Web'; invoice = 'TACustomerPortal.Web' }
$RemoteRoot = 'C:\TADeploy'
$Stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$PublishRoot = Join-Path $Repo 'src\publish'
# Git for Windows' OpenSSH first: Windows' own ssh.exe refuses ~/.ssh/config when its ACL has foreign entries.
$Ssh = 'C:\Program Files\Git\usr\bin\ssh.exe'
$Scp = 'C:\Program Files\Git\usr\bin\scp.exe'
if (-not (Test-Path $Ssh)) { $Ssh = Join-Path $env:windir 'System32\OpenSSH\ssh.exe'; $Scp = Join-Path $env:windir 'System32\OpenSSH\scp.exe' }
$SshOptions = @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=20')

New-Item -ItemType Directory -Force -Path (Join-Path $PublishRoot 'deploy-logs') | Out-Null
Start-Transcript -Path (Join-Path $PublishRoot "deploy-logs\deploy-$Stamp.log") | Out-Null

function Step([string] $Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }
function Ok([string] $Text) { Write-Host "  OK   $Text" -ForegroundColor Green }
function Warn([string] $Text) { Write-Host "  WARN $Text" -ForegroundColor Yellow }

function Invoke-Checked([string] $Exe, [string[]] $Arguments) {
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Exe $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

# Runs a PowerShell command on the server; prints its output and returns the remote exit code.
function Invoke-Remote([string] $Command) {
    $output = & $Ssh @SshOptions $SshHost $Command 2>&1
    $code = $LASTEXITCODE
    foreach ($line in $output) { Write-Host "  | $line" }
    return $code
}

function Get-ManifestSha([string] $Folder) {
    # Codec artifact SHA256: "<sha256>  <relative/posix/path>\n" per file, ordinal path order, UTF-8 without BOM.
    $prefix = $Folder.TrimEnd('\').Length + 1
    $files = @{}
    foreach ($f in Get-ChildItem $Folder -Recurse -File) { $files[$f.FullName.Substring($prefix).Replace('\', '/')] = $f.FullName }
    $paths = [string[]]@($files.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $lines = foreach ($p in $paths) { "$((Get-FileHash $files[$p] -Algorithm SHA256).Hash.ToLowerInvariant())  $p`n" }
    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes(($lines -join ''))
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $digest = (($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join '')
    return [pscustomobject]@{ Sha = $digest; Bytes = $bytes; Count = @($lines).Count }
}

$results = New-Object System.Collections.Generic.List[object]
$failed = $false
try { :main do {
    # ------------------------------------------------------------ rollback only
    if ($Rollback) {
        foreach ($t in $Target) {
            Step "Rollback $t on $SshHost"
            $code = Invoke-Remote "& '$RemoteRoot\Apply-TARelease.ps1' -Target $t -Rollback; exit `$LASTEXITCODE"
            $status = if ($code -eq 0) { 'ROLLED_BACK' } else { 'ROLLBACK_FAILED' }
            if ($code -ne 0) { $failed = $true }
            $results.Add([pscustomobject]@{ Target = $t; Result = $status })
        }
        break main
    }

    # ------------------------------------------------------------ 1. source state
    Step '1. Source'
    $branch = (& git -C $Repo rev-parse --abbrev-ref HEAD).Trim()
    $commit = (& git -C $Repo rev-parse HEAD).Trim()
    Write-Host "  Branch $branch @ $commit"
    $dirty = & git -C $Repo status --porcelain --untracked-files=no
    & git -C $Repo fetch --quiet origin
    $pushed = [bool](& git -C $Repo branch -r --contains $commit)
    foreach ($problem in @(
            @{ Bad = [bool]$dirty; Text = 'tracked files have uncommitted changes' },
            @{ Bad = -not $pushed; Text = 'HEAD is not pushed to origin' })) {
        if ($problem.Bad) {
            if ($DryRun) { Warn "$($problem.Text) (allowed in -DryRun only)" } else { throw "Cannot deploy: $($problem.Text)." }
        }
    }
    if (-not $dirty -and $pushed) { Ok 'Working tree clean, HEAD pushed' }

    # ------------------------------------------------------------ 2. server preflight (read-only)
    Step "2. Server $SshHost (read-only check)"
    $names = ($Target | ForEach-Object { "'$_'" }) -join ','
    # Remote commands use single quotes only: Windows PowerShell 5.1 mangles embedded double quotes for native exes.
    $check = "`$c = Get-Content '$RemoteRoot\deploy-config.json' -Raw | ConvertFrom-Json; " +
             "if (-not (Test-Path '$RemoteRoot\Apply-TARelease.ps1')) { 'MISSING Apply-TARelease.ps1'; exit 3 }; " +
             "foreach (`$t in @($names)) { `$x = `$c.Targets.`$t; if (-not `$x) { 'MISSING target ' + `$t; exit 3 }; " +
             "`$free = [math]::Round((Get-PSDrive `$x.SitePath.Substring(0, 1)).Free / 1GB, 1); " +
             "`$t + ' -> ' + `$x.SiteName + ' | ' + `$x.SitePath + ' | pool ' + `$x.AppPool + ' | ' + `$x.HealthUrl + ' | free ' + `$free + ' GB' }"
    if ((Invoke-Remote $check) -ne 0) { throw "Server preflight failed on $SshHost" }
    Ok 'Server ready'

    # ------------------------------------------------------------ 3. gates
    Step '3. Gates: restore, Release build, Safe tests'
    $sln = Join-Path $Repo 'TAPortal.sln'
    Invoke-Checked 'dotnet' @('restore', $sln, '--nologo', '-v', 'q')
    Invoke-Checked 'dotnet' @('build', $sln, '-c', 'Release', '--no-restore', '--nologo', '-v', 'q')
    Invoke-Checked 'dotnet' @('test', $sln, '-c', 'Release', '--no-build', '--nologo', '-v', 'q', '--filter', 'FullyQualifiedName~TACustomerPortal.Tests.Safe')
    Ok 'Gates passed'

    # ------------------------------------------------------------ 4. publish + package
    $packages = @{}
    foreach ($t in $Target) {
        $project = $Projects[$t]
        Step "4. Publish $t ($project)"
        $out = Join-Path $PublishRoot "deployed-$project"
        if ((Test-Path $out) -and (Get-ChildItem $out -Force | Select-Object -First 1)) {
            $keep = Join-Path $PublishRoot "rollback\$Stamp\$project"
            New-Item -ItemType Directory -Force -Path (Split-Path $keep) | Out-Null
            $old = Get-ManifestSha $out
            Move-Item $out $keep
            Write-Host "  Previous artifact $($old.Sha) moved to $keep"
        }
        Invoke-Checked 'dotnet' @('publish', (Join-Path $Repo "src\$project\$project.csproj"), '-c', 'Release', '-o', $out, '--no-restore', '--nologo', '-v', 'q')
        if (-not (Test-Path (Join-Path $out "$project.dll"))) { throw "Publish output has no $project.dll" }

        $manifest = Get-ManifestSha $out
        $manifestDir = if ($ChangeId) { Join-Path $Repo '.codec\reports' } else { Join-Path $PublishRoot 'manifests' }
        New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null
        $manifestName = if ($ChangeId) { "$ChangeId-$project.sha256" } else { "$project-$Stamp.sha256" }
        [IO.File]::WriteAllBytes((Join-Path $manifestDir $manifestName), $manifest.Bytes)

        $pkgDir = Join-Path $PublishRoot 'packages'
        New-Item -ItemType Directory -Force -Path $pkgDir | Out-Null
        $zip = Join-Path $pkgDir ("{0}-{1}-{2}.zip" -f $t, $commit.Substring(0, 7), $Stamp)
        # Entries use '/' (ZipFile.CreateFromDirectory on .NET Framework writes '\').
        Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
        try {
            $prefix = $out.TrimEnd('\').Length + 1
            foreach ($f in Get-ChildItem $out -Recurse -File) {
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $f.FullName.Substring($prefix).Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal)
            }
        }
        finally { $archive.Dispose() }
        $zipSha = (Get-FileHash $zip -Algorithm SHA256).Hash
        $packages[$t] = [pscustomobject]@{ Zip = $zip; Sha = $zipSha; Manifest = $manifest.Sha; Files = $manifest.Count }
        Ok "$($manifest.Count) files | artifact $($manifest.Sha) | package $zipSha"
    }

    if ($DryRun) {
        foreach ($t in $Target) { $results.Add([pscustomobject]@{ Target = $t; Project = $Projects[$t]; Commit = $commit.Substring(0, 7); Artifact = $packages[$t].Manifest; Package = $packages[$t].Sha; Result = 'DRY_RUN (not uploaded)' }) }
        break main
    }

    # ------------------------------------------------------------ 5. upload + apply
    foreach ($t in $Target) {
        $p = $packages[$t]
        $name = Split-Path $p.Zip -Leaf
        Step "5. Deploy $t -> $SshHost"
        Invoke-Checked $Scp (@($SshOptions) + @($p.Zip, "${SshHost}:C:/TADeploy/incoming/$name"))
        Ok "Uploaded $name"
        $remotePkg = "$RemoteRoot\incoming\$name"
        $code = Invoke-Remote ("& '$RemoteRoot\Apply-TARelease.ps1' -Target $t -Package '$remotePkg' -Sha256 $($p.Sha) -ExpectedCommit $commit; " +
                               "`$code = `$LASTEXITCODE; if (`$code -eq 0) { Remove-Item '$remotePkg' -Force }; exit `$code")
        $status = if ($code -eq 0) { 'DEPLOYED' } else { 'ROLLED_BACK_OR_FAILED (see C:\TADeploy\logs on server)' }
        if ($code -ne 0) { $failed = $true }
        $results.Add([pscustomobject]@{ Target = $t; Project = $Projects[$t]; Commit = $commit.Substring(0, 7); Artifact = $p.Manifest; Package = $p.Sha; Result = $status })
        if ($code -ne 0) { Warn "Stopping: $t did not deploy, later targets are skipped."; break }
    }
} while ($false) }
catch {
    $failed = $true
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    Step 'Summary'
    $results | Format-List | Out-String | Write-Host
    Stop-Transcript | Out-Null
}
if ($failed) { exit 1 }
exit 0
