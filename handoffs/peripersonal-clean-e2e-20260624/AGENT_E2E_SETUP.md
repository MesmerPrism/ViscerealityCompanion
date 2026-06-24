# Agent E2E Setup Guide

This guide is written so another agent can run the Peripersonal Companion setup
on a different Windows machine without relying on Till's local paths.

## Required Inputs

From the handoff ZIP:

- `apks/PeripersonalRuntime.apk`
- `apks/QuestQuestionnairePanel.apk`
- `apks/sha256sums.txt`

From GitHub:

- Repository: `MesmerPrism/ViscerealityCompanion`
- Branch: `codex/peripersonal-wpf-unity-operator-20260620`
- Unity runtime repository:
  `GeorgeFejer91/peripersonal-space-experiment-2025-12-10`
- Unity runtime branch:
  `codex/peripersonal-wpf-unity-runtime-20260620`
- Questionnaire panel repository:
  `MesmerPrism/quest-questionnaire-panel`
- Questionnaire panel branch:
  `codex/peripersonal-operator-runtime-20260618`

Use `PACKAGE_MANIFEST.json` in the ZIP for the exact branch-head commits that
were current when the bundle was created.

## Machine Prerequisites

- Windows 10 or Windows 11.
- Git.
- .NET SDK matching `global.json` in the repo.
- A Quest headset with Developer Mode enabled.
- ADB available either through Companion managed tooling or Android platform
  tools on `PATH`.
- The Quest and Windows machine on the same network for Wi-Fi ADB, or USB
  connected for initial setup.
- Windows firewall must allow the Companion CLI/app and LSL traffic when
  prompted.

## Clone And Build

```powershell
git clone https://github.com/MesmerPrism/ViscerealityCompanion.git
cd ViscerealityCompanion
git checkout codex/peripersonal-wpf-unity-operator-20260620
dotnet build ViscerealityCompanion.sln
```

The CLI DLL after build is:

```powershell
.\src\ViscerealityCompanion.Cli\bin\Debug\net10.0\viscereality.dll
```

Use it through:

```powershell
$cli = Resolve-Path .\src\ViscerealityCompanion.Cli\bin\Debug\net10.0\viscereality.dll
```

The WPF app can be launched from the same build output:

```powershell
.\src\ViscerealityCompanion.App\bin\Debug\net10.0-windows\ViscerealityCompanion.App.exe
```

In the WPF app, open the Peripersonal study shell. The base app still owns
general device status, battery/tooling, APK installation, and fallback actions.
The Peripersonal shell adds the sequential operator guide and the live
`Input owner` card.

## Install APKs

Connect the Quest over USB, confirm authorization inside the headset, then run:

```powershell
adb devices
adb install -r .\apks\PeripersonalRuntime.apk
adb install -r .\apks\QuestQuestionnairePanel.apk
```

Expected APK hashes for this bundle:

- `PeripersonalRuntime.apk`:
  `3cf14c777cf67648b829743d48eea21a4ee28da1ac8a0a7049f458bd4c1d95bd`
- `QuestQuestionnairePanel.apk`:
  `2768660e9af269f12dc8a39a6c98ce03d0890b4cf459602717983473a8920c3f`

If running from the repo instead of the ZIP copy, both APKs are also mirrored
in the Companion sample kit:

```powershell
.\samples\quest-session-kit\APKs\PeripersonalRuntime.apk
.\samples\quest-session-kit\APKs\QuestQuestionnairePanel.apk
```

To rebuild the APKs instead of using the bundled binaries, check out the Unity
and questionnaire panel branches listed above.

## Optional Wi-Fi ADB

Use the Companion UI guided workflow if possible. For a direct ADB check:

```powershell
adb tcpip 5555
adb shell ip route
adb connect <quest-ip>:5555
adb devices
```

Use the actual device serial or `ip:port` value in `--device`.

## Operator-Equivalent CLI Run

Set common variables:

```powershell
$device = "<quest-serial-or-ip:port>"
$root = Resolve-Path .\samples\study-shells
$state = Resolve-Path .\artifacts\peripersonal-e2e-state.json
$participant = "P001"
$session = "session-001"
```

Run the workflow:

```powershell
dotnet $cli study stop peripersonal-space --root $root --device $device

dotnet $cli study launch peripersonal-space --root $root --device $device

dotnet $cli peripersonal foreground-status `
  --wait-seconds 6 `
  --json

dotnet $cli peripersonal prepare `
  --participant $participant `
  --session $session `
  --handedness right-handed `
  --language en `
  --condition left-visible `
  --study peripersonal-space `
  --root $root `
  --state $state `
  --receipt-timeout-seconds 45 `
  --device $device

dotnet $cli peripersonal open-questionnaire `
  --block block-1 `
  --condition-number 1 `
  --participant-command-script "defaults;next;next;submit" `
  --participant-command-interval-ms 10000 `
  --study peripersonal-space `
  --root $root `
  --state $state `
  --receipt-timeout-seconds 45 `
  --device $device

dotnet $cli peripersonal foreground-status `
  --wait-seconds 6 `
  --json

dotnet $cli peripersonal mark-block1-submitted `
  --study peripersonal-space `
  --root $root `
  --state $state `
  --device $device

dotnet $cli peripersonal start-recording `
  --study peripersonal-space `
  --root $root `
  --state $state `
  --receipt-timeout-seconds 45 `
  --device $device

dotnet $cli peripersonal clock-probe `
  --duration-seconds 5 `
  --probe-interval-ms 250 `
  --study peripersonal-space `
  --root $root `
  --state $state `
  --receipt-timeout-seconds 45 `
  --device $device

dotnet $cli peripersonal particles particles-on `
  --study peripersonal-space `
  --root $root `
  --state $state `
  --receipt-timeout-seconds 45 `
  --device $device

dotnet $cli peripersonal mark-xr-block-end `
  --block xr-block-1 `
  --condition left-visible `
  --study peripersonal-space `
  --root $root `
  --state $state `
  --receipt-timeout-seconds 45 `
  --device $device

dotnet $cli peripersonal open-questionnaire `
  --block block-2 `
  --condition-number 2 `
  --participant-command-script "submit;choice=D;submit" `
  --participant-command-interval-ms 2500 `
  --study peripersonal-space `
  --root $root `
  --state $state `
  --receipt-timeout-seconds 45 `
  --device $device

dotnet $cli peripersonal foreground-status `
  --wait-seconds 6 `
  --json

dotnet $cli peripersonal particles particles-off `
  --study peripersonal-space `
  --root $root `
  --state $state `
  --receipt-timeout-seconds 45 `
  --device $device

dotnet $cli peripersonal mark-xr-block-end `
  --block xr-block-2 `
  --condition right-anchor-only `
  --study peripersonal-space `
  --root $root `
  --state $state `
  --receipt-timeout-seconds 45 `
  --device $device

dotnet $cli peripersonal open-questionnaire `
  --block block-3 `
  --condition-number 3 `
  --participant-command-script "submit;choice=E;submit" `
  --participant-command-interval-ms 2500 `
  --study peripersonal-space `
  --root $root `
  --state $state `
  --receipt-timeout-seconds 45 `
  --device $device

dotnet $cli peripersonal foreground-status `
  --wait-seconds 6 `
  --json

dotnet $cli peripersonal stop-recording `
  --study peripersonal-space `
  --root $root `
  --state $state `
  --receipt-timeout-seconds 45 `
  --device $device
```

## Expected Evidence

After `stop-recording`, run:

```powershell
dotnet $cli peripersonal workflow-status --state $state
```

The status should show:

- `Workflow state: Stopped`
- `Block 1 submitted: True`
- a non-empty `Pulled Quest backup`

The pulled backup should include:

- `questionnaire_results.jsonl`
- `session_events.csv`
- `timing_markers.csv`
- `clock_alignment_samples.csv`
- `clock_alignment_roundtrip.csv` in the Windows session folder

Foreground-status pass criteria:

- after `study launch`, the CLI/WPF classifier reports
  `Peripersonal XR runtime owns input`
- after opening a questionnaire block, the classifier reports either
  `Questionnaire panel owns input` or
  `Questionnaire panel is open but not input-focused`
- after participant submit settles, the classifier returns to
  `Peripersonal XR runtime owns input`

Minimal pass criteria:

- three questionnaire records with `status: completed`
- `Particles-ON` and `Particles-OFF` timing markers
- at least one clock echo persisted on both Windows and Quest
- final stop closes both Quest apps
