<#
.SYNOPSIS
    Launches the local repo build of the companion app for development.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoBuild,
    [switch]$RefreshLauncher,
    [switch]$Wait
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectPath = Join-Path $repoRoot 'src\ViscerealityCompanion.App\ViscerealityCompanion.App.csproj'
$refreshLauncherScript = Join-Path $PSScriptRoot 'Refresh-Local-Desktop-Launcher.ps1'

if (-not (Test-Path $projectPath)) {
    throw "App project not found at $projectPath"
}

if ($RefreshLauncher) {
    if (-not (Test-Path $refreshLauncherScript)) {
        throw "Local launcher refresh script not found at $refreshLauncherScript"
    }

    & $refreshLauncherScript | Out-Null
}

$dotnet = Get-Command dotnet -ErrorAction Stop
$arguments = @(
    'run',
    '--project', $projectPath,
    '-c', $Configuration,
    '--no-launch-profile'
)

if ($NoBuild) {
    $arguments += '--no-build'
}

$process = Start-Process `
    -FilePath $dotnet.Source `
    -ArgumentList $arguments `
    -WorkingDirectory $repoRoot `
    -WindowStyle Hidden `
    -PassThru

if ($Wait) {
    Wait-Process -Id $process.Id
}

$process | Select-Object Id, ProcessName, StartTime
