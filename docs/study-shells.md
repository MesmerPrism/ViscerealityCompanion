---
title: Study Shells
description: Reusable simplified operator modes for specific studies such as the Sussex University controller-breathing session.
summary: Study shells keep the full operator app available, but let the first tab switch into a pinned study workspace with one build, one device profile, and only the live metrics that a specific experimenter needs.
nav_label: Study Shells
nav_group: Operator Guides
nav_order: 35
---

# Study Shells

The main app now supports dedicated study shells: focused operator modes that
sit inside the same public operator window without requiring a separate app
build.

## Why They Exist

Many sessions do not need the full `Quest Library`, `Runtime Config`, or raw
`Twin Monitor` surfaces during the run itself. A study shell trims the operator
view down to:

- one pinned APK identity
- one pinned Quest device profile
- the live signals the experimenter actually needs to watch
- only the trigger buttons the study protocol allows

That keeps the participant-facing runtime simple while still leaving the full
desktop shell available when the study team needs deeper diagnostics.

When a study shell is activated from `Start Here`, the first tab becomes the
study workspace and the main header switches into that study mode as well.

## Sussex University Shell

The first committed study shell is `Sussex University`, backed by
`samples/study-shells/sussex-university.json`.

It currently pins:

- package id: `com.Viscereality.LslTwin`
- version: `0.1.0`
- SHA256: `1E2344DA34CBF22BA45BE129C7F7B0B45ED6B321154D0120889543B99D1D81C2`
- device profile: `CPU 5 / GPU 5 / static foveation level 1`
- expected LSL input target: `quest_biofeedback_in / quest.biofeedback`
- expected routing: `Controller Volume / LSL Heartbeat / LSL Direct`

The embedded Sussex workspace checks:

- whether the locally staged APK matches the pinned study hash
- whether the same build is already installed on the headset
- whether the pinned Quest device profile is currently active
- live LSL target, connected stream, and inlet status from `quest_twin_state`

- direct `0..1` inbound coherence when the runtime publicly echoes it
- controller breathing, heartbeat, and coherence values from the study runtime
- camera drift from the last recenter anchor
- particle visibility and whether rendering is suppressed by the operator or HUD
- current fps, frame time, and refresh-rate telemetry when the runtime publishes it

The active Sussex shell is organized into three operator views:

- `Pre-session` for connection, pinned-build verification, and device-profile setup
- `During session` for the live monitoring and command surface
- `Inspect` for detailed pinned-runtime settings, focused live values, and recent twin events

The current public shell can also send the study recenter command and the
dedicated particle visibility on/off commands.

## Sussex Verification Harness

The repo now includes a tracked live harness:

- command: `dotnet run --project tools/ViscerealityCompanion.VerificationHarness`
- output: `artifacts/verify/sussex-study-mode-live/`

That harness:

- opens the main WPF app
- activates `Sussex University experiment mode`
- starts a local float LSL sender on `quest_biofeedback_in / quest.biofeedback`
- publishes direct `0..1` coherence packets from this Windows machine at a bench heartbeat cadence
- installs, launches, and profiles the pinned Sussex APK
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
- point it at the pinned package, hash, and device profile
- list the live keys and study commands to expose
- reopen the app and launch the shell from `Start Here`

No Unity scene code needs to live in this repo for that operator window to be
useful.
