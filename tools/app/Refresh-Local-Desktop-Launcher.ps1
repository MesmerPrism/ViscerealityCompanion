<#
.SYNOPSIS
    Creates a clearly labeled Desktop and Start Menu launcher for the repo-local development build.
#>
[CmdletBinding()]
param(
    [string]$DesktopPath = [Environment]::GetFolderPath('Desktop'),
    [string]$StartMenuPath = [Environment]::GetFolderPath('Programs'),
    [string]$ShortcutName = 'Viscereality Companion (Local Dev).lnk'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$launcherScriptPath = Join-Path $repoRoot 'tools\app\Start-Desktop-App-Local.ps1'
$launcherHostPath = Join-Path $repoRoot 'tools\app\Start-Desktop-App-Local.vbs'
$iconPath = Join-Path $repoRoot 'src\ViscerealityCompanion.App\Assets\viscereality-companion-dev.ico'
$scriptHost = Join-Path $env:SystemRoot 'System32\wscript.exe'
$arguments = "//B //nologo `"$launcherHostPath`""
$obsoleteLaunchers = @(
    'Viscereality Companion Local.lnk',
    'Viscereality Companion (Local).lnk',
    'Viscereality Companion (Repo Build).lnk',
    $ShortcutName
)

if (-not (Test-Path $launcherScriptPath)) {
    throw "Local launcher script not found at $launcherScriptPath."
}

if (-not (Test-Path $launcherHostPath)) {
    throw "Local launcher host not found at $launcherHostPath."
}

if (-not (Test-Path $iconPath)) {
    throw "Launcher icon not found at $iconPath."
}

if (-not (Test-Path $scriptHost)) {
    throw "wscript.exe was not found at $scriptHost."
}

function Remove-ObsoleteLaunchers {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    if (-not (Test-Path $RootPath)) {
        return
    }

    foreach ($name in $obsoleteLaunchers) {
        $candidate = Join-Path $RootPath $name
        if (Test-Path $candidate) {
            Remove-Item $candidate -Force
        }
    }
}

function New-LauncherShortcut {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Shell,
        [Parameter(Mandatory = $true)]
        [string]$ShortcutPath
    )

    New-Item -ItemType Directory -Force -Path (Split-Path $ShortcutPath -Parent) | Out-Null

    $shortcut = $Shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $scriptHost
    $shortcut.Arguments = $arguments
    $shortcut.WorkingDirectory = $repoRoot
    $shortcut.IconLocation = "$iconPath,0"
    $shortcut.Description = 'Launch the repo-local Viscereality Companion development build via dotnet run without a console window'
    $shortcut.Save()

    return $Shell.CreateShortcut($ShortcutPath)
}

Remove-ObsoleteLaunchers -RootPath $DesktopPath
Remove-ObsoleteLaunchers -RootPath $StartMenuPath

$shell = New-Object -ComObject WScript.Shell
$desktopShortcutPath = Join-Path $DesktopPath $ShortcutName
$startMenuShortcutPath = Join-Path $StartMenuPath $ShortcutName
$desktopShortcut = New-LauncherShortcut -Shell $shell -ShortcutPath $desktopShortcutPath
$startMenuShortcut = New-LauncherShortcut -Shell $shell -ShortcutPath $startMenuShortcutPath

[PSCustomObject]@{
    DesktopShortcut = $desktopShortcutPath
    StartMenuShortcut = $startMenuShortcutPath
    TargetPath = $desktopShortcut.TargetPath
    Arguments = $desktopShortcut.Arguments
    WorkingDirectory = $desktopShortcut.WorkingDirectory
    IconLocation = $desktopShortcut.IconLocation
}
