# Audacity plugin (Logi Actions SDK)

[![Build and test](https://github.com/mrnil/AudacityPluginForLogi/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/mrnil/AudacityPluginForLogi/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/mrnil/AudacityPluginForLogi?include_prereleases&sort=semver)](https://github.com/mrnil/AudacityPluginForLogi/releases)
[![Licence: MIT](https://img.shields.io/github/license/mrnil/AudacityPluginForLogi)](LICENSE)
![Platforms: Windows | macOS (untested)](https://img.shields.io/badge/platforms-Windows%20%7C%20macOS%20%28untested%29-blue)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)

Controls Audacity over its **mod-script-pipe** scripting module. Windows + macOS.

## Audacity setup (one-off)
Preferences > Modules > `mod-script-pipe` = **Enabled**, then restart Audacity.
Pipes: Windows `\\.\pipe\ToSrvPipe` / `\\.\pipe\FromSrvPipe`; macOS `/tmp/audacity_script_pipe.to.<uid>` / `.from.<uid>`.

## Integrating with the SDK template
1. `logiplugintool generate Audacity` (gives you the csproj, `Helpers/PluginLog.cs`, `Helpers/PluginResources.cs`, build targets).
2. Copy `src/` from here over the generated `src/` (replaces `AudacityPlugin.cs`, `AudacityApplication.cs`, adds `Pipe/`, `Actions/`, `Adjustments/`, `Images/`).
3. Embed the icons — add to the csproj if the template doesn't already embed `Images/`:
   ```xml
   <ItemGroup><EmbeddedResource Include="Images\*.png" /></ItemGroup>
   ```
4. Merge `package/metadata/LoupedeckPackage.yaml` (keep the template's `supportedDevices`).
5. `python3 tools/verify_commands.py` with Audacity running to check every command id.

## Actions
| Button | Audacity command |
|---|---|
| Record / Record New Track | `Record1stChoice` / `Record2ndChoice` |
| Play / Stop, Stop, Pause | `PlayStop`, `Stop`, `Pause` |
| Go to Start / End | `CursProjectStart` / `CursProjectEnd` |
| Add Label | `AddLabel` (stopped) / `AddLabelPlaying` (playing/recording) |
| Undo, Save Project | `Undo`, `Save` |
| Zoom In / Out / Fit | `ZoomIn`, `ZoomOut`, `FitInWindow` |

| Dial | Turn | Press |
|---|---|---|
| Scrub | `CursorShortJump*` stopped, `Seek*Short` playing | Go to start |
| Zoom | `ZoomIn` / `ZoomOut` (max 4 per 50 ms) | Zoom to fit |
| Track Gain | `SetTrack: Volume=` on selected track(s), 0.5 dB/tick, ±36 dB, starting from the selected track's current volume | 0 dB |

## Known limits
- Transport state (record/play/pause highlight) is **optimistic**: the pipe has no state query or events, so starting playback from Audacity's own UI won't light the buttons. For the same reason, Pause does nothing while stopped (in Audacity it would arm pause for the next Play/Record, which the plugin can't track).
- Track Gain reads the first selected track's volume (`GetInfo: Type=Tracks`) at the start of each turn of the dial. Audacity's `SetTrack` has no track index, so all selected tracks are set to the same value.
- The plugin never sends Audacity anything unless you press a button or turn a dial: any scripting command, even a no-op, runs on Audacity's UI thread and closes open menus. Every 2 s it checks whether the Audacity process is still running (to drive the "-off" icons and plugin status) and reconnects when it isn't connected. If Audacity restarts within those 2 s, the first press afterwards fails and the plugin reconnects.
- If Audacity doesn't answer a command within 10 s (e.g. a modal Save dialog is open), the command fails, the plugin disconnects and drops anything queued behind it, then reconnects on the next poll. Up to 32 commands can queue; further presses are dropped until Audacity catches up.

## Assets
- `src/Images/*.png` — 80×80 PNG, RGBA, three variants each: `<name>.png`, `<name>-active.png` (record/play/pause), `<name>-off.png` (disconnected). Regenerate with `tools/make_icons.py src`.
- `src/package/metadata/Icon256x256.png` — 256×256 PNG, RGBA.

## Releases
Download the `.lplug4` from [Releases](https://github.com/mrnil/AudacityPluginForLogi/releases) and open it with Logi Options+ or the Loupedeck software. To publish a release, see [docs/release-process.md](docs/release-process.md); changes are listed in [CHANGELOG.md](CHANGELOG.md).

## Licence
MIT. See [LICENSE](LICENSE).
