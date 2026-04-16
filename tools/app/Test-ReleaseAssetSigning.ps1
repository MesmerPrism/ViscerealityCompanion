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

$reports = @(
    (Get-SigningReport -Path $PreviewSetupPath -AllowSelfSigned:$AllowSelfSignedPreviewSetup |
        Select-Object @{ Name = 'Asset'; Expression = { 'Preview setup' } }, *)
    (Get-SigningReport -Path $PackagePath -AllowSelfSigned:$AllowSelfSignedPackage |
        Select-Object @{ Name = 'Asset'; Expression = { 'Windows package' } }, *)
)

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
