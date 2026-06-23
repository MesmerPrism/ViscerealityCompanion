# Peripersonal Space End-to-End Test Onboarding

This guide describes how to run the Peripersonal Space end-to-end proof on a
new Windows machine. It uses the Viscereality Companion CLI commands that mirror
the human operator buttons in the WPF guide.

Use the bundled APK kit for the Quest apps and the Companion branch for the
Windows operator workflow.

## Required Inputs

- Peripersonal Companion branch:
  `codex/peripersonal-wpf-unity-operator-20260620`
- Bundle contents:
  - `quest-session-kit/APKs/PeripersonalRuntime.apk`
  - `apks/QuestQuestionnairePanel-labUpdater-debug.apk`
  - `study-shells/peripersonal-space.json`
  - `study-shells/peripersonal-space/templates/*.json`
  - `study-shells/peripersonal-space/visual-profiles/*.json`
  - `study-shells/peripersonal-space/controller-breathing-profiles/*.json`
  - `quest-session-kit/APKs/compatibility.json`
  - `docs/PERIPERSONAL_E2E_ONBOARDING.md`
  - optional supplemental visual guide:
    `docs/peripersonal-operator-onboarding.pdf`
  - `tools/Check-PeripersonalE2EPrereqs.ps1`

The two APKs required for the run are the Peripersonal Unity runtime and the
MAIA Spatial questionnaire panel. The study-shell JSON in the bundle is laid
out so `--root <bundle>\study-shells` resolves the runtime APK and the bundled
Peripersonal visual/controller profiles without copying files into the
Companion repo.

## Dependency Preflight

Open PowerShell on the Windows operator machine.

1. Confirm basic tools:

```powershell
git --version
dotnet --list-sdks
powershell -NoProfile -Command "$PSVersionTable.PSVersion"
```

Expected:

- Git is available.
- .NET SDK `10.0.x` is available for building/running the current Companion
  source branch.
- PowerShell 5.1 or newer is available.

2. Clone and build Companion:

```powershell
git clone --branch codex/peripersonal-wpf-unity-operator-20260620 https://github.com/MesmerPrism/ViscerealityCompanion.git
cd .\ViscerealityCompanion
dotnet build .\ViscerealityCompanion.sln /p:UseSharedCompilation=false
$Cli = Resolve-Path .\src\ViscerealityCompanion.Cli\bin\Debug\net10.0\viscereality.exe
```

3. Check Companion-managed Quest tooling:

```powershell
& $Cli tooling status
& $Cli tooling install-official
& $Cli tooling status
& $Cli windows-env analyze
```

The `install-official` command installs or refreshes the managed Meta/Android
tool cache used by Companion. `windows-env analyze` mirrors the WPF environment
check and reports missing liblsl/tooling/install-footprint issues before the
run.

4. Set the bundle root for the remaining checks:

```powershell
$Bundle = "C:\path\to\peripersonal-e2e-kit-YYYYMMDD-HHMMSS"
$StudyRoot = Join-Path $Bundle "study-shells"
```

5. Optional script check from the bundle:

```powershell
& "$Bundle\tools\Check-PeripersonalE2EPrereqs.ps1" -CompanionRepoRoot (Get-Location).Path -BundleRoot $Bundle
```

6. Confirm the bundled Peripersonal profiles resolve through the same CLI
surface used by the WPF profile tabs:

```powershell
& $Cli study-profile visual list --study peripersonal-space --root $StudyRoot
& $Cli study-profile controller list --study peripersonal-space --root $StudyRoot
& $Cli study-profile condition list --study peripersonal-space --root $StudyRoot --active-only
```

Expected:

- all four active conditions resolve to `Peripersonal ...` visual profiles;
- every condition resolves `Peripersonal Runtime Default` as its
  controller-breathing profile;
- no output says a Sussex visual profile was selected for the Peripersonal
  shell.

## Headset Preparation

Use a Quest with developer mode enabled and USB debugging authorized for the
operator machine.

1. Connect the Quest over USB.

2. Confirm USB visibility:

```powershell
& $Cli probe
```

Record the USB serial, for example `3487C10H3M017Q`.

3. Enable Wi-Fi ADB through Companion:

```powershell
& $Cli wifi -d 3487C10H3M017Q
```

The command prints the headset IP. If needed, reconnect explicitly:

```powershell
& $Cli connect 192.168.2.56:5555
```

For the rest of the run, use the Wi-Fi endpoint:

```powershell
$Device = "192.168.2.56:5555"
& $Cli status -d $Device
```

Run the bundle checker again with the selected device. This device-scoped pass
must report a Quest Wi-Fi IPv4 address, because the Peripersonal runtime command
receipts use LSL over the headset network, not USB ADB:

```powershell
& "$Bundle\tools\Check-PeripersonalE2EPrereqs.ps1" -CompanionRepoRoot (Get-Location).Path -BundleRoot $Bundle -Device $Device
```

## Install The Quest Apps

Set the bundle and state paths:

```powershell
$Bundle = "C:\path\to\peripersonal-e2e-kit-YYYYMMDD-HHMMSS"
$StudyRoot = Join-Path $Bundle "study-shells"
$StateRoot = Join-Path $PWD "artifacts\peripersonal-e2e-run"
New-Item -ItemType Directory -Force -Path $StateRoot | Out-Null
$State = Join-Path $StateRoot "peripersonal-cli-state.json"
```

Install the MAIA Spatial questionnaire panel:

```powershell
& $Cli install "$Bundle\apks\QuestQuestionnairePanel-labUpdater-debug.apk" -d $Device
```

Install and verify the pinned Peripersonal runtime:

```powershell
& $Cli study install peripersonal-space --root $StudyRoot -d $Device
& $Cli study apply-profile peripersonal-space --root $StudyRoot -d $Device
& $Cli study status peripersonal-space --root $StudyRoot -d $Device
```

Expected status:

- Package `com.Viscereality.ViscerealityPeriPersonal` is installed.
- The installed runtime hash matches the pinned shell.
- Companion-only `viscereality.*` profile keys are reported as metadata, not as
  Android `setprop` failures.

## Run The End-To-End Proof

The following sequence matches the clean proof run. The `participant-command`
scripts drive the questionnaire as a participant would: first a blank submit is
attempted for Blocks 2 and 3, then a valid answer is selected, then the real
participant Submit button is triggered.

1. Launch XR:

```powershell
& $Cli study launch peripersonal-space --root $StudyRoot -d $Device
```

2. Prepare the session:

```powershell
& $Cli peripersonal prepare `
  --participant P-E2E-001 `
  --session session-e2e-001 `
  --handedness right-handed `
  --language en `
  --condition left-visible `
  --root $StudyRoot `
  --state $State `
  --receipt-timeout-seconds 40 `
  -d $Device
```

For the current rule, `right-handed` maps the breath-tracking controller side to
the left controller and `left-handed` maps it to the right controller.

3. Open and submit MAIA Spatial Block 1:

```powershell
& $Cli peripersonal open-questionnaire `
  --block block-1 `
  --condition-number 1 `
  --participant-command-script "defaults;next;next;submit" `
  --participant-command-interval-ms 12000 `
  --root $StudyRoot `
  --state $State `
  --receipt-timeout-seconds 40 `
  -d $Device

& $Cli peripersonal mark-block1-submitted --root $StudyRoot --state $State
```

Block 1 contains the language, demographics, and MAIA setup stages.

4. Start the one global recording:

```powershell
& $Cli peripersonal start-recording `
  --root $StudyRoot `
  --state $State `
  --receipt-timeout-seconds 40 `
  -d $Device
```

5. Run clock alignment and particle markers:

```powershell
& $Cli peripersonal clock-probe `
  --duration-seconds 10 `
  --probe-interval-ms 250 `
  --root $StudyRoot `
  --state $State `
  --receipt-timeout-seconds 40 `
  -d $Device

& $Cli peripersonal particles particles-on --root $StudyRoot --state $State --receipt-timeout-seconds 40 -d $Device
& $Cli peripersonal particles particles-off --root $StudyRoot --state $State --receipt-timeout-seconds 40 -d $Device
```

The event log should contain distinct `Particles-ON` and `Particles-OFF`
markers.

6. Mark XR Block 1 end:

```powershell
& $Cli peripersonal mark-xr-block-end `
  --block xr-block-1 `
  --condition left-visible `
  --root $StudyRoot `
  --state $State `
  --receipt-timeout-seconds 40 `
  -d $Device
```

Recording must remain active.

7. Open and submit MAIA Spatial Block 2:

```powershell
& $Cli peripersonal open-questionnaire `
  --block block-2 `
  --condition-number 2 `
  --participant-command-script "submit;choice=D;submit" `
  --participant-command-interval-ms 15000 `
  --root $StudyRoot `
  --state $State `
  --receipt-timeout-seconds 40 `
  -d $Device
```

Expected behavior: blank submit is a no-op, choice `D` enables Submit, the
participant submit returns the XR app to foreground.

8. Mark XR Block 2 end:

```powershell
& $Cli peripersonal mark-xr-block-end `
  --block xr-block-2 `
  --condition left-visible `
  --root $StudyRoot `
  --state $State `
  --receipt-timeout-seconds 40 `
  -d $Device
```

9. Open and submit MAIA Spatial Block 3:

```powershell
& $Cli peripersonal open-questionnaire `
  --block block-3 `
  --condition-number 3 `
  --participant-command-script "submit;choice=E;submit" `
  --participant-command-interval-ms 15000 `
  --root $StudyRoot `
  --state $State `
  --receipt-timeout-seconds 40 `
  -d $Device
```

Expected behavior: blank submit is a no-op, choice `E` enables Submit, the
participant submit returns the XR app to foreground.

10. Stop recording and close apps:

```powershell
& $Cli peripersonal stop-recording `
  --root $StudyRoot `
  --state $State `
  --receipt-timeout-seconds 45 `
  -d $Device

& $Cli peripersonal workflow-status --root $StudyRoot --state $State
```

This final stop ends the single global recording, pulls the Quest backup files,
and closes the Quest apps.

## Evidence To Check After The Run

Find the session folder under:

```text
%LOCALAPPDATA%\ViscerealityCompanion\study-data\peripersonal-space\peripersonal-space
```

The folder name should follow:

```text
participantID_sessionID_timestamp
```

Confirm these files exist:

- `device-session-pull\questionnaire_results.jsonl`
- `clock_alignment_roundtrip.csv`
- `device-session-pull\runtime_state_samples.csv`
- `device-session-pull\timing_markers.csv`
- `device-session-pull\session_events.csv`

Expected questionnaire result count:

- 3 completed rows.
- Block 2 choice is `D` in this automated proof.
- Block 3 choice is `E` in this automated proof.

Expected session events:

- `recording_started`
- `Particles-ON`
- `Particles-OFF`
- two `XR-Block-End` markers in this abbreviated proof
- questionnaire launch/result events
- app focus transitions from questionnaire panel back to Unity
- `recording_stopped`

The controller data path requires live controller availability. Do not treat an
autonomous run with offline controllers as controller/breath-tracking proof.

## Visual Evidence

For a report-quality rerun, capture screenshots after each operator step:

```powershell
& $Cli hzdb screenshot --output "$StateRoot\01-xr-launched.png" -d $Device
```

Capture foreground state when proving panel submit return:

```powershell
adb -s $Device shell dumpsys activity activities
```

When present, the previous visual evidence guide is included in the bundle as a
supplement. Treat this Markdown file as the authoritative runbook for the
current bundle:

```text
docs\peripersonal-operator-onboarding.pdf
```

## Current Questionnaire Routing

The current implementation uses the existing Unity-mediated route:

```text
Windows Companion CLI/WPF
  -> Unity-targeted LSL command
  -> Unity command bridge
  -> Unity Android caller bridge
  -> questionnaire panel app
  -> participant submit
  -> panel writes result to Unity-owned content URI
  -> Unity callback PendingIntent
  -> Unity records questionnaire_results.jsonl
  -> Windows pulls the session bundle
```

Unity does not render, fill, or submit the questionnaire. It currently brokers
launch and receives the callback because the questionnaire caller contract is
Unity-owned. The disentangling notes are in:

```text
docs\peripersonal-questionnaire-routing.md
```
