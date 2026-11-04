<#
.SYNOPSIS
  Creates the T.A webhook site on the production server (run as Administrator; idempotent, every step re-runnable).
  Requested by the operator on 2026-11-04 (DNS A webhook.hoadontudong.vn -> this server).

    .\Setup-TAWebhooksSite.ps1 -Stage site       # folder, appsettings.json (copied here from the Portal), app pool, IIS site (http)
    .\Setup-TAWebhooksSite.ps1 -Stage cert       # Let's Encrypt certificate + https binding via win-acme, renewal task repaired
    .\Setup-TAWebhooksSite.ps1 -Stage register   # registers the "webhooks" target in C:\TADeploy\deploy-config.json

  Secrets (connection string, Tingee keys) are copied file-to-file on this server and never printed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('site', 'cert', 'register')][string] $Stage,
    [string] $HostName = 'webhook.hoadontudong.vn',
    [string] $SiteName = 'TA.Webhooks',
    [string] $PoolName = 'TA.Webhooks',
    [string] $SitePath = 'H:\WEBDEPLOYED\T.A\webhooks',
    [string] $PortalSettings = 'H:\WEBDEPLOYED\T.A\portal_released\appsettings.json',
    [string] $Wacs = 'C:\tools\win-acme\wacs.exe',
    [string] $DeployConfig = 'C:\TADeploy\deploy-config.json'
)
$ErrorActionPreference = 'Stop'
Import-Module WebAdministration
function Log([string] $Text) { Write-Host "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Text" }

switch ($Stage) {
    'site' {
        New-Item -ItemType Directory -Force -Path $SitePath | Out-Null
        $settings = Join-Path $SitePath 'appsettings.json'
        if (Test-Path $settings) { Log "appsettings.json exists, kept: $settings" }
        else {
            $portal = Get-Content $PortalSettings -Raw | ConvertFrom-Json
            if ([string]::IsNullOrWhiteSpace($portal.Database.ConnectionString) -or [string]::IsNullOrWhiteSpace($portal.Tingee.SecretKey)) { throw 'Portal appsettings.json has no Database/Tingee settings to copy.' }
            $cfg = [ordered]@{
                Logging      = [ordered]@{ LogLevel = [ordered]@{ Default = 'Information'; 'Microsoft.AspNetCore' = 'Warning' } }
                AllowedHosts = $HostName
                Database     = [ordered]@{ ConnectionString = $portal.Database.ConnectionString }
                Tingee       = [ordered]@{
                    BaseUrl = $portal.Tingee.BaseUrl; ClientId = $portal.Tingee.ClientId; SecretKey = $portal.Tingee.SecretKey
                    Mode = $portal.Tingee.Mode; WebhookMaxAgeMinutes = 10; WebhookBaseUrl = "https://$HostName"
                }
            }
            [IO.File]::WriteAllText($settings, ($cfg | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
            Log "appsettings.json created from the Portal settings (Database + Tingee; values not shown)"
        }
        if (-not (Test-Path "IIS:\AppPools\$PoolName")) {
            New-WebAppPool -Name $PoolName | Out-Null
            Log "App pool $PoolName created"
        }
        Set-ItemProperty "IIS:\AppPools\$PoolName" -Name managedRuntimeVersion -Value ''      # ASP.NET Core: No Managed Code
        Set-ItemProperty "IIS:\AppPools\$PoolName" -Name startMode -Value 'AlwaysRunning'     # a webhook must not wait for a cold start
        Set-ItemProperty "IIS:\AppPools\$PoolName" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
        & icacls $SitePath /grant "IIS AppPool\$($PoolName):(OI)(CI)RX" | Out-Null
        if (-not (Get-Website -Name $SiteName)) {
            New-Website -Name $SiteName -PhysicalPath $SitePath -ApplicationPool $PoolName -HostHeader $HostName -Port 80 | Out-Null
            Log "Site $SiteName created (http $HostName)"
        }
        else { Log "Site $SiteName exists" }
        Start-WebAppPool -Name $PoolName -ErrorAction SilentlyContinue
        Start-Website -Name $SiteName -ErrorAction SilentlyContinue
        Log ("Site state: " + (Get-Website -Name $SiteName).State + ", bindings: " + (((Get-Website -Name $SiteName).bindings.Collection | ForEach-Object { $_.protocol + ' ' + $_.bindingInformation }) -join ' | '))
    }
    'cert' {
        $site = Get-Website -Name $SiteName
        if (-not $site) { throw "Site $SiteName missing: run -Stage site first" }
        if (-not (Test-Path $Wacs)) { throw "win-acme not found at $Wacs" }
        Log "Requesting a Let's Encrypt certificate for $HostName (site id $($site.id))"
        & $Wacs --source iis --siteid $site.id --host $HostName --installation iis --store certificatestore --certificatestore WebHosting --accepttos --closeonfinish
        if ($LASTEXITCODE -ne 0) { throw "win-acme failed with exit code $LASTEXITCODE (see C:\ProgramData\win-acme\acme-v02.api.letsencrypt.org\Log)" }
        # The renewal task pointed at a deleted temp copy of wacs.exe: re-create it from the installed one.
        & $Wacs --setuptaskscheduler --closeonfinish
        $task = Get-ScheduledTask | Where-Object TaskName -match 'win-acme' | Select-Object -First 1
        Log ("Renewal task: " + $task.Actions[0].Execute + " (exists: " + (Test-Path $task.Actions[0].Execute) + ")")
        Log ("Bindings: " + (((Get-Website -Name $SiteName).bindings.Collection | ForEach-Object { $_.protocol + ' ' + $_.bindingInformation }) -join ' | '))
    }
    'register' {
        Copy-Item $DeployConfig "$DeployConfig.bak-$(Get-Date -Format yyyyMMddHHmmss)"
        $cfg = Get-Content $DeployConfig -Raw | ConvertFrom-Json
        $entry = [pscustomobject][ordered]@{ SiteName = $SiteName; SitePath = $SitePath; AppPool = $PoolName; MainDll = 'TAPortal.Webhooks.dll'; HealthUrl = "https://$HostName/health" }
        if ($cfg.Targets.PSObject.Properties['webhooks']) { $cfg.Targets.webhooks = $entry } else { $cfg.Targets | Add-Member -NotePropertyName webhooks -NotePropertyValue $entry }
        [IO.File]::WriteAllText($DeployConfig, ($cfg | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
        Log ("Deploy targets: " + (($cfg.Targets.PSObject.Properties.Name) -join ', '))
    }
}
exit 0
