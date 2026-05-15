[CmdletBinding()]
param(
    [string]$ProjectPath = "",
    [string]$PublishPath = "C:\Copilot\Focus L-AIci\publish\local",
    [string]$ProbeUrl = "http://127.0.0.1:5187/",
    [string]$McpProbeUrl = "http://127.0.0.1:5187/api/mcp"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $PSScriptRoot "..\FocusLAIci.Web\FocusLAIci.Web.csproj"
}

$projectFullPath = [System.IO.Path]::GetFullPath($ProjectPath)
$publishFullPath = [System.IO.Path]::GetFullPath($PublishPath)
$appOfflinePath = Join-Path $publishFullPath "app_offline.htm"

if (-not (Test-Path $projectFullPath)) {
    throw "Project not found: $projectFullPath"
}

New-Item -ItemType Directory -Path $publishFullPath -Force | Out-Null
Set-Content -Path $appOfflinePath -Value "<html><body>Focus L-AIci is publishing.</body></html>"

try {
    Get-Process w3wp -ErrorAction SilentlyContinue | ForEach-Object {
        Stop-Process -Id $_.Id -Force
    }

    dotnet publish $projectFullPath -c Release -o $publishFullPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}
finally {
    if (Test-Path $appOfflinePath) {
        Remove-Item $appOfflinePath -Force
    }
}

$homeResponse = Invoke-WebRequest -Uri $ProbeUrl -UseBasicParsing -TimeoutSec 30
if ($homeResponse.StatusCode -ne 200) {
    throw "Unexpected home probe status: $($homeResponse.StatusCode)"
}

$mcpResponse = Invoke-WebRequest -Uri $McpProbeUrl -Method Post -ContentType "application/json" -Body '{"jsonrpc":"2.0","id":"self-test","method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"publish-script","version":"1.0"}}}' -UseBasicParsing -TimeoutSec 30
if ($mcpResponse.StatusCode -lt 200 -or $mcpResponse.StatusCode -ge 300) {
    throw "Unexpected MCP probe status: $($mcpResponse.StatusCode)"
}

Write-Host "Published Focus L-AIci to $publishFullPath"
Write-Host "Home probe: $ProbeUrl"
Write-Host "MCP probe: $McpProbeUrl"
