---
title: Runtime Config
description: Inspector-style runtime-config editing from Windows, aligned to the Astral scene categories and suitable for tracked operator-side changes.
summary: The config editor mirrors the Astral inspector structure so operators can work with familiar sections instead of an undifferentiated key dump.
nav_label: Runtime Config
nav_group: Operator Guides
nav_order: 50
---

# Runtime Config

The runtime-config editor is meant to feel like the config inspector from the
Astral scene, not like a generic spreadsheet of keys.

![Runtime Config tab](assets/operator-shell-runtime-config.png)

## Inspector Sections

The editor is grouped into:

- `Session + Inputs`
- `Headset`
- `APK Runtime`
- `Twin + Timing`
- `All`

That split keeps Quest device policy separate from APK-side runtime policy while
still leaving the full public key surface available when needed.

## How To Use It

1. Open `Runtime Config`.
2. Pick the staged profile you want to track.
3. Stay in `Session + Inputs` first, then move into `Headset` or `APK Runtime`.
4. Use `Twin + Timing` when you need sync policy, runtime timing, or the runtime-config JSON bridge without mixing those controls into every other field.
5. Use `All` only when you need the complete document view.

The public `LslTwin` sample now auto-selects the `Viscereality Scene Baseline`
profile when that app is selected in the library.

## Current Session Behavior

Today the operator app can:

- load public config profiles
- edit the tracked values from Windows
- export the current tracked config
- publish the tracked snapshot over the desktop-side transport path

The desktop app is the control surface. The participant-facing APK does not need
to expose its own settings UI for this workflow to be useful.

## Why This Matters

Most operators should not need the Unity project open just to review or stage a
few settings during a session. This editor keeps the shape of the real runtime
config visible while making the session workflow usable from a normal Windows
app.
