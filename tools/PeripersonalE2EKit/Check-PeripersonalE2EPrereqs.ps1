param(
    [string]$CompanionRepoRoot = "",
    [string]$BundleRoot = "",
    [string]$Device = ""
)

$ErrorActionPreference = "Stop"

function Add-Check {
    param(
        [System.Collections.Generic.List[object]]$Checks,
        [string]$Name,
        [bool]$Ok,
        [string]$Detail,
        [string]$Fix = ""
    )

    $Checks.Add([pscustomobject]@{
        Check = $Name
        Ok = $Ok
        Detail = $Detail
        Fix = $Fix
    }) | Out-Null
}

function Resolve-OptionalPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    try {
        return (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    } catch {
        return $null
    }
}

function Get-CommandText {
    param([string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        return $null
    }

    return $command.Source
}

function Test-DotnetSdk10 {
    $dotnet = Get-CommandText "dotnet"
    if ($null -eq $dotnet) {
        return @{ Ok = $false; Detail = "dotnet was not found on PATH." }
    }

    $sdks = & dotnet --list-sdks 2>&1
    $hasSdk10 = $sdks | Where-Object { $_ -match "^10\." }
    return @{
        Ok = [bool]$hasSdk10
        Detail = ($sdks -join "; ")
    }
}

$checks = [System.Collections.Generic.List[object]]::new()
$cli = $null

Add-Check $checks "PowerShell" ($PSVersionTable.PSVersion.Major -ge 5) "Detected PowerShell $($PSVersionTable.PSVersion)." "Install PowerShell 5.1 or newer."

$git = Get-CommandText "git"
Add-Check $checks "Git" ($null -ne $git) $(if ($git) { (& git --version) } else { "git was not found on PATH." }) "Install Git for Windows."

$dotnet = Test-DotnetSdk10
Add-Check $checks ".NET SDK 10" $dotnet.Ok $dotnet.Detail "Install a .NET 10 SDK, then rebuild ViscerealityCompanion.sln."

$java = Get-CommandText "java"
if ($java) {
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $javaVersion = (& java -version 2>&1) -join "; "
    $javaExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    Add-Check $checks "Java 17" ($javaExitCode -eq 0 -and $javaVersion -match '"17\.') $javaVersion "Install JDK 17 if rebuilding the questionnaire panel."
} else {
    Add-Check $checks "Java 17" $false "java was not found on PATH." "Install JDK 17 if rebuilding the questionnaire panel. It is not required for installing the bundled APKs."
}

$repo = Resolve-OptionalPath $CompanionRepoRoot
if ($repo) {
    $cli = Join-Path $repo "src\ViscerealityCompanion.Cli\bin\Debug\net10.0\viscereality.exe"
    $sln = Join-Path $repo "ViscerealityCompanion.sln"
    Add-Check $checks "Companion solution" (Test-Path -LiteralPath $sln) $sln "Clone the Companion branch and run dotnet build."
    Add-Check $checks "Companion CLI build" (Test-Path -LiteralPath $cli) $cli "Run dotnet build .\ViscerealityCompanion.sln /p:UseSharedCompilation=false."

    if (Test-Path -LiteralPath $cli) {
        $tooling = & $cli tooling status 2>&1
        Add-Check $checks "Companion tooling status" ($LASTEXITCODE -eq 0) (($tooling | Select-Object -First 8) -join "; ") "Run viscereality tooling install-official."
    }
} else {
    Add-Check $checks "Companion repo" $false "No CompanionRepoRoot was provided or the path does not exist." "Pass -CompanionRepoRoot C:\path\to\ViscerealityCompanion."
}

$bundle = Resolve-OptionalPath $BundleRoot
if ($bundle) {
    $runtimeApk = Join-Path $bundle "quest-session-kit\APKs\PeripersonalRuntime.apk"
    $panelApk = Join-Path $bundle "apks\QuestQuestionnairePanel-labUpdater-debug.apk"
    $studyShell = Join-Path $bundle "study-shells\peripersonal-space.json"
    $compatibility = Join-Path $bundle "quest-session-kit\APKs\compatibility.json"
    $visualTemplate = Join-Path $bundle "study-shells\peripersonal-space\templates\visual-tuning-v1.template.json"
    $controllerTemplate = Join-Path $bundle "study-shells\peripersonal-space\templates\controller-breathing-tuning-v1.template.json"
    $visualProfiles = @(
        "peripersonal-left-visible.json",
        "peripersonal-left-anchor-only.json",
        "peripersonal-right-visible.json",
        "peripersonal-right-anchor-only.json"
    ) | ForEach-Object { Join-Path $bundle "study-shells\peripersonal-space\visual-profiles\$_" }
    $controllerProfiles = @(
        Join-Path $bundle "study-shells\peripersonal-space\controller-breathing-profiles\peripersonal-runtime-default.json"
    )
    Add-Check $checks "Runtime APK" (Test-Path -LiteralPath $runtimeApk) $runtimeApk "Use the Peripersonal E2E kit zip."
    Add-Check $checks "Questionnaire panel APK" (Test-Path -LiteralPath $panelApk) $panelApk "Use the Peripersonal E2E kit zip."
    Add-Check $checks "Study shell" (Test-Path -LiteralPath $studyShell) $studyShell "Use the Peripersonal E2E kit zip."
    Add-Check $checks "Compatibility manifest" (Test-Path -LiteralPath $compatibility) $compatibility "Use the Peripersonal E2E kit zip."
    Add-Check $checks "Visual tuning template" (Test-Path -LiteralPath $visualTemplate) $visualTemplate "Use a kit that includes study-shells\peripersonal-space\templates."
    Add-Check $checks "Controller-breathing template" (Test-Path -LiteralPath $controllerTemplate) $controllerTemplate "Use a kit that includes study-shells\peripersonal-space\templates."
    Add-Check $checks "Peripersonal visual profiles" (($visualProfiles | Where-Object { -not (Test-Path -LiteralPath $_) }).Count -eq 0) (($visualProfiles | ForEach-Object { Split-Path $_ -Leaf }) -join ", ") "Use a kit that includes all four Peripersonal condition visual profiles."
    Add-Check $checks "Peripersonal controller profile" (($controllerProfiles | Where-Object { -not (Test-Path -LiteralPath $_) }).Count -eq 0) (($controllerProfiles | ForEach-Object { Split-Path $_ -Leaf }) -join ", ") "Use a kit that includes the Peripersonal runtime controller-breathing profile."

    if ($cli -and (Test-Path -LiteralPath $cli)) {
        $studyRoot = Join-Path $bundle "study-shells"
        $conditions = & $cli study-profile condition list --study peripersonal-space --root $studyRoot --active-only --json 2>&1
        $resolved = $LASTEXITCODE -eq 0 -and
            (($conditions -join "`n") -match "Peripersonal Left Visible") -and
            (($conditions -join "`n") -match "Peripersonal Runtime Default")
        Add-Check $checks "Peripersonal condition/profile resolution" $resolved (($conditions | Select-Object -First 12) -join "; ") "Build the Companion CLI and use a kit with bundled Peripersonal profiles."
    }
} else {
    Add-Check $checks "Bundle root" $false "No BundleRoot was provided or the path does not exist." "Pass -BundleRoot C:\path\to\peripersonal-e2e-kit."
}

$adb = $null
$adbCandidates = @(
    $env:VISCEREALITY_ADB_EXE,
    (Get-CommandText "adb"),
    (Join-Path $env:LOCALAPPDATA "ViscerealityCompanion\tooling\platform-tools\current\platform-tools\adb.exe")
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

foreach ($candidate in $adbCandidates) {
    if (Test-Path -LiteralPath $candidate) {
        $adb = $candidate
        break
    }
}

Add-Check $checks "ADB available" ($null -ne $adb) $(if ($adb) { $adb } else { "adb was not found on PATH or in the Companion managed tool cache." }) "Run viscereality tooling install-official."

if ($adb) {
    $devices = & $adb devices -l 2>&1
    Add-Check $checks "ADB device list" ($LASTEXITCODE -eq 0) (($devices | Select-Object -First 8) -join "; ") "Authorize USB debugging on the Quest, or run viscereality connect <ip>:5555."

    if (-not [string]::IsNullOrWhiteSpace($Device)) {
        $state = & $adb -s $Device get-state 2>&1
        Add-Check $checks "Requested device state" ($LASTEXITCODE -eq 0 -and ($state -join "") -match "device") (($state | Select-Object -First 4) -join "; ") "Check the serial/IP:port and reconnect ADB."

        $wifi = & $adb -s $Device shell ip addr show wlan0 2>&1
        $wifiText = ($wifi | Select-Object -First 12) -join "; "
        $hasWifiIpv4 = $LASTEXITCODE -eq 0 -and (($wifi -join "`n") -match "\binet\s+\d+\.\d+\.\d+\.\d+")
        Add-Check $checks "Quest Wi-Fi IPv4 for LSL" $hasWifiIpv4 $wifiText "Connect the Quest to the same Wi-Fi/LAN as the Windows operator PC before running LSL-backed Peripersonal workflow commands."
    }
}

$checks | Format-Table -AutoSize

$failed = @($checks | Where-Object { -not $_.Ok })
if ($failed.Count -gt 0) {
    Write-Host ""
    Write-Host "Missing or incomplete prerequisites:" -ForegroundColor Yellow
    $failed | ForEach-Object {
        Write-Host "- $($_.Check): $($_.Fix)"
    }
    exit 1
}

Write-Host ""
Write-Host "All checked prerequisites passed." -ForegroundColor Green
