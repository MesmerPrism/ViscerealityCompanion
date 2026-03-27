---
title: Docs Home
description: Start here if you need the Windows operator app for installing, launching, monitoring, and remotely supervising a bundled Sussex build or another supplied Quest build.
summary: Use the packaged app or repo build to get from Quest headset to monitored live session first. The detailed tabs and CLI come after the operator path is clear.
nav_label: Docs Home
nav_group: Start Here
nav_order: 10
---

# Viscereality Companion

Viscereality Companion is the Windows operator station for Quest-based
AstralKarateDojo sessions. It is designed for people who are not working inside
the Unity project but still need a clean way to run the participant-facing APK,
watch live state, and keep session control on the operator side.

<div class="action-row">
  <a class="button primary" href="download.md">Install The App</a>
  <a class="button" href="first-session.md">Run A Sussex Session</a>
  <a class="button" href="study-shells.md">Study Shells</a>
  <a class="button" href="troubleshooting.md">Troubleshooting</a>
</div>

## If You Are Here For Sussex

- use the packaged Windows preview unless you are actively changing the repo
- the dedicated Sussex package already includes the bundled Sussex APK
- the app opens directly into `Sussex University experiment mode`
- the operator only needs Windows, a Quest in developer mode, one USB cable, and a shared Wi-Fi network for Wi-Fi ADB

## What The Public Sussex Package Already Includes

- the WPF operator app
- the dedicated Sussex study shell with startup lock enabled
- the bundled Sussex APK mirrored from the Astral build used for the study
- the pinned Quest device profile and study-specific monitoring surface
- the release assets needed to install the whole setup on another machine

## Use This Repo When You Need To

- install the bundled Sussex APK or another supplied study APK directly from Windows
- set Quest CPU and GPU levels before or during a session
- launch the study app and confirm the foreground package
- watch `quest_monitor`, twin-state, and operator log output
- stage runtime-config changes from the desktop side
- keep the participant experience simple while the operator handles the controls

## If This Is Your First Session

Use these three pages in order:

1. [Download](download.md) the Windows app or launcher path you were given.
2. [First Session](first-session.md) to connect the Quest, install the study APK, and launch it.
3. [Study Shells](study-shells.md) if your team gave you a pinned operator surface such as Sussex.

## Choose Your Path

<div class="card-grid">
  <a class="path-card" href="download.md">
    <h3>Install The Launcher</h3>
    <p>Preferred path for operators. The Sussex preview already bundles the pinned APK and opens in the dedicated study shell.</p>
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
    <p>Use the inspector-style editor that mirrors the Astral scene layout for tracked desktop-side changes.</p>
  </a>
  <a class="path-card" href="getting-started.md">
    <h3>Build From Source</h3>
    <p>Use this only if you are validating or changing the repo itself, and remember to pull the Git LFS APK mirror.</p>
  </a>
</div>

## Read These First

- [Download](download.md)
- [First Session](first-session.md)
- [Study Shells](study-shells.md)
- [App Overview](app-overview.md)
- [Monitoring and Control](monitoring-and-control.md)
- [Troubleshooting](troubleshooting.md)

## Project Scope

This repo is public and focused on the Windows app, docs, packaging, and sample
contracts. The AstralKarateDojo Unity repo stays separate. Nothing from that
scene repo needs to be mirrored here for operators to use the desktop app.
