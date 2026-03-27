---
title: First Session
description: Connect Quest, install the bundled Sussex APK or another supplied APK, launch it, and verify live monitoring from the Windows app.
summary: This is the operator-first walkthrough. Sussex operators can use the bundled Sussex APK from the packaged app; other teams can still point the app at a supplied APK.
nav_label: First Session
nav_group: Start Here
nav_order: 20
---

# First Session

This guide walks through the clean operator path in the desktop app. If your
study team already gave you a dedicated study shell such as Sussex, use that
surface first. The current Sussex package now opens there automatically on
launch. Otherwise use `Start Here`.

## Before You Touch The App

- make sure the Quest is in **developer mode**
- have one USB cable available for the first ADB trust step
- put the Windows machine and Quest on the same Wi-Fi network if you plan to use Wi-Fi ADB
- if this is Sussex, prefer the packaged preview install because it already bundles the Sussex APK and opens directly into the Sussex shell

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

## 2. Use The Right Operator Surface

In the app:

- if you installed the dedicated Sussex preview, stay in `Sussex University experiment mode`
- if you are in the full app, open the supplied study shell from `Start Here` when available
- browse to an APK only if the study shell or supplied target did not already ship it
- if Sussex mode is active, the bundled Sussex APK should already be staged for you

In the committed public sample catalog, that target is currently `LslTwin`.

If the study team already gave you a dedicated study shell, that path pins the
expected build and device profile for you and keeps the live monitor narrower.
The Sussex shell now stages its bundled Sussex APK automatically, and the
dedicated Sussex package opens directly into that shell instead of the full
operator workspace.

## 3. Install The Study Build

**WPF App:** In a study shell, click **Install Sussex APK**. In the Sussex
shell that uses the bundled Sussex APK by default. In the full app, click
**Install App**.

**CLI:**

```powershell
viscereality install path/to/viscereality.apk
```

If you are using the packaged Sussex preview, you should not need to browse for
another APK at this step.

## 4. Apply The Study Device Profile

If you are in a study shell, click **Apply Study Device Profile**. If you are
in the full app, use the CPU and GPU fields and apply the study-recommended
values.

**CLI:**

```powershell
viscereality perf 4 4
```

## 5. Launch And Confirm Live State

**WPF App:** In a study shell, click **Launch Study Runtime**. In the full app,
click **Launch App**.

Use the app to confirm:

- the top status cards show Quest connected, the Sussex APK installed, the device profile active, and live runtime active
- the foreground package is the one you launched
- the headset snapshot reports the expected model, battery, and CPU/GPU state
- `quest_monitor` is live if the build publishes the lightweight monitor outlet
- twin-state frames appear when the supplied APK includes the twin publisher
- if the study uses the Sussex shell, live monitoring should also show LSL input, coherence, recenter state, particles state, and performance

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

## What Healthy Looks Like

Before the participant starts, a good operator state is:

- the Quest is connected over USB or Wi-Fi ADB
- the intended study APK is selected and installed
- the headset reports the study runtime in front
- the study device profile has been applied
- the live runtime or study shell shows fresh twin-state timestamps

## 7. End Session

**WPF App:** Click **Export Manifest** to save session metadata, then close the app.

**CLI:**

```powershell
viscereality twin send twin-pause
```

## Sussex-Specific Operator Notes

- the dedicated Sussex package is meant to reduce researcher mistakes by hiding the broader operator tabs
- the bundled APK and study device profile should already match each other
- the top status cards should be treated as the main pre-session checklist
- if `Install Sussex APK` or `Launch Study Runtime` does nothing, check developer mode and ADB trust before assuming the Sussex APK is wrong

## Catalog Discovery

The app auto-discovers catalogs in this order:

1. `VISCEREALITY_QUEST_SESSION_KIT_ROOT` environment variable
2. `samples/quest-session-kit/` relative to the repo root
3. `samples/quest-session-kit/` next to the executable
4. `~/source/repos/AstralKarateDojo/QuestSessionKit/`

Override with the `--root` flag in the CLI or set the environment variable.
