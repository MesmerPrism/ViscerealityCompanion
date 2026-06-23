# Foreground And Input Ownership Notes

## Current Implementation

There are three foreground-related evidence surfaces today:

1. Companion WPF guide gates use the headset snapshot from ADB parsing.
   The Peripersonal guide checks `IsStudyRuntimeForeground()` and presents
   `HeadsetForegroundLabel` when deciding whether the XR runtime is foregrounded.
   This is useful for launch readiness, but it is still an external validation
   signal.

2. Unity exposes `/v1/status` through the HTTP bridge over `adb forward
   tcp:8787 tcp:8787`. The status response includes a `foreground` object, but
   the current Unity implementation builds it as a static XR self-report:

   ```json
   {
     "xr_app_foreground": true,
     "panel_foreground": false,
     "foreground_package": "com.Viscereality.ViscerealityPeriPersonal"
   }
   ```

   This proves the Unity bridge is responsive, but it should not be treated as
   authoritative proof that Unity owns input while the 2D panel is open.

3. Unity session recording logs real lifecycle events while recording:
   `OnApplicationFocus(bool)` writes `application_focus,focused/unfocused`, and
   `OnApplicationPause(bool)` writes `application_pause,paused/resumed`.
   In the completed run, these events showed the expected pattern around panel
   submit:
   - panel launch caused `application_focus,unfocused`
   - participant submit produced `questionnaire_result`
   - Unity then logged `application_pause,resumed` and
     `application_focus,focused`

## Recommended Rule

For a Unity immersive app plus a 2D panel app, use input ownership as
"foreground", not just "running".

Unity 3D app should report:

- `unityPaused`: from `OnApplicationPause(bool)`
- `unityFocused`: from `OnApplicationFocus(bool)` or
  `OVRManager.InputFocusAcquired/Lost`
- `hasInputFocus`: poll `OVRManager.hasInputFocus`
- optional: `hmdMounted`, `hmdUnmounted`
- optional native/OpenXR mirror: `XR_SESSION_STATE_FOCUSED` vs `VISIBLE`

2D panel app should report:

- `activityStarted`: `onStart`
- `activityResumed`: `onResume`
- `activityPaused`: `onPause`
- `activityStopped`: `onStop`
- `windowFocused`: `onWindowFocusChanged(true/false)`
- optional: panel open/close intent/session id

## Classification

| State | Unity signal | 2D panel signal | Meaning |
|---|---|---|---|
| Unity foreground | `paused=false`, `hasInputFocus=true` | panel not focused/resumed | 3D app owns input |
| Panel foreground over Unity | `paused=false`, `hasInputFocus=false` or OpenXR `VISIBLE` | `resumed=true`, `windowFocused=true` | Panel owns input; Unity still visible/running behind |
| Unity background/hidden | `OnApplicationPause(true)` | panel may or may not be focused | Unity should stop interaction/rendering/simulation |
| System UI foreground | Unity `hasInputFocus=false`; panel `windowFocused=false` | neither app owns input | Universal Menu/Home/system overlay owns focus |

Core rule:

```text
foreground_owner =
  panel if panel.activityResumed && panel.windowFocused
  else unity if !unityPaused && unity.hasInputFocus
  else system_or_unknown
```

## Implementation Recommendation

For Rusty/Manifold-style reporting, have both apps emit timestamped low-rate
state snapshots to the same broker. Use ADB or Meta MCP foreground readback as
validation evidence only, not as the runtime source of truth.

For the current Unity plus panel path, the next refinement should:

1. Extend Unity status/runtime-state samples with `unityPaused`,
   `unityFocused`, and `hasInputFocus`.
2. Extend the panel app with a low-rate lifecycle snapshot or result callback
   payload containing `activityResumed` and `windowFocused`.
3. Add a Companion-side foreground-owner classifier using the rule above.
4. Keep ADB `dumpsys window` and screenshots as evidence probes, not as the
   primary source for runtime control decisions.
