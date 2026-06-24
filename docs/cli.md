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

Installed Sussex packaged builds now expose a local agent workspace under
the current operator-data root. For unpackaged/source builds that is typically
`%LOCALAPPDATA%\ViscerealityCompanion\agent-workspace`; for packaged MSIX
installs it is the host-visible packaged path under
`%LOCALAPPDATA%\Packages\<package-family>\LocalCache\Local\ViscerealityCompanion\agent-workspace`.
The Sussex shell Home page includes `Open Agent Workspace` plus `Copy Local
Agent Prompt` so installed-app users can hand a local agent a bundled
`viscereality` command, the mirrored CLI docs, and the Sussex example
catalogs without pointing it at the protected WindowsApps payload. The
generated workspace includes `cli/current/Viscereality CLI.exe` plus
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
  - GUI-equivalent named study actions for validation sessions, including
    calibration, breathing mode, particle visibility, and recording controls
  - bounded twin-state snapshots and TEST sender runs for agent-led diagnostics
  - machine-readable inspection with `--json`
- `Sequential Guide` window:
  - one pre-session verification pass directly before a real participant
- `Experiment Session` window:
  - the live participant-run surface for participant id entry, `Start
    Recording`, `Stop Recording`, live telemetry, clock/network consistency,
    recenter, particle toggles, screenshots, direct dynamic-axis / fixed-axis
    calibration start controls, and quick access to the session folder, pulled
    Quest backup, and session review PDF

The GUI remains the primary participant-run surface, but the CLI now exposes
the same underlying study action IDs for controlled validation sessions. Use
named `study action` aliases instead of raw `twin send` whenever the command
should map to a real GUI control's Quest-side action. These commands do not
replace higher-level WPF workflows such as `Start Recording`, which also stage
participant metadata, create the local recorder, wait for Quest-side metadata
confirmation, and run clock-alignment steps. After `Stop Recording`, the GUI
pulls the Quest backup into `device-session-pull` and generates
`session_review_report.pdf` inside the same participant session folder.

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
| `study launch <study>` | Launch the pinned study runtime using the study launch policy. The command now refuses to launch while the headset reports asleep; wake the headset first. |
| `study stop <study>` | Stop the pinned study runtime and unwind any study task-pinning policy |
| `study status <study>` | Compare current headset state against the pinned study baseline |
| `study actions <study>` | List GUI-equivalent named study actions and their twin action IDs |
| `study action <study> <action>` | Send a named study twin action and wait for `quest_twin_state` command acknowledgement |
| `study snapshot <study>` | Capture a bounded `quest_twin_state` snapshot for the study-test telemetry keys |
| `study test-sender run <study>` | Start the GUI-equivalent TEST sender route, publish synthetic `HRV_Biofeedback / HRV`, and restore routing when the run ends when a prior route snapshot is available |
| `study probe-connection <study>` | Mirror the Step 9 `Probe Connection` check: inspect the pinned APK match, pinned device profile state, Quest Wi-Fi transport reachability, expected inlet, `quest_twin_state` return path, Wi-Fi snapshot context, and twin transport detail |
| `study diagnostics-report <study>` | Run the Windows LSL, machine inventory, Quest setup, Quest Wi-Fi transport, twin return-path, and safe command-acceptance diagnostics and write a shareable JSON/LaTeX/PDF report folder |

For Sussex, the study id is currently `sussex-university`.

Named Sussex actions include:

- `calibrate`
- `reset-calibration`
- `controller-volume`
- `automatic-cycle`
- `automatic-start`
- `automatic-pause`
- `start-recording`
- `stop-recording`
- `particles-on`
- `particles-off`
- `recenter`

`study action` uses the same command-acknowledgement contract as the GUI study
shell. A command is treated as complete only after `quest_twin_state` reports
the expected `study.command.last_action_sequence` for the published LSL command
or, when a sequence is unavailable, a fresh matching `study.command.last_action_id`.
If acknowledgement is requested and does not arrive before the timeout, the CLI
prints the last observed headset command state and returns exit code `2`. Use
`--no-ack` only for transport previews where a live headset response is not
expected.

For participant-run parity, use the WPF Experiment Session UI or the
verification harness in UI input parity mode. `study action start-recording`
and `study action stop-recording` are low-level Quest twin-command diagnostics
and are blocked by default so they cannot bypass the WPF participant metadata
and local recording workflow.

`study action` waits for command acknowledgement by default:

- `--settle-ms <milliseconds>` controls the pre-send LSL outlet advertise delay
- `--hold-ms <milliseconds>` controls how long the outlet remains alive after send
- `--wait-ack-seconds <seconds>` controls the `quest_twin_state` acknowledgement window
- `--no-ack` skips acknowledgement waiting for passive fire-and-forget checks
- `--allow-recording-command-diagnostic` explicitly permits the low-level
  `start-recording` / `stop-recording` Quest twin actions without the WPF
  participant workflow
- `--json` emits the sent action, sequence, and acknowledgement state

`study test-sender run` is a long-running CLI equivalent of the GUI TEST sender
toggle. Use `--duration-seconds <seconds>` for bounded runs, or omit it and stop
with Ctrl+C. Add `--no-routing` only when you intentionally want to test the
local Windows LSL outlet without temporarily switching the running Sussex
session to LSL/direct-LSL routing.

Remote headset wake/sleep is no longer part of the supported public GUI
operator flow for Sussex. Use manual headset wake/sleep, and clear Guardian or
other Meta visual blockers before `study launch`.

### Sussex Profiles

The Sussex profile commands mirror the GUI profile tabs and are the preferred
agentic path for repeatable tuning work:

- `sussex visual ...`
- `sussex condition ...`
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
current Sussex packaged release. Startup/default pinning and current-session hotload are
separate:

- `sussex controller set-startup "<profile>"` changes what the next Sussex
  launch stages to the headset
- `sussex controller apply-live "<profile>"` hotloads the currently running
  Sussex session and does not rewrite the saved next-launch profile

Both paths reset controller-breathing calibration in the runtime. Recalibrate
on-headset afterward.

### Sussex Conditions

The Sussex condition commands mirror the GUI `Conditions` tab. A condition is
the experiment-session choice that links one visual profile, one
controller-breathing profile, and an active-selection flag:

| Command | Description |
|---------|-------------|
| `sussex condition list` | List bundled conditions, local overrides, and local-only conditions |
| `sussex condition list --active-only` | List exactly what the Experiment Session condition dropdown will show |
| `sussex condition show <condition>` | Show one condition with resolved profile names |
| `sussex condition create` | Create a local condition from one visual profile and one breathing profile |
| `sussex condition update <condition>` | Edit a local condition, or save a bundled condition as a local override |
| `sussex condition duplicate <condition>` | Copy a condition into a new local condition |
| `sussex condition import <path>` | Import a shared condition JSON file |
| `sussex condition export <condition> <path>` | Export one condition as shareable JSON |
| `sussex condition delete <condition>` | Delete a local condition, or remove a local override |

The CLI accepts the portable bundled profile ids used by the GUI, such as
`condition-current-visual`, `condition-fixed-radius-no-orbit`,
`condition-current-breathing`, and `condition-fixed-radius-breathing`.

Example:

```powershell
viscereality sussex condition create `
  --id small-motion-no-orbit `
  --label "Small Motion, No Orbit" `
  --visual condition-fixed-radius-no-orbit `
  --breathing "Small Motion Conservative" `
  --inactive `
  --property visual.orbit=0..0 `
  --json

viscereality sussex condition update small-motion-no-orbit --active --json
viscereality sussex condition list --active-only --json
```

The packaged app and packaged CLI share the same host-visible operator-data
root, so conditions edited through the GUI are visible to the CLI and
conditions imported through the CLI are visible in the GUI after refreshing or
opening the `Conditions` tab.

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

Useful safety flags:

- `--local-only` skips persisted headset selectors and all ADB-backed Quest
  Wi-Fi transport probes. Use this when the headset is being used for something
  else and you only want the Windows/package/liblsl side.
- `--skip-stream-probe` skips the expected upstream HRV LSL stream lookup.
- `--check-timeout-seconds <n>` bounds each heavyweight diagnostic check. A
  timed-out check returns a warning and the analyzer still prints the remaining
  partial diagnostics.

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
`sussex_lsl_twin_diagnostics.json`, `sussex_lsl_twin_diagnostics.tex`, and
`sussex_lsl_twin_diagnostics.pdf`. The report combines `windows-env analyze`,
machine-visible LSL inventory, the Quest pinned APK/profile snapshot, the raw
Quest Wi-Fi transport path, `quest_twin_state` publisher visibility, the Step
9 return-path interpretation, and a safe particle-off command acknowledgement
probe.
Use `--skip-command-check` for passive inspection only.

For an agent-led validation pass, prefer the study-scoped commands over raw
`twin send`:

```powershell
viscereality study actions sussex-university
viscereality study test-sender run sussex-university --duration-seconds 180
viscereality study action sussex-university controller-volume --json
viscereality study action sussex-university calibrate --wait-ack-seconds 15 --json
viscereality study snapshot sussex-university --json
.\tools\app\Start-Sussex-VerificationHarness.ps1 -UiInputParity
```

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `VISCEREALITY_QUEST_SESSION_KIT_ROOT` | Override catalog root directory |
| `VISCEREALITY_OPERATOR_DATA_ROOT` | Override the host-visible operator-data root used for session state, study data, screenshots, tooling, and agent-workspace compatibility |
| `VISCEREALITY_ADB_EXE` | Override the `adb.exe` path the app and CLI should use |
| `VISCEREALITY_HZDB_EXE` | Override the `hzdb.exe` path the app and CLI should use |
| `VISCEREALITY_LSL_DLL` | Path to `lsl.dll` for LSL features |
| `LSLAPICFG` | Optional liblsl API configuration file. If unset, the app provides a quiet per-process config so JSON commands are not polluted by native liblsl logs. |

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
viscereality study actions sussex-university
viscereality windows-env analyze
viscereality study probe-connection sussex-university
viscereality study diagnostics-report sussex-university --wait-seconds 15
viscereality study install sussex-university
viscereality study apply-profile sussex-university
viscereality study launch sussex-university
viscereality sussex visual fields --json
viscereality sussex visual apply-live "<profile>" --json
viscereality sussex condition list --active-only --json
viscereality sussex condition show fixed-radius-no-orbit
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
