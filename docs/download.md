---
title: Download & Install
description: Install the Sussex-focused Viscereality Companion Windows research preview from the latest public release, or fall back to the repo build when no preview package exists yet.
summary: The current public installer is a Sussex-focused preview. The recommended path is the guided setup bootstrapper, which trusts the preview certificate, opens App Installer, and gives the installed app immediate access to the bundled Sussex APK.
nav_label: Download
nav_group: Start Here
nav_order: 15
---

# Download & Install

Use this page when you want the packaged Windows app instead of building the
repo from source.

The current public package is a **Sussex-focused preview**. It is meant for the
`Sussex University experiment mode` workflow first, not yet as the final
general-purpose multi-study release.

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

## What The Sussex Preview Already Includes

- the Windows operator app
- the dedicated `Sussex University experiment mode` shell
- the bundled Sussex APK used by the public Sussex study workflow
- the pinned Quest device profile and study-specific monitoring surface
- the install assets needed to put the Windows app on another machine

Sussex operators should not need a separate APK download if they are using the
packaged preview install.

The current Sussex preview also includes the updated sequential guide flow that
was live-checked on-head on `2026-03-31`, including the validation-capture PDF
preview that is generated directly from the short sample run, the verified
kiosk-exit return to Meta Home, and the current approved Sussex APK hash
`A97BF5467DA61E869690950FE41416CF1F393FA923E6943362A5E5AD1B364CC9`.

## Before You Start

- Windows 10 or later
- a Quest headset with **developer mode enabled**
- one USB cable for the first ADB trust step
- the Windows machine and Quest on the same Wi-Fi network if you want Wi-Fi ADB
- local admin approval for the preview certificate trust step

If developer mode is not already enabled on the headset, fix that before
expecting the companion to install or launch anything.

## Fast Path

1. Download `ViscerealityCompanion-Preview-Setup.exe`.
2. Accept the admin prompt so the helper can trust the preview certificate and open App Installer.
3. Finish the Windows install and launch `Viscereality Companion` from the Start menu.
4. Plug the Quest in once over USB, approve the USB debugging prompt in-headset, then use **Probe USB** and **Enable Wi-Fi ADB** if you want the session on Wi-Fi.
5. Stay in `Sussex University experiment mode`, confirm the bundled Sussex APK, and click **Install Sussex APK**.
6. Click **Apply Study Device Profile**, then **Launch Study Runtime**.

If you see the full app instead of Sussex mode, you are probably running an
older build or a repo-local source build instead of the packaged Sussex preview.

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

If the browser refuses the `ms-appinstaller:` link, download
`ViscerealityCompanion.appinstaller` and open it from disk after trusting
`ViscerealityCompanion.cer`.

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

## Common First-Run Problems

<div class="card-grid">
  <a class="path-card" href="first-session.md">
    <h3>Windows app installed, but Quest install or launch does nothing</h3>
    <p>Check Quest developer mode, USB debugging approval, and the first-session ADB path before assuming the APK is wrong.</p>
  </a>
  <a class="path-card" href="study-shells.md">
    <h3>Sussex mode is missing or the full app opened instead</h3>
    <p>The dedicated Sussex package should start in the locked Sussex shell. If it does not, you are probably on an older build.</p>
  </a>
  <a class="path-card" href="troubleshooting.md">
    <h3>The app installed, but Quest connection still fails</h3>
    <p>Move straight to the troubleshooting guide for USB trust, Wi-Fi ADB, and headset-state issues.</p>
  </a>
  <a class="path-card" href="getting-started.md">
    <h3>No preview release yet</h3>
    <p>Use the source-build path only when the packaged preview is missing or you need to change the repo.</p>
  </a>
</div>

## What You Do Not Need Separately

For the packaged Sussex preview you do not need:

- a local AstralKarateDojo checkout
- a second Sussex APK handoff by email or USB stick
- a separate device-profile file
- the full operator workspace if the Sussex study shell is the intended surface

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
