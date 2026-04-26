[CmdletBinding()]
param(
    [string]$PackageName = "MesmerPrism.ViscerealityCompanion",
    [string]$OutputRoot = (Join-Path (Get-Location) "artifacts\verify\consumer-install-smoke"),
    [switch]$SkipConditionEditRoundTrip
)

$ErrorActionPreference = "Stop"

function Invoke-ConsumerCli {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$CliPath,

        [Parameter(Mandatory = $true)]
        [string]$WorkspaceRoot,

        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory
    )

    $stdoutPath = Join-Path $OutputDirectory "$Name.out.json"
    $stderrPath = Join-Path $OutputDirectory "$Name.err.txt"
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $CliPath
    $startInfo.WorkingDirectory = $WorkspaceRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.Arguments = Join-ProcessArguments $Arguments

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    Set-Content -Path $stdoutPath -Value $stdout -NoNewline
    Set-Content -Path $stderrPath -Value $stderr -NoNewline

    if ($process.ExitCode -ne 0) {
        throw "CLI command '$Name' failed with exit code $($process.ExitCode). stderr: $stderr"
    }

    return $stdout
}

function Join-ProcessArguments {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    return ($Arguments | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join " "
}

function ConvertTo-ProcessArgument {
    param(
        [AllowNull()]
        [string]$Argument
    )

    if ($null -eq $Argument) {
        return '""'
    }

    if ($Argument.Length -eq 0) {
        return '""'
    }

    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.Append('"')
    $backslashCount = 0
    foreach ($character in $Argument.ToCharArray()) {
        if ($character -eq '\') {
            $backslashCount++
            continue
        }

        if ($character -eq '"') {
            [void]$builder.Append('\' * (($backslashCount * 2) + 1))
            [void]$builder.Append('"')
            $backslashCount = 0
            continue
        }

        if ($backslashCount -gt 0) {
            [void]$builder.Append('\' * $backslashCount)
            $backslashCount = 0
        }

        [void]$builder.Append($character)
    }

    if ($backslashCount -gt 0) {
        [void]$builder.Append('\' * ($backslashCount * 2))
    }

    [void]$builder.Append('"')
    return $builder.ToString()
}

function Get-ControlValue {
    param(
        [Parameter(Mandatory = $true)]
        $Profile,

        [Parameter(Mandatory = $true)]
        [string]$Id
    )

    $control = $Profile.controls | Where-Object { $_.id -eq $Id } | Select-Object -First 1
    if ($null -eq $control) {
        throw "Profile '$($Profile.name)' does not contain control '$Id'."
    }

    return [string]$control.value
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Actual,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Expected
    )

    if ($Actual -ne $Expected) {
        throw "$Name expected '$Expected' but was '$Actual'."
    }
}

if (Test-Path $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$OutputRoot = (Resolve-Path $OutputRoot).Path

$package = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue | Sort-Object Version -Descending | Select-Object -First 1
if ($null -eq $package) {
    throw "Package '$PackageName' is not installed."
}

$operatorDataRoot = Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)\LocalCache\Local\ViscerealityCompanion"
$workspaceRoot = Join-Path $operatorDataRoot "agent-workspace"
$cliPath = Join-Path $workspaceRoot "cli\current\Viscereality CLI.exe"
if (-not (Test-Path $cliPath)) {
    throw "Packaged agent-workspace CLI was not found at '$cliPath'. Open the packaged app once so it can refresh the local workspace mirror."
}

$process = Get-Process -Name "ViscerealityCompanion" -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$($package.InstallLocation)*" } |
    Select-Object -First 1
$processId = $null
$windowTitle = $null
if ($null -ne $process) {
    $processId = $process.Id
    $windowTitle = $process.MainWindowTitle
}

$conditionList = @(Invoke-ConsumerCli `
        -Name "condition-list" `
        -CliPath $cliPath `
        -WorkspaceRoot $workspaceRoot `
        -OutputDirectory $OutputRoot `
        -Arguments @("sussex", "condition", "list", "--json") |
    ConvertFrom-Json)
$conditionItems = @($conditionList | ForEach-Object { $_ })
$fixedCondition = Invoke-ConsumerCli `
    -Name "condition-fixed" `
    -CliPath $cliPath `
    -WorkspaceRoot $workspaceRoot `
    -OutputDirectory $OutputRoot `
    -Arguments @("sussex", "condition", "show", "fixed-radius-no-orbit") |
    ConvertFrom-Json
$fixedVisual = Invoke-ConsumerCli `
    -Name "visual-fixed" `
    -CliPath $cliPath `
    -WorkspaceRoot $workspaceRoot `
    -OutputDirectory $OutputRoot `
    -Arguments @("sussex", "visual", "show", "condition-fixed-radius-no-orbit", "--json") |
    ConvertFrom-Json
$currentVisual = Invoke-ConsumerCli `
    -Name "visual-current" `
    -CliPath $cliPath `
    -WorkspaceRoot $workspaceRoot `
    -OutputDirectory $OutputRoot `
    -Arguments @("sussex", "visual", "show", "condition-current-visual", "--json") |
    ConvertFrom-Json
$fixedBreathing = Invoke-ConsumerCli `
    -Name "breathing-fixed" `
    -CliPath $cliPath `
    -WorkspaceRoot $workspaceRoot `
    -OutputDirectory $OutputRoot `
    -Arguments @("sussex", "controller", "show", "condition-fixed-radius-breathing", "--json") |
    ConvertFrom-Json

$activeConditionIds = @($conditionItems | Where-Object { $_.is_active } | ForEach-Object { $_.id })
if ($activeConditionIds -notcontains "current") {
    throw "Active condition list did not include 'current'."
}

if ($activeConditionIds -notcontains "fixed-radius-no-orbit") {
    throw "Active condition list did not include 'fixed-radius-no-orbit'."
}

Assert-Equal -Name "Fixed condition visual profile" -Actual ([string]$fixedCondition.visual_profile_id) -Expected "condition-fixed-radius-no-orbit"
Assert-Equal -Name "Fixed condition breathing profile" -Actual ([string]$fixedCondition.controller_breathing_profile_id) -Expected "condition-fixed-radius-breathing"
Assert-Equal -Name "Fixed orbit minimum" -Actual (Get-ControlValue $fixedVisual "orbit_distance_min") -Expected "0"
Assert-Equal -Name "Fixed orbit maximum" -Actual (Get-ControlValue $fixedVisual "orbit_distance_max") -Expected "0"
Assert-Equal -Name "Fixed sphere radius minimum" -Actual (Get-ControlValue $fixedVisual "sphere_radius_min") -Expected "2"
Assert-Equal -Name "Fixed sphere radius maximum" -Actual (Get-ControlValue $fixedVisual "sphere_radius_max") -Expected "2"
Assert-Equal -Name "Current orbit envelope" -Actual "$(Get-ControlValue $currentVisual "orbit_distance_min")..$(Get-ControlValue $currentVisual "orbit_distance_max")" -Expected "0.2..1.5"
Assert-Equal -Name "Current sphere radius envelope" -Actual "$(Get-ControlValue $currentVisual "sphere_radius_min")..$(Get-ControlValue $currentVisual "sphere_radius_max")" -Expected "1..3"

$roundTrip = $null
if (-not $SkipConditionEditRoundTrip) {
    $roundTripRoot = Join-Path $OutputRoot "condition-roundtrip"
    $conditionRoot = Join-Path $roundTripRoot "library"
    $importRoot = Join-Path $roundTripRoot "imported-library"
    $exportPath = Join-Path $roundTripRoot "cli-temp-condition-export.json"
    New-Item -ItemType Directory -Path $conditionRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $importRoot -Force | Out-Null

    $created = Invoke-ConsumerCli `
        -Name "roundtrip-create" `
        -CliPath $cliPath `
        -WorkspaceRoot $workspaceRoot `
        -OutputDirectory $OutputRoot `
        -Arguments @("sussex", "condition", "--condition-root", $conditionRoot, "create", "--id", "cli-temp-condition", "--label", "CLI Temp Condition", "--visual", "condition-current-visual", "--breathing", "condition-current-breathing", "--active", "--json") |
        ConvertFrom-Json
    $updated = Invoke-ConsumerCli `
        -Name "roundtrip-update" `
        -CliPath $cliPath `
        -WorkspaceRoot $workspaceRoot `
        -OutputDirectory $OutputRoot `
        -Arguments @("sussex", "condition", "--condition-root", $conditionRoot, "update", "cli-temp-condition", "--label", "CLI Temp Condition Edited", "--inactive", "--json") |
        ConvertFrom-Json
    Invoke-ConsumerCli `
        -Name "roundtrip-export" `
        -CliPath $cliPath `
        -WorkspaceRoot $workspaceRoot `
        -OutputDirectory $OutputRoot `
        -Arguments @("sussex", "condition", "--condition-root", $conditionRoot, "export", "cli-temp-condition", $exportPath, "--json") | Out-Null
    Invoke-ConsumerCli `
        -Name "roundtrip-delete" `
        -CliPath $cliPath `
        -WorkspaceRoot $workspaceRoot `
        -OutputDirectory $OutputRoot `
        -Arguments @("sussex", "condition", "--condition-root", $conditionRoot, "delete", "cli-temp-condition", "--json") | Out-Null
    $imported = Invoke-ConsumerCli `
        -Name "roundtrip-import" `
        -CliPath $cliPath `
        -WorkspaceRoot $workspaceRoot `
        -OutputDirectory $OutputRoot `
        -Arguments @("sussex", "condition", "--condition-root", $importRoot, "import", $exportPath, "--json") |
        ConvertFrom-Json

    $roundTrip = [pscustomobject]@{
        CreatedId = $created.created.id
        CreatedActive = $created.created.is_active
        UpdatedActive = $updated.updated.is_active
        ExportExists = Test-Path $exportPath
        ImportedId = $imported.imported.id
        ImportedActive = $imported.imported.is_active
    }
}

$summary = [pscustomobject]@{
    PackageName = $package.Name
    PackageVersion = $package.Version.ToString()
    PackageFamilyName = $package.PackageFamilyName
    InstallLocation = $package.InstallLocation
    RunningProcessId = $processId
    RunningWindowTitle = $windowTitle
    CliPath = $cliPath
    ConditionCount = $conditionItems.Count
    ActiveConditions = $activeConditionIds
    FixedConditionVisual = $fixedCondition.visual_profile_id
    FixedConditionBreathing = $fixedCondition.controller_breathing_profile_id
    FixedOrbit = "$(Get-ControlValue $fixedVisual "orbit_distance_min")..$(Get-ControlValue $fixedVisual "orbit_distance_max")"
    FixedSphere = "$(Get-ControlValue $fixedVisual "sphere_radius_min")..$(Get-ControlValue $fixedVisual "sphere_radius_max")"
    CurrentOrbit = "$(Get-ControlValue $currentVisual "orbit_distance_min")..$(Get-ControlValue $currentVisual "orbit_distance_max")"
    CurrentSphere = "$(Get-ControlValue $currentVisual "sphere_radius_min")..$(Get-ControlValue $currentVisual "sphere_radius_max")"
    FixedBreathingName = $fixedBreathing.name
    ConditionEditRoundTrip = $roundTrip
}

$summaryPath = Join-Path $OutputRoot "summary.json"
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryPath
$summary | ConvertTo-Json -Depth 8
