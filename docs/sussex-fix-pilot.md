---
title: Sussex Fix Pilot
description: Operator-only release channel for validating the Sussex participant-session fix without changing the public Companion release.
summary: The pilot is a separate packaged Windows app with tag-pinned assets. It does not update or replace the public 0.1.79 release.
nav_label: Sussex Fix Pilot
nav_group: Reference
nav_order: 95
layout: focused
---

# Sussex Fix Pilot

This is an operator-only release channel for the validated participant-session
fix. It is intentionally separate from the public **Viscereality Companion**
release so an active legacy cohort can remain on its existing workflow.

## What stays unchanged

- The current public release remains the normal operator path. Do not replace
  its `releases/latest` feed while an active cohort depends on it.
- The legacy `0.1.79` assets remain available from the immutable
  [`sussex-timing-marker-msix-0.1.79` release](https://github.com/MesmerPrism/ViscerealityCompanion/releases/tag/sussex-timing-marker-msix-0.1.79).
- A pilot release never uses `/releases/latest/download/` for its App Installer
  or MSIX URLs.

## Pilot operator workflow

1. Receive the exact pilot release tag from the study owner, for example
   `sussex-fixed-pilot-v0.1.80`.
2. Download and run only that tag's
   `ViscerealityCompanion-SussexFixPilot-Setup.exe` asset. It installs a second
   Start-menu entry named **Viscereality Companion Sussex Fix Pilot**; it does
   not replace the public Companion app or its operator-data folder.
3. Before starting either Companion app, verify that the other is closed. This
   is an operator rule for the pilot period, not an in-app interlock.
4. Use a dedicated pilot Quest. The pilot and legacy APK currently share the
   `com.Viscereality.SussexExperiment` Android identity and LSL command/state
   channels, so they must not operate the same headset concurrently.
5. Use the Pilot's Sequential Guide followed by Experiment Session. Record the
   release tag, Windows package version, APK SHA-256, Quest serial, and session
   folder with the resulting validation evidence.

## Publishing a pilot

The **Release Sussex Fix Pilot** GitHub Actions workflow is manual-only. Enter
a three-part version such as `0.1.80`; it creates the immutable prerelease tag
`sussex-fixed-pilot-v0.1.80`.

Before dispatching it:

- commit the candidate Companion changes on the intended release branch;
- build the associated Astral APK from its committed runtime revision, update
  the bundled APK and both pinned hash manifests, then repeat the required
  validation against those final bytes;
- use a new Android versionCode for a new pilot APK. A different hash with the
  same Android package version is not a distinct deployable release.

The workflow rejects a reused pilot tag, signs and validates the setup EXE and
MSIX, verifies that the App Installer is bound to the pilot package identity
and tag URLs, uploads a retained workflow artifact, and creates a GitHub
prerelease with `--latest=false`.

Pilot promotion is a separate decision. Do not publish a normal `v*` release
until the legacy cohort has completed or explicitly agreed to migrate.
