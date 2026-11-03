<#
.SYNOPSIS
  One-time setup of the T.A production server so the two sites
    portal  = T.A Portal (Admin)       -> TAPortal.Web.dll
    invoice = T.A Customer Portal      -> TACustomerPortal.Web.dll
  can be deployed automatically over SSH (T.A Webhooks can be registered later as "webhooks").
  Run on the SERVER as Administrator (double-click Setup-TADeployServer.bat).

  What it does (asks before each important choice):
    1. Checks IIS and the .NET 8 Hosting Bundle.
    2. Lists IIS sites/applications, you pick one per project -> finds its folder and app pool.
    3. Installs and starts the Windows OpenSSH Server, PowerShell as the SSH shell.
    4. Authorises ONLY the T.A deploy key (ta_deploy.pub next to this script), key login only.
    5. Opens the SSH port in Windows Firewall, optionally only for the IP of the deploy machine,
       and tries to forward a port on the router (UPnP) when the server is behind NAT.
    6. Creates C:\TADeploy with Apply-TARelease.ps1 + deploy-config.json.
    7. Prints a summary (C:\TADeploy\server-info.txt) to send back to Claude.

  It never touches the database, the site files, appsettings or IIS bindings.
  Running it again adds/updates projects and keeps the ones already registered.
#>
$ErrorActionPreference = 'Stop'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$DeployRoot = 'C:\TADeploy'
$LogFile = Join-Path $DeployRoot 'logs\setup.log'
New-Item -ItemType Directory -Force -Path (Split-Path $LogFile) | Out-Null

function Log([string] $Text) { Add-Content -Path $LogFile -Value ('[{0}] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Text) -Encoding UTF8 }
function Ask([string] $Question, [string] $Default) {
    $suffix = if ($Default) { " [$Default]" } else { '' }
    $answer = Read-Host "$Question$suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
    return $answer.Trim()
}
function Step([string] $Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan; Log $Text }
function Ok([string] $Text) { Write-Host "  $Text" -ForegroundColor Green; Log "OK $Text" }
function Warn([string] $Text) { Write-Host "  $Text" -ForegroundColor Yellow; Log "WARN $Text" }

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Hay chay bang quyen Administrator (chuot phai > Run as administrator).'
}

try {
# ---------------------------------------------------------------- 1. IIS + Hosting Bundle
Step '1. Kiem tra IIS va .NET 8 Hosting Bundle'
Import-Module WebAdministration
$ancm = (Test-Path "$env:ProgramFiles\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll") -or (Test-Path "$env:windir\system32\inetsrv\aspnetcorev2.dll")
$aspnet8 = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) { $aspnet8 = [bool](& dotnet --list-runtimes | Where-Object { $_ -like 'Microsoft.AspNetCore.App 8.*' }) }
if ($ancm -and $aspnet8) { Ok '.NET 8 Hosting Bundle: CO' }
else { Warn "Thieu .NET 8 Hosting Bundle (ANCM=$ancm, AspNetCore8=$aspnet8). Tai tai https://dotnet.microsoft.com/download/dotnet/8.0 roi chay iisreset (thieu se bi loi 500.31)." }

# ---------------------------------------------------------------- 2. IIS targets
Step '2. Dang ky cac website T.A trong IIS (moi du an = 1 target)'
# Sites and their sub-applications, so a project hosted as "Site/app" can be picked too.
$choices = New-Object System.Collections.Generic.List[object]
foreach ($s in (Get-Website | Sort-Object Name)) {
    $bind = ($s.Bindings.Collection | ForEach-Object { "$($_.protocol) $($_.bindingInformation)" }) -join ', '
    $choices.Add([pscustomobject]@{ Name = $s.Name; Site = $s; AppPath = ''; Path = [Environment]::ExpandEnvironmentVariables($s.PhysicalPath); Pool = $s.applicationPool; Bind = $bind })
    foreach ($a in @(Get-WebApplication -Site $s.Name)) {
        $appPath = $a.path.TrimStart('/')
        $choices.Add([pscustomobject]@{ Name = "$($s.Name)/$appPath"; Site = $s; AppPath = "/$appPath"; Path = [Environment]::ExpandEnvironmentVariables($a.PhysicalPath); Pool = $a.applicationPool; Bind = $bind })
    }
}
if ($choices.Count -eq 0) { throw 'Khong tim thay website nao trong IIS.' }
for ($i = 0; $i -lt $choices.Count; $i++) {
    $c = $choices[$i]
    Write-Host ("  [{0}] {1}  |  pool={2}  |  {3}  |  {4}" -f ($i + 1), $c.Name, $c.Pool, $c.Path, $c.Bind)
}

# portal = T.A Portal (Admin), invoice = T.A Customer Portal. Webhooks is added later the same way.
$KnownTargets = [ordered]@{
    portal  = @{ Dll = 'TAPortal.Web.dll';         Health = '/'; Label = 'T.A Portal (Admin)' }
    invoice = @{ Dll = 'TACustomerPortal.Web.dll'; Health = '/'; Label = 'T.A Customer Portal' }
}
$OptionalTargets = @{ webhooks = @{ Dll = 'TAPortal.Webhooks.dll'; Health = '/health'; Label = 'T.A Webhooks (Tingee)' } }
$configPath = Join-Path $DeployRoot 'deploy-config.json'
$targets = [ordered]@{}
if (Test-Path $configPath) {
    Copy-Item $configPath "$configPath.bak-$(Get-Date -Format yyyyMMddHHmmss)"
    $old = Get-Content $configPath -Raw | ConvertFrom-Json
    if ($old.Targets) { $old.Targets.PSObject.Properties | ForEach-Object { $targets[$_.Name] = $_.Value } }
    if ($targets.Count) { Write-Host "  Da co: $($targets.Keys -join ', ') (giu nguyen neu khong dang ky lai)" }
}

$todo = @($KnownTargets.Keys | Where-Object { -not $targets.Contains($_) })
$next = if ($todo.Count) { $todo[0] } else { 'portal' }
do {
    $targetName = (Ask "Ten target (portal = T.A Portal, invoice = T.A Customer; webhooks = sau nay)" $next).ToLowerInvariant()
    $known = $KnownTargets[$targetName]
    if (-not $known) { $known = $OptionalTargets[$targetName] }
    if ($known) { Write-Host "  -> $($known.Label), DLL $($known.Dll)" }
    $mainDll = Ask 'File DLL chinh cua du an' $(if ($known) { $known.Dll } else { 'TAPortal.Web.dll' })
    $pick = [int](Ask "Nhap so thu tu website IIS cho target '$targetName' (0 = bo qua)" '0')
    if ($pick -ge 1 -and $pick -le $choices.Count) {
        $c = $choices[$pick - 1]
        Write-Host "  Website : $($c.Name)"
        Write-Host "  Thu muc : $($c.Path)"
        Write-Host "  AppPool : $($c.Pool)"
        $register = $true
        if (-not (Test-Path (Join-Path $c.Path $mainDll))) {
            Warn "Khong thay $mainDll trong $($c.Path) (dung neu day la lan deploy dau tien)."
            $register = (Ask 'Van dang ky target nay? (y/n)' 'n') -eq 'y'
        }
        if ($register) {
            $hostBinding = $c.Site.Bindings.Collection | Where-Object { $_.protocol -eq 'https' } | Select-Object -First 1
            if (-not $hostBinding) { $hostBinding = $c.Site.Bindings.Collection | Select-Object -First 1 }
            $parts = $hostBinding.bindingInformation.Split(':')
            $hostName = if ($parts.Count -ge 3 -and $parts[2]) { $parts[2] } else { 'localhost' }
            $healthDefault = "$($hostBinding.protocol)://$hostName$($c.AppPath)$(if ($known) { $known.Health } else { '/' })"
            $healthUrl = Ask 'URL kiem tra sau khi deploy (phai tra ve HTTP 200)' $healthDefault

            $targets[$targetName] = [ordered]@{
                SiteName  = $c.Name
                SitePath  = $c.Path
                AppPool   = $c.Pool
                MainDll   = $mainDll
                HealthUrl = $healthUrl
            }
            Ok "Da dang ky target '$targetName' -> $($c.Name)"
        }
    }
    $todo = @($KnownTargets.Keys | Where-Object { -not $targets.Contains($_) })
    $next = if ($todo.Count) { $todo[0] } else { '' }
} while ((Ask 'Dang ky them website/du an khac? (y/n)' $(if ($next) { 'y' } else { 'n' })) -eq 'y')

# ---------------------------------------------------------------- 3. OpenSSH server
Step '3. Cai OpenSSH Server'
if (-not (Get-Service sshd -ErrorAction SilentlyContinue)) {
    $cap = $null
    if (Get-Command Get-WindowsCapability -ErrorAction SilentlyContinue) {
        $cap = Get-WindowsCapability -Online -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'OpenSSH.Server*' } | Select-Object -First 1
    }
    if ($cap) {
        Write-Host '  Dang cai OpenSSH Server (co the mat vai phut)...'
        Add-WindowsCapability -Online -Name $cap.Name | Out-Null
    }
    else {
        # Windows older than Server 2019: official Microsoft MSI, signature checked before install.
        $msiUrl = 'https://github.com/PowerShell/Win32-OpenSSH/releases/download/v9.5.0.0p1-Beta/OpenSSH-Win64-v9.5.0.0.msi'
        Write-Host "  Dang tai OpenSSH: $msiUrl"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $msi = Join-Path $env:TEMP 'OpenSSH-Win64.msi'
        Invoke-WebRequest -Uri $msiUrl -OutFile $msi -UseBasicParsing
        $sig = Get-AuthenticodeSignature $msi
        if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'Microsoft') { throw "OpenSSH MSI khong co chu ky Microsoft hop le ($($sig.Status))." }
        $p = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" ADDLOCAL=Server /qn" -Wait -PassThru
        if ($p.ExitCode -ne 0) { throw "Cai OpenSSH MSI that bai, exit code $($p.ExitCode)." }
    }
}
Set-Service sshd -StartupType Automatic
Start-Service sshd   # first start creates host keys and sshd_config
New-Item -Path 'HKLM:\SOFTWARE\OpenSSH' -Force | Out-Null
New-ItemProperty -Path 'HKLM:\SOFTWARE\OpenSSH' -Name DefaultShell -PropertyType String -Force `
    -Value "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" | Out-Null
Ok 'OpenSSH Server dang chay, shell mac dinh = PowerShell.'

# ---------------------------------------------------------------- 4. Deploy key
Step '4. Cho phep khoa SSH cua may deploy'
$pubFile = Join-Path $Here 'ta_deploy.pub'
$pubKey = if (Test-Path $pubFile) { (Get-Content $pubFile -Raw).Trim() } else { Read-Host 'Dan public key (dong bat dau bang ssh-ed25519)' }
if ($pubKey -notmatch '^ssh-(ed25519|rsa) ') { throw 'Public key khong hop le.' }

# Administrators authenticate from administrators_authorized_keys (Windows OpenSSH rule).
$authKeys = Join-Path $env:ProgramData 'ssh\administrators_authorized_keys'
$existing = if (Test-Path $authKeys) { @(Get-Content $authKeys) } else { @() }
if ($existing -notcontains $pubKey) { Add-Content -Path $authKeys -Value $pubKey -Encoding ascii }
# SIDs instead of names so this also works on non-English Windows.
& icacls $authKeys /inheritance:r /grant '*S-1-5-32-544:F' /grant '*S-1-5-18:F' | Out-Null
Ok "Da them khoa vao $authKeys"

$sshdConfig = Join-Path $env:ProgramData 'ssh\sshd_config'
Copy-Item $sshdConfig "$sshdConfig.bak-$(Get-Date -Format yyyyMMddHHmmss)"
$port = [int](Ask 'Cong SSH tren server' '22')
$keyOnly = (Ask 'Chi cho dang nhap bang khoa, tat dang nhap bang mat khau? (y/n)' 'y') -eq 'y'
$cfg = @(Get-Content $sshdConfig | Where-Object { $_ -notmatch '^\s*#?\s*(Port|PasswordAuthentication|PubkeyAuthentication)\s' })
$cfg = @("Port $port", 'PubkeyAuthentication yes', ("PasswordAuthentication " + $(if ($keyOnly) { 'no' } else { 'yes' }))) + $cfg
if (-not ($cfg | Where-Object { $_ -match '^\s*Match\s+Group\s+administrators' })) {
    $cfg += @('', 'Match Group administrators', '       AuthorizedKeysFile __PROGRAMDATA__/ssh/administrators_authorized_keys')
}
Set-Content -Path $sshdConfig -Value $cfg -Encoding ascii
Restart-Service sshd
$listening = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue
if ($listening) { Ok "sshd dang lang nghe cong $port" } else { Warn "sshd KHONG lang nghe cong $port - xem Event Viewer > OpenSSH." }

# ---------------------------------------------------------------- 5. Firewall + router
Step '5. Mo cong tren Windows Firewall va router'
$allowFrom = Ask 'IP public cua may deploy (cach nhau dau phay; bo trong = moi IP)' ''
Get-NetFirewallRule -DisplayName 'TA Deploy SSH' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
$ruleArgs = @{ DisplayName = 'TA Deploy SSH'; Direction = 'Inbound'; Protocol = 'TCP'; LocalPort = $port; Action = 'Allow' }
if ($allowFrom) { $ruleArgs.RemoteAddress = $allowFrom.Split(',') | ForEach-Object { $_.Trim() } }
New-NetFirewallRule @ruleArgs | Out-Null
if ($allowFrom) { Ok "Firewall: cong $port chi mo cho $allowFrom" } else { Warn 'Cong SSH mo cho moi IP. Nen gioi han IP khi co the.' }

$netCfg = Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' } | Select-Object -First 1
$lanIp = $netCfg.IPv4Address.IPAddress
$gateway = $netCfg.IPv4DefaultGateway.NextHop
$externalPort = $port
$behindNat = $lanIp -match '^(10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[01])\.)'
if ($behindNat) {
    Write-Host "  Server nam sau router (IP noi bo $lanIp, gateway $gateway)."
    $externalPort = [int](Ask 'Cong ngoai tren router de chuyen vao SSH' '22022')
    $upnpOk = $false
    try {
        $maps = (New-Object -ComObject HNetCfg.NATUPnP).StaticPortMappingCollection
        if ($null -ne $maps) {
            try { if ($maps.Item($externalPort, 'TCP')) { $maps.Remove($externalPort, 'TCP') } } catch { }
            [void]$maps.Add($externalPort, 'TCP', $port, $lanIp, $true, 'TA Deploy SSH')
            $upnpOk = $true
            Ok "UPnP: da tao port forward $externalPort -> ${lanIp}:$port"
        }
        else { Warn 'UPnP: router khong ho tro hoac dang tat UPnP.' }
    }
    catch { Warn "UPnP: khong dung duoc ($($_.Exception.Message))." }
    if (-not $upnpOk) {
        Warn '>>> Can tao tay tren trang quan tri router (NAT / Port Forwarding / Virtual Server):'
        Warn "    Giao thuc TCP | Cong ngoai $externalPort | IP noi bo $lanIp | Cong noi bo $port | Trang router http://$gateway"
    }
}
else { Ok "Server co IP cong khai $lanIp, khong can mo cong router." }

# ---------------------------------------------------------------- 6. Deploy folder
Step "6. Tao $DeployRoot"
foreach ($d in 'incoming', 'logs', 'staging', 'backups') { New-Item -ItemType Directory -Force -Path (Join-Path $DeployRoot $d) | Out-Null }
Copy-Item (Join-Path $Here 'Apply-TARelease.ps1') (Join-Path $DeployRoot 'Apply-TARelease.ps1') -Force
Unblock-File (Join-Path $DeployRoot 'Apply-TARelease.ps1')
[ordered]@{
    BackupRoot = (Join-Path $DeployRoot 'backups')
    Incoming   = (Join-Path $DeployRoot 'incoming')
    Targets    = $targets
} | ConvertTo-Json -Depth 5 | Set-Content -Path $configPath -Encoding UTF8
Ok "Da ghi $configPath"

# ---------------------------------------------------------------- 7. Summary
Step '7. XONG - gui thong tin duoi day cho Claude'
$ips = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' }).IPAddress -join ', '
$keygen = Join-Path $env:SystemRoot 'System32\OpenSSH\ssh-keygen.exe'
if (-not (Test-Path $keygen)) { $keygen = Join-Path $env:ProgramFiles 'OpenSSH\ssh-keygen.exe' }
$fp = & $keygen -lf (Join-Path $env:ProgramData 'ssh\ssh_host_ed25519_key.pub')
$sql = (Get-Service | Where-Object { $_.Name -like 'MSSQL*' } | ForEach-Object { "$($_.Name)=$($_.Status)" }) -join ', '
$summary = @"
T.A DEPLOY SERVER
  Server name : $env:COMPUTERNAME
  IP noi bo   : $ips
  SSH port    : $port (cong ngoai: $externalPort)
  SSH user    : $env:USERNAME
  Host key    : $fp
  Hosting 8   : ANCM=$ancm AspNetCore8=$aspnet8
  SQL Server  : $sql
  Targets     :
$(($targets.GetEnumerator() | ForEach-Object { "    - $($_.Key): site=$($_.Value.SiteName) | folder=$($_.Value.SitePath) | pool=$($_.Value.AppPool) | dll=$($_.Value.MainDll) | health=$($_.Value.HealthUrl)" }) -join "`r`n")
"@
Write-Host $summary -ForegroundColor Green
$summaryFile = Join-Path $DeployRoot 'server-info.txt'
Set-Content -Path $summaryFile -Value $summary -Encoding UTF8
Write-Host "Da luu vao $summaryFile. Them IP public / ten mien cua server neu IP tren la IP noi bo."
}
catch {
    Log "ERROR $($_.Exception.Message)"
    Write-Host ''
    Write-Host "THAT BAI: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Log: $LogFile"
    exit 1
}
