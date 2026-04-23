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
2. the current operator-data root under `...\ViscerealityCompanion\tooling\platform-tools\current\platform-tools\adb.exe`
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

## The Companion Updates window still shows `Installed: n/a` after I refreshed the official Quest tools

If the same dialog also says something like:

```text
Official Quest tooling updated
hzdb 1.0.1 | Android platform-tools 37.0.0
```

then the install itself probably succeeded and the dialog was only still
showing its original pre-install snapshot.

That older packaged-window bug affected the per-tool status cards, not the
actual managed tooling cache. The real files and metadata live under the
current operator-data root:

```text
...\ViscerealityCompanion\tooling\hzdb\current\metadata.json
...\ViscerealityCompanion\tooling\platform-tools\current\metadata.json
```

Current builds refresh those cards immediately after the in-app tooling update
completes. If you are validating an older build, confirm the real state with:

```powershell
dotnet run --project src/ViscerealityCompanion.Cli -- tooling status
```

or, from the packaged local-agent workspace:

```powershell
.\viscereality.ps1 tooling status
```

If Wi-Fi ADB only works after manual `adb kill-server` / `adb start-server`
recovery in `cmd`, update to the latest packaged release first. The Sussex
Wi-Fi bootstrap now restarts the local ADB server automatically and falls back
to parsing `adb shell ip route` when `getprop dhcp.wlan0.ipaddress` does not
return a usable headset IP.

## The app launches but hzdb-dependent actions stay unavailable

Quest screenshot capture, Quest file pullback, and the experiment-shell
proximity helper depend on `hzdb`. The app looks for `hzdb` in:

1. `%VISCEREALITY_HZDB_EXE%`
2. the current operator-data root under `...\ViscerealityCompanion\tooling\hzdb\current\hzdb.exe`
3. `%LOCALAPPDATA%\Microsoft\WinGet\Links\hzdb.exe`
4. every entry on `PATH`
5. `npx.cmd` as a legacy fallback

The preferred public path is the managed LocalAppData cache, not `npx`.
Refresh it with the guided setup helper or:

```powershell
dotnet run --project src/ViscerealityCompanion.Cli -- tooling install-official
```

## The guided setup helper finished but the app did not open

Current helper builds now try to launch the packaged app automatically through
its Windows `AppsFolder` target after the MSIX install finishes. If that still
does not bring the app forward on a given machine, treat it as a Windows shell
activation problem, not as proof that the install failed.

Use the normal fallback:

1. Open `Viscereality Companion` from the Start menu.
2. If both `Viscereality Companion` and `Viscereality Companion Preview` exist, use `Viscereality Companion`. The preview entry is legacy migration state only.
3. If you want to verify the installed build explicitly, check `Get-AppxPackage MesmerPrism.ViscerealityCompanion*` in PowerShell.
4. If the app opens and the operator-data root contains the managed tooling metadata shown above, the packaged install succeeded.

## The app launches but live LSL features stay unavailable

The packaged release and portable Windows zip now bundle the official Windows
x64 `lsl.dll`. On those builds, live LSL features should work without a
separate liblsl install.

The exported local agent workspace now mirrors that bundled liblsl copy beside
the bundled CLI and sets `VISCEREALITY_LSL_DLL` in `viscereality.ps1` /
`viscereality.cmd` automatically. If an installed-app local agent still reports
that LSL is missing, do not rely on `tooling status` alone because that command
only reports the managed `hzdb` plus Android platform-tools cache.

Use these instead:

```powershell
.\viscereality.ps1 windows-env analyze
.\viscereality.ps1 study probe-connection sussex-university
```

If LSL still stays unavailable, confirm `lsl.dll` is reachable through one of
the expected locations:

- `%VISCEREALITY_LSL_DLL%`
- next to the app executable
- `runtimes/win-x64/native/lsl.dll`

If the bundled DLL is missing or blocked, the app falls back to degraded
availability messaging for those features.

## Switching between the TEST sender and another LSL sender is unreliable

Treat that as a Windows-side LSL inventory problem first, not as proof that
the headset-side Sussex inlet is broken.

Start with these checks in order:

1. Open the Sussex `Windows environment` page and run `Refresh Machine LSL State`.
2. On the same page, run `Analyze Windows Environment`.
3. If the headset path itself is under suspicion, run `Probe Connection` from
   Step 9 in the sequential guide or `study probe-connection sussex-university`
   from the CLI.

If you need to send the full state to a maintainer, press `Generate Diagnostics
Report` on the same Windows environment page or run:

```powershell
.\viscereality.ps1 study diagnostics-report sussex-university --wait-seconds 15
```

Share the generated report folder, especially `sussex_lsl_twin_diagnostics.pdf`
and `sussex_lsl_twin_diagnostics.json`. That report captures the Windows LSL
checks, duplicate stream inventory, Quest APK/profile state, `quest_twin_state`
publisher visibility, return-path interpretation, and a safe twin command
acknowledgement probe without relying on cropped screenshots.

`Machine LSL State` is the companion-side view. It compares what the app
believes it owns locally against what Windows currently resolves over liblsl.
The most common failure mode is more than one visible
`HRV_Biofeedback / HRV` publisher on the same machine. That can happen if:

- the companion TEST sender is still advertising after a failed stop
- another companion instance is still open
- an external Python sender is publishing the same stream contract

Current builds now wait for the local TEST sender loop to unwind on stop and
warn when the Windows inventory still shows stale companion-owned streams or
multiple matching upstream publishers.

The two probes deliberately cover different halves of the fault tree:

- `Analyze Windows Environment` checks the local Windows machine: liblsl load
  path, active IPv4 adapters, VPN or virtual adapter hazards, multicast support,
  local liblsl discovery health, twin bridge availability, install/launcher
  footprint leftovers, and whether `HRV_Biofeedback / HRV` is visible to this
  PC. The same pass now warns about multiple packaged Viscereality families,
  legacy preview leftovers, stale generic `viscereality.exe` exports in active
  operator-data roots, and unexpected Viscereality shortcut files that can make
  Windows Search or App Installer triage confusing. When a live headset
  selector is available, it also checks the raw PC↔Quest Wi-Fi path: selector
  drift, host/Quest subnet shape, ICMP reachability, and TCP `5555`
  reachability.
- `Probe Connection` checks the headset path: the installed Sussex APK hash
  against the pinned release, the required Quest device profile, the current
  foreground/snapshot Wi-Fi context, the Sussex inlet reported by the runtime,
  whether fresh `quest_twin_state / quest.twin.state` frames are returning to
  Windows, and whether Windows can even see the Quest-owned twin-state outlet
  plus its `source_id`.

That split is important. A Quest can visibly consume the TEST sender while
Windows-side stream discovery fails with a socket/adapter `set_option` error,
or while the Quest-to-Windows `quest_twin_state` return path is missing. In the
first case, reduce Windows adapter variables first: disable unused VPN or
virtual adapters, keep the active Wi-Fi network Private, allow the packaged app
through Windows Firewall on Private networks, and confirm the router is not
using client isolation. In the second case, keep Sussex visibly foregrounded and
debug the return telemetry before trusting calibration, recording confirmation,
or routed coherence readback in the GUI.

If the new `Quest Wi-Fi transport path` row fails while the SSIDs still match,
that is the clearest router diagnosis the shell can currently give. In that
state, stop blaming liblsl or the Sussex APK first; move both devices to a
different router/AP, disable guest/client isolation, or use a lab Wi-Fi that
permits direct client-to-client traffic.

For the return-path case, read the `Twin-state outlet` line in `Probe
Connection` or `viscereality study probe-connection ... --json`:

- `No Quest twin-state outlet is visible on Windows` means the forward
  `HRV_Biofeedback / HRV` path may still be working, but the Quest-originated
  LSL publisher is not being discovered by Windows. Check router client
  isolation, Quest foreground/awake state, and whether the Sussex scene has
  fully started its twin-state outlet.
- `Quest twin-state outlet is visible, but not from the expected Sussex
  source_id` means Windows can see a Quest publisher, but the companion's strict
  bridge will ignore it because it does not match the pinned package/source-id
  contract. Reinstall the pinned APK and capture the JSON output for the exact
  observed source id.
- `A twin-state stream is visible, but it does not match the Quest source-id
  contract` usually points at an older or hand-built runtime still advertising
  a legacy source id such as `quest.twin.state`.

That duplicate-source warning is advisory for the Windows-side inventory. The
Experiment Session coherence card follows the Quest-reported routed value from
`quest_twin_state`, including the newer `study.lsl.latest_default_value` /
`study.lsl.latest_ch0_value` fields when the direct `signal01.coherence_lsl`
mirror is absent. If the Quest is processing an external Python sender but the
Windows environment page warns about two publishers, stop the extra publisher
or stale TEST sender to make future stream resolution deterministic; do not
interpret the warning by itself as proof that Sussex stopped consuming the LSL
inlet.

During participant recordings, `upstream_lsl_monitor.csv` now includes the
resolved LSL `source_id` for the passive Windows-side inlet. Use that column to
confirm which publisher Windows latched onto when duplicate
`HRV_Biofeedback / HRV` sources were visible.

Current builds also split `Analyze Windows Environment` into more precise LSL
hazard checks:

- `Windows network adapter hazards` lists active IPv4 adapters and warns when
  VPN, Tailscale, WireGuard, Hyper-V, Docker, WSL, TAP/Wintun, VirtualBox,
  VMware, multiple default gateways, or adapters without multicast support are
  visible.
- `Windows liblsl discovery self-check` runs a deliberately unique local lookup
  before checking `HRV_Biofeedback / HRV`. If this fails with `set_option` or
  `The requested address is not valid in its context`, the Windows LSL
  inventory is unhealthy even if the Quest still receives samples from a sender.
- `Windows LSL loopback outlet self-check` opens a temporary local LSL outlet
  and tries to rediscover it from the same process. If the TEST sender says it
  is active but this loopback check fails, debug Windows LSL advertisement,
  firewall/profile, and adapter state before debugging the Quest inlet.
- `Expected Sussex LSL stream` then reports whether the actual upstream
  `HRV_Biofeedback / HRV` source is visible to Windows.

This separation is important for intermittent lab machines. A headset can
consume the TEST sender while the Windows GUI cannot reliably enumerate streams
or receive `quest_twin_state`. In that case, focus first on Windows network
shape: disable unused VPN/virtual adapters, keep only the lab Wi-Fi/Ethernet
path active if possible, set the active network profile to Private, and allow
the packaged app through Windows Firewall on Private networks.

## Stop Recording reports that the operation was canceled

If this happens during **Stop Recording** or **Stop participant recording**,
first check whether the cancellation occurred during Quest backup pullback
rather than during the Windows recording itself.

The stop path is:

1. Stop regular Windows recording samples.
2. Stop background clock-alignment monitoring.
3. Run the end clock-alignment burst.
4. Send `End Experiment`.
5. Confirm the Quest recorder stopped.
6. Pull Quest backup files into the Windows session folder.
7. Write the final stopped marker and complete the local recorder.

Older builds surfaced a timeout or cancellation in step 6 as a generic recorder
fault. Current builds distinguish that case: if `hzdb files ls` or
`hzdb files pull` times out while copying the Quest backup, the operator should
see a Quest-backup pullback warning and the Windows session folder path instead
of a misleading whole-recorder failure.

Inspect the reported session folder under the current operator-data root,
usually one of:

```text
%LOCALAPPDATA%\ViscerealityCompanion\study-data\...
%LOCALAPPDATA%\Packages\<package-family>\LocalCache\Local\ViscerealityCompanion\study-data\...
```

Check for:

- `session_events.csv`
- `signals_long.csv`
- `breathing_trace.csv`
- `clock_alignment_roundtrip.csv`
- `upstream_lsl_monitor.csv`
- `device-session-pull\`

If the Windows files are present but `device-session-pull` is absent, empty, or
partial, treat the run as a Quest backup pullback problem. The usual causes are
a slow or dropped Wi-Fi ADB connection, the headset entering an odd foreground
or awake/asleep state, another ADB or `hzdb` process contending for the device,
or Windows-side IO/security pressure making the `hzdb` subprocess exceed its
timeout.

## Explorer says a Sussex session folder path is not available

Older packaged builds could check a Sussex session folder path from inside the
app and then hand Explorer a non-host-visible alias. Current builds now resolve
the host-visible operator-data root first, so `Open Session Folder`, `Open
Quest Backup`, `Open Session PDF`, and the validation-capture folder buttons
should open the real on-disk location.

For packaged installs, that root is usually:

```text
%LOCALAPPDATA%\Packages\<package-family>\LocalCache\Local\ViscerealityCompanion\
```

For unpackaged/source builds, it remains:

```text
%LOCALAPPDATA%\ViscerealityCompanion\
```

If you are driving the bundled CLI from the exported agent workspace, use the
generated wrappers (`viscereality.ps1`, `viscereality.cmd`, `agent-env.ps1`,
`agent-env.cmd`). They now export `VISCEREALITY_OPERATOR_DATA_ROOT` so the CLI
uses that same host-visible root instead of drifting back to a different bare
LocalAppData path.

## The Sussex validation PDF fails even though the Windows session folder exists

This is separate from Quest pullback. The PDF is generated from the Windows
session folder after the run, so it can fail even when `Open Windows Session
Folder` works.

Current public builds render Sussex validation PDFs natively in .NET. They do
not depend on a machine-local Python, `numpy`, or `matplotlib` installation.

If PDF generation still fails, the remaining likely causes are:

- the Windows session folder is missing one of the expected recorder artifacts
  such as `session_settings.json`, `signals_long.csv`,
  `breathing_trace.csv`, or `clock_alignment_roundtrip.csv`
- one of those artifacts exists but is truncated, malformed, or unreadable
- the session folder was moved or deleted before the report renderer finished

The failure detail now comes from the native renderer itself. Treat Quest
pullback gaps and PDF generation as separate signals: a Quest backup warning
does not automatically mean the Windows-side recorder data was lost.

## The packaged launcher path is not available yet

If no signed packaged release exists, use the source-build path from
[Getting Started](getting-started.md) or build an unsigned package locally with
`tools/app/Build-App-Package.ps1 -Unsigned`.

## Smart App Control blocks the guided setup helper

This is usually not evidence that the release asset is unsigned. The current
guided setup helper EXE may still be Authenticode-signed with the repo's
self-issued package certificate. Windows Smart App Control evaluates the
downloaded EXE
*before* the helper can add that certificate to `Trusted People`, so fresh
machines can still block the helper outright even though the signature itself is
present and timestamped.

Use this order:

1. Install the package certificate from `ViscerealityCompanion.cer` into `Local Machine > Trusted People`.
2. Open the downloaded `ViscerealityCompanion.appinstaller` file from disk.
3. If App Installer itself still refuses the feed, install the `.msix` directly.

For release debugging, validate the shipped EXE and MSIX signatures locally:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Test-ReleaseAssetSigning.ps1 `
  -PreviewSetupPath .\artifacts\windows-installer\ViscerealityCompanion-Setup.exe `
  -PackagePath .\artifacts\windows-installer\ViscerealityCompanion.msix `
  -AllowSelfSignedPreviewSetup `
  -AllowSelfSignedPackage
```

Current release builds now require an RFC3161 timestamp on both assets. That
improves signature durability, but it does **not** make the helper Smart App
Control-compliant on fresh machines by itself. To make the helper broadly
trusted under Smart App Control, the release workflow must sign the helper with
a certificate from a trusted public provider or move that helper-signing path
to Microsoft's Trusted Signing flow. The workflow supports a separate guided
setup signing certificate via the existing
`WINDOWS_PREVIEW_SETUP_CERTIFICATE_BASE64` and
`WINDOWS_PREVIEW_SETUP_CERTIFICATE_PASSWORD` secrets so the helper can use a
public Authenticode signer without changing the self-signed MSIX sideloading
path. If that dedicated signer is absent, the workflow now signs the freshly
built helper with the package certificate instead of reusing an old pinned
bootstrapper asset.

If the packaged app itself is blocked **after** the MSIX installs, inspect the
Code Integrity log for the specific package family and executable path:

```powershell
Get-WinEvent -LogName 'Microsoft-Windows-CodeIntegrity/Operational' -MaxEvents 100 |
  Where-Object { $_.Message -match 'MesmerPrism\\.ViscerealityCompanion|ViscerealityCompanion\\.exe' }
```

The release MSIX must preserve the normal WAP multi-file payload inside
`ViscerealityCompanion.App`. Do not replace that payload with a repacked
single-file desktop publish, and for this repo do not repack the finished WAP
layout just to inject Authenticode signatures into the inner desktop payload
files. Local validation on this machine family showed that the native
WAP-produced package still launched cleanly, while the manually repacked
payload variant installed successfully but then tripped Code Integrity on
`ViscerealityCompanion.exe`. Current release validation checks that the package
still contains both `ViscerealityCompanion.App/ViscerealityCompanion.exe` and
`ViscerealityCompanion.App/ViscerealityCompanion.dll`.

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
pins keep resolving to the right app. If the packaged MSIX release is installed,
the canonical `Viscereality Companion.lnk` shortcut now tries the packaged app
first and falls back to the repo-local published build when Windows refuses to
start the packaged process.

## The docs site does not build

Confirm Node.js is installed, then run:

```powershell
npm ci
npm run pages:build
```

## The Quest app launches but the monitor or twin view stays empty

Check the study build first:

- `quest_monitor` only appears if the APK publishes the lightweight monitor outlet
- twin-state only appears if the APK publishes `quest_twin_state`
- the app can still install, launch, and track foreground state over ADB even when those streams are absent

## I need to change the Quest runtime itself

Do that in the participant-facing Quest runtime, not here. This repo is the
public Windows app and onboarding surface around that runtime.
