---
title: Troubleshooting
description: Common setup and build issues for the public shell.
nav_order: 60
---

# Troubleshooting

## The app says the sample catalog could not be found

Build and run from the repo root, or use the normal `dotnet run --project`
command shown in [Getting Started](getting-started.md). The app looks for the
copied sample catalog in its output folder first, then falls back to the repo
copy under `samples/quest-session-kit/`.

## The app builds but only shows preview transport messages

That is expected in the public repo. Quest control and twin mode are wired as
public contracts with preview implementations until a local private overlay is
attached.

## The docs site does not build

Confirm Node.js is installed, then run:

```powershell
npm install
npm run pages:build
```

## I need the live twin backend

Keep that out of the public repo. Attach it through a local-only project or
assembly overlay under one of the ignored private paths.
