<#
.SYNOPSIS
    Builds the Viscereality Companion MSIX package and optional App Installer file.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('x64')]
    [string]$Platform = 'x64',
    [ValidateSet('Release', 'Dev')]
    [string]$IdentityFlavor = 'Release',
    [string]$Version = '0.1.75.0',
    [string]$PackageId,
    [string]$Publisher = 'CN=MesmerPrism',
    [string]$DisplayName,
    [string]$PublisherDisplayName = 'Mesmer Prism',
    [string]$OutputRelativePath = 'artifacts\windows-installer',
    [string]$PackageFileName,
    [string]$AppInstallerFileName,
    [string]$CertificateFileName,
    [string]$AppInstallerUri,
    [string]$MainPackageUri,
    [string]$PackageCertificatePath,
    [string]$PackageCertificatePassword,
    [string]$PackageCertificateTimestampUrl,
    [string]$PreferredDotNetSdkVersion,
    [switch]$RefreshBundledSussexApk,
    [string]$BundledSussexApkSourcePath,
    [string]$BundledSussexApkVersion,
    [switch]$Unsigned
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$defaultTimestampUrl = 'http://timestamp.digicert.com'

function Get-IdentityDefaults {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Flavor
    )

    switch ($Flavor) {
        'Release' {
            return @{
                PackageId = 'MesmerPrism.ViscerealityCompanion'
                DisplayName = 'Viscereality Companion'
                PackageFileName = 'ViscerealityCompanion.msix'
                AppInstallerFileName = 'ViscerealityCompanion.appinstaller'
                CertificateFileName = 'ViscerealityCompanion.cer'
            }
        }
        'Dev' {
            return @{
                PackageId = 'MesmerPrism.ViscerealityCompanionDev'
                DisplayName = 'Viscereality Companion Dev'
                PackageFileName = 'ViscerealityCompanion-Dev.msix'
                AppInstallerFileName = 'ViscerealityCompanion-Dev.appinstaller'
                CertificateFileName = 'ViscerealityCompanion-Dev.cer'
            }
        }
        default {
            throw "Unsupported identity flavor: $Flavor"
        }
    }
}

$identityDefaults = Get-IdentityDefaults -Flavor $IdentityFlavor
if ([string]::IsNullOrWhiteSpace($PackageId)) {
    $PackageId = $identityDefaults.PackageId
}

if ([string]::IsNullOrWhiteSpace($DisplayName)) {
    $DisplayName = $identityDefaults.DisplayName
}

if ([string]::IsNullOrWhiteSpace($PackageFileName)) {
    $PackageFileName = $identityDefaults.PackageFileName
}

if ([string]::IsNullOrWhiteSpace($AppInstallerFileName)) {
    $AppInstallerFileName = $identityDefaults.AppInstallerFileName
}

if ([string]::IsNullOrWhiteSpace($CertificateFileName)) {
    $CertificateFileName = $identityDefaults.CertificateFileName
}

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $matches = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe'
        if ($LASTEXITCODE -eq 0 -and $matches) {
            return $matches | Select-Object -First 1
        }

        $matches = & $vswhere -latest -prerelease -products * -find 'MSBuild\**\Bin\MSBuild.exe'
        if ($LASTEXITCODE -eq 0 -and $matches) {
            return $matches | Select-Object -First 1
        }
    }

    $command = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $wellKnownPaths = @(
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2026\Community\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2026\Professional\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2026\Enterprise\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2026\BuildTools\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe')
    )
    foreach ($path in $wellKnownPaths) {
        if (Test-Path $path) {
            return $path
        }
    }

    throw 'MSBuild.exe was not found. Install Visual Studio Build Tools with the MSIX/Desktop Bridge workload, or run this in GitHub Actions on windows-latest.'
}

function Find-WindowsSdkTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ToolName
    )

    $command = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $kitsRoot) {
        $tool = Get-ChildItem -Path $kitsRoot -Recurse -Filter $ToolName -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($null -ne $tool) {
            return $tool.FullName
        }
    }

    throw "$ToolName was not found. Install the Windows 10/11 SDK packaging tools."
}

function Resolve-UapPlatformVersion {
    param(
        [string]$PreferredVersion
    )

    $uapRoots = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Platforms\UAP'),
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\DesignTime\CommonConfiguration\Neutral\UAP')
    )

    $availableVersions = foreach ($root in $uapRoots) {
        if (-not (Test-Path $root)) {
            continue
        }

        Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
            Select-Object -ExpandProperty Name
    }

    $availableVersions = @(
        $availableVersions |
            Sort-Object -Unique |
            Sort-Object { [version]$_ } -Descending
    )

    if ($null -eq $availableVersions -or $availableVersions.Count -eq 0) {
        throw 'No installed UAP Windows SDK versions were found under Windows Kits\10.'
    }

    if (-not [string]::IsNullOrWhiteSpace($PreferredVersion) -and $availableVersions -contains $PreferredVersion) {
        return $PreferredVersion
    }

    return $availableVersions | Select-Object -First 1
}

function Invoke-SignToolSign {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SignToolPath,
        [Parameter(Mandatory = $true)]
        [string]$CertificatePath,
        [Parameter(Mandatory = $true)]
        [string]$CertificatePassword,
        [Parameter(Mandatory = $true)]
        [string]$TargetPath,
        [string]$TimestampUrl
    )

    $signArgs = @(
        'sign',
        '/fd', 'SHA256',
        '/f', [System.IO.Path]::GetFullPath($CertificatePath),
        '/p', $CertificatePassword
    )

    if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $signArgs += @('/tr', $TimestampUrl, '/td', 'SHA256')
    }

    $signArgs += [System.IO.Path]::GetFullPath($TargetPath)

    & $SignToolPath @signArgs | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "signtool sign failed for $TargetPath with exit code $LASTEXITCODE"
    }
}

function Initialize-DotNetSdkResolver {
    param(
        [string]$PreferredSdkVersion
    )

    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) {
        return
    }

    $dotnetRoot = Split-Path -Parent $dotnetCommand.Source
    $sdkRoot = Join-Path $dotnetRoot 'sdk'
    if (-not (Test-Path $sdkRoot)) {
        return
    }

    if ([string]::IsNullOrWhiteSpace($PreferredSdkVersion)) {
        $globalJsonPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')) 'global.json'
        if (Test-Path $globalJsonPath) {
            try {
                $globalJson = Get-Content -Path $globalJsonPath -Raw | ConvertFrom-Json
                if (-not [string]::IsNullOrWhiteSpace($globalJson.sdk.version)) {
                    $PreferredSdkVersion = $globalJson.sdk.version.Trim()
                }
            }
            catch {
            }
        }
    }

    $sdkCandidates = Get-ChildItem -Path $sdkRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^10\.' -and (Test-Path (Join-Path $_.FullName 'Sdks')) }

    if ($null -eq $sdkCandidates -or $sdkCandidates.Count -eq 0) {
        return
    }

    $preferredSdkDir = if (-not [string]::IsNullOrWhiteSpace($PreferredSdkVersion)) {
        $sdkCandidates |
            Where-Object { $_.Name -eq $PreferredSdkVersion } |
            Select-Object -First 1
    }

    $stableSdkDir = $sdkCandidates |
        Where-Object { $_.Name -notmatch '-' } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    $sdkDir = if ($null -ne $preferredSdkDir) {
        $preferredSdkDir
    }
    elseif ($null -ne $stableSdkDir) {
        $stableSdkDir
    }
    else {
        $sdkCandidates |
            Sort-Object Name -Descending |
            Select-Object -First 1
    }

    if ($null -eq $sdkDir) {
        return
    }

    $env:DOTNET_ROOT = $dotnetRoot
    $env:DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR = $dotnetRoot
    $env:DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR = Join-Path $sdkDir.FullName 'Sdks'
    $env:DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER = $sdkDir.Name
}

function Set-ManifestValue {
    param(
        [Parameter(Mandatory = $true)]
        [xml]$Manifest,
        [Parameter(Mandatory = $true)]
        [string]$XPath,
        [Parameter(Mandatory = $true)]
        [string]$Value,
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlNamespaceManager]$NamespaceManager
    )

    $node = $Manifest.SelectSingleNode($XPath, $NamespaceManager)
    if ($null -eq $node) {
        throw "Manifest node not found: $XPath"
    }

    $node.Value = $Value
}

if (([string]::IsNullOrWhiteSpace($AppInstallerUri)) -xor ([string]::IsNullOrWhiteSpace($MainPackageUri))) {
    throw 'AppInstallerUri and MainPackageUri must either both be provided or both be omitted.'
}

if (-not $Unsigned) {
    if ([string]::IsNullOrWhiteSpace($PackageCertificatePath) -or [string]::IsNullOrWhiteSpace($PackageCertificatePassword)) {
        throw 'Signed package builds require PackageCertificatePath and PackageCertificatePassword.'
    }

    $PackageCertificatePassword = $PackageCertificatePassword.TrimEnd("`r", "`n")

    if ([string]::IsNullOrWhiteSpace($PackageCertificateTimestampUrl)) {
        $PackageCertificateTimestampUrl = $defaultTimestampUrl
        Write-Host "No timestamp URL was provided. Defaulting to $PackageCertificateTimestampUrl for package signing." -ForegroundColor Yellow
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$entryProjectPath = Join-Path $repoRoot 'src\ViscerealityCompanion.App\ViscerealityCompanion.App.csproj'
$packageProjectDir = Join-Path $repoRoot 'src\ViscerealityCompanion.App.Package'
$packageProjectPath = Join-Path $packageProjectDir 'ViscerealityCompanion.App.Package.wapproj'
$manifestPath = Join-Path $packageProjectDir 'Package.appxmanifest'
$appInstallerTemplatePath = Join-Path $packageProjectDir 'Package.appinstaller.template'
$appPackagesRoot = Join-Path $packageProjectDir 'AppPackages'
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))
$syncBundledSussexApkScriptPath = Join-Path $repoRoot 'tools\app\Sync-Bundled-Sussex-Apk.ps1'

if (-not (Test-Path $packageProjectPath)) {
    throw "Packaging project not found at $packageProjectPath"
}

if (-not (Test-Path $entryProjectPath)) {
    throw "Entry project not found at $entryProjectPath"
}

if (-not (Test-Path $manifestPath)) {
    throw "Package manifest not found at $manifestPath"
}

if ($RefreshBundledSussexApk -or -not [string]::IsNullOrWhiteSpace($BundledSussexApkSourcePath)) {
    if (-not (Test-Path $syncBundledSussexApkScriptPath)) {
        throw "Bundled Sussex APK sync script not found at $syncBundledSussexApkScriptPath"
    }

    $syncArgs = @{}
    if (-not [string]::IsNullOrWhiteSpace($BundledSussexApkSourcePath)) {
        $syncArgs['SourceApkPath'] = $BundledSussexApkSourcePath
    }

    if (-not [string]::IsNullOrWhiteSpace($BundledSussexApkVersion)) {
        $syncArgs['VersionName'] = $BundledSussexApkVersion
    }

    Write-Host 'Refreshing bundled Sussex APK metadata before packaging...' -ForegroundColor Cyan
    & $syncBundledSussexApkScriptPath @syncArgs
}

Initialize-DotNetSdkResolver -PreferredSdkVersion $PreferredDotNetSdkVersion

$preferredTargetPlatformVersion = $null
try {
    [xml]$packageProjectXml = Get-Content $packageProjectPath -Raw
    $packageProjectNamespaceManager = New-Object System.Xml.XmlNamespaceManager($packageProjectXml.NameTable)
    $packageProjectNamespaceManager.AddNamespace('msbuild', 'http://schemas.microsoft.com/developer/msbuild/2003')
    $targetPlatformVersionNode = $packageProjectXml.SelectSingleNode('//msbuild:TargetPlatformVersion', $packageProjectNamespaceManager)
    if ($null -ne $targetPlatformVersionNode) {
        $preferredTargetPlatformVersion = $targetPlatformVersionNode.InnerText
    }
}
catch {
}

$resolvedTargetPlatformVersion = Resolve-UapPlatformVersion -PreferredVersion $preferredTargetPlatformVersion

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
if (Test-Path $appPackagesRoot) {
    Remove-Item -Recurse -Force $appPackagesRoot
}

$originalPackageProjectBytes = [System.IO.File]::ReadAllBytes($packageProjectPath)
$originalManifest = Get-Content $manifestPath -Raw
$originalManifestBytes = [System.IO.File]::ReadAllBytes($manifestPath)

try {
    if (-not [string]::IsNullOrWhiteSpace($preferredTargetPlatformVersion) -and $preferredTargetPlatformVersion -ne $resolvedTargetPlatformVersion) {
        [xml]$packageProjectXml = Get-Content $packageProjectPath -Raw
        $packageProjectNamespaceManager = New-Object System.Xml.XmlNamespaceManager($packageProjectXml.NameTable)
        $packageProjectNamespaceManager.AddNamespace('msbuild', 'http://schemas.microsoft.com/developer/msbuild/2003')
        $targetPlatformVersionNode = $packageProjectXml.SelectSingleNode('//msbuild:TargetPlatformVersion', $packageProjectNamespaceManager)
        if ($null -eq $targetPlatformVersionNode) {
            throw "Package project node not found: //msbuild:TargetPlatformVersion"
        }

        $targetPlatformVersionNode.InnerText = $resolvedTargetPlatformVersion
        $packageProjectXml.Save($packageProjectPath)
        Write-Host "Using installed UAP SDK $resolvedTargetPlatformVersion for packaging because project target $preferredTargetPlatformVersion is not available on this machine." -ForegroundColor Yellow
    }
    else {
        Write-Host "Using UAP SDK $resolvedTargetPlatformVersion for packaging." -ForegroundColor Cyan
    }

    [xml]$manifest = $originalManifest
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
    $namespaceManager.AddNamespace('appx', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $namespaceManager.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')

    Set-ManifestValue -Manifest $manifest -XPath '/appx:Package/appx:Identity/@Name' -Value $PackageId -NamespaceManager $namespaceManager
    Set-ManifestValue -Manifest $manifest -XPath '/appx:Package/appx:Identity/@Publisher' -Value $Publisher -NamespaceManager $namespaceManager
    Set-ManifestValue -Manifest $manifest -XPath '/appx:Package/appx:Identity/@Version' -Value $Version -NamespaceManager $namespaceManager
    Set-ManifestValue -Manifest $manifest -XPath '/appx:Package/appx:Properties/appx:DisplayName/text()' -Value $DisplayName -NamespaceManager $namespaceManager
    Set-ManifestValue -Manifest $manifest -XPath '/appx:Package/appx:Properties/appx:PublisherDisplayName/text()' -Value $PublisherDisplayName -NamespaceManager $namespaceManager
    Set-ManifestValue -Manifest $manifest -XPath '/appx:Package/appx:Applications/appx:Application/uap:VisualElements/@DisplayName' -Value $DisplayName -NamespaceManager $namespaceManager
    $manifest.Save($manifestPath)

    Write-Host "Restoring ViscerealityCompanion.App for win-$Platform runtime packs..." -ForegroundColor Cyan
    dotnet restore $entryProjectPath -r "win-$Platform" | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed for $entryProjectPath with exit code $LASTEXITCODE"
    }

    $msbuildPath = Find-MSBuild
    $msbuildArgs = @(
        $packageProjectPath,
        '/restore',
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        '/p:UapAppxPackageBuildMode=SideLoadOnly',
        '/p:AppxBundle=Never',
        "/p:AppxPackageDir=$appPackagesRoot\",
        "/p:RuntimeIdentifier=win-$Platform",
        "/p:Version=$Version",
        "/p:AssemblyVersion=$Version",
        "/p:FileVersion=$Version",
        "/p:InformationalVersion=$Version",
        '/p:StageBundledCliOnBuild=True',
        "/p:GenerateAppInstallerFile=False",
        '/p:AppxPackageSigningEnabled=False'
    )

    Write-Host "Building MSIX package with $msbuildPath" -ForegroundColor Cyan
    & $msbuildPath @msbuildArgs | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "MSIX package build failed with exit code $LASTEXITCODE"
    }

    $builtPackage = Get-ChildItem -Path $appPackagesRoot -Recurse -Filter *.msix |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $builtPackage) {
        throw "No MSIX package was produced under $appPackagesRoot"
    }

    $packageOutputPath = Join-Path $outputPath $PackageFileName
    Copy-Item $builtPackage.FullName $packageOutputPath -Force

    if (-not $Unsigned) {
        $signToolPath = Find-WindowsSdkTool -ToolName 'signtool.exe'
        # Preserve the WAP-produced package as-is. Local validation on this
        # machine family showed that repacking the layout regressed packaged-app
        # launch admission under Windows code integrity for this repo.
        Write-Host "Signing package with $signToolPath" -ForegroundColor Cyan
        Invoke-SignToolSign `
            -SignToolPath $signToolPath `
            -CertificatePath $PackageCertificatePath `
            -CertificatePassword $PackageCertificatePassword `
            -TargetPath $packageOutputPath `
            -TimestampUrl $PackageCertificateTimestampUrl
    }

    Write-Host "Copied package to $packageOutputPath" -ForegroundColor Green

    if (-not [string]::IsNullOrWhiteSpace($AppInstallerUri)) {
        $template = Get-Content $appInstallerTemplatePath -Raw
        $appInstaller = $template.
            Replace('{AppInstallerUri}', $AppInstallerUri).
            Replace('{Version}', $Version).
            Replace('{Name}', $PackageId).
            Replace('{Publisher}', $Publisher).
            Replace('{ProcessorArchitecture}', $Platform).
            Replace('{MainPackageUri}', $MainPackageUri)

        $appInstallerOutputPath = Join-Path $outputPath $AppInstallerFileName
        Set-Content -Path $appInstallerOutputPath -Value $appInstaller -Encoding utf8
        Write-Host "Wrote App Installer file to $appInstallerOutputPath" -ForegroundColor Green
    }

    if (-not $Unsigned -and -not [string]::IsNullOrWhiteSpace($CertificateFileName)) {
        $resolvedCertificatePath = [System.IO.Path]::GetFullPath($PackageCertificatePath)
        $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $resolvedCertificatePath,
            $PackageCertificatePassword,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)

        try {
            $certificateBytes = $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
            $certificateOutputPath = Join-Path $outputPath $CertificateFileName
            [System.IO.File]::WriteAllBytes($certificateOutputPath, $certificateBytes)
            Write-Host "Exported public certificate to $certificateOutputPath" -ForegroundColor Green
        }
        finally {
            $certificate.Dispose()
        }
    }
}
finally {
    [System.IO.File]::WriteAllBytes($packageProjectPath, $originalPackageProjectBytes)
    [System.IO.File]::WriteAllBytes($manifestPath, $originalManifestBytes)
}
