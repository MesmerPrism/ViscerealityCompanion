# Sample Session Kit

This folder mirrors the public catalog contract used by the Android phone
companion and the Windows shell:

- `APKs/library.json`
- `HotloadProfiles/profiles.json`
- `DeviceProfiles/profiles.json`

The committed files are safe placeholders only. Replace or extend them locally
when you wire the shell to real Quest payloads.

The sample device profiles now include Quest CPU/GPU level properties so the
Windows app can demonstrate remote performance tuning once `adb` is available.
