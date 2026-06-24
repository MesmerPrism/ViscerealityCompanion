# Peripersonal Clean E2E Handoff

Date: 2026-06-24

This handoff captures the current Peripersonal Space study-shell state, the
clean end-to-end validation run completed on Till's machine, and the package
layout for a collaborator who needs to clone the Companion branch and run the
same setup on another Windows machine plus Quest headset.

## Scope

- Companion branch: `codex/peripersonal-wpf-unity-operator-20260620`
- Unity branch: `codex/peripersonal-wpf-unity-runtime-20260620`
- Questionnaire panel branch: `codex/peripersonal-operator-runtime-20260618`
- Study shell: `peripersonal-space`
- Runtime APK package: `com.Viscereality.ViscerealityPeriPersonal`
- Questionnaire panel APK package: `io.github.mesmerprism.questquestionnaire.panel`
- Questionnaire: MAIA-2 Spatial Frame, three-stage flow
- Recording model: one global recording after Questionnaire Block 1, one final stop at the end

## Files

- `RUN_SUMMARY.md`: what was tested and what passed in the completed run.
- `AGENT_E2E_SETUP.md`: machine-independent setup and E2E run instructions for another agent.
- `FOREGROUND_OWNERSHIP.md`: current foreground/focus signaling and recommended refinement.
- `FOREGROUND_STATUS_LIVE_VALIDATION.md`: latest live proof for Unity/panel/WPF foreground ownership.
- `EVIDENCE_NOTES.md`: how to read raw run evidence without reusing local paths.
- `GITHUB_PUSH_NOTES.md`: what should be pushed and how to verify the pushed branch.

The shareable ZIP generated from this handoff adds:

- `apks/PeripersonalRuntime.apk`
- `apks/QuestQuestionnairePanel.apk`
- `apks/sha256sums.txt`
- selected run evidence under `run-evidence/`
- a copy of these docs at the ZIP root

## Latest Foreground Status Update

The 2026-06-24 foreground-status update adds low-cadence UDP input-owner
beacons from both Quest apps:

- Unity emits `unityPaused`, `unityFocused`, `hasInputFocus`, and session
  metadata.
- The questionnaire panel emits Android lifecycle/window-focus state plus the
  active MAIA request/stage metadata.
- Companion WPF and Companion CLI classify those beacons without ADB as:
  Unity owns input, questionnaire panel owns input, questionnaire panel is open
  but not input-focused, or system/unknown.
- The Companion CLI now exposes the same WPF classifier through:
  `viscereality peripersonal foreground-status --wait-seconds 6 --json`.
- The bundled `PeripersonalRuntime.apk` has been refreshed to SHA-256
  `3cf14c777cf67648b829743d48eea21a4ee28da1ac8a0a7049f458bd4c1d95bd`.

## Important Caveat

The final run intentionally stopped capturing Quest screenshots after the user
asked for a faster run while wearing the headset. The proof for the later
questionnaire blocks is therefore the persisted questionnaire result JSONL,
Unity focus/pause events, command ledger, timing markers, clock CSVs, and final
Quest backup pull, not visual media.
