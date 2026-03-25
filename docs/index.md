---
title: Docs Home
description: Start here for the public Windows companion repo, build steps, release path, and private/public boundary.
nav_order: 10
---

# Viscereality Companion

Viscereality Companion is the public Windows-facing operator shell for the
Viscereality workflow. It is meant to hold the desktop app surface, sample
session-kit catalogs, onboarding docs, and release downloads while keeping the
coupled oscillator and private twin backend out of the public repo.

## Start Here

- [Getting Started](getting-started.md) — clone, build, run
- [App Overview](app-overview.md) — WPF desktop operator station
- [CLI Reference](cli.md) — command-line tool
- [Download](download.md) — tagged releases and install
- [Private Split](private-split.md) — public/private boundary
- [Troubleshooting](troubleshooting.md)

## What This Repo Gives You

- a buildable WPF desktop shell with 24 operator commands
- a `viscereality` CLI tool mirroring WPF capabilities
- LSL bidirectional twin mode — remote control Quest from Windows
- sample Quest Session Kit catalogs reused from the phone companion contract
- real Windows ADB transport and LSL P/Invoke services
- public twin-mode command wiring with LSL outlet/inlet bridge
- GitHub Pages and release automation

## What It Does Not Ship

- coupled oscillator code
- private twin orchestration runtime
- private APK payloads
- study-specific session presets
