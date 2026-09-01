param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "C:\TAPortal\Mcp",
    [string]$ListenUrl = "http://127.0.0.1:5100"
)

$ErrorActionPreference = "Stop"

Write-Host "Publishing TAPortal.DbMcp..."
dotnet restore "$PSScriptRoot\..\..\TAPortal.sln"
dotnet publish "$PSScriptRoot\..\..\src\TAPortal.DbMcp\TAPortal.DbMcp.csproj" `
    -c $Configuration `
    -o $OutputPath `
    /p:UseAppHost=false

Write-Host ""
Write-Host "Publish completed: $OutputPath"
Write-Host ""
Write-Host "Before starting the service, configure this machine-level or process-level environment variable:"
Write-Host "Database__ConnectionString"
Write-Host ""
Write-Host "Manual test command:"
Write-Host "dotnet `"$OutputPath\TAPortal.DbMcp.dll`" --urls $ListenUrl"
Write-Host ""
Write-Host "Health check: $ListenUrl/health"
Write-Host "MCP endpoint:  $ListenUrl/mcp"
Write-Host ""
Write-Host "For production, reverse-proxy HTTPS from mcp.<domain> to $ListenUrl and do not expose the internal port directly."
