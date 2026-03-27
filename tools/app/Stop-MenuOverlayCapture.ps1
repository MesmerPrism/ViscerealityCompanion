$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$latestPath = Join-Path $repoRoot 'artifacts\verify\menu-overlay-capture\latest-session.json'

if (-not (Test-Path $latestPath)) {
    throw "No latest capture metadata found at $latestPath."
}

$metadata = Get-Content $latestPath -Raw | ConvertFrom-Json
if (-not [string]::IsNullOrWhiteSpace($metadata.stopFile)) {
    New-Item -ItemType File -Force -Path $metadata.stopFile | Out-Null
}

Start-Sleep -Seconds 2

foreach ($processId in @($metadata.lslPid, $metadata.adbPid, $metadata.logcatPid)) {
    if ($null -eq $processId) {
        continue
    }

    try {
        $process = Get-Process -Id $processId -ErrorAction Stop
        Stop-Process -Id $process.Id -Force
    }
    catch {
    }
}

Write-Output "Stopped capture session in $($metadata.sessionDir)"
