---
title: Study Shells
description: Reusable simplified operator modes for specific studies such as the Sussex University controller-breathing session.
summary: Study shells can either live inside the full operator app or, when the manifest requests it, open directly into a pinned study workspace and hide the broader operator surfaces.
nav_label: Study Shells
nav_group: Operator Guides
nav_order: 35
---

# Study Shells

The main app now supports dedicated study shells: focused operator modes that
sit inside the same public operator window without requiring a separate app
codebase.

## Why They Exist

Many sessions do not need the full `Quest Library`, `Runtime Config`, or raw
`Twin Monitor` surfaces during the run itself. A study shell trims the operator
view down to:

- one approved APK identity
- one pinned Quest device profile
- the live signals the experimenter actually needs to watch
- only the trigger buttons the study protocol allows

That keeps the participant-facing runtime simple while still letting the study
team choose between two packaging modes:

- embedded study mode, where the operator can still return to the full app
- dedicated study package, where the app opens straight into one shell and
  keeps the broader operator surfaces hidden

When a study shell is activated from `Start Here`, the first tab becomes the
study workspace and the main header switches into that study mode as well. If
the catalog manifest sets a startup study plus `lockToStartupStudy`, the app
opens there immediately on launch and does not expose the route back to the
main operator tabs.

## Startup And Locking

Study-shell startup behavior is controlled in `samples/study-shells/manifest.json`:

- `startupStudyId`: opens that study automatically when the app starts
- `lockToStartupStudy`: hides the broader operator tabs and the `Exit Study Mode`
  action for that package

That means dedicated study packages are now mainly a config choice:

- keep both fields empty/default for the full operator app
- set `startupStudyId` only if you want a shell to open first but still allow return to the main app
- set both fields when you want the packaged app to behave like a study-specific operator surface

## Sussex University Shell

The first committed study shell is `Sussex University`, backed by
`samples/study-shells/sussex-university.json`.

The committed public Sussex package now uses the dedicated mode above:

- startup study: `sussex-university`
- startup lock: `true`

That means the public Sussex preview is intended to behave like a dedicated
researcher-facing operator surface, not like the broader multi-study app.

It currently pins:

- package id: `com.Viscereality.LslTwin`
- version: `0.1.0`
- SHA256: `1155F28643901543ACEE8DED52E84DD8CEF5C3FCF07B65DAF4181B5B5A4CE8A1`
- bundled APK path: `../quest-session-kit/APKs/SussexControllerStudy.apk`
- device profile: `CPU 5 / GPU 5 / static foveation level 1`
- expected LSL input target: `HRV_Biofeedback / HRV`
- expected routing: `Controller Volume / LSL Heartbeat / LSL Direct`

## Self-Contained Sussex Package

The public Sussex preview is supposed to be self-contained:

- the packaged Windows install already includes the bundled Sussex APK
- the study shell already knows the approved hash and device profile
- the operator should not need a separate Astral checkout or a second APK handoff

The committed `samples/quest-session-kit/APKs/SussexControllerStudy.apk` is intentionally the
same Sussex APK mirrored from the Astral build used for the study. It is stored
through Git LFS in the source repo, but the packaged Windows install exposes
the real file directly to the app at runtime.

The embedded Sussex workspace checks:

- whether the bundled APK matches the approved study hash
- whether the same build is already installed on the headset
- whether the pinned Quest device profile is currently active
- live LSL target, connected stream, and inlet status from `quest_twin_state`

- routed `0..1` inbound HRV biofeedback when the runtime publicly echoes it
- controller breathing, heartbeat, and coherence values from the study runtime
- camera drift from the last recenter anchor
- particle visibility and whether rendering is suppressed by the operator or HUD
- current fps, frame time, and refresh-rate telemetry when the runtime publishes it

The active Sussex shell is organized into three operator views:

- `Pre-session` for connection, Sussex APK verification, and device-profile setup
- `During session` for the live monitoring and command surface
- `Inspect` for detailed study-runtime settings, focused live values, and recent twin events

The current public shell can also send the study recenter command and the
dedicated particle visibility on/off commands.

The Sussex shell now uses the bundled APK path from the app payload on
startup, so packaged Windows installs do not depend on a machine-local Astral
workspace just to find the Sussex APK.

## Sussex Verification Harness

The repo now includes a tracked live harness:

- command: `dotnet run --project tools/ViscerealityCompanion.VerificationHarness`
- output: `artifacts/verify/sussex-study-mode-live/`

That harness:

- opens the main WPF app
- activates `Sussex University experiment mode`
- starts a local float LSL sender on `HRV_Biofeedback / HRV`
- publishes smoothed `0..1` HRV biofeedback packets from this Windows machine on an irregular heartbeat-timed cadence
- installs, launches, and profiles the bundled Sussex APK
- captures screenshots plus a text report

At the moment, the public study telemetry confirms full sender -> headset inlet
connectivity through `study.lsl.connected_*` and `study.lsl.status`. If the
runtime also exposes `signal01.coherence_lsl` or a `driver.stream.*.value01`
mirror entry, the harness will measure round-trip latency for the direct
coherence value. The currently verified public Sussex build does expose that
value, so the harness now reports measured coherence loop latency in the text
report.

## How Study Shell Discovery Works

The app looks for study-shell definitions in this order:

1. `VISCEREALITY_STUDY_SHELL_ROOT`
2. `samples/study-shells/` relative to the repo root
3. `samples/study-shells/` next to the executable

That means new simplified study windows are primarily a data/config task:

- add a JSON definition
- optionally choose a startup study and whether that package should lock to it
- point it at the pinned package, hash, and device profile
- list the live keys and study commands to expose
- reopen the app and launch the shell from `Start Here`

No Unity scene code needs to live in this repo for that operator window to be
useful.
