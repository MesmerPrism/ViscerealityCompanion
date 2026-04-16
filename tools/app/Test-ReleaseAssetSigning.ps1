<#
.SYNOPSIS
    Validates Authenticode signing on the published preview setup EXE and MSIX package.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PreviewSetupPath,
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,
    [switch]$AllowSelfSigned,
    [switch]$AllowSelfSignedPreviewSetup,
    [switch]$AllowSelfSignedPackage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

if ($AllowSelfSigned) {
    $AllowSelfSignedPreviewSetup = $true
    $AllowSelfSignedPackage = $true
}

function Get-SigningReport {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [switch]$AllowSelfSigned
    )

    $resolvedPath = (Resolve-Path $Path).Path
    $signature = Get-AuthenticodeSignature -FilePath $resolvedPath
    $signerCertificate = $signature.SignerCertificate
    $selfIssued = $null -ne $signerCertificate -and
        [string]::Equals($signerCertificate.Subject, $signerCertificate.Issuer, [System.StringComparison]::OrdinalIgnoreCase)
    $isAllowedSelfSignedTrustFailure =
        $AllowSelfSigned -and
        $selfIssued -and
        $signature.Status -eq [System.Management.Automation.SignatureStatus]::UnknownError -and
        $signature.StatusMessage -match 'not trusted by the trust provider'

    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -and -not $isAllowedSelfSignedTrustFailure) {
        throw "Authenticode validation failed for ${resolvedPath}: $($signature.Status) $($signature.StatusMessage)"
    }

    if ($null -eq $signerCertificate) {
        throw "No signer certificate was found on $resolvedPath."
    }

    $hasTimestamp = $null -ne $signature.TimeStamperCertificate
    if (-not $hasTimestamp) {
        throw "The signed asset $resolvedPath is missing an RFC3161 timestamp."
    }

    [pscustomobject]@{
        Path          = $resolvedPath
        Subject       = $signerCertificate.Subject
        Issuer        = $signerCertificate.Issuer
        Thumbprint    = $signerCertificate.Thumbprint
        HasTimestamp  = $hasTimestamp
        TimestampBy   = $signature.TimeStamperCertificate.Subject
        SelfIssued    = $selfIssued
        AllowSelfSigned = [bool]$AllowSelfSigned
    }
}

function Assert-MsixPayloadLayout {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $resolvedPath = (Resolve-Path $Path).Path
    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPath)

    try {
        $entryNames = $archive.Entries | ForEach-Object { $_.FullName }
        $requiredEntries = @(
            'ViscerealityCompanion.App/ViscerealityCompanion.exe',
            'ViscerealityCompanion.App/ViscerealityCompanion.dll'
        )

        foreach ($requiredEntry in $requiredEntries) {
            if ($requiredEntry -notin $entryNames) {
                throw "The MSIX payload layout is missing $requiredEntry. Do not replace the WAP-built desktop payload with a single-file repack."
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-PackagedExecutablePayloadSigning {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [switch]$AllowSelfSigned
    )

    $resolvedPath = (Resolve-Path $Path).Path
    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPath)
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("viscereality-payload-signing-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    try {
        $payloadEntries = @(
            $archive.Entries |
                Where-Object { $_.FullName -match '^ViscerealityCompanion\.App/.+\.(dll|exe)$' }
        )

        if ($payloadEntries.Count -eq 0) {
            throw "No executable payload files were found inside $resolvedPath."
        }

        foreach ($entry in $payloadEntries) {
            $entryPath = Join-Path $tempRoot ($entry.FullName -replace '/', '\')
            $entryDirectory = Split-Path -Parent $entryPath
            if (-not (Test-Path $entryDirectory)) {
                New-Item -ItemType Directory -Force -Path $entryDirectory | Out-Null
            }

            $entryStream = $entry.Open()
            try {
                $fileStream = [System.IO.File]::Create($entryPath)
                try {
                    $entryStream.CopyTo($fileStream)
                }
                finally {
                    $fileStream.Dispose()
                }
            }
            finally {
                $entryStream.Dispose()
            }

            try {
                Get-SigningReport -Path $entryPath -AllowSelfSigned:$AllowSelfSigned | Out-Null
            }
            catch {
                throw "Executable payload signing validation failed for $($entry.FullName): $($_.Exception.Message)"
            }
        }
    }
    finally {
        $archive.Dispose()
        if (Test-Path $tempRoot) {
            Remove-Item -Recurse -Force $tempRoot
        }
    }
}

$reports = @(
    (Get-SigningReport -Path $PreviewSetupPath -AllowSelfSigned:$AllowSelfSignedPreviewSetup |
        Select-Object @{ Name = 'Asset'; Expression = { 'Preview setup' } }, *)
    (Get-SigningReport -Path $PackagePath -AllowSelfSigned:$AllowSelfSignedPackage |
        Select-Object @{ Name = 'Asset'; Expression = { 'Windows package' } }, *)
)

Assert-MsixPayloadLayout -Path $PackagePath
Assert-PackagedExecutablePayloadSigning -Path $PackagePath -AllowSelfSigned:$AllowSelfSignedPackage

$selfIssuedReports = $reports | Where-Object { $_.SelfIssued }
foreach ($report in $selfIssuedReports) {
    $message = switch ($report.Asset) {
        'Preview setup' {
            "Preview setup bootstrapper is signed with a self-issued certificate. " +
            "Smart App Control can still block the downloaded helper EXE on fresh machines."
        }
        'Windows package' {
            "Windows package is signed with a self-issued certificate. " +
            "That remains workable only after the preview certificate is trusted explicitly for sideloading."
        }
        default {
            "One or more release assets are signed with a self-issued certificate."
        }
    }

    if ($report.AllowSelfSigned) {
        Write-Warning $message
    }
    else {
        throw $message
    }
}

$reports | Format-Table Asset, Path, Subject, Issuer, Thumbprint, HasTimestamp, TimestampBy, SelfIssued, AllowSelfSigned -AutoSize | Out-Host
