# Sussex Performance Audit (2026-04-02)

This audit separates steady-state participant-run cost from tuning-only and operator-side overhead. It uses:

- current Sussex scene wiring in `AstralKarateDojo/Assets/Scenes/SussexControllerStudy.unity`
- current runtime implementations in `AstralKarateDojo/Assets/Scripts/`
- current companion implementations in `src/ViscerealityCompanion.*`
- live Wi-Fi ADB timings measured on the attached headset on 2026-04-02
- the latest pulled Quest session at `C:\Users\tillh\AppData\Local\ViscerealityCompanion\study-data\sussex-university\participant-validation-20260402-144445\session-20260402T144448Z\device-session-pull`

## Verified Headline

The expensive paths are not the new visual/controller-breathing profile applies themselves. The main steady-state cost comes from:

1. full `quest_twin_state` snapshot export on the headset
2. broad Quest-side CSV capture channels that were originally useful for tuning
3. companion-side automatic ADB inspection, especially installed-build hashing

The new profile-apply path is bursty and cheap by comparison.

## Verified Measurements

### Live ADB timings

Measured on the attached headset over Wi-Fi ADB on 2026-04-02:

- `adb shell dumpsys battery`: about `150 ms`
- `adb shell dumpsys tracking`: about `204 ms`
- `adb shell dumpsys power`: about `176 ms`
- `adb shell dumpsys package com.Viscereality.SussexExperiment`: about `179 ms`
- `adb shell screencap -p ...`: about `749 ms`
- `adb pull .../base.apk`: about `1907 ms`
- installed APK size: `58,672,663` bytes
- full `dotnet run --project src/ViscerealityCompanion.Cli -- study status sussex-university`: about `6556 ms`
- tiny hotload CSV push: about `187 ms`
- simple `adb shell input keyevent 82`: about `199 ms`
- screenshot pull of the captured PNG: about `209 ms` for a `73,936` byte file

Interpretation:

- one-off operator actions such as screenshots, particle toggles, and small hotload pushes are acceptable in participant mode
- repeated inspection loops are the problem
- the worst offender on the companion side was repeated installed-build hashing, because it pulled a `58.7 MB` APK over Wi-Fi ADB just to recompute SHA-256

### Latest pulled Quest session

From the pulled Quest `session_settings.json`:

- session duration: `47.065 s`
- `signals_long.csv`: `1442` rows, `30.64 rows/s`
- `breathing_trace.csv`: `413` rows, `8.78 rows/s`
- `clock_alignment_samples.csv`: `241` rows, `5.12 rows/s`
- `timing_markers.csv`: `192` rows, `4.08 rows/s`
- `connection_snapshots.csv`: `90` rows, `1.91 rows/s`
- `lsl_samples.csv`: `48` rows, `1.02 rows/s`

Quest-side file volumes over the same run:

- `biofeedback_signals.csv`: `5,204,730` bytes, about `108.0 KiB/s`
- `biofeedback_raw_signals.csv`: `1,663,229` bytes, about `34.5 KiB/s`
- `signals_long.csv`: `381,728` bytes, about `7.9 KiB/s`
- `breathing_trace.csv`: `86,769` bytes, about `1.8 KiB/s`

Interpretation:

- the focused study recorder files are not the main volume source
- the broad legacy signal channels (`biofeedback_signals.csv`, `biofeedback_raw_signals.csv`) dominate steady-state file output

### Full twin-state snapshot size

Recent Sussex GUI traces already recorded the full snapshot size on the operator side:

- `artifacts/verify/gui-active-sleep-wake-trace-20260327-175942/gui-active-sleep-wake-trace-report.txt`
- reported lines include `Latest quest snapshot rev 111 (224 entries)`

That matters because `LslTwinStateOutletModule` publishes:

- one `begin` frame
- one `set` frame per entry
- one `end` frame

So a `224` entry snapshot means about `226` string frames per publish revision.

The current Sussex scene still has:

- `pollIntervalSeconds: 0.1`
- `republishSnapshotIntervalSeconds: 0.75`
- `skipPublishWhenNoConsumers: 0`
- all major payload categories enabled

and the latest session still shows breathing/radius state changing at about `8.8 Hz`.

Inference:

- during an active run, full twin-state traffic is likely still near the 10 Hz poll ceiling
- with a `224`-entry snapshot, that is on the order of `~2,000` `set` frames per second before LSL framing overhead
- the current focused Windows recorder stores only `29` selected signal names, so the underlying `quest_twin_state` transport is materially larger than the operator-facing reduced recorder output

## Code-Level Cost Centers

### High steady-state demand

`AstralKarateDojo/Assets/Scripts/IndirectParticles/Biofeedback/Transport/LSL/LslTwinStateOutletModule.cs`

- polls every `0.1 s`
- rebuilds the full snapshot, sorts it, hashes it, and republishes the whole structured state whenever anything changed
- scene wiring keeps connection snapshot, showcase routing, signal mirror, resolved driver values, HUD consumer signals, and hotload binding snapshot all enabled
- scene wiring does not skip publishing when there are no consumers

`AstralKarateDojo/Assets/Scripts/Utilities/Runtime/RuntimeLogManager.cs`

- current study scene enables session CSV recording
- current study scene enables `captureBiofeedbackSignalsToCsv`, `captureBiofeedbackRawSignalsToCsv`, `captureLslSamplesToCsv`, `captureConnectionSnapshotsToCsv`, and `captureHudSnapshotsToCsv`
- `biofeedback_signals.csv` and `biofeedback_raw_signals.csv` are the largest steady-state files in the latest live run

`ViscerealityCompanion/src/ViscerealityCompanion.App/ViewModels/StudyShellViewModel.cs`

- `Regular ADB readouts` uses a `1 s` timer
- before this audit it re-ran installed-build hashing on that timer path
- this audit changes the timer path so installed-build hashing stays on the manual refresh path instead of repeatedly pulling the APK

### Moderate steady-state demand

`AstralKarateDojo/Assets/Scripts/Utilities/Runtime/RuntimeHotloadManager.cs`

- polls every `0.5 s`
- opens and SHA-256 hashes the watched config file to detect drift
- useful for tuning, but unnecessary once the study is locked

`AstralKarateDojo/Assets/Scripts/Utilities/Runtime/RuntimeStudyTelemetryBridge.cs`

- runs every frame
- updates performance smoothing, recenter distance, session markers, and live study values
- comparatively light by itself, but it feeds the much heavier twin-state export and CSV capture layers

`ViscerealityCompanion/src/ViscerealityCompanion.Core/Services/StudyDataRecorderService.cs`

- local Windows study recorder writes synchronously with `AutoFlush = true`
- the focused recorder output is much smaller than the Quest-side broad signal capture, but it is still steady-state disk work

### Low or bursty demand

`AstralKarateDojo/Assets/Scripts/Utilities/Runtime/RuntimeStudyClockAlignmentRelay.cs`

- bounded by probe traffic, not full-time frame polling
- acceptable for start/end bursts or sparse background probes

`AstralKarateDojo/Assets/Scripts/Utilities/Runtime/RuntimePerformanceTuningModule.cs`

- periodic reapply timers every few seconds
- not the main overhead source

`AstralKarateDojo/Assets/Scripts/Utilities/Runtime/RuntimeHotloadLiveSyncWriter.cs`

- scene polling exists, but `writeOnlyInEditor = 1`
- no player-side cost in the APK

## Existing "Boost Mode" Is Not Enough

The current `RuntimeLogManager.runtimeLoggingEnabled` / "boost mode" path helps, but it does not create a real participant-safe locked mode by itself.

Why:

- `RuntimePlotManager.ShouldCaptureRuntimeEventsToCsv(...)` and `ShouldCaptureHudSnapshotsToCsv(...)` are gated by `runtimeLoggingEnabled`
- but `captureBiofeedbackSignalsToCsv`, `captureBiofeedbackRawSignalsToCsv`, `captureLslSamplesToCsv`, and `captureConnectionSnapshotsToCsv` are independent flags
- so disabling runtime logging still leaves the broad signal capture channels active unless the capture profile itself changes

That means a real locked mode needs a capture-policy switch, not just the current logging switch.

## Recommended Locked-In Performance Mode

Use the existing architecture and add one explicit experiment mode profile instead of scattered ad hoc toggles.

### 1. Add a runtime telemetry profile in the APK

Recommended modes:

- `Tuning`
- `ParticipantLocked`

`Tuning` keeps the current behavior.

`ParticipantLocked` should:

- keep `study.session.*` essentials
- keep `study.lsl.connected`, `study.lsl.status`, `study.lsl.received_sample_count`, `study.lsl.latest_timestamp`
- keep particle command confirmation keys
- keep recenter confirmation keys
- keep `study.breathing.value01`
- keep a small heartbeat/coherence confirmation surface
- keep `study.performance.fps` and `study.performance.frame_ms` only if operators truly need them
- drop full signal-mirror fanout
- drop resolved driver value export
- drop most inactive tracker diagnostics
- drop full hotload binding mirror once the run starts

Implementation direction:

- add a payload profile to `LslTwinStateOutletModule`
- make `BuildSnapshotEntries` branch on that profile instead of always assembling the maximal payload

### 2. Add a runtime capture profile in the APK

`ParticipantLocked` should not rely on the current broad CSV matrix.

Keep:

- `session_events.csv`
- `breathing_trace.csv`
- a reduced `signals_long.csv`
- `clock_alignment_samples.csv` only during explicit timing windows
- `timing_markers.csv` if timing analysis matters for the study

Disable by default in `ParticipantLocked`:

- `biofeedback_signals.csv`
- `biofeedback_raw_signals.csv`
- `lsl_samples.csv`
- `connection_snapshots.csv`
- `hud_snapshots.csv`

Implementation direction:

- add a capture profile to `RuntimePlotManager`
- let `RuntimeLogManager` switch writer creation from that profile
- do not treat `runtimeLoggingEnabled` as the only gating mechanism

### 3. Freeze hotload after the study is locked

Once the experiment starts, continuous file watching is no longer needed.

Recommended behavior in `ParticipantLocked`:

- allow explicit hotload/profile apply before the participant run
- once the participant run starts, lock the active profile hash
- stop watching for file changes

Implementation direction:

- enable `lockProfileHashAfterFirstSuccessfulApply`
- or add an explicit `FreezeHotloadWatching()` path when the session transitions into participant mode

### 4. Make "no consumer" actually stop twin-state work

The Sussex scene currently keeps:

- `skipPublishWhenNoConsumers: 0` on `LslTwinStateOutletModule`
- `skipPublishWhenNoConsumers: 0` on `RuntimeStudyClockAlignmentRelay`

For locked mode:

- set `skipPublishWhenNoConsumers: 1`
- or expose it as part of the runtime telemetry profile

That prevents the APK from building and publishing full snapshots when the operator is not monitoring live state.

### 5. Keep the companion in manual-inspection mode during real runs

For participant mode on Windows:

- keep `Regular ADB readouts` off by default
- use `Refresh Snapshot` only when needed
- keep screenshot capture available
- keep particle/recenter commands available
- keep reduced live `quest_twin_state` monitoring active

This audit already removes the worst automatic timer-side cost by keeping installed-build hashing on the manual refresh path.

### 6. Reuse existing modules instead of inventing a separate performance stack

The cleanest implementation is:

- `RuntimePerformanceTuningModule` stays responsible for Quest performance policy
- `RuntimePlotManager` gains capture profiles
- `RuntimeLogManager` obeys those capture profiles
- `LslTwinStateOutletModule` gains telemetry payload profiles
- `RuntimeHotloadManager` gains a clear frozen/locked study mode
- the companion study shell exposes one operator-facing mode switch that maps onto those existing systems

## Recommended Rollout Order

1. Add the APK-side telemetry payload profile.
2. Add the APK-side capture profile.
3. Add hotload freeze/lock behavior on participant start.
4. Add the companion-side explicit `ParticipantLocked` mode switch.
5. Re-run the same live session audit and compare file volume, twin-state entry count, and ADB traffic.

## Immediate Conclusion

If the study design is already tuned:

- keep the new visual and controller-breathing profile editors
- keep manual profile apply
- keep screenshots and particle/recenter commands
- keep a reduced breathing/session live state
- stop paying for the full tuning/debug telemetry surface during participant runs

That is where the real savings are.
