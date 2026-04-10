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
    [string]$Version = '0.1.33.0',
    [string]$PackageId = 'MesmerPrism.ViscerealityCompanion',
    [string]$Publisher = 'CN=MesmerPrism',
    [string]$DisplayName = 'Viscereality Companion',
    [string]$PublisherDisplayName = 'Mesmer Prism',
    [string]$OutputRelativePath = 'artifacts\windows-installer',
    [string]$PackageFileName = 'ViscerealityCompanion.msix',
    [string]$AppInstallerFileName = 'ViscerealityCompanion.appinstaller',
    [string]$CertificateFileName = 'ViscerealityCompanion.cer',
    [string]$AppInstallerUri,
    [string]$MainPackageUri,
    [string]$PackageCertificatePath,
    [string]$PackageCertificatePassword,
    [string]$PackageCertificateTimestampUrl,
    [switch]$RefreshBundledSussexApk,
    [string]$BundledSussexApkSourcePath,
    [string]$BundledSussexApkVersion,
    [switch]$Unsigned
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Find-MSBuild {
    $command = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $wellKnownPaths = @(
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

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $matches = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe'
        if ($LASTEXITCODE -eq 0 -and $matches) {
            return $matches | Select-Object -First 1
        }

        $matches = & $vswhere -latest -products * -find 'MSBuild\**\Bin\MSBuild.exe'
        if ($LASTEXITCODE -eq 0 -and $matches) {
            return $matches | Select-Object -First 1
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

function Initialize-DotNetSdkResolver {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) {
        return
    }

    $dotnetRoot = Split-Path -Parent $dotnetCommand.Source
    $sdkRoot = Join-Path $dotnetRoot 'sdk'
    if (-not (Test-Path $sdkRoot)) {
        return
    }

    $sdkDir = Get-ChildItem -Path $sdkRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^10\.' -and (Test-Path (Join-Path $_.FullName 'Sdks')) } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($null -eq $sdkDir) {
        return
    }

    $env:DOTNET_ROOT = $dotnetRoot
    $env:DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR = $dotnetRoot
    $env:DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR = Join-Path $sdkDir.FullName 'Sdks'
    $env:DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER = $sdkDir.Name
}

function Publish-PackagedAppPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$Configuration,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier,
        [Parameter(Mandatory = $true)]
        [string]$OutputPath
    )

    if (Test-Path $OutputPath) {
        Remove-Item -Path $OutputPath -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

    $publishArgs = @(
        'publish',
        $ProjectPath,
        '-c', $Configuration,
        '-r', $RuntimeIdentifier,
        '-p:PublishSingleFile=true',
        '-p:SelfContained=true',
        '-o', $OutputPath
    )

    Write-Host "Publishing packaged desktop payload to $OutputPath" -ForegroundColor Cyan
    & dotnet @publishArgs | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for packaged payload with exit code $LASTEXITCODE"
    }

    $exePath = Join-Path $OutputPath 'ViscerealityCompanion.exe'
    if (-not (Test-Path $exePath)) {
        throw "Packaged payload publish did not produce $exePath"
    }
}

function Repack-WithPublishedPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePackagePath,
        [Parameter(Mandatory = $true)]
        [string]$PublishedPayloadPath,
        [Parameter(Mandatory = $true)]
        [string]$OutputPackagePath,
        [Parameter(Mandatory = $true)]
        [bool]$Unsigned,
        [string]$PackageCertificatePath,
        [string]$PackageCertificatePassword,
        [string]$PackageCertificateTimestampUrl
    )

    $makeAppxPath = Find-WindowsSdkTool -ToolName 'makeappx.exe'
    $stageRoot = Join-Path ([System.IO.Path]::GetDirectoryName($OutputPackagePath)) ("_msix-stage-" + [guid]::NewGuid().ToString('N'))

    try {
        if (Test-Path $stageRoot) {
            Remove-Item -Path $stageRoot -Recurse -Force
        }

        New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null

        Write-Host "Unpacking MSIX scaffold to $stageRoot" -ForegroundColor Cyan
        & $makeAppxPath unpack /p $BasePackagePath /d $stageRoot /o | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "makeappx unpack failed with exit code $LASTEXITCODE"
        }

        $packagedAppPath = Join-Path $stageRoot 'ViscerealityCompanion.App'
        if (Test-Path $packagedAppPath) {
            Remove-Item -Path $packagedAppPath -Recurse -Force
        }

        New-Item -ItemType Directory -Force -Path $packagedAppPath | Out-Null
        Copy-Item -Path (Join-Path $PublishedPayloadPath '*') -Destination $packagedAppPath -Recurse -Force

        if (Test-Path $OutputPackagePath) {
            Remove-Item -Path $OutputPackagePath -Force
        }

        Write-Host "Packing single-file MSIX payload to $OutputPackagePath" -ForegroundColor Cyan
        & $makeAppxPath pack /d $stageRoot /p $OutputPackagePath /o | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "makeappx pack failed with exit code $LASTEXITCODE"
        }

        if (-not $Unsigned) {
            $signToolPath = Find-WindowsSdkTool -ToolName 'signtool.exe'
            $signArgs = @(
                'sign',
                '/fd', 'SHA256',
                '/f', [System.IO.Path]::GetFullPath($PackageCertificatePath),
                '/p', $PackageCertificatePassword
            )

            if (-not [string]::IsNullOrWhiteSpace($PackageCertificateTimestampUrl)) {
                $signArgs += @('/tr', $PackageCertificateTimestampUrl, '/td', 'SHA256')
            }

            $signArgs += $OutputPackagePath

            Write-Host "Signing repacked MSIX with $signToolPath" -ForegroundColor Cyan
            & $signToolPath @signArgs | Out-Host
            if ($LASTEXITCODE -ne 0) {
                throw "signtool sign failed with exit code $LASTEXITCODE"
            }
        }
    }
    finally {
        if (Test-Path $stageRoot) {
            Remove-Item -Path $stageRoot -Recurse -Force
        }
    }
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
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$entryProjectPath = Join-Path $repoRoot 'src\ViscerealityCompanion.App\ViscerealityCompanion.App.csproj'
$packageProjectDir = Join-Path $repoRoot 'src\ViscerealityCompanion.App.Package'
$packageProjectPath = Join-Path $packageProjectDir 'ViscerealityCompanion.App.Package.wapproj'
$manifestPath = Join-Path $packageProjectDir 'Package.appxmanifest'
$appInstallerTemplatePath = Join-Path $packageProjectDir 'Package.appinstaller.template'
$appPackagesRoot = Join-Path $packageProjectDir 'AppPackages'
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRelativePath))
$publishedPayloadPath = Join-Path $env:TEMP ("ViscerealityCompanion-PackagedPayload-" + [guid]::NewGuid().ToString('N'))
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

Initialize-DotNetSdkResolver

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
if (Test-Path $appPackagesRoot) {
    Remove-Item -Recurse -Force $appPackagesRoot
}

$originalManifest = Get-Content $manifestPath -Raw
$originalManifestBytes = [System.IO.File]::ReadAllBytes($manifestPath)

try {
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
        "/p:GenerateAppInstallerFile=False",
        "/p:AppxPackageSigningEnabled=$([bool](-not $Unsigned))"
    )

    if (-not $Unsigned) {
        $resolvedCertificatePath = [System.IO.Path]::GetFullPath($PackageCertificatePath)
        $msbuildArgs += "/p:PackageCertificateKeyFile=$resolvedCertificatePath"
        $msbuildArgs += "/p:PackageCertificatePassword=$PackageCertificatePassword"

        if (-not [string]::IsNullOrWhiteSpace($PackageCertificateTimestampUrl)) {
            $msbuildArgs += "/p:PackageCertificateTimestampUrl=$PackageCertificateTimestampUrl"
        }
    }

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
    Publish-PackagedAppPayload `
        -ProjectPath $entryProjectPath `
        -Configuration $Configuration `
        -RuntimeIdentifier "win-$Platform" `
        -OutputPath $publishedPayloadPath

    Repack-WithPublishedPayload `
        -BasePackagePath $builtPackage.FullName `
        -PublishedPayloadPath $publishedPayloadPath `
        -OutputPackagePath $packageOutputPath `
        -Unsigned ([bool]$Unsigned) `
        -PackageCertificatePath $PackageCertificatePath `
        -PackageCertificatePassword $PackageCertificatePassword `
        -PackageCertificateTimestampUrl $PackageCertificateTimestampUrl

    Write-Host "Wrote single-file package to $packageOutputPath" -ForegroundColor Green

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
    if (Test-Path $publishedPayloadPath) {
        Remove-Item -Path $publishedPayloadPath -Recurse -Force
    }

    [System.IO.File]::WriteAllBytes($manifestPath, $originalManifestBytes)
}
