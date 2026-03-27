---
title: Getting Started
description: Clone, build, test, and run the repo directly when you are working on the app instead of using the packaged launcher.
summary: Source-build path for developers and reviewers. Operators should usually start with the packaged download instead.
nav_label: Getting Started
nav_group: Developer Path
nav_order: 70
---

# Getting Started

If you are not changing code and a public preview release exists, use
[Download](download.md) instead of the source-build path below.

## Prerequisites

- Windows 10 or later
- .NET SDK 10.0 or later
- Android platform-tools with `adb` on `PATH` if you want live Quest install and launch
- Node.js 24 or later if you want to build the docs site

## Build And Run

```powershell
git clone <repo-url> ViscerealityCompanion
cd ViscerealityCompanion
dotnet build ViscerealityCompanion.sln
dotnet test ViscerealityCompanion.sln
dotnet run --project src/ViscerealityCompanion.App
```

If Smart App Control or Windows code integrity blocks the multi-file repo build
on this machine, use:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1
```

That path publishes a single-file `win-x64` app into
`artifacts/publish/ViscerealityCompanion.App/`, prunes stale repo-local
`ViscerealityCompanion.exe` copies, refreshes the canonical Desktop/Start Menu
launcher, and launches the verified build.

The app runs against the committed sample session-kit catalogs under
`samples/quest-session-kit/` and the public runtime-config profiles under
`samples/oscillator-config/`.

Study-shell definitions are loaded separately from `samples/study-shells/`. You
can also point the app at an external study-shell folder with
`VISCEREALITY_STUDY_SHELL_ROOT` so new simplified operator windows can be added
without changing the main app binary.

The repo-local session-kit sample is now the preferred default source. It
currently contains one public Quest target, `LslTwin`, plus one scene-matched
runtime baseline. That keeps older internal APK and profile variants out of the
public operator shell unless you explicitly point it at another catalog root.

If `adb` is available, the Windows shell will use it automatically for:

- USB probe and Wi-Fi ADB bootstrap
- direct APK install from the selected file path
- device profile application and explicit CPU/GPU level updates
- app launch and headset status polling

The shell does not auto-select a target on startup anymore. It waits for an
explicit headset snapshot. If the current foreground app is not in the public
catalog, the target remains empty until you choose one manually.

## CLI Tool

```powershell
dotnet run --project src/ViscerealityCompanion.Cli -- --help
dotnet run --project src/ViscerealityCompanion.Cli -- probe
dotnet run --project src/ViscerealityCompanion.Cli -- catalog list --root samples/quest-session-kit
dotnet run --project src/ViscerealityCompanion.Cli -- status
```

See [CLI Reference](cli.md) for the full command set.

## Build The Docs Site

```powershell
npm install
npm run pages:build
```

The generated site is written to `site/`.

## Windows Packaging

If you want the installed single-launcher path locally, use:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Build-App-Package.ps1 -Unsigned
```

That builds the MSIX package scaffolding under `artifacts/windows-installer/`.

## Read Next

- [First Session](first-session.md)
- [Monitoring and Control](monitoring-and-control.md)
- [Runtime Config](runtime-config.md)
