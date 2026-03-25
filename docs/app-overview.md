---
title: App Overview
description: The Windows shell layout and how it maps to the source projects.
nav_order: 30
---

# App Overview

The desktop shell is organized around the same operator tasks already present in
the Quest-side and phone-side workflows.

## Session

- Quest endpoint entry
- USB ADB probe and Wi-Fi bootstrap buttons
- live headset status for install state, foreground package, battery, and CPU/GPU levels
- session manifest export

## Quest Library

- app target selection
- direct APK path selection for the chosen target
- ordered bundle selection
- hotload preset selection
- device profile selection
- launch, foreground inspection, and CPU/GPU control actions

## Oscillator Config

- public profile selection and catalog metadata
- editable config sections for stretch, dimensions, driver overrides, visuals,
  motion, coupling, and diagnostics
- config export and publish actions that keep the backend boundary explicit

## Twin Mode

The first working version is a remote-only mode:

- headset-side config control remains disabled
- runtime preset and oscillator config changes are tracked in the Windows app
- the remote-only toggle is part of the operator workflow for supervised research sessions

## LSL Monitor

- stream name, type, and channel contract
- live preview value
- sample-rate and reconnect status surface

## Diagnostics

- utility actions mirrored from the phone companion
- rolling operator log

## Relation To The Source Repos

- `Viscereality` supplies the Quest-side concepts, LSL flow, twin-mode boundary, and oscillator config field groups.
- `AndroidPhoneQuestCompanion` supplies the session-kit catalog shape, Quest control operator flow, and monitor contract.
- `PolarH10` supplies the public-repo posture: Windows app, onboarding docs, Pages site, and tagged-release download path.
