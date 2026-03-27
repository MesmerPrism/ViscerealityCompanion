---
title: Download & Install
description: Install the Viscereality Companion Windows research preview from the latest public release, or fall back to the repo build when no preview package exists yet.
summary: The recommended path is the guided setup bootstrapper, which trusts the preview certificate, opens App Installer, and gives the installed app immediate access to the bundled Sussex APK.
nav_label: Download
nav_group: Start Here
nav_order: 15
---

# Download & Install

Use this page when you want the packaged Windows app instead of building the
repo from source.

The recommended preview path is the installed Windows package, not the portable
zip. That gives operators one branded launcher entry, a cleaner update story,
and immediate access to the bundled Sussex APK inside the app payload.

<div class="action-row">
  <a class="button primary" href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion-Preview-Setup.exe">Guided setup</a>
  <a class="button" href="ms-appinstaller:?source=https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.appinstaller">Install with App Installer</a>
  <a class="button" href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.cer">Download certificate</a>
  <a class="button" href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.appinstaller">Download appinstaller</a>
  <a class="button" href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.msix">Download MSIX</a>
  <a class="button" href="https://github.com/MesmerPrism/ViscerealityCompanion/releases">Open releases</a>
</div>

## Fast path

1. Download `ViscerealityCompanion-Preview-Setup.exe` from the latest release.
2. Accept the Windows admin prompt so the helper can trust the published preview certificate.
3. Let the helper open `ViscerealityCompanion.appinstaller` in App Installer.
4. Finish the install and launch Viscereality Companion from the Start menu.
5. Open the Sussex study shell if that is your experiment. The packaged app already includes the pinned Sussex APK under `samples/quest-session-kit/APKs/LslTwin.apk`.

## What ships in the preview install

The packaged Windows install now includes:

- the WPF operator app
- the sample Quest session kit
- the public study-shell catalog
- the bundled pinned Sussex APK used by `Sussex University experiment mode`

That means Sussex operators do not need a second manual APK handoff just to use
`Install Pinned Build`.

## Manual fallback

Use this if the guided helper is blocked by policy or you prefer to trust the
certificate yourself.

1. Download [ViscerealityCompanion.cer](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.cer).
2. Open the certificate and choose `Install Certificate`.
3. Select `Local Machine`.
4. Choose `Place all certificates in the following store`.
5. Select `Trusted People`.
6. Finish the import, then open `ViscerealityCompanion.appinstaller`.

Until the project uses a certificate from a publicly trusted CA, Windows needs
that trust step before App Installer will accept the research preview package.

## Release assets

Tagged preview releases are set up to publish:

- `ViscerealityCompanion-Preview-Setup.exe`
- `ViscerealityCompanion.msix`
- `ViscerealityCompanion.appinstaller`
- `ViscerealityCompanion.cer`
- `ViscerealityCompanion-win-x64.zip`
- `viscereality-cli-win-x64.zip`
- `SHA256SUMS.txt`

Use the portable zip only if you need a no-installer build for a controlled lab
machine. Use the source-build path only when you are validating or changing the
repo itself.

## If no public release exists yet

Use the repo build directly:

```powershell
dotnet build ViscerealityCompanion.sln
dotnet run --project src/ViscerealityCompanion.App
```

If Windows Smart App Control blocks the unpackaged repo build, use:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1
```

That source-build launcher also refreshes the canonical Desktop/Start Menu
shortcut and removes stale repo-local `ViscerealityCompanion.exe` copies so
Windows Search keeps pointing at the verified publish path.

Then continue with [First Session](first-session.md).
