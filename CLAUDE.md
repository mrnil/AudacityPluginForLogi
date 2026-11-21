# Audacity plugin (Logi Actions SDK)

Logi Actions SDK plugin (C#, net10.0) that drives Audacity from Loupedeck / MX Creative Console hardware over Audacity's `mod-script-pipe` scripting module. Windows and macOS. User-facing docs: README.md.

## Architecture
- `src/AudacityPlugin.cs`: plugin root. Owns the single `AudacityPipeClient`; sets plugin status from its connection state.
- `src/AudacityApplication.cs`: links the plugin to Audacity (process `Audacity`, bundle `org.audacityteam.audacity`).
- `src/Actions/AudacityCommand.cs`: all buttons as parameters of one `PluginDynamicCommand`, table-driven (`Defs`). Parameter key = Audacity scripting command id.
- `src/Adjustments/`: Scrub, Zoom and Gain dials; ticks go through `TickCoalescer`.
- `src/Pipe/`: `AudacityPipeClient` (one connection, one `Channel` queue, so request/response pairs never interleave; 2 s poll reconnects, or while connected checks `Plugin.IsApplicationRunning()`; never send Audacity anything the user didn't trigger, because any scripting command closes open menus; queue capped at 32; a 10 s response timeout disconnects so a late reply can't be read as the next command's), response parsing in `PipeResponseReader`, `IPipeTransport` with `WindowsPipeTransport` (`\\.\pipe\ToSrvPipe`/`FromSrvPipe`) and `UnixFifoTransport` (`/tmp/audacity_script_pipe.{to,from}.<uid>`).
- State is in memory only. Transport state and gain are optimistic: the pipe has no state query or events.
- Icons: `src/Images/<name>[-active|-off].png`, 80x80, embedded resources; regenerate with `python tools/make_icons.py src` (needs Pillow).

## Commands (run from repo root)
- Build: `dotnet build -c Debug`. Side effects: copies `src/package/` into `bin/Debug/`, writes `%LocalAppData%\Logi\LogiPluginService\Plugins\AudacityPlugin.link` and reloads the plugin in Logi Plugin Service.
- Compile only, no side effects: `dotnet msbuild src/AudacityPlugin.csproj -restore -t:Compile -p:Configuration=Debug -p:SolutionDir=<repo root>/`
- Always build from the root solution or pass `SolutionDir`. Building `src/AudacityPlugin.csproj` on its own puts intermediates in `src/obj/`, whose generated `.cs` files then break later builds with duplicate-attribute errors (fix: delete `src/obj/`).
- Lint: part of the build. Analyzers (`latest-recommended`) and `.editorconfig` style rules are enforced with warnings as errors, in both the plugin and test projects.
- Test: `dotnet test --project tests/AudacityPlugin.Tests` (xUnit v3 on Microsoft.Testing.Platform, set in `global.json`). Don't run bare `dotnet test` at the root: it builds the plugin project, which triggers the deploy side effects above.
- Tests compile plugin sources as linked files (see `tests/AudacityPlugin.Tests/AudacityPlugin.Tests.csproj`), not via ProjectReference; add a folder there when its classes need tests.
- Dependency audit: `dotnet list src/AudacityPlugin.csproj package --vulnerable --include-transitive` and the same for `tests/AudacityPlugin.Tests`. The plugin has no NuGet packages, so DLLs bundled with `PluginApi.dll` are not covered.
- Secret scan: `gitleaks git --redact` (history); the pre-commit hook scans staged changes.
- Hooks: after cloning, run `git config core.hooksPath .githooks` (gitleaks pre-commit, Conventional Commits commit-msg).
- CI: `.github/workflows/build.yml` (windows-latest). `PluginApi.dll` isn't on NuGet and must never be committed or shipped (Logitech proprietary), so CI silently installs Logi Options+ from Logitech's download, checks its Authenticode signature, then builds Release and tests. The plugin's `PluginApi` reference is `Private=false`, and CI fails if the DLL appears in `bin/Release`. Actions are pinned to commit SHAs.
- Package / install: `logiplugintool pack ./bin/Release ./Audacity.lplug4`, then `logiplugintool install ./Audacity.lplug4`.
- Manifest check: after any change to `LoupedeckPackage.yaml`, pack and run `logiplugintool verify <package>.lplug4` (it rejects unknown `supportedDevices` names); `logiplugintool metadata <package>.lplug4` shows what was parsed.
- Plugin log: `%LocalAppData%\Logi\LogiPluginService\Logs\plugin_logs\Audacity.log`.
- Check command ids against the installed Audacity (Audacity running): `python tools/verify_commands.py`.
- Debug: attach to `LogiPluginService.exe` (`.vscode/launch.json`).

## Conventions in this codebase
- `this.` on all member access, BCL type names (`String`, `Int32`, `Boolean`), `using` directives inside the namespace, Allman braces, `_camelCase` private fields (all from `.editorconfig`).
- One class per file; folders by role (`Actions/`, `Adjustments/`, `Pipe/`, `Helpers/`) matching sub-namespaces of `Loupedeck.AudacityPlugin`.
- Infrastructure is `internal sealed`; SDK-facing action classes are `public`.
- Async: `ConfigureAwait(false)`, fire-and-forget as `_ = ...`, exception filters on specific types.
- `// VERIFY:` marks assumptions not yet confirmed against Audacity or the SDK.
- Comments explain why, not what.

## Audacity scripting facts (checked against Audacity 3.7.9)
- `GetInfo: Type=Tracks Format=JSON` lists one entry per track (stereo = one entry, `channels:2`) with `selected`, `kind` and `volume` as **linear** gain.
- `SetTrack: Volume=<dB>` sets the **selected** tracks; there is no track index. `SetTrack: Gain=` (pre-3.x name) returns OK but does nothing, and `tools/verify_commands.py` checks command ids only, not parameters.
- Audacity's JSON doesn't escape backslashes; `TrackListParser` repairs that.
- Any scripting command runs on Audacity's UI thread and closes open menus.
- Only one client can hold the pipe. To probe Audacity while the plugin is loaded, rename `%LocalAppData%\Logi\LogiPluginService\Plugins\AudacityPlugin.link` to `.link.off`, open `loupedeck:plugin/Audacity/reload`, probe, then rename it back and reload.

## SDK notes
- Use the `logi-sdk` skill; it searches a local copy of the SDK docs.
- `pluginFolderWin`/`pluginFolderMac` in `LoupedeckPackage.yaml` must match the build output folder (`bin/`), or the plugin won't load.
- `supportedDevices` names must exist in `Loupedeck.DeviceType` (MX Creative Console is `LoupedeckExtendedFamily`).
