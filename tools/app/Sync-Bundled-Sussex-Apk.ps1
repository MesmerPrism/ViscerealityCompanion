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

function Set-JsonStringPropertyInPlace {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$PropertyName,

        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasUtf8Bom = $bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and
        $bytes[2] -eq 0xBF

    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) {
        $text = $text.Substring(1)
    }

    $pattern = '("' + [System.Text.RegularExpressions.Regex]::Escape($PropertyName) + '"\s*:\s*")[^"]*(")'
    $updated = [System.Text.RegularExpressions.Regex]::Replace(
        $text,
        $pattern,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            return $match.Groups[1].Value + $Value + $match.Groups[2].Value
        },
        1)

    if ($updated -eq $text) {
        throw "Property '$PropertyName' was not found in $Path"
    }

    $encoding = [System.Text.UTF8Encoding]::new($hasUtf8Bom)
    [System.IO.File]::WriteAllText($Path, $updated, $encoding)
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

Set-JsonStringPropertyInPlace -Path $compatibilityPath -PropertyName 'sha256' -Value $sha256
Set-JsonStringPropertyInPlace -Path $studyShellPath -PropertyName 'sha256' -Value $sha256
if (-not [string]::IsNullOrWhiteSpace($VersionName)) {
    Set-JsonStringPropertyInPlace -Path $studyShellPath -PropertyName 'versionName' -Value $VersionName
}

Write-Host "Bundled Sussex APK refreshed from $resolvedSourceApkPath" -ForegroundColor Green
Write-Host "Updated SHA256 to $sha256" -ForegroundColor Green
