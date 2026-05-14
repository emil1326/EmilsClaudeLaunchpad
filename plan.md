# Emil's Claude Launchpad — Implementation Plan

## Context

**Problem.** Emil runs multiple Claude Code sessions across different projects in Windows Terminal tabs. When his terminal restarts (or he reboots), he loses all sessions and has to manually: open WT, navigate to each project folder, run `claude --resume`, find the right chat with Ctrl+A, open it, then re-type `/remote-control` so it "works correctly." Doing this across N projects takes a long time and he doesn't always remember to redo it everywhere.

**Goal.** A small Windows tray app — `Emil's Claude Launchpad` — that restores a full workspace of Claude sessions in one click. Each preset = one WT tab (or group of tabs) with the right title, color, cwd, and a `claude --resume <session-id> "/remote-control"` invocation, so resuming + re-arming remote-control happens in a single launch with no manual steps.

**Why this approach.** `claude` accepts a positional prompt argument *together with* `--resume <id>`, so `claude --resume <uuid> "/remote-control"` natively does the "resume that session AND send /remote-control as the next message" workflow Emil currently does by hand. That removes the need for SendKeys, focus-stealing tricks, or any post-launch injection.

## Decisions (locked in with the user)

- **Target framework:** `net10.0-windows` (.NET 10 SDK already installed on this machine; .NET 8 SDK is not).
- **Default shell in tabs:** `powershell.exe` (Windows PowerShell 5.1 — confirmed on PATH). `pwsh` is NOT on PATH; presets may override `shell` per-tab if PS7 gets installed later.
- **Session strategy:** every preset pins a stable `session-id` (UUID) plus the cwd it belongs to. The default `initialPrompt` is `/remote-control`. So one click per preset = exactly Emil's current manual workflow, automated.
- **Tech:** C# WinForms tray app (NotifyIcon + small popup form). Single-instance via named Mutex. Autostart via `HKCU\...\Run` (toggleable from inside the form).
- **UI surface:** *one* surface — the popup `LauncherForm`. **Right-click on the tray icon opens the form** (overriding the Windows-default "show context menu" behavior). Left-click does the same thing (convenience). There is no separate text-based ContextMenuStrip — every action lives as a control inside the form (per-session launch buttons, "Launch all", "Edit config", "Reload", "Autostart at login" checkbox, "Exit").
- **`workingDir` = session-origin cwd (load-bearing).** `claude --resume <session-id>` only finds the session when run from the folder where it was originally created. The launcher relies on `-d <workingDir>` in the `wt new-tab` invocation to put the tab in that folder before powershell runs `claude`. **If `workingDir` doesn't match the session's origin folder, `--resume` will silently fail to find that session.** Config docs make this explicit; `SessionLauncher` watches for the claude resume-failed exit pattern (best-effort) and surfaces a balloon tip pointing at the likely culprit.
- **Command injection — SendKeys is OUT.** Argv-only. Optional `preCommands` (joined with `;` and prepended in the `powershell -Command` payload) cover the env-setup case.
- **Packaging & distribution: Velopack + GitHub Releases.** App is published as `net10.0-windows`, `win-x64`, **self-contained**, single-file. **Trimming was disabled** — `NETSDK1175` blocks `PublishTrimmed=true` for WinForms in .NET 10 SDK 10.0.201. The plan's Open TODO #6 anticipated this fallback; Setup.exe lands at ~50 MB instead of ~30-40 MB. Velopack wraps the publish output into a `Setup.exe` + delta packages. Releases live on a **public GitHub repo** (`EmilsClaudeLaunchpad`); a GitHub Actions workflow builds + publishes a new Velopack release on every tag push. The app checks the GitHub Releases feed on startup and prompts to apply updates — subsequent updates are MB-scale deltas, not full reinstalls.

## High-level architecture

```
Tray icon (NotifyIcon)
   │
   ├─ Right-click  ─┐
   └─ Left-click   ─┴─→ LauncherForm (the ONLY UI surface)
                            │
                            ├─ Per-session launch buttons (tinted to tabColor)
                            ├─ "Launch all"
                            ├─ "Edit config"  /  "Reload"
                            ├─ "Autostart at login" checkbox
                            └─ "Exit"
                            │
                            ▼
                    SessionLauncher.Launch(preset)
                            │
                ┌───────────┴────────────┐
                ▼                        ▼
     ClaudeCommandBuilder       WtCommandBuilder
     (build pwsh -Command       (build ArgumentList for
      payload, single-quote      wt.exe — new-tab blocks
      safe)                      separated by literal ";")
                            │
                            ▼
                ProcessStartInfo { FileName="wt.exe",
                                   ArgumentList=[...],
                                   UseShellExecute=false }
                Process.Start(...)
```

## Project layout

```
F:\vsCode\EmilsClaudeLaunchpad\
  EmilsClaudeLaunchpad.sln
  README.md
  .gitignore
  .github\
    workflows\
      release.yml                       // build + vpk pack + GitHub Release on tag push
  build\
    publish.ps1                         // local helper: dotnet publish + vpk pack
  src\
    EmilsClaudeLaunchpad\
      EmilsClaudeLaunchpad.csproj      // net10.0-windows, UseWindowsForms=true,
                                        //   OutputType=WinExe, Nullable=enable, ApplicationIcon,
                                        //   PackageReference Velopack, PublishTrimmed=true,
                                        //   TrimMode=partial, RuntimeIdentifier=win-x64
      Program.cs                        // VelopackApp.Build().Run() FIRST, then STAThread Main,
                                        //   mutex, Application.Run(TrayAppContext)
      TrayAppContext.cs                 // NotifyIcon (no ContextMenuStrip — both clicks open form)
      LauncherForm.cs / .Designer.cs    // popup form, the only UI surface
      Config\
        PresetsConfig.cs                // root POCO ({ schemaVersion, settings, sessions[] })
        SessionPreset.cs                // { id, title, kind (Single|Group), tab?, tabs?, window? }
        TabSpec.cs                      // { title, tabColor, wtProfile, workingDir, shell,
                                        //   preCommands[], claudeArgs[], initialPrompt }
        ConfigStore.cs                  // Load/Save %APPDATA%\EmilsClaudeLaunchpad\presets.json,
                                        //   seed-on-first-run, GetConfigPath() for "Edit config"
      Launching\
        ClaudeCommandBuilder.cs         // BuildPwshPayload(TabSpec) -> single string for -Command
        WtCommandBuilder.cs             // BuildArgumentList(SessionPreset) -> IReadOnlyList<string>
        SessionLauncher.cs              // Launch(preset), LaunchAll(presets) with small inter-launch delay
      Startup\
        SingleInstance.cs               // static Mutex "Local\EmilsClaudeLaunchpad"
        AutoStartManager.cs             // wraps Velopack's startup-shortcut API (HKCU Run fallback)
      Update\
        UpdateManager.cs                // wraps Velopack UpdateManager: check, download, apply
      Resources\
        tray.ico                        // placeholder OK for v1 (TODO: real icon later)
        app.ico
```

## The load-bearing detail: `wt.exe` invocation

Always use `ProcessStartInfo.ArgumentList` (per-element escaping by .NET → CommandLineToArgvW rules → `;` passes through as a literal arg → wt.exe's own parser treats it as the tab/window separator). Never use `Arguments` (the single string variant) — escaping gets fragile fast.

**Single-tab launch** (one `wt.exe` invocation, one new window with one tab):

```csharp
ArgumentList = [
  "new-tab",
  "--title",     "Backend",
  "--tabColor",  "#FF8800",
  "-d",          @"F:\vsCode\my-backend",
  "powershell", "-NoExit", "-Command",
    "claude --resume 7c1e0c2a-1111-2222-3333-444444444444 '/remote-control'"
]
```

**Group launch** (one window, multiple tabs — `;` as standalone arg-list entries):

```csharp
ArgumentList = [
  "new-tab", "--title", "Backend",  "--tabColor", "#FF8800",
             "-d", @"F:\vsCode\my-backend",
             "powershell", "-NoExit", "-Command",
               "claude --resume <uuid-1> '/remote-control'",
  ";",
  "new-tab", "--title", "Frontend", "--tabColor", "#0088FF",
             "-d", @"F:\vsCode\my-frontend",
             "powershell", "-NoExit", "-Command",
               "claude --resume <uuid-2> '/remote-control'",
  ";",
  "new-tab", "--title", "Scratch",  "--tabColor", "#888888",
             "-d", @"F:\vsCode",
             "powershell", "-NoExit", "-Command",
               "claude '/remote-control'"
]
```

**PowerShell quoting inside `-Command` payload.** The payload is one string. Inside it, the user-supplied `initialPrompt` is wrapped in single quotes and any internal `'` is doubled (`''`). `claudeArgs` are joined with spaces; each arg is single-quoted only if it contains whitespace or special chars (UUIDs and flags don't need quoting). Keep this in a single `PwshSingleQuote(string)` helper in `ClaudeCommandBuilder` so the quoting rule lives in one place.

## Config schema

**Location:** `%APPDATA%\EmilsClaudeLaunchpad\presets.json` (resolved via `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)`). Auto-created with seed content on first launch.

```json
{
  "schemaVersion": 1,
  "settings": {
    "defaultShell": "powershell",
    "autostart": false
  },
  "sessions": [
    {
      "id": "backend-resume",
      "title": "Backend (resume + rc)",
      "kind": "single",
      "tab": {
        "title": "Backend",
        "tabColor": "#FF8800",
        "wtProfile": null,
        "workingDir": "F:\\vsCode\\my-backend",
        "shell": "powershell",
        "preCommands": [],
        "claudeArgs": ["--resume", "7c1e0c2a-1111-2222-3333-444444444444"],
        "initialPrompt": "/remote-control"
      }
    },
    {
      "id": "fullstack-group",
      "title": "Full-stack workspace",
      "kind": "group",
      "window": null,
      "tabs": [
        {
          "title": "Backend",
          "tabColor": "#FF8800",
          "workingDir": "F:\\vsCode\\my-backend",
          "claudeArgs": ["--resume", "7c1e0c2a-..."],
          "initialPrompt": "/remote-control"
        },
        {
          "title": "Frontend",
          "tabColor": "#0088FF",
          "workingDir": "F:\\vsCode\\my-frontend",
          "claudeArgs": ["--resume", "8e2f1d3b-..."],
          "initialPrompt": "/remote-control"
        },
        {
          "title": "Scratch",
          "tabColor": "#888888",
          "workingDir": "F:\\vsCode",
          "claudeArgs": [],
          "initialPrompt": "/remote-control"
        }
      ]
    }
  ]
}
```

Schema rules:
- `kind: "single"` reads `tab`; `kind: "group"` reads `tabs[]`. A group = one `wt.exe` invocation = one WT window with N tabs.
- Each `TabSpec` field except `workingDir` and `title` is optional. `claudeArgs` empty + `initialPrompt` null = bare `claude`.
- `shell` inherits from `settings.defaultShell` when null.
- **`workingDir` must point at the folder where the target claude session was originally created** when using `--resume <id>` — `claude --resume` only finds sessions belonging to the current cwd. A mismatch will cause `--resume` to silently fail to locate the session (claude will likely start a fresh interactive session instead). There's no programmatic check for this in v1; document it in the seed config's top-of-file comment and let the balloon-tip path catch failures empirically.
- Schema parsed via `System.Text.Json` with `JsonStringEnumConverter` and case-insensitive property naming. `schemaVersion` exists from day one to make future migrations painless.

## File-by-file responsibilities

**`Program.cs`** — `[STAThread] Main(string[] args)`. **First line of `Main` is `VelopackApp.Build().Run(args)`** — Velopack uses CLI hooks (`--squirrel-install`, `--squirrel-updated`, `--squirrel-uninstall`, etc.) to wire first-run setup, post-update tasks, and uninstall cleanup; this call must run *before* any WinForms initialization. After that: `SingleInstance.TryAcquire("EmilsClaudeLaunchpad")` → exit silently if already held. Then `ApplicationConfiguration.Initialize()` → `Application.Run(new TrayAppContext())`. The `TrayAppContext` kicks off an async `UpdateManager.CheckOnStartup()` (non-blocking, balloon tip if an update is available).

**`TrayAppContext.cs : ApplicationContext`** — Owns the `NotifyIcon`. **No `ContextMenuStrip`.** Both `MouseClick` (any button) and the default left-click activation route to `ShowLauncher()`, which shows the single `LauncherForm` instance positioned near the cursor. To suppress the OS-default right-click context menu while keeping the right-click event, the `NotifyIcon`'s `ContextMenuStrip` property is left null and `MouseUp` is hooked directly (filtering on `MouseButtons.Left | MouseButtons.Right`). Surfaces launch failures (e.g. `wt.exe` not found) via balloon tip; never crashes.

**`LauncherForm.cs`** — Small popup form (`FormBorderStyle = FixedToolWindow`, `ShowInTaskbar = false`, `TopMost = true`, `StartPosition = Manual`). On show, position near the tray icon using `Cursor.Position` + screen working-area clamping; `Deactivate` handler hides (not disposes) the form so it behaves popup-like. Layout, top to bottom:
- A `FlowLayoutPanel` of `Button`s — one per session, button background tinted from the session's primary `tabColor` (group sessions use the first tab's color, or a left-edge color strip showing all colors).
- Separator.
- `Launch all` button (wide).
- A row with `Edit config` and `Reload` buttons.
- Separator.
- `Autostart at login` checkbox (state read from `AutoStartManager.IsEnabled`, toggling calls `Enable()`/`Disable()`).
- `Check for updates` button (calls `UpdateManager.CheckNow()`; shows balloon tip "up to date" or "update available — apply now?" with confirm).
- A version label (small, bottom-right): `v<assembly-version>`.
- `Exit` button (calls `Application.Exit()`).

The form is built once, then re-populated from `ConfigStore.Load()` every time it's shown (cheap, picks up config edits without restart).

**`Config\ConfigStore.cs`** — `Load()` returns `PresetsConfig`; auto-creates the dir + seed file if missing. `Save(PresetsConfig)` writes indented JSON. `GetConfigPath()` exposes the path for the "Edit config" menu (opened via `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })` → default editor). On parse failure: log + balloon-tip the exception, return last good config in memory if available, else return an empty-but-valid config so the app stays usable.

**`Launching\ClaudeCommandBuilder.cs`** — Pure function `BuildPwshPayload(TabSpec) : string`. Steps: (1) emit `preCommands` joined with `; ` (empty if none); (2) emit `claude` followed by `string.Join(' ', tab.ClaudeArgs.Select(MaybeQuote))`; (3) if `initialPrompt` non-null, append ` ` + `PwshSingleQuote(initialPrompt)`. Single point of truth for PowerShell quoting. Easy to unit-test.

**`Launching\WtCommandBuilder.cs`** — Pure function `BuildArgumentList(SessionPreset) : IReadOnlyList<string>`. For `Single`: one `new-tab` block. For `Group`: `new-tab` blocks separated by literal `";"` elements. Per-block emits `new-tab`, `--title <t>`, optional `--tabColor <c>`, optional `--profile <p>`, `-d <workingDir>`, then `<shell>`, `-NoExit`, `-Command`, `<payload>` (from `ClaudeCommandBuilder`). Documented invariant: `;` is never inside a string — always a standalone element.

**`Launching\SessionLauncher.cs`** — `Launch(SessionPreset)` builds a `ProcessStartInfo { FileName = "wt.exe", UseShellExecute = false, CreateNoWindow = true }`, copies args into `ArgumentList`, calls `Process.Start`. Catches `Win32Exception` (wt.exe not found) → balloon tip with remediation. `LaunchAll(IEnumerable<SessionPreset>)` iterates with `Task.Delay(80)` between calls to avoid wt's new-window association race.

**`Startup\SingleInstance.cs`** — Holds a static `Mutex` named `Local\EmilsClaudeLaunchpad` (per-user scope). `TryAcquire(name)` returns false if held; caller exits. Mutex pinned for process lifetime.

**`Startup\AutoStartManager.cs`** — Reads/writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value name `EmilsClaudeLaunchpad`. Exposes `IsEnabled`, `Enable()`, `Disable()`. **The registered path is the Velopack-managed `current` symlink**: `%LOCALAPPDATA%\EmilsClaudeLaunchpad\current\EmilsClaudeLaunchpad.exe` (quoted). Velopack maintains the `current` junction across updates, so the registry value never needs to be rewritten on update. Exe path resolution uses `Environment.ProcessPath`'s parent's parent + `\current\EmilsClaudeLaunchpad.exe`; falls back to `Environment.ProcessPath` directly when running from a non-Velopack-installed build (e.g. `dotnet run` during dev), so the toggle still works in development.

**`Update\UpdateManager.cs`** — Thin wrapper around Velopack's `UpdateManager` configured with the public GitHub Releases feed (`https://github.com/<user>/EmilsClaudeLaunchpad`). Methods: `CheckOnStartup()` (called once shortly after the tray comes up — non-blocking, balloon-tip if an update is found), `CheckNow()` (called from a "Check for updates" item in the form), `ApplyAndRestart(UpdateInfo)` (downloads delta, applies, restarts the app). All errors swallowed + balloon-tipped — never crash the tray over a failed update check. Disabled cleanly when running from `dotnet run` (no Velopack install present) so dev iteration isn't blocked by feed checks.

## Packaging, install, and auto-update

### csproj configuration

`src\EmilsClaudeLaunchpad\EmilsClaudeLaunchpad.csproj` key properties:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ApplicationIcon>Resources\app.ico</ApplicationIcon>

    <!-- Velopack-friendly publish defaults -->
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <PublishTrimmed>true</PublishTrimmed>
    <TrimMode>partial</TrimMode>            <!-- WinForms-safe; full is risky -->
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <Version>0.1.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Velopack" Version="0.*" />
  </ItemGroup>
</Project>
```

WinForms + trimming caveat: even with `TrimMode=partial`, some designer-generated reflection can get trimmed wrong. If a runtime `MissingMethodException` shows up in v0.1, fall back to `PublishTrimmed=false` (back to ~60 MB but reliable). Test before shipping.

### Local build helper — `build\publish.ps1`

```powershell
# Local dev: produce a Velopack package without pushing to GitHub.
$ErrorActionPreference = 'Stop'
$version = $args[0]
if (-not $version) { throw "Usage: .\publish.ps1 <version>  (e.g. 0.1.0)" }

dotnet tool restore                    # gets vpk from .config\dotnet-tools.json
dotnet publish src\EmilsClaudeLaunchpad `
  -c Release -r win-x64 --self-contained `
  -o publish
dotnet vpk pack `
  --packId EmilsClaudeLaunchpad `
  --packVersion $version `
  --packDir publish `
  --mainExe EmilsClaudeLaunchpad.exe `
  --packTitle "Emil's Claude Launchpad"
```

Output: `Releases\Setup.exe` + `Releases\<id>-<version>-full.nupkg` + delta packages on subsequent builds.

`vpk` is pinned via `dotnet-tools.json` (committed) so CI and local builds use the same version.

### GitHub Actions — `.github\workflows\release.yml`

Triggered on tag push (`v*`). Steps:
1. `actions/checkout@v4`
2. `actions/setup-dotnet@v4` with `.NET 10.x`
3. `dotnet tool restore` (installs `vpk` from `dotnet-tools.json`)
4. `dotnet publish src/EmilsClaudeLaunchpad -c Release -r win-x64 --self-contained -o publish`
5. `dotnet vpk download github --repoUrl <repo-url> --token ${{ secrets.GITHUB_TOKEN }}` — pulls previous releases so vpk can compute deltas
6. `dotnet vpk pack --packId EmilsClaudeLaunchpad --packVersion ${{ github.ref_name }}` (strip the leading `v`) `--packDir publish --mainExe EmilsClaudeLaunchpad.exe`
7. `dotnet vpk upload github --repoUrl <repo-url> --publish --releaseName "${{ github.ref_name }}" --tag ${{ github.ref_name }} --token ${{ secrets.GITHUB_TOKEN }}`

Uses the auto-provisioned `GITHUB_TOKEN` (no manual secret setup). Public repo → free Actions minutes.

### Install flow (user perspective)

1. Visit `https://github.com/<user>/EmilsClaudeLaunchpad/releases/latest`, download `Setup.exe`.
2. Run `Setup.exe`. Velopack installs the app to `%LOCALAPPDATA%\EmilsClaudeLaunchpad\` (versioned subfolder + a `current` junction pointing at it) and runs it. Tray icon appears, seed `presets.json` is written to `%APPDATA%\EmilsClaudeLaunchpad\`.
3. Open the form (right-click tray), toggle `Autostart at login` → registry entry written, pointing at `%LOCALAPPDATA%\EmilsClaudeLaunchpad\current\EmilsClaudeLaunchpad.exe`.
4. Done. Next reboot, tray comes up automatically.

### Update flow

1. On startup, `UpdateManager.CheckOnStartup()` queries the GitHub Releases feed (a few KB request).
2. If a newer release exists, balloon tip: *"Update v0.2.0 available — click to apply"*.
3. Clicking the balloon (or `Check for updates` in the form) downloads the delta package (typically 2-5 MB), applies it, restarts the app. The `current` junction now points at the new version folder; the autostart registry entry is unchanged and still works.
4. Old version folders are pruned by Velopack (keeps last N by default).

### Uninstall flow

1. Settings → Apps → "Emil's Claude Launchpad" → Uninstall (standard Windows Programs & Features entry, registered by Velopack).
2. Velopack's uninstall hook fires (Program.cs `VelopackApp.Build().OnAfterUninstallFastCallback(...).Run(args)` registers cleanup): removes the autostart registry value, removes the install folder.
3. `%APPDATA%\EmilsClaudeLaunchpad\presets.json` is **left in place** intentionally (config survives uninstall/reinstall). Document this in the README; users who want a clean slate delete it manually.

## Branding notes (per global CLAUDE.md)

- Identifiers / paths / namespaces: `EmilsClaudeLaunchpad` (no apostrophe, no spaces, no `ECL` or other abbreviations).
- User-facing strings: `Emil's Claude Launchpad` — tray tooltip, form title, menu items, README headings.
- Solution / project name: `EmilsClaudeLaunchpad`. Folder: `EmilsClaudeLaunchpad\`. Default tray tooltip: `"Emil's Claude Launchpad"`.

## Critical files to be created

- `F:\vsCode\EmilsClaudeLaunchpad\EmilsClaudeLaunchpad.sln`
- `F:\vsCode\EmilsClaudeLaunchpad\src\EmilsClaudeLaunchpad\EmilsClaudeLaunchpad.csproj` (`net10.0-windows`, `UseWindowsForms=true`, `OutputType=WinExe`)
- `F:\vsCode\EmilsClaudeLaunchpad\src\EmilsClaudeLaunchpad\Program.cs`
- `F:\vsCode\EmilsClaudeLaunchpad\src\EmilsClaudeLaunchpad\TrayAppContext.cs`
- `F:\vsCode\EmilsClaudeLaunchpad\src\EmilsClaudeLaunchpad\LauncherForm.cs` (+ `.Designer.cs`)
- `F:\vsCode\EmilsClaudeLaunchpad\src\EmilsClaudeLaunchpad\Config\{PresetsConfig,SessionPreset,TabSpec,ConfigStore}.cs`
- `F:\vsCode\EmilsClaudeLaunchpad\src\EmilsClaudeLaunchpad\Launching\{ClaudeCommandBuilder,WtCommandBuilder,SessionLauncher}.cs`
- `F:\vsCode\EmilsClaudeLaunchpad\src\EmilsClaudeLaunchpad\Startup\{SingleInstance,AutoStartManager}.cs`
- `F:\vsCode\EmilsClaudeLaunchpad\src\EmilsClaudeLaunchpad\Update\UpdateManager.cs`
- `F:\vsCode\EmilsClaudeLaunchpad\src\EmilsClaudeLaunchpad\Resources\tray.ico` (placeholder)
- `F:\vsCode\EmilsClaudeLaunchpad\.github\workflows\release.yml`
- `F:\vsCode\EmilsClaudeLaunchpad\build\publish.ps1`
- `F:\vsCode\EmilsClaudeLaunchpad\.config\dotnet-tools.json` (pins `vpk`)
- `F:\vsCode\EmilsClaudeLaunchpad\README.md`
- `F:\vsCode\EmilsClaudeLaunchpad\.gitignore` (standard VS / .NET, plus `publish/`, `Releases/`)

## Open TODOs / verify during build

1. **`--tabColor` value format** — expected `#RRGGBB`. Confirm with a one-tab smoke test before relying on multi-tab. If invalid value silently breaks the whole window, the WtCommandBuilder should validate (regex `^#[0-9A-Fa-f]{6}$`) and skip the flag rather than emit a bad one.
2. **wt window-association race** under `LaunchAll`. 80 ms delay is a guess; tune empirically.
3. **Icon** — `tray.ico` is a placeholder for v1. Quiet branding via the form title + tray tooltip is enough at first. Velopack also uses `app.ico` for the `Setup.exe` icon and Programs & Features entry — placeholder OK but worth swapping later.
4. **Single-instance "show on second launch"** — v1: silent exit. v2 candidate: named pipe to ask the running instance to show the launcher. Noted as a known limitation.
5. **Schema validation** — v1 relies on `System.Text.Json` failing on bad shapes. Add a light post-parse pass that checks: every session has a non-empty `id` and `title`, `kind` matches `tab`/`tabs[]` presence, color values are valid hex when present. Surface errors via balloon tip without crashing.
6. **WinForms + trimming compatibility.** `TrimMode=partial` is generally safe for WinForms but designer reflection can occasionally lose methods. First Release build: smoke-test every form interaction. If a `MissingMethodException` shows up, flip `PublishTrimmed` to `false` (back to ~60 MB binary) and ship that instead — reliability beats size.
7. **Velopack CLI flag drift.** Velopack's `vpk` CLI flags have changed across versions (e.g. `--packId` vs older `-u`). Pin via `dotnet-tools.json` so CI + local match, and double-check the exact flag names against the version pinned before committing the workflow.
8. **Repo URL placeholder.** The plan references `https://github.com/<user>/EmilsClaudeLaunchpad` throughout. Replace `<user>` with the actual GitHub handle once the repo is created (one find/replace pass before the first tag push).
9. **Code signing.** v1 ships unsigned. Windows SmartScreen will show a warning the first few downloads until reputation builds up. Acceptable for personal use; a future v2 could buy/use a code-signing cert (cost: $80-200/year). Document in README so users aren't surprised.

## Verification (manual smoke test)

1. **Build & launch.** `dotnet build` then `dotnet run` from `src\EmilsClaudeLaunchpad`. Tray icon appears; no main window; process survives.
2. **Single-instance.** Start a second `EmilsClaudeLaunchpad.exe`; confirm it exits silently and the first tray icon still works.
3. **Config bootstrap.** Delete `%APPDATA%\EmilsClaudeLaunchpad\presets.json`; relaunch; confirm seed file is written and the tray menu lists its sessions.
4. **Single-tab launch.** Tray menu → `Backend (resume + rc)`. Confirm: one WT window, one tab titled "Backend" with orange color, cwd is the configured path, `claude --resume <uuid> "/remote-control"` ran and `/remote-control` was sent as the first new message in the resumed session.
5. **Group launch.** Tray menu → `Full-stack workspace`. Confirm: one WT window with three tabs, each with correct title/color/cwd, each running the configured claude invocation.
6. **Launch all.** Confirm every preset spawns in its own window (or its own group window), no errors, app stays healthy.
7. **Autostart toggle.** Enable → check `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` has `EmilsClaudeLaunchpad` = quoted exe path. Disable → value gone. Log out + back in with it enabled → tray icon comes up.
8. **Edit/reload config.** `Edit config` opens JSON in the default editor. Edit a session title. Click `Reload`. Confirm menu reflects change.
9. **Error paths.** (a) Point a `workingDir` at a non-existent path → tab opens but powershell errors; app stays healthy. (b) Use a bogus session-id in `claudeArgs` → claude's own error surfaces in the tab. (c) Temporarily rename `wt.exe` off PATH → tray balloon tip explains the failure.
10. **Quoting edge cases.** Test `initialPrompt` values with apostrophes (e.g. `"don't break this"`) and with backticks. Tab should launch cleanly with the prompt visible verbatim in claude.
11. **Local Velopack pack.** Run `.\build\publish.ps1 0.1.0` from a clean checkout; confirm `Releases\Setup.exe` is produced (~30-40 MB), runs, and installs the app into `%LOCALAPPDATA%\EmilsClaudeLaunchpad\current\`. Tray icon comes up, version label in the form reads `v0.1.0`.
12. **GitHub Actions release.** Push tag `v0.1.0`. Confirm: workflow runs green, a new GitHub Release is created with `Setup.exe` + `*-full.nupkg` attached, and the release notes draft appears on the repo's Releases page.
13. **Auto-update end-to-end.** With v0.1.0 installed, bump csproj `Version` to `0.2.0`, push tag `v0.2.0`. After the release publishes, restart the local app. Confirm: balloon tip says update available, clicking it downloads a small delta (NOT a full 30 MB), app restarts at v0.2.0, autostart registry entry still works (still points at the `current` junction).
14. **Uninstall.** Settings → Apps → Uninstall "Emil's Claude Launchpad". Confirm: install folder gone, autostart registry value gone, `%APPDATA%\EmilsClaudeLaunchpad\presets.json` preserved.

## Out of scope for v1 (deliberate)

- GUI editor for presets (v1 = edit JSON in default editor).
- Multiple-instance "wake the running app" behavior.
- SendKeys / post-launch claude command injection.
- Per-session log streaming or status monitoring.
- Real custom icon assets (placeholder is fine).
- Code signing of `Setup.exe` (accept SmartScreen warning for v1).
- Unit tests (optional; `WtCommandBuilder` and `ClaudeCommandBuilder` are the only worthwhile targets and can be added later).
