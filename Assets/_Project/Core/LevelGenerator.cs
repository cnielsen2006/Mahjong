using System;
using System.Collections.Generic;

namespace Mahjong.Core
{
    public sealed class Level
    {
        public int Index;                 // journey level number, or day number for daily boards
        public ulong Seed;
        public int GeneratorVersion;
        public Layout Layout;
        public byte[] Kinds;
        public int[] Solution;            // pairs of position ids in a legal forward removal order
        public int Target, Rated;
        public RatingStats Stats;
        public int Attempts;

        public int Leaves { get { return (Rated + 1) / 2; } }   // 1..5 for the UI
    }

    /// <summary>Picks a layout for level N deterministically from authored and procedural sources.</summary>
    public static class LayoutSelector
    {
        static int PoolSize { get { return LayoutLibrary.All.Count + Procedural.FamilyCount; } }

        static int[] Permutation(int block)
        {
            int p = PoolSize;
            var perm = new int[p];
            for (int i = 0; i < p; i++) perm[i] = i;
            var r = new Xoshiro256(Mix.Next(Seeds.WorldSeed ^ Seeds.LayoutSalt ^ (ulong)block));
            r.Shuffle(perm);
            return perm;
        }

        public static Layout Pick(int index, int target, Xoshiro256 rng, int attempt)
        {
            var band = DifficultyConfig.GetBand(target);
            int p = PoolSize;
            var perm = Permutation(index / 50);
            int start = ((index % 50) + attempt) % p;
            bool milestone = DifficultyConfig.IsMilestone(index);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < p; i++)
                {
                    int idx = perm[(start + i) % p];
                    if (idx < LayoutLibrary.All.Count)
                    {
                        var a = LayoutLibrary.All[idx];
                        if (pass == 0 && milestone && !a.Special) continue;
                        var l = FitAuthored(a, band, rng);
                        if (l != null) return l;
                    }
                    else
                    {
                        if (pass == 0 && milestone) continue;
                        int tiles = (rng.Range(band.MinTiles, band.MaxTiles + 1)) & ~1;
                        var l = Procedural.Build(idx - LayoutLibrary.All.Count, tiles, band.MaxLayers, rng.Fork("proc"));
                        if (l != null && l.Count >= band.MinTiles - 4) return l;
                    }
                }
            }
            return null;
        }

        static Layout FitAuthored(LayoutLibrary.Authored a, DifficultyConfig.Band band, Xoshiro256 rng)
        {
            int total = a.Layout.Count;
            int lo = Math.Max(band.MinTiles, a.BaseCount + (a.BaseCount & 1));
            int hi = Math.Min(band.MaxTiles, total);
            if (lo > hi) return null;
            int chosen = rng.Range(lo, hi + 1) & ~1;
            if (chosen < lo) chosen = lo + (lo & 1);
            if (chosen >= total) return a.Layout;
            var pos = new List<TilePos>(a.Layout.Pos);
            return new Layout(a.Layout.Id, "authored", Procedural.Finalize(pos, chosen));
        }
    }

    public static class LevelGenerator
    {
        /// <summary>Journey level N (1-based). offset shifts target difficulty for players who want easier or harder.</summary>
        public static Level Journey(int n, int offset)
        {
            if (n < 1) n = 1;
            int target = DifficultyConfig.Clamp(DifficultyConfig.Target(n) + (n <= DifficultyConfig.TutorialLevels ? 0 : offset), 1, 10);
            Layout fixedLayout = n <= DifficultyConfig.TutorialLevels ? LayoutLibrary.Tutorial(n) : null;
            return Build(n, Seeds.ForLevel(n), target, fixedLayout);
        }

        /// <summary>Daily board for a date. dayNumber is any stable day counter (days since 2020-01-01).</summary>
        public static Level Daily(int yyyymmdd, int dayNumber, int weekdayMondayZero)
        {
            int target = DifficultyConfig.DailyTarget(weekdayMondayZero);
            return Build(1000 + dayNumber, Seeds.ForDaily(yyyymmdd), target, null);
        }

        /// <summary>Free play board from an arbitrary seed.</summary>
        public static Level Free(ulong seed, int target)
        {
            return Build((int)(seed % 100000UL), seed, DifficultyConfig.Clamp(target, 1, 10), null);
        }

        public static Level Build(int index, ulong seed, int target, Layout fixedLayout)
        {
            Level best = null; int bestDiff = int.MaxValue;
            for (int k = 0; k < DifficultyConfig.MaxAttempts; k++)
            {
                var rng = new Xoshiro256(Seeds.ForAttempt(seed, k));
                Layout layout = fixedLayout ?? LayoutSelector.Pick(index, target, rng.Fork("layout"), k);
                if (layout == null) continue;
                var pairKinds = KindPlanner.Plan(layout.Count, target, rng.Fork("kinds"));
                var kinds = new byte[layout.Count];
                var sol = new int[layout.Count];
                if (!SolutionBuilder.Build(layout, pairKinds, DifficultyConfig.Hardness(target), rng.Fork("place"), kinds, sol)) continue;
                if (!SolutionBuilder.VerifySolution(layout, kinds, sol)) continue;
                RatingStats stats;
                int rated = Rater.Rate(layout, kinds, rng.Fork("rate"), out stats);
                int diff = Math.Abs(rated - target);
                if (fixedLayout != null) diff = 0;
                if (best == null || diff < bestDiff)
                {
                    bestDiff = diff;
                    best = new Level
                    {
                        Index = index, Seed = seed, GeneratorVersion = DifficultyConfig.GeneratorVersion,
                        Layout = layout, Kinds = kinds, Solution = sol, Target = target, Rated = rated,
                        Stats = stats, Attempts = k + 1
                    };
                }
                if (diff <= DifficultyConfig.Tolerance) break;
            }
            if (best == null) throw new InvalidOperationException("Level generation failed for index " + index);
            return best;
        }
    }
}
