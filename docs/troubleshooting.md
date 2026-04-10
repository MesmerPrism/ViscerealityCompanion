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
2. `%LOCALAPPDATA%\ViscerealityCompanion\tooling\platform-tools\current\platform-tools\adb.exe`
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
2. `%LOCALAPPDATA%\ViscerealityCompanion\tooling\hzdb\current\hzdb.exe`
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

If LSL still stays unavailable, confirm `lsl.dll` is reachable through one of
the expected locations:

- `%VISCEREALITY_LSL_DLL%`
- next to the app executable
- `runtimes/win-x64/native/lsl.dll`

If the bundled DLL is missing or blocked, the app falls back to preview
messaging for those features.

## The packaged launcher path is not available yet

If no signed preview release exists, use the source-build path from
[Getting Started](getting-started.md) or build an unsigned package locally with
`tools/app/Build-App-Package.ps1 -Unsigned`.

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
