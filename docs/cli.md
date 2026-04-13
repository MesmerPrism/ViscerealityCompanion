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
`%LOCALAPPDATA%\ViscerealityCompanion\agent-workspace`. The Sussex shell Home
page includes `Open Agent Workspace` plus `Copy Local Agent Prompt` so
installed-app users can hand a local agent a bundled `viscereality` command,
the mirrored CLI docs, and the Sussex example catalogs without pointing it at
the protected WindowsApps payload. The generated workspace includes
`cli/current/viscereality.exe` plus `viscereality.ps1` and `viscereality.cmd`
wrapper scripts that preload the mirrored sample-root overrides and point
`VISCEREALITY_LSL_DLL` at the bundled workspace copy when it is present.

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
    recenter, particle toggles, screenshots, and quick access to the session
    folder, pulled Quest backup, and session review PDF

The CLI currently mirrors the setup and profile side of Sussex. It does not
yet replace the participant-run `Start Recording` / `Stop Recording` flow in
the `Experiment Session` window. After `Stop Recording`, the GUI now pulls the
Quest backup into `device-session-pull` and generates `session_review_report.pdf`
inside the same participant session folder.

## Running

From the guided-install local agent workspace under
`%LOCALAPPDATA%\ViscerealityCompanion\agent-workspace`, prefer:

```powershell
.\viscereality.ps1 --help
```

That wrapper preloads the mirrored sample-root overrides before invoking the
bundled CLI under `cli/current`, and it now exports the bundled workspace
`lsl.dll` path automatically when that copy is present.

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
| `study probe-connection <study>` | Mirror the Step 9 `Probe Connection` check: inspect the expected inlet, `quest_twin_state` return path, and twin transport detail |

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

Example:

```powershell
viscereality sussex controller fields --json
viscereality sussex controller update "<profile>" `
  --set use_principal_axis_calibration=off `
  --set min_accepted_delta=0.0008 `
  --set min_acceptable_travel=0.02 `
  --json
```

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
| `tooling install-official` | Install or update Meta `hzdb` plus Android platform-tools into `%LOCALAPPDATA%\ViscerealityCompanion\tooling` |

`tooling status` only covers the managed official Quest tool cache. It does not
decide whether liblsl is available in the current process layout.

### Windows Environment

| Command | Description |
|---------|-------------|
| `windows-env analyze` | Mirror the GUI `Analyze Windows Environment` check for `adb`, `hzdb`, liblsl, the local twin bridge, the exported agent workspace, and the expected upstream LSL stream |

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `VISCEREALITY_QUEST_SESSION_KIT_ROOT` | Override catalog root directory |
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
viscereality study install sussex-university
viscereality study apply-profile sussex-university
viscereality study launch sussex-university
viscereality sussex visual fields --json
viscereality sussex visual apply-live "<profile>" --json
viscereality sussex controller fields --json
viscereality sussex controller update "<profile>" --set use_principal_axis_calibration=off --set min_accepted_delta=0.0008 --set min_acceptable_travel=0.02 --json
viscereality sussex controller apply-live "<profile>" --json
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
