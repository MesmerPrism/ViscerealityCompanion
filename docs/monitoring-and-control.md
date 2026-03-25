---
title: Monitoring and Control
description: How the Windows app handles live telemetry, twin-state tracking, remote triggers, and operator-side control during a session.
summary: "The current workflow is remote-first: Windows owns the operator controls, while the headset stays participant-facing and reports state back through ADB and LSL."
nav_label: Monitoring & Control
nav_group: Operator Guides
nav_order: 40
---

# Monitoring and Control

The app is built for supervised research sessions where the participant should
not need to navigate setup or control surfaces inside the headset.

## Remote-First Mode

The current working mode is intentionally simple:

- Windows owns the operator controls
- the Quest build is the participant-facing runtime
- triggers come from the desktop app
- tracked config changes are staged from the desktop app
- live state is reported back through ADB snapshots and, when available, LSL streams

This is why the app exposes the `Remote-only research control` toggle directly
in the main shell.

## What To Watch During A Session

### ADB-backed checks

Use the app to confirm:

- the active Quest endpoint
- battery level
- CPU and GPU settings
- foreground package and visible activities
- whether the expected package is still running in front

### Lightweight monitor stream

`LSL Monitor` is for fast transport confirmation:

- default stream name: `quest_monitor`
- default stream type: `quest.telemetry`
- default channel index: `0`

If the supplied APK publishes the lightweight monitor outlet, this page gives
you the quickest live check.

### Twin-state tracking

`Twin Monitor` is the richer comparison surface:

- bridge status
- live publisher detection
- requested vs reported values
- raw twin events

Use this page when you need to verify whether the headset is reporting the
state you expect, not just whether the basic monitor stream is alive.

## What The Operator Can Control Today

- connect the Quest
- install and launch the APK
- apply Quest CPU and GPU levels
- restart monitor subscriptions
- send twin commands such as `start`, `pause`, `resume`, and `marker`
- publish tracked runtime-config snapshots from Windows
- export a session manifest

## What Is Deliberately Not Required Yet

The first working mode does not depend on the APK exposing its own control UI.
That is deliberate. The operator flow is meant to work even when the headset
user should only wear the device and follow the study instructions.
