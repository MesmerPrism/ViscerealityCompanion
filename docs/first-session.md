---
title: First Session
description: Connect Quest, install the supplied APK, launch it, and verify live monitoring from the Windows app.
summary: This is the operator-first walkthrough. It assumes the research team already gave you the APK and the app target to use.
nav_label: First Session
nav_group: Start Here
nav_order: 20
---

# First Session

This guide walks through the clean operator path in the desktop app. Use the
`Start Here` tab if you want the shortest route through the UI.

## 1. Connect Your Quest

Plug the Quest into your Windows machine via USB cable, then:

**WPF App:** Click **Probe USB**, then **Enable Wi-Fi ADB**, then enter the
Quest IP address and click **Connect Quest**.

**CLI:**

```powershell
viscereality probe
viscereality wifi
viscereality connect 192.168.43.1:5555
```

The first time you do this on a machine, approve the USB debugging prompt in
the headset.

## 2. Select The Study Build

In the app:

- open `Start Here` or `Quest Library`
- run `Refresh Device Snapshot` first if you want the app to adopt a known
  foreground target automatically
- if Quest Home or another non-catalog app is foreground, select the supplied
  target manually
- confirm the bundle, runtime preset, and device profile
- browse to the APK file if it is not already staged in the catalog

In the committed public sample catalog, that target is currently `LslTwin`.

## 3. Install And Launch

**WPF App:** Click **Install App**, then **Launch App**.

**CLI:**

```powershell
viscereality install path/to/viscereality.apk
viscereality launch com.example.package
```

## 4. Set Quest Performance

Use the CPU and GPU fields in the app and apply the study-recommended values.

**CLI:**

```powershell
viscereality perf 4 4
```

## 5. Confirm Live State

Use the app to confirm:

- the foreground package is the one you launched
- the headset snapshot reports the expected model, battery, and CPU/GPU state
- `quest_monitor` is live if the build publishes the lightweight monitor outlet
- twin-state frames appear when the supplied APK includes the twin publisher

**CLI:**

```powershell
viscereality status
viscereality monitor --stream quest_monitor --type quest.telemetry
```

## 6. Keep Session Control On Windows

The current research mode is remote-only by default:

- the operator app tracks requested config values
- the APK reports state
- on-device settings changes are not required for this first working mode

When you need to mark or pause a session, use the buttons on `Start Here` or
the detailed controls in `Twin Monitor`.

## 7. End Session

**WPF App:** Click **Export Manifest** to save session metadata, then close the app.

**CLI:**

```powershell
viscereality twin send twin-pause
```

## Catalog Discovery

The app auto-discovers catalogs in this order:

1. `VISCEREALITY_QUEST_SESSION_KIT_ROOT` environment variable
2. `samples/quest-session-kit/` relative to the repo root
3. `samples/quest-session-kit/` next to the executable
4. `~/source/repos/AstralKarateDojo/QuestSessionKit/`

Override with the `--root` flag in the CLI or set the environment variable.
