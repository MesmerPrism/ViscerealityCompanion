# Foreground Status Live Validation

Date: 2026-06-24

Raw artifact source on Till's machine:

```text
S:\Work\repos\active\PeripersonalCompanion\artifacts\foreground-status-live-20260624-101827
```

This validation checked the new non-ADB foreground/input-owner path across the
Unity runtime, the Quest questionnaire panel, Companion CLI, and Companion WPF.

## What Was Tested

1. Installed and launched the Peripersonal Unity runtime.
2. Queried Companion CLI foreground status after launch.
3. Opened the MAIA Spatial Block 1 questionnaire through the normal
   operator-equivalent CLI command.
4. Filled/submitted the panel through participant automation, not the operator
   fallback close action.
5. Monitored panel lifecycle/window-focus events and Unity resume/focus events.
6. Loaded the WPF Peripersonal shell and read back the `Input owner` UI text.
7. Repeated the status readback while the panel was open but not window-focused.
8. Confirmed the WPF/CLI classifier returned to Unity after submit settled.

## APKs Used

- Unity runtime APK:
  `viscereality-peri-personal-runtime.apk`
  SHA-256 `3cf14c777cf67648b829743d48eea21a4ee28da1ac8a0a7049f458bd4c1d95bd`
- Questionnaire panel APK:
  `app-minimal-debug.apk`
  SHA-256 `2768660e9af269f12dc8a39a6c98ce03d0890b4cf459602717983473a8920c3f`

## Evidence Files

From the foreground run folder:

- `foreground-status-live-summary.md`
- `04-companion-foreground-status-after-launch.out.txt`
- `15-monitor-during-normal-open.out.json`
- `17-companion-foreground-status-after-normal-submit.out.txt`
- `23-wpf-uia-readback-after-fix.txt`
- `36-wpf-uia-final-during-panel.txt`
- `38-wpf-uia-final-settled-after-submit.txt`

## Key Observations

- After launch, Companion reported:
  `Peripersonal XR runtime owns input.`
- During normal questionnaire open/submit, the raw monitor recorded panel
  `window_focus_acquired`, `submission_completed`, `activity_stopped`, then
  Unity resume/focus events.
- WPF initially exposed a constructor-order bug: the foreground status was
  applied before Peripersonal guide commands were initialized. The fix defers
  the initial apply until after command construction.
- After the fix, WPF loaded the Peripersonal shell and showed the `Input owner`
  card.
- When the panel was resumed but not window-focused, WPF reported:
  `Questionnaire panel is open but not input-focused.`
- After participant submit settled, WPF reported:
  `Peripersonal XR runtime owns input.`

## Interpretation

The implemented signal path is representative of the human operator UI because
the CLI command and the WPF card use the same Companion foreground classifier.
The only difference in the proof run is that participant panel interaction was
driven by the panel test automation instead of a person selecting answers in
the headset.
