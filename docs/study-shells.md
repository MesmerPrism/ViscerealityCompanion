---
title: Study Shells
description: Reusable simplified operator windows for specific studies such as the Sussex University controller-breathing session.
summary: Study shells keep the full operator app available, but let you open a narrower window with one pinned build, one pinned device profile, and only the live metrics that a specific experimenter needs.
nav_label: Study Shells
nav_group: Operator Guides
nav_order: 35
---

# Study Shells

The main app now supports dedicated study shells: focused operator windows that
sit on top of the same public ADB and twin-monitoring services without
requiring a separate app build.

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

## Sussex University Shell

The first committed study shell is `Sussex University`, backed by
`samples/study-shells/sussex-university.json`.

It currently pins:

- package id: `com.Viscereality.LslTwin`
- version: `0.1.0`
- SHA256: `B1D5529516F9867FEF790C276EA3324415188B1B3AB4A54FD32B3705654F3D3C`
- device profile: `CPU 2 / GPU 5 / static foveation level 1`
- expected LSL input target: `quest_biofeedback_in / quest.biofeedback`

The window checks:

- whether the locally staged APK matches the pinned study hash
- whether the same build is already installed on the headset
- whether the pinned Quest device profile is currently active
- live LSL connectivity counts from `quest_twin_state`
- controller breathing, heartbeat, and coherence values from the study runtime

The current public shell can also send the study recenter command.

## Current Limits

Two Sussex items still depend on new scene-side telemetry or commands from the
Quest runtime:

- recenter drift distance from the last recenter point
- particle visibility on/off control

The study shell already reserves space for those states, but if the current APK
does not publish them, the UI will show that they are not exposed yet instead
of guessing.

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
