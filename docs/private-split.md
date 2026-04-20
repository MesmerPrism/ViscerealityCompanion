---
title: Project Scope
description: What this public repo covers, what the Windows companion owns, and how the current Sussex-focused release fits into the broader Viscereality roadmap.
summary: This repo covers the public Windows companion, docs, packaging, and study-shell delivery. The current release is Sussex-first, with broader general-purpose tooling planned next.
nav_label: Project Scope
nav_group: Reference
nav_order: 90
---

# Project Scope

Viscereality Companion is the public Windows-side operator repo around the
broader Viscereality system. It is the part researchers, labs, and operators
should use when they need a stable Windows control surface.

## This Repo Owns

- the WPF desktop app
- the CLI
- Windows ADB control
- LSL monitoring and outlet plumbing
- twin command and state transport on the desktop side
- sample session-kit contracts
- public onboarding docs, Pages, and release automation

## Current Public Release Focus

- the current release path is optimized for the Sussex collaboration workflow
- the public Windows package is meant to give researchers a one-stop install,
  launch, monitoring, and operator workflow
- the dedicated Sussex shell is the first hardened study-specific surface
- broader general-purpose multi-study tooling comes after the Sussex-first
  path is stable

## Practical Rule

If you need Windows setup, study-shell packaging, operator workflow, or public
docs, work in this repo. If you need to change the participant-facing Quest
runtime itself, do that in the runtime project and then refresh the approved
APK here.
