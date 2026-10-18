# Market Requirements Document (MRD)
## Product name: Mahjong

Version 1.1 | 2026-10-17 | Owner: Product
Companion docs: `00-Research-Summary.md`, `02-PRD.md`, `03-TRD.md`

Change in 1.1: the product is explicitly **non-monetized**. No ads, no in-app purchases, no currencies, no accounts. The market analysis below still describes the monetized competitors because that is exactly what we are positioning against.

---

## 1. Problem statement

Mahjong Solitaire is one of the largest evergreen casual-puzzle categories. The category leader for the older-adult segment, Vita Mahjong, has ~1.8M App Store ratings at 4.9 stars, yet its gameplay is a decade-old formula wrapped in a heavy ad load, a coin economy, subscriptions, and a large (415 MB) download. Players complain about ad frequency and pop-ups, and competitors differentiate only on art and meta. Nobody in the category offers:

1. a **truly infinite, deterministic level stream** (everyone ships a finite, hand-stored list),
2. a **genuinely fuss-free experience**: no ads, nothing to buy, no sign-in, no pop-ups,
3. a **first-class desktop experience** alongside mobile, and
4. an **accessibility-first** presentation without feeling like a "senior" product.

## 2. Market overview

- **Category:** Casual puzzle > tile matching > Mahjong Solitaire.
- **Players:** Skews 45+, majority female in comparable titles, high session frequency (several short sessions per day), low churn once habituated, strong response to daily calendars and collections.
- **Platforms:** iOS and Android dominate. Desktop (Steam, Microsoft Store, Mac App Store) is served by old or low-quality ports, which leaves room for a modern cross-platform title.
- **Business model norm:** Free-to-play with interstitial and rewarded ads, one-time Remove Ads ($4.99 to $5.99), optional subscription ($2.99/week to $19.99/year), consumable coins for hints/undo/shuffle. **We do none of this.** The game is complete as downloaded; whether a store lists it free or with a one-time price is a distribution decision that changes nothing in the product.
- **Acquisition:** Store search ("mahjong", "mahjong solitaire", "mahjong no ads"), word of mouth and reviews. "No ads, no purchases" is itself a store-listing headline that the category leaders cannot match.

## 3. Target segments

| Segment | Needs | How we win |
|---|---|---|
| **Relaxers (primary)**, 45+, play daily in 5 to 15 minute sessions | No timer, calm visuals, big readable tiles, "one more level" flow, predictable daily ritual, no interruptions | Infinite levels, daily challenge, zen mode, large-tile default, zero ads and zero pop-ups |
| **Completionists**, 30 to 60, collect and track | Calendars, badges, streaks, cosmetics, stats | Daily calendar with monthly badges, achievements, tile sets and backgrounds unlocked by play, deterministic level IDs they can share ("try level 4,217") |
| **Desktop casuals**, PC/Mac, often the same person as above on a bigger screen | Mouse and keyboard, windowed play, progress that follows them, no mobile-style ad spam | Native desktop builds, platform cloud backup plus save export/import, identical feature set everywhere |
| **Brain-training seekers** | Perceived cognitive benefit, difficulty progression, score | Difficulty rating per level, weekly brain score, memory variants (phase 2) |

## 4. Competitive landscape

| Competitor | Strength | Weakness we exploit |
|---|---|---|
| Vita Mahjong | Accessibility branding, enormous meta, polish | Ad fatigue, 400 MB+ install, finite stored levels, mobile only |
| Mahjong Titan | Thousands of layouts, daily calendar | Dated UI, no desktop account continuity |
| Mahjong Solitaire Epic | Simple and clean | Thin progression |
| Mahjong Journey | Story meta | Heavy boosters and pay pressure |
| Microsoft Mahjong | Daily calendar gold standard | Windows-centric, not on iOS/Android in a modern form |

## 5. Opportunity and positioning

**For** adults who want a calm daily puzzle **who** are tired of ad-heavy, repetitive tile games, **Mahjong** is a Mahjong Solitaire game **that** never runs out of levels, has no ads and nothing to buy, plays identically on phone, tablet and desktop, and respects the player's eyes, time and attention. **Unlike** Vita Mahjong, every level is generated from a seed, guaranteed solvable, rated for difficulty, and shareable by number, and the app never interrupts play.

## 6. Market requirements (what the market demands, not how we build it)

| ID | Requirement | Priority | Rationale |
|---|---|---|---|
| MR-1 | Endless level progression with no visible repetition | Must | Category leaders advertise "10,000+ levels"; infinite is a better headline and zero content cost |
| MR-2 | Every level is winnable and the same for every player | Must | Players share and compare levels; unwinnable boards drive 1-star reviews |
| MR-3 | Smooth, tunable difficulty curve with relief levels | Must | Retention drops when difficulty spikes; competitors add "hard level" encouragement systems |
| MR-4 | Large, readable tiles and no time pressure by default | Must | Core of the 45+ segment; Vita's whole brand |
| MR-5 | Hint, Undo, Shuffle always available, unlimited | Must | Expected in every competitor, but paywalled there; free here |
| MR-6 | Daily challenge, same board worldwide, calendar and badges | Must | Strongest known retention lever in the category |
| MR-7 | Works fully offline | Must | Travel and older devices; also follows from "no network features" |
| MR-8 | Mobile (iOS, Android) and desktop (Windows, macOS) with the same features and portable progress | Must | Differentiator; Vita is mobile only |
| MR-9 | Cosmetic collection unlocked by play: tile faces, backgrounds, tile backs | Should | Completionist retention |
| MR-10 | **No ads, no in-app purchases, no currencies, no accounts, no tracking** | Must | The product's defining promise; the category's main complaint is ad and pay pressure |
| MR-11 | Low install size (< 120 MB) | Should | Vita is 415 MB; a store-visible advantage |
| MR-12 | Localization in at least 8 languages at launch | Should | Text volume is small |
| MR-13 | Achievements, streaks, statistics (local) | Should | Completionist retention |
| MR-14 | Live-ops events, seasons, ranks, pets, side games | Won't | Fuss. Deliberately excluded |
| MR-15 | Accessibility: color-blind safe tile sets, reduced motion, scalable text, left-handed layout | Should | Underserved; store feature-worthy |

## 7. Success metrics

There is no analytics SDK, so these are measured only from store dashboards and beta feedback.

| Metric | Target |
|---|---|
| Store rating | 4.7+ |
| Crash-free sessions (store-reported) | 99.5% |
| Beta: level win rate without Shuffle (levels 6 to 200) | 85% to 92% |
| Beta: median session length | 8 minutes |
| Install size | < 120 MB mobile |
| Reviews mentioning "no ads" positively | Present in the first 50 reviews |

## 8. Risks

| Risk | Mitigation |
|---|---|
| Generated levels feel samey or "machine-made" | Layout family library with hand-authored silhouettes; difficulty rater; themed special shapes |
| Difficulty curve mis-tuned with no analytics to correct it | Beta test with a feedback form; per-player difficulty offset setting; generator version bumps are cheap |
| Determinism breaks across platforms or versions | In-house PRNG, golden-hash regression tests, generator versioning (see TRD) |
| Scope creep toward competitor-style meta | MR-14 is an explicit Won't; any addition must pass the "does it add fuss?" test |
| Trademark and art: "Mahjong" is generic, but tile art is often copied | Commission original tile faces; do not copy Vita's assets |

## 9. Out of scope

Ads, in-app purchases, subscriptions, currencies, accounts and sign-in, analytics and tracking, push-notification campaigns, events and seasons, ranks and leaderboards, pets, side mini-games, four-player mahjong, multiplayer, social feeds, tray/triple-match mode, user-generated layouts (may come later because the generator already supports arbitrary layouts).
