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
    [string]$OutputRelativePath = 'artifacts\publish\ViscerealityCompanion.App'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectPath = Join-Path $repoRoot 'src\ViscerealityCompanion.App\ViscerealityCompanion.App.csproj'
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))
$exePath = Join-Path $outputPath 'ViscerealityCompanion.exe'

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

$exe = Get-Item $exePath
[PSCustomObject]@{
    OutputPath = $outputPath
    ExePath = $exe.FullName
    LastWriteTimeUtc = $exe.LastWriteTimeUtc
    Length = $exe.Length
}
