# Third-Party Dependencies

This repository's source code is licensed under the [MIT License](LICENSE).

That MIT license applies to the Viscereality Companion source tree itself. It
does not relicense third-party runtimes, device tools, APK payloads, or other
upstream artifacts that may be bundled with a release build or fetched by the
operator tooling.

## Current Public Dependency Boundary

### Meta Horizon Debug Bridge (`hzdb`)

- The repo does not ship Meta's `hzdb` Windows binary in source control or in
  the GitHub release assets.
- The guided preview installer and the CLI `tooling install-official` command
  fetch the current published Windows package from Meta's npm publication:
  `@meta-quest/hzdb-win32-x64`.
- Upstream license/terms:
  [Meta Platform Technologies SDK License Agreement](https://developers.meta.com/horizon/licenses/)
- This repo does not claim to relicense `hzdb` under MIT.

### Android SDK Platform-Tools (`adb`, `fastboot`, related files)

- The repo does not ship Google's Android SDK Platform-Tools in source control
  or in the GitHub release assets.
- The guided preview installer and the CLI `tooling install-official` command
  fetch the current published Windows package from Google's Android SDK
  repository metadata and download host.
- Upstream license/terms:
  [Android Software Development Kit License Agreement](https://developer.android.com/studio/releases/platform-tools)
- This repo does not claim to relicense Android SDK Platform-Tools under MIT.

### Bundled Public Study Payloads

- This public repo may mirror explicitly approved public study payloads needed
  by the Windows operator flow, such as the curated Sussex APK mirror under
  `samples/quest-session-kit/APKs/`.
- Those payloads are not implicitly relicensed under MIT by virtue of being
  stored or distributed from this repo.

### Bundled Runtime Libraries

- Some packaged Windows builds include third-party runtime libraries needed for
  the operator path, such as the bundled Windows `lsl.dll` runtime.
- Those upstream libraries remain under their own terms. The repo's MIT license
  does not override or replace those upstream terms.

### Native PDF Rendering Libraries

- Sussex validation and diagnostics PDFs are generated in .NET with the
  `PDFsharp-MigraDoc` package family from empira Software GmbH.
- Upstream project/docs: <https://docs.pdfsharp.net/>
- Upstream repository: <https://github.com/empira/PDFsharp>
- Upstream license: MIT
- Those upstream libraries remain under their own MIT terms in packaged builds;
  this repo does not claim authorship of them or relicense them under a
  different license.

## Practical Rule

If a file or runtime comes from this repo's own source tree, MIT is the default
license unless a more specific notice says otherwise. If a file or runtime is
mirrored from or fetched from an upstream publisher, treat the upstream
publisher's license and terms as authoritative.
