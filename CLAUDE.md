# Audacity plugin (Logi Actions SDK)

Logi Actions SDK plugin (C#, net10.0) that drives Audacity from Loupedeck / MX Creative Console hardware over Audacity's `mod-script-pipe` scripting module. Windows and macOS. User-facing docs: README.md.

## Architecture
- `src/AudacityPlugin.cs`: plugin root. Owns the single `AudacityPipeClient`; sets plugin status from its connection state.
- `src/AudacityApplication.cs`: links the plugin to Audacity (process `Audacity`, bundle `org.audacityteam.audacity`).
- `src/Actions/AudacityCommand.cs`: all buttons as parameters of one `PluginDynamicCommand`, table-driven (`Defs`). Parameter key = Audacity scripting command id.
- `src/Adjustments/`: Scrub, Zoom and Gain dials; ticks go through `TickCoalescer`.
- `src/Pipe/`: `AudacityPipeClient` (one connection, one `Channel` queue, so request/response pairs never interleave; 2 s poll reconnects or probes with `Message`), `IPipeTransport` with `WindowsPipeTransport` (`\\.\pipe\ToSrvPipe`/`FromSrvPipe`) and `UnixFifoTransport` (`/tmp/audacity_script_pipe.{to,from}.<uid>`).
- State is in memory only. Transport state and gain are optimistic: the pipe has no state query or events.
- Icons: `src/Images/<name>[-active|-off].png`, 80x80, embedded resources; regenerate with `python tools/make_icons.py src` (needs Pillow).

## Commands (run from repo root)
- Build: `dotnet build -c Debug`. Side effects: copies `src/package/` into `bin/Debug/`, writes `%LocalAppData%\Logi\LogiPluginService\Plugins\AudacityPlugin.link` and reloads the plugin in Logi Plugin Service.
- Compile only, no side effects: `dotnet msbuild src/AudacityPlugin.csproj -restore -t:Compile -p:Configuration=Debug -p:SolutionDir=<repo root>/`
- Lint: part of the build. Analyzers (`latest-recommended`) and `src/.editorconfig` style rules are enforced with warnings as errors.
- Test: no test project yet.
- Dependency audit: `dotnet list src/AudacityPlugin.csproj package --vulnerable --include-transitive`. There are no NuGet packages, so DLLs bundled with `PluginApi.dll` are not covered.
- Secret scan: `gitleaks git --redact` (history); the pre-commit hook scans staged changes.
- Hooks: after cloning, run `git config core.hooksPath .githooks` (gitleaks pre-commit, Conventional Commits commit-msg).
- Package / install: `logiplugintool pack ./bin/Release ./Audacity.lplug4`, then `logiplugintool install ./Audacity.lplug4`.
- Plugin log: `%LocalAppData%\Logi\LogiPluginService\Logs\plugin_logs\Audacity.log`.
- Check command ids against the installed Audacity (Audacity running): `python tools/verify_commands.py`.
- Debug: attach to `LogiPluginService.exe` (`.vscode/launch.json`).

## Conventions in this codebase
- `this.` on all member access, BCL type names (`String`, `Int32`, `Boolean`), `using` directives inside the namespace, Allman braces, `_camelCase` private fields (all from `src/.editorconfig`).
- One class per file; folders by role (`Actions/`, `Adjustments/`, `Pipe/`, `Helpers/`) matching sub-namespaces of `Loupedeck.AudacityPlugin`.
- Infrastructure is `internal sealed`; SDK-facing action classes are `public`.
- Async: `ConfigureAwait(false)`, fire-and-forget as `_ = ...`, exception filters on specific types.
- `// VERIFY:` marks assumptions not yet confirmed against Audacity or the SDK.
- Comments explain why, not what.

## SDK notes
- Use the `logi-sdk` skill; it searches a local copy of the SDK docs.
- `pluginFolderWin`/`pluginFolderMac` in `LoupedeckPackage.yaml` must match the build output folder (`bin/`), or the plugin won't load.
- `supportedDevices` names must exist in `Loupedeck.DeviceType` (MX Creative Console is `LoupedeckExtendedFamily`).
