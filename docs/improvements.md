# Improvements backlog

Stuff that's not in the app yet but probably should be at some point. Ranked roughly by how much pain they'd save vs. how much work they'd take. Not a roadmap, just a parking lot.

## Schema fields that already exist but the editor doesn't expose

These are kind of free wins. The plumbing is already there, just no UI on top.

### `WtProfile` per tab
The field exists in `Config/TabPreset.cs` and `Launching/WtCommandBuilder.cs` already forwards it to `wt.exe --profile`. But there's no textbox/dropdown for it in `EditorForm`, so you can only set it by editing JSON. Would be nice to pick from the installed WT profiles directly.

### `PreCommands` per tab
Same story. `Config/TabPreset.cs` defines it, `ClaudeCommandBuilder.cs` runs them before `claude`, but the editor pretends they don't exist. Useful for `conda activate`, `nvm use`, or really any setup line you want to run before claude starts. A small multi-line textbox would do the job.

### `Window` per group
`GroupPreset.Window` is round-tripped by `ConfigStore` and `WtCommandBuilder` forwards it as `wt --window <id>`, but there's no UI for it. Setting it would let you target an existing terminal window by id/name instead of always opening a fresh one. Niche but useful if you want everything in one WT instance.

## Robustness / "stop silently failing" stuff

### Stale chat warning
If a `TabPreset.SessionId` points to a `.jsonl` that doesn't exist anymore in `~/.claude/projects/`, right now you just get a tab where claude starts a fresh empty session and you don't realize until you look. Should at least warn in the launcher (red dot on the group?) and in the editor (banner on the tab).

Already noted as a v0.1.2 candidate in [`plan.md`](./plan.md).

### Backup before save
`ConfigStore.Save()` is a direct `File.WriteAllText`. Zero safety net. If the editor has a bug that writes garbage, you've lost your config. A rotating `presets.json.bak` (keep last 2-3) would be cheap insurance.

### `workingDir` exists check
If you renamed/moved a project folder, the tab will open, powershell will complain that the cwd is gone, and claude `--resume` will fail to find anything. A visual warning in the launcher button (red border? warning icon?) when the dir is missing would catch this before launch.

## Quality of life

### Global hotkey to pop the launcher
Right now you have to aim at the tray icon, which is a tiny target. A `RegisterHotKey` Win32 call for something like `Ctrl+Alt+L` (configurable in settings) would be wayyyy faster.

### Reorder groups & tabs in the editor
Currently the order is whatever the JSON file has, so reordering means editing the JSON. Up/down arrow buttons on each group header (and each tab row) would do it.

### Second-launch behavior
`Startup/SingleInstance.cs` exits silently when there's already an instance running. So if you double-click the .exe a second time, nothing visible happens. A tiny named-pipe protocol where the second instance tells the first one "show your launcher and die" would be much friendlier.

### Search/filter in the editor's chat list
Not urgent right now, but if you accumulate 50+ chats it'll get hard to find the right one. A textbox above "Available Claude chats" that filters live (by folder name, preview text, or session id).

### Settings dialog
`AppSettings.DefaultShell` is editable in JSON only. A small settings dialog (shell, autostart, hotkey, balloon notifications on/off, backup retention) would clean that up.

## Smaller stuff

### "Duplicate group" in the editor
For when you want a variant of an existing group (same set of tabs, different name, different color). Right-click a group header → "Duplicate".

### "Show me the wt.exe command"
A debug button that dumps the exact `ProcessStartInfo.ArgumentList` that would be passed to `wt.exe`. Useful when launches misbehave and you want to copy-paste the command into a terminal to see what's wrong.

### Per-group keyboard shortcuts inside the launcher
While the launcher form is open, pressing `1`-`9` launches that group. Faster than clicking when you know which one you want.

---

## What I'd actually do first

If I had to pick three for the next pass: **#1 (WtProfile) + #2 (PreCommands)** because they're dead code in the schema and the editor pretends they don't exist, then **#4 (backup before save)** because losing a config to a save bug would be infuriating and the fix is like 5 lines.

Stale chat warning (#3) is the next obvious one but it's a bit more UI work.
