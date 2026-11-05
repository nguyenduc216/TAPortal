<#
.SYNOPSIS
  Runs migrations on the production database exactly as AGENTS.md "## AGENT MIGRATION EXECUTION" requires, over SSH
  (alias ta-server) with sqlcmd and Windows authentication on the server (no connection string is copied or printed).

.DESCRIPTION
  For each "NNN:VVV:RRR" in -Plan (migration database/migrations/NNN-*.sql, verify database/scripts/VVV-*.sql, rollback
  database/scripts/RRR-*.sql), in order:
    1. pre-check (verify): PASS ⇒ ALREADY_APPLIED (skipped); anything but NOT_APPLIED ⇒ stop;
    2. dry-run in ONE transaction: migration → verify (PASS) → rollback → verify (NOT_APPLIED) → ROLLBACK, with an
       @@TRANCOUNT guard after every batch, sqlcmd -b; then @@TRANCOUNT = 0 and the pre-check again (unchanged);
    3. real run: migration → verify (PASS) ⇒ EXECUTED; otherwise rollback → verify ⇒ ROLLED_BACK and stop.
  Before the first real run: latest full backup of TAPortalSys within 24 hours, otherwise BACKUP ... COPY_ONLY, CHECKSUM
  into the instance backup folder + RESTORE VERIFYONLY; and the gates (restore, Release build, Safe tests) unless
  -SkipGates. Output is text only (PRINT); the log goes to .codec/reports/<ChangeId>-migrations-<UTC>.log.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File deploy/windows/Invoke-TAMigrations.ps1 -Plan "019:027:028","020:029:030" -ChangeId TINGEE-INTEGRATION-CHG-20261105T042908Z-94446D
#>
param(
    [Parameter(Mandatory)][string[]]$Plan,
    [Parameter(Mandatory)][string]$ChangeId,
    [string]$SshHost = 'ta-server',
    [string]$Instance = '.\MSSQLSERVER2014',
    [switch]$DryRunOnly,
    [switch]$SkipGates
)
$ErrorActionPreference = 'Stop'
# powershell -File passes "a","b" as one string: accept commas inside items too.
$Plan = @($Plan | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$log = Join-Path $root ".codec\reports\$ChangeId-migrations-$stamp.log"
$remote = "C:\TADeploy\migrations\$ChangeId-$stamp"

function Log([string]$text) { $line = "$((Get-Date).ToUniversalTime().ToString('u')) $text"; Write-Host $line; Add-Content -Path $log -Value $line -Encoding utf8 }
function Fail([string]$text) { Log "STOP: $text"; exit 1 }
# Remote commands use no double quotes (Windows PowerShell 5.1 mangles them for native exes): every SQL text is a file.
function Upload([string]$name, [string]$text) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) $name
    [IO.File]::WriteAllText($tmp, $text, (New-Object Text.UTF8Encoding($false)))
    & scp -q $tmp "${SshHost}:$($remote.Replace('\', '/'))/$name"
    $code = $LASTEXITCODE
    Remove-Item $tmp
    if ($code -ne 0) { Fail "upload of $name failed" }
}
function Sql([string]$file, [switch]$Plain) {
    $format = if ($Plain) { ' -h -1 -W' } else { '' }
    $out = & ssh $SshHost "sqlcmd -S $Instance -E -d TAPortalSys -b -I$format -i $remote\$file; exit `$LASTEXITCODE" 2>&1 | Out-String
    $code = $LASTEXITCODE
    Add-Content -Path $log -Value $out -Encoding utf8
    return [pscustomobject]@{ Code = $code; Out = $out }
}
$script:queries = 0
function Query([string]$sql) {
    $script:queries++
    $name = "query-$($script:queries).sql"
    Upload $name "SET NOCOUNT ON;`r`n$sql"
    $r = Sql $name -Plain
    if ($r.Code -ne 0) { Fail "query failed: $name" }
    return $r.Out.Trim()
}
function Status([string]$out) { if ($out -match 'VERIFY_STATUS=(\w+)') { $Matches[1] } else { 'UNKNOWN' } }

New-Item -ItemType File -Force -Path $log | Out-Null
Log "Change $ChangeId plan $($Plan -join ', ') host $SshHost instance $Instance remote $remote dryRunOnly=$DryRunOnly"
Log "HEAD $(git -C $root rev-parse HEAD)"

# Files
$items = foreach ($p in $Plan) {
    $n = $p.Split(':'); if ($n.Count -ne 3) { Fail "bad plan item $p (NNN:VVV:RRR)" }
    $m = Get-ChildItem (Join-Path $root 'database\migrations') -Filter "$($n[0])-*.sql" | Select-Object -First 1
    $v = Get-ChildItem (Join-Path $root 'database\scripts') -Filter "$($n[1])-verify-*.sql" | Select-Object -First 1
    $r = Get-ChildItem (Join-Path $root 'database\scripts') -Filter "$($n[2])-rollback-*.sql" | Select-Object -First 1
    if (-not ($m -and $v -and $r)) { Fail "missing migration/verify/rollback for $p" }
    [pscustomobject]@{ Migration = $m; Verify = $v; Rollback = $r }
}
& ssh $SshHost "New-Item -ItemType Directory -Force '$remote' | Out-Null"
if ($LASTEXITCODE -ne 0) { Fail "cannot create $remote" }
$files = $items | ForEach-Object { $_.Migration.FullName; $_.Verify.FullName; $_.Rollback.FullName }
& scp -q @files "${SshHost}:$($remote.Replace('\', '/'))/"
if ($LASTEXITCODE -ne 0) { Fail "upload failed" }

$checkedGates = $false
foreach ($it in $items) {
    $M = $it.Migration.Name; $V = $it.Verify.Name; $R = $it.Rollback.Name
    Log "=== $M"
    # 1. pre-check
    $pre = Sql $V
    $preStatus = Status $pre.Out
    Log "PRECHECK $V exit=$($pre.Code) VERIFY_STATUS=$preStatus"
    if ($pre.Code -ne 0) { Fail "pre-check failed" }
    if ($preStatus -eq 'PASS') { Log "RESULT $M ALREADY_APPLIED"; continue }
    if ($preStatus -ne 'NOT_APPLIED') { Fail "pre-check is $preStatus (partially applied?) - operator decision needed" }

    # 2. dry-run with rollback, one transaction
    $guard = "IF @@TRANCOUNT = 0 RAISERROR('ABORT: transaction lost', 16, 1);"
    $wrapper = @"
SET XACT_ABORT ON;
BEGIN TRAN;
GO
:r $remote\$M
GO
$guard
GO
PRINT N'--- VERIFY AFTER MIGRATION';
GO
:r $remote\$V
GO
$guard
GO
:r $remote\$R
GO
$guard
GO
PRINT N'--- VERIFY AFTER ROLLBACK';
GO
:r $remote\$V
GO
$guard
GO
ROLLBACK;
PRINT N'DRYRUN_TRANCOUNT=' + CAST(@@TRANCOUNT AS nvarchar(5));
GO
"@
    $dryName = "dryrun-$($M)"
    Upload $dryName $wrapper
    $dry = Sql $dryName
    $parts = $dry.Out -split '--- VERIFY AFTER ROLLBACK'
    $afterMigration = if ($parts.Count -ge 1) { Status (($parts[0] -split '--- VERIFY AFTER MIGRATION')[-1]) } else { 'UNKNOWN' }
    $afterRollback = if ($parts.Count -ge 2) { Status $parts[1] } else { 'UNKNOWN' }
    $tranOk = $dry.Out -match 'DRYRUN_TRANCOUNT=0'
    Log "DRYRUN exit=$($dry.Code) afterMigration=$afterMigration afterRollback=$afterRollback trancount0=$tranOk"
    $again = Status (Sql $V).Out
    Log "PRECHECK AGAIN VERIFY_STATUS=$again"
    if ($dry.Code -ne 0 -or $afterMigration -ne 'PASS' -or $afterRollback -ne 'NOT_APPLIED' -or -not $tranOk -or $again -ne 'NOT_APPLIED') {
        Log "RESULT $M DRY_RUN_FAILED"; Fail "dry-run failed"
    }
    if ($DryRunOnly) { Log "RESULT $M DRY_RUN_PASSED (no real run: -DryRunOnly)"; continue }

    # Backup and gates, once
    if (-not $checkedGates) {
        $age = Query "SELECT ISNULL(CAST(DATEDIFF(minute, MAX(backup_finish_date), GETDATE()) AS varchar(10)), 'NONE') FROM msdb.dbo.backupset WHERE database_name = 'TAPortalSys' AND type = 'D'"
        Log "BACKUP latest full age (minutes): $age"
        if ($age -eq 'NONE' -or [int]$age -gt 1440) {
            $dir = Query "DECLARE @d nvarchar(4000); EXEC master.dbo.xp_instance_regread N'HKEY_LOCAL_MACHINE', N'Software\Microsoft\MSSQLServer\MSSQLServer', N'BackupDirectory', @d OUTPUT; SELECT @d"
            $bak = "$dir\TAPortalSys-predeploy-$stamp.bak"
            Query "BACKUP DATABASE [TAPortalSys] TO DISK = N'$bak' WITH COPY_ONLY, CHECKSUM, INIT; RESTORE VERIFYONLY FROM DISK = N'$bak' WITH CHECKSUM; SELECT 'BACKUP_OK'" | Out-Null
            Log "BACKUP taken and verified: $bak"
        }
        if (-not $SkipGates) {
            Push-Location $root
            try {
                dotnet restore TAPortal.sln | Out-Null; if ($LASTEXITCODE -ne 0) { Fail "gate: restore" }
                dotnet build TAPortal.sln -c Release --no-restore | Out-Null; if ($LASTEXITCODE -ne 0) { Fail "gate: build" }
                $t = dotnet test tests/TACustomerPortal.Tests -c Release --no-build --filter "FullyQualifiedName~TACustomerPortal.Tests.Safe" | Out-String
                if ($LASTEXITCODE -ne 0) { Fail "gate: Safe tests" }
                Log "GATES restore/build/Safe tests: PASS $((($t -split "`n") | Where-Object { $_ -match 'Passed!|Failed!' }) -join ' ')"
            } finally { Pop-Location }
        }
        $checkedGates = $true
    }

    # 3. real run
    $run = Sql $M
    $post = Sql $V
    $postStatus = Status $post.Out
    Log "REAL RUN exit=$($run.Code) VERIFY_STATUS=$postStatus"
    if ($run.Code -ne 0 -or $postStatus -ne 'PASS') {
        $rb = Sql $R
        $rbStatus = Status (Sql $V).Out
        Log "ROLLBACK exit=$($rb.Code) VERIFY_STATUS=$rbStatus"
        Log "RESULT $M ROLLED_BACK"; Fail "real run failed"
    }
    Log "RESULT $M EXECUTED"
}
Log "DONE log=$log"
