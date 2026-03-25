# Sample Session Kit

This folder mirrors the public catalog contract used by the Android phone
companion and the Windows shell:

- `APKs/library.json`
- `HotloadProfiles/profiles.json`
- `DeviceProfiles/profiles.json`

The committed files are curated public defaults for the current `LslTwin`
operator flow. The repo now defaults to this local session-kit mirror before it
falls back to any broader AstralKarateDojo workspace on the same machine.

Only the current `LslTwin` app target and one scene-matched hotload baseline
ship here, so stale APK or profile entries from older internal experiments do
not leak into the public operator shell by default.
