# GitHub Push Notes

Use these branch heads when rebuilding the validated Peripersonal setup.

## Companion / Operator App

- Repository: `https://github.com/MesmerPrism/ViscerealityCompanion.git`
- Remote in this checkout: `upstream`
- Branch: `codex/peripersonal-wpf-unity-operator-20260620`
- Pushed base handoff commit: `7085a92 Add Peripersonal operator workflow handoff`
- Final branch-head commit for this handoff update is recorded in the generated
  ZIP manifest, because a tracked commit cannot contain its own final hash.

The Companion push includes:

- Peripersonal study-shell workflow changes.
- Companion CLI parity for operator-equivalent Peripersonal commands.
- Automatic HTTP forward setup for Unity status.
- Peripersonal keep-awake launch handling.
- Low-cadence foreground/input-owner listener used by both CLI and WPF.
- WPF `Input owner` card and sequential guide checks for the Peripersonal shell.
- Focused tests covering Peripersonal command transport, workflow service,
  study data recording, keep-awake launch policy, and foreground status
  classification.
- This tracked handoff folder.
- The refreshed Unity runtime APK and questionnaire panel APK mirrored in
  `samples/quest-session-kit/APKs/`.

Validation before Companion push:

```powershell
dotnet build ViscerealityCompanion.sln
dotnet test tests\ViscerealityCompanion.Core.Tests\ViscerealityCompanion.Core.Tests.csproj --filter FullyQualifiedName~PeripersonalForegroundStatusServiceTests
git diff --check
```

Results:

- solution build passed
- foreground status focused tests passed, 6 tests
- `git diff --check` passed with line-ending warnings only

## Unity / XR Runtime

- Repository: `https://github.com/GeorgeFejer91/peripersonal-space-experiment-2025-12-10.git`
- Remote in this checkout: `origin`
- Branch: `codex/peripersonal-wpf-unity-runtime-20260620`
- Commit: `13b18e4 Add Peripersonal foreground status beacons`
- Push command:

```powershell
git push origin codex/peripersonal-wpf-unity-runtime-20260620
```

Validation before push:

- `git diff --check` passed.
- The Unity runtime APK in the ZIP and Companion sample kit has SHA-256
  `3cf14c777cf67648b829743d48eea21a4ee28da1ac8a0a7049f458bd4c1d95bd`.
- The completed end-to-end run used the bundled runtime APK hash recorded in
  `apks/sha256sums.txt`.

## Quest Questionnaire Panel

- Repository: `https://github.com/MesmerPrism/quest-questionnaire-panel.git`
- Remote in this checkout: `origin`
- Branch: `codex/peripersonal-operator-runtime-20260618`
- Commit: `05da956 Add questionnaire foreground status beacons`
- Push command:

```powershell
git push origin codex/peripersonal-operator-runtime-20260618
```

Validation before push:

```powershell
git diff --check
.\gradlew.bat :app:assembleDebug
.\gradlew.bat :examples:native-caller:assembleDebug
```

Results:

- `git diff --check` passed with line-ending warnings only.
- `:app:assembleDebug` passed.
- `:examples:native-caller:assembleDebug` passed.
- The minimal panel APK in the ZIP has SHA-256
  `2768660e9af269f12dc8a39a6c98ce03d0890b4cf459602717983473a8920c3f`.
- The same panel APK is mirrored in the Companion sample kit for clone-and-run
  setup; the panel repo itself intentionally remains source-only for APK
  binaries.

## Binary Evidence Bundle

The shareable ZIP is generated under `artifacts/` and is intentionally not
committed. It includes the APKs, checksums, selected run evidence, and a package
manifest with the exact three-repo branch heads at bundle time.
