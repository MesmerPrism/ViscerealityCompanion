# Sussex UI Parity Drift Audit - 2026-06-24

## Scope

This audit records why the 2026-06-24 companion work should be parked before
continuing the Sussex breathing-recording fix. It compares the current mixed
working state against the latest published Viscereality Companion release and
the earlier thread decisions in `Diff Astral Karate Dojo state`.

## Published Baseline

Latest public/update-channel release:

- GitHub release: `sussex-timing-marker-msix-0.1.79`
- Windows package version: `0.1.79.0`
- Source commit: `641fb1d20da3f9f15ff60fa968bf23db673ee200`
- Installed package on this machine:
  `MesmerPrism.ViscerealityCompanion_0.1.79.0_x64__zncnfcs118r0y`
- App Installer feed version: `0.1.79.0`
- Installed bundled Sussex APK hash:
  `B73AABFBC114E2B7CDB86D3D7817055572CFC3A73401D4F28AD283FF23425582`

Important caveat: the published package contains the B73 APK, while the
`641fb1d` git LFS pointer still refers to the older 0DB APK object. The local
commit `70b8b25` makes the checkout package-equivalent by changing only the
bundled APK pointer to B73 while keeping the 0.1.79 source and metadata shape.

## Original Issue

Collaborator report:

> Respiration calibrates perfectly before recording, either from setup or the
> experiment window. As soon as Start Recording is pressed, breathing feedback
> stops and recalibration cannot happen. Stopping recording immediately restores
> breathing feedback and calibration.

The code diagnosis from the thread remains narrow:

- Start Recording puts the AKD runtime into participant-locked mode.
- Participant-locked mode reduces the `quest_twin_state` payload.
- The reduced payload hides operator readback groups that the companion uses
  for breathing feedback and recalibration gating:
  - `tracker.breathing.controller.*`
  - `routing.breathing.*`
  - LSL connection/readback keys
- The likely fix is AKD-side and minimal: keep the recording payload reduced,
  but preserve the small operator readback set needed by the companion UI.

## Current Parked WIP State

The current companion WIP branch had moved beyond that narrow issue:

- `70b8b25` changed the bundled APK to the published B73 asset.
- `94614de` changed the bundled APK to the AKD telemetry-fix EAC asset and
  improved the APK sync helper.
- `60a1e5f` added an unverified UI parity acknowledgement flow, CLI command
  guards, harness changes, and documentation.
- The uncommitted layer added ADB/hzdb timeout hardening, runtime JSON fallback
  behavior, harness outlet changes, and another APK/study-shell rollback.

Relative to the published release source commit `641fb1d`, the WIP branch plus
working tree changed 22 files with about 2887 insertions and 140 deletions.
The uncommitted layer alone changed 10 files.

Most concerning uncommitted drift:

- staged rollback of `SussexExperiment.apk` from EAC to 0DB
- staged rollback of Sussex metadata hashes from EAC to 0DB
- removal of the published `conditions` block from
  `samples/study-shells/sussex-university.json`

That rollback does not match the published 0.1.79 package and is a plausible
source of confusing setup/calibration behavior. It should stay parked with the
WIP branch, not be carried into the next breath-tracking test.

## Interpretation

The increasing failure surface was probably caused by mixing several layers:

- the original AKD participant-locked telemetry issue
- companion-side setup and status refresh behavior
- Wi-Fi ADB and APK hash pulls over a slow mobile hotspot
- harness behavior that was not part of normal human operation
- repeated swaps between 0DB, B73, and EAC APK lines

The managed Quest tooling itself did not look stale during the review:

- managed `hzdb` resolved as 1.2.1
- managed Android platform-tools resolved as 37.0.0

The slow hotspot can plausibly explain long or failed Wi-Fi ADB/status reads
and LSL/twin discovery instability, but it does not explain the original
recording-only loss of breathing feedback.

## Reset Plan

1. Park this WIP branch exactly as diagnostic material.
2. Continue from the published-package-equivalent companion baseline:
   source `641fb1d` plus B73 APK asset, represented locally by `70b8b25`.
3. First prepare a no-headset reproduction branch using only the published app
   baseline.
4. Keep the AKD telemetry fix isolated on
   `fix/akd-participant-locked-operator-telemetry-20260624`.
5. When the controller is available, run the live test in two phases:
   - published baseline B73 to reproduce the collaborator's issue
   - minimal AKD telemetry-fix APK to verify the breath-tracking fix
6. Keep ADB/hzdb timeout hardening and harness improvements separate until the
   narrow breathing issue has been reproduced and fixed.
