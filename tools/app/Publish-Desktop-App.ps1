<#
.SYNOPSIS
    Publishes a Smart App Control-safe single-file build of the companion app.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$OutputRelativePath = 'artifacts\publish\ViscerealityCompanion.App',
    [switch]$RefreshLaunchers
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectPath = Join-Path $repoRoot 'src\ViscerealityCompanion.App\ViscerealityCompanion.App.csproj'
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))
$exePath = Join-Path $outputPath 'ViscerealityCompanion.exe'
$sampleRoots = @(
    'samples\quest-session-kit',
    'samples\study-shells'
)

function Sync-Directory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    if (-not (Test-Path $SourcePath)) {
        throw "Source directory not found at $SourcePath"
    }

    if (Test-Path $DestinationPath) {
        Remove-Item -Path $DestinationPath -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path (Split-Path $DestinationPath -Parent) | Out-Null
    Copy-Item -Path $SourcePath -Destination $DestinationPath -Recurse -Force
}

function Remove-StaleRepoExecutables {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$CurrentExePath
    )

    $currentFullPath = [System.IO.Path]::GetFullPath($CurrentExePath)
    $repoExes = Get-ChildItem -Path $RepoRoot -Recurse -File -Filter 'ViscerealityCompanion.exe' -ErrorAction SilentlyContinue
    $removed = New-Object System.Collections.Generic.List[string]

    foreach ($candidate in $repoExes) {
        $candidatePath = [System.IO.Path]::GetFullPath($candidate.FullName)
        if ([string]::Equals($candidatePath, $currentFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        try {
            Remove-Item -Path $candidate.FullName -Force
            $removed.Add($candidate.FullName) | Out-Null
        }
        catch {
            Write-Warning "Could not remove stale launcher candidate $($candidate.FullName): $($_.Exception.Message)"
        }
    }

    return $removed.ToArray()
}

if (-not (Test-Path $projectPath)) {
    throw "App project not found at $projectPath"
}

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

$publishArgs = @(
    'publish',
    $projectPath,
    '-c', $Configuration,
    '-r', $RuntimeIdentifier,
    '-p:PublishSingleFile=true',
    '-p:SelfContained=false',
    '-o', $outputPath
)

Write-Host "Publishing Viscereality Companion to $outputPath" -ForegroundColor Cyan
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for $projectPath with exit code $LASTEXITCODE"
}

if (-not (Test-Path $exePath)) {
    throw "Published executable not found at $exePath"
}

foreach ($relativePath in $sampleRoots) {
    $sourcePath = Join-Path $repoRoot $relativePath
    $destinationPath = Join-Path $outputPath $relativePath
    Sync-Directory -SourcePath $sourcePath -DestinationPath $destinationPath
}

$removedExePaths = Remove-StaleRepoExecutables -RepoRoot $repoRoot -CurrentExePath $exePath

if ($RefreshLaunchers) {
    $refreshLauncherScript = Join-Path $repoRoot 'tools\app\Refresh-Desktop-Launcher.ps1'
    if (-not (Test-Path $refreshLauncherScript)) {
        throw "Launcher refresh script not found at $refreshLauncherScript"
    }

    & $refreshLauncherScript | Out-Null
}

$exe = Get-Item $exePath
[PSCustomObject]@{
    OutputPath = $outputPath
    ExePath = $exe.FullName
    LastWriteTimeUtc = $exe.LastWriteTimeUtc
    Length = $exe.Length
    RemovedStaleExeCount = @($removedExePaths).Count
    RefreshedLaunchers = [bool]$RefreshLaunchers
}
