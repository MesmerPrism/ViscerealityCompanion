<#
.SYNOPSIS
    Replaces old desktop launchers with one clean Viscereality Companion shortcut.
#>
[CmdletBinding()]
param(
    [string]$DesktopPath = [Environment]::GetFolderPath('Desktop'),
    [string]$ShortcutName = 'Viscereality Companion.lnk'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$appDir = Join-Path $repoRoot 'src\ViscerealityCompanion.App\bin\Debug\net10.0-windows'
$targetPath = Join-Path $appDir 'ViscerealityCompanion.exe'
$iconPath = Join-Path $repoRoot 'src\ViscerealityCompanion.App\Assets\viscereality-companion.ico'

if (-not (Test-Path $targetPath)) {
    throw "Target executable not found at $targetPath. Build the app first."
}

if (-not (Test-Path $iconPath)) {
    throw "Launcher icon not found at $iconPath."
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
$shortcut.TargetPath = $targetPath
$shortcut.WorkingDirectory = $appDir
$shortcut.IconLocation = "$iconPath,0"
$shortcut.Description = 'Launch Viscereality Companion'
$shortcut.Save()

$created = $shell.CreateShortcut($shortcutPath)
[PSCustomObject]@{
    Shortcut = $shortcutPath
    TargetPath = $created.TargetPath
    WorkingDirectory = $created.WorkingDirectory
    IconLocation = $created.IconLocation
}
