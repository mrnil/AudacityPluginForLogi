# v0.1.0 - First release

The first release of the Audacity plugin for Logitech MX Creative Console and Loupedeck devices. It lets you record, play, label and zoom in Audacity, and set track gain, from your device's buttons and dials.

## What you get

### Buttons

- **Record** and **Record New Track**
- **Play/Stop**, **Stop** and **Pause**
- **Go to Start** and **Go to End**
- **Add Label**, at the cursor when stopped or at the play position while playing or recording
- **Undo** and **Save Project**
- **Zoom In**, **Zoom Out** and **Zoom to Fit**

Record, Play and Pause light up to show what Audacity is doing, and the buttons dim while Audacity isn't running.

### Dials

| Dial | Turn | Press |
|---|---|---|
| Scrub | Moves the cursor when stopped, seeks while playing | Go to start |
| Zoom | Zooms in and out | Zoom to fit |
| Track Gain | Changes the selected track's volume in 0.5 dB steps (±36 dB), starting from its current volume | Resets to 0 dB |

## Setup

1. In Audacity, open **Preferences > Modules**, set `mod-script-pipe` to **Enabled**, then restart Audacity.
2. Download `Audacity-v0.1.0.lplug4` below and open it to install it with Logi Options+ or the Loupedeck software.
3. Add the Audacity actions to your device.

## Known limits

- The lit buttons only follow what you do from your device. Audacity doesn't report its state, so starting playback with the mouse or keyboard won't light them. For the same reason, Pause does nothing while stopped.
- Track Gain sets every selected track to the same volume, starting from the first selected track's level.
- If Audacity is busy with a dialog (for example Save) for more than 10 seconds, the plugin reconnects and drops any presses made meanwhile.

## Requirements

- Audacity 3.x with `mod-script-pipe` enabled (tested with Audacity 3.7.9)
- Logi Options+ or the Loupedeck software
- Logitech MX Creative Console, or Loupedeck CT, Live or Live S
- Windows. macOS is supported but hasn't been tested yet: please [report any problems](https://github.com/mrnil/AudacityPluginForLogi/issues)
