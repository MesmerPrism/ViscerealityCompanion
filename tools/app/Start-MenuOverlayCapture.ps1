$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sessionRoot = Join-Path $repoRoot 'artifacts\verify\menu-overlay-capture'
$sessionId = Get-Date -Format 'yyyyMMdd-HHmmss'
$sessionDir = Join-Path $sessionRoot $sessionId
New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null

function Get-AdbSelectors {
    $output = adb devices
    if ($LASTEXITCODE -ne 0) {
        throw 'adb devices failed.'
    }

    return @(
        $output |
            Select-Object -Skip 1 |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ } |
            ForEach-Object {
                $parts = $_ -split '\s+'
                if ($parts.Length -lt 2 -or $parts[1] -ne 'device') {
                    return
                }

                $parts[0]
            }
    )
}

function Resolve-AdbSelector {
    $availableSelectors = @(Get-AdbSelectors)
    $sessionPath = Join-Path $env:LOCALAPPDATA 'ViscerealityCompanion\session\app-state.json'
    if (Test-Path $sessionPath) {
        try {
            $sessionState = Get-Content $sessionPath -Raw | ConvertFrom-Json
            foreach ($candidate in @($sessionState.ActiveEndpoint, $sessionState.LastUsbSerial)) {
                if (-not [string]::IsNullOrWhiteSpace($candidate) -and $availableSelectors -contains $candidate) {
                    return $candidate
                }
            }

            if (-not [string]::IsNullOrWhiteSpace($sessionState.ActiveEndpoint) -and $sessionState.ActiveEndpoint -like '*:*') {
                adb connect $sessionState.ActiveEndpoint | Out-Null
                $availableSelectors = @(Get-AdbSelectors)
                if ($availableSelectors -contains $sessionState.ActiveEndpoint) {
                    return $sessionState.ActiveEndpoint
                }
            }
        }
        catch {
            Write-Warning "Could not parse ${sessionPath}: $($_.Exception.Message)"
        }
    }

    if ($availableSelectors.Count -eq 0) {
        throw 'No active ADB devices are available.'
    }

    $wifiSelector = $availableSelectors | Where-Object { $_ -like '*:*' } | Select-Object -First 1
    if (-not [string]::IsNullOrWhiteSpace($wifiSelector)) {
        return $wifiSelector
    }

    return $availableSelectors[0]
}

$selector = Resolve-AdbSelector
$cliDll = Join-Path $repoRoot 'src\ViscerealityCompanion.Cli\bin\Release\net10.0\viscereality.dll'
$traceScript = Join-Path $PSScriptRoot 'Trace-AdbState.ps1'
$stopFile = Join-Path $sessionDir 'stop.txt'
$lslLog = Join-Path $sessionDir 'quest_twin_state.log'
$lslErr = Join-Path $sessionDir 'quest_twin_state.err.log'
$adbLog = Join-Path $sessionDir 'adb-state.jsonl'
$logcatLog = Join-Path $sessionDir 'quest_shell.logcat.txt'
$logcatErr = Join-Path $sessionDir 'quest_shell.logcat.err.txt'
$metadataPath = Join-Path $sessionDir 'session.json'
$latestPath = Join-Path $sessionRoot 'latest-session.json'

if (-not (Test-Path $cliDll)) {
    throw "CLI build output not found at $cliDll. Build the solution first."
}

if (-not (Test-Path $traceScript)) {
    throw "ADB trace script not found at $traceScript."
}

$lslProcess = Start-Process `
    -FilePath 'dotnet' `
    -ArgumentList @($cliDll, 'monitor', '--stream', 'quest_twin_state', '--type', 'quest.twin.state', '--channel', '0') `
    -WorkingDirectory (Split-Path $cliDll -Parent) `
    -RedirectStandardOutput $lslLog `
    -RedirectStandardError $lslErr `
    -WindowStyle Hidden `
    -PassThru

$adbProcess = Start-Process `
    -FilePath 'powershell' `
    -ArgumentList @('-ExecutionPolicy', 'Bypass', '-File', $traceScript, '-Selector', $selector, '-OutputPath', $adbLog, '-StopFile', $stopFile) `
    -WorkingDirectory $sessionDir `
    -WindowStyle Hidden `
    -PassThru

adb -s $selector logcat -c | Out-Null
$logcatProcess = Start-Process `
    -FilePath 'adb' `
    -ArgumentList @(
        '-s', $selector,
        'logcat',
        '-v', 'threadtime',
        'ActivityTaskManager:I',
        'ActivityManager:I',
        'InputDispatcher:I',
        'PhoneWindowManager:I',
        'VrShell:I',
        'SystemUI:I',
        '*:S'
    ) `
    -RedirectStandardOutput $logcatLog `
    -RedirectStandardError $logcatErr `
    -WindowStyle Hidden `
    -PassThru

$metadata = [ordered]@{
    sessionId = $sessionId
    startedAt = (Get-Date).ToString('o')
    selector = $selector
    sessionDir = $sessionDir
    stopFile = $stopFile
    lslLog = $lslLog
    lslErrorLog = $lslErr
    adbLog = $adbLog
    logcatLog = $logcatLog
    logcatErrorLog = $logcatErr
    lslPid = $lslProcess.Id
    adbPid = $adbProcess.Id
    logcatPid = $logcatProcess.Id
}

$metadata | ConvertTo-Json -Depth 5 | Set-Content -Path $metadataPath -Encoding UTF8
$metadata | ConvertTo-Json -Depth 5 | Set-Content -Path $latestPath -Encoding UTF8

Start-Sleep -Seconds 2

$lslPreview = if (Test-Path $lslLog) {
    Get-Content $lslLog -Tail 5 -ErrorAction SilentlyContinue
}
else {
    @()
}

$adbPreview = if (Test-Path $adbLog) {
    Get-Content $adbLog -Tail 3 -ErrorAction SilentlyContinue
}
else {
    @()
}

Write-Output "SessionDir: $sessionDir"
Write-Output "Selector: $selector"
Write-Output "StopFile: $stopFile"
Write-Output "LslPid: $($lslProcess.Id)"
Write-Output "AdbPid: $($adbProcess.Id)"
Write-Output "LogcatPid: $($logcatProcess.Id)"
Write-Output ''
Write-Output 'LSL preview:'
$lslPreview | ForEach-Object { Write-Output $_ }
Write-Output ''
Write-Output 'ADB preview:'
$adbPreview | ForEach-Object { Write-Output $_ }
