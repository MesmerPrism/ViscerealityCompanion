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
- Keep the proximity sensor in normal behavior during the normal Sussex path.
  Do not disable proximity before kiosk entry, and do not leave it disabled
  during kiosk exit. Treat proximity bypass as a manual recovery and debugging
  tool only.
- Do not enter kiosk mode until the right controller is awake and tracked.
- Do not allow the experimenter's calibration to survive into the participant
  run.
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
4. Wake the right controller if needed and verify it is active before kiosk
   entry.
5. Launch Sussex in kiosk mode from the GUI.
6. Confirm the visible scene on-headset. If shell focus and the visible scene
   disagree, use one Quest screenshot as the source of truth.

### 3. Bench verification before subject handoff

1. Verify the Unity command path by sending `Particles Off`, `Particles On`,
   and `Recenter`.
2. Verify that the study LSL inlet is connected and visible in the GUI.
3. Start controller breath-volume calibration from the GUI.
4. Finish calibration and visually confirm that the runtime behaved as
   expected.
5. Send a dedicated `Reset Calibration` GUI command so the experimenter's
   calibration cannot leak into the participant run.
6. Send `Particles Off`.
7. Place the headset in the exact starting position for the participant.
8. Put the headset to sleep with the physical power button.

### 4. Participant start

1. When the participant is ready, wake the headset with the physical power
   button.
2. Send `Particles On`.
3. Send `Recenter`.
4. Ask the participant to confirm the view.
5. Capture one Quest screenshot as an operator-side confirmation.
6. Enter the participant number in the GUI.
7. If the participant number already exists on this Windows machine, show a
   warning but do not block the run.
8. Press `Start Experiment`.
9. `Start Experiment` should:
   - stamp participant and session metadata
   - reset any stale recording state
   - trigger the participant calibration and start path in the headset runtime
   - start companion-side data capture immediately
10. Keep the Sussex shell on the live-monitor surface for the running session.

### 5. Participant end

1. Press `End Experiment`.
2. `End Experiment` should:
   - stop companion-side data capture
   - send the headset-side stop and end command
   - send `Reset Calibration`
   - send `Particles Off`
   - write final session metadata to disk
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
  start, or stop commands, implement that in `AstralKarateDojo` first and then
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
     `%LOCALAPPDATA%\ViscerealityCompanion\study-data\sussex-university\<participant>\<session-start-utc>\`.

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

### Runtime-side features in `AstralKarateDojo` first, then mirror the APK here

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
%LOCALAPPDATA%\ViscerealityCompanion\study-data\sussex-university\
  participant-0007\
    20260329T141530Z\
      session_settings.json
      session_events.csv
      signals_long.csv
      breathing_trace.csv
      quest_screenshot_prestart.png
```

### `signals_long.csv`

Write one row per observed value change or sample. Recommended columns:

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
- `controller_active`
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
- `event_name`
- `event_detail`
- `command_action_id`
- `result`

### `session_settings.json`

This is the immutable per-run truth source for what settings were supposed to
be active, even if they are identical across participants.

## Suggested Implementation Order

1. Lock the Sussex workflow and hazard rules in the GUI copy and docs.
2. Add the fixed readiness gate and participant-number flow in the WPF shell.
3. Add runtime command ids and telemetry keys in `AstralKarateDojo`.
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
