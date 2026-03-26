<#
.SYNOPSIS
    Replaces old desktop launchers with one clean Viscereality Companion shortcut.
#>
[CmdletBinding()]
param(
    [string]$DesktopPath = [Environment]::GetFolderPath('Desktop'),
    [string]$ShortcutName = 'Viscereality Companion.lnk',
    [switch]$RefreshPublishedBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$launcherScriptPath = Join-Path $repoRoot 'tools\app\Start-Desktop-App.ps1'
$iconPath = Join-Path $repoRoot 'src\ViscerealityCompanion.App\Assets\viscereality-companion.ico'
$shellHost = Get-Command pwsh.exe -ErrorAction SilentlyContinue
if ($null -eq $shellHost) {
    $shellHost = Get-Command powershell.exe -ErrorAction SilentlyContinue
}

if (-not (Test-Path $launcherScriptPath)) {
    throw "Launcher script not found at $launcherScriptPath."
}

if (-not (Test-Path $iconPath)) {
    throw "Launcher icon not found at $iconPath."
}

if ($null -eq $shellHost) {
    throw 'Neither pwsh.exe nor powershell.exe was found on PATH.'
}

$arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$launcherScriptPath`""
if ($RefreshPublishedBuild) {
    $arguments += ' -Refresh'
}

$obsoleteLaunchers = @(
    'Launch Viscereality Companion.cmd',
    'Viscereality Companion Launcher.lnk',
    'Viscereality Companion.lnk'
)

foreach ($name in $obsoleteLaunchers) {
    $candidate = Join-Path $DesktopPath $name
    if (Test-Path $candidate) {
        Remove-Item $candidate -Force
    }
}

$shell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path $DesktopPath $ShortcutName
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $shellHost.Source
$shortcut.Arguments = $arguments
$shortcut.WorkingDirectory = $repoRoot
$shortcut.IconLocation = "$iconPath,0"
$shortcut.Description = 'Launch Viscereality Companion via the single-file publish path'
$shortcut.Save()

$created = $shell.CreateShortcut($shortcutPath)
[PSCustomObject]@{
    Shortcut = $shortcutPath
    TargetPath = $created.TargetPath
    Arguments = $created.Arguments
    WorkingDirectory = $created.WorkingDirectory
    IconLocation = $created.IconLocation
}
