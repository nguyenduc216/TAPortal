<#
.SYNOPSIS
  Read-only query against the production database (TAPortalSys) over SSH (alias ta-server) with sqlcmd and Windows
  authentication on the server, as AGENTS.md "## OPERATOR STANDING AUTHORIZATION (2026-11-05)" allows.

.DESCRIPTION
  Read-only by two locks:
    1. the text is refused when it contains a write/DDL/exec keyword (INSERT, UPDATE, DELETE, MERGE, DROP, ALTER, CREATE,
       TRUNCATE, EXEC, GRANT, REVOKE, DENY, BACKUP, RESTORE, DBCC, SELECT ... INTO, OPENROWSET, xp_/sp_ calls, ...);
    2. it runs inside BEGIN TRAN ... ROLLBACK with SET LOCK_TIMEOUT 5000 and DEADLOCK_PRIORITY LOW, so even a missed
       write is undone and the query never waits on production locks for long.
  The result is printed only (never written to the repository or .codec): it may contain customer data.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File deploy/windows/Invoke-TAReadQuery.ps1 -Sql "SELECT Status, COUNT(*) FROM dbo.PaymentTransactions GROUP BY Status"
#>
param(
    [string]$Sql,
    [string]$File,
    [string]$SshHost = 'ta-server',
    [string]$Instance = '.\MSSQLSERVER2014',
    [string]$Database = 'TAPortalSys'
)
$ErrorActionPreference = 'Stop'
if ($File) { $Sql = [IO.File]::ReadAllText((Resolve-Path $File)) }
if (-not $Sql -or -not $Sql.Trim()) { throw 'Pass -Sql or -File.' }
if ($Database -notmatch '^\w+$') { throw 'Bad database name.' }

# Comments and string literals cannot hide a keyword from the check: strip them first, then look at the words.
$code = [regex]::Replace($Sql, "--[^\r\n]*|/\*[\s\S]*?\*/|N?'(?:[^']|'')*'", ' ')
$blocked = '\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|GRANT|REVOKE|DENY|BACKUP|RESTORE|DBCC|KILL|SHUTDOWN|RECONFIGURE|INTO|OPENROWSET|OPENDATASOURCE|OPENQUERY|BULK|COMMIT|SAVE|ROLLBACK|BEGIN|USE|GO|SETUSER|WAITFOR|xp_\w+|sp_\w+)\b'
$hit = [regex]::Match($code, $blocked, 'IgnoreCase')
if ($hit.Success) { throw "Read-only: '$($hit.Value)' is not allowed." }
if ($code -match '^\s*:|\n\s*:') { throw 'Read-only: sqlcmd commands are not allowed.' }

$Ssh = 'C:\Program Files\Git\usr\bin\ssh.exe'
$Scp = 'C:\Program Files\Git\usr\bin\scp.exe'
if (-not (Test-Path $Ssh)) { $Ssh = Join-Path $env:windir 'System32\OpenSSH\ssh.exe'; $Scp = Join-Path $env:windir 'System32\OpenSSH\scp.exe' }

$name = "readquery-$([guid]::NewGuid().ToString('N')).sql"
$remote = "C:\TADeploy\queries"
$text = @"
SET NOCOUNT ON;
SET LOCK_TIMEOUT 5000;
SET DEADLOCK_PRIORITY LOW;
SET XACT_ABORT ON;
BEGIN TRAN;
$Sql
;
IF @@TRANCOUNT > 0 ROLLBACK;
"@
$tmp = Join-Path ([IO.Path]::GetTempPath()) $name
[IO.File]::WriteAllText($tmp, $text, (New-Object Text.UTF8Encoding($false)))
try {
    & $Ssh $SshHost "New-Item -ItemType Directory -Force '$remote' | Out-Null"
    & $Scp -q $tmp "${SshHost}:$($remote.Replace('\', '/'))/$name"
    if ($LASTEXITCODE -ne 0) { throw 'upload failed' }
    $ErrorActionPreference = 'Continue'
    & $Ssh $SshHost "sqlcmd -S $Instance -E -d $Database -b -I -W -s '|' -f i:65001 -i $remote\$name; `$c = `$LASTEXITCODE; Remove-Item $remote\$name; exit `$c" 2>&1 | Out-String | Write-Output
    $code = $LASTEXITCODE
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
exit $code
