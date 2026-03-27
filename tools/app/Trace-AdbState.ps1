param(
    [Parameter(Mandatory = $true)]
    [string]$Selector,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [Parameter(Mandatory = $true)]
    [string]$StopFile,
    [int]$IntervalMs = 400
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-FirstMatchValue {
    param(
        [string[]]$Lines,
        [string]$Prefix
    )

    foreach ($line in $Lines) {
        $trimmedLine = $line.Trim()
        if ($trimmedLine.StartsWith($Prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $trimmedLine.Substring($Prefix.Length).Trim()
        }
    }

    return $null
}

function Get-PatternLines {
    param(
        [string[]]$Lines,
        [string]$Pattern
    )

    return @(
        $Lines |
            Select-String -Pattern $Pattern |
            ForEach-Object { $_.Line.Trim() }
    )
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
"# ADB state trace started $(Get-Date -Format o) for $Selector" | Set-Content -Path $OutputPath -Encoding UTF8

while (-not (Test-Path $StopFile)) {
    $timestamp = Get-Date
    $powerOutput = @(adb -s $Selector shell dumpsys power 2>&1)
    $powerExitCode = $LASTEXITCODE
    $activityOutput = @(adb -s $Selector shell dumpsys activity activities 2>&1)
    $activityExitCode = $LASTEXITCODE

    $powerLines = @($powerOutput | ForEach-Object { "$_".Trim() })
    $activityLines = @($activityOutput | ForEach-Object { "$_".Trim() })

    $record = [ordered]@{
        timestamp = $timestamp.ToString('o')
        selector = $Selector
        powerExitCode = $powerExitCode
        activityExitCode = $activityExitCode
        wakefulness = Get-FirstMatchValue -Lines $powerLines -Prefix 'mWakefulness='
        interactive = Get-FirstMatchValue -Lines $powerLines -Prefix 'mInteractive='
        halInteractive = Get-FirstMatchValue -Lines $powerLines -Prefix 'mHalInteractiveModeEnabled='
        displayPower = @(
            $powerLines |
                Select-String -Pattern 'Display Power:|mState=' |
                ForEach-Object { $_.Line.Trim() }
        )
        resumedActivity = Get-PatternLines -Lines $activityLines -Pattern 'ResumedActivity:'
        topResumedActivity = Get-PatternLines -Lines $activityLines -Pattern 'topResumedActivity='
        focusedApp = Get-PatternLines -Lines $activityLines -Pattern 'mFocusedApp='
        currentFocus = Get-PatternLines -Lines $activityLines -Pattern 'mCurrentFocus='
        focusWindow = Get-PatternLines -Lines $activityLines -Pattern 'mFocusedWindow='
    }

    ($record | ConvertTo-Json -Compress -Depth 5) | Add-Content -Path $OutputPath -Encoding UTF8
    Start-Sleep -Milliseconds $IntervalMs
}

"# ADB state trace stopped $(Get-Date -Format o)" | Add-Content -Path $OutputPath -Encoding UTF8
