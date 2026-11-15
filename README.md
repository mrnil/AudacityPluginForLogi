# Audacity plugin (Logi Actions SDK)

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
| Track Gain | `SetTrack: Gain=` on selected track(s), 0.5 dB/tick, ±36 dB | 0 dB |

## Known limits
- Transport state (record/play/pause highlight) is **optimistic**: the pipe has no state query or events, so starting playback from Audacity's own UI won't light the buttons.
- Gain is tracked plugin-side from 0 dB, not read back from Audacity.
- Connection is polled every 2 s (`Message` no-op) to drive the "-off" icons and plugin status. The probe is skipped while a command is waiting for Audacity.
- If Audacity doesn't answer a command within 10 s (e.g. a modal Save dialog is open), the command fails, the plugin disconnects and drops anything queued behind it, then reconnects on the next poll. Up to 32 commands can queue; further presses are dropped until Audacity catches up.

## Assets
- `src/Images/*.png` — 80×80 PNG, RGBA, three variants each: `<name>.png`, `<name>-active.png` (record/play/pause), `<name>-off.png` (disconnected). Regenerate with `tools/make_icons.py src`.
- `src/package/metadata/Icon256x256.png` — 256×256 PNG, RGBA.
