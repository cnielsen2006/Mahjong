# Product Requirements Document (PRD)
## Mahjong - Mahjong Solitaire for mobile and desktop

Version 1.1 | 2026-10-17 | Owner: Product
Companion docs: `01-MRD.md` (why), `03-TRD.md` (how)

Change in 1.1: all monetization removed. No ads, no purchases, no currencies, no accounts. The game is a complete, self-contained product with minimum fuss.

Conventions: requirement IDs are `P-<area>-<n>`. Priority is MoSCoW (Must / Should / Could / Won't for v1). Every Must has acceptance criteria that QA (or an automated test) can verify.

---

## 1. Product summary

A Mahjong Solitaire game where levels are generated on the fly from a seed, so the Journey never ends, every player gets the same level N, and every level is guaranteed solvable. Large, readable tiles, no timer, calm presentation, no ads, nothing to buy, no sign-in. Ships on iOS, Android, Windows and macOS from one Unity project.

### 1.1 Pillars
1. **Calm and clear.** Big tiles, soft motion, nothing shouts. No timers unless the player opts in.
2. **Always another level.** Infinite deterministic Journey with a tuned difficulty curve and relief levels.
3. **No fuss.** Open the app, tap Play. No ads, no shop, no currency, no pop-ups, no account. Hints, Undo and Shuffle are always available.
4. **One game, every screen.** Portrait phone, landscape tablet, windowed desktop.

### 1.2 Target platforms (v1)
| Platform | Min spec | Input | Distribution |
|---|---|---|---|
| iOS / iPadOS | iOS 16+, A11+ | Touch | App Store |
| Android | API 26+ (Android 8), 2 GB RAM, OpenGL ES 3.0 / Vulkan | Touch | Google Play (AAB), direct APK |
| Windows | Win 10 x64, DX11 | Mouse, keyboard, touch | Steam, itch.io or direct download |
| macOS | 12+, Apple Silicon and Intel | Mouse, trackpad, keyboard | Mac App Store, Steam or direct download |
| WebGL | Could | Mouse | Own site |

Whether the stores list the app as free or with a one-time price is a business decision outside this spec; nothing in the product changes either way.

---

## 2. Game modes

### 2.1 Journey (Must) - the main mode
- Levels numbered 1, 2, 3... with no upper bound. Level N is produced by the deterministic generator (TRD section 4) from `(worldSeed, N)`.
- Difficulty follows a curve (section 5). Every 10th level is a "breather" (easier), every 25th a "milestone" (harder, special layout).
- Player always sees the current level number and can replay any completed level from a level list.
- Win: all tiles removed. Stuck (no free pair): offer Shuffle, Undo or Restart. There is no fail state and no lives.
- Completing a level awards stars (1 to 3 based on booster use, see 3.6) and advances `highestLevel`.

### 2.2 Daily Challenge (Must)
- One board per calendar day (UTC date), identical for all players: seed = `hash(dailySeedSalt, yyyymmdd)`.
- Difficulty: medium-hard, independent of Journey progress, with a weekly pattern (Mon easiest, Sun hardest).
- Calendar view for the current and previous months. Completed days show a crown (gold if no Shuffle used, silver otherwise). Completing all days of a month awards a monthly badge. Past days of the current and previous month can be played late.
- Streak counter for consecutive days completed.

### 2.3 Zen / Free Play (Should)
- Player picks a layout family and size, tile set and difficulty, and plays endless boards. The seed is shown as a short code and can be typed in, so a board can be shared with a friend.
- No stars. Boosters unlimited. Good for desktop and for learning.

### 2.4 Timed mode (Could) and Memory variants (Won't in v1)
- Timed: optional per-level timer with bonus stars. Off by default and never surfaced to a player who has not enabled it.
- Memory (face-down tiles) is a phase 2 idea.

---

## 3. Core gameplay rules

### 3.1 Tile set (P-CORE-1, Must)
Standard 42 face kinds, each with 4 copies = 144 tiles maximum per board (plus optional flower/season groups):

| Group | Kinds | Copies |
|---|---|---|
| Dots (circles) | 1 to 9 | 4 each |
| Bamboo | 1 to 9 | 4 each |
| Characters (numbers) | 1 to 9 | 4 each |
| Winds | East, South, West, North | 4 each |
| Dragons | Red, Green, White | 4 each |
| Flowers | Plum, Orchid, Chrysanthemum, Bamboo | 1 each, any flower matches any flower |
| Seasons | Spring, Summer, Autumn, Winter | 1 each, any season matches any season |

- Generated boards use a subset of kinds (TRD 4.5). A board with T tiles uses roughly T/4 kinds.
- Flowers and Seasons are **off by default** (config flag `useWildGroups`) because group matching confuses many players. When on, the UI shows a shared colored border for the group.
- Tile sets (cosmetic skins) remap faces but never change matching logic. Every tile set must keep numbers and suit markers readable at the minimum tile size (6.3).

### 3.2 Board and free-tile rule (P-CORE-2, Must)
- Positions are on a half-tile grid: integer `x`, `y` in half-tile units and integer `z` layer (0 = bottom). A tile occupies cells `[x, x+2) x [y, y+2)` on layer `z`.
- A tile is **covered** if any tile on layer `z+1` overlaps its footprint.
- A tile is **blocked left** if any tile on the same layer overlaps cells `[x-2, x) x [y, y+2)`; **blocked right** symmetric on `[x+2, x+4)`.
- A tile is **free** if it is not covered and (not blocked left or not blocked right).
- Acceptance: unit tests for every case including half-offset neighbors and partial covers (TRD 9).

### 3.3 Selection and matching (P-CORE-3, Must)
- Tap a free tile: it highlights (selected). Tap a non-free tile: brief nudge shake and a soft sound, nothing selected, and the blocking tiles flash once (teaching aid, can be disabled).
- Tap a second free tile with the same kind (or same wild group): both animate out, board re-evaluates free tiles.
- Tap a second free tile with a different kind: selection moves to the new tile (no penalty).
- Tap the selected tile again: deselect.
- Desktop: hover highlights free tiles subtly; click behaves like tap; right-click or Esc deselects; keyboard `H` hint, `Z`/Ctrl+Z undo, `S` shuffle, `R` restart.
- Optional "match assist" (Should): when a selected tile has exactly one free partner, show a thin link line to it. Never auto-remove.

### 3.4 Boosters (P-CORE-4, Must)
All boosters are free and unlimited. The only consequence of using them is the star rating.

| Booster | Effect |
|---|---|
| Hint | Highlights one currently free matching pair (prefers a pair whose removal unblocks the most tiles) |
| Undo | Reverts the last removal; unlimited history within a level; also undoes a Shuffle |
| Shuffle | Re-deals the remaining tiles into the remaining positions, guaranteed solvable from that point (TRD 4.7) |
| Restart | Rebuilds the level from its seed |

- Booster buttons are always visible on the game screen; no counters, no dialogs, no confirmation except Restart (single "Restart level?" confirm, suppressible in settings).

### 3.5 Stuck state (P-CORE-5, Must)
- When no free pair exists after a move, after 600 ms show a small non-blocking panel: "No more moves" with Shuffle, Undo, Restart. The board stays visible behind it.
- Because levels are generated by reverse play, the initial board always has a winning line, but the player's choices can still dead-end. The copy must never say "unsolvable".

### 3.6 Stars and combo (P-CORE-6, Should)
- Stars at level end: 3 stars if no Shuffle used and at most 1 Hint; 2 stars if at most 1 Shuffle or up to 3 Hints; 1 star otherwise. Undo never affects stars. Replaying a level can only raise its star count.
- Combo: pairs removed within 3 seconds of each other show a small "x2, x3..." flourish up to x5. Purely cosmetic feedback, no score, no timer shown.

### 3.7 Level end (P-CORE-7, Must)
- Win panel: stars, time taken, pairs cleared, "Next level" primary button, "Replay" secondary. No interstitials, no upsells, no pop-ups of any kind.
- Daily win panel: crown, streak count, calendar shortcut, Share button (copies "I cleared today's board in 4:12" style text, no image in v1).
- Setting "Auto-continue": after a win, start the next level automatically after 2 seconds (default off).

---

## 4. Deterministic level generation - product view

The TRD owns the algorithm. Product constraints:

| ID | Requirement | Priority |
|---|---|---|
| P-GEN-1 | Level N is identical for all players on all platforms and all app versions that share the same generator version | Must |
| P-GEN-2 | Every generated board is solvable from the initial state | Must |
| P-GEN-3 | Generation of any level completes within 50 ms on a 2019 mid-range phone (Snapdragon 665 class) without visible hitch; may run on a worker thread with a brief shimmer | Must |
| P-GEN-4 | No level has more than 144 tiles or fewer than 24 | Must |
| P-GEN-5 | Each level has a difficulty rating 1 to 10 that drives the curve and is shown as a 1 to 5 leaf icon | Must |
| P-GEN-6 | Consecutive levels do not reuse the same layout family more than twice in a row, and the same exact layout is not reused within any window of 50 levels | Should |
| P-GEN-7 | Layout families are visually recognizable. At least 24 hand-authored silhouettes plus 6 procedural families at launch | Should |
| P-GEN-8 | Milestone levels (every 25th) use a "special shape" layout | Should |
| P-GEN-9 | When the generator version changes, completed levels keep their status; the current level is regenerated with the new version. History stores `(levelIndex, generatorVersion)` | Must |
| P-GEN-10 | A debug screen (dev builds, and a 7-tap secret on the version label in release) lets testers enter a level number or seed and view the rating and solver stats | Must |

---

## 5. Difficulty curve (P-DIFF, Must)

Intended shape of target difficulty `D(N)` on a 1 to 10 scale (the TRD holds the authoritative integer tables):

```
base(N)      = 2 + 6 * (1 - exp(-N / 400))          // asymptotes toward 8
wave(N)      = 1.0 * sin(2*pi*N / 20)                  // gentle 20-level rhythm
breather     = (N % 10 == 0) ? -2.0 : 0
milestone    = (N % 25 == 0) ? +1.5 : 0
D(N)         = clamp(round(base + wave + breather + milestone), 1, 10)
```

Difficulty is realized by (in priority order): tile count, layer count and stacking depth, number of distinct kinds relative to tile count, and generator hardness heuristics (TRD 4.6). The generator must produce a board whose **rated** difficulty is within +-1 of `D(N)`; otherwise it re-rolls deterministically (TRD 4.8).

Tile-count bands (reference, tunable in `DifficultyConfig`):

| D | Tiles | Max layers | Kinds |
|---|---|---|---|
| 1 | 24 to 36 | 2 | tiles/4 |
| 2 | 36 to 48 | 2 | tiles/4 |
| 3 | 48 to 64 | 3 | tiles/4 |
| 4 | 64 to 80 | 3 | tiles/4 |
| 5 | 80 to 96 | 4 | tiles/4 |
| 6 | 96 to 112 | 4 | tiles/4 |
| 7 | 112 to 128 | 5 | tiles/4 |
| 8 | 128 to 144 | 5 | tiles/4 |
| 9 | 128 to 144 | 6 | tiles/5 |
| 10 | 144 | 7 | tiles/6 |

Levels 1 to 5 are a tutorial ramp with fixed hand-authored layouts and scripted hints (section 8). A "Difficulty offset" setting (-2 / -1 / 0 / +1 / +2) shifts the whole curve for players who find it too easy or too hard; it does not change which level N they are on, only the target passed to the generator, and the level record stores the offset used.

---

## 6. Presentation and UX

### 6.1 Screens (P-UI, Must unless noted)
1. **Boot / splash**: loads save and config, under 3 seconds, no logos longer than 1 second.
2. **Home**: big Play button (continues Journey at level N), Daily Challenge card with today's status and streak, Zen (Should), Collection, Settings. That is the whole screen.
3. **Game**: board, top bar (level number, difficulty leaves, pause), bottom bar (Hint, Undo, Shuffle), tiles-remaining counter.
4. **Pause**: Resume, Restart, Settings, Home.
5. **Level end** (win) and **Stuck** panels.
6. **Daily calendar**: month grid, badges row, streak.
7. **Level list**: scrollable list of completed levels with stars; tap to replay; jump-to-level field.
8. **Collection**: tile sets, backgrounds, tile backs; owned, equipped, locked with "unlocks at N stars / level N" text.
9. **Settings**: sound, music, haptics, tile size (Normal / Large / Extra large), left-handed, reduced motion, dim blocked tiles, difficulty offset, auto-continue, language, text scale, credits, version label.
10. **Debug** (hidden).

### 6.2 Responsive layout (P-UI-RESP, Must)
- The board auto-fits the safe area with 4% margin in portrait and landscape. Tile size is computed from layout bounds; the player's tile-size setting is a minimum, and when the board would not fit at that size the board becomes pannable and zoomable (pinch on mobile, wheel and drag on desktop) instead of shrinking below the minimum.
- Portrait phones: bottom bar for boosters, top bar for status. Landscape and desktop: side bar on the right (left when left-handed).
- Desktop: resizable window, min 960x600, fullscreen toggle (F11 / Cmd+Ctrl+F), UI scales with DPI.
- Tablets: same as desktop landscape.

### 6.3 Readability (P-UI-READ, Must)
- Minimum rendered tile face width: 11 mm on phones at Normal, 14 mm at Large, 17 mm at Extra large (computed from screen DPI).
- Tile faces must pass WCAG AA contrast (4.5:1) for the suit glyph against the tile body in every tile set.
- Selected tile: 3-state visual (idle, hovered/free, selected) using outline plus tint, never color alone.
- Blocked tiles rendered 15% darker by default (setting: off / subtle / strong).

### 6.4 Motion and audio (P-FX, Should)
- Tile removal: 250 ms scale-and-fade toward the pair midpoint plus a small particle puff. Combo adds a brief glow.
- Board enter: tiles drop in over 600 ms total, bottom layer first. Skippable by tap.
- Reduced motion: all tweens become 80 ms fades, particles off.
- Sounds: select, match, combo tiers, no-move, win chime, button. Calm ambient music loop with 3 tracks. Music off by default on desktop.
- Haptics on mobile for select and match (setting).

### 6.5 Visual themes (P-ART, Should)
- Launch with 4 tile sets (Classic ivory, High-contrast, Modern flat, Nature), 6 backgrounds, 4 tile backs, all shipped in the build. No downloads.
- Art direction: warm neutral palette, soft shadows, no casino gloss. Tiles are rounded rectangles with a visible side thickness so stacking reads clearly.

---

## 7. Unlocks and progression (P-PROG)

There is no currency and nothing to buy. Everything unlocks by playing.

| ID | Requirement | Priority |
|---|---|---|
| P-PROG-1 | Stars per level and total stars; cosmetics unlock at star thresholds (25, 75, 150, 300, 500, 800, 1200) and at level milestones (25, 50, 100, 200) | Must |
| P-PROG-2 | Daily streak with visible counter on Home; monthly badges for full months | Must |
| P-PROG-3 | Achievements (20 at launch, local only, e.g. "Clear 100 levels", "7-day streak", "3-star a difficulty-8 level") | Should |
| P-PROG-4 | Statistics screen: levels won, pairs removed, best combo, average stars, time played, longest streak | Should |
| P-PROG-5 | Journey chapters: background and music change every 50 levels (visual only, no gating) | Should |
| P-PROG-6 | Nothing is ever locked that affects gameplay. Boosters, modes and difficulty are available from level 1 (Zen and Daily after the tutorial) | Must |

---

## 8. Onboarding (P-ONB, Must)
- Level 1 (12 tiles, 2 layers): a hand pointer taps the first pair; explains "free" with the blocker flash. Level 2: Hint. Level 3: a tile blocked on both sides, teaches the side rule. Level 4: Undo. Level 5: stuck state and Shuffle. Levels 1 to 5 are fixed layouts, not generated.
- "I know how to play" on level 1 jumps to level 6 with all tutorial flags set.
- No other tutorials, tips, or first-run dialogs anywhere in the app.

---

## 9. Save data (P-SAVE)
- Local save only, JSON, versioned, written atomically on every level end, booster use and settings change. No account, no sign-in.
- Platform cloud backup (Should): iCloud on Apple, Google Play Games Saved Games on Android, Steam Cloud on desktop. Silent, automatic, no UI beyond an on/off toggle. Conflict rule: keep the save with the higher `highestLevel`; ties go to the latest timestamp.
- "Export / Import save" in Settings (Should): writes a single file the player can move between devices themselves. This is the no-fuss cross-ecosystem answer instead of accounts.
- Save migration must be tested from every shipped schema version.

## 10. Localization (P-L10N, Should)
Launch languages: English, Spanish, Portuguese (BR), French, German, Italian, Japanese, Simplified Chinese. All strings in Unity Localization tables; no hard-coded UI text. The UI has under 200 strings. Right-to-left: not in v1.

## 11. Accessibility (P-A11Y)
| ID | Requirement | Priority |
|---|---|---|
| P-A11Y-1 | Tile size setting with three steps; minimum touch target 11 mm | Must |
| P-A11Y-2 | High-contrast tile set that is color-blind safe (suits distinguished by shape and number, not hue) | Must |
| P-A11Y-3 | Reduced motion toggle; respects OS "reduce motion" where exposed | Must |
| P-A11Y-4 | UI text scale 100% / 125% / 150% | Should |
| P-A11Y-5 | Left-handed layout mirror | Should |
| P-A11Y-6 | Full keyboard play on desktop: arrows move a cursor across free tiles, Enter selects | Should |
| P-A11Y-7 | Screen-reader labels on all UI buttons | Could |

## 12. Privacy (P-PRIV, Must)
- No analytics SDK, no advertising SDK, no tracking, no network calls except platform cloud backup when enabled. The privacy policy can honestly say "this app collects nothing".
- No permissions beyond what the platform requires to run; no notification permission prompt in v1 (a daily reminder toggle is a Could, off by default, prompting only when the player turns it on).

## 13. Quality bar (P-QA, Must)
- 60 fps on target mobile devices with a 144-tile board and particles; 30 fps floor on min spec.
- Cold start to Home under 3 s on a mid-range phone; install under 120 MB on mobile.
- No data loss on crash: save written before any scene transition.
- 1,000 consecutive generated levels verified solvable in CI on every commit; golden hashes guard determinism (TRD 9).
- Pseudo-localization pass for text overflow at 150% text scale.

## 14. Release plan
| Milestone | Content |
|---|---|
| M1 Core (4 wks) | Board, rules, generator with solver and rating, Journey mode, debug screen, desktop + Android builds |
| M2 Product (4 wks) | Home, level end, boosters, daily challenge, save/migration, settings, tile sets, collection, onboarding |
| M3 Polish (2 wks) | Cloud backup, localization, achievements, stats, FX, audio, accessibility, store builds |
| M4 Release (1 wk) | Beta on TestFlight / Play internal track / Steam playtest, difficulty tweak from beta feedback, launch |
| Later, if wanted | Memory mode, timed mode, shareable seed images, WebGL demo, daily reminder |

## 15. Open questions
1. Free or one-time price on each store (does not affect the product).
2. Whether flowers/seasons wild groups ever ship on by default in Journey (default: off; available in Zen).
