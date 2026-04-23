# Quest ADB / HZDB Recovery Notes

Date: 2026-03-28

Scope: Meta Quest control behavior observed from
`C:\Users\tillh\source\repos\ViscerealityCompanion` while debugging Sussex
study-shell launch failures on a Quest 3S over Wi-Fi ADB and USB ADB.

## Why This Exists

These notes capture the device-control findings that are easy to lose in shell
history:

- which `adb` and `hzdb` commands were tested
- which HorizonOS activities and services showed up in each blocked state
- which signals changed device state in a useful way
- which commands looked promising but did not solve the issue
- what the current best recovery sequence is

Current operator-facing docs and UI now use `runtime` / `task pinning`
wording. Any `kiosk` terminology below is historical context for the March
2026 lock-task behavior that was observed on this machine.

## Command Setup For This Repo

If you want this repo to stage the current official Quest tooling first, run:

```powershell
dotnet run --project src/ViscerealityCompanion.Cli -- tooling install-official
```

The shell snippets below keep plain `adb` for brevity. On a machine that is
using the managed LocalAppData cache directly, you can substitute:

```powershell
$adb = Join-Path $env:LOCALAPPDATA 'ViscerealityCompanion\tooling\platform-tools\current\platform-tools\adb.exe'
$hzdb = Join-Path $env:LOCALAPPDATA 'ViscerealityCompanion\tooling\hzdb\current\hzdb.exe'
```

## Primary Failure Shape

The recurring launch blocker was not a generic ADB disconnect. The device was
reachable, but the shell sat in a Meta visual-limbo stack:

- `com.oculus.guardian/com.oculus.vrguardianservice.guardiandialog.GuardianDialogActivity`
- `com.oculus.os.vrlockscreen/.SensorLockActivity`
- `com.oculus.vrshell/.FocusPlaceholderActivity`
- `com.oculus.os.clearactivity/.ClearActivity`
- `com.oculus.vrshell/.HomeActivity`
- `com.oculus.panelapp.settings/com.oculus.panelapp.quicksettings.QuickSettingsActivity`

The characteristic blocked launch log line was:

- `Launch is blocked because: a Guardian dialog is currently showing.`

The Guardian dialog that kept coming back was:

- `GUARDIAN_TRACKING_LOST_CONTINUE`

## Most Useful Inspection Commands

Use these first before assuming the device is just "awake" or "asleep":

```powershell
$device = '192.168.2.56:5555'

adb -s $device shell dumpsys activity activities |
  Select-String -Pattern 'ResumedActivity|mFocusedWindow|GuardianDialogActivity|FocusPlaceholderActivity|SensorLockActivity|ClearActivity|HomeActivity|QuickSettingsActivity'
```

```powershell
$device = '192.168.2.56:5555'
adb -s $device shell dumpsys vrpowermanager
```

```powershell
$device = '192.168.2.56:5555'
adb -s $device shell service list |
  Select-String -Pattern 'guardian|sensorlock|vrfocus|vrpowermanager|tracking'
```

Useful services observed on this machine:

- `guardian`
- `sensorlock`
- `vrfocus`
- `vrpowermanager`
- `tracking`

Selector note:

- For low-level tracing on 2026-03-28, the most reliable selector was the USB
  serial `3487C10H3M017Q`.
- `adb -s 3487C10H3M017Q ...` worked consistently over USB.
- `hzdb device list --format json` returned `[]` in that USB-only state, so for
  shell-level tracing use `adb` directly and do not assume `hzdb` will see the
  same transport path.

## Android Input Stack Visibility

The Quest shell-visible Linux input stack was much smaller than the effective
VR control surface:

```powershell
adb -s 3487C10H3M017Q shell getevent -lp
adb -s 3487C10H3M017Q shell dumpsys input
```

Observed input devices:

- `/dev/input/event0` `gpio-keys`
  - `KEY_VOLUMEUP`
  - `KEY_SWITCHVIDEOMODE`
- `/dev/input/event1` `pmic_pwrkey`
  - `KEY_POWER`
- `/dev/input/event2` `pmic_resin`
  - `KEY_VOLUMEDOWN`

Important implication:

- The Touch controllers did not appear as normal `/dev/input/event*` devices in
  the shell-visible input stack.
- This means the right-controller Meta / menu button is not currently exposed
  to `adb shell getevent` the way a normal Android keyboard or gamepad button
  would be.

Keylayout observations:

```powershell
adb -s 3487C10H3M017Q shell ls /system/usr/keylayout
adb -s 3487C10H3M017Q shell ls /odm/usr/keylayout
adb -s 3487C10H3M017Q shell cat /system/usr/keylayout/Generic.kl
```

- `/system/usr/keylayout/Generic.kl` is readable and confirms that Android
  defines generic injectable keycodes such as `HOME`, `MENU`, `BUTTON_MODE`,
  `GUIDE`, `SLEEP`, and `WAKEUP`.
- `/odm/usr/keylayout` contains Quest-specific files including:
  - `gpio-keys.kl`
  - `oculus-device.kl`
- Shell could list those ODM files but could not read them:
  - `cat: /odm/usr/keylayout/gpio-keys.kl: Permission denied`

Working conclusion:

- The Quest almost certainly has device-specific input mappings beyond the
  generic Android keylayout, but part of that mapping sits behind permissions
  not available to the shell user.
- For now, treat the controller Meta / system button as a Meta-managed path,
  not as a normal shell-visible Android input device.

## HZDB Command Mapping Observed On This Machine

`hzdb` is available and usable:

```powershell
& $hzdb --version
```

Observed command behavior:

- `hzdb device wake --device <serial>`
  - Sent a wake signal but did not clear the Guardian blocker by itself.
- `hzdb device proximity --disable --device <serial>`
  - Broadcast `com.oculus.vrpowermanager.prox_close`
  - From `STANDBY`, this transitioned `vrpowermanager` to `HEADSET_MOUNTED`
    and triggered `SCREEN_ON`.
- `hzdb device proximity --enable --device <serial>`
  - Broadcast `com.oculus.vrpowermanager.automation_disable`
  - This explicitly dismissed the Guardian dialog and drove
    `vrpowermanager` into `STANDBY`.

Important reliability note:

- Do not trust implicit device selection with `hzdb`.
- Pass `--device <serial>` explicitly.

## ADB Commands Tested

### Commands That Helped

```powershell
$device = '192.168.2.56:5555'
adb -s $device shell am broadcast -a com.oculus.vrpowermanager.automation_disable
```

Observed behavior:

- `GuardianDialogMgr` logged `explicitly dismissing dialog`
- `vrpowermanager` logged transition `HEADSET_MOUNTED -> WAITING_FOR_SLEEP_MSG -> STANDBY`

```powershell
$device = '192.168.2.56:5555'
adb -s $device shell am broadcast -a com.oculus.vrpowermanager.prox_close
```

Observed behavior:

- `vrpowermanager` logged transition `STANDBY -> HEADSET_MOUNTED`
- `SCREEN_ON` followed
- HorizonOS relaunched `SensorLockActivity`

### Commands That Did Not Solve It Alone

```powershell
adb -s $device shell input keyevent 26
adb -s $device shell input keyevent 223
adb -s $device shell input keyevent 224
adb -s $device shell input keyevent 3
& $hzdb device wake --device $device
```

Observed behavior:

- They changed wakefulness or brought `HomeActivity` forward, but did not
  reliably clear the Guardian tracking-loss blocker.

### Power / Sleep / Wake Comparison

From a mounted baseline, these three inputs did not behave the same:

```powershell
adb -s 3487C10H3M017Q shell input keyevent POWER
adb -s 3487C10H3M017Q shell input keyevent SLEEP
adb -s 3487C10H3M017Q shell input keyevent WAKEUP
```

Observed behavior:

| Command | Observed `vrpowermanager` effect | Observed shell effect |
| --- | --- | --- |
| `input keyevent POWER` | stayed `HEADSET_MOUNTED` in repeated snapshots | no reliable state change from the tested mounted baselines |
| `input keyevent SLEEP` | `HEADSET_MOUNTED -> STANDBY` | deterministic sleep / `SCREEN_OFF` behavior |
| `input keyevent WAKEUP` | `STANDBY -> WARM_UP -> HEADSET_MOUNTED` | deterministic wake leg, typically relaunching `SensorLockActivity` |

Practical rule:

- For controlled ADB experiments, `SLEEP` / `WAKEUP` are more informative and
  reproducible than raw `POWER`.
- Do not assume `input keyevent POWER` is equivalent to pressing the headset's
  physical power button in the way Meta's UX stack handles it.

## Injected High-Level Keycodes Versus Meta Button Semantics

The generic Android keycodes exist, but they do not obviously reproduce the
dedicated Quest Meta-button behavior:

```powershell
adb -s 3487C10H3M017Q shell input keyevent HOME
adb -s 3487C10H3M017Q shell input keyevent MENU
adb -s 3487C10H3M017Q shell input keyevent GUIDE
adb -s 3487C10H3M017Q shell input keyevent BUTTON_MODE
adb -s 3487C10H3M017Q shell input keyevent APP_SWITCH
```

Observed behavior from Guardian-blocked baselines:

- `HOME`
  - moved `mCurrentFocus` / `mFocusedApp` to `com.oculus.vrshell/.HomeActivity`
  - but `mTopFullscreenOpaqueWindowState` could still remain the Guardian
    dialog
- `BUTTON_MODE`
  - behaved similarly to `HOME` in the cleanest repeated run
- `GUIDE`
  - produced no visible state change in the clean repeated run
- `MENU` and `APP_SWITCH`
  - looked closer to `HOME` than to a dedicated Quest system button in the
    coarse pass, but they need cleaner repeats before treating that as fully
    stable

Source-tagged retry:

```powershell
adb -s 3487C10H3M017Q shell input keyboard keyevent GUIDE
adb -s 3487C10H3M017Q shell input gamepad keyevent GUIDE
adb -s 3487C10H3M017Q shell input keyboard keyevent BUTTON_MODE
adb -s 3487C10H3M017Q shell input gamepad keyevent BUTTON_MODE
```

Observed behavior:

- `GUIDE` stayed inert in both keyboard- and gamepad-source retries
- `BUTTON_MODE` still behaved like a route to `HomeActivity`, not like a
  dedicated Quest Meta-button press

Current conclusion:

- We do not currently have evidence that any shell-injected Android keycode
  reproduces the unremappable right-controller Meta / menu button semantics.
- The dedicated Quest system button path is probably handled above or beside
  the standard shell-visible Android input route.

## Manual Right-Controller Meta / Menu Button Probe

Manual probe date:

- 2026-03-28

Trace artifact:

- `artifacts/verify/controller-meta-button-trace-20260328-143403/`

Manual action:

- a human pressed the right-controller Meta / menu button three times while
  three observers were running:
  - `adb shell getevent -lt`
  - filtered `adb logcat -v time`
  - a `250 ms` poll of `dumpsys window` plus `dumpsys vrpowermanager`

Observed result:

- `getevent.txt`
  - remained empty
- `logcat.txt`
  - remained empty with the tested filter
- `window-focus.txt`
  - stayed stable for the entire capture
  - `mCurrentFocus=...HomeActivity`
  - `mTopFullscreenOpaqueWindowState=...FocusPlaceholderActivity`
  - `Virtual proximity state: CLOSE`
  - `State: HEADSET_MOUNTED`

Immediate post-trace input inspection:

```powershell
adb -s 3487C10H3M017Q shell dumpsys input | Select-String -Pattern 'RecentQueue:|KeyEvent' -Context 0,10
```

Observed result:

- `RecentQueue` contained six `KeyEvent` entries
- that is consistent with three taps being delivered as three down/up pairs
- the queue did not reveal the actual keycode, only that `KeyEvent`s existed

Interpretation:

- The physical right-controller Meta / menu button does appear to enter
  Android's higher-level input-dispatch path.
- It still does not appear as a normal shell-visible Linux input event in the
  accessible `/dev/input/event*` set.
- It also did not cause an observable shell-focus or `vrpowermanager` change in
  this Home / FocusPlaceholder baseline.
- So far, the strongest model is:
  - controller Meta-button press becomes an internally routed `KeyEvent`
  - but the underlying device path and semantic action are not exposed through
    the shell-level tools used here

What this does and does not prove:

- It does support the claim that the button is not "nothing"; the system really
  is receiving key events.
- It does not yet identify the keycode or prove equivalence to `HOME`,
  `BUTTON_MODE`, `GUIDE`, or any other injectable Android key.
- It does not yet show whether Meta consumes the event before ordinary shell
  surfaces can log or dispatch it further.

## Power Button Versus Controller Meta Button Comparison

Comparison probe date:

- 2026-03-28

Trace artifact:

- `artifacts/verify/hardware-vs-meta-button-trace-20260328-143829/`

Manual action:

- one headset hardware-button family was pressed twice
- the right-controller Meta / menu button was pressed twice

Observer setup:

- `adb shell getevent -lt`
- full `adb logcat -v time`
- a `350 ms` poll of:
  - `dumpsys window`
  - `dumpsys vrpowermanager`
  - `dumpsys input` `RecentQueue`

Important result:

- `getevent.txt`
  - captured both button families
  - headset hardware button:
    - `/dev/input/event1`
    - `KEY_POWER` down / up pairs
  - right-controller Meta / menu button:
    - `/dev/input/event3`
    - device name `Device 0xD2A88F3474ADDF66`
    - `KEY_FORWARD` down / up pairs
- `logcat-full.txt`
  - captured the power-button route clearly
  - included `SideFpsEventHandler: notifyPowerPressed`
  - included `PowerManagerService: Going to sleep due to power_button`
  - included `PowerManagerService: Waking up from Asleep (reason=WAKE_REASON_POWER_BUTTON, details=android.policy:POWER)`
- the most useful combined read came from:
  - `getevent.txt`
  - the polled `RecentQueue` plus `vrpowermanager` / focus state
  - the power-manager lines in `logcat-full.txt`

Observed split:

1. Power-button family
   - `getevent.txt` showed two `KEY_POWER` down / up pairs on
     `/dev/input/event1`
   - at `2026-03-28T14:38:53.3100924+01:00`, two fresh `KeyEvent`s appeared
     with ages `338 ms` and `171 ms`
   - by `2026-03-28T14:38:53.8103598+01:00`, `vrpowermanager` had moved to
     `STANDBY` and shell focus was null
   - at `2026-03-28T14:38:55.7131775+01:00`, another fresh `KeyEvent` pair
     appeared with ages `387 ms` and `162 ms`
   - by that same poll, the headset was back in `HEADSET_MOUNTED` with
     `HomeActivity` focused again
   - `logcat-full.txt` matched the same sequence with explicit sleep / wake
     lines from the power manager

2. Controller Meta / menu family
   - `getevent.txt` showed two `KEY_FORWARD` down / up pairs on
     `/dev/input/event3`
   - around `2026-03-28T14:38:58.0981404+01:00`, two fresh
     `TouchModeEvent(inTouchMode=true)` entries appeared with ages
     `470 ms` and `393 ms`
   - no corresponding `STANDBY` / wake transition occurred
   - `HomeActivity`, `FocusPlaceholderActivity`, and `HEADSET_MOUNTED` all
     stayed stable
   - around `2026-03-28T14:39:02.9598341+01:00` and
     `2026-03-28T14:39:03.4625119+01:00`, another fresh event cluster appeared:
     - one very fresh `KeyEvent` at `47 ms`
     - then `KeyEvent`s at `622 ms` and `382 ms`
     - plus a `TouchModeEvent(inTouchMode=true)` at `25 ms`
   - again, no shell-focus or `vrpowermanager` state change followed

Working interpretation:

- The headset power-button path is easy to distinguish:
  - it produces fresh dispatcher `KeyEvent`s
  - and it changes `vrpowermanager` state in the expected sleep / wake pattern
- The right-controller Meta / menu path looks different:
  - it does surface at the shell-visible input layer in this later probe
  - the best current low-level mapping is:
    - physical right-controller Meta / menu button
    - `/dev/input/event3`
    - `KEY_FORWARD`
  - it can also produce fresh higher-level dispatcher activity
  - that activity can include `TouchModeEvent`s and sometimes `KeyEvent`s
  - and it does not force the shell / wake-state transitions that the power
    button does from this Home baseline

What remains unresolved:

- whether the `KEY_FORWARD` mapping is stable across firmware / OS / focus
  states
- whether `KEY_FORWARD` is the real end-to-end semantic route or just the best
  current shell-visible label for a higher-level Quest-specific button path
- the queue snapshots are partial and age-based, so they show event timing but
  not full semantics
- the exact mapping from physical controller Meta / menu press to Android /
  HorizonOS command semantics above that low-level event is still unknown

### Promising But Permission-Blocked

The lockscreen package exposes:

- `oculus.lockscreen.action.UNLOCK`

But shell cannot broadcast it directly:

- `Permission Denial: not allowed to send broadcast oculus.lockscreen.action.UNLOCK`

The settings package exposes:

- `horizonos.appactions.settings.UPDATE_TRAVEL_MODE`

But shell cannot call it directly either:

- `Requires permission horizonos.permission.APPACTIONS_QUERY_AND_EXECUTE`

## Injected Power / Sleep / Wake Trace

Trace artifact:

- `artifacts/verify/injected-key-trace-20260328-145008/`

Injected sequence:

```powershell
adb -s 3487C10H3M017Q shell input keyevent POWER
adb -s 3487C10H3M017Q shell input keyevent SLEEP
adb -s 3487C10H3M017Q shell input keyevent WAKEUP
adb -s 3487C10H3M017Q shell input keyevent FORWARD
adb -s 3487C10H3M017Q shell input keyevent BUTTON_MODE
adb -s 3487C10H3M017Q shell input keyevent GUIDE
adb -s 3487C10H3M017Q shell input keyevent HOME
```

Observed result:

- `getevent.txt`
  - showed only device-add metadata
  - did not show any of the injected key events
- the injected keys therefore entered above the Linux `/dev/input/event*` layer

Power-family split:

- injected `POWER`
  - did hit the real power-manager route in this run
  - `logcat-full.txt` contained:
    - `SideFpsEventHandler: notifyPowerPressed`
    - `PowerManagerService: Going to sleep due to power_button`
  - `state-poll.txt` moved from `HEADSET_MOUNTED` to `STANDBY`
- injected `SLEEP`
  - while already in `STANDBY`, it kept the device in `STANDBY`
  - the useful log line was:
    - `sleepRelease() calling goToSleep(GO_TO_SLEEP_REASON_SLEEP_BUTTON)`
- injected `WAKEUP`
  - woke the device back to `HEADSET_MOUNTED`
  - brought `SensorLockActivity` back into the visible window stack

Working interpretation:

- injected `POWER` is not reliably "nothing"; in this baseline it reached the
  same power-manager path as the physical headset button
- earlier runs still showed `POWER` being inert from other mounted baselines, so
  treat injected `POWER` as state-dependent rather than deterministic
- `SLEEP` / `WAKEUP` remain the cleaner primitives for controlled automation
  because their effect stays easier to reason about

## Injected Menu-Key Comparison

Trace artifact:

- `artifacts/verify/injected-menu-key-trace-20260328-145216/`

Recovered baseline before capture:

```powershell
adb -s 3487C10H3M017Q shell am broadcast -a com.oculus.vrpowermanager.automation_disable
Start-Sleep -Milliseconds 500
adb -s 3487C10H3M017Q shell am broadcast -a com.oculus.vrpowermanager.prox_close
Start-Sleep -Milliseconds 800
adb -s 3487C10H3M017Q shell input keyevent HOME
```

The shell returned to `HEADSET_MOUNTED` with `HomeActivity`, but
`SensorLockActivity` was still present and Guardian could re-enter the stack.

Injected sequence:

```powershell
adb -s 3487C10H3M017Q shell input keyevent FORWARD
adb -s 3487C10H3M017Q shell input keyevent BUTTON_MODE
adb -s 3487C10H3M017Q shell input keyevent GUIDE
adb -s 3487C10H3M017Q shell input keyevent HOME
adb -s 3487C10H3M017Q shell input keyevent MENU
```

Observed result:

- `getevent.txt`
  - again stayed limited to device-add metadata
  - none of the injected keys surfaced at the Linux input-device layer
- every injected key still created fresh dispatcher `KeyEvent` pairs in
  `dumpsys input` `RecentQueue`

Per-key behavior:

- `FORWARD`
  - produced a fresh `KeyEvent` pair
  - `logcat-full.txt` showed an explicit `android.intent.category.HOME` launch
    of `com.oculus.vrshell/.HomeActivity`
  - it did not dismiss `GuardianDialogActivity`
- `BUTTON_MODE`
  - produced a fresh `KeyEvent` pair
  - by the next state polls, `HomeActivity` had moved to `Window #0`
  - no separate explicit `HomeActivity` launch line was observed in this run
- `GUIDE`
  - produced another explicit `android.intent.category.HOME` launch of
    `com.oculus.vrshell/.HomeActivity`
  - it still did not dismiss `GuardianDialogActivity`
- `HOME`
  - produced a fresh `KeyEvent` pair
  - once `HomeActivity` was already at `Window #0`, no additional distinct
    transition stood out
- `MENU`
  - produced a fresh `KeyEvent` pair
  - no distinct shell or focus transition stood out in the captured baseline

Working interpretation:

- `FORWARD`, `BUTTON_MODE`, and `GUIDE` are better thought of as
  "bring / relaunch Home" candidates than as true emulations of the
  right-controller Meta / system button
- `MENU` is not a useful stand-in for the Quest Meta / menu route in this shell
  state
- none of the injected menu-like keys closed `GuardianDialogActivity` or
  `SensorLockActivity`
- so far, injected keys can help detect or provoke Home-routing behavior, but
  they are not a reliable "close accidental Meta menu / close system dialog"
  primitive on their own

## Current Best Recovery Sequence

This was the first sequence that actually launched the Sussex Unity runtime:

```powershell
$device = '192.168.2.56:5555'

adb -s $device shell am broadcast -a com.oculus.vrpowermanager.automation_disable
Start-Sleep -Milliseconds 500

adb -s $device shell am broadcast -a com.oculus.vrpowermanager.prox_close
Start-Sleep -Milliseconds 500

adb -s $device shell monkey -p com.Viscereality.LslTwin -c android.intent.category.LAUNCHER 1
```

What happened:

- `automation_disable` dismissed the active Guardian dialog
- `prox_close` brought the device back from `STANDBY` to `HEADSET_MOUNTED`
- launching immediately after that started
  `com.Viscereality.LslTwin/com.unity3d.player.UnityPlayerGameActivity`

Why the timing matters:

- after a few seconds, Guardian can request
  `GUARDIAN_TRACKING_LOST_CONTINUE` again
- launching quickly can beat that re-request window long enough for the Unity
  runtime to start

This is a workaround, not a complete semantic fix:

- it reproduces a useful launch path
- it does not yet positively "press Continue" on the Guardian dialog

## Expected State Transitions

The recovery path that matched the logs most closely was:

1. Blocked shell:
   - Guardian dialog foreground
2. `automation_disable`
   - Guardian dismissed
   - `vrpowermanager` moves to `STANDBY`
3. `prox_close`
   - `vrpowermanager` moves to `HEADSET_MOUNTED`
   - `SCREEN_ON`
   - `SensorLockActivity` is relaunched
4. immediate app launch
   - Unity activity can start before Guardian re-requests the tracking-loss
     dialog

## Known Caveats

- `SensorLockActivity` can still appear during the wake leg.
- `FocusPlaceholderActivity` often appears while the shell is juggling focus.
- `ClearActivity` can temporarily become the foreground app during the same
  transition.
- A clean `status` snapshot immediately after the sequence does not guarantee
  the Guardian dialog will not return a few seconds later.

## Screenshot-confirmed "Finding position in room" limbo

New probes on 2026-03-28 separated the visible Guardian layer from the deeper
SensorLock layer. The relevant screenshots and command outputs are under:

- `artifacts/verify/guardian-live-probe-20260328-150705/`
- `artifacts/verify/guardian-recovery-command-probe-20260328-150729/`
- `artifacts/verify/guardian-dialog-tap-probe-20260328-150817/`
- `artifacts/verify/guardian-key-nav-probe-20260328-150925/`
- `artifacts/verify/guardian-force-stop-probe-20260328-150954/`
- `artifacts/verify/sensorlock-vrpower-probe-20260328-151100/`
- `artifacts/verify/sensorlock-unlock-broadcast-probe-20260328-151230/`
- `artifacts/verify/sensorlock-dismiss-keyguard-probe-20260328-151331/`

What the screenshots proved:

- the "cannot find position in room" lock is a real
  `GuardianDialogActivity` dialog over a black `SensorLockActivity` backdrop
- the visible dialog text is:
  - title: `Finding position in room`
  - primary button: `Continue without tracking`
  - secondary button: `Turn on travel mode`
- `dumpsys vrpowermanager` can still say `HEADSET_MOUNTED` while the display is
  visually blocked, so screenshots are mandatory for this state

How the limbo was reproduced:

- `input keyevent SLEEP`
- `input keyevent WAKEUP`
- result: screenshot-confirmed black `SensorLockActivity`
- `input keyevent HOME`
- result: the Guardian dialog becomes visibly readable on top of the black
  background

What did not work from plain adb shell:

- low-level power emulation:
  - `sendevent /dev/input/event1 ... KEY_POWER ...`
  - result: `Permission denied`
  - implication: the shell user cannot replay the true headset power button at
    the Linux input layer
- dialog interaction:
  - `input tap` on the screenshot-derived primary-button centers
  - `input keyevent DPAD_CENTER`
  - `input keyevent ENTER`
  - `input keyevent TAB`
  - `input keyevent DPAD_DOWN`
  - `input keyevent BUTTON_A`
  - `input keyevent SPACE`
  - result: no screenshot-confirmed change
  - logs from the same sessions include `Input event injection from pid ... failed`
- shell keyguard dismissal:
  - `wm dismiss-keyguard`
  - result: no screenshot-confirmed change

What partially worked:

- `am force-stop com.oculus.guardian`
  - result: `GuardianDialogActivity` disappears
  - screenshot after the force-stop is still black
  - `SensorLockActivity` remains on top

What still did not recover the black SensorLock state:

- `am force-stop com.oculus.os.vrlockscreen`
- `am start -a android.intent.action.MAIN -c android.intent.category.HOME -n com.oculus.vrshell/.HomeActivity`
- `am broadcast -a com.oculus.vrpowermanager.automation_disable`
- `am broadcast -a com.oculus.vrpowermanager.prox_open`
- `am broadcast -a com.oculus.vrpowermanager.prox_close`
- `am broadcast -a com.oculus.vrpowermanager.automation_enable`

Important automation surface discovered in `com.oculus.os.vrlockscreen`:

- exported receiver action:
  - `oculus.lockscreen.action.UNLOCK`
  - receiver: `com.oculus.os.vrlockscreen/.KeyguardAutomationReceiver`
- shell result:
  - `SecurityException: Permission Denial: not allowed to send broadcast`
- implication:
  - Meta does expose a lockscreen automation path, but it is permission-gated
    away from the ordinary adb shell user

Additional service surfaces discovered on-device:

- `sensorlock: [vros.os.sensorlock.ISensorLock]`
- `guardian: [internal.horizonos.os.guardian.IGuardianManagerService]`
- `LockscreenService: [oculus.internal.ILockscreen]`
- `vros_lockscreen: [dev.vros.internal.os.lockscreen.ILockscreenService]`
- `oculus.internal.power.IVrPowerManager/default`

Current working conclusion:

- from rootless adb shell, we can:
  - trigger this limbo
  - prove which visual layer is active with screenshots
  - dismiss the Guardian dialog layer by force-stopping Guardian
- from rootless adb shell, we cannot yet:
  - reproduce the real physical power-button recovery path
  - accept `Continue without tracking`
  - clear the remaining black `SensorLockActivity`

Until a shell-allowed binder or privileged broadcast path is found, the
physical headset power button still has a recovery capability that plain adb
shell does not.

## Metacam-verified return-to-Home probe

The screenshots and command outputs for this pass are under:

- `artifacts/verify/return-home-probe-20260328-161707/`

New screenshot rule from this probe:

- for Quest shell / Guardian / Home-world debugging on this device, prefer
  `hzdb capture screenshot --method metacam` over plain
  `adb exec-out screencap -p`
- on the current Sussex public-companion path, the same `hzdb` metacam command
  produced stale repeated frames against the USB serial while the active Wi-Fi
  ADB selector returned fresh changing images, so the companion now prefers the
  live Wi-Fi endpoint for screenshot capture and falls back to USB only when
  Wi-Fi is unavailable
- in this session, direct `screencap` often showed only the black background
  plus the performance HUD while `metacam` still captured the real
  `GuardianDialogActivity` surface
- example command:

```powershell
& $hzdb capture screenshot `
  --device 3487C10H3M017Q `
  --method metacam `
  --output C:\path\to\shot.png
```

What the metacam-confirmed probe showed:

- restarting `vrshell` can re-surface the live Guardian UI even after the
  shell has fallen back to black-only captures
- in this run, the recovered visible blocker was the second dialog:
  - title: `Finding position in room`
  - primary button: `Continue`
- `uiautomator dump` still exposed that dialog tree while `metacam` confirmed
  it visually

What did not get back to visible Home without physical buttons once that second
dialog was on screen:

- `am broadcast -a com.oculus.vrpowermanager.automation_disable`
- `am broadcast -a com.oculus.vrpowermanager.prox_close`
- `am start -a android.intent.action.MAIN -c android.intent.category.HOME`
- `am force-stop com.oculus.guardian`
- relaunching shell-owned surfaces after the Guardian force-stop:
  - `com.oculus.vrshell/.HomeActivity`
  - `com.oculus.igvr/.IgvrHomeWorldCanvasActivity`
  - `com.oculus.explore/.ExploreActivity`
- injected power-family keys from the live visible dialog:
  - `input keyevent POWER`
  - `input keyevent SLEEP`
  - `input keyevent WAKEUP`
- injected menu-family keys from the live visible dialog:
  - `input keyevent FORWARD`
  - `input keyevent BUTTON_MODE`
  - `input keyevent GUIDE`
- exact-button interaction attempts using the `uiautomator` bounds for
  `Continue`:
  - `input touchscreen tap 250 529`
  - `input mouse tap 250 529`
  - `input touchscreen motionevent DOWN/UP 250 529`
- focus-driven button activation retries:
  - `input keyevent TAB`
  - `input gamepad keyevent BUTTON_A`
  - `input dpad keyevent DPAD_CENTER`

Additional shell-structure finding:

- `am task lock <TASK_ID>` can pin Home underneath the blocker but does not
  clear the blocker itself
- in this run, locking the current Home task moved the shell into
  `mLockTaskModeState=PINNED` while the visible metacam capture still showed
  the same Guardian `Continue` dialog
- removing small shell overlay tasks such as `ToastsActivity`,
  `AnytimeUIActivity`, and `FocusPlaceholderActivity` also did not restore
  visible Home; in one repeat, stripping those layers simply let the Guardian
  dialog reappear visibly again

Current working conclusion from the metacam pass:

- we now have a reliable screenshot-backed way to tell whether the headset is
  actually still on the Guardian blocker even when `adb screencap` suggests a
  featureless black frame
- from a rootless shell on this build, we still do not have a command-only
  path that reliably accepts the second Guardian `Continue` dialog or restores
  visible Home the way the physical headset power button sometimes does

## Returning To An Already-Running APK Without Relaunch

The screenshots and state captures for this pass are under:

- `artifacts/verify/sensorlock-return-probe-20260328-151714/`

What was verified in that probe:

- baseline:
  - `00-app-after-8s.png` and `01-app-after-20s.png` show the Sussex Unity app
    visibly running
  - the same captures show
    `com.Viscereality.LslTwin/com.unity3d.player.UnityPlayerGameActivity`
    owning focus
- black shell / hidden-app control:
  - `input keyevent HOME`
  - screenshots `17-control-after-home-2s.png`,
    `18-control-after-home-10s.png`, and
    `19-control-after-home-22s.png` stay black for at least 22 seconds
  - the app task is still alive in recents as task `5997`
  - `HomeActivity` owns `mCurrentFocus`
  - `SensorLockActivity` is still present in the task stack behind that black
    state
- stricter SensorLock-focused black state:
  - `input keyevent HOME`
  - `input keyevent SLEEP`
  - `input keyevent WAKEUP`
  - screenshots `21-black-sensorlock-after-home-sleep-wakeup-2s.png` and
    `22-black-sensorlock-after-home-sleep-wakeup-10s.png` stay black
  - `21-black-sensorlock-after-home-sleep-wakeup-2s-activities.txt` shows:
    - `ResumedActivity=...SensorLockActivity`
    - `mCurrentFocus=...SensorLockActivity`
    - `mTopFullscreenOpaqueWindowState=...SensorLockActivity`
  - the Sussex task `5997` is still present in recents and still has its Unity
    activity record
- task-level return without relaunch:
  - `am task lock 5997`
  - shell output in `23-task-lock-from-sensorlock-command.txt`:
    `Activity manager is in lockTaskMode`
  - screenshots `23-after-task-lock-from-sensorlock-2s.png` and
    `24-after-task-lock-from-sensorlock-10s.png` show the already-running
    Unity app back on screen
  - `23-after-task-lock-from-sensorlock-2s-activities.txt` shows:
    - `ResumedActivity=...UnityPlayerGameActivity`
    - `mCurrentFocus=...UnityPlayerGameActivity`
    - `mTopFullscreenOpaqueWindowState=...UnityPlayerGameActivity`
  - no fresh `monkey`, `am start`, or other app-launch intent was issued for
    that recovery
  - after inspection, `am task lock stop` cleanly exits lock-task mode again

Working implication:

- if the Sussex APK is already running and you know its live task ID, shell can
  recover the visible app from a screenshot-confirmed black Home / SensorLock
  state without relaunching the APK by bringing that task to the front with
  `am task lock <TASK_ID>`
- the task ID is session-specific; on this device it was `5997` during the
  captured run and was discoverable via `dumpsys activity recents`

## Lock Task Versus Physical Meta Button

The relevant screenshots and trace outputs for this pass are under:

- `artifacts/verify/locktask-menu-probe-20260328-153109/`
- `artifacts/verify/physical-meta-locktask-trace-20260328-154003/`

What `am task lock <TASK_ID>` meant in the tested Sussex run:

- shell output:
  - `Activity manager is in lockTaskMode`
- visible effect:
  - the Sussex Unity app stayed visibly in front
- shell state:
  - `11-after-correct-lock-activities.txt` shows
    `UnityPlayerGameActivity` as:
    - `ResumedActivity`
    - `mCurrentFocus`
    - `mTopFullscreenOpaqueWindowState`
  - `HomeActivity` and `SensorLockActivity` still remain elsewhere in the
    stack, so lock-task mode does not mean those shell activities disappear

What happened to menu-like injected keys while lock-task mode was active:

- tested keys:
  - `FORWARD`
  - `BUTTON_MODE`
  - `GUIDE`
  - `HOME`
- screenshot-confirmed behavior:
  - `FORWARD`, `BUTTON_MODE`, and `GUIDE` left the Unity scene visibly in
    front in:
    - `12-after-correct-lock-forward.png`
    - `12-after-correct-lock-button_mode.png`
    - `12-after-correct-lock-guide.png`
  - the matching activity dumps still show Unity owning focus and the top
    fullscreen opaque window
  - `HOME` still escaped the lock-task foreground and produced a black shell
    view in `12-after-correct-lock-home.png`

What happened to the physical controller Meta / menu button while lock-task
mode was active:

- baseline locked screenshot before the physical presses:
  - `physical-meta-locktask-trace-20260328-154003/00-ready-baseline.png`
- screenshot after the physical presses:
  - `physical-meta-locktask-trace-20260328-154003/01-after-physical-meta.png`
- screenshot after exiting lock-task mode:
  - `physical-meta-locktask-trace-20260328-154003/02-after-lock-stop.png`
- visual result:
  - the headset stayed on the Unity scene during the physical Meta-button
    presses
- low-level input result:
  - `getevent-event3.txt` still recorded repeated
    `EV_KEY KEY_FORWARD DOWN/UP` pairs
  - this confirms the controller Meta / menu button was still being pressed and
    observed at the low-level input layer
- shell-state result:
  - `state-poll.txt` kept showing Unity as:
    - `ResumedActivity`
    - `mCurrentFocus`
    - `mTopFullscreenOpaqueWindowState`
  - no screenshot-confirmed transition to Home, Guardian, or a visible system
    menu occurred during the locked run

Current conclusion:

- lock-task mode on this HorizonOS build is not a perfect global kiosk mode,
  because injected `HOME` can still break out
- but it is the first screenshot-confirmed state found so far where the
  physical controller Meta / menu button can still generate its normal
  `KEY_FORWARD` signal without producing a visible foreground change in the
  Sussex Unity app
- that makes lock-task mode a strong candidate for "keep the app visually in
  front and neutralize accidental Meta-menu presses" as long as the remaining
  `HOME` escape path is acceptable or separately handled

Unlocked comparison after leaving lock-task mode:

- dedicated trace:
  - `physical-meta-unlocked-after-lock-trace-20260328-160100/`
- transition setup:
  - `01-locktask-state-after.txt` shows `mLockTaskModeState=PINNED`
  - `02-locktask-state-after-stop.txt` shows `mLockTaskModeState=NONE`
  - `02-after-lock-stop.png` still shows the Sussex Unity app visibly in front
- physical input result after leaving lock-task mode:
  - `04-getevent-event3.txt` recorded repeated
    `EV_KEY KEY_FORWARD DOWN/UP` pairs during the user presses
- screenshot-confirmed visible result:
  - `03-ready-unlocked.png` is the unlocked baseline
  - `05-after-physical-meta.png` still shows the Unity scene
  - the user reported hearing the usual Meta-menu sound cue but never seeing a
    visual menu popup or close animation
- shell / log result:
  - `05-after-physical-meta-activities.txt` captured
    `FocusPlaceholderActivity` as:
    - `ResumedActivity`
    - `mCurrentFocus`
    - `mTopFullscreenOpaqueWindowState`
  - `04-logcat.txt` shows transient `FocusPlaceholderActivity` launches at
    `15:53:09` and `15:53:30`
  - the same log also shows `FocusPlaceholderActivity: Finishing activity` and
    focus returning to `UnityPlayerGameActivity`

Interpretation of the unlocked comparison:

- outside lock-task mode, the physical controller Meta / menu button can still
  trigger shell-level `FocusPlaceholderActivity` focus churn and the familiar
  sound cue
- however, in this run it still did not produce a screenshot-confirmed visible
  system menu
- this means "Meta button had an effect" and "user actually saw the system
  menu" are not the same event on this build
- compared with the locked run, the unlocked run is weaker as a protection
  state because the shell was allowed to steal focus transiently even though the
  headset never showed a visible menu in the captured screenshots
- for experiment control, treat lock-task mode as the stronger "visually
  neutralize accidental Meta presses" state, and treat transient
  `FocusPlaceholderActivity` bursts plus `KEY_FORWARD` events as the current
  best signal that the Meta route was attempted without a visible shell escape

## Confirmed Sussex Kiosk Workflow

The relevant confirmation artifacts for the final successful Sussex operator run
are under:

- `artifacts/verify/recreate-locktask-state-20260328-173806/`
- `artifacts/verify/return-home-remaining-20260328-174319/`

This is the first full operator-confirmed kiosk workflow from this machine that
met both practical experiment requirements:

- while Sussex was active, the controller Meta / menu button had no visible
  effect and the operator reported no menu sound
- after the exit sequence, the headset returned cleanly to Meta Home and the
  controller Meta / menu button worked normally again

Confirmed kiosk entry shape:

- launch the Sussex APK normally first
- resolve the current Sussex task id from `dumpsys activity recents`
- pin that live task with `am task lock <TASK_ID>`
- treat kiosk entry as confirmed only when the activity dump shows all of:
  - `mLockTaskModeState=PINNED`
  - `ResumedActivity=...UnityPlayerGameActivity`
  - `mCurrentFocus=...UnityPlayerGameActivity`
  - `mTopFullscreenOpaqueWindowState=...UnityPlayerGameActivity`

Confirmed kiosk exit stack:

```powershell
adb -s $device shell am broadcast -a com.oculus.vrpowermanager.automation_disable
adb -s $device shell am task lock stop
adb -s $device shell am start -W -a android.intent.action.MAIN -c android.intent.category.HOME -n com.oculus.vrshell/.HomeActivity
adb -s $device shell am force-stop com.Viscereality.LslTwin
```

Confirmed kiosk exit success shape:

- `mLockTaskModeState=NONE`
- `mCurrentFocus=...HomeActivity`
- the Sussex package is force-stopped
- the top opaque shell window may be `HomeActivity` directly or a Home-side UX
  layer such as `com.oculus.systemux/.VirtualObjectsActivity`
- operator-visible result: Meta Home is usable again and the controller Meta /
  menu button regains its normal behavior

Implementation note for `ViscerealityCompanion`:

- the Sussex study shell now treats this as its default runtime behavior

March 28 refinement:

- the exit path above remains the strongest confirmed operator workflow on this
  machine
- fresh kiosk entry from a clean Home baseline is not yet equally confirmed
- later unattended reruns showed two distinct entry failure modes:
  - `artifacts/verify/manual-kiosk-cycle-20260328-193427/`
    - normal launch from clean Home resumed Unity visibly
    - `am task lock <TASK_ID>` did not stick
    - the activity dump stayed at `mLockTaskModeState=NONE`
    - operator result matched that weaker shell state: app visible, controller
      Meta/menu still usable, clean Home exit
  - `artifacts/verify/proxclose-kiosk-entry-test-20260328-194029/`
    - a timed `hzdb device proximity --disable --duration-ms 28800000` did make
      task lock stick again
    - but the pinned foreground became
      `GuardianDialogActivity` / `SensorLockActivity`, not the Unity activity
    - shell state after launch+lock:
      - `mLockTaskModeState=PINNED`
      - `ResumedActivity=...GuardianDialogActivity`
      - `mCurrentFocus=...GuardianDialogActivity`
      - `mTopFullscreenOpaqueWindowState=...GuardianDialogActivity`
  - `artifacts/verify/home-auto-prox-launch-lock-20260328-194209/`
    - `HOME -> HomeActivity -> automation_disable -> timed prox_close ->
      immediate launch -> task lock` reproduced the same pinned-Guardian result
- working implication:
  - the earlier operator-confirmed success should currently be treated as
    "confirmed kiosk exit from a good pinned runtime state", not as proof that
    clean Home -> launch -> pin is already deterministic
  - timed virtual-close / `prox_close` looks like a real prerequisite for the
    pin to stick on this HorizonOS build
  - but unattended proximity-hold entry can pin the Guardian / SensorLock shell
    instead of the Unity task
  - the next required validation is a coordinated wearer-on entry run:
    keep the headset on, establish the timed proximity hold, launch
    immediately, pin immediately, and confirm whether the Unity app rather than
    Guardian owns:
    - `ResumedActivity`
    - `mCurrentFocus`
    - `mTopFullscreenOpaqueWindowState`
- the launch/stop toggle is expected to map to "launch in kiosk mode" and
  "exit kiosk mode" instead of a plain foreground app launch/force-stop cycle
- do not replace this with build-time Unity changes or with a different
  shell-side ritual unless a later trace proves the new path is more robust
- the Sussex shell now also exposes a `Capture Quest Screenshot` bench-tool
  action that calls `hzdb capture screenshot --method metacam` and keeps a
  visual-confirmation warning active until the operator captures a fresh image

## April 2026 HorizonOS Update

The March 2026 kiosk findings below are now historical only. After the Meta /
HorizonOS update observed on this machine by April 12, 2026:

- Sussex can still launch and exit correctly while the controller Meta / menu
  button remains active
- kiosk must no longer be described as a reliable Meta / menu-button lockout
- launch should be blocked while the headset reports asleep, with the operator
  told `Wake the headset to enable launching`
- launch should also stay blocked while Guardian or other Meta visual blockers
  are active
- the public GUI no longer exposes remote headset wake/sleep controls for
  Sussex; manual headset wake/sleep is the only supported operator path for now
- screenshot and visible-scene confirmation remain the real success criteria

## March 28 GUI Validation Update

Fresh validation against the patched public companion app confirmed two
separate things:

- the new GUI behavior is correct: kiosk launch no longer hides the wake/launch
  trace behind a silent pre-block, and the Sussex shell now reports the full
  recovery attempt plus a `Quest visual confirmation pending` warning when
  shell-level confirmation is insufficient
- the remaining failure is still device-side: on the current HorizonOS build,
  the headset can stay black or Guardian-blocked even after the companion
  reports `HomeActivity`, `FocusPlaceholderActivity`, or a recent Sussex task
  id

The GUI-triggered screenshot evidence from the patched app:

- `quest_20260328_181253.png` showed a black visible scene after a kiosk-launch
  attempt even though the shell had already walked through
  `WAKEUP -> HOME -> HomeActivity -> automation_disable -> prox_close ->
  launch -> task-lock probe`
- the Sussex shell text correctly preserved the full trace and ended in
  `Launch completed ..., but kiosk mode was not fully confirmed`
- this means the current public app behavior should be treated as:
  shell-confirmed and screenshot-verified, not shell-confirmed-only

## March 28 Confirmed GUI Kiosk Cycle

After tightening the Sussex task-id parser and narrowing the wake classifier so
that a normal Home-owned `FocusPlaceholderActivity` companion layer is no
longer misclassified as wake limbo, the public Sussex shell completed a clean
operator-confirmed GUI cycle from visible Home:

- starting point: usable Meta Home, with the controller Meta/menu button
  working normally
- GUI action: `Launch Kiosk Runtime`
- visible result: the Sussex APK opened cleanly
- controller result while the APK was active:
  - the controller Meta/menu button was visually disabled
  - no menu sound played
  - the app stayed in front
- GUI action: `Exit Kiosk Runtime`
- visible result: the headset returned cleanly to Meta Home
- controller result after exit:
  - the controller Meta/menu button worked normally again

This is the currently confirmed operator workflow for the Sussex shell:

1. start from visible Meta Home
2. launch through the Sussex GUI runtime toggle
3. treat the app as in kiosk mode only after operator-side visual confirmation
4. exit through the same GUI toggle
5. confirm return to Home visually

Important remaining limitation:

- if the headset is already stuck in the black `SensorLockActivity` limbo
  family before the operator starts the Sussex GUI cycle, rootless shell still
  does not reliably recover it on this machine
- in those already-blocked cases, a physical headset power-button press can
  still be required to restore visible Home before the GUI workflow should be
  retried

## March 28 Physical Home Recovery Trace

Artifact bundle:

- `artifacts/verify/physical-home-recovery-trace-20260328-192059/`

Operator-confirmed sequence:

- wearing the headset was not enough to recover a usable Home view
- pressing the physical headset power button once brought the headset into the
  main menu
- after that, the controller Meta/menu button could open and close the menu
  normally again

Trace-confirmed downstream path:

- `getevent.txt` captured a real `/dev/input/event1 KEY_POWER DOWN/UP`
- `logcat-full.txt` recorded `SideFpsEventHandler: notifyPowerPressed`
- immediately after that:
  - `SensorLockActivity` closed
  - tracking resumed
  - `GuardianDialogActivity` was explicitly dismissed and closed
  - Home-side overlays came back, including
    `com.oculus.systemux/com.oculus.panelapp.virtualobjects.VirtualObjectsActivity`
- `state-poll.txt` then showed:
  - `ResumedActivity=HomeActivity`
  - `mCurrentFocus=HomeActivity`
  - `mTopFullscreenOpaqueWindowState=FocusPlaceholderActivity`
  - `mLockTaskModeState=NONE`

Practical interpretation:

- the physical headset power button is now confirmed as a privileged recovery
  path for the blocked tracking / Guardian-to-Home transition on this build
- the controller Meta/menu button regained its normal Home/menu behavior only
  after that recovery had already happened
- this trace still does not show the controller Meta/menu button as a normal
  low-level `/dev/input` event source in the same way as the physical power
  button

## March 28 Post-Power Passthrough Follow-Up

Artifact bundle:

- `artifacts/verify/sussex-study-mode-live/`

Observed follow-up after the later live Sussex kiosk run on 2026-03-28:

- after GUI-driven kiosk exit, the headset was still stuck in the black
  `SensorLockActivity` family until a physical headset power-button press
- after that press, shell state improved to:
  - `ResumedActivity=HomeActivity`
  - `mCurrentFocus=HomeActivity`
  - initially `mTopFullscreenOpaqueWindowState=FocusPlaceholderActivity`
  - later `mTopFullscreenOpaqueWindowState=ControlBarActivity`
- however the matching metacam captures still showed passthrough-only room
  imagery instead of a visible Meta menu:
  - `debug-after-user-power-press.png`
  - `debug-after-controlbar-launch.png`
  - `debug-after-button-mode.png`
  - `debug-after-guide.png`
  - `debug-after-physical-controller-button.png`

Extra attempts that still did not surface a visible menu in metacam:

- explicitly relaunching
  `com.oculus.vrshell/com.oculus.panelapp.controlbar.ControlBarActivity`
- injected menu-style keys from the recovered Home-side shell:
  - `input keyevent BUTTON_MODE`
  - `input keyevent GUIDE`
- pressing the physical controller Meta/menu button

Updated interpretation:

- do not treat `HomeActivity`, `FocusPlaceholderActivity`, or even
  `ControlBarActivity` ownership alone as proof that the operator is back at a
  visible Meta menu
- on this machine, screenshot confirmation is still mandatory after kiosk exit
  even when the shell-side activity stack looks "home-like"
- the current build can still degrade into a passthrough limbo where Home-side
  activities exist, but no screenshot-confirmed Meta menu is visible
- one confirmed contributor to the failing unattended harness run was that the
  harness had been arming the 8h proximity hold before launch even though its
  helper name implied the opposite
- that meant the failing harness pass was not actually reproducing the earlier
  "headset on face, normal wear sensor" operator workflow that had exited
  cleanly

## April 8 Off-Face Exit Failure

Artifact bundle:

- `artifacts/verify/headset-recovery-20260408-073525/`

Observed during a later off-face Sussex cleanup attempt on 2026-04-08:

- shell-side `dumpsys activity` still looked home-like with
  `HomeActivity` plus `FocusPlaceholderActivity`
- `dumpsys vrpowermanager` simultaneously reported `State: STANDBY` with
  `Virtual proximity state: DISABLED`
- `uiautomator dump` confirmed the visible blocker was again the Guardian
  `Finding position in room` dialog with `Continue without tracking`
- after `automation_disable -> prox_close`, the Guardian layer dismissed, but
  the deeper blocker became `com.oculus.os.vrlockscreen` with the text:
  `Press the power button to enable cameras and microphones`
- even after renewed physical power-button attempts, the session still did not
  produce a dependable screenshot-confirmed Meta Home recovery

Current proximity-state rule on this headset build:

- `Virtual proximity state: CLOSE` means the direct `prox_close` override is
  actively holding the virtual wear sensor closed.
- `Virtual proximity state: DISABLED` means `automation_disable` restored
  normal wear-sensor behavior.

Updated practical rule:

- do not use Sussex kiosk exit or `viscereality study stop sussex-university`
  as default off-face cleanup
- if Sussex must be exited, prefer a user-worn manual quit with screenshot
  confirmation afterward
- for functionality tests, quitting the Sussex APK is not required

## March 29 Double-Power Escape Hatch

Artifact bundle:

- `artifacts/verify/limbo-home-recovery-dead-end-20260329/`

Observed dead-end:

- shell-side recovery returned `HomeActivity` and `FocusPlaceholderActivity`
  again, but the matching metacam captures still stayed passthrough-only:
  - `recovery-home-proof-20260329-1.png`
  - `recovery-home-proof-20260329-2.png`
- after `adb reboot`, the headset came back to
  `com.oculus.guardian/.GuardianDialogActivity`
- from that fresh post-reboot state, running
  `automation_disable -> prox_close -> HomeActivity` again produced a black
  metacam frame instead of visible Home:
  - `recovery-home-proof-20260329-3.png`

Confirmed escape:

- pressing the physical headset power button twice restored a real Home-side
  scene
- `recovery-home-proof-20260329-4-after-double-power.png` shows visible Meta
  Home / store content again
- shell-side state after that recovery improved to a usable Home-side surface
  with:
  - `com.oculus.systemux/com.oculus.panelapp.virtualobjects.VirtualObjectsActivity`
  - `com.oculus.vrshell/com.oculus.panelapp.controlbar.ControlBarActivity`
  - `com.oculus.vrshell/.HomeActivity`

Updated interpretation:

- from this specific March 29 dead-end, shell-only recovery was exhausted
- neither shell-owned `HomeActivity` nor a full `adb reboot` was enough by
  itself
- a double physical headset power-button press is now a confirmed stronger
  escape hatch than a single press for this passthrough/black Home-limbo
  family on the current build

## Recommended Companion Behavior

For `ViscerealityCompanion`, prefer this recovery order when the wake path is
blocked by Guardian / ClearActivity / FocusPlaceholder:

1. if the study APK is already known to be running, resolve its current task ID
   from `dumpsys activity recents` and try `am task lock <TASK_ID>` first
2. if that does not recover visibility, try `automation_disable`
3. try `prox_close`
4. proceed with launch immediately
5. only fall back to raw power-key recovery if the task-lock and
   automation/prox sequence make no progress

Do not treat raw `KEYCODE_POWER` as the primary recovery for this specific
blocker.

## Open Follow-Up Work

- Find whether a shell-allowed path exists to explicitly accept
  `GUARDIAN_TRACKING_LOST_CONTINUE` instead of merely dismissing / outracing it.
- Test whether the current `FORWARD` / `GUIDE` Home-routing behavior is stable
  while the Sussex Unity runtime is actually foregrounded, not just from shell
  baselines.
- Determine whether `FocusPlaceholderActivity` bursts are detectable quickly
  enough in the companion to count as a robust accidental-Meta-button signal
  even when no visible menu appears.
- Check whether a follow-up action after `FORWARD` / `GUIDE` can dismiss
  `GuardianDialogActivity` or `SensorLockActivity`, or whether those overlays
  are fully permission-gated.
- Check whether `am task lock <TASK_ID>` has any unwanted side effects for the
  operator flow, and whether a cleaner non-locking task-to-front command exists
  on this HorizonOS build.
- If possible, add a future control experiment in the same session:
  - one known headset hardware button press such as volume up
  - one right-controller Meta / menu press
  - compare which layers see each event
- Check whether the verification harness should keep polling foreground state
  after launch so it can observe late success more robustly.
- If a future session finds a reliable replacement for the race-based launch
  sequence, update this file and the central Quest pattern note together.
