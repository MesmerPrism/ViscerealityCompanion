---
title: Download & Install
description: Install the Sussex-focused Viscereality Companion Windows research preview from the latest public release, or fall back to the repo build when no preview package exists yet.
summary: The current public installer is a Sussex-focused preview. The recommended path is the signed App Installer package plus the preview certificate; the helper EXE is optional and may be blocked by Smart App Control on some Windows machines.
nav_label: Download
nav_group: Start Here
nav_order: 15
layout: focused
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

The safest public install path is the **certificate + App Installer** route.
The optional helper EXE is still available, but it is an ordinary Windows
bootstrapper and can be blocked by Smart App Control on some machines.

<div class="download-start">
  <section class="download-path download-path-primary">
    <h2>Start Here</h2>
    <p>Use the signed App Installer flow directly. This avoids relying on the optional helper EXE, which Smart App Control may block on some Windows machines.</p>
    <div class="action-row">
      <a class="button primary" href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.cer">Download certificate</a>
      <a class="button" href="ms-appinstaller:?source=https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.appinstaller">Open App Installer</a>
      <a class="button" href="https://github.com/MesmerPrism/ViscerealityCompanion/releases">Open release page</a>
    </div>
    <ol class="step-list">
      <li>Download <a href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.cer"><code>ViscerealityCompanion.cer</code></a>.</li>
      <li>Install it into <code>Local Machine</code> → <code>Trusted People</code>.</li>
      <li>Open <a href="ms-appinstaller:?source=https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.appinstaller">Windows App Installer directly</a> or download <a href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.appinstaller"><code>ViscerealityCompanion.appinstaller</code></a> and open it from disk.</li>
      <li>Install the app, then launch <strong>Viscereality Companion</strong> from the Start menu.</li>
      <li>Inside the app, stay in <code>Sussex University experiment mode</code> and use the sequential guide.</li>
    </ol>
  </section>

  <section class="download-path">
    <h2>Optional Helper EXE</h2>
    <p>Use this only if you want the branded bootstrapper. It downloads the same certificate and then opens App Installer, but Smart App Control may block it because it is a standalone EXE.</p>
    <ol class="step-list">
      <li>Download and run <a href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion-Preview-Setup.exe"><code>ViscerealityCompanion-Preview-Setup.exe</code></a>.</li>
      <li>If Windows blocks it, go back to the certificate + App Installer path above instead.</li>
      <li>If it runs, let it trust the certificate and then hand off to Windows App Installer.</li>
    </ol>
  </section>
</div>

## Direct Downloads

If you know exactly which file you want, use these direct links:

- [Guided setup helper (`ViscerealityCompanion-Preview-Setup.exe`)](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion-Preview-Setup.exe)
- [App Installer file (`ViscerealityCompanion.appinstaller`)](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.appinstaller)
- [Windows package (`ViscerealityCompanion.msix`)](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.msix)
- [Preview signing certificate (`ViscerealityCompanion.cer`)](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.cer)
- [Portable Windows zip (`ViscerealityCompanion-win-x64.zip`)](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion-win-x64.zip)
- [CLI zip (`viscereality-cli-win-x64.zip`)](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/viscereality-cli-win-x64.zip)
- [Checksums (`SHA256SUMS.txt`)](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/SHA256SUMS.txt)

## What The Sussex Preview Includes

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

## After Install

1. Launch `Viscereality Companion` from the Start menu.
2. Confirm it opens in `Sussex University experiment mode`.
3. Plug the Quest in once over USB and approve the USB debugging prompt in-headset.
4. Use the sequential guide for the full Sussex setup path.

If you see the full app instead of Sussex mode, you are probably running an
older build or a repo-local source build instead of the packaged Sussex preview.

Continue with:

- [First Session](first-session.md) for the operator path
- [Study Shells](study-shells.md) for what is bundled into Sussex mode
- [Troubleshooting](troubleshooting.md) if the headset does not respond over USB or Wi-Fi ADB

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

## If You Need A Portable Or Repo Build

Use the portable zip only if you need a no-installer build for a controlled lab
machine:

- [Portable Windows zip (`ViscerealityCompanion-win-x64.zip`)](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion-win-x64.zip)

Use the repo build only when you are validating or changing the source:

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
