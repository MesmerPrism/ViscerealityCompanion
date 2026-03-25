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

If the current foreground app is outside the public catalog, the operator app
keeps the selected target empty. That prevents a false warning state from
appearing before you have chosen the real study APK.

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
- focused requested/reported inspection by section
- recent twin events without rendering every live value at once

Use this page when you need to verify whether the headset is reporting the
state you expect, not just whether the basic monitor stream is alive.

![Twin Monitor tab](assets/operator-shell-twin-monitor.png)

On March 25, 2026, this flow was verified live against `LslTwin` over Wi-Fi
ADB. The Windows app launched the Quest build, applied `CPU 2 / GPU 2`, and
tracked `188` reported headset values over `quest_twin_state` without the tab
crashing.

For study-specific windows such as the Sussex shell, the same twin-state stream
is reduced to a much narrower monitor:

- pinned build verification
- pinned device-profile verification
- LSL routing and connectivity
- controller breathing, heartbeat, and coherence values
- only the study trigger buttons that are actually allowed

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

Some study-shell controls still depend on new scene-side telemetry. For
example, the Sussex shell already reserves space for recenter drift distance
and particle visibility, but the public APK must publish those signals before
the window can do more than report that they are not exposed yet.
