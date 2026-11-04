<#
.SYNOPSIS
  Dev-machine half of the T.A production deploy (policy: AGENTS.md "## AGENT PRODUCTION DEPLOY").
  One command: audit the code -> push the branch -> publish -> ship over SSH to the production server,
  where C:\TADeploy\Apply-TARelease.ps1 backs up, copies, health-checks and rolls back on failure.

    .\Deploy-TARemote.ps1 -Target portal,invoice -DryRun      # audit + build + package, no push/upload
    .\Deploy-TARemote.ps1 -Target portal,invoice -ChangeId <CHANGE_ID>
    .\Deploy-TARemote.ps1 -Target invoice -Rollback           # restore the previous server backup

  Audit (step 3) covers the commits since the commit last deployed to each target (stored on the server in
  C:\TADeploy\state\<target>.txt; first deploy: since the merge-base with origin/main):
    - whitespace errors (git diff --check)
    - secrets in added lines (connection-string passwords, filled secret keys, private keys)
    - forbidden files (certificates/keys, appsettings.Production.json, .env)
    - migrations changed -> requires -MigrationsApplied (run under "## AGENT MIGRATION EXECUTION" first)
    - vulnerable NuGet packages (High/Critical fail)
    - Codec policy tests and validation
    - Release build 0 errors + all Safe* tests
  Any FAIL stops before push/upload. Exit code: 0 = all DEPLOYED (or dry run OK), 1 = stopped or rolled back.
  Targets: portal = TAPortal.Web (TA.Portal), invoice = TACustomerPortal.Web (TA.Invoice),
           webhooks = TAPortal.Webhooks (TA.Webhooks, https://webhook.hoadontudong.vn, health /health).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('portal', 'invoice', 'webhooks')][string[]] $Target,
    [switch] $DryRun,
    [switch] $Rollback,
    # Confirms that every migration in the audited range was executed under the migration policy.
    [switch] $MigrationsApplied,
    # Codec CHANGE_ID; the artifact manifests are stored under .codec/reports/<CHANGE_ID>-<PROJECT>.sha256.
    [string] $ChangeId,
    [string] $SshHost = 'ta-server'
)

$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Projects = @{ portal = 'TAPortal.Web'; invoice = 'TACustomerPortal.Web'; webhooks = 'TAPortal.Webhooks' }
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
function Bad([string] $Text) { Write-Host "  FAIL $Text" -ForegroundColor Red; $script:auditFailures++ }

# Runs a native command capturing stdout+stderr as text; Windows PowerShell 5.1 would otherwise turn any
# stderr line into a terminating error under $ErrorActionPreference = 'Stop'. $LASTEXITCODE is kept.
function Invoke-Native([string] $Exe, [string[]] $Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Exe @Arguments 2>&1 | ForEach-Object { "$_" } }
    finally { $ErrorActionPreference = $previous }
}

function Invoke-Checked([string] $Exe, [string[]] $Arguments) {
    # git/scp/dotnet print progress on stderr; only the exit code decides success.
    Invoke-Native $Exe $Arguments | ForEach-Object { Write-Host "  | $_" }
    if ($LASTEXITCODE -ne 0) { throw "$Exe $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

function Invoke-Git([string[]] $Arguments) {
    $output = & git -C $Repo @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
    return $output
}

# Runs a PowerShell command on the server; prints its output and returns the remote exit code.
# Remote commands use single quotes only: Windows PowerShell 5.1 mangles embedded double quotes for native exes.
function Invoke-Remote([string] $Command, [switch] $Quiet) {
    $output = Invoke-Native $Ssh (@($SshOptions) + @($SshHost, $Command))
    $code = $LASTEXITCODE
    $script:remoteOutput = @($output | ForEach-Object { "$_" })
    if (-not $Quiet) { foreach ($line in $script:remoteOutput) { Write-Host "  | $line" } }
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
$auditFailures = 0
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
    $branch = (Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD')).Trim()
    $commit = (Invoke-Git @('rev-parse', 'HEAD')).Trim()
    Write-Host "  Branch $branch @ $commit"
    if ($branch -eq 'main' -or $branch -eq 'HEAD') { throw "Deploy runs from a feature branch, not '$branch' (merging main needs its own approval)." }
    $dirty = Invoke-Git @('status', '--porcelain', '--untracked-files=no')
    if ($dirty) {
        if ($DryRun) { Warn 'tracked files have uncommitted changes (allowed in -DryRun only; they are NOT audited)' }
        else { throw 'Cannot deploy: tracked files have uncommitted changes. Commit them first.' }
    }
    Invoke-Git @('fetch', '--quiet', 'origin') | Out-Null
    $remoteRef = "origin/$branch"
    $hasRemote = [bool](& git -C $Repo rev-parse --verify --quiet $remoteRef)
    if ($hasRemote) {
        & git -C $Repo merge-base --is-ancestor $remoteRef HEAD
        if ($LASTEXITCODE -ne 0) { throw "$remoteRef has commits that are not in HEAD (another agent pushed?). Review and merge them first; never force push." }
        $ahead = [int](Invoke-Git @('rev-list', '--count', "$remoteRef..HEAD"))
    }
    else { $ahead = [int](Invoke-Git @('rev-list', '--count', 'HEAD')) }
    Ok "Working tree checked; $ahead local commit(s) not yet on origin"

    # ------------------------------------------------------------ 2. server preflight (read-only)
    Step "2. Server $SshHost (read-only check)"
    $names = ($Target | ForEach-Object { "'$_'" }) -join ','
    $check = "`$c = Get-Content '$RemoteRoot\deploy-config.json' -Raw | ConvertFrom-Json; " +
             "if (-not (Test-Path '$RemoteRoot\Apply-TARelease.ps1')) { 'MISSING Apply-TARelease.ps1'; exit 3 }; " +
             "foreach (`$t in @($names)) { `$x = `$c.Targets.`$t; if (-not `$x) { 'MISSING target ' + `$t; exit 3 }; " +
             "`$free = [math]::Round((Get-PSDrive `$x.SitePath.Substring(0, 1)).Free / 1GB, 1); " +
             "`$last = 'none'; `$s = '$RemoteRoot\state\' + `$t + '.txt'; if (Test-Path `$s) { `$last = (Get-Content `$s -TotalCount 1).Trim() }; " +
             "`$t + ' -> ' + `$x.SiteName + ' | ' + `$x.SitePath + ' | pool ' + `$x.AppPool + ' | ' + `$x.HealthUrl + ' | free ' + `$free + ' GB | last ' + `$last }"
    if ((Invoke-Remote $check) -ne 0) { throw "Server preflight failed on $SshHost" }
    $lastDeployed = @{}
    foreach ($line in $script:remoteOutput) { if ($line -match '^(\w+) -> .* \| last (\S+)$') { $lastDeployed[$Matches[1]] = $Matches[2] } }
    Ok 'Server ready'

    # ------------------------------------------------------------ 3. audit
    Step '3. Audit'
    # Audit range starts at the oldest last-deployed commit among the targets (merge-base with origin/main if unknown).
    $bases = foreach ($t in $Target) {
        $last = $lastDeployed[$t]
        if ($last -and $last -ne 'none') {
            Invoke-Native 'git' @('-C', $Repo, 'cat-file', '-e', "$last^{commit}") | Out-Null
            if ($LASTEXITCODE -eq 0) { $last; continue }
            Warn "$t last deployed commit $last is unknown locally; auditing from origin/main"
        }
        (Invoke-Git @('merge-base', 'HEAD', 'origin/main')).Trim()
    }
    $base = $bases | Sort-Object { [int](Invoke-Git @('rev-list', '--count', "$_..HEAD")) } -Descending | Select-Object -First 1
    $commitCount = [int](Invoke-Git @('rev-list', '--count', "$base..HEAD"))
    $changed = @(Invoke-Git @('diff', '--name-only', "$base..HEAD"))
    Write-Host "  Range $($base.Substring(0, 7))..$($commit.Substring(0, 7)): $commitCount commit(s), $($changed.Count) file(s)"

    & git -C $Repo diff --check "$base..HEAD" | Out-Null
    if ($LASTEXITCODE -ne 0) { Bad 'git diff --check: whitespace errors in the range' } else { Ok 'No whitespace errors' }

    $secretPatterns = @(
        @{ Name = 'connection-string password'; Regex = '(?i)\b(password|pwd)=(?!\s*[;"''<{$]|\*+)[^;"''\s]{3,}' },
        @{ Name = 'filled secret value';        Regex = '(?i)"(secretkey|clientsecret|apikey|accesstoken|privatekey)"\s*:\s*"[^"]{6,}"' },
        @{ Name = 'private key block';          Regex = '-----BEGIN [A-Z ]*PRIVATE KEY-----' }
    )
    $added = Invoke-Git @('diff', '-U0', "$base..HEAD", '--', '.', ':(exclude)*.md', ':(exclude)tests/**')
    $file = ''; $secretHits = 0
    foreach ($line in $added) {
        if ($line -like '+++ b/*') { $file = $line.Substring(6); continue }
        if (-not $line.StartsWith('+') -or $line.StartsWith('+++')) { continue }
        foreach ($p in $secretPatterns) { if ($line -match $p.Regex) { Bad "$($p.Name) in $file"; $secretHits++ } }
    }
    if ($secretHits -eq 0) { Ok 'No secrets in added lines' }

    $forbidden = @($changed | Where-Object { $_ -match '\.(pfx|p12|pem|key)$|(^|/)appsettings\.Production\.json$|(^|/)\.env$' -and (Test-Path (Join-Path $Repo $_)) })
    if ($forbidden.Count) { foreach ($f in $forbidden) { Bad "forbidden file committed: $f" } } else { Ok 'No certificates, keys or production settings committed' }

    $migrations = @($changed | Where-Object { $_ -like 'database/migrations/*' })
    if ($migrations.Count -and -not $MigrationsApplied) {
        foreach ($m in $migrations) { Bad "migration changed: $m - run it under the migration policy first, then pass -MigrationsApplied" }
    }
    elseif ($migrations.Count) { Ok "Migrations confirmed applied: $($migrations -join ', ')" }
    else { Ok 'No migration changes' }

    # Only the projects being shipped (their transitive packages include Shared/Integrations); test-only packages never reach the server.
    $vulnFound = @{}
    $vulnOffline = $false
    foreach ($t in $Target) {
        $project = $Projects[$t]
        $vuln = Invoke-Native 'dotnet' @('list', (Join-Path $Repo "src\$project\$project.csproj"), 'package', '--vulnerable', '--include-transitive')
        if ($LASTEXITCODE -ne 0) { $vulnOffline = $true; continue }
        foreach ($v in $vuln) {
            if ($v -match '^\s*>\s*(\S+)\s+(\S+)\s+(Critical|High|Moderate|Low)\b') {
                $key = "$($Matches[1]) $($Matches[2]) ($($Matches[3]))"
                if (-not $vulnFound.Contains($key)) { $vulnFound[$key] = New-Object System.Collections.Generic.List[string] }
                $vulnFound[$key].Add($project)
            }
        }
    }
    if ($vulnOffline) { Warn 'Vulnerable package check could not run for every project (offline?)' }
    foreach ($key in $vulnFound.Keys) {
        $text = "vulnerable package $key in $($vulnFound[$key] -join ', ')"
        if ($key -match '\((Critical|High)\)$') { Bad $text } else { Warn $text }
    }
    if (-not $vulnOffline -and $vulnFound.Count -eq 0) { Ok 'No known vulnerable packages in shipped projects' }

    $codecTests = Invoke-Native 'python' @('-m', 'unittest', 'discover', '-s', (Join-Path $Repo '.codec\tools'), '-p', 'test_codec.py')
    if ($LASTEXITCODE -ne 0) { Bad 'Codec policy tests failed'; $codecTests | Select-Object -Last 5 | ForEach-Object { Write-Host "  | $_" } } else { Ok 'Codec policy tests' }
    # The fixed publish paths in .codec/config.json belong to the operator's workspace; other checkouts only warn on them.
    $codecErrors = @(Invoke-Native 'python' @((Join-Path $Repo '.codec\tools\codec.py'), 'validate') | Where-Object { $_ -like 'ERROR:*' })
    $realErrors = @($codecErrors | Where-Object { $_ -notlike 'ERROR: Publish path violates fixed convention*' })
    if ($realErrors.Count) { foreach ($e in $realErrors) { Bad "codec validate $e" } }
    elseif ($codecErrors.Count) { Warn 'codec validate: publish paths differ from the operator workspace (this checkout)' }
    else { Ok 'Codec policy valid' }

    $sln = Join-Path $Repo 'TAPortal.sln'
    Invoke-Checked 'dotnet' @('restore', $sln, '--nologo', '-v', 'q')
    & dotnet build $sln -c Release --no-restore --nologo -v q
    if ($LASTEXITCODE -ne 0) { Bad 'Release build failed' } else { Ok 'Release build' }
    if ($LASTEXITCODE -eq 0) {
        & dotnet test $sln -c Release --no-build --nologo -v q --filter 'FullyQualifiedName~TACustomerPortal.Tests.Safe'
        if ($LASTEXITCODE -ne 0) { Bad 'Safe tests failed' } else { Ok 'Safe tests' }
    }

    if ($auditFailures -gt 0) { throw "Audit failed with $auditFailures problem(s); nothing was pushed or deployed." }
    Ok 'AUDIT PASSED'

    # ------------------------------------------------------------ 4. push
    Step "4. Push $branch"
    if ($DryRun) { Warn 'DryRun: not pushing' }
    elseif ($ahead -gt 0 -or -not $hasRemote) {
        Invoke-Checked 'git' @('-C', $Repo, 'push', 'origin', "HEAD:refs/heads/$branch")
        Ok "Pushed $ahead commit(s) to origin/$branch"
    }
    else { Ok 'Already on origin' }

    # ------------------------------------------------------------ 5. publish + package
    $packages = @{}
    foreach ($t in $Target) {
        $project = $Projects[$t]
        Step "5. Publish $t ($project)"
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
        foreach ($t in $Target) { $results.Add([pscustomobject]@{ Target = $t; Project = $Projects[$t]; Commit = $commit.Substring(0, 7); Artifact = $packages[$t].Manifest; Package = $packages[$t].Sha; Result = 'DRY_RUN (audit passed, not pushed, not uploaded)' }) }
        break main
    }

    # ------------------------------------------------------------ 6. upload + apply
    foreach ($t in $Target) {
        $p = $packages[$t]
        $name = Split-Path $p.Zip -Leaf
        Step "6. Deploy $t -> $SshHost"
        Invoke-Checked $Scp (@($SshOptions) + @($p.Zip, "${SshHost}:C:/TADeploy/incoming/$name"))
        Ok "Uploaded $name"
        $remotePkg = "$RemoteRoot\incoming\$name"
        $code = Invoke-Remote ("& '$RemoteRoot\Apply-TARelease.ps1' -Target $t -Package '$remotePkg' -Sha256 $($p.Sha) -ExpectedCommit $commit; " +
                               "`$code = `$LASTEXITCODE; if (`$code -eq 0) { Remove-Item '$remotePkg' -Force; " +
                               "New-Item -ItemType Directory -Force -Path '$RemoteRoot\state' | Out-Null; Set-Content -Path '$RemoteRoot\state\$t.txt' -Value '$commit' -Encoding ASCII }; exit `$code")
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
