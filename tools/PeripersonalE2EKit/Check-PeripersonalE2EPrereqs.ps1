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

Add-Check $checks "PowerShell" ($PSVersionTable.PSVersion.Major -ge 5) "Detected PowerShell $($PSVersionTable.PSVersion)." "Install PowerShell 5.1 or newer."

$git = Get-CommandText "git"
Add-Check $checks "Git" ($null -ne $git) $(if ($git) { (& git --version) } else { "git was not found on PATH." }) "Install Git for Windows."

$dotnet = Test-DotnetSdk10
Add-Check $checks ".NET SDK 10" $dotnet.Ok $dotnet.Detail "Install a .NET 10 SDK, then rebuild ViscerealityCompanion.sln."

$java = Get-CommandText "java"
if ($java) {
    $javaVersion = (& java -version 2>&1) -join "; "
    Add-Check $checks "Java 17" ($javaVersion -match '"17\.') $javaVersion "Install JDK 17 if rebuilding the questionnaire panel."
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
    Add-Check $checks "Runtime APK" (Test-Path -LiteralPath $runtimeApk) $runtimeApk "Use the Peripersonal E2E kit zip."
    Add-Check $checks "Questionnaire panel APK" (Test-Path -LiteralPath $panelApk) $panelApk "Use the Peripersonal E2E kit zip."
    Add-Check $checks "Study shell" (Test-Path -LiteralPath $studyShell) $studyShell "Use the Peripersonal E2E kit zip."
    Add-Check $checks "Compatibility manifest" (Test-Path -LiteralPath $compatibility) $compatibility "Use the Peripersonal E2E kit zip."
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
    }
}

$checks | Format-Table -AutoSize

$failed = $checks | Where-Object { -not $_.Ok }
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
