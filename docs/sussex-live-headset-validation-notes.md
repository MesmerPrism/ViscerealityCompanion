# Sussex Live Headset Validation Notes

Use this file as the running checklist for the next full on-head headset pass. Add findings here before or after the run so they do not get lost between coding sessions.

## Current Readiness Snapshot

- Companion repo branch: `main`
- Companion repo state at this check: `pending 2026-04-15 Sussex controller vibration, calibration guard, and LSL release updates`
- Fresh approved Sussex APK build prepared on `2026-04-15`
- Current Sussex APK SHA-256:
  `EF7DD259FF3ED9101505CEC936585E7B20213993890090BAB3D2DC78C2A30E79`
- Mirrored companion bundle refreshed to the same hash at:
  `C:\Users\tillh\source\repos\ViscerealityCompanion\samples\quest-session-kit\APKs\SussexExperiment.apk`
- Current Windows installer artifacts refreshed at:
  `C:\Users\tillh\source\repos\ViscerealityCompanion\artifacts\windows-installer`
- Current readiness caveat: the rebuilt `0.1.2` bundle now has a fresh CLI-driven off-face validation of controller command feedback, controller-tracking calibration guards, and external Python LSL coherence routing, but the next worn-head pass should still recheck kiosk exit and the full participant-facing handoff path.
- Quest OS baseline still matches the approved Sussex software identity:
  `14 | build 2921110053000610 | display UP1A.231005.007.A1`
- Wi-Fi ADB was last confirmed live during the previous readiness pass on:
  `192.168.2.56:5555`
- Headset Wi-Fi and PC Wi-Fi last matched during the previous readiness pass:
  `MagentaWLAN-R5V4`
- Companion build/test passed after the timing-contract update and APK sync.
- Participant-facing runtime validation checks passed before the fresh Sussex APK build.
- Expected note for the next worn-head run: controller-breathing calibration should be started from the `Experiment Session` window and preserved across `Start Recording`; the rebuilt `0.1.2` package still needs a worn-head recheck of kiosk exit and the full participant handoff path.
- With the controller disconnected, the pinned device profile now stays active with advisory battery warnings instead of downgrading the Sussex build status.

## Latest Live Result

- The last completed live Sussex validation pass completed successfully on `2026-04-03` with the headset exercised off-face.
- That pass used the accepted published GUI path instead of a freshly republished harness executable because Windows Application Control still blocks newly published local harness binaries on this machine.
- The run confirmed `Use Automatic Driver`, `Pause Automatic`, `Start Automatic`, and `Use Controller Volume Driver` end to end, including live GUI readback and on-headset value movement on the current `0.1.2` Sussex APK.
- The same off-face validation also reconfirmed the pinned-build hash path against the current approved Sussex APK and left kiosk exit intentionally skipped to avoid the known off-face passthrough limbo path.
- Latest live-validation log:
  `C:\Users\tillh\source\repos\ViscerealityCompanion\artifacts\verify\sussex-manual-accept-run\automation-log.txt`
- Latest live-validation screenshots:
  `C:\Users\tillh\source\repos\ViscerealityCompanion\artifacts\verify\sussex-manual-accept-run`
- Latest completed live-pass APK hash:
  `AFB296E22A5FFE1F648AC32D73CAA6CE3B335EAFFAD2A2B1847D16DDB06ECA29`
- Remaining live caveat:
  the sparse background clock-alignment probe loop starts and logs its own start/stop events, but it still does not receive Quest echoes during the short harness run.

Additional CLI-driven validation completed on `2026-04-15` with the current
bundled APK hash:

- installed and launched the refreshed Sussex APK
- confirmed the runtime foreground and captured a fresh Quest screenshot
- sent reset and start-calibration commands through the public CLI/LSL command
  path
- confirmed the APK echoed the latest command id, controller tracking state,
  calibration guard state, dynamic-axis validation state, and low-motion
  rejection counters through `quest_twin_state`
- ran an external Python `HRV_Biofeedback / HRV` sender instead of the
  companion TEST sender and confirmed the headset received samples and mirrored
  the routed coherence value back to the desktop state

## Core Goal

Validate the full Sussex experiment flow on a live headset with the headset worn by a person, not sitting off-head on the bench.

## Known Hazards

- Use the physical power button for sleep and wake. Do not use ADB awake or sleep commands for the participant handoff path.
- The current Sussex launch path intentionally uses the direct `prox_close`
  keep-awake override. Treat `Virtual proximity state: CLOSE` as override
  active, and `Virtual proximity state: DISABLED` as normal wear-sensor
  behavior restored after `automation_disable`.
- Wake the right controller before kiosk entry. If it is asleep during the transition, it may stay inactive afterward.
- Off-head kiosk exit can still behave differently from on-head exit. Judge the final runtime path from an actual worn-head test.
- Manual headset-side volume may still be required if Wi-Fi ADB volume enforcement does not stick.

## Before The Run

- Confirm the companion build is the intended branch and commit.
- Confirm the mirrored `SussexExperiment.apk` hash matches the pinned hash in the companion bundle.
- Confirm the headset OS build still matches the approved Sussex software baseline.
- Confirm the headset is on the same Wi-Fi network as the Windows machine.
- Confirm the WPF app can reach the headset over Wi-Fi ADB after USB is unplugged.
- Confirm the experiment heartbeat / coherence LSL source is running on the Windows side before kiosk launch.

## Sequential Guide Checks

- USB probe succeeds while the cable is attached.
- Wi-Fi ADB enables cleanly and reconnect target is saved.
- Headset Wi-Fi name matches the PC Wi-Fi name.
- USB unplug step stays blocked while USB is still attached and turns green after unplug.
- APK check shows the correct Sussex package and expected APK hash.
- Device profile check reports CPU, GPU, brightness, volume, headset battery, controller battery, and OS build correctly.
- Boundary step is followed manually before kiosk launch.
- Kiosk launch succeeds over Wi-Fi ADB.
- LSL connection shows as connected both on the headset side and in the WPF app.
- Particle on/off test responds correctly.
- Quest screenshot capture updates with a fresh runtime image each time, not a stale cached frame.
- Breathing calibration is guarded by right-controller tracking. If the
  controller is not tracked, the GUI should report that state and the runtime
  should not enter calibration.
- During controller calibration, the compact calibration-quality block should show a sensible `Good`, `Degraded`, `Poor`, or `Stalled` summary instead of raw counter spam.
- The 20 second validation capture completes and writes both Windows and Quest-side folders.

## Live Runtime Checks

- The participant can see the expected scene after wake and recenter.
- The operator can verify the scene both visually in-headset and from a captured Quest screenshot.
- Recenter visibly changes the scene alignment when requested.
- Particle visibility commands visibly change the runtime state.
- The right controller remains active after kiosk entry.
- The runtime remains controllable after USB is unplugged.
- LSL coherence values keep arriving during the run.
- The clock-alignment window completes its 10 second handshake at experiment start.
- The clock-alignment window also completes the matching end burst cleanly at experiment stop.
- Sparse background clock probes are logged during the run without interrupting the participant flow.
- If controller calibration is tried, the quality block should explain obvious failure modes quickly, especially unstable tracking, too-little movement, or stalled accepted frames.

## Data Checks

- Windows session folder is created under the expected participant folder.
- Quest device session folder is created for the same participant and session id.
- `datasetId`, `datasetHash`, and `settingsHash` match between Windows and Quest outputs.
- `session_settings.json` is present on both sides.
- `session_events.csv` is present on both sides.
- `signals_long.csv` is present on both sides.
- `breathing_trace.csv` is present on both sides.
- `clock_alignment_roundtrip.csv` is present on Windows.
- `clock_alignment_samples.csv` is present in the pulled Quest data.
- `upstream_lsl_monitor.csv` is present on Windows.
- `timing_markers.csv` is present in the pulled Quest data.
- Shared traces between Windows and Quest look aligned enough for comparison.
- Windows recording scope and Quest recording scope are reviewed for expected start and stop differences.
- Timing markers include at least:
  `heartbeat_packet_receive`
  `heartbeat_real_beat_publish`
  `coherence_packet_receive`
  `coherence_value_publish`
  `orbit_radius_peak`

## Visual Review After The Run

- Open the quick validation plots in the sequential guide and check that breathing and coherence traces are plausible.
- Open the generated PDF preview report from the validation-capture step and confirm that breathing, coherence, and clock-alignment traces look plausible.
- Compare Windows vs Quest traces for shared signals to spot lag, clipping, or dropouts.
- Verify that screenshots used for validation actually correspond to the current runtime state.
- Inspect whether `orbit_radius_peak` lands where the participant-facing visual expansion appears strongest.

## Questions To Resolve During The Next Run

- Is Wi-Fi ADB volume control reliable enough to remove the manual volume fallback?
- Is breathing calibration stable enough to become a required guide step again?
- Does clock alignment produce a stable enough offset estimate across repeated runs?
- Do Windows and Quest recording windows now align closely enough after the clock-alignment and recorder-scope changes?
- Does the passive upstream Windows LSL monitor line up cleanly with the Quest timing markers for heartbeat/coherence delivery?
- How close is `orbit_radius_peak` to the earliest participant-visible peak in the rendered particle motion?
- Are there any participant-facing visual issues after sleep, wake, recenter, or kiosk exit that only appear on-head?

## Log Useful Artifacts Here

- Companion log folder:
  `C:\Users\tillh\AppData\Local\ViscerealityCompanion\logs`
- Windows study data root:
  `C:\Users\tillh\AppData\Local\ViscerealityCompanion\study-data`
- Verification harness artifacts:
  `C:\Users\tillh\source\repos\ViscerealityCompanion\artifacts\verify`
