---
title: Study Shells
description: Reusable simplified operator modes for specific studies such as the Sussex University controller-breathing session.
summary: Study shells can either live inside the full operator app or, when the manifest requests it, open directly into an approved study workspace and hide the broader operator surfaces.
nav_label: Study Shells
nav_group: Operator Guides
nav_order: 35
---

# Study Shells

The main app now supports dedicated study shells: focused operator modes that
sit inside the same public operator window without requiring a separate app
codebase.

## Why They Exist

Many sessions do not need the full `Quest Library`, `Runtime Config`, or raw
`Twin Monitor` surfaces during the run itself. A study shell trims the operator
view down to:

- one approved APK identity
- one dedicated Quest device profile
- the live signals the experimenter actually needs to watch
- only the trigger buttons the study protocol allows

That keeps the participant-facing runtime simple while still letting the study
team choose between two packaging modes:

- embedded study mode, where the operator can still return to the full app
- dedicated study package, where the app opens straight into one shell and
  keeps the broader operator surfaces hidden

When a study shell is activated from `Start Here`, the first tab becomes the
study workspace and the main header switches into that study mode as well. If
the catalog manifest sets a startup study plus `lockToStartupStudy`, the app
opens there immediately on launch and does not expose the route back to the
main operator tabs.

## Startup And Locking

Study-shell startup behavior is controlled in `samples/study-shells/manifest.json`:

- `startupStudyId`: opens that study automatically when the app starts
- `lockToStartupStudy`: hides the broader operator tabs and the `Exit Study Mode`
  action for that package

That means dedicated study packages are now mainly a config choice:

- keep both fields empty/default for the full operator app
- set `startupStudyId` only if you want a shell to open first but still allow return to the main app
- set both fields when you want the packaged app to behave like a study-specific operator surface

## Sussex University Shell

The first committed study shell is `Sussex University`, backed by
`samples/study-shells/sussex-university.json`.

The committed public Sussex package now uses the dedicated mode above:

- startup study: `sussex-university`
- startup lock: `true`

That means the public Sussex preview is intended to behave like a dedicated
researcher-facing operator surface, not like the broader multi-study app.

It currently pins:

- package id: `com.Viscereality.SussexExperiment`
- version: `0.1.0`
- SHA256: `A97BF5467DA61E869690950FE41416CF1F393FA923E6943362A5E5AD1B364CC9`
- bundled APK path: `../quest-session-kit/APKs/SussexExperiment.apk`
- device profile: `CPU 5 / GPU 5 / static foveation level 1`
- expected LSL input target: `HRV_Biofeedback / HRV`
- expected routing: `Controller Volume / LSL Heartbeat / LSL Direct`
- runtime launch mode: `launchInKioskMode=true`

For the committed Sussex shell, the runtime toggle is intentionally a kiosk
toggle rather than a plain launch/stop button:

- `Launch Kiosk Runtime` launches the Sussex APK, resolves the live Unity task,
  and pins it with `am task lock <TASK_ID>`
- `Exit Kiosk Runtime` sends the confirmed Home-return stack:
  `automation_disable -> task lock stop -> HomeActivity -> force-stop Sussex`
- `Capture Quest Screenshot` prefers the active Wi-Fi ADB endpoint when one is
  available and falls back to USB only if Wi-Fi ADB is not available
- when Sussex itself is in front, the helper prefers the runtime-oriented
  screenshot path first so particle-scene and participant-view checks reflect
  the current Unity view more reliably
- when Horizon shell or Guardian truth matters, the shell-oriented capture path
  still remains available as a fallback
- it is the required operator-side truth source whenever HorizonOS shell state
  and the visible headset scene disagree

That behavior is specific to the Sussex study shell config and is meant to keep
accidental controller Meta / menu presses from visually interrupting the
experiment while the runtime is active.

Current operator rule:

- if kiosk launch or exit only reaches shell-level confirmation, the Sussex
  shell must leave a visual-confirmation warning in place instead of claiming
  success
- on this machine, the shell can still end up in black / Guardian-blocked
  states where `HomeActivity`, `FocusPlaceholderActivity`, or even a resumed
  Sussex task is reported without a usable visible scene
- treat the screenshot card as mandatory for launch/exit verification in those
  cases

Current confirmed GUI behavior on this machine:

- from visible Meta Home, `Launch Kiosk Runtime` now reaches a working Sussex
  kiosk state again
- in that confirmed kiosk state, the controller Meta/menu button is visually
  neutralized while the Unity runtime stays in front
- `Exit Kiosk Runtime` then returns the headset to visible Meta Home and the
  controller Meta/menu button works normally again
- if the headset is already in black `SensorLock` limbo before the operator
  starts, a physical power-button recovery can still be required first

## Self-Contained Sussex Package

The public Sussex preview is supposed to be self-contained:

- the packaged Windows install already includes the bundled Sussex APK
- the study shell already knows the approved hash and device profile
- the operator should not need a separate Astral checkout or a second APK handoff

The committed `samples/quest-session-kit/APKs/SussexExperiment.apk` is intentionally the
same Sussex APK mirrored from the Astral build used for the study. It is stored
through Git LFS in the source repo, but the packaged Windows install exposes
the real file directly to the app at runtime.

When you approve a newer Sussex APK from `AstralKarateDojo`, refresh the public
bundle with:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Sync-Bundled-Sussex-Apk.ps1
```

That keeps the packaged app's `samples/quest-session-kit/APKs/` layout stable
while updating the mirrored APK and the pinned Sussex hash in
`compatibility.json` and `sussex-university.json`.

The source-maintainer handoff is:

```powershell
cd C:\Users\tillh\source\repos\AstralKarateDojo
& "C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe" `
  -batchmode -nographics -quit `
  -projectPath "$PWD" `
  -activeBuildProfile "Assets/Settings/Build Profiles/Meta Quest Sussex Experiment.asset" `
  -logFile "$PWD\Logs\build_sussex_study_apk.log" `
  -executeMethod AstralKarateDojo.IndirectParticles.Editor.BuildWorkflowTools.BuildMetaQuestSussexStudyApk

cd C:\Users\tillh\source\repos\ViscerealityCompanion
powershell -ExecutionPolicy Bypass -File .\tools\app\Sync-Bundled-Sussex-Apk.ps1
```

That is the only time the public source repo needs a local `AstralKarateDojo`
checkout. The packaged Sussex operator app remains self-contained.

The embedded Sussex workspace checks:

- whether the bundled APK matches the approved study hash
- whether the same build is already installed on the headset
- whether the pinned Quest device profile is currently active
- live LSL target, connected stream, and inlet status from `quest_twin_state`

- routed `0..1` inbound HRV biofeedback when the runtime publicly echoes it
- controller breathing, heartbeat, and coherence values from the study runtime
- camera drift from the last recenter anchor
- particle visibility and whether rendering is suppressed by the operator or HUD
- current fps, frame time, and refresh-rate telemetry when the runtime publishes it

The active Sussex shell is organized into three operator views:

- `Pre-session` for connection, Sussex APK verification, and device-profile setup
- `Visual Profiles` for named appearance-only Sussex presets that the experimenter can create, compare, save, and apply over the existing hotload-file path
- `During session` for the live monitoring and command surface
- `Inspect` for detailed study-runtime settings, focused live values, and recent twin events

The `Visual Profiles` tab is intentionally narrower than the general runtime
editor. It only exposes the approved appearance controls for the Sussex scene:

- sphere deformation on/off
- particle-size minimum and maximum
- depth-wave minimum and maximum
- transparency minimum and maximum
- saturation minimum and maximum
- brightness minimum and maximum
- orbit-distance minimum and maximum

Each saved profile is stored as one self-describing JSON file under the local
Companion profile library. The shell compiles that profile into a
`showcase_active_runtime_config_json` hotload payload and uploads it through the
existing Sussex ADB file path. The headset never consumes the partial source
JSON directly.

The workflow tab now also offers `Open Sequential Guide`, a pop-out onboarding
window that walks the operator through the fixed Sussex protocol step by step:

- USB probe
- Wi-Fi ADB enable
- Wi-Fi match confirmation
- USB unplug and Wi-Fi-only confirmation
- APK and device-profile verification
- kiosk launch
- LSL and particle verification
- optional controller calibration
- 20 second validation capture with inline timing alignment, local and pulled Quest output folders, and a generated PDF review report
- reset-to-ready handoff back to the main shell

The validation step now keeps the timing workflow inside the same guide
surface. Instead of opening a separate timing window, step 12 shows:

- the start clock-alignment burst
- the sparse drift probe state during the 20 second capture
- the matching end burst before pullback and PDF generation

That makes it easier for the operator to see which phase is currently running
without losing the main workflow context.

The Sussex command path was also tightened so normal study buttons do not do a
full pre-send wake + snapshot refresh when the headset is already awake. On a
healthy live session, command presses should now reach the runtime noticeably
faster than the earlier public builds.

The current public shell can also send the study recenter command and the
dedicated particle visibility on/off commands.

The Sussex shell now uses the bundled APK path from the app payload on
startup, so packaged Windows installs do not depend on a machine-local Astral
workspace just to find the Sussex APK.

## Sussex Verification Harness

The repo now includes a tracked live harness:

- command: `powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Sussex-VerificationHarness.ps1`
- output: `artifacts/verify/sussex-study-mode-live/`

That harness:

- opens the main WPF app
- activates `Sussex University experiment mode`
- starts a local float LSL sender on `HRV_Biofeedback / HRV`
- publishes smoothed `0..1` HRV biofeedback packets from this Windows machine on an irregular heartbeat-timed cadence
- installs, launches, and profiles the bundled Sussex APK
- captures GUI and Quest screenshots for kiosk-launch and kiosk-exit review
- writes a text report alongside those screenshots

The latest on-head harness approval pass on `2026-03-31`:

- verified kiosk exit back to `com.oculus.vrshell`
- updated the approved Sussex verification baseline to the current APK hash
- confirmed matching Windows and Quest session metadata for the participant run
- kept one known non-blocking caveat: sparse background clock-alignment probes
  still start, but they do not yet receive Quest echoes during the short
  harness run

Treat those screenshots as evidence to inspect, not as an automatic proof that
Meta Home was visibly restored. On the current HorizonOS build, kiosk exit can
still land in a passthrough / placeholder limbo even after `HomeActivity` has
focus again, so the operator must review the captured Quest screenshot before
calling the exit side "good". See `docs/quest-adb-hzdb-recovery-notes.md` for
the current blocked-shell findings on this machine.

The harness should preserve normal proximity behavior by default. If a previous
run left an 8h proximity hold active, clear it first instead of re-arming it;
otherwise the unattended harness is no longer matching the real
"headset-on-face" Sussex operator workflow.

On this machine, prefer that published harness launcher over raw
`dotnet run` because Windows Application Control can block unpackaged local
loads. If the harness still fails with Win32 `4551` while loading `lsl.dll`,
treat that as the known machine policy blocker rather than as a Sussex scene or
bundle regression.

At the moment, the public study telemetry confirms full sender -> headset inlet
connectivity through `study.lsl.connected_*` and `study.lsl.status`. If the
runtime also exposes `signal01.coherence_lsl` or a `driver.stream.*.value01`
mirror entry, the harness will measure round-trip latency for the direct
coherence value. The currently verified public Sussex build does expose that
value, so the harness now reports measured coherence loop latency in the text
report.

If the Quest launches drift into Guardian / SensorLock / FocusPlaceholder
limbo, use `docs/quest-adb-hzdb-recovery-notes.md` as the source of truth for
tested `adb` / `hzdb` recovery commands and the observed Meta shell state
machine on this setup.

## How Study Shell Discovery Works

The app looks for study-shell definitions in this order:

1. `VISCEREALITY_STUDY_SHELL_ROOT`
2. `samples/study-shells/` relative to the repo root
3. `samples/study-shells/` next to the executable

That means new simplified study windows are primarily a data/config task:

- add a JSON definition
- optionally choose a startup study and whether that package should lock to it
- point it at the study package, hash, and device profile
- list the live keys and study commands to expose
- reopen the app and launch the shell from `Start Here`

No Unity scene code needs to live in this repo for that operator window to be
useful.
