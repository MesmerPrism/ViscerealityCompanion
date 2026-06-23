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
