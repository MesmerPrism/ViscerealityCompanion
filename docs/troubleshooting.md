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

Make sure Android platform-tools are installed. The app looks for `adb.exe` in:

1. `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe`
2. `%ANDROID_SDK_ROOT%\platform-tools\adb.exe`
3. `%ANDROID_HOME%\platform-tools\adb.exe`
4. every entry on `PATH`

If this machine is using the Unity-bundled `adb.exe`, add that folder to
`PATH` or install the normal platform-tools package.

## The app launches but live LSL features stay unavailable

Confirm `lsl.dll` is reachable through one of the expected locations:

- `%VISCEREALITY_LSL_DLL%`
- next to the app executable
- `runtimes/win-x64/native/lsl.dll`

If the DLL is missing, the app falls back to preview messaging for those
features.

## The packaged launcher path is not available yet

If no signed preview release exists, use the source-build path from
[Getting Started](getting-started.md) or build an unsigned package locally with
`tools/app/Build-App-Package.ps1 -Unsigned`.

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
