<#
.SYNOPSIS
    Launches the Smart App Control-safe published companion app.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$OutputRelativePath = 'artifacts\publish\ViscerealityCompanion.App',
    [switch]$Refresh,
    [switch]$SkipLauncherRefresh,
    [switch]$Wait
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-InstalledPackagePresent {
    try {
        return $null -ne (Get-AppxPackage 'MesmerPrism.ViscerealityCompanion' -ErrorAction Stop)
    }
    catch {
        return $false
    }
}

function Get-NewestInputWriteTimeUtc {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$RelativePaths
    )

    $newest = [DateTime]::MinValue
    foreach ($relativePath in $RelativePaths) {
        $fullPath = Join-Path $repoRoot $relativePath
        if (-not (Test-Path $fullPath)) {
            continue
        }

        $files = Get-ChildItem $fullPath -Recurse -File |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

        foreach ($file in $files) {
            if ($file.LastWriteTimeUtc -gt $newest) {
                $newest = $file.LastWriteTimeUtc
            }
        }
    }

    return $newest
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$publishScript = Join-Path $PSScriptRoot 'Publish-Desktop-App.ps1'
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))
$exePath = Join-Path $outputPath 'ViscerealityCompanion.exe'
$shouldRefreshLaunchers = -not $SkipLauncherRefresh -and -not (Test-InstalledPackagePresent)

if (-not (Test-Path $publishScript)) {
    throw "Publish script not found at $publishScript"
}

$needsPublish = $Refresh -or -not (Test-Path $exePath)
if (-not $needsPublish) {
    $publishedAt = (Get-Item $exePath).LastWriteTimeUtc
    $inputAt = Get-NewestInputWriteTimeUtc @(
        'src\ViscerealityCompanion.App',
        'src\ViscerealityCompanion.Core',
        'samples\quest-session-kit',
        'samples\study-shells'
    )
    $needsPublish = $inputAt -gt $publishedAt
}

if ($needsPublish) {
    & $publishScript `
        -Configuration $Configuration `
        -RuntimeIdentifier $RuntimeIdentifier `
        -OutputRelativePath $OutputRelativePath `
        -RefreshLaunchers:$shouldRefreshLaunchers | Out-Null
}
elseif ($shouldRefreshLaunchers) {
    $refreshLauncherScript = Join-Path $PSScriptRoot 'Refresh-Desktop-Launcher.ps1'
    if (-not (Test-Path $refreshLauncherScript)) {
        throw "Launcher refresh script not found at $refreshLauncherScript"
    }

    & $refreshLauncherScript | Out-Null
}

if (-not (Test-Path $exePath)) {
    throw "Published executable not found at $exePath"
}

$process = Start-Process -FilePath $exePath -WorkingDirectory $outputPath -PassThru
if ($Wait) {
    Wait-Process -Id $process.Id
}

$process | Select-Object Id, ProcessName, Path
