---
title: Docs Home
description: Start here if you need the Windows companion app that gives researchers, labs, and collaborators one place to install, launch, monitor, and run Viscereality study workflows.
summary: Sussex-focused public entry point for the Windows companion app, with broader multi-study tooling planned after the current Sussex collaboration release is stable.
nav_label: Docs Home
nav_group: Start Here
nav_order: 10
---

# Viscereality Companion

Viscereality Companion is the Windows-side access point for researchers, labs,
and collaborators who want to use Viscereality without hand-building Quest
tooling or digging through runtime code. The goal is one place to connect the
headset, install the right build, verify live telemetry, run the study
workflow, and keep operator control on Windows.

<div class="action-row">
  <a class="button primary" href="download.md">Install The App</a>
  <a class="button" href="first-session.md">Run A Sussex Session</a>
  <a class="button" href="https://mesmerprism.com/projects/viscereality-companion.html">Companion Overview</a>
  <a class="button" href="https://viscereality.org/">Viscereality Project Site</a>
  <a class="button" href="troubleshooting.md">Troubleshooting</a>
</div>

## Current Public Focus

- the current release is being hardened for the collaboration with [Hugo Critchley's lab at Sussex](https://www.sussex.ac.uk/research/centres/sussex-neuroscience/phd/4yearphd/supervisors/translational-projects/hugo-critchley-project)
- the packaged Sussex preview already bundles the approved Sussex APK, device profile, study shell, and operator diagnostics
- the app opens directly into `Sussex University experiment mode` so a researcher can stay on one guided Windows workflow
- broader general-purpose multi-study tooling comes after the Sussex-first release path is stable

## What The Windows Companion Already Handles

- the WPF operator app
- USB and Wi-Fi ADB connection from Windows
- install and launch for the bundled Sussex APK or another supplied study APK
- live Quest, LSL, and twin-state monitoring
- study shells that lock the operator UI to a specific protocol
- runtime-config staging and saved profile workflows from the desktop side
- release assets that let another machine install the same researcher-facing setup

## Use This Repo When You Need To

- install the study build directly from Windows
- stage device and runtime settings without opening the Quest-side project
- launch the participant-facing runtime and confirm the foreground package
- watch `quest_monitor`, LSL, twin-state, and operator diagnostics during a run
- keep researchers on a one-stop GUI instead of a pile of separate lab utilities
- package a study-specific operator surface for a lab workflow

## If This Is Your First Session

Use these three pages in order:

1. [Download](download.md) the Windows app or launcher path you were given.
2. [First Session](first-session.md) to connect the Quest, install the study APK, and launch it.
3. [Study Shells](study-shells.md) if your team gave you a dedicated operator surface such as Sussex.

## Choose Your Path

<div class="card-grid">
  <a class="path-card" href="https://mesmerprism.com/projects/viscereality-companion.html">
    <h3>Companion Overview</h3>
    <p>Public-facing overview of what the companion app is for, who it is for, and where the Sussex-first release sits in the broader roadmap.</p>
  </a>
  <a class="path-card" href="download.md">
    <h3>Install The Launcher</h3>
    <p>Preferred path for researchers and operators. The current public installer is a Sussex-focused preview that already bundles the study build and opens in the dedicated study shell.</p>
  </a>
  <a class="path-card" href="first-session.md">
    <h3>First Sussex Session</h3>
    <p>Connect Quest, approve USB debugging, install the Sussex APK, switch to Wi-Fi ADB, and launch Sussex in one pass.</p>
  </a>
  <a class="path-card" href="study-shells.md">
    <h3>Dedicated Study Shell</h3>
    <p>Understand what the Sussex package pins, hides, and bundles so researchers stay on the safe operator path.</p>
  </a>
  <a class="path-card" href="runtime-config.md">
    <h3>Runtime Config</h3>
    <p>Use the grouped runtime editor to stage tracked desktop-side changes without dropping into the Quest project.</p>
  </a>
  <a class="path-card" href="getting-started.md">
    <h3>Build From Source</h3>
    <p>Use this only if you are validating or changing the repo itself, and remember to pull the Git LFS APK mirror.</p>
  </a>
</div>

## Need A Custom Adaptation?

If you need a lab-specific study shell, a custom operator workflow, different
telemetry plumbing, packaging for a new protocol, or changes to the
participant-facing Viscereality build itself, I am open to commissioned
adaptation work around both the Viscereality system and this Windows companion.

<div class="action-row">
  <a class="button primary" href="https://mesmerprism.com/#contact">Contact Via Mesmer Prism</a>
  <a class="button" href="https://github.com/MesmerPrism/ViscerealityCompanion">Source Repo</a>
</div>

## Read These First

- [Download](download.md)
- [First Session](first-session.md)
- [Study Shells](study-shells.md)
- [App Overview](app-overview.md)
- [Monitoring and Control](monitoring-and-control.md)
- [Troubleshooting](troubleshooting.md)

## Project Scope

This repo is public and focused on the Windows companion, documentation,
packaging, and study-shell delivery. The participant-facing Quest runtime is
part of the broader Viscereality system but stays separate from this public
operator repo. If you need the outward-facing project context, go to
[viscereality.org](https://viscereality.org/). If you need the public-facing
companion overview, use the
[Mesmer Prism companion page](https://mesmerprism.com/projects/viscereality-companion.html).
