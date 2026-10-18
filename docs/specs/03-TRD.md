# Technical Requirements Document (TRD)
## Mahjong - Unity, mobile + desktop, deterministic level generation

Version 1.1 | 2026-10-17 | Owner: Engineering
Companion docs: `01-MRD.md`, `02-PRD.md`. Where this document and the PRD disagree on an algorithm, this document wins; where they disagree on player-facing behavior, the PRD wins.

Change in 1.1: no ads, no IAP, no currencies, no analytics or crash SDKs, no remote content, no accounts. Fewer packages, fewer services, fewer tickets.

> **Implementation note (first playable build):** the game described here now exists in this repository.
> See `docs/IMPLEMENTATION_NOTES.md` there for what was built and what differs from this document.
> The main change is section 4.4: levels are built by finding a full legal clearing sequence with a forward backtracking
> search and then assigning kinds, instead of reverse placement. Everything else in section 4 holds.

This document is written so that an implementing engineer or coding agent can build the game ticket by ticket (section 12) without further design input. Anything marked **MUST** is a hard requirement verified by tests in section 9.

---

## 1. Technology stack

| Concern | Choice | Notes |
|---|---|---|
| Engine | **Unity 6 LTS (6000.0.x)**, latest patch at project start | Vita Mahjong ships on 2022.3; Unity 6 gives us URP 2D and Input System maturity plus better build size |
| Scripting backend | IL2CPP on all platforms, .NET Standard 2.1 API level, C# 9 | Mono only for editor/tests |
| Render pipeline | URP with 2D Renderer | Sprite lighting unused; keep pipeline asset minimal |
| Input | Unity Input System package | One action map for touch/mouse/keyboard |
| UI | UGUI + TextMeshPro, Canvas Scaler "Scale with screen size" | UI Toolkit not used for runtime (TMP fonts and sprite swapping are simpler in UGUI for a casual game) |
| Tweening | DOTween (free, Asset Store) or PrimeTween (free) | Pick one at project start, wrap behind `ITween` helper so swap is cheap |
| JSON | `com.unity.nuget.newtonsoft-json` | JsonUtility cannot do dictionaries or polymorphism |
| Content | All in the build. `ContentCatalog` ScriptableObject with direct references to tile sets, backgrounds, music and layout JSON | No Addressables, no downloads, no catalogs. Sprite atlases keep draw calls low |
| Localization | Unity Localization package | String tables only |
| Ads / IAP / analytics / crash SDKs | **None** | No third-party SDKs at all. `Debug.Log` and the platform's own crash reporting are enough |
| Cloud backup | Platform-native (iCloud KVS / Play Games Saved Games / Steam Cloud) | Behind `ICloudSave`; optional; `NullCloudSave` everywhere else |
| Tests | Unity Test Framework (NUnit) edit-mode + play-mode; `Mahjong.Core` also builds as a .NET 8 console project for CI and tooling | |
| CI | GitHub Actions with GameCI (`game-ci/unity-builder`) for Android, Windows, macOS; iOS via Xcode on a mac runner | Golden determinism tests run on Windows, macOS and Linux runners via the .NET console build |
| Version control | Git, LFS for binary assets, `.gitignore` for Library/Temp/Logs/Builds | |

Package list (`Packages/manifest.json`, pin exact versions at setup): `com.unity.render-pipelines.universal`, `com.unity.inputsystem`, `com.unity.localization`, `com.unity.nuget.newtonsoft-json`, `com.unity.test-framework`, `com.unity.2d.sprite`. Nothing else unless a ticket names it.

---

## 2. Architecture

### 2.1 Assemblies (asmdef) and dependency direction

```
Mahjong.Core        pure C#, NO UnityEngine reference. Rules, layouts, generator, solver, rater, save model, PRNG.
   ^
Mahjong.Services    interfaces + implementations for storage, cloud backup, clock, achievements. References Core.
   ^
Mahjong.Game        Unity presentation: board view, tile view, input, camera fit, FX, audio. References Core + Services.
   ^
Mahjong.UI          screens, panels, navigation, view models. References Game.
Mahjong.Editor      editor tools (layout editor, validators, batch stats). References all.
Mahjong.Tests.Core  edit-mode NUnit tests over Core (also compiled in the console project).
Mahjong.Tests.Play  play-mode tests over Game/UI.
tools/LevelTool     .NET 8 console app that includes Core sources via a shared project; CLI for batch generation, ASCII render, golden hash export.
```

`Mahjong.Core` **MUST** compile with `noEngineReferences: true` in its asmdef. This is what makes CI determinism tests possible outside Unity.

### 2.2 Runtime composition
- One bootstrap scene `Boot` creates a `GameRoot` DontDestroyOnLoad object that owns a simple service locator (`Services.Get<T>()`), loads the save, applies settings, then loads scene `Main` additively.
- `Main` contains the board camera, `BoardView`, and a `ScreenStack` (UGUI) that pushes/pops screen prefabs: Home, Game HUD, Pause, LevelEnd, Stuck, Daily, LevelList, Collection, Settings, Stats, Debug.
- No singletons other than `GameRoot`. No `FindObjectOfType` at runtime. No static mutable state in `Core`.
- Game flow is a small state machine: `Boot -> Home -> LoadingLevel -> Playing -> (Paused | Stuck | Won) -> Home/NextLevel`.

### 2.3 Core domain model (namespace `Mahjong.Core`)

```csharp
public readonly struct TilePos { public readonly short X, Y, Z; }     // half-tile units, z layer
public enum Suit : byte { Dots, Bamboo, Characters, Wind, Dragon, Flower, Season }
public readonly struct TileKind { public readonly Suit Suit; public readonly byte Rank; public int MatchGroup { get; } }
// MatchGroup: unique per (Suit,Rank) except Flower (all -> one group) and Season (all -> one group).

public sealed class Layout {          // immutable after construction
    public string Id; public string Family; public int Version;
    public TilePos[] Positions;       // sorted by (Z, Y, X) ascending; index = position id
    public int[][] Below, Above, Left, Right;   // adjacency by position id, each inner array sorted ascending
    public int Width, Height, Layers; // bounding box in half units
}

public sealed class Board {           // mutable game state
    public Layout Layout;
    public TileKind[] KindAt;         // per position id; default(TileKind) means removed
    public bool[] Present;
    public int Remaining;
    public bool IsFree(int pos);
    public bool IsCovered(int pos);  public bool IsBlockedLeft(int pos); public bool IsBlockedRight(int pos);
    public void Remove(int a, int b); public void Restore(int a, int b);   // for undo
    public void GetFreePairs(List<(int,int)> output);   // output sorted by (a,b) ascending
    public ulong Hash();              // FNV-1a over Layout.Id, GeneratorVersion, KindAt[], Present[]
}

public sealed class Level {
    public int Index; public ulong Seed; public int GeneratorVersion;
    public Layout Layout; public TileKind[] Kinds;      // initial assignment per position
    public int[] SolutionOrder;       // position ids in a legal forward removal order, pairs at [2i],[2i+1]
    public int TargetDifficulty; public int RatedDifficulty; public RatingStats Stats;
}
```

### 2.4 Rules (`Mahjong.Core.Rules`)
Implements PRD 3.2 exactly:
- Footprint of pos p: cells `[X, X+2) x [Y, Y+2)` on layer Z.
- `Below[p]`: positions q with `q.Z == p.Z - 1` and footprints overlapping. `Above[p]` symmetric.
- `Left[p]`: positions q with `q.Z == p.Z`, `q.X + 2 > p.X - 2 && q.X < p.X` and y-overlap `q.Y < p.Y + 2 && q.Y + 2 > p.Y`. `Right[p]` mirrored.
- `IsFree(p) = Present[p] && !Any(Above[p], Present) && (!Any(Left[p], Present) || !Any(Right[p], Present))`.
- Adjacency arrays are built once per layout with an O(n^2) pass (n <= 144) and cached.

---

## 3. Determinism contract (MUST)

The generator, solver, rater, shuffle and hint **MUST** produce bit-identical results on every platform, CPU, OS, Unity version and IL2CPP/Mono backend. Rules for all code in `Mahjong.Core`:

1. **Own PRNG only.** Never `System.Random`, `UnityEngine.Random`, `Guid`, `GetHashCode()` of strings or objects, `DateTime`, `Environment.TickCount`.
2. **Integer arithmetic only** in anything that affects output. No `float`/`double`, no `Math.Exp/Sin/Pow`. Ratios are expressed in fixed point (`int` per-mille, or `long` with a 2^16 scale). Floats may be used only for presentation.
3. **Deterministic iteration.** Never iterate a `Dictionary`/`HashSet` where order affects output. Use arrays, `List<T>` with explicit sorts, or `SortedDictionary` with an explicit comparer. Never `OrderBy` without a total ordering tie-break (LINQ `OrderBy` is stable, but inputs must be in deterministic order anyway). Prefer no LINQ in Core hot paths.
4. **No string hashing from the runtime.** Use the in-house FNV-1a 64 (`Hashing.Fnv1a64(string)`), which hashes UTF-16 code units.
5. **No culture-dependent operations** (`ToString()` without `CultureInfo.InvariantCulture`, `string.Compare` without `StringComparison.Ordinal`).
6. **Versioned.** `GeneratorVersion` (int) is bumped whenever any output-affecting change ships. Golden tests pin each version. Old versions are kept in the codebase behind the version switch for at least 2 releases so late-synced saves still resolve (see 7.3).
7. **No threads inside Core.** Generation may be called from a worker thread by the Game layer, but Core itself is single-threaded and allocation-light (reuse buffers via a `GenContext`).

### 3.1 PRNG and hashing (`Mahjong.Core.Rng`)

```csharp
public static class Mix {
    public static ulong SplitMix64(ref ulong state) {
        ulong z = (state += 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
    public static ulong Fnv1a64(string s) { ulong h = 14695981039346656037UL; foreach (char c in s) { h ^= c; h *= 1099511628211UL; } return h; }
    public static ulong Fnv1a64(ReadOnlySpan<byte> bytes) { ... same ... }
}

public struct Xoshiro256 {            // xoshiro256** 1.0
    ulong s0, s1, s2, s3;
    public Xoshiro256(ulong seed) { ulong st = seed; s0 = Mix.SplitMix64(ref st); s1 = Mix.SplitMix64(ref st); s2 = Mix.SplitMix64(ref st); s3 = Mix.SplitMix64(ref st); }
    public ulong NextU64() { ulong result = RotL(s1 * 5, 7) * 9; ulong t = s1 << 17; s2 ^= s0; s3 ^= s1; s1 ^= s2; s0 ^= s3; s2 ^= t; s3 = RotL(s3, 45); return result; }
    public int NextInt(int maxExclusive) { return (int)(NextU64() % (ulong)maxExclusive); }   // modulo bias is acceptable and must stay for compatibility
    public int Range(int minInclusive, int maxExclusive) => minInclusive + NextInt(maxExclusive - minInclusive);
    public void Shuffle<T>(IList<T> list) { for (int i = list.Count - 1; i > 0; i--) { int j = NextInt(i + 1); (list[i], list[j]) = (list[j], list[i]); } }
    public Xoshiro256 Fork(string label) => new Xoshiro256(NextU64() ^ Mix.Fnv1a64(label));
}
```

Seeds:

```
WORLD_SEED  = 0x4D41484A4F4E4721   // "MAHJONG!"
DAILY_SALT  = 0x4441494C59313233   // "DAILY123"
levelSeed(N)        = SplitMix64(state: WORLD_SEED ^ ((ulong)N * 0x9E3779B97F4A7C15))
dailySeed(yyyymmdd) = SplitMix64(state: DAILY_SALT ^ (ulong)yyyymmdd)
zenSeed             = user-entered or SplitMix64(unix seconds)   // shown to the player as a 16-hex string
attemptSeed(seed,k) = SplitMix64(state: seed ^ ((ulong)k * 0xD1B54A32D192ED03))
```

Every sub-step forks its own stream with a label (`"layout"`, `"kinds"`, `"place"`, `"rate"`, `"shuffle:<n>"`) so changing one step's consumption does not perturb the others.

---

## 4. Level generation pipeline (`Mahjong.Core.Generation`)

```
GenerateLevel(int N, ulong seed, GeneratorConfig cfg, GenContext ctx) -> Level
  1. target = DifficultyCurve.Target(N)                     // 1..10, integer table (4.1)
  2. for attempt k in 0..cfg.MaxAttempts-1 (default 64):
       rng     = new Xoshiro256(attemptSeed(seed, k))
       layout  = LayoutSelector.Pick(N, target, rng.Fork("layout"), attempt k)        // 4.2 / 4.3
       kinds   = KindPlanner.Plan(layout.Positions.Length, target, rng.Fork("kinds")) // 4.5 -> pairKinds[]
       ok      = ReversePlacer.Place(layout, pairKinds, hardness(target), rng.Fork("place"), out kindsAt, out solutionOrder)  // 4.4
       if !ok continue
       rated   = Rater.Rate(layout, kindsAt, rng.Fork("rate"), out stats)             // 4.6
       if |rated - target| <= cfg.Tolerance (default 1) return Level(...)
       keep best-so-far by |rated - target|
  3. return best-so-far (never fail at runtime; log a warning event with N and k)
```

Target time: < 20 ms typical, < 50 ms worst case on Snapdragon 665 for 144 tiles including 64 attempts worst case. Rater is the hot spot; keep its simulation count bounded (4.6).

### 4.1 Difficulty curve (integer, authoritative)

```
Target(N):
  if N <= 5: return tutorialTable[N]            // 1,1,1,2,2
  base  = BASE_TABLE[min(N, 2000) / 50]         // 41 entries, hand-tuned, 2..8, roughly 2 + 6*(1 - e^(-N/400)) rounded; stored in DifficultyConfig
  wave  = WAVE_TABLE[N % 20]                    // 20 entries in {-1,0,0,1,1,1,0,0,-1,-1,-1,0,0,1,1,1,0,0,-1,-1}
  adj   = (N % 25 == 0) ? +2 : (N % 10 == 0) ? -2 : 0
  return clamp(base + wave + adj, 1, 10)
```

`DifficultyConfig` (ScriptableObject in Unity, JSON in the console tool) holds `BASE_TABLE`, `WAVE_TABLE`, the tile-count bands and the rater weights. Changing any of these **bumps GeneratorVersion**.

Bands per target (from PRD section 5), stored as `{minTiles, maxTiles, maxLayers, kindsDivisor}`.

### 4.2 Layout sources

Two sources, both producing a `Layout`:

**A. Authored layouts** (`Assets/Content/Layouts/*.layout.json`, also embedded as a resource in the console tool):

```json
{ "id": "turtle", "family": "classic", "version": 1, "tags": ["classic","wide"],
  "difficulty": [3, 8],
  "positions": [[0,0,0],[2,0,0], ... ] }
```
Positions are `[x,y,z]` half-units. At least 24 at launch (PRD P-GEN-7), each validated by the editor tool (4.9). Required silhouettes: turtle, pyramid, fortress, butterfly, dragon, bridge, spiral, diamond, cat, heart, crown, arena, cross, flower, fish, tower, stairs, wave, hourglass, star, temple, boat, moon, key.

**B. Procedural families** (`IProceduralFamily { Layout Build(int targetTiles, int maxLayers, Xoshiro256 rng); }`). Six at launch, each mirrored on the X axis for visual balance:

| Family | Construction |
|---|---|
| `Pyramid` | Base rectangle w x h tiles (w from 5..9, h from 4..7 to fit target), each upper layer inset by one half-tile on each side when `halfOffset` is true, else by one full tile. Layers until tiles exhausted or `maxLayers`. |
| `Fortress` | Rectangular ring 2 tiles thick, corner towers 2x2 stacked up to `maxLayers`, optional center keep. |
| `Blob` | Seed cell at center of layer 0; repeatedly add a random cell adjacent to the blob (and its mirror) until half the target is placed; upper layers: any cell whose footprint is >= 50% supported by the layer below, chosen randomly until target reached. |
| `Terraces` | 3 to 5 rows of different lengths, each row offset by one half-tile from its neighbor, second layer sits on the row intersections. |
| `Islands` | 2 to 4 small pyramids (3x3 to 5x4 bases) placed symmetrically with 1-tile gaps, connected by single-layer bridges. |
| `Columns` | 4 to 7 vertical stacks of height 2..`maxLayers` on a sparse base; produces deep boards for D >= 7. |

Every family **MUST**:
- produce an even tile count equal to `targetTiles` (if the construction overshoots or lands odd, remove positions from the top layer in deterministic order (highest z, then highest y, then highest x) until even and <= target; if under target by >= 2, add a mirrored pair on layer 0 at the next free symmetric slot; if that is impossible, return `null` and the attempt is skipped);
- satisfy the validator (4.9);
- bounding box <= 40 x 24 half-units (20 x 12 tiles).

### 4.3 Layout selection for level N

```
pool = all authored layouts + one virtual entry per procedural family   (ordered by Id, ordinal)
block = N / 50; slot = N % 50
perm  = permutation of pool indices seeded by SplitMix64(WORLD_SEED ^ 0x4C41594F5554 ^ block)   // "LAYOUT"
start = perm[slot % pool.Count]
walk perm from start (wrapping) and pick the first entry that fits:
   authored: tiles within band(target) AND target within entry.difficulty
   procedural: always fits (it is built to the band); its targetTiles = rng.Range(band.min, band.max+1) rounded down to even
on attempt k > 0: advance start by k (so re-rolls change layout too)
milestone (N % 25 == 0): restrict pool to layouts tagged "special" (authored, themed) if any fit, else fall through
```

This satisfies PRD P-GEN-6 without needing to know what previous levels produced.

### 4.4 Reverse placement (solvability guarantee)

The board is built by playing the game **backwards**: pairs of same-group tiles are placed in the reverse of a legal removal order. Because every placement step is a legal removal in the forward direction, the forward sequence (placement order reversed) is a complete solution.

Definitions during placement: `filled[p]` (bool), `unfilledDependents[p]` = number of positions in `Above[p] ∪ Left[p] ∪ Right[p]` not yet filled.

A position `p` is **placeable** iff:
1. `!filled[p]`
2. every `q` in `Below[p]` is filled (in the forward game those tiles are removed after p, so they exist when p is removed; a position with an empty supporter would make p a floating tile at some forward state)
3. `!Any(Left[p], filled) || !Any(Right[p], filled)` (p would be free at that forward moment; `Above[p]` is empty by construction because tiles above require p to be filled first)

Note that `Below` filled is a necessary condition for p to be *physically present* at that forward moment, not a freeness condition. It is what makes the generated solution respect the layout's stacking.

```
Place(layout, pairKinds[], hardness, rng, out kindsAt, out solutionOrder):
  n = positions.Length; require n even and pairKinds.Length == n/2
  filled = all false; placedCount = 0; placement = []        // placement[i] = position placed at step i
  for pairIndex in 0 .. n/2-1:
     C = placeable positions (ascending id)                   // recomputed incrementally: maintain a candidate bitset, update only neighbors of the last placed pair
     if C.Count < 2: return false
     // weight: favor positions that many others depend on, so they are placed early (= removed late in forward play) and never stranded
     weight[p] = 1 + unfilledDependents[p] * unfilledDependents[p] * CRITICAL_WEIGHT   (CRITICAL_WEIGHT = 4)
     a = weightedPick(C, weight, rng)
     filled[a] = true
     C2 = positions in C (excluding a) that are still placeable with a filled, plus any position that became placeable because a is now filled (recompute neighbors of a)
     // also a must remain free after b is placed: reject b in Left[a] if Right[a] already has a filled tile, and symmetric
     C2 = C2 where placing b keeps a free
     if C2.Count == 0: filled[a] = false; mark a as "tried" for this pairIndex; retry pick up to 8 times; else return false
     b = pick from C2:
        hardness = 0..1000 (per-mille). With probability hardness/1000 choose b to MAXIMIZE distance metric
        dist(a,b) = |a.X-b.X| + |a.Y-b.Y| + 4*|a.Z-b.Z|   (ties -> lower id), else uniform random from C2.
     filled[b] = true
     kindsAt[a] = kindsAt[b] = pairKinds[pairIndex]         // for Flower/Season groups assign two distinct faces from the group's unused faces, deterministic order
     placement.Append(a); placement.Append(b)
     update unfilledDependents for Below of a and b, and candidate bitset for neighbors of a and b
  solutionOrder = reverse(placement) in pairs: for i in 0..n/2-1: solutionOrder[2i] = placement[n-2-2i], solutionOrder[2i+1] = placement[n-1-2i]
  return true
```

Why `hardness` makes boards harder: when the two tiles of a pair are placed far apart in reverse time and space, the four copies of a kind become free at very different forward moments, which creates "wrong pair" traps (removing two copies that are both free now can strand the other two under each other). Easy boards place pairs close in time so matches are discoverable.

`hardness(target)`: `HARDNESS_TABLE[target]` = `{0,0,100,200,350,500,650,800,900,1000}` per-mille (index 1..10, index 0 unused).

Failure handling: `Place` returns false on a dead end (for example, the last two unfilled positions are stacked on each other). The caller moves to the next attempt seed, which also changes layout choice (4.3). Authored layouts are validated at import time to succeed within 8 attempts across 100 seeds (4.9); procedural families are fuzz-tested in CI (9.2).

### 4.5 Kind planning

```
Plan(tileCount, target, rng) -> TileKind[] pairKinds (length tileCount/2)
  band = bands[target]; K = max(2, tileCount / band.kindsDivisor)
  candidateKinds = the 34 non-wild kinds ordered by (Suit, Rank); if cfg.UseWildGroups also Flower group and Season group as 2 extra "kinds" of 2 pairs max
  // spread across suits: round-robin pick by suit so a 24-tile board is not 6 bamboo tiles
  shuffledBySuit = for each suit, shuffle its ranks with rng; then interleave suits in order Dots, Bamboo, Characters, Wind, Dragon
  chosen = first K of the interleaved sequence
  pairsPerKind = all 2 (i.e., 4 copies); totalPairs = tileCount/2
  if totalPairs > 2K: distribute extra pairs one at a time over chosen kinds in rng-shuffled order, max 2 extra (so <= 4 pairs = 8 copies, only used at D >= 9)
  if totalPairs < 2K: remove one pair from kinds in shuffled order until it fits (min 1 pair per kind)
  pairKinds = expand (kind repeated pairsPerKind times); rng.Shuffle(pairKinds)
  // difficulty lever 2: for hard targets, spread same-kind pairs apart in placement order
  if hardness(target) >= 500: for each kind with >= 2 pairs, move its second occurrence to index (firstIndex + totalPairs/2) % totalPairs by swapping
```

Wild groups: a Flower pair is two *different* flower faces that match by group. `pairKinds` stores the group kind; `Place` assigns concrete faces.

### 4.6 Rater (difficulty estimation)

Deterministic, integer, bounded cost. Produces `RatingStats` and an integer 1..10.

```
Rate(layout, kindsAt, rng, out stats):
  // Static features
  f1 = tiles                                           (24..144)
  f2 = layers                                          (1..7)
  f3 = initialFreePairs                                (count at start)
  f4 = kindsCount
  f5 = maxStackDepth = max over positions of the longest Below chain
  // Dynamic feature: greedy random play success rate
  SIMS = 24 (config); wins = 0; sumFreePairs = 0; steps = 0
  for s in 0..SIMS-1:
     board = fresh copy; r = rng.Fork("sim" + s)
     loop: pairs = board.GetFreePairs(); if none break
           sumFreePairs += pairs.Count; steps++
           // 50% "naive" moves (uniform random pair), 50% "smart" moves (pair that frees the most tiles; tie -> random) alternate by step parity
           board.Remove(pick)
     if board.Remaining == 0 wins++
  winPerMille = wins * 1000 / SIMS
  avgFreePairsX100 = steps == 0 ? 0 : sumFreePairs * 100 / steps
  // Score in fixed point, weights in DifficultyConfig (defaults):
  score = 1000
        + (1000 - winPerMille) * 4            // dead-end proneness is the dominant factor
        + (f1 - 24) * 12                      // bigger boards
        + (f2 - 1) * 250                      // more layers
        + max(0, 900 - avgFreePairsX100) * 2  // fewer options at each step
        - min(f3, 12) * 60                    // many opening moves = easier
  rating = clamp(1 + score / 1400, 1, 10)     // integer division
  stats = { winPerMille, avgFreePairsX100, f1..f5, score }
```

The constants are initial guesses. Calibration procedure (ticket T-14): generate 2,000 levels with the console tool, log stats, and adjust weights so the distribution of `rated` for each `target` is centered on the target. During beta, collect win-without-shuffle rates per level from testers (a Debug screen "Copy stats" button exports the local stats as text) and recalibrate; each recalibration bumps `GeneratorVersion`.

Cost: 24 sims x <= 72 moves x free-pair scan O(n) with incremental freeness: roughly 24 * 72 * 144 = 250k simple operations, well under 5 ms.

### 4.7 Shuffle (in-level, keeps solvability)

```
Shuffle(board, levelSeed, shuffleIndex) :
  positions = present position ids (ascending)
  multiset  = present kinds grouped by MatchGroup (each group count is even by invariant; assert)
  pairKinds = one entry per 2 tiles per group, in group order, then rng.Shuffle with rng = new Xoshiro256(SplitMix64(levelSeed ^ (0x5348554646 + (ulong)shuffleIndex)))   // "SHUFF"
  subLayout = Layout restricted to `positions` (adjacency recomputed over the subset, which is cheap)
  run ReversePlacer.Place(subLayout, pairKinds, hardness=0, rng.Fork("place")) with up to 16 attempts (attemptSeed)
  if every attempt fails (possible when the remaining positions cannot host any legal removal order, e.g. the only two remaining tiles are stacked on each other):
     fallback 1: "isolated first". Positions with no Left/Right/Above/Below inside the subset are mutually free; pair them up first (ascending id), then run Place on the remainder.
     fallback 2: "partial shuffle". Place as many legal pairs as possible, assign the leftover positions the leftover kinds in ascending id order, and return `partial = true`. The game then re-evaluates the stuck state and may show the stuck panel again. Write a `Debug.LogWarning`; CI fuzz (9.2) requires this below 0.1%.
```

The shuffle is deterministic given `(levelSeed, shuffleIndex, current present set)`, which is itself deterministic given the player's moves, so a replay log (section 7.4) can reproduce any game.

### 4.8 Re-roll and best-of policy
Attempts are capped (64 at generation, 16 at shuffle). The level returned is the first within tolerance, else the closest. CI tracks the fraction of levels that needed > 8 attempts; target < 2%.

### 4.9 Layout validator (editor tool and CI)
A layout is valid iff:
1. No two positions on the same layer overlap.
2. Every position on z > 0 is supported: total overlapping area with layer z-1 >= 50% of its footprint (2 of 4 cells).
3. Tile count is even, 24 <= count <= 144.
4. Bounding box <= 40 x 24 half-units.
5. `ReversePlacer.Place` succeeds within 8 attempts for each of 100 fixed seeds (0..99) using `hardness=1000` and kinds from `Plan(count, 10, ...)`.
6. Rated difficulty across those 100 seeds falls inside `[difficulty.min - 1, difficulty.max + 1]` for at least 80% of seeds (warns otherwise; the designer adjusts the tag).

The editor tool `Tools > Mahjong > Validate Layouts` runs this over the folder and prints a table. The console tool runs the same code in CI.

### 4.10 Hint selection
Among `GetFreePairs()`, choose the pair maximizing `(newlyFreedTiles * 4 + remainingCopiesOfKind)`; tie-break by lowest `(a, b)`. If the level's `SolutionOrder` next pair is currently free, prefer it (only when the board state still lies on the generated solution path, tracked by a simple pointer that advances while the player's removals match the solution set-wise; once off-path, the heuristic alone is used).

---

## 5. Unity presentation layer (`Mahjong.Game`)

### 5.1 Board rendering
- World space, orthographic camera. 1 Unity unit = 1 full tile width. A tile at `(x, y, z)` is at world `( x/2 + z*0.10, y/2 + z*0.10 )` (the per-layer offset draws the stack rising toward upper-right; the tile sprite includes a 0.10 unit side face at bottom-left).
- `TileView` prefab: `SpriteRenderer` body, `SpriteRenderer` face (child), `SpriteRenderer` outline (child, off by default), no colliders. 144 tiles are pooled; never instantiated during play.
- Sorting: `sortingOrder = z * 10000 + (layout.Height - y) * 64 + x` on all three renderers (outline +1, face +2).
- Dim blocked tiles via a shared `MaterialPropertyBlock` color (not per-material instances).
- Input: `BoardInput` converts screen point to world point and asks `BoardView.HitTest(worldPoint)`: iterate present tiles from highest z to lowest; the first whose rect `(worldX, worldY, 1, 1)` contains the point wins. Front-most tile wins because visual offsets are applied in the same formula.

### 5.2 Camera fit and responsiveness (PRD 6.2)
- Compute the world bounds of the layout (including layer offsets). Choose `orthographicSize` so bounds fit the safe-area rect minus margins; clamp so that the rendered tile width in millimeters (`Screen.dpi`) is >= the setting minimum (11/14/17 mm; if `Screen.dpi` is 0, assume 160 on Android, 326 on iOS, 96 on desktop). If clamping makes the board overflow, enable pan/zoom (touch drag and pinch; mouse drag and wheel) with the camera clamped to bounds.
- Re-run on `Screen.orientation`, resolution and safe-area changes (poll every 0.5 s, cheap).
- UGUI canvases: `CanvasScaler` match 0.5 with reference 1080x1920 (portrait) and 1920x1080 (landscape); a `SafeAreaFitter` on each root panel; HUD layout switches between bottom-bar and side-bar variants via a `LayoutGroup` swap when aspect > 1.

### 5.3 Input System actions
Action map `Gameplay`: `Point` (pointer position), `Tap` (touch press / left click), `Deselect` (right click / Esc), `Hint` (H), `Undo` (Z, Ctrl+Z), `Shuffle` (S), `Restart` (R), `Pause` (Esc when nothing selected), `Zoom` (scroll, pinch via EnhancedTouch), `Pan` (middle drag, two-finger drag), `Navigate` (arrows) and `Submit` (Enter) for keyboard play. Map `UI` is the default UGUI map.

### 5.4 FX and audio
- All tweens go through `Fx.TweenService`; `Settings.ReducedMotion` swaps durations and disables particles globally.
- `AudioService` with a 16-voice pool, mixer groups Master/Music/SFX, settings persisted.
- Particles: one pooled `ParticleSystem` for match puff; UI confetti on win uses a UI-space particle or a sprite animation (no third-party UIParticle required).

### 5.5 Content
- `ContentCatalog` ScriptableObject lists every `TileSet`, `BackgroundDef`, `TileBackDef`, music clip and authored layout `TextAsset`, each with an `unlock` rule (`{type: stars|level|always, value}`). Everything ships in the build.
- A `TileSet` ScriptableObject maps every `MatchGroup` and every concrete face to a `Sprite` plus a `back`, `body` and `outline` sprite and the minimum legible size tag. An editor validation script checks all 42 (+8 wild) faces exist and that each tile set has one sprite atlas.
- Launch content: 4 tile sets, 6 backgrounds, 4 tile backs, 3 music tracks, 24+ layouts.

### 5.6 Level loading flow
```
LevelLoader.Load(N):
  show HUD shimmer; run GenerateLevel on a Task.Run worker (Core is thread-safe for distinct contexts); await; build BoardView from Level; play drop-in animation.
```
Fixed tutorial levels 1..5 are authored `Level` JSON files (positions and kinds), bypassing the generator, flagged `GeneratorVersion = 0`.

---

## 6. UI layer (`Mahjong.UI`)
- `ScreenStack` with push/pop and a modal flag; one prefab per screen; each screen has a `ViewModel` class in plain C# that reads from Core/Services and raises C# events. No business logic in `MonoBehaviour`s beyond binding.
- All text via Localization `LocalizedString`; all numbers via `Fmt.Number(n)` (invariant for logic, locale for display).
- Buttons have a minimum 48x48 dp hit area even when the visual is smaller.
- Screens and their data contracts follow PRD 6.1. The **Debug** screen (hidden: 7 taps on the version label, always on in development builds) exposes: level number / seed input, generate and show stats, copy board hash, copy replay, copy local stats, force stuck, unlock all cosmetics, reset save.

---

## 7. Persistence (`Mahjong.Core.Save`, `Mahjong.Services.Storage`)

### 7.1 Save schema (JSON, Newtonsoft, version 1)
```json
{
  "schemaVersion": 1,
  "createdUtc": "2026-10-17T12:00:00Z", "updatedUtc": "...",
  "deviceId": "guid",
  "generatorVersion": 1,
  "journey": { "highestLevel": 128, "currentLevel": 129,
               "completed": { "1": {"stars":3,"gen":1,"offset":0,"bestSeconds":41,"noShuffle":true}, "...": {} } },
  "inProgress": { "mode":"journey", "levelIndex":129, "gen":1, "offset":0, "seed":"hex", "moves":[[12,57],[3,9]], "shuffles":1, "hints":0, "undos":2, "elapsedSeconds":63 },
  "daily": { "completed": { "20261017": {"stars":3,"gold":true,"seconds":252} }, "streak": 4, "lastCompleted": "20261017", "badges": ["202609"] },
  "collection": { "equipped": {"tileset":"tileset_classic","background":"bg_01","back":"back_01"} },
  "stats": { "levelsWon": 128, "pairsRemoved": 9180, "bestCombo": 5, "secondsPlayed": 15320, "noShuffleWins": 90, "longestStreak": 9, "totalStars": 311 },
  "achievements": { "unlocked": ["win_100"], "progress": {"streak_7": 4} },
  "settings": { "sound":1.0, "music":0.6, "haptics":true, "tileSize":1, "leftHanded":false, "reducedMotion":false, "textScale":0, "dimBlocked":1, "difficultyOffset":0, "autoContinue":false, "confirmRestart":true, "locale":"en", "cloudBackup":true, "tutorialDone":true }
}
```
Cosmetic ownership is not stored: it is derived from `stats.totalStars` and `journey.highestLevel` against the catalog's unlock rules, so there is nothing to migrate or desync.
- `moves` is the replay log (position id pairs in order, plus shuffle/undo markers encoded as `[-1, shuffleIndex]` and `[-2, 0]`). It lets the game resume mid-level exactly and lets support reproduce bugs.
- Writes: serialize to `save.json.tmp`, flush, then rename over `save.json`; keep `save.bak` of the previous good file. Load tries `save.json`, then `save.bak`.
- Migrations: `SaveMigrator.Migrate(JObject, fromVersion)` chain; one test per shipped version.

### 7.2 Cloud backup and export
`ICloudSave { Task<byte[]> Pull(); Task Push(byte[]); }` with platform implementations and a `NullCloudSave`. Merge policy per PRD section 9, implemented in Core (`SaveMerge.Merge(a, b)`), unit-tested. Backup runs silently after each save write (debounced to once per 30 s) and on app start; failures are logged and ignored.

Export / Import (PRD section 9): Settings writes `mahjong-save.json` via the platform file picker (desktop: `System.Windows.Forms`-free approach, use `SFB` standalone file browser or Unity's `EditorUtility`-free equivalent; mobile: native share sheet). Import validates the schema version and merges with the same policy.

### 7.3 Generator version migration
When `save.generatorVersion < current`: completed levels keep their records (keyed by level index with their own `gen`). The in-progress level is discarded if its `gen` differs (player restarts the same level number on the new generator; show a one-time toast). Daily challenge boards are pinned to the generator version current on that date via a tiny table `DailyGenVersionByDate` shipped in config so that old dates remain replayable identically.

### 7.4 Replay and bug reports
`Debug > Copy replay` emits `{level, gen, seed, moves}` as a string. The console tool can load it and print the board at any move.

---

## 8. Services (`Mahjong.Services`)

Only four small interfaces, each with a `Null*` implementation used in editor, tests and on platforms without the feature.

```csharp
public interface IStorage    { byte[] Read(string key); void WriteAtomic(string key, byte[] data); bool Exists(string key); }
public interface ICloudSave  { Task<byte[]> Pull(); Task Push(byte[] data); bool IsAvailable { get; } }
public interface IClock      { DateTime UtcNow { get; } }           // mockable for daily-challenge tests
public interface IAchievements { void Unlock(string id); }          // local only in v1; platform mapping is a Could
```

No network code exists outside `ICloudSave` implementations. A unit test asserts that no assembly other than `Mahjong.Services` references `System.Net` or `UnityEngine.Networking`.

---

## 9. Testing and verification

### 9.1 Edit-mode / console unit tests (`Mahjong.Tests.Core`)
- Rules: free/covered/blocked for aligned, half-offset and partial-cover cases; wild group matching; undo restores exact state and hash.
- PRNG: known-answer tests for SplitMix64, xoshiro256**, FNV-1a (vectors from the reference implementations).
- Generator: for N in 1..1000, `GenerateLevel` succeeds; forward-simulating `SolutionOrder` is legal at every step and clears the board (this is the formal solvability check); tile counts are within the band; rated within tolerance for >= 95% of levels.
- **Golden determinism:** `golden/levels_v{GeneratorVersion}.txt` contains `N, layoutId, boardHash, rated` for N in 1..1000 plus the daily seeds for 2026-01-01..2026-12-31. The test regenerates and compares exactly. Updating this file requires a GeneratorVersion bump in the same commit (a test asserts the file name matches the version).
- Shuffle: 10,000 random mid-game states (seeded) -> shuffle result is solvable from that state (solution forward-check) or flagged partial; partial rate < 0.1%.
- Difficulty curve table shape: non-decreasing block averages; breathers lower than neighbors.
- Save: round-trip, migration from each prior schema, merge policy, atomic write recovery (truncate tmp file, load must fall back), export/import round-trip.
- Progression: star rules, unlock rule evaluation, achievement conditions, streak across month and year boundaries with a mocked clock.

### 9.2 CI jobs
1. `dotnet test` of the console project on **windows-latest, macos-latest, ubuntu-latest**; all three must produce the same golden hashes (cross-platform determinism gate).
2. Unity edit-mode and play-mode tests (GameCI) on Linux.
3. Layout validator over `Assets/Content/Layouts`.
4. Procedural family fuzz: 2,000 builds per family across targets, validator must pass 100%, `Place` success within 8 attempts >= 99%.
5. Builds: Android AAB, Windows x64, macOS; iOS Xcode project archived on a mac runner (signing in a later stage).
6. Performance smoke (play-mode): generate levels 1..200 and record max generation time; fail if > 50 ms on the CI machine scaled by a configured factor (initial factor 1.0; calibrate against a Snapdragon 665 device measurement in T-27).

### 9.3 Manual device matrix
iPhone SE (2nd gen), iPhone 15, iPad 9th gen, Pixel 4a, a 2 GB RAM Android 8 device, Galaxy Tab A, Windows laptop 1366x768 at 125% DPI, MacBook Air M1, 4K monitor at 150% scaling.

---

## 10. Performance and size budgets

| Budget | Value |
|---|---|
| Frame time | 16.6 ms on target devices, 33 ms floor on min spec with 144 tiles + particles |
| Draw calls on board | <= 12 (one sprite atlas per tile set; SRP batcher on) |
| Generation | < 20 ms typical, < 50 ms worst, off main thread |
| Memory | < 300 MB on Android min spec |
| Install | < 120 MB mobile, < 200 MB desktop (all content included; use ASTC/ETC2 on mobile, atlases at 2048) |
| Cold start to Home | < 3 s mid-range phone |
| GC allocations during play | 0 per frame in steady state (pooled tiles, cached lists, no LINQ in update paths) |

---

## 11. Project conventions

- Folder layout: `Assets/_Project/{Core,Services,Game,UI,Editor,Content,Tests}`; third-party under `Assets/ThirdParty` (expected contents: the tween library and nothing else).
- One class per file; `namespace Mahjong.<Assembly>`; `private` fields `_camelCase`; no `public` fields on MonoBehaviours (use `[SerializeField] private`).
- Nullable reference types enabled in Core. `Core` has zero `using UnityEngine`.
- Every public method in Core has an XML doc comment stating determinism-relevant behavior.
- Commits that touch Core generation must say `[gen]` in the message and either keep goldens green or bump `GeneratorVersion` with regenerated goldens.
- Tunables live in ScriptableObjects (`DifficultyConfig`, `ContentCatalog`, `FxConfig`). No remote config, no feature flags.
- Adding any third-party SDK, network call, or currency-like counter requires a spec change first. Default answer is no.

---

## 12. Work breakdown (tickets for the implementing engineer or coding agent)

Each ticket lists deliverables and the test or check that closes it. Tickets inside a milestone can be parallelized where no dependency is noted.

### Milestone M1 - Core and generator (depends on nothing external)
| ID | Ticket | Deliverable | Done when |
|---|---|---|---|
| T-01 | Repo and project scaffold | Unity 6 project, asmdefs per 2.1, packages pinned, `.gitignore`, LFS, EditorConfig, console project `tools/LevelTool` sharing Core sources | CI builds the console project and runs an empty test on 3 OSes |
| T-02 | PRNG and hashing | `Mix`, `Xoshiro256`, known-answer tests | Tests green |
| T-03 | Domain model and rules | `TilePos`, `TileKind`, `Layout` (with adjacency builder), `Board`, `Rules`, `Board.Hash` | Rule tests from 9.1 green |
| T-04 | Layout JSON loader and validator | Loader, validator per 4.9 (items 1-4), 6 authored layouts to start (turtle, pyramid, fortress, butterfly, bridge, diamond) | Validator passes; invalid fixtures fail with messages |
| T-05 | Procedural families | 6 families per 4.2 B with evenness fix-up | Fuzz test per 9.2 item 4 |
| T-06 | Kind planner | `KindPlanner.Plan` per 4.5 incl. wild groups | Tests: counts, suit spread, pair totals |
| T-07 | Reverse placer | `ReversePlacer.Place` per 4.4 incl. hardness and incremental candidate tracking | Forward check of `SolutionOrder` for 1000 random (layout, seed) pairs |
| T-08 | Rater | `Rater.Rate` per 4.6 with config weights | Deterministic across runs; cost < 5 ms per level in console benchmark |
| T-09 | Difficulty curve and config | `DifficultyConfig` tables, `Target(N)` | Shape tests |
| T-10 | Layout selector | 4.3 incl. block permutation and milestone rule | No layout id repeats within any 50-level window for N in 1..5000 (test) |
| T-11 | Level generator orchestrator | `GenerateLevel` with attempts, tolerance, best-of | 1..1000 all solvable, >= 95% within tolerance |
| T-12 | Shuffle and hint | 4.7 and 4.10 | Shuffle fuzz per 9.1; hint always returns a legal pair when one exists |
| T-13 | Golden files and version gate | `golden/levels_v1.txt`, test, CI cross-OS job | Three OS runners agree |
| T-14 | Console tool features | `levels --from --to --stats`, `show N` ASCII render, `replay file`, `calibrate` (prints rated vs target histogram) | Used to calibrate rater weights; document results in `docs/calibration-v1.md` |
| T-15 | Board view and input | Tile pool, sorting, hit test, selection, match, undo, stuck detection, drop-in anim | Play-mode test: scripted taps clear generated level 7 |
| T-16 | Camera fit, pan/zoom, safe area | 5.2 | Manual check on portrait, landscape, 4K at 150% |
| T-17 | Minimal HUD + debug screen | Level number, counters, booster buttons wired; debug per section 6 | Play level N by number on Android and Windows builds |

### Milestone M2 - Product
| ID | Ticket | Deliverable | Done when |
|---|---|---|---|
| T-18 | Save system | Schema, atomic write, backup, migrator skeleton, replay log, resume mid-level | Tests in 9.1 |
| T-19 | Screen stack and Home | Navigation, Home screen with Play/Daily/Zen/Collection/Settings | Flow test Boot->Home->Game->Win->Home |
| T-20 | Level end, stuck, pause panels | Per PRD 3.5, 3.7, incl. auto-continue | Play-mode test |
| T-21 | Stars and progression | Star rules, unlock rule evaluation, difficulty offset plumbing | Unit tests |
| T-22 | Daily challenge | Seed by date via `IClock`, calendar screen, streak, badges, `DailyGenVersionByDate` | Tests with mocked clock across month and year boundaries |
| T-23 | Level list and replay | Scroll list with virtualization, jump-to-level | Scroll 5000 rows at 60 fps |
| T-24 | Settings | All PRD 6/11 toggles incl. tile size, left-handed mirror, reduced motion, text scale, dim blocked, difficulty offset, auto-continue | Each setting persists and applies live |
| T-25 | Content catalog and collection | `ContentCatalog`, `TileSet` SO, 4 sets, 6 backgrounds, 4 backs, 3 music tracks, collection screen with unlock text | Validation script passes; everything loads offline |
| T-26 | Onboarding levels 1-5 | Authored levels, scripted pointer, skip | Manual QA script |
| T-27 | Performance pass | Pooling, zero-alloc update, atlas, batching, device timing of generator (sets CI factor) | Budgets in section 10 met on device matrix |

### Milestone M3 - Polish
| ID | Ticket | Deliverable | Done when |
|---|---|---|---|
| T-28 | Cloud backup and export/import | Platform `ICloudSave` impls, merge, export/import UI | Cross-device test iOS<->iPad, Android<->Android, Steam; export/import round-trip |
| T-29 | Localization | 8 string tables, pseudo-loc, locale number formatting | No overflow at 150% text scale |
| T-30 | Achievements and stats | 20 local achievements, stats screen | Unit tests for unlock conditions |
| T-31 | FX and audio polish | Tweens, particles, SFX, music, haptics, reduced-motion variants | Design sign-off |
| T-32 | Accessibility pass | Contrast check script for tile sets, keyboard navigation, screen-reader labels | Checklist in PRD 11 |
| T-33 | Store builds | AAB, IPA, Win, macOS notarized, Steam depot; privacy labels declare "no data collected" | Internal test tracks live |

### Milestone M4 - Release
T-34 beta (TestFlight, Play internal track, Steam playtest) with a feedback form, T-35 difficulty recalibration from beta (bump GeneratorVersion), T-36 crash fixes, T-37 launch checklist.

---

## 13. Reference pseudo-implementation of the forward solvability check (used by tests)

```csharp
public static bool VerifySolution(Layout layout, TileKind[] kinds, int[] order) {
    var board = new Board(layout, kinds);
    for (int i = 0; i < order.Length; i += 2) {
        int a = order[i], b = order[i + 1];
        if (!board.IsFree(a) || !board.IsFree(b)) return false;
        if (kinds[a].MatchGroup != kinds[b].MatchGroup || a == b) return false;
        board.Remove(a, b);
    }
    return board.Remaining == 0;
}
```

## 14. ASCII board format (console tool and bug reports)
One line per layer from bottom to top, grid in half-units, each tile drawn as its 2-character code at its `(x, y)` origin (`D5` dots 5, `B3` bamboo 3, `C7` characters 7, `WE` wind east, `DR` dragon red, `F1` flower 1, `S2` season 2), `..` for empty. Used by `LevelTool show N` and in golden-file diffs.

## 15. Glossary
- **Half-unit grid**: positions are integers where a tile is 2 units wide and 2 tall, so tiles can sit half-offset over two tiles below.
- **Free tile**: not covered and open on the left or right.
- **Reverse placement**: building a board by placing pairs in the reverse of a legal removal order, which guarantees solvability.
- **Hardness**: generator parameter (per-mille) that spreads pair placements to create wrong-pair traps.
- **Rated difficulty**: integer 1..10 estimated by the rater; **target difficulty** is what the curve asks for.
- **GeneratorVersion**: integer identifying a frozen generator behavior; levels are reproducible only within a version.
