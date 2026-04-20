---
title: Sussex Stable Session Workflow
description: Fixed operator workflow and implementation backlog for low-variability Sussex sessions.
summary: Converts the verified Sussex command findings into a repeatable operator checklist, guardrails, and implementation plan for the remaining GUI and data-collection features.
nav_label: Sussex Workflow
nav_group: Operator Guides
nav_order: 36
---

# Sussex Stable Session Workflow

This page freezes the current best-known Sussex operator path after the March
2026 Quest, kiosk, and GUI verification passes. The goal is to reduce
run-to-run variability by turning the verified findings into one fixed operator
workflow with explicit hazards and a concrete implementation backlog.

## Stable Operator Rules

- Use ADB and `hzdb` for connectivity, install, device-profile setup, screenshot
  capture, and verified study commands.
- Do not use GUI-driven or shell-injected sleep and wake as the normal
  participant handoff path. Use the physical headset power button for
  between-operator and between-participant sleep and wake transitions.
- Use the direct `prox_close` keep-awake override through Sussex launch and the
  pre-session guide on this build. Restore normal wear-sensor behavior with
  `automation_disable` when the live session is done.
- Treat the raw Quest readback literally:
  - `Virtual proximity state: CLOSE` means the keep-awake override is active.
  - `Virtual proximity state: DISABLED` means normal wear-sensor behavior is
    active again.
- Wake the headset before kiosk launch. Do not launch Sussex while the headset
  reports asleep; on the current April 2026 Meta OS build that can leave the
  runtime running in a black or limbo scene that may require a headset restart.
- Do not use remote GUI wake/sleep controls for Sussex. Manual headset
  wake/sleep is the only supported operator path for now.
- Do not enter kiosk mode until the right controller is awake and tracked.
- Do not treat kiosk mode as a reliable right-controller Meta / menu button
  lockout on the current April 2026 Meta OS build. Treat it as best-effort task
  pinning plus screenshot-confirmed foreground only.
- Do not allow an experimenter's bench calibration to survive into the
  participant run. Calibration intentionally performed in the `Experiment
  Session` window before `Start Recording` is participant calibration and must
  survive the recording start command.
- Keep the Sussex APK running across participants unless a real failure
  requires kiosk exit or runtime restart.

## Fixed Session Workflow

### 1. Pre-session readiness with USB attached

1. Connect the headset by USB to establish a trusted ADB path.
2. Probe USB, enable Wi-Fi ADB, and switch the shell onto a working Wi-Fi ADB
   endpoint.
3. Apply the pinned Sussex device profile.
4. When implemented and verified, apply the pinned Sussex brightness and volume
   values.
5. Verify that the staged Sussex APK matches the pinned hash.
6. Verify that the installed headset APK matches the pinned hash. Install or
   update it if it does not.
7. Verify that the headset software version, build id, and display id still
   match the recorded verified Sussex baseline.
8. Verify that headset battery and right-controller battery both meet the study
   threshold.
9. Verify that the right controller is connected and active.
10. Show a single study-ready state only after all checks pass.

Important: USB can stay attached for setup, but the readiness state should
explicitly prefer a live Wi-Fi ADB transport before continuing.

### 2. Boundary plus kiosk entry by the experimenter

1. Disconnect USB.
2. The experimenter puts on the headset in the subject position.
3. Create or confirm the Guardian boundary.
4. Wake the headset if needed. The launcher should read `Wake the headset to
   enable launching` until the headset is awake.
5. Disable proximity before launch. The current launch path also auto-arms the
   keep-awake override, but the guide should still show its state explicitly.
   Expect `CLOSE` while the override is active and `DISABLED` after you restore
   normal proximity.
6. Wake the right controller if needed and verify it is active before kiosk
   entry.
7. Launch Sussex in kiosk mode from the GUI.
8. If Guardian or another Meta visual blocker is still visible, clear it before
   launching.
9. Do not use controller Meta / menu-button behavior as the kiosk-success
   signal on this build.
10. Confirm the visible scene on-headset. If shell focus and the visible scene
   disagree, use one Quest screenshot as the source of truth.

### 3. Bench verification before subject handoff

1. Verify the Unity command path by sending `Particles Off`, `Particles On`,
   and `Recenter`.
2. Verify that the study LSL inlet is connected and visible in the GUI.
3. Keep the headset on-face or leave the keep-awake proximity override active
   while probing. Otherwise the return path can go stale between steps.
4. Start controller breath-volume calibration from the GUI.
5. Finish calibration and visually confirm that the runtime behaved as
   expected.
6. Send a dedicated `Reset Calibration` GUI command so the experimenter's
   calibration cannot leak into the participant run.
7. Send `Particles Off`.
8. Place the headset in the exact starting position for the participant.
8. Put the headset to sleep with the physical power button.

### 4. Participant start

1. When the participant is ready, wake the headset with the physical power
   button.
2. Open the dedicated `Experiment Session` window if it is not already open.
3. Send `Particles On`.
4. Send `Recenter`.
5. Ask the participant to confirm the view.
6. Capture one Quest screenshot as an operator-side confirmation.
7. Enter the participant number in the `Experiment Session` window.
8. If the participant number already exists on this Windows machine, show a
   warning but do not block the run.
9. If the run uses controller breathing, start dynamic-axis or fixed-axis
   controller calibration from the `Experiment Session` window and wait for the
   runtime to accept it.
10. Press `Start Recording`.
11. `Start Recording` should:
   - stamp participant and session metadata
   - reset stale recorder state only, not controller calibration
   - preserve the current participant controller calibration
   - trigger the headset-side experiment start path
   - start companion-side data capture immediately
12. Keep the `Experiment Session` window open for the running session.

### 5. Participant end

1. Press `Stop Recording` in the `Experiment Session` window.
2. `Stop Recording` should:
   - stop companion-side data capture
   - send the headset-side stop and end command
   - pull the Quest-side backup into `device-session-pull`
   - report Quest backup pullback timeout/cancel as a pullback warning while
     preserving the Windows session folder and stopped recorder metadata
   - send `Reset Calibration`
   - send `Particles Off`
   - write final session metadata to disk
   - generate `session_review_report.pdf`
3. The experimenter removes the headset from the participant.
4. Put the headset to sleep with the physical power button.
5. Return to the participant-number entry state without forcing kiosk exit, so
   the next participant can reuse the same prepared runtime.

### 6. Exception path

- If kiosk launch or exit reaches only shell-level confirmation, require a
  Quest screenshot review before proceeding.
- If Guardian, SensorLock, or FocusPlaceholder blockers appear, use the
  recovery notes and physical power-button path instead of trying to automate
  around them inside the normal session workflow.
- If the study runtime contract needs new telemetry keys or new calibration,
  start, or stop commands, implement that in the participant-facing runtime first and then
  refresh the mirrored Sussex APK here.

## Missing Features To Build

### Repo-side features in this repo

1. Fixed workflow state machine in the Sussex shell
   - Add explicit phases such as `Setup`, `Boundary`, `BenchCheck`,
     `ParticipantReady`, `Running`, and `WrapUp`.
   - Gate buttons so only the next safe actions are prominent.
   - Replace the current loose checklist feel with a derived readiness panel
     and a current-step banner.

2. Readiness gate and greenlight model
   - Compute a single `Ready For Kiosk` and `Ready For Participant` state from:
     - Wi-Fi ADB connected
     - pinned APK hash verified
     - pinned device profile active
     - verified OS, build, and display baseline match
     - headset battery above threshold
     - controller battery above threshold
     - right controller connected and active
   - Keep duplicate detail visible in `Inspect`, but keep the main Sussex shell
     binary: blocked or ready.

3. Participant and session controls
   - Add a participant-number field in the Sussex shell.
   - Warn on duplicate participant numbers found under the local study-data
     root.
   - Add `Start Experiment`, `End Experiment`, `Start Calibration`, and
     `Reset Calibration` commands and actions to the study shell.
   - Keep `Particles On`, `Particles Off`, `Recenter`, and `Capture Quest
     Screenshot` visible as secondary tools, not the main workflow driver.

4. Brightness and volume device controls
   - Extend `IQuestControlService` and `WindowsAdbQuestControlService` with
     dedicated brightness and volume setters after device-level write and
     readback is verified.
   - Add those values to the pinned Sussex device profile or to a study policy
     block.
   - Do not gate the stable workflow on them until they have live readback
     coverage and repeatable tests.

5. Companion-side recording service
   - Add a dedicated study recorder service instead of overloading
     `SessionManifestWriter`.
   - Start and stop it from the Sussex shell commands.
   - Store one folder per participant session under
     the current operator-data root under
     `...\ViscerealityCompanion\study-data\sussex-university\<participant>\<session-start-utc>\`.

6. Session settings snapshot
   - Write one immutable settings snapshot per participant run.
   - Include:
     - participant number
     - session start and end timestamps
     - study id and app version
     - APK hash, package id, and version
     - headset OS, build, and display ids
     - pinned device profile id and applied values
     - brightness and volume values once supported
     - active LSL stream name and type
     - operator-facing threshold values used by the gate
     - Windows machine name and current Quest selector

7. Recording UX and failure handling
   - Show clear `Idle`, `Armed`, `Recording`, `Stopping`, and `Faulted`
     recorder states.
   - If data capture cannot start, block `Start Experiment`.
   - If a duplicate participant id is detected, warn but allow explicit
     continue.
   - If writing fails mid-run, surface a hard failure banner and append an
     operator log entry.

8. Tests and harness coverage
   - Unit tests for readiness-gate logic, duplicate-id detection,
     session-folder naming, and data-row formatting.
   - Integration tests for recorder start and stop behavior and settings
     snapshot generation.
   - Extend the Sussex verification harness to cover new calibration, reset,
     start, and end commands once the runtime exposes them.

### Runtime-side features in the participant-facing runtime first, then mirror the APK here

1. New twin commands and control ids
   - `Start Calibration`
   - `Reset Calibration`
   - `Start Experiment`
   - `End Experiment`
   - Optional explicit `Finish Calibration` or `Abort Calibration` if the
     calibration flow is not fully self-contained

2. New published telemetry required for recording
   - headset position and rotation
   - controller position and rotation
   - heartbeat packet arrival and value
   - coherence value routed from the LSL heartbeat packets
   - breathing volume
   - sphere-radius progress
   - raw sphere radius
   - explicit runtime and session state markers for calibration started,
     calibration completed, experiment started, and experiment ended

3. Participant and session metadata sink
   - The runtime needs a way to receive the participant number and any session
     id that should be embedded in headset-side outputs or markers.
   - If that metadata should be visible in `quest_twin_state`, define the key
     names explicitly.

4. Telemetry contract stabilization
   - Freeze the public key names used by the Sussex shell and recorder before
     rolling the data pipeline out to real participants.
   - Avoid renaming keys between APK drops once recording has begun.

## Recommended Data Layout

Use a per-participant folder plus row-oriented long tables, not one wide,
evolving CSV. The row shape should stay stable across signals.

### Folder shape

```text
<operator-data-root>\study-data\sussex-university\
  participant-0007\
    20260329T141530Z\
      device-session-pull\
      session_snapshot.json
      session_settings.json
      session_events.csv
      signals_long.csv
      breathing_trace.csv
      session_review_report.pdf
      quest_screenshot_prestart.png
```

### `signals_long.csv`

Write one row per fixed companion sampling tick. Do not suppress unchanged
values during a run. Recommended columns:

- `participant_id`
- `session_id`
- `recorded_at_utc`
- `source_timestamp_utc`
- `lsl_timestamp_seconds`
- `source`
- `signal_group`
- `signal_name`
- `value_numeric`
- `value_text`
- `unit`
- `sequence`
- `quest_selector`

Minimum signals to record here:

- headset position `x`, `y`, `z`
- headset rotation `qx`, `qy`, `qz`, `qw`
- controller position `x`, `y`, `z`
- controller rotation `qx`, `qy`, `qz`, `qw`
- heartbeat packet values
- coherence values carried by those packets

`recorded_at_utc` should always be the companion write time. When available,
also persist the source-side timestamp separately instead of collapsing
everything into one field.

### `breathing_trace.csv`

Keep the breathing-specific trace separate because it will likely be denser and
frequently analyzed on its own.

Recommended columns:

- `participant_id`
- `session_id`
- `recorded_at_utc`
- `source_timestamp_utc`
- `breath_volume01`
- `sphere_radius_progress01`
- `sphere_radius_raw`
- `controller_calibrated`

### `session_events.csv`

Capture workflow and command markers as discrete events:

- participant number entered
- duplicate-id warning shown
- kiosk launched
- particles on and off
- recenter sent
- calibration started
- calibration reset
- experiment started
- experiment ended
- recording started and stopped
- physical screenshot captured

Recommended columns:

- `participant_id`
- `session_id`
- `recorded_at_utc`
- `source_timestamp_utc`
- `event_name`
- `event_detail`
- `command_action_id`
- `result`

### `session_settings.json`

This is the immutable per-run truth source for what settings were supposed to
be active, even if they are identical across participants.

### `session_snapshot.json`

This is the richer machine-readable session-condition snapshot for QC and
forensic review. It should bundle:

- the full `session_settings.json` payload
- the companion-side view of the pinned headset/app/device-profile state
- the live Sussex twin-state snapshot captured when the run starts
- the effective visual-profile state
- the effective controller-breathing profile state

The goal is that a later reviewer can open a single JSON file in the Windows
session folder and reconstruct the exact session conditions without needing the
headset online.

The pulled Quest session should now mirror the same idea on-device:

- Quest `session_settings.json` remains the compact file/index for row counts,
  file paths, app identity, and recording metadata.
- Quest `session_snapshot.json` is the richer runtime-side snapshot and should
  include the active study snapshot entries, current runtime config summary,
  active capture/payload profile, and the resolved hotload bindings that define
  the APK-side experiment conditions.

### Participant Locked Mode

Participant locked mode is the Sussex runtime mode used during an actual
participant run after tuning is complete.

It should reduce live and Quest-side overhead by disabling the broad legacy
Quest CSV matrix and the verbose twin-state diagnostics that are useful during
tuning, while preserving:

- the Windows-side `signals_long.csv` and `breathing_trace.csv` recorder output
- the Quest-side `signals_long.csv` and `breathing_trace.csv`
- the Quest-side `session_events.csv`, `session_settings.json`, and
  `session_snapshot.json`
- the study snapshot keys needed for breathing, coherence, sphere radius, LSL
  timing/counters, session state, and headset/controller pose

That means participant locked mode is a live-telemetry reduction, not a data
retention reduction.

## Suggested Implementation Order

1. Lock the Sussex workflow and hazard rules in the GUI copy and docs.
2. Add the fixed readiness gate and participant-number flow in the WPF shell.
3. Add runtime command ids and telemetry keys in the participant-facing runtime.
4. Mirror the updated Sussex APK and extend
   `samples/study-shells/sussex-university.json`.
5. Build the recorder and per-participant storage.
6. Add brightness and volume only after live readback is proven stable.
7. Extend the verification harness to cover the new fixed flow.

## Current Code Touchpoints

- `src/ViscerealityCompanion.App/ViewModels/StudyShellViewModel.cs`
- `src/ViscerealityCompanion.App/StudyShellView.xaml`
- `src/ViscerealityCompanion.Core/Models/StudyShellModels.cs`
- `src/ViscerealityCompanion.Core/Services/StudyShellCatalogLoader.cs`
- `src/ViscerealityCompanion.Core/Services/WindowsAdbQuestControlService.cs`
- `src/ViscerealityCompanion.Core/Services/LslTwinModeBridge.cs`
- `samples/study-shells/sussex-university.json`
