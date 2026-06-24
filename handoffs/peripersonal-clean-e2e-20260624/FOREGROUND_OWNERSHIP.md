# Foreground And Input Ownership Notes

## Implemented Contract

The Peripersonal Unity runtime, the Quest questionnaire panel, and Companion
now use low-cadence app-owned beacons as the primary foreground/input-owner
signal. ADB and screenshots are validation evidence only.

Transport:

- UDP port: `47892`
- Multicast group: `239.255.42.42`
- Protocol id: `viscereality.foreground_status.v1`
- Also sent to broadcast as a same-LAN fallback

Unity source id: `unity`

Unity reports:

- `unity_paused`
- `unity_focused`
- `has_input_focus`
- `hmd_mounted`
- package/session metadata when available

Panel source id: `panel`

Panel reports:

- `activity_started`
- `activity_resumed`
- `window_focused`
- active request/session id
- questionnaire block/stage metadata
- submission lifecycle markers

Companion listens to these beacons in both WPF and CLI. The WPF Peripersonal
study shell displays the current `Input owner`, and the CLI exposes the same
classifier:

```powershell
dotnet $cli peripersonal foreground-status --wait-seconds 6 --json
```

## Classification

| State | Unity signal | Panel signal | Meaning |
|---|---|---|---|
| Unity foreground | `unity_paused=false`, `has_input_focus=true` | panel not resumed/focused | 3D app owns input |
| Panel foreground | Unity running with input not owned | `activity_resumed=true`, `window_focused=true` | 2D panel owns input; Unity is visible/running behind |
| Panel open, not focused | any Unity non-focused state | `activity_resumed=true`, `window_focused=false` | panel opened but Quest/system focus is not on the panel window |
| Unity background/hidden | `unity_paused=true` | panel may or may not be focused | Unity should not be considered the active operator target |
| System UI foreground | Unity lacks input focus | panel not window-focused | Universal Menu/Home/system overlay or unknown owner |

Core rule:

```text
foreground_owner =
  panel if panel.activity_resumed && panel.window_focused
  panel_open_without_focus if panel.activity_resumed && !panel.window_focused
  else unity if !unity_paused && unity.has_input_focus
  else system_or_unknown
```

## Live Validation Result

The 2026-06-24 foreground run proved the intended states:

- After `study launch`, Companion CLI reported
  `Peripersonal XR runtime owns input`.
- During a normal MAIA Block 1 open/submit flow, raw panel events included
  `window_focus_acquired`, `submission_completed`, and `activity_stopped`.
- After participant submit, raw Unity events included resume/focus and the
  classifier returned to Unity.
- WPF readback showed the Peripersonal shell `Input owner` card.
- In the real Quest state where the panel had opened but was not
  window-focused, WPF reported
  `Questionnaire panel is open but not input-focused`.
- After submit settled, WPF reported
  `Peripersonal XR runtime owns input`.

Evidence files are listed in `FOREGROUND_STATUS_LIVE_VALIDATION.md`.

## Important Boundary

Do not use ADB foreground readback as the runtime authority for this workflow.
ADB, Meta tooling, and screenshots are useful for validation and debugging, but
the operator app should trust app-owned beacons for the live UI state.
