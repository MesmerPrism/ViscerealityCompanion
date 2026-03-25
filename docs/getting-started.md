---
title: Getting Started
description: Clone, build, test, and run the public Windows shell.
nav_order: 20
---

# Getting Started

## Prerequisites

- Windows 10 or later
- .NET SDK 10.0 or later
- Android platform-tools with `adb` on `PATH` if you want live Quest install and launch
- Node.js 24 or later if you want to build the docs site

## Build And Run

```powershell
git clone <your-public-url> ViscerealityCompanion
cd ViscerealityCompanion
dotnet build ViscerealityCompanion.sln
dotnet test ViscerealityCompanion.sln
dotnet run --project src/ViscerealityCompanion.App
```

### CLI Tool

The repo also includes a command-line interface:

```powershell
dotnet run --project src/ViscerealityCompanion.Cli -- --help
dotnet run --project src/ViscerealityCompanion.Cli -- probe
dotnet run --project src/ViscerealityCompanion.Cli -- catalog list --root samples/quest-session-kit
dotnet run --project src/ViscerealityCompanion.Cli -- status
```

See [CLI Reference](cli.md) for the full command set.

The app runs against the committed sample session-kit catalogs under
`samples/quest-session-kit/` and the public oscillator config profiles under
`samples/oscillator-config/`.

If `adb` is available, the Windows shell will use it automatically for:

- USB probe and Wi-Fi ADB bootstrap
- direct APK install from the selected file path
- device profile application and explicit CPU/GPU level updates
- app launch and headset status polling

Runtime preset and oscillator config edits remain operator-side in the current
remote-only mode and are not pushed into the headset runtime yet.

## Build The Docs Site

```powershell
npm install
npm run pages:build
```

The generated site is written to `site/`.

## Local Private Overlay

If you need to attach local-only code, keep it in ignored paths such as:

- `src/ViscerealityCompanion.Private/`
- `tests/ViscerealityCompanion.Private.Tests/`
- `private/`

Keep the editable config document and its public samples in this repo. Restrict
the private overlay to the live coupling runtime, transport wiring, and any
study-specific compute code.
