# Viscereality Companion

Viscereality Companion is the public Windows operator app for
AstralKarateDojo-based research sessions. It is meant for people who receive a
Quest APK from the study team, or use a bundled public study build such as the
Sussex package, and need a stable desktop tool to install it, launch it,
monitor live state, and keep session control on the operator side.

Start with [Docs Home](docs/index.md) or the live
[Pages site](https://mesmerprism.github.io/ViscerealityCompanion/).
If a public research preview release exists, install it from
[Download & Install](https://mesmerprism.github.io/ViscerealityCompanion/download.html).

This repo is deliberately separate from the Unity scene repo. It does not copy
AstralKarateDojo internals into the public tree. Instead, it ships the Windows
surface around that workflow plus curated public operator payloads that the
desktop release needs:

- WPF desktop app for Quest connection, install, launch, monitoring, and runtime-config staging
- reusable study-shell windows for simplified experiment-specific operator flows
- CLI for scriptable ADB, LSL, and twin command workflows
- repo-local Sussex sample catalog, scene-matched hotload baseline, device profiles, and the bundled Sussex APK
- public runtime-config editor that mirrors the Astral inspector layout
- Pages docs and release automation
- Windows packaging scaffolding for a single branded launcher install path

## Who It Is For

- operators running study sessions on Windows
- collaborators who need the desktop control surface but not the Unity scene code
- developers who need the transport, onboarding, and release repo around the Quest build

## Core Workflow

1. Connect the Quest over USB or Wi-Fi ADB.
2. Select the bundled or supplied app target and APK.
3. Install the APK, apply Quest CPU and GPU levels, and launch it.
4. Monitor headset state, LSL telemetry, and twin-state tracking from Windows.
5. Stage or publish tracked runtime-config changes from the desktop side when the study protocol calls for them.

The current research mode is intentionally remote-first: the desktop app is the
control surface, and the APK is treated as the participant-facing runtime.

## Verified Operator Path

Verified on March 25, 2026 against a live Quest reachable over Wi-Fi ADB:

- the app starts with no selected target until `Refresh Device Snapshot` runs
- Quest Home leaves the target empty, while a known app can still be selected manually in `Quest Library`
- selecting `LslTwin` and launching it from the GUI succeeded
- applying Quest performance levels from the GUI updated the live headset to `CPU 2 / GPU 2`
- `Twin Monitor` stayed stable and tracked `188` reported headset values from `quest_twin_state`
- the Sussex study shell pinned one APK hash plus one Quest device profile without exposing the full runtime-config surface

## Install Or Build

For operators, the intended path is the packaged launcher once preview releases
are published. The Sussex preview path now bundles the Sussex APK inside
the installed app payload, so operators do not need a separate APK download for
that study shell.

- Download/install guide: [docs/download.md](docs/download.md)
- First-session walkthrough: [docs/first-session.md](docs/first-session.md)
- Study-shell guide: [docs/study-shells.md](docs/study-shells.md)

For local development:

```powershell
git clone <repo-url> ViscerealityCompanion
cd ViscerealityCompanion
git lfs install
git lfs pull
dotnet build ViscerealityCompanion.sln
dotnet test ViscerealityCompanion.sln
dotnet run --project src/ViscerealityCompanion.App
```

`git lfs pull` matters here because the committed `samples/quest-session-kit/APKs/SussexControllerStudy.apk`
is the real Sussex APK bundled into the public package, not a placeholder.

If Windows Smart App Control or Code Integrity blocks the repo-built WPF app,
use:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1
```

That launcher keeps a single-file publish under
`artifacts/publish/ViscerealityCompanion.App/` and refreshes it when the app
sources or bundled sample assets change.

Build the docs site locally with:

```powershell
npm install
npm run pages:build
```

## Docs

Start with:

- [Docs Home](docs/index.md)
- [Download](docs/download.md)
- [First Session](docs/first-session.md)
- [Study Shells](docs/study-shells.md)
- [Monitoring and Control](docs/monitoring-and-control.md)
- [Runtime Config](docs/runtime-config.md)
- [Getting Started](docs/getting-started.md)

## Packaging

The repo includes Windows packaging scaffolding under
`src/ViscerealityCompanion.App.Package/` plus
`tools/app/Build-App-Package.ps1`.

That path is meant to produce one branded launcher entry for the installed app,
instead of asking operators to run the unpackaged repo build directly.

If you need to refresh the bundled Sussex APK from a fresh Astral build before
packaging, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Sync-Bundled-Sussex-Apk.ps1
```

That copies `AstralKarateDojo/Artifacts/APKs/SussexControllerStudy.apk` into
`samples/quest-session-kit/APKs/` and updates the pinned Sussex hash metadata.

`Build-App-Package.ps1` can do the same refresh inline via
`-RefreshBundledSussexApk` or `-BundledSussexApkSourcePath <path>`.

For repo-local desktop launchers on machines with Smart App Control, refresh the
shortcut to the safe single-file launcher with:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Refresh-Desktop-Launcher.ps1
```

## Scope

This public repo does not ship:

- the AstralKarateDojo Unity scene
- arbitrary private APK payloads or unpublished study presets
- scene-internal runtime code that belongs in the separate Unity repo

Approved public study bundles that are intentionally mirrored under
`samples/quest-session-kit/` and `samples/study-shells/` are part of scope for
this repo.

If you need to change the Quest runtime itself, do that in
`AstralKarateDojo`. If you need to run or support sessions from Windows, do it
here.

## License

[MIT](LICENSE)
