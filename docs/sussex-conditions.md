---
title: Sussex Conditions
description: Configure the experiment-session conditions that combine one Sussex visual profile, one breathing profile, and an active-selection flag.
summary: Conditions are the operator-facing study choices used by the Sussex Experiment Session window.
nav_label: Sussex Conditions
nav_group: Operator Guides
nav_order: 36
---

# Sussex Conditions

The Sussex shell now has a dedicated `Conditions` tab. A condition is a small,
shareable study object that currently combines:

- one visual profile
- one controller-breathing profile
- one `Active` flag
- optional key/value metadata for later protocol differences

Only active conditions appear in the `Experiment Session` condition dropdown.
This keeps the live participant window focused while still letting the study
team keep draft or archived conditions in the local library.

## Bundled Conditions

The committed Sussex package includes two active bundled conditions:

- `current`: uses `condition-current-visual` plus
  `condition-current-breathing`, preserving the current Sussex settings.
- `fixed-radius-no-orbit`: uses `condition-fixed-radius-no-orbit` plus
  `condition-fixed-radius-breathing`.

The fixed-radius visual profile sets `orbit_distance_min` and
`orbit_distance_max` to `0`, so the orbit-distance envelope collapses to no
orbit offset. It also sets `sphere_radius_min` and `sphere_radius_max` to `2`,
so the shared sphere-radius envelope is locked at that radius. That matches the
intended interpretation: equal min/max envelope values lock the animated
property, and zero orbit distance disables that orbit-distance contribution.

## GUI Workflow

Open `Sussex University experiment mode`, then use the `Conditions` tab.

- `New Condition` creates a local inactive condition using the first available
  visual and breathing profile references.
- `Duplicate` copies the selected condition into a new local inactive row.
- `Save Selected` writes the row to the local condition library. Saving a
  bundled row creates a local override with the same condition id.
- `Load Condition` imports a shared condition JSON file into the local library.
- `Share Selected` exports the selected condition as JSON.
- `Delete Local` deletes a local condition, or removes the local override for a
  bundled condition.

The visual and breathing dropdowns use the same profile libraries as the
`Visual Profiles` and `Breathing Profiles` tabs. When the `Conditions` tab is
opened, those dropdowns refresh automatically so newly created or imported
profiles are available without restarting the app.

## CLI Workflow

The CLI mirrors the GUI through:

```powershell
viscereality sussex condition list --json
viscereality sussex condition show fixed-radius-no-orbit
viscereality sussex condition create `
  --id new-condition `
  --label "New Condition" `
  --visual condition-current-visual `
  --breathing condition-current-breathing `
  --inactive `
  --json
viscereality sussex condition update new-condition --active --json
viscereality sussex condition export new-condition .\new-condition.json --json
viscereality sussex condition import .\new-condition.json --json
viscereality sussex condition delete new-condition --json
```

Use `--active-only` with `list` to see exactly what the Experiment Session
dropdown will expose. The CLI accepts the same portable bundled profile ids
that the GUI stores in condition files, such as `condition-current-visual` and
`condition-fixed-radius-breathing`.

Local condition files are stored under the current operator-data root:

```text
...\ViscerealityCompanion\study-conditions\sussex-university\
```

For packaged installs this is the host-visible package-local root used by the
app and exported local-agent workspace. For source builds it is normally under
`%LOCALAPPDATA%\ViscerealityCompanion`, unless
`VISCEREALITY_OPERATOR_DATA_ROOT` is set.

## Adding Future Differences

Conditions intentionally keep optional metadata separate from the profile
references. Today the runtime behavior is fully defined by the selected visual
and breathing profiles. Later protocol differences can be added to the
condition schema and GUI without changing the Unity APK or rewriting existing
condition files.
