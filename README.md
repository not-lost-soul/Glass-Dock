# GlassDock

A liquid-glass notch for Windows 10 and 11, fused to the top centre of the screen.

Clear glass with lensing and a specular rim, or Windows 11–style Acrylic, Mica, or Solid materials. The notch morphs system alerts (volume, brightness, devices, power, network, lock keys), hosts media controls, apps, tray files, notes, and tools — then shrinks to a thin click-through mini notch when you are busy so it never gets in the way.

Idle CPU is effectively zero: the animation loop only runs while something is moving.

## Features

- **Liquid glass** — real backdrop lensing, refraction, colour fringe, and motion smear; optional Acrylic / Mica / Solid
- **System alerts** — volume, brightness, USB/Bluetooth devices, charger, battery, network, Caps/Num Lock
- **Media** — now playing from any app that uses Windows media controls (Spotify, YouTube Music, Apple Music, VLC, …). Album art and equalizer stay visible on the compact notch while music plays
- **Apps** — pin shortcuts, Store apps, files, or folders; full All Apps drawer
- **Tray** — drop files onto the notch to park them
- **Notes & tools** — quick notes, calendar strip, pomodoro, clipboard history, system stats
- **Mini notch** — configurable height (default 20 px); pure idle mode is click-through; hover the top edge to expand

## Requirements

- Windows 10 or Windows 11
- .NET Framework 4.x (included with Windows)

## Build

Double-click `build.bat`, or from a Developer Command Prompt / normal cmd:

```bat
build.bat
```

This produces `GlassDock.exe` in the same folder. Close any running instance before replacing the binary.

## Run

1. Run `GlassDock.exe`
2. Right-click the notch → **Settings…** to theme, motion, alerts, and behaviour
3. Optional: enable **Start with Windows** in Settings → Behavior

Config and data files are written next to the executable:

| File           | Purpose              |
|----------------|----------------------|
| `settings.ini` | Preferences          |
| `apps.txt`     | Pinned apps          |
| `tray.txt`     | Tray items           |
| `notes.txt`    | Notes tab content    |

## Usage tips

- **Drag** empty glass to slide along the top or pull down into a floating island (snaps to centre / top)
- **Hover** the notch to expand; leave it alone and it collapses
- **Music** keeps the compact media strip visible even when the notch would otherwise go mini
- Push the cursor into the **top edge, centre**, to recover a fully collapsed mini notch
- Drag apps or files onto the open Apps tab to add them

## License

GlassDock is free under the **MIT License**. See [LICENSE](LICENSE).

You may use and share it at no cost. The software is provided as-is, without warranty.
