---
title: Download
description: Install the packaged Windows launcher when a preview release exists, or fall back to the repo build when it does not.
summary: Operators should prefer the installed launcher so the app shows up with one branded icon in Windows. The zip and source-build paths remain available.
nav_label: Download
nav_group: Start Here
nav_order: 15
---

# Download

The intended operator path is a packaged Windows install with one launcher entry
in the Start menu and a matching taskbar icon.

## Fastest Operator Path

If you are trying to get to a session quickly:

1. Use the packaged app when a release is available.
2. If no release is available, use `Start-Desktop-App.ps1` from the repo.
3. Then continue with [First Session](first-session.md).

## Install Options

### Packaged app

Use the packaged app when a preview release exists. That path is preferred for
study operators because it gives you:

- one branded launcher entry
- the correct app icon in the taskbar and window chrome
- a cleaner install and update story than running the repo build directly

### Portable zip

The release pipeline also publishes a portable `win-x64` zip. Use that if you
need a no-installer build for a controlled lab machine.

### Source build

If no public preview release exists yet, use the source-build path from
[Getting Started](getting-started.md).

## Release Assets

Tagged releases are set up to publish:

- `ViscerealityCompanion-win-x64.zip`
- `SHA256SUMS.txt`

The repo also includes MSIX packaging scaffolding under
`src/ViscerealityCompanion.App.Package/` and
`tools/app/Build-App-Package.ps1` so the installed launcher path can be shipped
once the signing setup is in place.

## If No Release Exists Yet

Use the repo build directly:

```powershell
dotnet build ViscerealityCompanion.sln
dotnet run --project src/ViscerealityCompanion.App
```

If Windows Smart App Control blocks the unpackaged repo build, use:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1
```

Then continue with [First Session](first-session.md).
