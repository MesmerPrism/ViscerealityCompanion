---
title: Relation To AstralKarateDojo
description: What lives in this public Windows repo and what stays in the separate AstralKarateDojo Unity repo.
summary: This repo is public and focused on the operator app. The Unity scene and study APK development stay in AstralKarateDojo.
nav_label: Relation To Astral
nav_group: Reference
nav_order: 90
---

# Relation To AstralKarateDojo

Viscereality Companion is not a mirror of the Unity project. It is the public
Windows-side operator repo around that project.

## This Repo Owns

- the WPF desktop app
- the CLI
- Windows ADB control
- LSL monitoring and outlet plumbing
- twin command and state transport on the desktop side
- sample session-kit contracts
- public onboarding docs, Pages, and release automation

## AstralKarateDojo Owns

- the Unity scene
- the Quest runtime and scene-local wiring
- study APK development
- scene-internal runtime code and scene assets
- any study-specific content that belongs with the runtime repo

## Practical Rule

If you need to run sessions from Windows, work in this repo. If you need to
change the Quest scene or APK behavior, work in `AstralKarateDojo`.
