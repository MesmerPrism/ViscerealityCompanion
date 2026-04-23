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
    [string]$ResultsDirectory = 'artifacts\test-results\signed-dotnet-test',
    [string]$TrxLogFileName = 'signed-dotnet-test.trx',
    [string]$BlameHangTimeout = '5m',
    [ValidateRange(5, 3600)]
    [int]$HeartbeatSeconds = 30,
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
    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
        [System.Security.Cryptography.X509Certificates.StoreName]::My,
        [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)

    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        $match = $store.Certificates |
            Where-Object { $_.Thumbprint -eq $normalized } |
            Select-Object -First 1
    }
    finally {
        $store.Close()
    }

    if ($null -eq $match) {
        throw "Signing certificate $normalized was not found in the CurrentUser\My certificate store."
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

function Test-FileHasAuthenticodeSigner {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    try {
        [System.Security.Cryptography.X509Certificates.X509Certificate]::CreateFromSignedFile($TargetPath) | Out-Null
        return $true
    }
    catch {
        return $false
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
            Where-Object { -not (Test-FileHasAuthenticodeSigner -TargetPath $_.FullName) }
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

function Test-ArgumentSwitchPresent {
    param(
        [AllowNull()]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string[]]$Switches
    )

    if ($null -eq $Arguments -or $Arguments.Count -eq 0) {
        return $false
    }

    foreach ($argument in $Arguments) {
        foreach ($switch in $Switches) {
            if ($argument -eq $switch -or $argument.StartsWith("$switch`:", [StringComparison]::OrdinalIgnoreCase) -or $argument.StartsWith("$switch=", [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
    }

    return $false
}

function Test-ArgumentValueContains {
    param(
        [AllowNull()]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$Needle
    )

    if ($null -eq $Arguments -or $Arguments.Count -eq 0) {
        return $false
    }

    foreach ($argument in $Arguments) {
        if ($argument.IndexOf($Needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

function Invoke-WatchedDotNetCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DisplayName,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,
        [Parameter(Mandatory = $true)]
        [string]$OutputPath,
        [Parameter(Mandatory = $true)]
        [int]$HeartbeatSeconds
    )

    function ConvertTo-CmdArgument {
        param(
            [AllowNull()]
            [string]$Argument
        )

        if ([string]::IsNullOrEmpty($Argument)) {
            return '""'
        }

        if ($Argument -notmatch '[\s"&|<>^]') {
            return $Argument
        }

        return '"' + ($Argument -replace '"', '\"') + '"'
    }

    $outputDirectory = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    }

    $errorPath = Join-Path $outputDirectory "$([System.IO.Path]::GetFileNameWithoutExtension($OutputPath)).stderr.log"
    $exitCodePath = Join-Path $outputDirectory "$([System.IO.Path]::GetFileNameWithoutExtension($OutputPath)).exitcode"
    Remove-Item -LiteralPath $OutputPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $errorPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $exitCodePath -Force -ErrorAction SilentlyContinue
    $started = Get-Date
    Write-Host "Running dotnet $($Arguments -join ' ')" -ForegroundColor Cyan

    $dotnetCommand = "dotnet $((@($Arguments) | ForEach-Object { ConvertTo-CmdArgument $_ }) -join ' ')"
    $command = "$dotnetCommand > $(ConvertTo-CmdArgument $OutputPath) 2> $(ConvertTo-CmdArgument $errorPath) & echo %ERRORLEVEL% > $(ConvertTo-CmdArgument $exitCodePath)"
    $process = Start-Process -FilePath $env:ComSpec -ArgumentList @('/d', '/s', '/c', "`"$command`"") -WorkingDirectory $WorkingDirectory -PassThru -NoNewWindow
    try {
        while (-not $process.WaitForExit($HeartbeatSeconds * 1000)) {
            $process.Refresh()
            $elapsed = [int]((Get-Date) - $started).TotalSeconds
            $outputBytes = if (Test-Path -LiteralPath $OutputPath) { (Get-Item -LiteralPath $OutputPath).Length } else { 0 }
            $errorBytes = if (Test-Path -LiteralPath $errorPath) { (Get-Item -LiteralPath $errorPath).Length } else { 0 }
            Write-Host ("{0} still running: {1}s elapsed, stdout={2} bytes, stderr={3} bytes" -f $DisplayName, $elapsed, $outputBytes, $errorBytes) -ForegroundColor DarkCyan
        }

        $process.WaitForExit()
        if (Test-Path -LiteralPath $OutputPath) {
            Get-Content -LiteralPath $OutputPath | Out-Host
        }
        if (Test-Path -LiteralPath $errorPath) {
            Get-Content -LiteralPath $errorPath | Out-Host
        }

        if (-not (Test-Path -LiteralPath $exitCodePath)) {
            throw "dotnet $DisplayName finished but did not write an exit code. Full output: $OutputPath; stderr: $errorPath"
        }

        $exitCodeText = (Get-Content -LiteralPath $exitCodePath -Raw).Trim()
        $exitCode = [int]$exitCodeText
        if ($exitCode -ne 0) {
            throw "dotnet $DisplayName failed with exit code $exitCode. Full output: $OutputPath; stderr: $errorPath"
        }
    }
    finally {
        if ($null -ne $process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$resolvedTarget = if ([System.IO.Path]::IsPathRooted($Target)) { $Target } else { Join-Path $repoRoot $Target }
$testsRoot = Join-Path $repoRoot 'tests'
$resolvedResultsDirectory = if ([System.IO.Path]::IsPathRooted($ResultsDirectory)) { $ResultsDirectory } else { Join-Path $repoRoot $ResultsDirectory }
$certificate = Get-SigningCertificate -Thumbprint $SigningThumbprint
$signToolPath = Find-SignToolPath
$normalizedThumbprint = $certificate.Thumbprint.ToUpperInvariant()

if (-not (Test-Path $resolvedTarget)) {
    throw "Test target not found at $resolvedTarget"
}

if (-not $SkipBuild) {
    $buildLogPath = Join-Path $resolvedResultsDirectory 'signed-dotnet-build.log'
    Invoke-WatchedDotNetCommand -DisplayName 'build' -Arguments @('build', $resolvedTarget, '-c', $Configuration) -WorkingDirectory $repoRoot -OutputPath $buildLogPath -HeartbeatSeconds $HeartbeatSeconds
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

if (-not (Test-ArgumentSwitchPresent -Arguments $DotNetTestArgs -Switches @('--results-directory'))) {
    $testArgs += @('--results-directory', $resolvedResultsDirectory)
}

if (-not (Test-ArgumentValueContains -Arguments $DotNetTestArgs -Needle 'trx')) {
    $testArgs += @('--logger', "trx;LogFileName=$TrxLogFileName")
}

if (-not (Test-ArgumentSwitchPresent -Arguments $DotNetTestArgs -Switches @('--blame-hang'))) {
    $testArgs += '--blame-hang'
}

if (-not (Test-ArgumentSwitchPresent -Arguments $DotNetTestArgs -Switches @('--blame-hang-timeout'))) {
    $testArgs += @('--blame-hang-timeout', $BlameHangTimeout)
}

if ($null -ne $DotNetTestArgs -and $DotNetTestArgs.Count -gt 0) {
    $testArgs += $DotNetTestArgs
}

$testLogPath = Join-Path $resolvedResultsDirectory 'signed-dotnet-test.log'
Invoke-WatchedDotNetCommand -DisplayName 'test' -Arguments $testArgs -WorkingDirectory $repoRoot -OutputPath $testLogPath -HeartbeatSeconds $HeartbeatSeconds
