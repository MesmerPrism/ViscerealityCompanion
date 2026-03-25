---
title: Download
description: Tagged releases publish the Windows portable build here.
nav_order: 40
---

# Download

This repo is set up so that pushing a tag like `v0.1.0` triggers the
`release-desktop.yml` workflow on GitHub Actions.

Each tagged release publishes:

- `ViscerealityCompanion-win-x64.zip`
- `SHA256SUMS.txt`

## Intended Install Path

1. Open the latest GitHub release.
2. Download the `win-x64` zip.
3. Extract it to a writable folder.
4. Launch `ViscerealityCompanion.App.exe`.

## Current State

The release pipeline is scaffolded in this repo. Once the public GitHub remote
is connected and tags are pushed, this page becomes the public download entry
point.
