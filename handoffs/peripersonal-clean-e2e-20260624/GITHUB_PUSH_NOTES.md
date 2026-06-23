# GitHub Push Notes

Branch to push:

`codex/peripersonal-wpf-unity-operator-20260620`

Remote:

`upstream https://github.com/MesmerPrism/ViscerealityCompanion.git`

The push should include:

- Peripersonal study-shell workflow changes already present in the working tree.
- Companion CLI parity for the operator-equivalent Peripersonal commands.
- HTTP forward setup for Unity status.
- Peripersonal keep-awake launch handling.
- Focused tests covering the Peripersonal command transport, workflow service,
  and keep-awake launch policy.
- This handoff folder.

The shareable ZIP is intentionally generated under `artifacts/` and should not
be committed unless a maintainer explicitly decides to publish binary evidence
bundles from the repo.

After pushing, update the ZIP copy of this file with:

- commit hash
- push command
- push result
- any tests run before push
