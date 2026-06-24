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
- Focused tests covering Peripersonal command transport, workflow service,
  study data recording, and keep-awake launch policy.
- This tracked handoff folder.

Validation before Companion push:

```powershell
dotnet test tests\ViscerealityCompanion.Core.Tests\ViscerealityCompanion.Core.Tests.csproj --filter "FullyQualifiedName~WindowsAdbQuestControlServiceTests|FullyQualifiedName~PeripersonalCommandTransportTests|FullyQualifiedName~PeripersonalOperatorWorkflowServiceTests|FullyQualifiedName~StudyDataRecorderServiceTests"
```

Result: passed, 56 tests.

## Unity / XR Runtime

- Repository: `https://github.com/GeorgeFejer91/peripersonal-space-experiment-2025-12-10.git`
- Remote in this checkout: `origin`
- Branch: `codex/peripersonal-wpf-unity-runtime-20260620`
- Commit: `b8e853f Update Peripersonal runtime E2E bridge`
- Push command:

```powershell
git push origin codex/peripersonal-wpf-unity-runtime-20260620
```

Validation before push:

- `git diff --check` passed.
- The completed end-to-end run used the bundled runtime APK hash recorded in
  `apks/sha256sums.txt`.

## Quest Questionnaire Panel

- Repository: `https://github.com/MesmerPrism/quest-questionnaire-panel.git`
- Remote in this checkout: `origin`
- Branch: `codex/peripersonal-operator-runtime-20260618`
- Commit: `1538fb5 Add questionnaire foreground return intent`
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

## Binary Evidence Bundle

The shareable ZIP is generated under `artifacts/` and is intentionally not
committed. It includes the APKs, checksums, selected run evidence, and a package
manifest with the exact three-repo branch heads at bundle time.
