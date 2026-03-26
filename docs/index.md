---
title: Docs Home
description: Start here if you need the Windows operator app for installing, launching, monitoring, and remotely supervising a supplied Quest build.
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
  <a class="button" href="first-session.md">Run A First Session</a>
  <a class="button" href="study-shells.md">Open A Study Shell</a>
  <a class="button" href="monitoring-and-control.md">Monitoring And Control</a>
</div>

## Use This Repo When You Need To

- install a supplied APK directly onto a Quest from Windows
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
    <p>Preferred path for operators. Use the packaged desktop app when a preview release is available.</p>
  </a>
  <a class="path-card" href="first-session.md">
    <h3>First Session</h3>
    <p>Connect Quest, install the supplied APK, launch it, and verify live monitoring in one pass.</p>
  </a>
  <a class="path-card" href="runtime-config.md">
    <h3>Runtime Config</h3>
    <p>Use the inspector-style editor that mirrors the Astral scene layout for tracked desktop-side changes.</p>
  </a>
  <a class="path-card" href="getting-started.md">
    <h3>Build From Source</h3>
    <p>Use this only if you are validating or changing the repo itself.</p>
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
