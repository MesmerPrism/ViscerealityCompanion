<#
.SYNOPSIS
    Exports the shared MesmerPrism code-signing certificate for Viscereality Companion releases.

.DESCRIPTION
    Exports a PFX, public CER, and Base64 text file suitable for the GitHub
    Actions secrets used by the release workflow. By default this script reuses
    the long-lived shared MesmerPrism signer already installed on this machine.
#>
[CmdletBinding()]
param(
    [string]$Subject = 'CN=MesmerPrism',
    [string]$FriendlyName = 'Viscereality Companion Signing',
    [int]$ValidYears = 3,
    [string]$OutputRelativePath = 'artifacts\preview-signing',
    [string]$PfxFileName = 'ViscerealityCompanion-preview-signing.pfx',
    [string]$CerFileName = 'ViscerealityCompanion.cer',
    [string]$Base64FileName = 'ViscerealityCompanion-preview-signing.base64.txt',
    [string]$PasswordFileName = 'preview-password.txt',
    [string]$ExistingThumbprint = '08A5878AD6E652A94517D2C79144EB2655B0088C',
    [SecureString]$Password
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $PSBoundParameters.ContainsKey('Password')) {
    $Password = Read-Host 'Enter a password for the exported PFX' -AsSecureString
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

if ([string]::IsNullOrWhiteSpace($ExistingThumbprint)) {
    $notAfter = (Get-Date).Date.AddYears($ValidYears)
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -FriendlyName $FriendlyName `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -HashAlgorithm 'SHA256' `
        -KeyAlgorithm 'RSA' `
        -KeyLength 4096 `
        -KeyExportPolicy Exportable `
        -NotAfter $notAfter

    $actionLabel = 'Created'
}
else {
    $normalizedThumbprint = ($ExistingThumbprint -replace '\s+', '').ToUpperInvariant()
    $cert = Get-ChildItem Cert:\CurrentUser\My,Cert:\LocalMachine\My |
        Where-Object { $_.Thumbprint -eq $normalizedThumbprint } |
        Select-Object -First 1

    if ($null -eq $cert) {
        throw "Existing certificate with thumbprint $normalizedThumbprint was not found in CurrentUser\My or LocalMachine\My."
    }

    if (-not $cert.HasPrivateKey) {
        throw "Existing certificate $normalizedThumbprint does not have an exportable private key."
    }

    $actionLabel = 'Exported existing'
}

$pfxPath = Join-Path $outputPath $PfxFileName
$cerPath = Join-Path $outputPath $CerFileName
$base64Path = Join-Path $outputPath $Base64FileName
$passwordPath = Join-Path $outputPath $PasswordFileName

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $Password | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

$pfxBytes = [System.IO.File]::ReadAllBytes($pfxPath)
[Convert]::ToBase64String($pfxBytes) | Set-Content -Path $base64Path -Encoding utf8

$plainPassword = [System.Net.NetworkCredential]::new('', $Password).Password
[System.IO.File]::WriteAllText(
    $passwordPath,
    $plainPassword,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "$actionLabel Viscereality Companion certificate bundle." -ForegroundColor Green
Write-Host "Signer thumbprint: $($cert.Thumbprint)" -ForegroundColor Green
Write-Host "PFX : $pfxPath"
Write-Host "CER : $cerPath"
Write-Host "B64 : $base64Path"
Write-Host "PWD : $passwordPath"
Write-Host ''
Write-Host 'Configure these GitHub repository secrets:' -ForegroundColor Cyan
Write-Host "  WINDOWS_PACKAGE_CERTIFICATE_BASE64 = <contents of $base64Path>"
Write-Host "  WINDOWS_PACKAGE_CERTIFICATE_PASSWORD = <the password you just entered>"
Write-Host "  WINDOWS_PACKAGE_PUBLISHER = $Subject"
Write-Host ''
Write-Host 'Guided setup signer secrets:'
Write-Host "  WINDOWS_PREVIEW_SETUP_CERTIFICATE_BASE64 = <contents of $base64Path>"
Write-Host "  WINDOWS_PREVIEW_SETUP_CERTIFICATE_PASSWORD = <the password you just entered>"
Write-Host ''
Write-Host "Password file: $passwordPath"
Write-Host ''
Write-Host 'The release workflow publishes the CER alongside the MSIX so operators can trust it before installing the packaged release.'
