# JokerDBDTracker Roadmap

Updated: 2026-02-20

## `v1.2.0` Core UX + Progress (released)

- [x] Sound effects on keybinds and panel buttons.
- [x] XP rebalance for active watching.
- [x] Additional XP bonuses (session/time/activity based).
- [x] Prestige icons with fallback behavior.
- [x] UI cleanup and unified style.
- [x] Settings MVP:
  - startup behavior
  - language
  - UI scale
  - fullscreen behavior
  - cache reset
- [x] Daily and weekly quests with timers and claim flow.
- [x] Localization pass (RU/EN).

## `v1.2.0.1` Stability Patch (released)

- [x] Crash handlers + diagnostics log pipeline.
- [x] Player startup/navigation fail-safe behavior.
- [x] Improved sound playback reliability across machines.
- [x] Fixed hotkey conflicts and startup race conditions in player.
- [x] Effect application safety under rapid input.
- [x] Fullscreen/player layout fixes and monitor-bound behavior.

## `v1.2.1` Quality Follow-up (in progress)

- [ ] Final full regression pass:
  - profile persistence
  - quests reset/claim flow
  - player controls/effects/hotkeys
  - GitHub updater behavior
- [ ] Extra runtime telemetry for non-fatal player script timeouts.
- [ ] Final cleanup of minor UI edge cases found in release testing.

## `v1.3.0` Progress and Retention

- [ ] Watch calendar (activity heatmap).
- [ ] Date-based stream navigation.
- [ ] Weekly/monthly goals with rewards.
- [ ] Additional progression multipliers for consistency.
- [ ] New Favorites sub-tab: `Timecodes`.
- [ ] Save current playback timecode with custom title.
- [ ] Quick jump, edit, and delete for saved timecodes.

## `v1.4.0` Data and Reliability

- [ ] Profile export/import (history, achievements, favorites, XP, prestige).
- [ ] One-click backup.
- [ ] Stream list local cache and offline mode.
- [ ] Clear online/offline state indication.

## `v1.5.0` Personalization

- [ ] Theme system (Light/Dark/Custom).
- [ ] Accent colors.
- [ ] Compact/standard card density modes.
- [ ] UI preset packs.

## `v1.6.0` Social Features

- [ ] Coop Watch Together MVP (room + invite + sync play/pause/time).
- [ ] Optional lightweight room chat/reactions.

## `v1.7.0` Monetization and Customization

- [ ] Donation-based cosmetic packs (no P2W core impact).
- [ ] Extra sound packs.
- [ ] Profile visual styles/themes.
- [ ] Transparent donation value page.

## Cross-release Technical Priorities

- [ ] Anti-lag polish after each release.
- [ ] Backward-compatible profile schema updates.
- [ ] Fail-safe handling for YouTube/GitHub/network actions.
- [ ] Local error telemetry with exportable diagnostic report.
