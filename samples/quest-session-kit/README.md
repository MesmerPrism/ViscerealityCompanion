# Sample Session Kit

This folder mirrors the public catalog contract used by the Android phone
companion and the Windows shell:

- `APKs/library.json`
- `HotloadProfiles/profiles.json`
- `DeviceProfiles/profiles.json`

The committed files are curated public defaults for the current Sussex
operator flow. The repo now defaults to this local session-kit mirror before it
looks for any externally supplied session-kit root on the same machine.

Only the current Sussex app target and one scene-matched hotload baseline
ship here, so stale APK or profile entries from older internal experiments do
not leak into the public operator shell by default.

The committed `APKs/SussexExperiment.apk` currently mirrors the bundled
Sussex controller-breathing build. That keeps the packaged Windows install and
the Sussex study shell self-contained: the app bundle already has the APK it
needs for `Install Sussex APK`.

That APK now lives in Git LFS. If you clone the repo for development or local
packaging, run `git lfs pull` before expecting the real APK bytes to be
available under `APKs/SussexExperiment.apk`.
