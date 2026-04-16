---
title: CLI Reference
description: Command-line interface for the ViscerealityCompanion operator station.
summary: The CLI mirrors the desktop app for scripted ADB, LSL, and twin workflows when you do not want the WPF shell.
nav_label: CLI Reference
nav_group: Developer Path
nav_order: 80
---

# CLI Reference

The `viscereality` CLI mirrors the WPF desktop app's capabilities for
scripting, automation, and headless operation.

Installed Sussex preview builds now expose a local agent workspace under
the current operator-data root. For unpackaged/source builds that is typically
`%LOCALAPPDATA%\ViscerealityCompanion\agent-workspace`; for packaged MSIX
installs it is the host-visible packaged path under
`%LOCALAPPDATA%\Packages\<package-family>\LocalCache\Local\ViscerealityCompanion\agent-workspace`.
The Sussex shell Home page includes `Open Agent Workspace` plus `Copy Local
Agent Prompt` so installed-app users can hand a local agent a bundled
`viscereality` command, the mirrored CLI docs, and the Sussex example
catalogs without pointing it at the protected WindowsApps payload. The
generated workspace includes `cli/current/viscereality.exe` plus
`viscereality.ps1` and `viscereality.cmd` wrapper scripts that preload the
mirrored sample-root overrides, point `VISCEREALITY_LSL_DLL` at the bundled
workspace copy when it is present, and export `VISCEREALITY_OPERATOR_DATA_ROOT`
so the bundled CLI uses the same host-visible operator-data root as the app.

For source builds, the simplest way to stage the official Quest-side developer
tools the app expects is:

```powershell
dotnet run --project src/ViscerealityCompanion.Cli -- tooling install-official
```

## Sussex Workflow Split

For the Sussex study workflow there is now a deliberate split between CLI and
GUI surfaces:

- CLI:
  - deterministic setup, install, launch, status, and profile operations
  - machine-readable inspection with `--json`
- `Sequential Guide` window:
  - one pre-session verification pass directly before a real participant
- `Experiment Session` window:
  - the live participant-run surface for participant id entry, `Start
    Recording`, `Stop Recording`, live telemetry, clock/network consistency,
    recenter, particle toggles, screenshots, direct dynamic-axis / fixed-axis
    calibration start controls, and quick access to the session folder, pulled
    Quest backup, and session review PDF

The CLI currently mirrors the setup and profile side of Sussex. It does not
yet replace the participant-run `Start Recording` / `Stop Recording` flow in
the `Experiment Session` window. After `Stop Recording`, the GUI now pulls the
Quest backup into `device-session-pull` and generates `session_review_report.pdf`
inside the same participant session folder.

## Running

From the guided-install local agent workspace under
the current operator-data root, prefer:

```powershell
.\viscereality.ps1 --help
```

That wrapper preloads the mirrored sample-root overrides before invoking the
bundled CLI under `cli/current`, and it now exports the bundled workspace
`lsl.dll` path automatically when that copy is present. It also exports
`VISCEREALITY_OPERATOR_DATA_ROOT` so the CLI keeps using the same host-visible
operator-data root as the packaged app.

From a source checkout, run:

```powershell
dotnet run --project src/ViscerealityCompanion.Cli -- <command> [options]
```

Or install as a global tool:

```powershell
dotnet pack src/ViscerealityCompanion.Cli
dotnet tool install --global --add-source src/ViscerealityCompanion.Cli/bin/Release viscereality
viscereality --help
```

## Commands

### Device Connection

| Command | Description |
|---------|-------------|
| `probe` | Detect Quest devices connected via USB |
| `wifi` | Enable Wi-Fi ADB on the USB-connected Quest |
| `connect <endpoint>` | Connect to a Quest device over Wi-Fi |
| `status` | Query headset status (connection, battery, foreground app) |

### App Management

| Command | Description |
|---------|-------------|
| `install <apk>` | Install an APK on the connected Quest |
| `launch <package>` | Launch an app by package ID |
| `perf <cpu> <gpu>` | Set CPU and GPU performance levels (0–5) |

### LSL Monitoring

| Command | Description |
|---------|-------------|
| `monitor` | Monitor an LSL stream (continuous output) |

Options:

- `--stream <name>` — stream name (default: `quest_monitor`)
- `--type <type>` — stream type (default: `quest.telemetry`)
- `--channel <index>` — channel index (default: `0`)

### Twin Mode

| Command | Description |
|---------|-------------|
| `twin send <action>` | Send a twin command (for example `twin-start`, `twin-pause`) |
| `twin status` | Show twin bridge status and settings comparison |

`twin send` is intentionally conservative for one-shot command testing. It now
waits briefly after opening the LSL bridge before publishing and keeps the
process alive briefly after publishing so the outlet has time to advertise and
deliver the sample:

- `--settle-ms <milliseconds>` controls the pre-send settle delay
- `--hold-ms <milliseconds>` controls the post-send hold delay

The defaults are suitable for normal local validation. Shorten them only when
you already have a stable receiver path and need a faster scripted loop.

### Catalog

| Command | Description |
|---------|-------------|
| `catalog list` | List available apps, bundles, and profiles |

Options:

- `--root <path>` — catalog root directory (auto-detected if omitted)

### Study Shells

| Command | Description |
|---------|-------------|
| `study list` | List available pinned study shells |
| `study install <study>` | Install the pinned study APK |
| `study apply-profile <study>` | Apply the pinned study device profile |
| `study launch <study>` | Launch the pinned study runtime using the study kiosk policy. The command now refuses to launch while the headset reports asleep; wake the headset first. |
| `study stop <study>` | Stop the pinned study runtime using the study kiosk-exit policy |
| `study status <study>` | Compare current headset state against the pinned study baseline |
| `study probe-connection <study>` | Mirror the Step 9 `Probe Connection` check: inspect the pinned APK match, pinned device profile state, Quest Wi-Fi transport reachability, expected inlet, `quest_twin_state` return path, Wi-Fi snapshot context, and twin transport detail |
| `study diagnostics-report <study>` | Run the Windows LSL, machine inventory, Quest setup, Quest Wi-Fi transport, twin return-path, and safe command-acceptance diagnostics and write a shareable JSON/LaTeX/PDF report folder |

For Sussex, the study id is currently `sussex-university`.

Remote headset wake/sleep is no longer part of the supported public GUI
operator flow for Sussex. Use manual headset wake/sleep, and clear Guardian or
other Meta visual blockers before `study launch`.

### Sussex Profiles

The Sussex profile commands mirror the GUI profile tabs and are the preferred
agentic path for repeatable tuning work:

- `sussex visual ...`
- `sussex controller ...`

Common subcommands on both surfaces are:

- `list`
- `fields`
- `show <profile>`
- `create`
- `update <profile>`
- `delete <profile>`
- `import <path>`
- `export <profile> <path>`
- `set-startup <profile>`
- `clear-startup`
- `apply-live <profile>`

Use `--json` whenever an agent needs stable machine-readable output.

For the controller-breathing surface, the calibration setup is now part of the
same field catalog and profile commands:

- `use_principal_axis_calibration=on` keeps the dynamic motion-axis solve
- `use_principal_axis_calibration=off` keeps the fixed warmed-up controller
  orientation
- `min_accepted_delta=0.0008` controls how much movement counts as a new
  accepted calibration sample
- `min_acceptable_travel=0.02` controls how much total travel calibration must
  see before it accepts the solve
- `vibration_inhale_frequency` / `vibration_inhale_intensity` set the controller
  vibration used while the runtime classifies inhale
- `vibration_exhale_frequency` / `vibration_exhale_intensity` set the controller
  vibration used while the runtime classifies exhale
- `vibration_retention_frequency` / `vibration_retention_intensity` set the
  tracked low-motion retention vibration

Bad tracking has no vibration parameters. The Sussex runtime always disables
controller vibration while tracking is bad, the selected controller is inactive,
or controller-breathing calibration has not been accepted yet.

Example:

```powershell
viscereality sussex controller fields --json
viscereality sussex controller update "<profile>" `
  --set use_principal_axis_calibration=off `
  --set min_accepted_delta=0.0008 `
  --set min_acceptable_travel=0.02 `
  --json
```

The release also bundles two low-motion controller profiles derived from the
bundled baseline:

- `Small Motion Mild`
- `Small Motion Conservative`

`Small Motion Conservative` is the pinned startup controller profile for the
current Sussex preview. Startup/default pinning and current-session hotload are
separate:

- `sussex controller set-startup "<profile>"` changes what the next Sussex
  launch stages to the headset
- `sussex controller apply-live "<profile>"` hotloads the currently running
  Sussex session and does not rewrite the saved next-launch profile

Both paths reset controller-breathing calibration in the runtime. Recalibrate
on-headset afterward.

### Utilities

| Command | Description |
|---------|-------------|
| `utility home` | Return to Quest launcher |
| `utility back` | Send back event |
| `utility wake` | Wake Quest display |
| `utility list` | List installed packages |
| `utility reboot` | Reboot Quest |

### hzdb

| Command | Description |
|---------|-------------|
| `hzdb screenshot` | Capture a Quest screenshot |
| `hzdb perf` | Capture a Perfetto trace |
| `hzdb proximity <enable|disable>` | Control the proximity sensor |
| `hzdb wake` | Wake the Quest |
| `hzdb info` | Read detailed device info |
| `hzdb ls <path>` | List files under a Quest path |
| `hzdb pull <remote-path> <local-path>` | Pull one Quest file to Windows |

### Tooling

| Command | Description |
|---------|-------------|
| `tooling status` | Show the local managed official Quest tooling cache state |
| `tooling status --check-upstream` | Also query the latest published upstream versions |
| `tooling install-official` | Install or update Meta `hzdb` plus Android platform-tools into the current operator-data root under `...\ViscerealityCompanion\tooling` |

`tooling status` only covers the managed official Quest tool cache. It does not
decide whether liblsl is available in the current process layout.

### Windows Environment

| Command | Description |
|---------|-------------|
| `windows-env analyze` | Mirror the GUI `Analyze Windows Environment` check for `adb`, `hzdb`, liblsl, Windows network-adapter hazards, the local twin bridge, the exported agent workspace, liblsl discovery health, a temporary local LSL outlet rediscovery check, and the expected upstream LSL stream. When a live headset selector is available, it also adds a Quest Wi-Fi transport-path check that probes raw reachability to the headset's current Wi-Fi ADB endpoint. |

`windows-env analyze` separates four related LSL questions:

- whether this Windows process can load the liblsl runtime
- whether liblsl discovery can run at all without a socket / adapter error
- whether this PC can advertise a temporary local LSL outlet and rediscover it
- whether the expected `HRV_Biofeedback / HRV` sender is currently visible on
  Windows

That distinction matters because the Quest can sometimes receive a sender while
the Windows-side discovery inventory is failing. When the discovery self-check
reports `set_option` or "requested address is not valid in its context", or
when the local loopback outlet cannot be rediscovered, treat that as a Windows
adapter / multicast / firewall-profile hazard first. The analysis also lists
active IPv4 adapters and warns about common instability sources such as VPN,
Tailscale, WireGuard, Hyper-V, Docker, WSL, TAP/Wintun, VirtualBox, VMware,
multiple default gateways, and adapters without multicast support.

When a headset is connected over Wi-Fi ADB, `windows-env analyze` now also
checks the PC↔Quest router path directly:

- whether the selector IP matches the headset-reported Wi-Fi IP
- whether the host Wi-Fi adapter and Quest appear to share the same IPv4 subnet
- whether Windows can ping the Quest IP
- whether Windows can open TCP port `5555` on the Quest IP

If the PC and Quest report the same SSID but TCP `5555` is blocked, treat that
as a router/client-isolation hazard first, not as proof that the Sussex APK or
Windows liblsl path is broken.

Use `study probe-connection sussex-university --wait-seconds 15` for the
headset-side half. That probe now reports the pinned Sussex build match and the
required device profile before it reports the runtime inlet and
`quest_twin_state` return path. It also inventories the Windows-visible
`quest_twin_state / quest.twin.state` publisher and prints the source id that
the companion expects from the pinned Sussex APK. If the Quest visibly reacts
to the TEST sender but `Probe Connection` says no `quest_twin_state` has reached
Windows, treat that as a return-telemetry problem rather than a forward-LSL
problem. If the new `Twin-state outlet` line says no publisher is visible, look
at Quest-to-Windows multicast/client-isolation or whether the Sussex scene is
publishing the twin-state outlet. If it shows a visible outlet with a different
source id, reinstall the pinned APK and capture the CLI JSON because that points
at a Quest build/source-id contract mismatch.

Use `study diagnostics-report sussex-university --wait-seconds 15` when you
need one artifact to send to another maintainer. The command writes a
timestamped folder under the operator-data diagnostics root containing
`sussex_lsl_twin_diagnostics.json`, `sussex_lsl_twin_diagnostics.tex`, and,
when Python plus matplotlib are available, `sussex_lsl_twin_diagnostics.pdf`.
The report combines `windows-env analyze`, machine-visible LSL inventory, the
Quest pinned APK/profile snapshot, the raw Quest Wi-Fi transport path,
`quest_twin_state` publisher visibility, the Step 9 return-path
interpretation, and a safe particle-off command
acknowledgement probe. Use `--skip-command-check` for passive inspection only.

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `VISCEREALITY_QUEST_SESSION_KIT_ROOT` | Override catalog root directory |
| `VISCEREALITY_OPERATOR_DATA_ROOT` | Override the host-visible operator-data root used for session state, study data, screenshots, tooling, and agent-workspace compatibility |
| `VISCEREALITY_ADB_EXE` | Override the `adb.exe` path the app and CLI should use |
| `VISCEREALITY_HZDB_EXE` | Override the `hzdb.exe` path the app and CLI should use |
| `VISCEREALITY_LSL_DLL` | Path to `lsl.dll` for LSL features |

## Example

```powershell
viscereality probe
viscereality wifi
viscereality connect 192.168.43.1:5555
viscereality perf 4 4
viscereality install path/to/app.apk
viscereality launch com.Viscereality.SussexExperiment
viscereality twin send twin-start
viscereality monitor --stream quest_monitor --type quest.telemetry
```

## Example Sussex Agent Workflow

```powershell
viscereality study status sussex-university
viscereality windows-env analyze
viscereality study probe-connection sussex-university
viscereality study diagnostics-report sussex-university --wait-seconds 15
viscereality study install sussex-university
viscereality study apply-profile sussex-university
viscereality study launch sussex-university
viscereality sussex visual fields --json
viscereality sussex visual apply-live "<profile>" --json
viscereality sussex controller fields --json
viscereality sussex controller show "Small Motion Conservative" --json
viscereality sussex controller set-startup "Small Motion Conservative" --json
# Optional for a running, foreground Sussex session:
viscereality sussex controller apply-live "Small Motion Conservative" --json
```

After that deterministic CLI setup:

1. Open the Sussex `Sequential Guide` in the desktop app and complete the
   pre-session checks.
2. Use the guide's final handoff or the Home screen button to open
   `Experiment Session`.
3. Run the real participant session from that window with `Start Recording`
   and `Stop Recording`.
4. After `Stop Recording`, use the same window to open:
   - the Windows session folder
   - the pulled Quest backup folder
   - the generated `session_review_report.pdf`

If you need a CLI-only recovery path for Quest-side files after the run, use
`hzdb ls` plus `hzdb pull` against the recorded `study.recording.device.session_dir`
or the `session_snapshot.json` entry in the Windows session folder.
