---
title: Troubleshooting
description: Common operator and setup issues for the Windows app, ADB path, monitoring path, and packaged launcher.
summary: Start here when the app launches but the Quest session path, monitor path, or packaged install path does not behave as expected.
nav_label: Troubleshooting
nav_group: Support
nav_order: 60
---

# Troubleshooting

## The app says the sample catalog could not be found

Build and run from the repo root, or use the normal `dotnet run --project`
command shown in [Getting Started](getting-started.md). The app looks for the
copied sample catalog in its output folder first, then falls back to the repo
copy under `samples/quest-session-kit/`.

## The app launches but cannot find adb

The app now prefers the managed official Quest tooling cache first, then falls
back to normal Android SDK locations. It looks for `adb.exe` in:

1. `%VISCEREALITY_ADB_EXE%`
2. the current operator-data root under `...\ViscerealityCompanion\tooling\platform-tools\current\platform-tools\adb.exe`
3. `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe`
4. `%ANDROID_SDK_ROOT%\platform-tools\adb.exe`
5. `%ANDROID_HOME%\platform-tools\adb.exe`
6. every entry on `PATH`

If you want the repo to install and update the official Meta/Google tooling for
you, rerun the guided setup helper or run:

```powershell
dotnet run --project src/ViscerealityCompanion.Cli -- tooling install-official
```

If this machine is using the Unity-bundled `adb.exe`, add that folder to
`PATH` or install the normal platform-tools package.

If Wi-Fi ADB only works after manual `adb kill-server` / `adb start-server`
recovery in `cmd`, update to the latest packaged preview first. The Sussex
Wi-Fi bootstrap now restarts the local ADB server automatically and falls back
to parsing `adb shell ip route` when `getprop dhcp.wlan0.ipaddress` does not
return a usable headset IP.

## The app launches but hzdb-dependent actions stay unavailable

Quest screenshot capture, Quest file pullback, and the experiment-shell
proximity helper depend on `hzdb`. The app looks for `hzdb` in:

1. `%VISCEREALITY_HZDB_EXE%`
2. the current operator-data root under `...\ViscerealityCompanion\tooling\hzdb\current\hzdb.exe`
3. `%LOCALAPPDATA%\Microsoft\WinGet\Links\hzdb.exe`
4. every entry on `PATH`
5. `npx.cmd` as a legacy fallback

The preferred public path is the managed LocalAppData cache, not `npx`.
Refresh it with the guided setup helper or:

```powershell
dotnet run --project src/ViscerealityCompanion.Cli -- tooling install-official
```

## The app launches but live LSL features stay unavailable

The packaged preview and portable Windows zip now bundle the official Windows
x64 `lsl.dll`. On those builds, live LSL features should work without a
separate liblsl install.

The exported local agent workspace now mirrors that bundled liblsl copy beside
the bundled CLI and sets `VISCEREALITY_LSL_DLL` in `viscereality.ps1` /
`viscereality.cmd` automatically. If an installed-app local agent still reports
that LSL is missing, do not rely on `tooling status` alone because that command
only reports the managed `hzdb` plus Android platform-tools cache.

Use these instead:

```powershell
.\viscereality.ps1 windows-env analyze
.\viscereality.ps1 study probe-connection sussex-university
```

If LSL still stays unavailable, confirm `lsl.dll` is reachable through one of
the expected locations:

- `%VISCEREALITY_LSL_DLL%`
- next to the app executable
- `runtimes/win-x64/native/lsl.dll`

If the bundled DLL is missing or blocked, the app falls back to preview
messaging for those features.

## Switching between the TEST sender and another LSL sender is unreliable

Treat that as a Windows-side LSL inventory problem first, not as proof that
the headset-side Sussex inlet is broken.

Start with these checks in order:

1. In the Sussex `Pre-session` Bench-tools card, run `Refresh Machine LSL State`.
2. In the same card, run `Analyze Windows Environment`.
3. If the headset path itself is under suspicion, run `Probe Connection` from
   Step 9 in the sequential guide or `study probe-connection sussex-university`
   from the CLI.

`Machine LSL State` is the companion-side view. It compares what the app
believes it owns locally against what Windows currently resolves over liblsl.
The most common failure mode is more than one visible
`HRV_Biofeedback / HRV` publisher on the same machine. That can happen if:

- the companion TEST sender is still advertising after a failed stop
- another companion instance is still open
- an external Python sender is publishing the same stream contract

Current builds now wait for the local TEST sender loop to unwind on stop and
warn when the Windows inventory still shows stale companion-owned streams or
multiple matching upstream publishers.

## Explorer says a Sussex session folder path is not available

Older packaged builds could check a Sussex session folder path from inside the
app and then hand Explorer a non-host-visible alias. Current builds now resolve
the host-visible operator-data root first, so `Open Session Folder`, `Open
Quest Backup`, `Open Session PDF`, and the validation-capture folder buttons
should open the real on-disk location.

For packaged installs, that root is usually:

```text
%LOCALAPPDATA%\Packages\<package-family>\LocalCache\Local\ViscerealityCompanion\
```

For unpackaged/source builds, it remains:

```text
%LOCALAPPDATA%\ViscerealityCompanion\
```

If you are driving the bundled CLI from the exported agent workspace, use the
generated wrappers (`viscereality.ps1`, `viscereality.cmd`, `agent-env.ps1`,
`agent-env.cmd`). They now export `VISCEREALITY_OPERATOR_DATA_ROOT` so the CLI
uses that same host-visible root instead of drifting back to a different bare
LocalAppData path.

## The packaged launcher path is not available yet

If no signed preview release exists, use the source-build path from
[Getting Started](getting-started.md) or build an unsigned package locally with
`tools/app/Build-App-Package.ps1 -Unsigned`.

## Smart App Control blocks the guided setup helper

This is usually not evidence that the release asset is unsigned. The current
preview helper EXE is Authenticode-signed, but with the repo's self-issued
preview certificate. Windows Smart App Control evaluates the downloaded EXE
*before* the helper can add that certificate to `Trusted People`, so fresh
machines can still block the helper outright.

Use this order:

1. Install the preview certificate from `ViscerealityCompanion.cer` into `Local Machine > Trusted People`.
2. Open the downloaded `ViscerealityCompanion.appinstaller` file from disk.
3. If App Installer itself still refuses the feed, install the `.msix` directly.

For release debugging, validate the shipped EXE and MSIX signatures locally:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Test-ReleaseAssetSigning.ps1 `
  -PreviewSetupPath .\artifacts\windows-installer\ViscerealityCompanion-Preview-Setup.exe `
  -PackagePath .\artifacts\windows-installer\ViscerealityCompanion.msix `
  -AllowSelfSigned
```

Current release builds now require an RFC3161 timestamp on both assets. That
improves signature durability, but it does **not** make the helper Smart App
Control-compliant on fresh machines by itself. To make the helper broadly
trusted under Smart App Control, the release workflow must be given a
certificate from a trusted public provider or moved to Microsoft's Trusted
Signing flow.

## The repo-local pinned launcher opens an error instead of the app

Refresh the verified single-file launcher path instead of trusting an older
repo-local executable or shortcut:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1 -Refresh
```

If you only need to rebuild the Desktop and Start Menu shortcuts, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Refresh-Desktop-Launcher.ps1
```

That republish path refreshes the canonical shortcut target and removes stale
repo-local `ViscerealityCompanion.exe` copies so Windows search and taskbar
pins keep resolving to the verified published build.

## The docs site does not build

Confirm Node.js is installed, then run:

```powershell
npm install
npm run pages:build
```

## The Quest app launches but the monitor or twin view stays empty

Check the study build first:

- `quest_monitor` only appears if the APK publishes the lightweight monitor outlet
- twin-state only appears if the APK publishes `quest_twin_state`
- the app can still install, launch, and track foreground state over ADB even when those streams are absent

## I need to change the Quest runtime itself

Do that in `AstralKarateDojo`, not here. This repo is the public Windows app
and onboarding surface around that runtime.
