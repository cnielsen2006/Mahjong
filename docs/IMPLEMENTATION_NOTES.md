# Implementation notes

What was built against `docs/specs`, as of the first playable build. Written to be read before changing anything.

## Verified

| Check | How | Result |
|---|---|---|
| Rules, RNG known answers, layouts, shuffle, solver, session/undo/replay | `CoreTests tests` | 1,014 checks pass |
| Determinism | Golden file of 1,000 levels, regenerated and compared | Passes. Only run on this one machine and OS so far; the cross-OS CI job in the TRD does not exist yet |
| Solvability | Every generated level's stored solution is replayed forward by the generator and by the tests | All levels tested solve |
| Rating tolerance | `CoreTests stats` | Rated difficulty is within 1 of target for essentially every level, but see "Not verified" |
| Generation speed | `stats` / in-engine autotest | About 1.5 ms average, 20 to 100 ms worst case on this PC |
| Hint latency | `CoreTests bench` | About 2 ms average, 15 ms worst |
| UI and play flow in the real player | In-engine autotest plus real mouse and keyboard through `tools/ui.ps1` | Plays levels 1 to 1200 by tapping, resume after quit, save round trip, every screen in landscape and portrait |
| Android | Builds an IL2CPP arm64 APK (21 MB) | Not installed or run on any device |

## Differences from the specs

| Area | Spec | Built | Why |
|---|---|---|---|
| Level construction | Reverse placement (TRD 4.4) | Forward solution search with backtracking, then kind assignment | Reverse placement dead-ended on stacked layouts and on the tutorial boards. The forward search is simpler and never fails on a sane layout. Difficulty now comes from how far apart in the clearing sequence the pairs of one kind are placed, plus layout size |
| Layouts | 24 authored + 6 procedural | 11 authored + 4 procedural | Time. More ASCII layouts are cheap to add in `LayoutLibrary.cs` |
| Layout reuse window | No repeat within 50 levels | Block permutation of a 15-entry pool | Impossible with 15 entries. Procedural layouts are different each time anyway |
| Difficulty rater | Calibrated against real play | Calibrated only against the generator's own structure and greedy-play simulations | There is no player data yet. Ratings are a plausible ordering, not measured difficulty |
| Difficulty ramp | PRD formula | Faster table: targets reach 5 around level 200, 7 around level 450 | Levels 6 to 50 were very small with the original table |
| Input | Unity Input System package | Legacy Input Manager | Fewer moving parts, no package download. The project's default input handling is the legacy one |
| Tile size setting | Minimum physical size 11 / 14 / 17 mm | Fit screen (default) / Large 9 mm / Extra large 12 mm, with pan and zoom when the board does not fit | 11 mm minimum cannot fit a 14-column board on a phone |
| Assemblies | Core / Services / Game / UI | Core / Game / Editor | Services and UI would each have been a handful of files |
| Services layer | Storage, cloud, clock interfaces | Not built | No cloud backup. Cross-device progress is a copy/paste save code in Settings |
| Localisation | 8 to 10 languages | English only | |
| Flowers and seasons | Optional wild groups | Matching rule exists in Core, but the generator never produces them | |
| Cosmetic "tile backs" | Included | Dropped | There are no face-down tiles in v1 |
| Music default | Off on desktop | Off on desktop, low on mobile | As specified |
| Haptics | Setting | Not built | |

## Not built

- Keyboard cursor navigation across tiles (P-A11Y-6) and screen-reader labels (P-A11Y-7).
- Cloud backup, platform achievements, notifications, localisation.
- iOS and macOS builds (need a Mac). WebGL is untested and probably blocked by the use of background threads.
- Cross-OS determinism CI. The golden test exists and should be run on Linux, macOS and an ARM device before trusting levels to match everywhere.
- Unity-side unit tests. Logic tests live in `tools/CoreTests`; the UI is covered by the in-engine autotest only.

## Not verified

- Sound and music were synthesised in code and never listened to. Levels, balance and pleasantness are untested.
- Whether the difficulty curve feels right to a human.
- Touch gestures (pinch, drag-to-pan) and Android behaviour on real hardware.
- Performance on low-end phones. Rendering uses about 1 draw call per sprite batch and no per-frame allocations in the board loop, but it has not been profiled on a device.

## Gotchas learned the hard way

- An overlay canvas always draws on top of the camera, so a full-screen background image in it hides the board. The background lives in its own camera-space canvas behind the tiles.
- A method named like a Unity type (`Screen<T>()`) shadows `UnityEngine.Screen` inside that class.
- Shell heredocs mangle large C# files. Create files with an editor tool, not `cat <<EOF`.
