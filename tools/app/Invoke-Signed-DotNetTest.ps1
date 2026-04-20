<#
.SYNOPSIS
    Builds test targets, signs unsigned test output binaries with the shared preview signer, and runs dotnet test.
#>
[CmdletBinding()]
param(
    [string]$Target = 'ViscerealityCompanion.sln',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$SigningThumbprint = $(if (-not [string]::IsNullOrWhiteSpace($env:VISCEREALITY_PREVIEW_SIGNING_THUMBPRINT)) { $env:VISCEREALITY_PREVIEW_SIGNING_THUMBPRINT } else { '08A5878AD6E652A94517D2C79144EB2655B0088C' }),
    [switch]$SkipBuild,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$DotNetTestArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Find-SignToolPath {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path $kitsRoot)) {
        throw 'signtool.exe was not found. Install the Windows SDK packaging tools.'
    }

    $tool = Get-ChildItem -Path $kitsRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($null -eq $tool) {
        throw 'signtool.exe was not found. Install the Windows SDK packaging tools.'
    }

    return $tool.FullName
}

function Get-SigningCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint
    )

    $normalized = ($Thumbprint -replace '\s', '').ToUpperInvariant()
    $match = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Thumbprint -eq $normalized } |
        Select-Object -First 1

    if ($null -eq $match) {
        throw "Signing certificate $normalized was not found in Cert:\CurrentUser\My."
    }

    if (-not $match.HasPrivateKey) {
        throw "Signing certificate $normalized is present, but no private key is available."
    }

    return $match
}

function Get-TestHostDirectories {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TestsRoot,
        [Parameter(Mandatory = $true)]
        [string]$Configuration
    )

    if (-not (Test-Path $TestsRoot)) {
        return @()
    }

    @(
        Get-ChildItem -Path $TestsRoot -Recurse -File -Filter testhost.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match [regex]::Escape("\bin\$Configuration\") } |
            Select-Object -ExpandProperty DirectoryName -Unique
    )
}

function Invoke-SignToolSign {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SignToolPath,
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    & $SignToolPath sign /fd SHA256 /sha1 $Thumbprint /s My $TargetPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "signtool sign failed for $TargetPath with exit code $LASTEXITCODE"
    }
}

function Sign-UnsignedTestOutputs {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Directories,
        [Parameter(Mandatory = $true)]
        [string]$SignToolPath,
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint
    )

    $targets = foreach ($directory in $Directories) {
        Get-ChildItem -Path $directory -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in '.dll', '.exe' } |
            Where-Object { $null -eq (Get-AuthenticodeSignature -FilePath $_.FullName).SignerCertificate }
    }

    $targets = @($targets | Sort-Object FullName -Unique)
    if ($targets.Count -eq 0) {
        Write-Host 'No unsigned test output binaries were found.' -ForegroundColor Green
        return
    }

    Write-Host "Signing $($targets.Count) unsigned test output binary file(s)." -ForegroundColor Cyan
    foreach ($target in $targets) {
        Invoke-SignToolSign -SignToolPath $SignToolPath -Thumbprint $Thumbprint -TargetPath $target.FullName
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$resolvedTarget = if ([System.IO.Path]::IsPathRooted($Target)) { $Target } else { Join-Path $repoRoot $Target }
$testsRoot = Join-Path $repoRoot 'tests'
$certificate = Get-SigningCertificate -Thumbprint $SigningThumbprint
$signToolPath = Find-SignToolPath
$normalizedThumbprint = $certificate.Thumbprint.ToUpperInvariant()

if (-not (Test-Path $resolvedTarget)) {
    throw "Test target not found at $resolvedTarget"
}

if (-not $SkipBuild) {
    Write-Host "Building $resolvedTarget ($Configuration) before signing test outputs..." -ForegroundColor Cyan
    & dotnet build $resolvedTarget -c $Configuration | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for $resolvedTarget with exit code $LASTEXITCODE"
    }
}

$testHostDirectories = @(Get-TestHostDirectories -TestsRoot $testsRoot -Configuration $Configuration)
if ($testHostDirectories.Count -eq 0) {
    throw "No testhost.exe directories were found under $testsRoot for configuration $Configuration."
}

Write-Host "Using signing certificate $($certificate.Subject) [$normalizedThumbprint]" -ForegroundColor Cyan
Sign-UnsignedTestOutputs -Directories $testHostDirectories -SignToolPath $signToolPath -Thumbprint $normalizedThumbprint

$testArgs = @(
    'test',
    $resolvedTarget,
    '-c', $Configuration,
    '--no-build'
)
if ($null -ne $DotNetTestArgs -and $DotNetTestArgs.Count -gt 0) {
    $testArgs += $DotNetTestArgs
}

Write-Host "Running dotnet $($testArgs -join ' ')" -ForegroundColor Cyan
& dotnet @testArgs | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed for $resolvedTarget with exit code $LASTEXITCODE"
}
