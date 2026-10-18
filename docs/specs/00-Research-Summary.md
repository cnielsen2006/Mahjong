# Research Summary: Mahjong Solitaire Market and Vita Mahjong Teardown

Date: 2026-10-17
Purpose: Evidence base for the MRD, PRD and TRD in this folder. Read this first if you need to know *why* a requirement exists.

---

## 1. What "Mahjong" means in this market

The mobile hits called "Mahjong" are almost all **Mahjong Solitaire** (also called Shanghai or Taipei), not the four-player rummy-style table game. Rules:

- 144 tiles (or a smaller set) are laid face-up in a stacked, pyramid-like layout.
- A tile is **free** when nothing sits on top of it and its left *or* right side is open.
- The player taps two free, identical tiles to remove them.
- The board is cleared when all pairs are removed. The player is stuck when no free pair exists.
- Flower (4 distinct) and Season (4 distinct) tiles traditionally match within their group even though the faces differ. Many casual games drop this rule for clarity.

Solvability facts (Wikipedia, de Bondt 2012, Stam 2007):

- Deciding whether a fully-known board is solvable is NP-complete. Playing optimally with hidden tiles is PSPACE-complete.
- About 3% of randomly shuffled Turtle layouts are unwinnable even with perfect information, so **random shuffling is not acceptable** for a level-based game.
- Nearly all shipped implementations generate boards by **reverse play** (place pairs in the reverse order of a legal removal sequence), which guarantees at least one winning line exists.
- A board that is solvable with perfect information may still be unwinnable from the player's view because lower tiles are hidden. Undo and Shuffle exist to absorb that.

## 2. Vita Mahjong (Vita Studio / Oakever, package `com.vitastudio.mahjong`)

### Store positioning
- Tagline: "Exclusive Solitaire Puzzle Game for Senior Citizens". Large tiles, no timers, calm pace.
- App Store: 4.9 stars from ~1.8M ratings, 415 MB, 62 languages, iOS 15+, Game Center leaderboards, listed events ("Autumn Capybara Springs", "Spooky Tile Night").
- Claims "over 10,000 levels", "Daily Brain Exercises", "Brain-Training Mode", offline play, Hints and Undo.
- Monetization: ads (AppLovin MAX mediation with Unity Ads, Firebase analytics), Remove Ads $5.99, Weekly $2.99 / Monthly $5.99 / Yearly $19.99 subscriptions, coin packs $0.99 to $14.99.
- Criticism in reviews: ad frequency and length, "automatic tile matching" confusion, too simple for some players.

### Teardown of the APK in this folder (v3.41.1, build 1905)
- **Engine:** Unity 2022.3.62f2, IL2CPP, arm64 only, Burst enabled.
- **Libraries seen:** DOTween Pro, Spine (2D skeletal animation for characters/pets), TextMeshPro, Newtonsoft.Json, Coffee UIParticle (particles inside UI), Unity Localization module, Firebase Analytics + Crashlytics, AppLovin MAX, AppsFlyer, Unity IAP (module named `Module.Iap.DailyGift.Logic`).
- **Content is delivered as downloadable AssetBundles** with a custom manifest format (name, CRC, size, dependencies, asset list). About 135 bundle manifests ship in the APK; the heavy art is downloaded on demand.
- **Feature list inferred from bundle names** (this is the real feature surface of a top-grossing competitor):
  - Core: `mahjong_sort`, `match_effect`, `hint_optimize`, `tile_push`, `holder_bg`/`last_holder` (a tray-style sister mode), `dynamic_card`, `clampcard`, `click_block_lock`.
  - Difficulty and pacing: `hard_level`, `hard_level_encourage`, `special_level`, `special_shape`, `material_level`, `breakthrough`, `first_try_win`, `win_streak`, `combo*` (`combolevel`, `comboback`, `combokeep`, `perfect_combo`, `fullcombo`), `extrascore`, `unbelievableoutstand`, `encourage_emoji`, `positive_feedback`.
  - Meta and live ops: `journey` + `journey_card_face` (map-based progression), `rank_season`, `rank_short_season`, `rank_collect`, `rank_glory_star`, `race`, `iq`/`iq_week` (brain score), `dailyflower`, `dailyvictory_calendar`, `daily_task_unlock`, `achievement`, `milestone_settle_up`, `activity_theme`, `holiday_skin`, `advance_notice`, `pet`, `crown`, `country`.
  - Cosmetics: about 20 `cardface_*` sets (classic, Chinese chess, dinosaurs, sea animals, seasons, stained glass, Dunhuang, holidays), `card_back_pattern`, `theme_v3`, `bg_v1`, `level_bgm`, `splash_skin`.
  - Side games: `fruit_ninja`, `cut_pizza`, `eliminate_puzzle`, `color_challenge`, `puzzle`, `pinata`, `game_box`, `special_challenge`, `quick_match`.
  - Monetization: `coinshop`, `super_prop`, `dye_prop`, `super_blind_box`, `keepgift`, `gift_view`, `mt_ads`, `push`.

Takeaways: the gameplay is simple, but retention is driven by a thick meta layer (journey map, ranks, streaks, daily calendar, cosmetics). Our v1 must nail the core and the generator, and lay data foundations for that meta so it can be added without rewrites.

## 3. Other competitors (quick scan)

| Game | Notable traits |
|---|---|
| Mahjong Titan (Kristanix) | Thousands of hand-made layouts, daily challenge calendar, tile sets and backgrounds, light monetization. |
| Mahjong Solitaire Epic | Daily challenge, hundreds of layouts, simple UI, ads + remove-ads IAP. |
| Mahjong Journey | Story/map meta over the same core, boosters, events. |
| Microsoft Mahjong | Daily challenge calendar with monthly badges is the recognized retention standard. |
| Mahjong Blast, Vita Tile, Tile Voyage | "Tray" variant: drag tiles into a 7-slot tray, three-of-a-kind clears. Different game, same art. |

Common denominators: daily challenge with calendar + badges, hint/undo/shuffle, many tile sets and backgrounds, no timer by default, offline, free with ads + remove-ads.

## 4. What the research implies for our design

1. **Core must be deterministic and guaranteed solvable.** Reverse-play generation seeded by level index, with a difficulty rating pass. This replaces Vita's hand-stored 10,000 levels with an infinite, 100% reproducible level stream that costs no content budget.
2. **Accessibility is a market position, not a checkbox.** Large tiles, high contrast, no timer, generous touch targets, reduced motion option.
3. **Daily challenge is table stakes.** Date-seeded so all players share the same board, with a calendar and monthly badge.
4. **Meta can follow later** but save data, economy and event hooks must exist in v1's data model.
5. **Desktop is underserved.** Most competitors are mobile-only; a Unity build to Windows/macOS/Steam with mouse and keyboard support is a differentiator with little extra cost.

## Sources
- App Store listing: https://apps.apple.com/us/app/-/id6468921495
- Google Play listing: https://play.google.com/store/apps/details?id=com.vitastudio.mahjong
- BlueStacks description: https://www.bluestacks.com/campaign/com.vitastudio.mahjong/es
- Third-party review: https://iphoneappsarena.com/vita-mahjong/
- Review-sentiment report: https://kimola.com/reports/vita-mahjong-feedback-analysis-master-user-engagement-google-play-en-gb-154911
- Mahjong solitaire rules and complexity: https://en.wikipedia.org/wiki/Mahjong_solitaire
- de Bondt, "Solving Mahjong Solitaire boards with peeking": https://arxiv.org/pdf/1203.6559
- Elonen, why there is no complete solving algorithm without peeking: https://elonen.iki.fi/code/misc-notes/no-alg-mahj-solit/
- Open-source solver implementing Stam's approach: https://github.com/cchaiyatad/mahjong-solitaire-solver
- Hobbyist notes on generation cost: https://halfdome.bearblog.dev/mahjongg-solitaire/
- Mahjong Galaxy Space solvable-generation note: https://apps.apple.com/app/id665699654
- Vita Tile (tray variant): https://apps.apple.com/us/app/vita-tile/id6743194757
- Competitive scan pages (AI-generated, low confidence): https://marlvel.ai/apps/mahjong-titan-1
- APK teardown: local files `com.vitastudio.mahjong.apk` and `config.arm64_v8a.apk` in this folder.
