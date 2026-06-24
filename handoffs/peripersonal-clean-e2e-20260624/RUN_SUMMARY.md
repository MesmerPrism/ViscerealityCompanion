# Completed Run Summary

Run artifact source on Till's machine:

`artifacts/e2e-clean-final-20260624-011935`

Session:

- Participant: `FINALWAKE`
- Session id: `session-clean-final`
- Prepared session folder: `FINALWAKE_session-clean-final_20260623-232315`
- Handedness: `right-handed`
- Breath-tracking controller: `left`
- Final workflow state: `Stopped`

## Operator Path

The run used Companion CLI commands that mirror the operator UI actions:

1. Stop any stale study runtime.
2. Launch the Peripersonal Unity runtime.
3. Prepare session metadata and folder naming.
4. Open MAIA Spatial Questionnaire Block 1.
5. Mark Block 1 submitted.
6. Start the single global recording.
7. Run an LSL clock probe.
8. Set particles visible.
9. Mark XR Block 1 end.
10. Open MAIA Spatial Questionnaire Block 2.
11. Set particles hidden.
12. Mark XR Block 2 end.
13. Open MAIA Spatial Questionnaire Block 3.
14. Stop global recording, pull Quest backup, and close Quest apps.

## Evidence That Passed

- `questionnaire_results.jsonl` contains three completed MAIA Spatial records:
  - `maia_spatial:language_selection`
  - `maia_spatial:spatial_frame_reference_1`, choice `D`
  - `maia_spatial:spatial_frame_reference_2`, choice `E`
- `session_events.csv` records:
  - `recording_started`
  - Block 2 and Block 3 questionnaire launches
  - `application_focus,unfocused` when the panel took input
  - `questionnaire_result`
  - `application_pause,resumed` and `application_focus,focused` after submit
  - `recording_stopped`
- `timing_markers.csv` contains distinct particle markers:
  - `Particles-ON`, `particle_visibility=true`
  - `Particles-OFF`, `particle_visibility=false`
  - XR block-end markers
- Clock alignment evidence exists on both sides:
  - Windows: `clock_alignment_roundtrip.csv`, 15 echoed samples
  - Quest pull: `clock_alignment_samples.csv`, 15 persisted samples
- Stop Recording pulled 11 files from the Quest session folder and closed both Quest apps.
- Final cleanup removed `adb forward tcp:8787`, released Agent Board leases, and left no `dotnet` or `testhost` processes running.

## Latest Foreground-Status Validation

Additional live validation was run on 2026-06-24 after adding foreground/input
ownership beacons to Unity, the questionnaire panel, and the WPF/CLI classifier.

Run artifact source on Till's machine:

`artifacts/foreground-status-live-20260624-101827`

Latest foreground proof:

- Unity runtime APK installed and used:
  `viscereality-peri-personal-runtime.apk`
  SHA-256 `3cf14c777cf67648b829743d48eea21a4ee28da1ac8a0a7049f458bd4c1d95bd`.
- Panel APK installed and used:
  `app-minimal-debug.apk`
  SHA-256 `2768660e9af269f12dc8a39a6c98ce03d0890b4cf459602717983473a8920c3f`.
- Companion CLI `peripersonal foreground-status --wait-seconds 6 --json`
  proved that the Windows classifier receives Quest UDP beacons without ADB.
- After launch, the classifier reported:
  `Peripersonal XR runtime owns input.`
- During a normal MAIA Block 1 open/submit flow, the monitor recorded:
  `window_focus_acquired` from the panel, then `submission_completed`,
  `activity_stopped`, Unity resume/focus events, and final owner `Unity`.
- WPF initially failed to load the Peripersonal shell because the foreground
  status was applied before Peripersonal guide commands were initialized. That
  constructor-order bug is fixed by deferring the initial foreground-status
  apply until after command construction.
- WPF readback after the fix showed the Peripersonal shell `Input owner` card.
- In the real headset state where the panel was resumed but not window-focused,
  WPF now reports:
  `Questionnaire panel is open but not input-focused.`
- After participant submit settles, WPF reports:
  `Peripersonal XR runtime owns input.`

## Known Run Notes

- The first wrapper attempt used a PowerShell helper parameter named `$args`,
  which caused `dotnet` to receive no CLI arguments. No study state was created
  in that failed attempt. The actual clean sequence restarted at the corrected
  helper and all real operator commands completed with exit code `0`.
- The final run switched to a faster no-screenshot mode after the user asked
  for speed while wearing the headset. Later-stage proof is file and event
  based.
- The clock offset value is numerically large because Quest and Windows use
  different local clock bases. The important validation signal is that probes
  were echoed and persisted on both sides.
