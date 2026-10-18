# Mahjong - specification pack

A Mahjong Solitaire game in Unity for iOS, Android, Windows and macOS with deterministic, guaranteed-solvable, infinitely generated levels. **No ads, no purchases, no currencies, no accounts, no tracking.** Just the game.

| File | Audience | Read it for |
|---|---|---|
| `00-Research-Summary.md` | Everyone | Market facts, Vita Mahjong teardown (it is a Unity 2022.3 IL2CPP game with ~135 downloadable content bundles), solvability literature, sources |
| `01-MRD.md` | Product, stakeholders | Why this product, segments, competitors, market requirements MR-1..15, success metrics |
| `02-PRD.md` | Product, design, QA | Player-facing behavior: modes, rules, boosters, difficulty curve, screens, accessibility, release plan |
| `03-TRD.md` | Engineering and the implementing coding agent | Stack, architecture, the full generation algorithm (reverse placement, rater, shuffle), determinism contract, data schemas, tests, CI, and a 37-ticket work breakdown |

## How to hand this to an implementing model
1. Give it `03-TRD.md` in full and `02-PRD.md` in full. The MRD and research summary are optional context.
2. Assign tickets T-01 onward in order inside each milestone (section 12 of the TRD). Each ticket states its "done when" check.
3. Insist on the determinism contract (TRD section 3) and the golden-file gate (TRD 9.1) before any presentation work; that is the part that is hard to retrofit.
4. Any change to generation output requires a `GeneratorVersion` bump plus regenerated golden files in the same commit.

## Key decisions already made
- Pair-matching Mahjong Solitaire, not the tray/triple-match variant and not four-player mahjong.
- Levels are generated from `hash(WORLD_SEED, N)` by reverse play, rated for difficulty with an integer-only rater, and re-rolled deterministically until the rating matches the curve.
- Core logic is a pure C# assembly with no UnityEngine dependency, also built as a .NET console tool for CI on Windows, macOS and Linux.
- No timers by default, large tiles, unlimited Hint/Undo/Shuffle (they only affect the star rating).
- Zero third-party SDKs beyond a tween library. No ads, IAP, analytics, crash reporting, remote config or downloadable content. Cosmetics unlock by stars and level milestones. Progress is local, with optional platform cloud backup and a save export/import file.
