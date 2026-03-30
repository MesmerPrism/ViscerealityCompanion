<#
.SYNOPSIS
    Refreshes the bundled Sussex APK mirror and approved hashes from an Astral build output.
#>
[CmdletBinding()]
param(
    [string]$SourceApkPath,
    [string]$VersionName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Sha256Hex {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($stream)
        return ([System.BitConverter]::ToString($hashBytes)).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$defaultAstralRepoRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot '..\AstralKarateDojo'))
$resolvedSourceApkPath = if ([string]::IsNullOrWhiteSpace($SourceApkPath)) {
    [System.IO.Path]::GetFullPath((Join-Path $defaultAstralRepoRoot 'Artifacts\APKs\SussexExperiment.apk'))
} else {
    [System.IO.Path]::GetFullPath($SourceApkPath)
}

if (-not (Test-Path $resolvedSourceApkPath)) {
    throw "Source Sussex APK not found at $resolvedSourceApkPath"
}

$bundledApkPath = Join-Path $repoRoot 'samples\quest-session-kit\APKs\SussexExperiment.apk'
$compatibilityPath = Join-Path $repoRoot 'samples\quest-session-kit\APKs\compatibility.json'
$studyShellPath = Join-Path $repoRoot 'samples\study-shells\sussex-university.json'

Copy-Item -LiteralPath $resolvedSourceApkPath -Destination $bundledApkPath -Force
$sha256 = Get-Sha256Hex -Path $bundledApkPath

$compatibility = Get-Content -LiteralPath $compatibilityPath -Raw | ConvertFrom-Json
if ($null -eq $compatibility.apps -or @($compatibility.apps).Count -lt 1) {
    throw "No compatibility app entries were found in $compatibilityPath"
}

$compatibility.apps[0].sha256 = $sha256
$compatibility | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $compatibilityPath -Encoding utf8

$studyShell = Get-Content -LiteralPath $studyShellPath -Raw | ConvertFrom-Json
$studyShell.app.sha256 = $sha256
if (-not [string]::IsNullOrWhiteSpace($VersionName)) {
    $studyShell.app.versionName = $VersionName
}

$studyShell | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $studyShellPath -Encoding utf8

Write-Host "Bundled Sussex APK refreshed from $resolvedSourceApkPath" -ForegroundColor Green
Write-Host "Updated SHA256 to $sha256" -ForegroundColor Green
