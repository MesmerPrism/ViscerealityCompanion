---
title: Download & Install
description: Install the packaged Viscereality Companion Windows release from the latest public release, or fall back to the repo build when no package exists yet.
summary: Use the guided setup helper on permissive lab machines, and prefer the direct App Installer path on Smart App Control-protected machines unless the helper has been signed with a publicly trusted certificate.
nav_label: Download
nav_group: Start Here
nav_order: 15
layout: focused
---

# Download & Install

Use this page when you want the packaged Windows app instead of building the
repo from source.

The current public package is **Viscereality Companion**. It is still centered
on the `Sussex University experiment mode` workflow, but the packaged public
line now uses the stable release package family instead of the older preview
family.

The current packaged public release line tracked by this repo is
`0.1.75.0`.

The recommended path is the installed Windows package, not the portable
zip. That gives operators one branded launcher entry, a cleaner update story,
and immediate access to the bundled Sussex APK and bundled Windows liblsl
runtime inside the app payload.

This release also expands the Sussex diagnostics used by the sequential guide
and packaged CLI. `Analyze Windows Environment` checks local liblsl discovery
health, a temporary local LSL outlet rediscovery path, active Windows adapter
hazards, and duplicate expected-stream publishers. `Probe Connection` now
inspects the Windows-side `HRV_Biofeedback / HRV` inventory directly, can
auto-start the built-in companion TEST sender when no expected upstream stream
is visible at all, and reports pinned Sussex APK match, pinned device profile
state, headset Wi-Fi snapshot context, runtime inlet, `quest_twin_state`
return path, and the Windows-visible Quest twin-state outlet source id in one
place.

The release ships **both** install paths:

- a guided-setup helper EXE
- a manual certificate + App Installer path

Start with the guided setup helper on permissive lab machines. On machines with
Smart App Control or other download-reputation policy, use the manual
certificate + App Installer path first unless the helper has been re-signed
with a publicly trusted Authenticode certificate. The manual path also remains
useful on machines that disable the `ms-appinstaller:` browser protocol by
requiring the downloaded `.appinstaller` file to be opened from disk instead.

The helper also refreshes the managed official Quest tooling cache under the
current operator-data root (`...\ViscerealityCompanion\tooling`; for packaged
installs usually `%LOCALAPPDATA%\Packages\<package-family>\LocalCache\Local\ViscerealityCompanion\tooling`)
from Meta's published Windows `hzdb` package and Google's published Android
platform-tools package. Those developer tools remain under their own upstream
terms; this repo does not relicense them under MIT.

<div class="download-start">
  <section class="download-path download-path-primary">
    <h2>Automatic guided setup</h2>
    <p>Use this on normal operator machines. The helper installs or updates the packaged app, refreshes the managed official Quest tooling cache, and then tries to open the app automatically.</p>
    <p>The helper now ships as part of each public release. If Windows still reports that the App Installer feed belongs to an older package family, the helper can offer a cleanup-and-retry path for the retired preview install.</p>
    <div class="action-row">
      <a class="button primary" href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion-Setup.exe">Download guided setup</a>
      <a class="button" href="https://github.com/MesmerPrism/ViscerealityCompanion/releases">Open release page</a>
    </div>
    <ol class="step-list">
      <li>Download and run <a href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion-Setup.exe"><code>ViscerealityCompanion-Setup.exe</code></a>.</li>
      <li>Let the helper trust the package certificate, refresh the managed official Quest tooling cache, and install or update the packaged app.</li>
      <li>When the helper finishes, it should try to open <strong>Viscereality Companion</strong> automatically. If Windows does not bring it forward, launch that Start-menu entry manually.</li>
      <li>Inside the app, stay in <code>Sussex University experiment mode</code> and use the sequential guide.</li>
    </ol>
  </section>

  <section class="download-path">
    <h2>Manual install</h2>
    <p>Use this path first on Smart App Control-protected machines, or any time you want to drive the App Installer flow yourself from disk.</p>
    <div class="action-row">
      <a class="button primary" href="https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.cer">Download certificate</a>
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
    <p>If the helper EXE is blocked by Smart App Control or other Windows reputation policy, use this path instead.</p>
  </section>
</div>

## Direct Downloads

If you know exactly which file you want, use these direct links:

- [Guided setup helper (`ViscerealityCompanion-Setup.exe`)](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion-Setup.exe)
- [App Installer file (`ViscerealityCompanion.appinstaller`)](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.appinstaller)
- [Windows package (`ViscerealityCompanion.msix`)](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.msix)
- [Package signing certificate (`ViscerealityCompanion.cer`)](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion.cer)
- [Portable Windows zip (`ViscerealityCompanion-win-x64.zip`)](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/ViscerealityCompanion-win-x64.zip)
- [CLI zip (`viscereality-cli-win-x64.zip`)](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/viscereality-cli-win-x64.zip)
- [Checksums (`SHA256SUMS.txt`)](https://github.com/MesmerPrism/ViscerealityCompanion/releases/latest/download/SHA256SUMS.txt)

## What The Sussex Release Includes

- the Windows operator app
- the dedicated `Sussex University experiment mode` shell
- the bundled Sussex APK used by the public Sussex study workflow
- the bundled read-only Sussex visual profiles shipped in the release build
- the bundled read-only Sussex controller-breathing profiles, including
  `Small Motion Mild` and the pinned startup `Small Motion Conservative`
- the bundled Windows x64 `lsl.dll` runtime used by the built-in TEST sender and live LSL monitor path
- the pinned Quest device profile and study-specific monitoring surface
- the install assets needed to put the Windows app on another machine
- the app-bundled CLI payload used to seed the current operator-data root under `...\ViscerealityCompanion\agent-workspace` with `viscereality.ps1`, `viscereality.cmd`, the bundled workspace `lsl.dll`, and a ready-made local-agent prompt after first launch

Sussex operators should not need a separate APK download if they are using the
packaged install.

The current Sussex release also includes the updated Home/orientation shell,
the sequential guide flow, the breathing-profiles tab, participant
locked mode, Windows-plus-Quest session snapshots, the LSL/runtime hardening
changes, the controller-tracking calibration guards, and the refactored Sussex
visual-profile workflow:

- the dedicated `Experiment Session` operator popout for real participant runs
- automatic Quest pullback into `device-session-pull` after normal participant recordings
- automatic session review PDFs for both validation captures and normal participant runs
- warning-only Quest backup pullback timeout reporting so a slow `hzdb` pull
  does not hide the saved Windows session folder or poison the whole recorder
  state as faulted
- host-visible packaged operator-data paths for session folders, screenshots,
  logs, tooling, and the exported local agent workspace

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
`EF7DD259FF3ED9101505CEC936585E7B20213993890090BAB3D2DC78C2A30E79`.
Controller vibration is now scene-owned in Sussex and profile-driven for
inhale, exhale, and tracked low-motion retention. Bad tracking and
not-yet-calibrated controller breathing always mean no controller vibration.
`Start Recording` preserves a calibration that the operator completed in the
`Experiment Session` window before recording starts.

The refreshed Sussex shell layout now puts the operator's high-level checklist
on `Home`, while `During session` keeps Quest screenshot capture and LSL clock
alignment grouped with the live breathing, coherence, particle, and recenter
checks.

## Before You Start

- Windows 10 or later
- a Quest headset with **developer mode enabled**
- one USB cable for the first ADB trust step
- the Windows machine and Quest on the same Wi-Fi network if you want Wi-Fi ADB
- local admin approval for the package certificate trust step

If developer mode is not already enabled on the headset, fix that before
expecting the companion to install or launch anything.

## After Install

1. Launch `Viscereality Companion` from the Start menu.
2. Confirm it opens in `Sussex University experiment mode`.
3. Plug the Quest in once over USB and approve the USB debugging prompt in-headset.
4. Use the sequential guide for the full Sussex setup path.
5. Check the top app header for the opened-build badge. The installed package
   should identify itself as `Published install 0.1.75.0`; unpackaged local
   builds explicitly say `Unpackaged build`.

If you see the full app instead of Sussex mode, you are probably running an
older build or a repo-local source build instead of the packaged Sussex
release. If both `Viscereality Companion` and `Viscereality Companion Preview`
appear in Start, use `Viscereality Companion`. The preview-family entry is
legacy migration state only.

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
    <h3>No packaged release yet</h3>
    <p>Use the source-build path only when the packaged release is missing or you need to change the repo.</p>
  </a>
</div>

## What You Do Not Need Separately

For the packaged Sussex release you do not need:

- a separate runtime source checkout
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
