---
title: First Session
description: End-to-end walkthrough for a Quest operator session.
nav_order: 30
---

# First Session

This guide walks through a complete Quest operator session using
ViscerealityCompanion — from USB connection to live monitoring.

## 1. Connect Your Quest

Plug the Quest into your Windows machine via USB cable, then:

**WPF App:** Click **Probe USB**, then **Enable Wi-Fi ADB**, then
enter the Quest IP address and click **Connect**.

**CLI:**

```powershell
viscereality probe
viscereality wifi
viscereality connect 192.168.43.1:5555
```

## 2. Set Performance Levels

**WPF App:** Set CPU and GPU levels (0–5) and click **Apply Performance**.

**CLI:**

```powershell
viscereality perf 4 4
```

## 3. Install and Launch an App

**WPF App:** Select an app from the catalog, optionally browse to the APK file,
then click **Install** followed by **Launch**.

**CLI:**

```powershell
viscereality install path/to/viscereality-twin.apk
viscereality launch org.aliusresearch.viscereality.twin
```

## 4. Start Twin Mode

Twin mode disables on-device control and routes all parameter changes through
the Windows companion via LSL.

**WPF App:** Click **Start twin session** in the Twin Commands panel.

**CLI:**

```powershell
viscereality twin send twin-start
```

## 5. Monitor Telemetry

**WPF App:** The LSL Monitor panel shows live stream values, sample rate,
and status.

**CLI:**

```powershell
viscereality monitor --stream quest_monitor --type quest.telemetry
```

Press `Ctrl+C` to stop monitoring.

## 6. Push Runtime Config

Select a hotload profile or edit runtime parameters in the workspace editor,
then publish. The companion sends the full config snapshot via LSL to the Quest.

## 7. End Session

**WPF App:** Click **Export Manifest** to save session metadata, then close.

**CLI:**

```powershell
viscereality twin send twin-pause
```

## Catalog Discovery

The app auto-discovers catalogs in this order:

1. `VISCEREALITY_QUEST_SESSION_KIT_ROOT` environment variable
2. `~/source/repos/AstralKarateDojo/QuestSessionKit/`
3. `samples/quest-session-kit/` next to the executable
4. `samples/quest-session-kit/` relative to the repo root

Override with the `--root` flag in the CLI or set the environment variable.
