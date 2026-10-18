namespace Mahjong.Core
{
    public enum Suit : byte { Dots, Bamboo, Characters, Wind, Dragon, Flower, Season }

    /// <summary>Tile kinds are plain bytes 0..41. Flowers and seasons match within their group.</summary>
    public static class Kinds
    {
        public const byte None = 255;
        public const int Count = 42;
        public const int PlainCount = 34;

        public static Suit SuitOf(int k)
        {
            if (k < 9) return Suit.Dots;
            if (k < 18) return Suit.Bamboo;
            if (k < 27) return Suit.Characters;
            if (k < 31) return Suit.Wind;
            if (k < 34) return Suit.Dragon;
            if (k < 38) return Suit.Flower;
            return Suit.Season;
        }

        /// <summary>Rank within the suit, starting at 1.</summary>
        public static int RankOf(int k)
        {
            if (k < 9) return k + 1;
            if (k < 18) return k - 9 + 1;
            if (k < 27) return k - 18 + 1;
            if (k < 31) return k - 27 + 1;
            if (k < 34) return k - 31 + 1;
            if (k < 38) return k - 34 + 1;
            return k - 38 + 1;
        }

        public static int Group(int k)
        {
            if (k >= 34 && k < 38) return 34;
            if (k >= 38 && k < 42) return 38;
            return k;
        }

        public static bool Match(int a, int b) { return Group(a) == Group(b); }

        public static string Name(int k)
        {
            switch (SuitOf(k))
            {
                case Suit.Dots: return RankOf(k) + " Dots";
                case Suit.Bamboo: return RankOf(k) + " Bamboo";
                case Suit.Characters: return RankOf(k) + " Characters";
                case Suit.Wind: return new[] { "East", "South", "West", "North" }[RankOf(k) - 1] + " Wind";
                case Suit.Dragon: return new[] { "Red", "Green", "White" }[RankOf(k) - 1] + " Dragon";
                case Suit.Flower: return "Flower " + RankOf(k);
                default: return "Season " + RankOf(k);
            }
        }
    }
}
