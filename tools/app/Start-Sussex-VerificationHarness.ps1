<#
.SYNOPSIS
    Publishes and launches the Sussex verification harness from a single-file output.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$OutputRelativePath = 'artifacts\\publish\\ViscerealityCompanion.VerificationHarness',
    [switch]$Refresh,
    [switch]$SkipKioskExit,
    [switch]$Wait
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\\..')).Path
$projectPath = Join-Path $repoRoot 'tools\\ViscerealityCompanion.VerificationHarness\\ViscerealityCompanion.VerificationHarness.csproj'
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))
$exePath = Join-Path $outputPath 'ViscerealityCompanion.VerificationHarness.exe'

if (-not (Test-Path $projectPath)) {
    throw "Verification harness project not found at $projectPath"
}

$needsPublish = $Refresh -or -not (Test-Path $exePath)
if (-not $needsPublish) {
    $publishedAt = (Get-Item $exePath).LastWriteTimeUtc
    $inputAt = Get-NewestInputWriteTimeUtc @(
        'tools\\ViscerealityCompanion.VerificationHarness',
        'src\\ViscerealityCompanion.App',
        'src\\ViscerealityCompanion.Core',
        'samples\\quest-session-kit',
        'samples\\study-shells'
    )
    $needsPublish = $inputAt -gt $publishedAt
}

if ($needsPublish) {
    New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

    $publishArgs = @(
        'publish',
        $projectPath,
        '-c', $Configuration,
        '-r', $RuntimeIdentifier,
        '--self-contained', 'false',
        '-p:PublishSingleFile=true',
        '-o', $outputPath
    )

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $projectPath with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path $exePath)) {
    throw "Published verification harness executable not found at $exePath"
}

$previousSkipKioskExit = $env:VC_SKIP_KIOSK_EXIT
try {
    if ($SkipKioskExit) {
        $env:VC_SKIP_KIOSK_EXIT = '1'
    }
    else {
        Remove-Item Env:VC_SKIP_KIOSK_EXIT -ErrorAction SilentlyContinue
    }

    $process = Start-Process -FilePath $exePath -WorkingDirectory $repoRoot -PassThru
}
finally {
    if ($null -eq $previousSkipKioskExit) {
        Remove-Item Env:VC_SKIP_KIOSK_EXIT -ErrorAction SilentlyContinue
    }
    else {
        $env:VC_SKIP_KIOSK_EXIT = $previousSkipKioskExit
    }
}

if ($Wait) {
    Wait-Process -Id $process.Id
    $process.Refresh()
    if ($process.ExitCode -ne 0) {
        throw "Verification harness exited with code $($process.ExitCode). Check artifacts\\verify\\sussex-study-mode-live\\sussex-study-mode-error.txt for details."
    }
}

$process | Select-Object Id, ProcessName, Path
