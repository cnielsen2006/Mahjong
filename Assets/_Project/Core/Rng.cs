using System;
using System.Collections.Generic;

namespace Mahjong.Core
{
    /// <summary>Deterministic hashing and seed mixing. Integer arithmetic only.</summary>
    public static class Mix
    {
        public static ulong SplitMix64(ref ulong state)
        {
            ulong z = (state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>One SplitMix64 step from a starting value.</summary>
        public static ulong Next(ulong state)
        {
            ulong s = state;
            return SplitMix64(ref s);
        }

        public static ulong Fnv1a64(string s)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < s.Length; i++)
            {
                h ^= s[i];
                h *= 1099511628211UL;
            }
            return h;
        }

        public static ulong Fnv1aStep(ulong h, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                h ^= (value & 0xFF);
                h *= 1099511628211UL;
                value >>= 8;
            }
            return h;
        }
    }

    /// <summary>xoshiro256** seeded through SplitMix64. Never use System.Random in Core.</summary>
    public sealed class Xoshiro256
    {
        ulong s0, s1, s2, s3;

        public Xoshiro256(ulong seed)
        {
            ulong st = seed;
            s0 = Mix.SplitMix64(ref st);
            s1 = Mix.SplitMix64(ref st);
            s2 = Mix.SplitMix64(ref st);
            s3 = Mix.SplitMix64(ref st);
        }

        static ulong RotL(ulong x, int k) { return (x << k) | (x >> (64 - k)); }

        public ulong NextU64()
        {
            ulong result = RotL(s1 * 5, 7) * 9;
            ulong t = s1 << 17;
            s2 ^= s0; s3 ^= s1; s1 ^= s2; s0 ^= s3;
            s2 ^= t;
            s3 = RotL(s3, 45);
            return result;
        }

        /// <summary>Uniform-ish int in [0, maxExclusive). Modulo bias is accepted and must stay for compatibility.</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            return (int)(NextU64() % (ulong)maxExclusive);
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            return minInclusive + NextInt(maxExclusive - minInclusive);
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }

        public Xoshiro256 Fork(string label)
        {
            return new Xoshiro256(NextU64() ^ Mix.Fnv1a64(label));
        }
    }

    /// <summary>Seed derivation rules. Changing any constant changes every level: bump GeneratorVersion.</summary>
    public static class Seeds
    {
        public const ulong WorldSeed = 0x4D41484A4F4E4721UL;
        public const ulong DailySalt = 0x4441494C59313233UL;
        public const ulong LayoutSalt = 0x4C41594F5554UL;
        public const ulong ShuffleSalt = 0x5348554646UL;

        public static ulong ForLevel(int n) { return Mix.Next(WorldSeed ^ ((ulong)n * 0x9E3779B97F4A7C15UL)); }
        public static ulong ForDaily(int yyyymmdd) { return Mix.Next(DailySalt ^ (ulong)yyyymmdd); }
        public static ulong ForAttempt(ulong seed, int k) { return Mix.Next(seed ^ ((ulong)k * 0xD1B54A32D192ED03UL)); }
    }
}
