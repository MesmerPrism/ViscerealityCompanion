---
title: Private Split
description: What stays in the public repo and what belongs in a local-only overlay.
nav_order: 50
---

# Private Split

The public repo is for the operator surface, onboarding, and safe sample
contracts. The config contract stays public; only the live runtime dynamics and
transport stay out of it.

## Public

- WPF UI shell
- session-kit manifest format
- oscillator config document format
- public sample oscillator profiles
- Windows-side ADB install, launch, status polling, and device-property control
- docs and release automation
- preview transport implementations
- manifest export and operator logs

## Private

- job-system coupling dynamics implementation
- live bidirectional twin-mode backend and runtime handoff
- any research-only or study-specific compute code
- private APKs and sensitive presets

## Suggested Local Overlay

Use one of the ignored paths:

- `src/ViscerealityCompanion.Private/`
- `tests/ViscerealityCompanion.Private.Tests/`
- `private/`

The public shell already exposes the twin command contract, hotload presets,
and editable oscillator config surface, so a local overlay can attach the live
backend without changing the public repo API.
