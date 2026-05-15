# Emil's Claude Launchpad

A small Windows tray app that brings back a full workspace of Claude Code chats in one click. Each preset opens a Windows Terminal tab in the right folder, with the right title and color, and runs `claude --resume <session-id> "/remote-control"` for you. So when your terminal crashes (or you reboot, or you just close the wrong window like I keep doing), you get every chat back without the usual hassle of opening WT, finding each project folder, hunting down the right chat with Ctrl+A, and re-typing `/remote-control` across all of them.

Yeah, that workflow was getting wayyyy too long. So I made this.

## What it does

You set up "groups" once, from the editor in the app. A group is just a bunch of Claude Code chats that belong together (the backend chat + the frontend chat + the scratch one, say). For each chat you pick a color, a title, the cwd it lives in, and any extra args you want.

Then from the tray icon, you click a group, and boom: one WT window opens, every tab lands at the right folder, claude resumes the right session in each one, and `/remote-control` is already typed in. You're back in the exact state you left.

Honestly it's the kind of tiny thing that doesn't sound like much until you've lost an entire workspace 5 times in a row and finally got fed up :>

## Install

Grab `Setup.exe` from the [latest release](../../releases/latest) and run it. The tray icon shows up and you're done.

First time around, Windows SmartScreen will warn you because the build is unsigned (yeah, I haven't paid for a code signing cert and probably won't anytime soon). Click "More info" → "Run anyway."

Config lives at `%APPDATA%\EmilsClaudeLaunchpad\presets.json` and survives uninstall, so reinstalling won't nuke your groups.

## Build it yourself

```powershell
dotnet build
dotnet run --project src\EmilsClaudeLaunchpad
```

## Package a release locally

```powershell
.\build\publish.ps1 0.1.0
```

Spits out `Releases\Setup.exe`, self-contained with .NET 10 bundled. The binary's around 60 MB because WinForms doesn't play nice with `PublishTrimmed`, so size is what it is.

## Docs

- [`docs/plan.md`](docs/plan.md) — the original design + every architectural decision with the reasoning behind it. Read this if you want to know why something is the way it is.
- [`docs/improvements.md`](docs/improvements.md) — backlog of features that could be added later, ranked roughly by how much they'd actually help.
