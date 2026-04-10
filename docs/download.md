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

The current packaged preview line tracked by this repo is
`0.1.31.0`.

The recommended preview path is the installed Windows package, not the portable
zip. That gives operators one branded launcher entry, a cleaner update story,
and immediate access to the bundled Sussex APK and bundled Windows liblsl
runtime inside the app payload.

The release ships **both** install paths:

- an automatic guided-setup helper EXE
- a manual certificate + App Installer fallback

Try the automatic helper first. If Windows Smart App Control blocks it, fall
back immediately to the manual certificate path below. Some Windows machines
also disable the `ms-appinstaller:` web-link protocol, so the manual path uses
the downloaded `.appinstaller` file from disk instead of relying on a browser
handoff.

The helper also refreshes the managed official Quest tooling cache under
`%LOCALAPPDATA%\ViscerealityCompanion\tooling` from Meta's published Windows
`hzdb` package and Google's published Android platform-tools package. Those
developer tools remain under their own upstream terms; this repo does not
relicense them under MIT.

<div class="download-start">
  <section class="download-path download-path-primary">
    <h2>Automatic guided setup</h2>
    <p>Try the branded helper first. It automates the normal preview-install path and then hands off to Windows App Installer.</p>
    <div class="action-row">
      <a class="button primary" href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion-Preview-Setup.exe">Download guided setup</a>
      <a class="button" href="https://github.com/MesmerPrism/ViscerealityCompanion/releases">Open release page</a>
    </div>
    <ol class="step-list">
      <li>Download and run <a href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion-Preview-Setup.exe"><code>ViscerealityCompanion-Preview-Setup.exe</code></a>.</li>
      <li>If Windows allows it, let the helper trust the preview certificate, refresh the managed official Quest tooling cache, and open Windows App Installer.</li>
      <li>Install the app, then launch <strong>Viscereality Companion</strong> from the Start menu.</li>
      <li>Inside the app, stay in <code>Sussex University experiment mode</code> and use the sequential guide.</li>
    </ol>
    <p>If Windows Smart App Control blocks the helper, use the manual fallback below.</p>
  </section>

  <section class="download-path">
    <h2>Manual fallback if the helper is blocked</h2>
    <p>Use this path when the helper EXE is blocked, or when Windows disables the <code>ms-appinstaller:</code> browser protocol.</p>
    <div class="action-row">
      <a class="button" href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.cer">Download certificate</a>
      <a class="button" href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.appinstaller">Download App Installer file</a>
      <a class="button" href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.msix">Download MSIX directly</a>
    </div>
    <ol class="step-list">
      <li>Download <a href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.cer"><code>ViscerealityCompanion.cer</code></a>.</li>
      <li>Open it and choose <code>Install Certificate...</code>.</li>
      <li>Select <code>Local Machine</code>.</li>
      <li>Choose <code>Place all certificates in the following store</code>.</li>
      <li>Select <code>Trusted People</code>, then finish the wizard.</li>
      <li>Download <a href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.appinstaller"><code>ViscerealityCompanion.appinstaller</code></a> and open that downloaded file from disk.</li>
      <li>If the downloaded <code>.appinstaller</code> file still refuses to open, install the direct <a href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.msix"><code>.msix</code> package</a> instead.</li>
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
- the bundled read-only Sussex visual profiles shipped in the release build
- the bundled Windows x64 `lsl.dll` runtime used by the built-in TEST sender and live LSL monitor path
- the pinned Quest device profile and study-specific monitoring surface
- the install assets needed to put the Windows app on another machine

Sussex operators should not need a separate APK download if they are using the
packaged preview install.

The current Sussex preview also includes the updated Home/orientation shell,
the sequential guide flow, the controller-breathing profile tab, participant
locked mode, Windows-plus-Quest session snapshots, the LSL/runtime hardening
changes, and the refactored Sussex visual-profile workflow:

- the dedicated `Experiment Session` operator popout for real participant runs
- automatic Quest pullback into `device-session-pull` after normal participant recordings
- automatic session review PDFs for both validation captures and normal participant runs

- the bundled Sussex baseline stays permanently available as a library profile
- the app can also ship additional bundled Sussex visual profiles from the
  release payload, listed ahead of the local writable profile library
- one saved profile can be pinned as the next-launch override
- the visual table edits only the runtime working draft until you explicitly
  save it as a new profile or overwrite a saved one
- `Apply To Current Session` now means runtime-only hotload, not profile
  mutation
- the simplified Sussex visual surface now includes tracer controls, sphere
  radius limits, and the particle-size-relative-to-radius toggle

It also includes the dedicated Sussex breathing-driver controls with explicit
controller-vs-automatic readback on the `During session` tab. The bundled
Sussex APK in this package is `0.1.2` with SHA256
`265168A57323F5A73FEDF310254824D7ABCD71D69EE64BAFC6D9B6EE7A80CA85`.

The refreshed Sussex shell layout now puts the operator's high-level checklist
on `Home`, while `During session` keeps Quest screenshot capture and LSL clock
alignment grouped with the live breathing, coherence, particle, and recenter
checks.

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
5. Check the top app header for the opened-build badge. The installed preview
   should identify itself as `Published install 0.1.31.0`; unpackaged local
   builds explicitly say `Unpackaged build`.

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
dotnet run --project src/ViscerealityCompanion.Cli -- tooling install-official
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
