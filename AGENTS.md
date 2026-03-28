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
  `samples/quest-session-kit/APKs/SussexControllerStudy.apk`, but it does not
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

## Available Skills

- `$bureau-context` is available and should be used when the task genuinely
  needs cross-project or machine-wide context before falling back to this repo's
  local rules.
- `$uncodixfy-ui` is available and should be used for layout, styling, and UI
  polish work so the WPF shell stays restrained and product-specific instead of
  drifting toward generic dashboard chrome.
