# JokerDBDTracker

Desktop WPF tracker for JokerDBD streams with built-in player, progress system, quests, XP, prestige, and visual/audio effects.

Current stable release: `v1.2.0.1`

## Highlights

- Unified stream catalog with favorites and watched state.
- Embedded YouTube player with immersive mode and effect panel.
- Profile progression: XP, levels, prestige, achievements.
- Daily and weekly quests with claimable rewards.
- RU/EN localization.
- Settings MVP:
  - language
  - startup behavior
  - UI scale
  - fullscreen behavior
  - cache reset

## Release Scope

### `v1.2.0`

- Core UX upgrade and UI cleanup.
- XP economy rebalance and session bonuses.
- Quest system expansion (daily/weekly + timers + claim flow).
- Prestige icon set with fallback behavior.
- Player sound effects and keybind support.

### `v1.2.0.1` (stability patch)

- Startup/runtime crash handling with local diagnostics logging.
- Player reliability fixes for WebView2 startup/navigation race conditions.
- Safer effect application pipeline under heavy key spam.
- Better keybind handling while player is loading.
- Sound playback fallback fixes (without Windows default error beep).
- Fullscreen and monitor-bound behavior fixes.

## Install

1. Open releases: `https://github.com/dragloner/JokerDBDTracker/releases`
2. Download latest `win-x64` build.
3. Extract to any writable folder.
4. Run `JokerDBDTracker.exe`.

## Build From Source

Requirements:

- Windows 10/11
- .NET SDK (matching project target)
- Microsoft Edge WebView2 Runtime

Commands:

```powershell
dotnet build JokerDBDTracker.sln -c Debug
dotnet build JokerDBDTracker.sln -c Release
dotnet publish JokerDBDTracker.csproj -c Release -r win-x64
```

## Data and Logs

- App data: `%APPDATA%\\JokerDBDTracker`
- Diagnostics log: `%LOCALAPPDATA%\\JokerDBDTracker\\Logs\\app.log`

## Roadmap

See `ROADMAP.md`.

## Maintainer

`dragloner`
