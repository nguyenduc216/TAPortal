# Generates the fixed AES-256 key that TAPortal.Web uses to encrypt customer system-account passwords/PINs
# (configuration CustomerSecrets:Key, environment variable CustomerSecrets__Key).
#
# Run ONCE. Then:
#   1. Set the printed value as CustomerSecrets__Key for the TAPortal.Web site on the server.
#   2. Keep one copy outside the server (password manager or sealed paper copy).
# Never commit the value to Git and never generate a second key while passwords are stored: data saved under one key
# cannot be read with another. Running this script does not change any server or file.

$ErrorActionPreference = "Stop"

$bytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }

Write-Host ""
Write-Host "CustomerSecrets__Key ="
Write-Host ([Convert]::ToBase64String($bytes))
Write-Host ""
Write-Host "Copy this value now into the server configuration and into your offline copy. It is not saved anywhere."
