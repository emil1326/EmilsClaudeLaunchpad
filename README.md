# Emil's Claude Launchpad

A tiny Windows tray app that brings back a workspace of Claude Code sessions in one click. Each preset opens a Windows Terminal tab with the right cwd, title, color, and a `claude --resume <session-id> "/remote_control"` invocation, so the terminal restarts that used to kill the day are now a one-click recovery.

## Status

Pre-release. See [`plan.md`](./plan.md) for the full design and verification plan.

## Build

```powershell
dotnet build
dotnet run --project src\EmilsClaudeLaunchpad
```

## Package a release locally

```powershell
.\build\publish.ps1 0.1.0
```

Produces `Releases\Setup.exe` (~30-40 MB, self-contained, .NET 10 bundled).

## Install (end-user)

Download `Setup.exe` from the [latest GitHub release](../../releases/latest), run it, and the tray icon appears.

First-run SmartScreen warning is expected — the build is unsigned. Click "More info" → "Run anyway."

Config lives at `%APPDATA%\EmilsClaudeLaunchpad\presets.json` and survives uninstall.
