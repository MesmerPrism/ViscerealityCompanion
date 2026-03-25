# Viscereality Companion

Public Windows-first operator shell for the Viscereality workflow.

This repo is the public-facing setup for a desktop app that combines the
operator patterns from:

- the Viscereality Quest-side session kit and LSL monitor flow
- the Android phone Quest companion app
- the twin-mode control contract
- the public-docs and release posture used in `PolarH10`

The goal is a Windows app and public git that provide onboarding, downloads,
sample session-kit contracts, public oscillator config editing, and the
operator interface surface without publishing the private job-system dynamics
implementation.

## What Is Public Here

- WPF desktop shell under `src/ViscerealityCompanion.App`
- public transport and twin-mode abstractions plus preview implementations
- sample Quest Session Kit catalogs under `samples/quest-session-kit/`
- sample oscillator config profiles under `samples/oscillator-config/`
- docs site source under `docs/`
- CI, Pages, and tagged release automation under `.github/workflows/`

## What Stays Private

This repo intentionally does **not** include:

- the job-system coupling dynamics implementation
- the live twin-mode/runtime handoff backend
- private APK payloads
- study-specific presets or secrets

Use a local-only overlay such as `src/ViscerealityCompanion.Private/` when you
need to attach those pieces.

## Quick Start

```powershell
git clone <your-public-url> ViscerealityCompanion
cd ViscerealityCompanion
dotnet build ViscerealityCompanion.sln
dotnet test ViscerealityCompanion.sln
dotnet run --project src/ViscerealityCompanion.App
```

The app starts from the committed sample session-kit catalogs and public
oscillator config profiles. When `adb` is available on the Windows machine, the
desktop shell can install APKs, connect to Quest, apply CPU/GPU levels, launch
apps, and poll headset status directly. Runtime preset and oscillator config
changes stay operator-side in the current remote-only mode.

## Docs And Site

Start with:

- [Docs Home](docs/index.md)
- [Getting Started](docs/getting-started.md)
- [App Overview](docs/app-overview.md)
- [Download](docs/download.md)
- [Private Split](docs/private-split.md)

Build the static docs site locally with:

```powershell
npm install
npm run pages:build
```

## Releases

Pushing a `v*` tag triggers the Windows release workflow. It publishes a
portable `win-x64` desktop zip and checksum files to GitHub Releases. The
public download page points to those tagged artifacts.

## Structure

- `src/ViscerealityCompanion.Core/` public models, loaders, abstractions, preview services
- `src/ViscerealityCompanion.App/` WPF operator shell
- `tests/ViscerealityCompanion.Core.Tests/` loader and manifest tests
- `samples/quest-session-kit/` sample library, preset, and device-profile contracts
- `samples/oscillator-config/` public oscillator-config catalog and profiles
- `docs/` public onboarding and reference pages
- `tools/site/` static Pages build

## License

[MIT](LICENSE)
