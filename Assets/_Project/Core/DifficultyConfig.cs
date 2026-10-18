using System;

namespace Mahjong.Core
{
    /// <summary>
    /// All generator tuning lives here. Changing ANY value that affects output must bump GeneratorVersion
    /// and regenerate the golden file.
    /// </summary>
    public static class DifficultyConfig
    {
        public const int GeneratorVersion = 1;
        public const int TutorialLevels = 5;
        public const int MaxAttempts = 64;
        public const int Tolerance = 1;
        public const int RaterSims = 16;

        public struct Band
        {
            public int MinTiles, MaxTiles, MaxLayers, KindsDivisor;
            public Band(int a, int b, int c, int d) { MinTiles = a; MaxTiles = b; MaxLayers = c; KindsDivisor = d; }
        }

        // Index 1..10 (index 0 unused).
        static readonly Band[] Bands =
        {
            new Band(0, 0, 0, 4),
            new Band(24, 36, 2, 4),
            new Band(36, 48, 2, 4),
            new Band(48, 64, 3, 4),
            new Band(64, 80, 3, 4),
            new Band(80, 96, 4, 4),
            new Band(96, 112, 4, 4),
            new Band(112, 128, 5, 4),
            new Band(128, 144, 5, 4),
            new Band(128, 144, 6, 5),
            new Band(144, 144, 7, 6),
        };

        // Per-mille chance of picking the farthest partner when placing a pair (see SolutionBuilder).
        static readonly int[] HardnessTable = { 0, 0, 0, 100, 200, 350, 500, 650, 800, 900, 1000 };

        // One entry per 50 levels. Gentle start, reaching big boards within a few hundred levels.
        static readonly int[] BaseTable =
        {
            2, 3, 4, 4, 5, 5, 6, 6, 6, 7, 7, 7, 7, 7, 8, 8, 8, 8, 8, 8, 8,
            8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8
        };

        static readonly int[] WaveTable = { -1, 0, 0, 1, 1, 1, 0, 0, -1, -1, -1, 0, 0, 1, 1, 1, 0, 0, -1, -1 };

        static readonly int[] TutorialTable = { 1, 1, 1, 1, 2, 2 };

        public static Band GetBand(int difficulty) { return Bands[Clamp(difficulty, 1, 10)]; }
        public static int Hardness(int difficulty) { return HardnessTable[Clamp(difficulty, 1, 10)]; }

        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        /// <summary>Target difficulty 1..10 for Journey level N (N starts at 1).</summary>
        public static int Target(int n)
        {
            if (n < 1) n = 1;
            if (n <= TutorialLevels) return TutorialTable[n];
            int bi = Math.Min(n, 2000) / 50;
            int adj = (n % 25 == 0) ? 2 : (n % 10 == 0) ? -2 : 0;
            return Clamp(BaseTable[bi] + WaveTable[n % 20] + adj, 1, 10);
        }

        /// <summary>Daily challenge difficulty by weekday, 0 = Monday ... 6 = Sunday.</summary>
        public static int DailyTarget(int weekdayMondayZero)
        {
            int[] t = { 5, 5, 6, 6, 7, 7, 8 };
            return t[Clamp(weekdayMondayZero, 0, 6)];
        }

        public static bool IsMilestone(int n) { return n > TutorialLevels && n % 25 == 0; }
    }
}
