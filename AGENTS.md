# Agent Workflow Guardrails

If the task expands beyond this repo into broader machine context,
cross-project patterns, or central-bureau maintenance, use
`$bureau-context` first.

## Build And Compile Validation

- Build: `dotnet build ViscerealityCompanion.sln`
- Test: `dotnet test ViscerealityCompanion.sln`
- Run WPF app: `dotnet run --project src/ViscerealityCompanion.App`
- Run CLI: `dotnet run --project src/ViscerealityCompanion.Cli`
- Build docs site: `npm run pages:build`
- Serve docs locally: `npm run pages:serve`

## Architecture Rules

### Public / Private Split

This is the **public** repo. It ships:

- WPF desktop operator shell
- CLI tool
- ADB device control (real Windows transport + preview mock)
- LSL monitoring and outlet services (real Windows P/Invoke + preview mock)
- Twin mode command/state bridge (public contract + LSL transport)
- Sample configs, onboarding docs, CI/release automation

It does **not** ship:

- Coupled oscillator dynamics runtime
- Private twin orchestration backend
- Private APKs or study presets
- Study-locked runtime configurations

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
4. Known Unity project paths under `~/source/repos/`

## Twin Mode Protocol

The public twin bridge uses LSL streams compatible with the AstralKarateDojo
twin architecture:

- **Commands out**: `quest_twin_commands` / `quest.twin` (operator → headset)
- **State in**: `quest_twin_state` / `quest.twin` (headset → operator)
- **Config out**: `quest_hotload_config` / `quest.config` (full config snapshots)

Snapshot protocol: `begin → set key=value → end` frame sequence.

## Reference Repos

- `AstralKarateDojo/QuestSessionKit` — PowerShell Quest session launcher, APK
  catalog, hotload profiles, device profiles, LSL monitoring
- `AndroidPhoneQuestCompanion` — Android companion app patterns
- `PolarH10` — GitHub Pages structure, release pipeline, onboarding patterns

## Smart App Control Note

This machine may block `dotnet test` when unsigned assemblies are loaded.
Workaround: single-file publish with
`dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=false`.

## Skills Gap — Aspirational References

Two skill names were referenced during initial planning but do **not** exist on
disk or in any Copilot skill registry:

| Skill name | Intent | Status |
|---|---|---|
| `$bureau-context` | Cross-project conventions from `~/Agent Bureau/` (naming, branching, CI patterns shared across repos) | Not yet authored — create as a Copilot skill when the Agent Bureau directory has enough material |
| `uncodixify` | UI/UX design guidance (layout, colour, accessibility, WPF styling conventions) | Not yet authored — define scope before writing |

Until these skills are created, agents should rely on the rules in this file and
`.copilot-instructions.md` for project conventions.
