<#
.SYNOPSIS
    Publishes the guided Viscereality Companion setup bootstrapper.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Version = '0.1.74.0',
    [string]$OutputRelativePath = 'artifacts\windows-installer',
    [string]$FileName = 'ViscerealityCompanion-Setup.exe',
    [string]$PackageCertificatePath,
    [string]$PackageCertificatePassword,
    [string]$PackageCertificateTimestampUrl
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$defaultTimestampUrl = 'http://timestamp.digicert.com'

function Resolve-SignToolPath {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    $searchRoots = @(
        ${env:ProgramFiles(x86)},
        $env:ProgramFiles
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($root in $searchRoots) {
        $kitsRoot = Join-Path $root 'Windows Kits\10\bin'
        if (-not (Test-Path $kitsRoot)) {
            continue
        }

        $match = Get-ChildItem -Path $kitsRoot -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($null -ne $match) {
            return $match.FullName
        }
    }

    throw "Could not locate signtool.exe. Install the Windows SDK or add signtool.exe to PATH."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$projectPath = Join-Path $repoRoot 'src\ViscerealityCompanion.PreviewInstaller\ViscerealityCompanion.PreviewInstaller.csproj'
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))
$publishPath = Join-Path $outputPath 'guided-setup-publish'

if (-not (Test-Path $projectPath)) {
    throw "Guided setup project not found at $projectPath"
}

if (-not [string]::IsNullOrWhiteSpace($PackageCertificatePath) -and
    [string]::IsNullOrWhiteSpace($PackageCertificateTimestampUrl)) {
    $PackageCertificateTimestampUrl = $defaultTimestampUrl
    Write-Host "No timestamp URL was provided. Defaulting to $PackageCertificateTimestampUrl for guided-setup signing." -ForegroundColor Yellow
}

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
if (Test-Path $publishPath) {
    Remove-Item -Recurse -Force $publishPath
}

$restoreArgs = @(
    'restore',
    $projectPath,
    '--runtime', $RuntimeIdentifier
)

Write-Host 'Restoring guided setup bootstrapper runtime packs...' -ForegroundColor Cyan
dotnet @restoreArgs | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed for $projectPath with exit code $LASTEXITCODE"
}

$publishArgs = @(
    'publish',
    $projectPath,
    '--configuration', $Configuration,
    '--runtime', $RuntimeIdentifier,
    '/p:PublishSingleFile=true',
    '/p:SelfContained=true',
    '/p:PublishTrimmed=false',
    '/p:EnableCompressionInSingleFile=true',
    '/p:DebugType=None',
    '/p:DebugSymbols=false',
    "/p:Version=$Version",
    "/p:AssemblyVersion=$Version",
    "/p:FileVersion=$Version",
    "/p:InformationalVersion=$Version",
    '--output', $publishPath
)

Write-Host 'Publishing guided setup bootstrapper...' -ForegroundColor Cyan
dotnet @publishArgs | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for $projectPath with exit code $LASTEXITCODE"
}

$publishedExe = Join-Path $publishPath 'ViscerealityCompanion.PreviewInstaller.exe'
if (-not (Test-Path $publishedExe)) {
    throw "Published guided setup bootstrapper not found at $publishedExe"
}

$finalPath = Join-Path $outputPath $FileName
Copy-Item $publishedExe $finalPath -Force

if (-not [string]::IsNullOrWhiteSpace($PackageCertificatePath)) {
    if (-not (Test-Path $PackageCertificatePath)) {
        throw "Signing certificate not found at $PackageCertificatePath"
    }

    $signToolPath = Resolve-SignToolPath
    $signArgs = @(
        'sign',
        '/fd', 'SHA256',
        '/f', $PackageCertificatePath
    )

    if (-not [string]::IsNullOrWhiteSpace($PackageCertificatePassword)) {
        $signArgs += @('/p', $PackageCertificatePassword)
    }

    if (-not [string]::IsNullOrWhiteSpace($PackageCertificateTimestampUrl)) {
        $signArgs += @('/tr', $PackageCertificateTimestampUrl, '/td', 'SHA256')
    }

    $signArgs += $finalPath

    Write-Host 'Signing guided setup bootstrapper...' -ForegroundColor Cyan
    & $signToolPath @signArgs | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed for $finalPath with exit code $LASTEXITCODE"
    }
}

Remove-Item -Recurse -Force $publishPath
Write-Host "Copied guided setup bootstrapper to $finalPath" -ForegroundColor Green
