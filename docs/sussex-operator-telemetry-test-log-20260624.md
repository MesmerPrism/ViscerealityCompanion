# Sussex operator telemetry test log - 2026-06-24

This log tracks the companion-side changes and exact commands used to prepare the next Sussex/AKD live validation pass.

## Scope

- Replace temporary live-test helpers with companion CLI commands that mirror GUI actions.
- Keep the raw `twin send` command available, but use study-shell commands for collaborator-facing tests.
- Record every command used during local verification and the next controller/headset session.

## Durable command surface

- `probe --json` mirrors the USB probe with machine-readable output.
- `study actions <study>` lists named GUI-equivalent study actions.
- `study action <study> <action>` sends named underlying Quest twin actions such as `calibrate` and `controller-volume`, then waits for quest twin-state acknowledgement. The `start-recording` and `stop-recording` actions are low-level diagnostics only, are blocked by default, and require `--allow-recording-command-diagnostic`; participant-run parity still uses the WPF Experiment Session UI or `Start-Sussex-VerificationHarness.ps1 -UiInputParity`.
- `study snapshot <study>` captures a bounded quest twin-state snapshot for the study-test keys.
- `study test-sender run <study>` mirrors the GUI TEST sender toggle by applying temporary LSL/direct-LSL routing, publishing the synthetic HRV stream, and restoring routing when the run ends if a prior route snapshot was available.

## Implementation-phase command log

Working directory for all commands below:

```powershell
S:\Work\repos\reference\ViscerealityCompanion
```

1. Build after adding study CLI commands:

```powershell
dotnet build .\ViscerealityCompanion.sln --no-restore
```

Result: passed.

2. Focused CLI diagnostics tests:

```powershell
dotnet test .\tests\ViscerealityCompanion.Integration.Tests\ViscerealityCompanion.Integration.Tests.csproj --no-build --filter "FullyQualifiedName~CliStudyCommandTests|FullyQualifiedName~CliDiagnosticsCommandTests"
```

Result: passed, 9 tests.

3. Built CLI smoke checks:

```powershell
dotnet .\src\ViscerealityCompanion.Cli\bin\Debug\net10.0\viscereality.dll probe --help
dotnet .\src\ViscerealityCompanion.Cli\bin\Debug\net10.0\viscereality.dll study action --help
dotnet .\src\ViscerealityCompanion.Cli\bin\Debug\net10.0\viscereality.dll study actions sussex-university
dotnet .\src\ViscerealityCompanion.Cli\bin\Debug\net10.0\viscereality.dll study test-sender run --help
```

Result: passed. `study actions sussex-university` listed the GUI-equivalent aliases including `calibrate`, `controller-volume`, `start-recording`, and `stop-recording`.

4. Short TEST sender run before native LSL log suppression:

```powershell
dotnet .\src\ViscerealityCompanion.Cli\bin\Debug\net10.0\viscereality.dll study test-sender run sussex-university --duration-seconds 2 --no-routing --json
```

Result: sender started and stopped successfully, but liblsl native INFO/WARN lines followed the JSON output.

5. Manual `LSLAPICFG` check:

```powershell
$cfg = Join-Path $PWD 'artifacts\local\quiet-lsl-api.cfg'; New-Item -ItemType Directory -Force -Path (Split-Path $cfg) | Out-Null; Set-Content -Path $cfg -Value "[log]`nlevel = -2`n"; $env:LSLAPICFG = $cfg; dotnet .\src\ViscerealityCompanion.Cli\bin\Debug\net10.0\viscereality.dll study test-sender run sussex-university --duration-seconds 1 --no-routing --json
```

Result: sender started and stopped successfully with clean JSON output. This confirmed that the LSL config was correct and the app needed to sync the config into the native C runtime before liblsl initialized.

6. Build after adding the resolver-side quiet LSL config:

```powershell
dotnet build .\ViscerealityCompanion.sln --no-restore
```

Result: passed.

7. Default TEST sender JSON check after resolver update:

```powershell
Remove-Item Env:\LSLAPICFG -ErrorAction SilentlyContinue; dotnet .\src\ViscerealityCompanion.Cli\bin\Debug\net10.0\viscereality.dll study test-sender run sussex-university --duration-seconds 2 --no-routing --json
```

Result: sender started and stopped successfully with clean JSON output and no manual `LSLAPICFG`.

8. Focused CLI tests after resolver update:

```powershell
dotnet test .\tests\ViscerealityCompanion.Integration.Tests\ViscerealityCompanion.Integration.Tests.csproj --no-build --filter "FullyQualifiedName~CliStudyCommandTests|FullyQualifiedName~CliDiagnosticsCommandTests"
```

Result: passed, 9 tests.

9. Core test project:

```powershell
dotnet test .\tests\ViscerealityCompanion.Core.Tests\ViscerealityCompanion.Core.Tests.csproj --no-build --logger "console;verbosity=normal"
```

Result: passed, 186 tests.

10. CLI integration group:

```powershell
dotnet test .\tests\ViscerealityCompanion.Integration.Tests\ViscerealityCompanion.Integration.Tests.csproj --no-build --filter "FullyQualifiedName~Cli" --logger "trx;LogFileName=integration-cli.trx" --results-directory .\TestResults\integration-cli
dotnet test .\tests\ViscerealityCompanion.Integration.Tests\ViscerealityCompanion.Integration.Tests.csproj --no-build --filter "FullyQualifiedName~Cli"
```

Result: passed, 15 tests.

11. Full integration assembly:

```powershell
dotnet test .\tests\ViscerealityCompanion.Integration.Tests\ViscerealityCompanion.Integration.Tests.csproj --no-build --logger "trx;LogFileName=integration-full.trx" --results-directory .\TestResults\integration-full
```

Result: failed with 169 passed, 1 failed. Failure was `ViscerealityCompanion.Integration.Tests.HzdbCommandTests.Hzdb_perf_capture_short_trace`, expected `OperationOutcomeKind.Success` but got `Failure`. This is a hardware/hzdb perf-trace integration test, not part of the new CLI command surface. Avoid rerunning full Quest/hzdb integration before reserving hardware via Agent Board.

12. Final build and CLI group after tightening command acknowledgement matching:

```powershell
dotnet build .\ViscerealityCompanion.sln --no-restore
dotnet test .\tests\ViscerealityCompanion.Integration.Tests\ViscerealityCompanion.Integration.Tests.csproj --no-build --filter "FullyQualifiedName~Cli"
```

Result: build passed; CLI integration group passed, 15 tests.

13. Publish actual desktop app artifact with bundled CLI:

```powershell
& .\tools\app\Publish-Desktop-App.ps1
```

Result: passed. Published app:

```text
S:\Work\repos\reference\ViscerealityCompanion\artifacts\publish\ViscerealityCompanion.App\ViscerealityCompanion.exe
```

Bundled CLI:

```text
S:\Work\repos\reference\ViscerealityCompanion\artifacts\publish\ViscerealityCompanion.App\cli\current\Viscereality CLI.exe
```

14. Published CLI smoke checks:

```powershell
Get-ChildItem -Path .\artifacts\publish\ViscerealityCompanion.App\cli\current -Force | Select-Object Name,Length,LastWriteTime
& '.\artifacts\publish\ViscerealityCompanion.App\cli\current\Viscereality CLI.exe' study actions sussex-university
& '.\artifacts\publish\ViscerealityCompanion.App\cli\current\Viscereality CLI.exe' study test-sender run sussex-university --duration-seconds 1 --no-routing --json
```

Result: published CLI contained `Viscereality CLI.exe`, listed the named study actions, and ran the synthetic HRV TEST sender with clean JSON output.

15. Republish after updating bundled CLI documentation:

```powershell
& .\tools\app\Publish-Desktop-App.ps1
```

Result: passed. The published app artifact now includes the updated `docs\cli.md` command reference.

16. Final routed TEST sender JSON pass after suppressing all app-owned native LSL logs:

```powershell
dotnet build .\ViscerealityCompanion.sln --no-restore
dotnet test .\tests\ViscerealityCompanion.Integration.Tests\ViscerealityCompanion.Integration.Tests.csproj --no-build --filter "FullyQualifiedName~Cli"
& .\tools\app\Publish-Desktop-App.ps1
Remove-Item Env:\LSLAPICFG -ErrorAction SilentlyContinue; & '.\artifacts\publish\ViscerealityCompanion.App\cli\current\Viscereality CLI.exe' study test-sender run sussex-university --duration-seconds 1 --json
```

Result: build passed; CLI integration group passed, 15 tests; publish passed; published routed TEST sender run returned clean JSON with `OpenOutcome`, `RouteOutcome`, `StartOutcome`, and `StopOutcome` all successful. `RestoreOutcome` was preview because no headset twin-state snapshot was available in this local no-headset smoke run.

17. Strict GUI/CLI command acknowledgement contract pass:

```powershell
dotnet test .\tests\ViscerealityCompanion.Core.Tests\ViscerealityCompanion.Core.Tests.csproj --no-restore
dotnet build .\src\ViscerealityCompanion.App\ViscerealityCompanion.App.csproj --no-restore
dotnet build .\src\ViscerealityCompanion.Cli\ViscerealityCompanion.Cli.csproj --no-restore
```

Result: the app build passed, but the parallel Core/CLI invocations hit a shared `ViscerealityCompanion.Core.dll` output lock from `VBCSCompiler`. This was a test-execution conflict from parallel builds, not a code failure. Rerun sequentially below with shared compilation disabled.

```powershell
dotnet test .\tests\ViscerealityCompanion.Core.Tests\ViscerealityCompanion.Core.Tests.csproj --no-restore /p:UseSharedCompilation=false
dotnet build .\src\ViscerealityCompanion.App\ViscerealityCompanion.App.csproj --no-restore /p:UseSharedCompilation=false
dotnet build .\src\ViscerealityCompanion.Cli\ViscerealityCompanion.Cli.csproj --no-restore /p:UseSharedCompilation=false
dotnet test .\tests\ViscerealityCompanion.Integration.Tests\ViscerealityCompanion.Integration.Tests.csproj --no-restore /p:UseSharedCompilation=false --filter "FullyQualifiedName~StudyShellSnapshotPolicyTests|FullyQualifiedName~CliStudyCommandTests"
dotnet run --no-build --project .\src\ViscerealityCompanion.Cli\ViscerealityCompanion.Cli.csproj -- study actions sussex-university
```

Result: Core tests passed, 191 tests. App build passed. CLI build passed. Filtered integration tests passed, 16 tests, covering runtime-config blocker policy, CLI study command surface, and the guard that blocks raw `study action start-recording` without `--allow-recording-command-diagnostic`. CLI action-list smoke check passed and listed the GUI-equivalent `calibrate`, `start-recording`, and `stop-recording` actions.

Implemented behavior: GUI study commands now wait for a fresh headset acknowledgement before reporting success or allowing workflow callers to continue. The shared acknowledgement contract requires the expected `study.command.last_action_sequence` and compatible action id, or a fresh matching action id when a sequence is unavailable. Missing acknowledgement is a failure for GUI sequencing. The CLI `study action` command uses the same contract and exits with code `2` when acknowledgement was requested but not observed before timeout. Raw recording start/stop commands are blocked unless the operator passes `--allow-recording-command-diagnostic`. For actual participant-run parity, the next live pass should use the UI input parity harness rather than raw `study action start-recording`.

18. Live UI input parity attempt paused:

```powershell
dotnet build .\tools\ViscerealityCompanion.VerificationHarness\ViscerealityCompanion.VerificationHarness.csproj --no-restore /p:UseSharedCompilation=false
$env:VISCEREALITY_ADB_EXE='S:\Work\tools\Android\windows-sdk\platform-tools\adb.exe'
$env:VISCEREALITY_LSL_DLL='C:\Users\tillh\Tools\liblsl\1.16.2\bin\lsl.dll'
$env:VC_UI_INPUT_PARITY='1'
dotnet run --no-build --project .\tools\ViscerealityCompanion.VerificationHarness\ViscerealityCompanion.VerificationHarness.csproj
```

Result: harness build passed. The representative UI input parity run was started with the WPF Experiment Session UI path, then stopped before completion because the headset battery was depleted. No live headset/UI result should be treated as verified from this attempt. Earlier experimental external-sender diagnostics were stopped and reverted because they are not human-parity. The current branch is intentionally WIP/unverified until a charged-headset run completes.

## Live-test command log

Commands for the next controller/headset validation pass will be recorded here before the collaborator handoff is written.
