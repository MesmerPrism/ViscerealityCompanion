# Peripersonal Foreground Status

The Peripersonal operator shell classifies Quest foreground state from
app-owned input focus reports, not from ADB.

Two Quest producers emit low-rate UDP beacons:

- Unity XR app: `source_app = "unity"`
- questionnaire panel app: `source_app = "panel"`

The Windows companion listens on UDP port `47892`, joins multicast group
`239.255.42.42` when the network adapter allows it, and also accepts broadcast
packets. Beacons are observability only. They do not carry questionnaire
answers, participant raw data, controller samples, or commands.

## Windows Firewall

Windows may show an access prompt the first time a process opens the foreground
status listener. During tests that process can be `testhost.exe`; during real
operator use it will be `ViscerealityCompanion.exe` or the packaged Companion
app identity. Accepting the `testhost.exe` prompt proves the test process can
listen, but it does not automatically grant the real app inbound access.

For live headset runs, allow Viscereality Companion on Private networks for UDP
port `47892`. If the WPF shell keeps showing `Waiting for headset foreground
beacons` while Unity or the questionnaire panel is visibly running on the same
Wi-Fi, check this firewall rule before falling back to ADB foreground readback.

## Contract

Protocol id:

```text
viscereality.foreground_status.v1
```

Example Unity beacon:

```json
{
  "protocol_version": "viscereality.foreground_status.v1",
  "source_app": "unity",
  "package_name": "com.Viscereality.ViscerealityPeriPersonal",
  "activity_name": "com.unity3d.player.UnityPlayerGameActivity",
  "sequence": 17,
  "emitted_at_utc": "2026-06-24T12:00:00.0000000Z",
  "session_id": "session-001",
  "participant_ref": "P001",
  "reason": "tick",
  "lifecycle": {
    "unity_paused": false,
    "unity_focused": true,
    "has_input_focus": true,
    "hmd_mounted": true
  },
  "questionnaire": {}
}
```

Example panel beacon:

```json
{
  "protocol_version": "viscereality.foreground_status.v1",
  "source_app": "panel",
  "package_name": "io.github.mesmerprism.questquestionnaire.panel",
  "activity_name": "QuestionnaireActivity",
  "sequence": 9,
  "emitted_at_utc": "2026-06-24T12:00:10Z",
  "session_id": "session-001",
  "participant_ref": "",
  "reason": "window_focus_acquired",
  "lifecycle": {
    "activity_started": true,
    "activity_resumed": true,
    "window_focused": true
  },
  "questionnaire": {
    "request_id": "request-001",
    "questionnaire_id": "maia2-spatial-frame-questionnaire-v1",
    "open_stage": "maia_spatial:spatial_frame_reference_1"
  }
}
```

## Classification

The companion treats beacons as fresh for four seconds.

```text
foreground_owner =
  panel if panel.activity_resumed && panel.window_focused
  panel_open_without_focus if panel.activity_resumed && !panel.window_focused
  else unity if !unity_paused && unity.has_input_focus
  else system_or_unknown
```

If no fresh Unity or panel beacon exists, the shell shows `Unknown` and falls
back to ADB foreground readback only as validation evidence.

The panel-open-without-focus state is intentional. It tells the operator that
the questionnaire app did open, but Quest input focus is currently owned by
another surface such as the system UI, so a participant may need to reselect
the panel before answering.
