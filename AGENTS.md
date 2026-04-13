# Agent Workflow Guardrails

If the task expands beyond this repo into broader machine context,
cross-project patterns, or central-bureau maintenance, use
`$bureau-context` first.

## Build And Compile Validation

- Build: `dotnet build ViscerealityCompanion.sln`
- Test: `dotnet test ViscerealityCompanion.sln`
- Run WPF app (Smart App Control-safe): `powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1`
- Run WPF app (direct dev loop): `dotnet run --project src/ViscerealityCompanion.App`
- Run CLI: `dotnet run --project src/ViscerealityCompanion.Cli`
- Run Sussex verification harness: `powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Sussex-VerificationHarness.ps1`
- Build MSIX package: `powershell -ExecutionPolicy Bypass -File .\tools\app\Build-App-Package.ps1 -Unsigned`
- Build docs site: `npm run pages:build`
- Serve docs locally: `npm run pages:serve`

## GUI Validation Guardrails

- For live Quest GUI testing, establish an active ADB selector first. Use
  `ProbeUsbAsync` / `Probe USB` when a headset is attached over USB, or
  `ConnectAsync` / `Connect Quest` when using a known Wi-Fi `ip:port`
  endpoint.
- If a one-off WPF harness is used from `artifacts/verify/`, make it write
  screenshots and a text report to a dedicated subfolder and inspect that
  output before assuming the run is hung.
- A one-off WPF harness should clear stale error/report/screenshot artifacts at
  the start of the run so old failures cannot be mistaken for the current pass.
- A one-off WPF harness must shut the application down explicitly after the
  scripted window closes. Do not rely on `window.Close()` alone when the
  harness uses `ShutdownMode.OnExplicitShutdown`.
- The tracked Sussex verification harness runs the study inside the main app,
  not in a separate `StudyShellWindow`. Validate the embedded `Sussex
  University experiment mode` tab and header, because that is the current
  operator-facing surface.
- The Sussex verification harness brings up a local float LSL sender on
  `HRV_Biofeedback / HRV` and publishes smoothed `0..1` HRV biofeedback
  packets on an irregular heartbeat-timed cadence. Each packet arrival is the
  heartbeat event, and the packet value itself is the routed coherence /
  biofeedback value.
- The current public Sussex telemetry only confirms that path through
  `study.lsl.connected_*` and `study.lsl.status`, and some builds may also echo
  the normalized `0..1` value on `signal01.coherence_lsl` or a
  `driver.stream.*.value01` mirror entry. Do not claim value-level round-trip
  latency unless the runtime actually exposes that coherence value or an inlet
  sample timestamp on `quest_twin_state` during the verified run.
- For Sussex step-9 LSL troubleshooting, prefer the sequential-guide
  `Probe Connection` action before inventing ad hoc checks. It refreshes the
  current ADB-backed headset snapshot and then reports the expected inlet,
  runtime target, connected inlet, connection counts, and whether fresh
  `quest_twin_state / quest.twin.state` frames are returning to Windows.
- For companion-side LSL troubleshooting, prefer the Sussex `Pre-session`
  Bench-tools `Machine LSL State` panel before assuming the Quest side is at
  fault. It compares the companion-owned TEST sender, twin outlets, clock
  probe transport, passive monitor tasks, and the currently visible Windows
  `HRV_Biofeedback / HRV` publishers.
- If `Machine LSL State` or `windows-env analyze` shows multiple visible
  `HRV_Biofeedback / HRV` sources on Windows, treat that as a likely cause of
  unreliable switching between the built-in TEST sender and external Python
  senders. Do not claim a clean companion stop until the Windows-side
  inventory no longer shows the companion-owned source id.
- `liblsl` on Windows may log repeated startup warnings like
  `Could not bind multicast responder ... to interface ::1 (An invalid argument
  was supplied.)` while enumerating the IPv6 loopback adapter. Treat that as a
  known benign library quirk on this machine, not as evidence that the harness
  is hung or that LSL transport is broken. Judge the run from actual stream
  resolution, twin-state freshness, and the generated `artifacts/verify/...`
  report instead.
- In WPF XAML, any control property that binds TwoWay by default
  (`ProgressBar.Value`, `Slider.Value`, similar range controls) must use
  `Mode=OneWay` when the viewmodel property is read-only. Otherwise the app can
  fall into dispatcher exception loops that look like hangs.
- If a GUI validation run appears stuck, check
  `%LOCALAPPDATA%\\ViscerealityCompanion\\logs` and the active
  `artifacts/verify/...` folder before retrying.
- On the current HorizonOS build, do not treat `viscereality study launch
  sussex-university`, kiosk entry, or shell-owned process state as proof that
  Sussex is visibly foregrounded. The app can launch, trigger a permission /
  task-lock transition, and bounce back to Home.
- On the current HorizonOS build after the April 2026 Meta OS update on this
  machine, do not describe Sussex kiosk mode as reliably disabling the
  right-controller Meta / menu button. The app can launch and remain in front
  while the menu button still behaves normally. Treat kiosk as best-effort task
  pinning plus screenshot-confirmed foreground, not as a guaranteed button
  lockout.
- If the headset reports `Asleep`, wake it before any Sussex launch. Do not
  launch while asleep. On this build that can leave Sussex running in a black /
  limbo scene that may require a full headset restart. Operator-facing launch
  surfaces should say `Wake the headset to enable launching` when that guard is
  active.
- The public GUI should not expose remote headset wake or sleep controls for
  Sussex right now. The only official operator path is manual wake/sleep on the
  headset itself, with launch blocked until the headset is awake and free of
  Guardian or other Meta visual blockers.
- For live Sussex validation, require both:
  - foreground confirmation from `viscereality status` or an equivalent ADB
    foreground-app check showing
    `com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity`
  - a fresh Quest screenshot captured after launch, preferably with
    `viscereality hzdb screenshot --method screencap` or the equivalent direct
    `hzdb` command, showing the Sussex scene rather than passthrough / Home
- If repeated screenshots are byte-identical, do not assume the screenshot path
  is frozen. First decide whether the visible headset scene itself is actually
  unchanged. If the screenshot still shows Home or passthrough while the shell
  claims Sussex is active, trust the screenshot and treat the launch as failed.
- If the kiosk launch path bounces back to Home, prefer an explicit non-kiosk
  launch for off-face validation:
  `adb shell am start -n com.Viscereality.SussexExperiment/com.unity3d.player.UnityPlayerGameActivity`
  After that, refresh the companion snapshot before using `Sequential Guide` or
  `Experiment Session`.
- Do not call `Exit Kiosk Runtime`, `viscereality study stop
  sussex-university`, or any equivalent Sussex APK quit path while the headset
  is off-face or while the visible headset scene is unconfirmed. On this
  HorizonOS build that cleanup path can strand the headset in Guardian /
  SensorLock / passthrough limbo even when shell ownership looks home-like.
- For off-face automation and functionality tests, quitting the Sussex APK is
  optional and should normally be skipped.
- Recent Meta OS updates on this machine have weakened true kiosk lock.
  Off-face stop / restart is now less risky than older builds, but it is still
  not a validation signal by itself. If an agent restarts Sussex off-face,
  immediately re-check both foreground state and a fresh screenshot.
- If Sussex really must be exited, ask the user to wear the headset and quit it
  from a visible on-head state, then confirm the resulting Home-side scene with
  `Capture Quest Screenshot` or an equivalent metacam capture.

## Architecture Rules

### Public / Private Split

This is the **public** repo. It ships:

- WPF desktop operator shell
- CLI tool
- ADB device control (real Windows transport + preview mock)
- LSL monitoring and outlet services (real Windows P/Invoke + preview mock)
- Twin mode command/state bridge (public contract + LSL transport)
- Sample configs, onboarding docs, CI/release automation
- Curated public study bundles when the packaged Windows install depends on them

It does **not** ship:

- Coupled oscillator dynamics runtime
- Private twin orchestration backend
- Private APKs or unpublished study presets
- Study-locked runtime configurations

### Astral Sussex Contract

- The Sussex Quest runtime is authored in `AstralKarateDojo`.
- This repo may mirror an approved Sussex APK under
  `samples/quest-session-kit/APKs/SussexExperiment.apk`, but it does not
  edit Unity scenes, configs, or build-time scene wiring.
- If the Sussex runtime needs a different scene hierarchy, LSL inlet contract,
  or runtime config asset, make that change in `AstralKarateDojo` first and
  then refresh the mirrored APK here.
- Use `powershell -ExecutionPolicy Bypass -File .\tools\app\Sync-Bundled-Sussex-Apk.ps1`
  to refresh the bundled Sussex APK and pinned hashes from an Astral build.
- Do not introduce companion-side workflow steps that mutate Unity scenes or
  rely on build-time scene rewriting in `AstralKarateDojo`.

### Service Abstraction Pattern

All external integrations use interfaces with factory-created implementations:

- `IQuestControlService` → `WindowsAdbQuestControlService` or `PreviewQuestControlService`
- `ILslMonitorService` → `WindowsLslMonitorService` or `PreviewLslMonitorService`
- `ILslOutletService` → `WindowsLslOutletService` or `PreviewLslOutletService`
- `ITwinModeBridge` → `LslTwinModeBridge` or `UnavailablePrivateTwinModeBridge`

Factories auto-detect capabilities (ADB path, OS, liblsl availability) and
return the appropriate implementation.

### Code Style

- Records with immutable fields for models
- Async-first with CancellationToken support throughout
- Factory pattern for external dependency resolution
- MVVM with custom ObservableObject + AsyncRelayCommand for WPF
- System.Text.Json for serialization (case-insensitive)
- xUnit for testing, Preview* implementations for mocking

## ADB Locator Strategy

The `QuestControlServiceFactory` auto-detects `adb.exe` by checking:

1. `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe`
2. `%ANDROID_SDK_ROOT%\platform-tools\adb.exe`
3. `%ANDROID_HOME%\platform-tools\adb.exe`
4. Every entry on `PATH`

On this machine, Unity also bundles adb at:
`C:\Program Files\Unity\Hub\Editor\6000.2.7f2\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`

## LSL Native Dependency

The `WindowsLslMonitorService` and `WindowsLslOutletService` use P/Invoke to
`lsl.dll`. The runtime locates it via:

1. `%VISCEREALITY_LSL_DLL%` environment variable
2. `<app-dir>/lsl.dll`
3. `<app-dir>/runtimes/win-x64/native/lsl.dll`
4. User-installed official liblsl copies under `~/Tools/liblsl/*/bin/lsl.dll`
5. Known Unity project paths under `~/source/repos/`

Known Windows quirk: `lsl.dll` may emit IPv6 loopback multicast-responder bind
warnings against `::1` during startup on this machine. Those warnings are noisy
but non-fatal unless stream discovery or publication actually fails afterward.
On this machine, the trusted official runtime is currently installed at
`C:\Users\tillh\Tools\liblsl\1.16.2\bin\lsl.dll`; prefer that copy over the
Unity-bundled repo copies if Windows Application Control blocks those older
DLLs.

## Twin Mode Protocol

The public twin bridge uses LSL streams compatible with the AstralKarateDojo
twin architecture:

- **Commands out**: `quest_twin_commands` / `quest.twin.command` (operator → headset)
- **State in**: `quest_twin_state` / `quest.twin.state` (headset → operator)
- **Config out**: `quest_hotload_config` / `quest.config` (full config snapshots)

Snapshot protocol: `begin → set key=value → end` frame sequence.

## Reference Repos

- `AstralKarateDojo/QuestSessionKit` — PowerShell Quest session launcher, APK
  catalog, hotload profiles, device profiles, LSL monitoring
- `AndroidPhoneQuestCompanion` — Android companion app patterns
- `PolarH10` — GitHub Pages structure, release pipeline, onboarding patterns

## Smart App Control Note

This machine may block `dotnet test` and unpackaged multi-file WPF launches
when unsigned repo assemblies are loaded. Use
`powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1`
for the companion app, or publish manually with
`dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=false`.
To refresh the Desktop/Start Menu shortcut onto that safe launcher path, run
`powershell -ExecutionPolicy Bypass -File .\tools\app\Refresh-Desktop-Launcher.ps1`.

For the Sussex verification harness on this machine, do not default to
`dotnet run --project tools/ViscerealityCompanion.VerificationHarness`.
Use `powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Sussex-VerificationHarness.ps1`
so the harness runs from a published single-file output first. If LSL still
does not load, verify that `~/Tools/liblsl/*/bin/lsl.dll` or
`%VISCEREALITY_LSL_DLL%` points at the official runtime before blaming the
Astral/companion sync.
Freshly republished local harness executables can still be blocked by Windows
Application Control on this machine; treat that as a launcher-path issue, not
as evidence that the Sussex APK, scene config, or LSL contract regressed.
For Quest wake / Guardian tracking-loss debugging, keep
`docs/quest-adb-hzdb-recovery-notes.md` up to date. It is the repo-local memory
for tested `adb` / `hzdb` commands, observed shell states, and the current best
recovery sequence.

## Local-Agent Operation Modes

For Sussex shell automation there are now two supported agent-facing control
surfaces:

1. GUI-driving
   - Use this when the task depends on rendered state, visual validation,
     screenshot confirmation, or user-facing layout/tooltip placement.
   - The Sussex GUI is intentionally split by session phase:
     - `Open Sequential Guide` is the pre-session checklist. Use it once
       directly before a participant to verify transport, APK/profile state,
       LSL connectivity, particle commands, and the optional 20 second
       validation capture.
     - `Open Experiment Session` is the live participant-run surface. Use it
       after the guide has passed. It owns participant-id entry, `Start
       Recording`, `Stop Recording`, `Open Session Folder`, `Open Quest
       Backup`, `Open Session PDF`, live telemetry, clock/network consistency,
       recenter, particle toggles, screenshots, and the condensed operator log.
     - The main Sussex shell remains the broader tuning and inspection surface
       for profile editing, diagnostics, and deeper runtime review before the
       study setup is locked.
   - Example: verify that the pinned startup profile appears in the `Visual
     Profiles` table, or confirm that a tooltip is attached to the right row.
   - Use real UI interactions: activate the tab, focus the control, type or
     toggle through UI Automation / keyboard input, then invoke the matching
     button. Do not rely on direct WPF property mutation when documenting or
     scripting GUI behavior.
2. CLI-driving
   - Prefer this for deterministic state changes, profile creation, profile
     edits, startup/default changes, APK/profile staging, and machine-readable
     inspection.
   - Example: create a new profile, halve `particle_size_min` and
     `particle_size_max`, save it, pin it for next launch, or export it.
   - The CLI is not yet the preferred live participant-run surface. Do not
     invent ad hoc CLI replacements for the session recorder flow when the task
     is to run a real participant session; use the `Experiment Session` window.

When an action exists in both places, the CLI is the preferred automation path.
It uses the same persisted profile JSON files, the same startup/apply state
files under the current operator-data root
(`%LOCALAPPDATA%\ViscerealityCompanion\...` for unpackaged builds, or the
host-visible packaged `...\Packages\<family>\LocalCache\Local\ViscerealityCompanion\...`
path for installed MSIX builds), the same Sussex template schemas, and the
same hotload/twin publish channels as the GUI. The bundled agent-workspace
wrappers now set `VISCEREALITY_OPERATOR_DATA_ROOT` so the mirrored CLI stays on
that same host-visible root.
Current exception: Sussex kiosk exit / `study stop` cleanup is not a preferred
off-face automation path on this machine. Treat runtime exit as a user-worn
operator action unless the user explicitly wants to take that risk.

For the full Sussex agentic operator workflow, prefer this order:

1. CLI for repeatable setup, install/launch, and profile work.
2. GUI `Sequential Guide` once directly before the real session.
3. GUI `Experiment Session` window for the live participant run and recording.

## Sussex CLI Parity

Run the CLI with:

```powershell
dotnet run --project src/ViscerealityCompanion.Cli -- <args>
```

Or, after installing/publishing the tool:

```powershell
viscereality <args>
```

The Sussex-specific command surface is:

- `viscereality sussex visual ...`
- `viscereality sussex controller ...`

Important parity rules:

- `sussex visual apply-live <profile>` and
  `sussex controller apply-live <profile>` mirror the GUI `Apply To Current
  Session` buttons.
  - They publish over the live `quest_hotload_config` twin path.
  - They do not rewrite the saved next-launch/default profile.
  - They require the Sussex runtime to be in the foreground.
- `sussex visual set-startup <profile>` and
  `sussex controller set-startup <profile>` mirror the GUI next-launch/default
  actions.
  - They change the persisted startup/default profile.
  - If Sussex is not currently in the foreground, the CLI also syncs the
    device-side startup CSV immediately.
  - If Sussex is running in the foreground, the change is saved locally and
    the device-side startup CSV is deferred until the next `study stop` or
    `study launch`.
- `viscereality study launch sussex-university` now stages the saved Sussex
  startup/default profile(s) to the device before launch, matching the GUI.
- `viscereality study stop sussex-university` now refreshes the device-side
  startup CSV after stop, matching the GUI deferred-sync behavior.
  - Do not use it as unattended cleanup while the headset is off-face.
  - If exit matters, ask the user to quit Sussex while wearing the headset.
  - For functionality tests, leaving the Sussex runtime running is acceptable.

Current CLI parity stops at setup, runtime control, and profile management. The
real participant-run recorder flow is intentionally GUI-first:

- use the CLI for `study install`, `study apply-profile`, `study launch`,
  `study status`, and Sussex profile authoring/apply work
- use the `Sequential Guide` for the final pre-session verification pass
- use the `Experiment Session` window for `Start Recording`, `Stop Recording`,
  live telemetry monitoring, pulled Quest backups, session review PDFs, and
  in-session command tools
- use `viscereality hzdb ls ...` and `viscereality hzdb pull ...` only as
  recovery or inspection helpers when you need to inspect Quest-side files
  directly after a run

## Sussex Profile Recipes

Use `--json` for agent-readable output whenever possible.

Inspect the complete tooltip/metadata catalog:

```powershell
viscereality sussex visual fields --json
viscereality sussex controller fields --json
```

Inspect one saved or bundled profile:

```powershell
viscereality sussex visual show "<profile-id-or-name>" --json
viscereality sussex controller show "<profile-id-or-name>" --json
```

Create a new visual profile from the bundled baseline:

```powershell
viscereality sussex visual create `
  --name "Half-size particles" `
  --from bundled-baseline `
  --scale particle_size_min=0.5 `
  --scale particle_size_max=0.5 `
  --set-startup `
  --json
```

Update an existing controller-breathing profile and make it the next-launch
default:

```powershell
viscereality sussex controller update "<profile-id-or-name>" `
  --set median_window=7 `
  --set-startup `
  --json
```

Reset startup/default behavior back to the bundled baseline:

```powershell
viscereality sussex visual clear-startup --json
viscereality sussex controller clear-startup --json
```

Import/export profile JSON directly:

```powershell
viscereality sussex visual import "C:\path\profile.json" --json
viscereality sussex visual export "<profile-id-or-name>" "C:\path\out.json" --json
viscereality sussex controller import "C:\path\profile.json" --json
viscereality sussex controller export "<profile-id-or-name>" "C:\path\out.json" --json
```

## Sussex Parameter Guidance

Do not guess control ids from prose if the exact field mapping matters. Ask the
CLI for the field catalog first.

The field catalogs include, for every parameter:

- stable control id
- group/title
- type
- baseline value
- safe minimum and maximum
- runtime JSON key / hotload key
- full tooltip text
- effect
- increase/decrease visual meaning
- tradeoffs
- pair metadata for `_min` / `_max` envelopes

For Sussex visual profiles, paired envelope controls usually need to move
together:

- `oblateness_by_radius_min` + `oblateness_by_radius_max`
- `sphere_radius_min` + `sphere_radius_max`
- `particle_size_min` + `particle_size_max`
- `depth_wave_min` + `depth_wave_max`
- `transparency_min` + `transparency_max`
- `saturation_min` + `saturation_max`
- `brightness_min` + `brightness_max`
- `orbit_distance_min` + `orbit_distance_max`

For natural-language requests like "reduce particle size by 50%", an agent
should usually scale both `particle_size_min` and `particle_size_max` by `0.5`
unless the user explicitly asks for asymmetric behavior.

## Available Skills

- `$bureau-context` is available and should be used when the task genuinely
  needs cross-project or machine-wide context before falling back to this repo's
  local rules.
- `$uncodixfy-ui` is available and should be used for layout, styling, and UI
  polish work so the WPF shell stays restrained and product-specific instead of
  drifting toward generic dashboard chrome.
