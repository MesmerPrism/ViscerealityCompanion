# Contributing

ViscerealityCompanion is a public Windows-facing operator shell for the broader
Viscereality workflow. Keep changes focused on the public interface surface:

- WPF app shell
- session-kit catalog format and sample assets
- onboarding and troubleshooting docs
- CI, Pages, and release automation
- transport abstractions and preview implementations

Do not submit private compute or orchestration code here. In particular:

- do not add the coupled oscillator implementation
- do not add private twin-mode runtime code
- do not commit private APKs, unpublished study presets, or device-specific secrets
- curated public study bundles that intentionally ship with the docs and release assets are acceptable when they are explicitly approved for this repo

## Local workflow

```powershell
dotnet build ViscerealityCompanion.sln
dotnet test ViscerealityCompanion.sln
dotnet run --project src/ViscerealityCompanion.App
```

If you update the docs site:

```powershell
npm install
npm run pages:build
```

## Private overlay

The repo intentionally ignores:

- `src/ViscerealityCompanion.Private/`
- `tests/ViscerealityCompanion.Private.Tests/`
- `private/`

Use those locations for local-only adapters or private orchestration code.
