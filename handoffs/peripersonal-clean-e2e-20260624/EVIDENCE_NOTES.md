# Evidence Notes

The ZIP includes selected raw evidence from the completed Till-machine run.
Some raw files, especially `RUN_COMMAND_LOG.md`, preserve absolute paths such
as `S:\Work\...` and `C:\Users\tillh\...` because those were the paths used
when the validation happened.

Do not reuse those absolute paths on another machine.

For a collaborator or agent on another Windows machine:

- use `AGENT_E2E_SETUP.md` for commands
- use paths relative to the cloned repo or the unzipped handoff folder
- treat raw run paths as provenance only
- treat `PACKAGE_MANIFEST.json` and `apks/sha256sums.txt` as the portable file
  verification surfaces

The evidence files to inspect first are:

- `run-evidence/26-session-file-summary.json`
- `run-evidence/25-questionnaire_results.jsonl`
- `run-evidence/25-session_events.csv`
- `run-evidence/25-timing_markers.csv`
- `run-evidence/24-clock-alignment-roundtrip.csv`
- `run-evidence/25-clock_alignment_samples.csv`
- `run-evidence/40-cleanup-summary.json`
