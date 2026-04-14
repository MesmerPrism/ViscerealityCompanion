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
- version: `0.1.2`
- SHA256: `CFDD4038C46A07A0824A0C51DEFEE9D7A21ADD06F937E9A2E8A0FCD24759E5B4`
- bundled APK path: `../quest-session-kit/APKs/SussexExperiment.apk`
- device profile: `CPU 5 / GPU 5 / static foveation level 1`
- expected LSL input target: `HRV_Biofeedback / HRV`
- expected routing: `Controller Volume / LSL Heartbeat / LSL Direct`
- profile workspaces: `Visual Profiles` and `Breathing Profiles`
- runtime launch mode: `launchInKioskMode=true`

For the committed Sussex shell, the runtime toggle is intentionally a kiosk
toggle rather than a plain launch/stop button:

- `Launch Kiosk Runtime` launches the Sussex APK, resolves the live Unity task,
  and pins it with `am task lock <TASK_ID>`
- if the headset reports asleep, the launch surface now blocks the action and
  tells the operator `Wake the headset to enable launching`
- if Guardian or another Meta visual blocker is active, the launch surface also
  stays blocked until that blocker is cleared in-headset
- `Exit Kiosk Runtime` sends the confirmed Home-return stack:
  `automation_disable -> task lock stop -> HomeActivity -> force-stop Sussex`
  when an operator intentionally invokes it from a visible on-head runtime
  state
- `Capture Quest Screenshot` prefers the active Wi-Fi ADB endpoint when one is
  available and falls back to USB only if Wi-Fi ADB is not available
- when Sussex itself is in front, the helper prefers the runtime-oriented
  screenshot path first so particle-scene and participant-view checks reflect
  the current Unity view more reliably
- when Horizon shell or Guardian truth matters, the shell-oriented capture path
  still remains available as a fallback
- it is the required operator-side truth source whenever HorizonOS shell state
  and the visible headset scene disagree

That behavior is specific to the Sussex study shell config, but on the current
April 2026 Meta OS build it no longer guarantees that the controller Meta /
menu button is visually neutralized while Sussex is active. Treat it as
best-effort task pinning plus screenshot-confirmed foreground only. The public
GUI also no longer exposes remote headset wake/sleep controls for Sussex; use
manual headset wake/sleep only.

Current operator rule:

- if kiosk launch or exit only reaches shell-level confirmation, the Sussex
  shell must leave a visual-confirmation warning in place instead of claiming
  success
- on this machine, the shell can still end up in black / Guardian-blocked
  states where `HomeActivity`, `FocusPlaceholderActivity`, or even a resumed
  Sussex task is reported without a usable visible scene
- treat the screenshot card as mandatory for launch/exit verification in those
  cases
- do not use `Exit Kiosk Runtime` or `viscereality study stop
  sussex-university` as off-face automated cleanup on this machine
- if Sussex must be exited, ask the wearer to quit while the headset is on-face
  and visually confirm the Home-side result afterward
- for off-face functionality tests, quitting Sussex is optional and can be
  skipped

Current confirmed GUI behavior on this machine after the April 2026 Meta OS
update:

- from visible Meta Home, `Launch Kiosk Runtime` can still reach a working
  Sussex runtime in front
- the controller Meta / menu button is no longer a reliable kiosk-success
  signal, because it can remain active even while Sussex stays in front
- from a worn-head, visible Sussex runtime state, `Exit Kiosk Runtime` can
  still return the headset to visible Meta Home, but screenshot confirmation
  remains mandatory
- if the headset is already asleep or in black `SensorLock` limbo before the
  operator starts, wake or recover it first; do not launch Sussex from sleep

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
- `Visual Profiles` for named appearance-only Sussex presets that the experimenter can create, save, pin as the next-launch override, and apply over the existing hotload-file path
- `During session` for the live monitoring and command surface
- `Inspect` for detailed study-runtime settings, focused live values, and recent twin events

The `Visual Profiles` tab is intentionally narrower than the general runtime
editor. It only exposes the approved appearance controls for the Sussex scene:

- simplified tracer controls:
  - tracers enabled
  - tracer lifetime seconds
  - tracers per oscillator
- sphere deformation on/off
- sphere radius minimum and maximum
- particle size relative to radius on/off
- particle-size minimum and maximum
- depth-wave minimum and maximum
- transparency minimum and maximum
- saturation minimum and maximum
- brightness minimum and maximum
- orbit-distance minimum and maximum

Each saved profile is stored as one self-describing JSON file under the local
Companion profile library. The shell compiles saved profiles and runtime drafts
into a `showcase_active_runtime_config_json` hotload payload and uploads that
through the existing Sussex ADB file path. The headset never consumes the
partial source JSON directly.

The packaged app can also carry extra read-only Sussex visual profiles from:

- `samples/study-shells/sussex-university/visual-profiles/`

Those bundled release profiles are listed after the bundled baseline and before
the local writable profile library. They can be selected, applied, exported,
and pinned for launch, but editing them requires copying them into the runtime
draft and saving that draft as a new local profile.

The workflow is intentionally split into four separate surfaces plus the
orientation-first Sussex home view:

- the Home tab:
  - this is the operator landing page
  - it summarizes pre-session state, live-session state, and the current
    sequential-guide step before the operator dives deeper
- the Experiment Session window:
  - this is the dedicated live participant-run surface after the sequential
    guide has passed
- it keeps participant entry, `Start Recording`, `Stop Recording`, live
  telemetry, clock/network consistency, recenter, particle toggles,
  screenshot capture, direct `Start Dynamic-Axis Calibration`, `Start
  Fixed-Axis Calibration`, `Reset Calibration`, quick access to the Windows
  session folder, the pulled Quest backup folder, the generated
  `session_review_report.pdf`, and the condensed operator log on one
  low-distraction popout
- one saved launch profile:
  - this is the pinned saved profile the shell stages to the device-side
    startup CSV before Sussex launches
- one runtime working draft:
  - the editable table is always this draft
  - selecting a saved profile copies it into the draft
  - runtime edits and `Apply To Current Session` only mutate the draft and the
    running Sussex session
- the saved profile library:
  - `Save As New Profile` copies the current runtime draft into the library as
    a new saved profile
  - `Save Changes To Selected Profile` overwrites the currently selected saved
    local saved profile from the runtime draft
  - `Set Selected Profile For Next Launch` pins a saved profile without
    changing the current running session

When preparing a public release, copy the machine-local visual profiles you want
to ship into that bundled folder with:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Sync-Bundled-Sussex-VisualProfiles.ps1
```

That sync keeps the most recent local file for each visual profile name and
skips repo-local GUI/test artifacts such as `zzz-*`.

The parameter table always compares against the current saved launch profile.
At shell startup, the runtime working draft is initialized from that same
launch profile so the editable and comparison columns begin aligned, but later
runtime edits stay temporary until the operator explicitly saves them back into
the profile library. Row reset still returns a single value to the bundled
Sussex baseline.

## Agent Automation And CLI Parity

The packaged Sussex shell now exposes two agent-facing control modes:

- GUI-driving for rendered-state checks, screenshots, and visual confirmation
- CLI-driving for deterministic profile edits, startup/default changes, and
  machine-readable inspection

For GUI-driving, use real UI interactions: select the relevant Sussex tab or
popout window, focus the editable cell or toggle, type or toggle through UI
Automation / keyboard input, then invoke the matching button. For deterministic
profile authoring and startup/default changes, prefer the CLI.

For automation, prefer the CLI. It now uses the same persisted profile JSON
files, startup/default state, apply-state tracking, and device-side hotload
sync rules as the GUI. The intended agentic Sussex workflow is:

1. CLI for deterministic setup, launch, and profile work.
2. GUI `Sequential Guide` once directly before the participant.
3. GUI `Experiment Session` window for the live participant run and recording.

Do not invent ad hoc CLI replacements for the live participant recorder flow.
The current CLI parity stops at setup/runtime control and profile automation;
the real participant run is intentionally driven from the `Experiment Session`
window. The CLI can still help with post-run recovery or inspection through
`viscereality hzdb ls ...` and `viscereality hzdb pull ...`, but the normal
participant start/stop flow remains GUI-first.

The agent-readable field catalogs are:

```powershell
viscereality sussex visual fields --json
viscereality sussex controller fields --json
```

Those commands expose the stable control ids, ranges, baseline values,
runtime-key mappings, full tooltip text, and the effect/increase/decrease/
tradeoff metadata bundled in the Sussex schemas.

The core profile commands are:

```powershell
viscereality sussex visual list --json
viscereality sussex visual show "<profile>" --json
viscereality sussex visual create --name "<new-name>" --from bundled-baseline --set ... --scale ... --json
viscereality sussex visual update "<profile>" --set ... --scale ... --json
viscereality sussex visual set-startup "<profile>" --json
viscereality sussex visual clear-startup --json
viscereality sussex visual apply-live "<profile>" --json

viscereality sussex controller list --json
viscereality sussex controller show "<profile>" --json
viscereality sussex controller create --name "<new-name>" --from bundled-baseline --set ... --scale ... --json
viscereality sussex controller update "<profile>" --set ... --scale ... --json
viscereality sussex controller set-startup "<profile>" --json
viscereality sussex controller clear-startup --json
viscereality sussex controller apply-live "<profile>" --json
```

For the controller profile table and CLI, calibration setup now lives in the
same controller field catalog. The important ids are:

- `use_principal_axis_calibration`
  - `on` means dynamic motion axis
  - `off` means fixed warmed-up controller orientation
- `min_accepted_delta`
  - the smallest movement that still counts as a new accepted calibration sample
- `min_acceptable_travel`
  - the total travel calibration must observe before it accepts the solve

Example:

```powershell
viscereality sussex controller update "<profile>" `
  --set use_principal_axis_calibration=off `
  --set min_accepted_delta=0.0008 `
  --set min_acceptable_travel=0.02 `
  --set-startup `
  --json
```

Current-session `apply-live` mirrors the GUI `Apply To Current Session`
buttons:

- it publishes over the live `quest_hotload_config` twin path
- it requires the Sussex runtime to be in the foreground
- it does not rewrite the saved next-launch/default profile

Startup/default commands mirror the GUI next-launch actions:

- they change the persisted startup/default profile
- they sync the device-side startup CSV immediately when Sussex is not
  currently in the foreground
- if Sussex is running in the foreground, the saved startup/default change is
  deferred until the next `viscereality study stop sussex-university` or
  `viscereality study launch sussex-university`
- do not treat `viscereality study stop sussex-university` as the default
  off-face cleanup path; on this machine that quit flow can still end in
  Guardian / SensorLock / passthrough limbo
- if exit is required, prefer a worn-head operator quit with visual
  confirmation afterward
- for normal functionality validation, leaving Sussex running is acceptable

For natural-language requests like "reduce particle size by 50%", agents should
usually adjust both `particle_size_min` and `particle_size_max`, because the
visual shell models that concept as a paired min/max envelope instead of one
single knob.

The Home tab now also offers `Open Sequential Guide`, a pop-out onboarding
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
- reset-to-ready handoff into the dedicated `Experiment Session` window or back to the main shell

At the LSL step, the guide now exposes `Probe Connection`. That button does not
sniff raw LSL packets directly from Android. Instead, it combines the current
ADB-backed headset snapshot with the live Sussex twin telemetry so the operator
gets an explicit diagnosis of:

- the expected Quest inlet stream name/type
- the runtime target currently configured on the headset
- the currently connected inlet stream and connection counts
- the fresh return path back to Windows on `quest_twin_state / quest.twin.state`
- the companion's operator-to-headset channels on `quest_twin_commands / quest.twin.command` and `quest_hotload_config / quest.config`

The Sussex shell now exposes a dedicated `Windows environment` page so the
Windows-side diagnostics no longer crowd the `Pre-session` setup column. That
page includes the `Machine LSL State` and `Analyze Windows Environment`
surfaces, plus the host-visible operator-data, tooling, workspace, and liblsl
paths the guided installer is supposed to keep aligned. Use that page when the
operator needs the Windows-side view instead of the headset-side view. It
compares:

- the currently visible `HRV_Biofeedback / HRV` publishers on Windows
- the companion-owned TEST sender and twin outlets
- the clock-alignment probe transport
- the passive upstream monitor used during recording

That makes duplicate upstream senders and stale companion-owned streams visible
without guessing from partial symptoms. If switching between the built-in TEST
sender and an external Python sender becomes unreliable, refresh `Machine LSL
State` first and then run `Analyze Windows Environment`.

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

The `During session` surface now also exposes the dedicated breathing-driver
workflow for Sussex:

- `Use Automatic Driver` switches the study into the standalone automatic breathing cycle
- `Start Automatic` and `Pause Automatic` control whether that automatic cycle is currently running
- `Use Controller Volume Driver` returns the study to the controller-volume path

That readback is intentionally driven by the dedicated Sussex automatic-cycle
telemetry (`study.breathing.value01` plus `routing.automatic_breathing.running`)
instead of inferring state from `routing.adaptive_pacer.enabled`.

The dedicated `Experiment Session` window now keeps the other live-session
controls in one place during the participant run:

- live particles and performance status
- breathing-driver and coherence state
- recenter status and fresh Quest screenshot capture
- clock/network consistency plus the latest clock-alignment status
- one-click dynamic-axis and fixed-axis calibration start buttons plus reset
  calibration
- `Detail` foldouts on the operator cards so failures can be debugged without
  leaving the session window
- the collapsible operator log for deeper troubleshooting without taking over
  the main live-monitoring layout

The embedded `During session` surface remains available when the operator wants
broader shell context or deeper inspection, but the popout is now the preferred
live-run window.

Packaged installs now resolve operator data through the host-visible packaged
root (`%LOCALAPPDATA%\Packages\<package-family>\LocalCache\Local\ViscerealityCompanion\...`)
instead of exposing raw app-container aliases. That is the path the session
folder, pulled Quest backup, screenshots, logs, tooling cache, and local agent
workspace buttons now open. Unpackaged/source builds still use the normal
`%LOCALAPPDATA%\ViscerealityCompanion\...` root.

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

The latest completed harness-driven Sussex validation pass on `2026-04-02`:

- confirmed controller-breathing profile apply and restore readback through the embedded Sussex shell
- confirmed participant start/end in `participant_locked` mode on the rebuilt `0.1.1` Sussex APK
- confirmed matching Windows and Quest session metadata for the participant run, including Windows `session_snapshot.json` and Quest `session_snapshot.json`
- updated the approved Sussex APK hash in the pinned public shell metadata to `B19921EE126B780B9530D94DA30ED298A58410D1FEDE58C077B27DD140A9E3A0`
- confirmed the reduced Quest locked-mode file set (`session_events.csv`, `signals_long.csv`, `breathing_trace.csv`, `clock_alignment_samples.csv`, `timing_markers.csv`, `session_settings.json`, `session_snapshot.json`) while broad legacy files such as `lsl_samples.csv` stayed intentionally absent

The current public `0.1.2` Sussex bundle regained a fresh live verification
baseline on `2026-04-03` through the accepted published GUI path:

- confirmed `Use Automatic Driver` switched Sussex into the standalone automatic breathing route with clear GUI readback
- confirmed the automatic breathing value moved live on the headset after `Start Automatic`, paused cleanly, resumed, and then returned to `Controller Volume`
- confirmed the installed headset APK hash matched the pinned public Sussex hash `AFB296E22A5FFE1F648AC32D73CAA6CE3B335EAFFAD2A2B1847D16DDB06ECA29`
- recorded the accepted-app run under `artifacts/verify/sussex-manual-accept-run/`

After that off-face pass, the Sussex bundle was refreshed again on `2026-04-06`
to the current pinned SHA256 `CFDD4038C46A07A0824A0C51DEFEE9D7A21ADD06F937E9A2E8A0FCD24759E5B4`
so the companion can confirm the simplified tracer controls through
`hotload.integrated_tracers_*` readback. Treat the `2026-04-03` notes as the
last published off-face behavior baseline, and the newer hash above as the
current shipped Sussex bundle.

That `2026-04-03` pass was also run off-face, so kiosk exit was intentionally
skipped instead of being re-verified in the same run. On this machine, Windows
Application Control still blocks freshly republished local harness executables,
so the accepted published GUI path remains the reliable live-validation route
for now.

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
