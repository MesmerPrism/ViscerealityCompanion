---
title: Peripersonal Questionnaire Routing
description: Current Peripersonal questionnaire launch/result route and the later decoupling path.
summary: Notes why the Peripersonal branch currently keeps questionnaire launch and callback routing through the Unity runtime, and how to split the panel result path later if the study no longer needs Unity-side result ingest.
nav_label: Peripersonal Questionnaire Routing
nav_group: Operator Guides
nav_order: 42
---

# Peripersonal Questionnaire Routing

The Peripersonal branch currently keeps the Quest questionnaire panel on the
same caller-owned Android handoff used by the shared panel app. The panel is a
separate Quest application, but the active Unity runtime is the Quest-side
caller for the current experiment workflow.

## Current Route

```text
Windows WPF button or CLI command
  -> Peripersonal Companion workflow command
  -> Unity-targeted operator command over LSL
  -> Unity Peripersonal command bridge
  -> Unity Android questionnaire caller bridge
  -> separate Quest questionnaire panel app
  -> participant submit inside the panel
  -> panel writes result to Unity-owned content URI
  -> panel sends Unity callback PendingIntent
  -> Unity records questionnaire_results.jsonl
  -> Windows pulls the completed session bundle
```

Unity does not render the questionnaire, fill answers, or submit the form. The
panel app owns the participant UI and validation. Unity currently owns only the
Quest-side launch/result contract and records the completed panel result into
the same app-private session bundle as the XR timing and runtime files.

## Why Keep This For Now

This route is useful while the Peripersonal study shell is still being hardened
because it gives the end-to-end run one coherent Quest-side session bundle:

- the same prepared session id and participant ref reach Unity and the panel;
- questionnaire launch attempts are acknowledged through the existing Unity
  operator command ledger;
- panel submit produces a Unity callback that can be recorded beside XR timing
  files;
- the final Windows pull can validate `questionnaire_results.jsonl` together
  with continuous recording, clock alignment, and marker files;
- XR foreground return after participant submit is observable from the same
  Quest-side caller relationship.

The cost is coupling. Unity currently receives completed questionnaire result
JSON even though the XR scene may not need the answers themselves.

## Later Decoupling Path

If the study design settles on Windows as the only questionnaire-result owner,
the routing can be simplified:

```text
Windows WPF button or CLI command
  -> panel launch route
  -> separate Quest questionnaire panel app
  -> participant submit inside the panel
  -> panel records or sends result to Windows-owned storage/endpoint
  -> panel backgrounds itself
  -> XR runtime is foregrounded or resumes
  -> Windows sends Unity only questionnaire-start/submitted markers if needed
```

That split should keep Unity out of questionnaire answer ownership. Unity would
receive only low-rate operator markers such as `Questionnaire-Block-2-Opened`
or `Questionnaire-Block-2-Submitted` when those markers are needed for the XR
timeline or LSL recording.

Before changing to that route, validate the replacement contract end to end:

- panel submit writes a durable Windows-owned result with request id, session
  id, participant ref, stage, and status;
- submit is accepted only when the current questionnaire stage is complete;
- the panel backgrounds or closes without an operator fallback action;
- the XR runtime reliably returns to foreground without an ADB relaunch;
- Windows records questionnaire submit markers in the command ledger and LSL
  timeline;
- the final session folder contains both questionnaire data and XR data under
  the same naming convention.

Until that validation exists, the Peripersonal branch should keep the current
Unity-mediated caller route and treat it as an implementation decision rather
than a permanent protocol requirement.
