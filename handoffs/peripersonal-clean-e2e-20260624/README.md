# Peripersonal Clean E2E Handoff

Date: 2026-06-24

This handoff captures the current Peripersonal Space study-shell state, the
clean end-to-end validation run completed on Till's machine, and the package
layout for a collaborator who needs to clone the Companion branch and run the
same setup on another Windows machine plus Quest headset.

## Scope

- Companion branch: `codex/peripersonal-wpf-unity-operator-20260620`
- Unity branch: `codex/peripersonal-wpf-unity-runtime-20260620` at `b8e853f`
- Questionnaire panel branch: `codex/peripersonal-operator-runtime-20260618` at `1538fb5`
- Study shell: `peripersonal-space`
- Runtime APK package: `com.Viscereality.ViscerealityPeriPersonal`
- Questionnaire panel APK package: `io.github.mesmerprism.questquestionnaire.panel`
- Questionnaire: MAIA-2 Spatial Frame, three-stage flow
- Recording model: one global recording after Questionnaire Block 1, one final stop at the end

## Files

- `RUN_SUMMARY.md`: what was tested and what passed in the completed run.
- `AGENT_E2E_SETUP.md`: machine-independent setup and E2E run instructions for another agent.
- `FOREGROUND_OWNERSHIP.md`: current foreground/focus signaling and recommended refinement.
- `EVIDENCE_NOTES.md`: how to read raw run evidence without reusing local paths.
- `GITHUB_PUSH_NOTES.md`: what should be pushed and how to verify the pushed branch.

The shareable ZIP generated from this handoff adds:

- `apks/PeripersonalRuntime.apk`
- `apks/QuestQuestionnairePanel.apk`
- `apks/sha256sums.txt`
- selected run evidence under `run-evidence/`
- a copy of these docs at the ZIP root

## Important Caveat

The final run intentionally stopped capturing Quest screenshots after the user
asked for a faster run while wearing the headset. The proof for the later
questionnaire blocks is therefore the persisted questionnaire result JSONL,
Unity focus/pause events, command ledger, timing markers, clock CSVs, and final
Quest backup pull, not visual media.
