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

@(
    'MesmerPrism.ViscerealityCompanionPreview',
    'MesmerPrism.ViscerealityCompanion'
) | ForEach-Object {
    Set-Variable -Name ($_.Replace('.', '_')) -Value $_ -Option ReadOnly -Scope Script
} | Out-Null

$script:SupportedPackageIds = @(
    $script:MesmerPrism_ViscerealityCompanionPreview,
    $script:MesmerPrism_ViscerealityCompanion
)

function Test-InstalledPackagePresent {
    try {
        return $null -ne (Get-InstalledPackageRegistration)
    }
    catch {
        return $false
    }
}

function Get-InstalledPackageRegistration {
    try {
        return Get-AppxPackage |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_.PackageFamilyName) -and
                $script:SupportedPackageIds -contains $_.Name
            } |
            Sort-Object @{ Expression = { [Array]::IndexOf($script:SupportedPackageIds, $_.Name) } }, @{ Expression = { $_.Version }; Descending = $true } |
            Select-Object -First 1
    }
    catch {
        return $null
    }
}

function Try-StartInstalledPackage {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Package,
        [switch]$Wait
    )

    $existingProcessIds = @(Get-Process -Name 'ViscerealityCompanion' -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Id)
    $launchTarget = "shell:AppsFolder\$($Package.PackageFamilyName)!App"
    Start-Process -FilePath 'explorer.exe' -ArgumentList $launchTarget | Out-Null

    $launchedProcess = $null
    foreach ($attempt in 1..12) {
        Start-Sleep -Milliseconds 250
        $launchedProcess = Get-Process -Name 'ViscerealityCompanion' -ErrorAction SilentlyContinue |
            Where-Object { $existingProcessIds -notcontains $_.Id } |
            Select-Object -First 1

        if ($null -ne $launchedProcess) {
            break
        }
    }

    if ($null -eq $launchedProcess -and $existingProcessIds.Count -gt 0) {
        return [PSCustomObject]@{
            LaunchMode        = 'PackagedApp'
            PackageFamilyName = $Package.PackageFamilyName
            PackageVersion    = $Package.Version.ToString()
            LaunchTarget      = $launchTarget
            ReusedProcess     = $true
            ProcessId         = $null
        }
    }

    if ($null -eq $launchedProcess) {
        return $null
    }

    Start-Sleep -Seconds 2
    $launchedProcess = Get-Process -Id $launchedProcess.Id -ErrorAction SilentlyContinue
    if ($null -eq $launchedProcess) {
        return $null
    }

    if ($Wait) {
        Wait-Process -Id $launchedProcess.Id
    }

    [PSCustomObject]@{
        LaunchMode        = 'PackagedApp'
        PackageFamilyName = $Package.PackageFamilyName
        PackageVersion    = $Package.Version.ToString()
        LaunchTarget      = $launchTarget
        ReusedProcess     = $false
        ProcessId         = $launchedProcess.Id
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
$installedPackage = Get-InstalledPackageRegistration

if (-not (Test-Path $publishScript)) {
    throw "Publish script not found at $publishScript"
}

$shouldRefreshLaunchers = -not $SkipLauncherRefresh -and $null -eq $installedPackage

if ($null -ne $installedPackage) {
    $packageLaunch = Try-StartInstalledPackage -Package $installedPackage -Wait:$Wait
    if ($null -ne $packageLaunch) {
        return $packageLaunch
    }

    $needsPublish = $Refresh -or -not (Test-Path $exePath)
}
else {
    $needsPublish = $Refresh -or -not (Test-Path $exePath)
}

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
    try {
        & $publishScript `
            -Configuration $Configuration `
            -RuntimeIdentifier $RuntimeIdentifier `
            -OutputRelativePath $OutputRelativePath `
            -RefreshLaunchers:$shouldRefreshLaunchers | Out-Null
    }
    catch {
        if (-not (Test-Path $exePath)) {
            throw
        }
    }
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
