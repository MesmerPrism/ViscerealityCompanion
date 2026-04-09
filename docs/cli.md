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
    recenter, particle toggles, screenshots, and quick access to the session
    folder

The CLI currently mirrors the setup and profile side of Sussex. It does not
yet replace the participant-run `Start Recording` / `Stop Recording` flow in
the `Experiment Session` window.

## Running

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
| `study launch <study>` | Launch the pinned study runtime using the study kiosk policy |
| `study stop <study>` | Stop the pinned study runtime using the study kiosk-exit policy |
| `study status <study>` | Compare current headset state against the pinned study baseline |

For Sussex, the study id is currently `sussex-university`.

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

### Utilities

| Command | Description |
|---------|-------------|
| `utility home` | Return to Quest launcher |
| `utility back` | Send back event |
| `utility wake` | Wake Quest display |
| `utility list` | List installed packages |
| `utility reboot` | Reboot Quest |

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `VISCEREALITY_QUEST_SESSION_KIT_ROOT` | Override catalog root directory |
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
viscereality study install sussex-university
viscereality study apply-profile sussex-university
viscereality study launch sussex-university
viscereality sussex visual fields --json
viscereality sussex visual apply-live "<profile>" --json
viscereality sussex controller apply-live "<profile>" --json
```

After that deterministic CLI setup:

1. Open the Sussex `Sequential Guide` in the desktop app and complete the
   pre-session checks.
2. Use the guide's final handoff or the Home screen button to open
   `Experiment Session`.
3. Run the real participant session from that window with `Start Recording`
   and `Stop Recording`.
