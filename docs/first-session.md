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

In the committed public sample catalog, that target is currently `Sussex Experiment APK`.

If the study team already gave you a dedicated study shell, that path pins the
expected build and device profile for you and keeps the live monitor narrower.
The Sussex shell now stages its bundled Sussex APK automatically, and the
dedicated Sussex package opens directly into that shell instead of the full
operator workspace.

For first-time Sussex setup, stay on the `Home` tab and use the
`Open Sequential Guide` window there. It walks the operator through USB probe,
Wi-Fi ADB handoff, Wi-Fi-only verification, APK/profile checks, kiosk launch,
LSL confirmation, particle verification, a controller-tracking guarded
calibration check, and the short 20 second validation capture. After that
capture finishes, the guide now gives one-click access to the Windows session
folder, the pulled Quest backup folder, and the formatted PDF preview report.
That validation step now keeps the clock-alignment process inline in the guide
itself: start burst first, the 20 second capture in the middle with sparse
drift probes armed in the background, then the matching end burst before
pullback and PDF generation. The LSL step now also includes `Probe Connection`,
which refreshes the ADB-backed headset snapshot and then reports the Quest inlet
target, pinned APK/profile readiness, the currently connected inlet stream, and
whether fresh `quest_twin_state / quest.twin.state` frames are making it back to
Windows.

When the guide reaches the final reset-and-handoff step, use `Open Experiment
Session Window` for the real participant. That dedicated popout is now the
preferred low-distraction live-run surface:

- enter the participant id there
- use `Start Recording` and `Stop Recording` there
- watch the recording state, breathing/coherence telemetry, and clock/network
  consistency there
- keep recenter, particles on/off, and Quest screenshot available there as
  secondary tools
- after `Stop Recording`, use that same window to open the Windows session
  folder, the pulled Quest backup folder, and the generated
  `session_review_report.pdf`

The broader Sussex shell still remains the tuning and profile-editing surface
before the study setup is locked in.

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

For the Sussex kiosk path, keep the headset on-face for launch and the first
visual verification pass. If the shell says **Wake Headset To Enable
Launching**, physically wake the headset first and do not launch from sleep.
On the current April 2026 Meta OS build, launching while asleep can leave
Sussex running in a black or limbo scene that may require a headset restart.
If the headset is sitting in Guardian or another Meta visual-blocker state,
clear that blocker first and wait for launch to re-enable. The app no longer
offers remote wake/sleep controls in the GUI for Sussex; manual wake/sleep is
the only supported operator path for now.
Kiosk pinning and the proximity hold are separate: launch first, confirm the
runtime is correct, and only then decide whether you need the optional
proximity bypass for a longer unattended phase. Also treat kiosk as best-effort
task pinning only: do not rely on it to disable the controller Meta / menu
button on this build.

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

The Sussex launch path now applies the same direct `prox_close`
keep-awake override used in Quest Multi Stream before the APK starts. The
sequential guide and Experiment Session window both surface that state, and the
operator can restore normal wear-sensor behavior later with **Enable
Proximity**.

On the current Quest build, that readback is intentionally literal:

- `Virtual proximity state: CLOSE` means the direct `prox_close` keep-awake
  override is active.
- `Virtual proximity state: DISABLED` means `automation_disable` restored normal
  wear-sensor behavior.

Treat that keep-awake override as a wear-sensor bypass, not as the kiosk
switch. Kiosk behavior still comes from task pinning. If you need a
participant-facing black screen or idle view during the run, prefer doing that
inside Unity instead of putting the headset to sleep with the hardware power
button.

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

**WPF App:** If you are in a kiosk study shell such as Sussex and the headset
is on-face with a visible runtime scene, the operator can click
**Exit Kiosk Runtime** before closing the app. That exit path clears active
proximity automation, stops task pinning, returns Home to the front, and then
stops the Sussex APK. Do not trigger that quit path from an off-face automated
cleanup pass on this machine. If exit matters, ask the wearer to quit while
wearing the headset, then capture a Quest screenshot and confirm the visible
headset scene before calling the run complete. For bench functionality tests,
leaving Sussex running is acceptable. Then click **Export Manifest** to save
session metadata.

**CLI:**

```powershell
viscereality twin send twin-pause
```

## Sussex-Specific Operator Notes

- the dedicated Sussex package is meant to reduce researcher mistakes by hiding the broader operator tabs
- the bundled APK and study device profile should already match each other
- the `Sequential Sussex Guide` is now the preferred onboarding surface for first-time setup and bench verification
- the `Experiment Session` window is now the preferred live participant-run surface after the guide handoff; the embedded shell tabs remain for broader tuning and inspection
- the top status cards should be treated as the main pre-session checklist
- the GUI no longer exposes remote headset wake/sleep controls for Sussex; use manual headset wake/sleep only
- if the launch button reads `Wake Headset To Enable Launching`, wake the headset first; Sussex launch is now blocked while the headset reports asleep
- if launch stays disabled because Guardian or another Meta visual blocker is active, clear that blocker in-headset before trying again
- on the current April 2026 Meta OS build, Sussex kiosk mode no longer reliably disables the controller Meta / menu button, so use screenshot/scene confirmation instead of menu-button behavior as the success signal
- controller breathing calibration is guarded by live controller tracking; if the right controller is not tracked, the GUI reports that state and the runtime does not start calibration
- calibrate from the `Experiment Session` window before `Start Recording` when the participant run should use controller breathing; `Start Recording` preserves the current controller calibration
- if `Install Sussex APK` or `Launch Study Runtime` does nothing, check developer mode and ADB trust before assuming the Sussex APK is wrong
- if the headset is already awake, normal Sussex command buttons should now react faster because the app no longer forces a full pre-send snapshot refresh before every command
- the `Experiment Session` window now keeps Quest screenshot capture, LSL clock-alignment consistency, and the live participant-run controls on one focused operator surface
- normal participant runs now also pull the Quest-side backup into `device-session-pull` and generate `session_review_report.pdf` after `Stop Recording`

## Catalog Discovery

The app auto-discovers catalogs in this order:

1. `VISCEREALITY_QUEST_SESSION_KIT_ROOT` environment variable
2. `samples/quest-session-kit/` relative to the repo root
3. `samples/quest-session-kit/` next to the executable
4. `~/source/repos/AstralKarateDojo/QuestSessionKit/`

Override with the `--root` flag in the CLI or set the environment variable.
