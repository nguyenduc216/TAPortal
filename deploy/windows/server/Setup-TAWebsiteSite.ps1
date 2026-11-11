<#
.SYNOPSIS
  Moves the public website tuvangiaiphapta.vn (IIS site TA.LandingPage, static index.html) to the ASP.NET Core project
  TAWebsite.Web (WEBSITE-TA). Requested by the operator on 2026-11-12 ("deploy lên server cả backend và frontend").
  Run as Administrator on the production server; every stage is idempotent.

    .\Setup-TAWebsiteSite.ps1 -Stage prepare    # folder, appsettings.json (DB connection copied from the Portal), app pool TA.Website
    .\Setup-TAWebsiteSite.ps1 -Stage register   # "website" target in C:\TADeploy\deploy-config.json (health = current site)
    # ... deploy the release with Deploy-TARemote.ps1 -Target website (files land in the new folder, site unchanged) ...
    .\Setup-TAWebsiteSite.ps1 -Stage switch     # TA.LandingPage -> new folder + pool; /health must answer, else reverted
    .\Setup-TAWebsiteSite.ps1 -Stage unswitch   # back to the static page (old folder and pool, kept untouched)

  The existing http/https bindings and certificate of TA.LandingPage are kept. Secrets are copied file-to-file, never printed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('prepare', 'register', 'switch', 'unswitch')][string] $Stage,
    [string] $HostName = 'tuvangiaiphapta.vn',
    [string] $SiteName = 'TA.LandingPage',
    [string] $PoolName = 'TA.Website',
    [string] $SitePath = 'H:\WEBDEPLOYED\T.A\website',
    [string] $PortalSettings = 'H:\WEBDEPLOYED\T.A\portal_released\appsettings.json',
    [string] $DeployConfig = 'C:\TADeploy\deploy-config.json',
    [string] $PreviousFile = 'C:\TADeploy\state\website-previous-site.json'
)
$ErrorActionPreference = 'Stop'
Import-Module WebAdministration
function Log([string] $Text) { Write-Host "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Text" }
function SiteInfo { $s = Get-Website -Name $SiteName; "path=$($s.physicalPath) pool=$($s.applicationPool) state=$($s.State)" }
function Health([string] $Url) {
    for ($i = 0; $i -lt 15; $i++) {
        try { $r = Invoke-WebRequest -Uri $Url -TimeoutSec 15 -UseBasicParsing; if ($r.StatusCode -eq 200) { return $true } } catch { }
        Start-Sleep -Seconds 3
    }
    return $false
}
function SetHealth([string] $Url) {
    Copy-Item $DeployConfig "$DeployConfig.bak-$(Get-Date -Format yyyyMMddHHmmss)"
    $cfg = Get-Content $DeployConfig -Raw | ConvertFrom-Json
    if ($cfg.Targets.PSObject.Properties['website']) { $cfg.Targets.website.HealthUrl = $Url; [IO.File]::WriteAllText($DeployConfig, ($cfg | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false)) }
}

if (-not (Get-Website -Name $SiteName)) { throw "Site $SiteName not found" }

switch ($Stage) {
    'prepare' {
        New-Item -ItemType Directory -Force -Path $SitePath | Out-Null
        $settings = Join-Path $SitePath 'appsettings.json'
        if (Test-Path $settings) { Log "appsettings.json exists, kept: $settings" }
        else {
            $portal = Get-Content $PortalSettings -Raw | ConvertFrom-Json
            if ([string]::IsNullOrWhiteSpace($portal.Database.ConnectionString)) { throw 'Portal appsettings.json has no Database:ConnectionString to copy.' }
            $cfg = [ordered]@{
                Logging      = [ordered]@{ LogLevel = [ordered]@{ Default = 'Information'; 'Microsoft.AspNetCore' = 'Warning' } }
                AllowedHosts = "$HostName;www.$HostName;localhost"
                Database     = [ordered]@{ ConnectionString = $portal.Database.ConnectionString }
            }
            [IO.File]::WriteAllText($settings, ($cfg | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
            Log 'appsettings.json created (Database connection copied from the Portal; value not shown)'
        }
        if (-not (Test-Path "IIS:\AppPools\$PoolName")) { New-WebAppPool -Name $PoolName | Out-Null; Log "App pool $PoolName created" }
        Set-ItemProperty "IIS:\AppPools\$PoolName" -Name managedRuntimeVersion -Value ''   # ASP.NET Core: No Managed Code
        & icacls $SitePath /grant "IIS AppPool\$($PoolName):(OI)(CI)RX" | Out-Null
        & icacls $SitePath /grant "IIS AppPool\$($PoolName):(OI)(CI)M" /T | Out-Null      # logs folder of ANCM
        Log "Prepared $SitePath; site still: $(SiteInfo)"
    }
    'register' {
        Copy-Item $DeployConfig "$DeployConfig.bak-$(Get-Date -Format yyyyMMddHHmmss)"
        $cfg = Get-Content $DeployConfig -Raw | ConvertFrom-Json
        # Until the switch, the site still serves the static page: health = its home page (the new release is not live yet).
        $current = (Get-Website -Name $SiteName).physicalPath
        $health = if ($current -eq $SitePath) { "https://$HostName/health" } else { "https://$HostName/" }
        $entry = [pscustomobject][ordered]@{ SiteName = $SiteName; SitePath = $SitePath; AppPool = $PoolName; MainDll = 'TAWebsite.Web.dll'; HealthUrl = $health }
        if ($cfg.Targets.PSObject.Properties['website']) { $cfg.Targets.website = $entry } else { $cfg.Targets | Add-Member -NotePropertyName website -NotePropertyValue $entry }
        [IO.File]::WriteAllText($DeployConfig, ($cfg | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
        Log ("Deploy targets: " + (($cfg.Targets.PSObject.Properties.Name) -join ', ') + "; website health $health")
    }
    'switch' {
        if (-not (Test-Path (Join-Path $SitePath 'TAWebsite.Web.dll'))) { throw "No release in ${SitePath}: deploy target 'website' first" }
        $site = Get-Website -Name $SiteName
        if ($site.physicalPath -ne $SitePath) {
            New-Item -ItemType Directory -Force -Path (Split-Path $PreviousFile) | Out-Null
            [IO.File]::WriteAllText($PreviousFile, ([ordered]@{ physicalPath = $site.physicalPath; applicationPool = $site.applicationPool } | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
            Log "Previous site saved to $PreviousFile ($(SiteInfo))"
        }
        Start-WebAppPool -Name $PoolName -ErrorAction SilentlyContinue
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $SitePath
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $PoolName
        Log "Switched: $(SiteInfo)"
        if (Health "https://$HostName/health") { SetHealth "https://$HostName/health"; Log "RESULT: SWITCHED, https://$HostName/health OK" }
        else {
            $prev = Get-Content $PreviousFile -Raw | ConvertFrom-Json
            Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $prev.physicalPath
            Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $prev.applicationPool
            Log "RESULT: REVERTED (health failed): $(SiteInfo)"
            exit 1
        }
    }
    'unswitch' {
        if (-not (Test-Path $PreviousFile)) { throw "No ${PreviousFile}: nothing to restore" }
        $prev = Get-Content $PreviousFile -Raw | ConvertFrom-Json
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $prev.physicalPath
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $prev.applicationPool
        SetHealth "https://$HostName/"
        Log "RESULT: UNSWITCHED, static site back: $(SiteInfo)"
    }
}
exit 0
