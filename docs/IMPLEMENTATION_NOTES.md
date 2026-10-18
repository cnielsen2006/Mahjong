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
| Board rendering | 2D sprites | Real 3D: beveled tile mesh, perspective camera, user-adjustable tilt | See "The 3D board" below |
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
- Performance on low-end phones. Tiles are GPU-instanced (a few draw calls for the whole board) and the board loop allocates nothing per frame, but it has not been profiled on a device. MSAA 4x is on; drop it in `App.Awake` if a device struggles.
- The 3D board has only been checked in the Windows player. The shader needs `#pragma target 3.0` and instancing, which every Android GLES3 / Vulkan device has, but it has not been run on one.

## The 3D board

- `TileMesh.cs` builds one shared tile mesh (coloured back and lower wall, ivory upper wall, chamfer, textured face) and the slab the tiles rest on. Both are procedural, like the rest of the art. The face texture is the same atlas the 2D sprites use; the mesh maps onto the rim-and-face rectangle of each drawn tile (`TileAtlas.FaceUv`).
- `Resources/MahjongTile.shader` (built-in pipeline) does the shading: one fixed world-space light, so tilting the board changes how it is lit. Tint, face rectangle and glow are instanced per tile. Selected / hint / blocked / assist feedback is a glow on the tile's rim and face, replacing the old outline sprite. Fading tiles (spawn, match, undo) swap to a blended copy of the material for the length of the animation.
- `BoardView` tilts a pivot at the board centre about x. The camera never rotates: it sits on -z looking at the pivot, and the part of the screen the HUD leaves free is selected with an off-centre projection matrix (so pan and zoom are shifts of the frustum, not camera moves). Layers stack straight up towards the camera, `TileMesh.Thick` each; the old per-layer x/y offset trick is gone.
- Tilt is `SettingsData.tilt` (0 to 60 degrees, default 20) and `SettingsData.roll` (-45 to 45, 0 = level; positive pushes the right edge away). Adjust them in Settings, with the tilt slider on the pause menu (live, the board is visible behind it), by dragging the background in game (up/down = tilt, left/right = roll; a drag that starts on a tile still pans a board bigger than the screen), or with Page Up / Page Down or Shift + mouse wheel (tilt only). `BoardView` eases to the settings, and `App.SaveSoon()` writes the save once changes stop.
- Auto zoom (`SettingsData.autoZoom`, default on, off while Reduced motion is on): the camera frames the bounding box of the tiles still on the board (`ViewTarget`), easing there at about 0.7 per second, and recentres on them. The close-up is capped at 2.5 times the whole-board fit, or a tile 22% of the viewport wide, so the last pairs are not enormous. The fit is recomputed every frame (about 16 small projections); it does not depend on any cached state. Note that it only zooms when the remaining tiles' bounding box actually shrinks: levels are not cleared outside-in, so a half-cleared board can still span the whole slab.
- Taps are rays tested against each tile's box in board space (`HitTest`), nearest first, so stacks and tilt pick the right tile.
- The background picture is drawn by its own camera (`App.BgCam`, layer 31, depth -10); the board camera only clears depth and draws over it. A camera-space canvas on the board camera does not work: the off-centre frustum shifts it.

## Gotchas learned the hard way

- An overlay canvas always draws on top of the camera, so a full-screen background image in it hides the board. The background lives in its own canvas, drawn by a separate background camera before the board camera (see "The 3D board").
- A method named like a Unity type (`Screen<T>()`) shadows `UnityEngine.Screen` inside that class.
- Shell heredocs mangle large C# files. Create files with an editor tool, not `cat <<EOF`.
