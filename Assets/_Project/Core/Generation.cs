using System;
using System.Collections.Generic;

namespace Mahjong.Core
{
    /// <summary>Chooses which tile kinds appear on a board and how many pairs of each.</summary>
    public static class KindPlanner
    {
        /// <summary>Returns one entry per pair (length tileCount / 2), in a random order.</summary>
        public static byte[] Plan(int tileCount, int target, Xoshiro256 rng)
        {
            int pairs = tileCount / 2;
            var band = DifficultyConfig.GetBand(target);
            int k = DifficultyConfig.Clamp(tileCount / band.KindsDivisor, 2, Kinds.PlainCount);
            if (k > pairs) k = pairs;

            // ranks per suit, shuffled, then interleaved so small boards still mix suits
            int[] suitStart = { 0, 9, 18, 27, 31 };
            int[] suitSize = { 9, 9, 9, 4, 3 };
            var lists = new List<byte>[5];
            for (int s = 0; s < 5; s++)
            {
                lists[s] = new List<byte>();
                for (int r = 0; r < suitSize[s]; r++) lists[s].Add((byte)(suitStart[s] + r));
                rng.Shuffle(lists[s]);
            }
            var chosen = new List<byte>();
            for (int round = 0; round < 9 && chosen.Count < k; round++)
                for (int s = 0; s < 5 && chosen.Count < k; s++)
                    if (round < lists[s].Count) chosen.Add(lists[s][round]);

            rng.Shuffle(chosen);
            int per = pairs / k, rem = pairs % k;
            var result = new List<byte>(pairs);
            for (int i = 0; i < chosen.Count; i++)
            {
                int c = per + (i < rem ? 1 : 0);
                for (int j = 0; j < c; j++) result.Add(chosen[i]);
            }
            rng.Shuffle(result);
            return result.ToArray();
        }
    }

    /// <summary>
    /// Solution-first board construction. A full legal clearing sequence (pairs of simultaneously free tiles) is found
    /// on the empty layout with a randomized, backtracking search, then tile kinds are assigned to the steps.
    /// The sequence is legal by construction, so every board is solvable. How far apart in time the pairs of one kind
    /// are placed is the main difficulty lever (hardness).
    /// </summary>
    public static class SolutionBuilder
    {
        sealed class Dec
        {
            public Layout L; public Board B; public Xoshiro256 Rng; public int Hardness;
            public int Budget = 40000, Nodes;
            public HashSet<ulong> Failed = new HashSet<ulong>();
            public int[] Order;
            public List<int>[] Free = new List<int>[80];
        }

        public static int LastNodes;

        static int Crit(Layout L, int x)
        {
            return L.Below[x].Length * 2 + L.Left[x].Length + L.Right[x].Length;
        }

        static int CountFree(Board b)
        {
            int c = 0;
            for (int i = 0; i < b.Present.Length; i++) if (b.IsFree(i)) { if (++c >= 2) return c; }
            return c;
        }

        /// <summary>Finds a legal pair-by-pair clearing order for the layout, or null.</summary>
        public static int[] Decompose(Layout L, int hardness, Xoshiro256 rng)
        {
            var d = new Dec { L = L, B = new Board(L, new byte[L.Count]), Rng = rng, Hardness = hardness, Order = new int[L.Count] };
            for (int i = 0; i < d.Free.Length; i++) d.Free[i] = new List<int>();
            bool ok = Dfs(d, 0);
            LastNodes = d.Nodes;
            return ok ? d.Order : null;
        }

        static bool Dfs(Dec d, int depth)
        {
            var b = d.B;
            if (b.Remaining == 0) return true;
            if (++d.Nodes > d.Budget) return false;
            ulong h = b.PresenceHash();
            if (d.Failed.Contains(h)) return false;
            var free = d.Free[depth];
            b.FreeTiles(free);
            int f = free.Count;
            if (f < 2) { d.Failed.Add(h); return false; }

            int pc = f * (f - 1) / 2;
            var pa = new int[pc]; var pb = new int[pc]; var pw = new int[pc];
            int k = 0;
            for (int i = 0; i < f; i++)
                for (int j = i + 1; j < f; j++)
                {
                    int a = free[i], c = free[j];
                    int dist = Math.Abs(d.L.Pos[a].X - d.L.Pos[c].X) + Math.Abs(d.L.Pos[a].Y - d.L.Pos[c].Y) +
                               4 * Math.Abs(d.L.Pos[a].Z - d.L.Pos[c].Z);
                    pa[k] = a; pb[k] = c;
                    pw[k] = 1 + Crit(d.L, a) + Crit(d.L, c) + d.Hardness * dist / 1000;
                    k++;
                }
            int tries = Math.Min(pc, 12);
            for (int t = 0; t < tries; t++)
            {
                int total = 0;
                for (int i = 0; i < pc; i++) total += pw[i];
                if (total <= 0) break;
                int roll = d.Rng.NextInt(total), pick = 0;
                for (int i = 0; i < pc; i++)
                {
                    roll -= pw[i];
                    if (roll < 0) { pick = i; break; }
                }
                int x = pa[pick], y = pb[pick];
                pw[pick] = 0;
                b.Remove(x, y);
                if (b.Remaining == 0 || CountFree(b) >= 2)
                {
                    d.Order[depth * 2] = x; d.Order[depth * 2 + 1] = y;
                    if (Dfs(d, depth + 1)) return true;
                }
                b.Restore(x, y);
                if (d.Nodes > d.Budget) return false;
            }
            d.Failed.Add(h);
            return false;
        }

        /// <summary>Orders pair kinds along the solution. Low hardness keeps the pairs of a kind together in time.</summary>
        public static byte[] Arrange(byte[] pairKinds, int hardness, Xoshiro256 rng)
        {
            int n = pairKinds.Length;
            var distinct = new List<byte>();
            var count = new int[Kinds.Count];
            for (int i = 0; i < n; i++)
            {
                if (count[pairKinds[i]]++ == 0) distinct.Add(pairKinds[i]);
            }
            rng.Shuffle(distinct);
            var arranged = new byte[n];
            int idx = 0;
            for (int i = 0; i < distinct.Count; i++)
                for (int c = 0; c < count[distinct[i]]; c++) arranged[idx++] = distinct[i];
            int swaps = n * hardness / 250;
            for (int s = 0; s < swaps; s++)
            {
                int a = rng.NextInt(n), b = rng.NextInt(n);
                byte tmp = arranged[a]; arranged[a] = arranged[b]; arranged[b] = tmp;
            }
            return arranged;
        }

        public static bool Build(Layout L, byte[] pairKinds, int hardness, Xoshiro256 rng, byte[] kindsOut, int[] solutionOut)
        {
            int n = L.Count;
            if (n % 2 != 0 || pairKinds.Length != n / 2) return false;
            var order = Decompose(L, hardness, rng.Fork("decomp"));
            if (order == null) return false;
            var arranged = Arrange(pairKinds, hardness, rng.Fork("arrange"));
            for (int i = 0; i < n / 2; i++)
            {
                int a = order[2 * i], c = order[2 * i + 1];
                solutionOut[2 * i] = a; solutionOut[2 * i + 1] = c;
                kindsOut[a] = arranged[i]; kindsOut[c] = arranged[i];
            }
            return true;
        }

        /// <summary>Replays the solution forward. This is the formal solvability proof used by tests and the generator.</summary>
        public static bool VerifySolution(Layout layout, byte[] kinds, int[] order)
        {
            var board = new Board(layout, kinds);
            for (int i = 0; i + 1 < order.Length; i += 2)
            {
                int a = order[i], b = order[i + 1];
                if (a == b || !board.IsFree(a) || !board.IsFree(b)) return false;
                if (!Kinds.Match(kinds[a], kinds[b])) return false;
                board.Remove(a, b);
            }
            return board.Remaining == 0;
        }
    }

    public struct RatingStats
    {
        public int WinPerMille, AvgFreePairsX100, Tiles, Layers, InitialFreePairs, KindsUsed, MaxStack, Score;
        public override string ToString()
        {
            return "tiles=" + Tiles + " layers=" + Layers + " kinds=" + KindsUsed + " free0=" + InitialFreePairs +
                   " win=" + WinPerMille + " avgFree=" + AvgFreePairsX100 + " stack=" + MaxStack + " score=" + Score;
        }
    }

    /// <summary>Integer-only difficulty estimate (1..10) from structure plus greedy-play simulations.</summary>
    public static class Rater
    {
        public static int ScoreOffset = 1500;
        public static int ScoreDivisor = 450;

        static int StackDepth(Layout L, int p, int[] memo)
        {
            if (memo[p] >= 0) return memo[p];
            int best = 1;
            var b = L.Below[p];
            for (int i = 0; i < b.Length; i++) best = Math.Max(best, 1 + StackDepth(L, b[i], memo));
            memo[p] = best;
            return best;
        }

        public static int Rate(Layout L, byte[] kinds, Xoshiro256 rng, out RatingStats stats)
        {
            var init = new Board(L, kinds);
            var free = new List<int>(); var pairs = new List<long>();
            init.GetFreePairs(free, pairs);
            stats.InitialFreePairs = pairs.Count;
            stats.Tiles = L.Count; stats.Layers = L.Layers;
            var seen = new bool[Kinds.Count]; int used = 0;
            for (int i = 0; i < kinds.Length; i++) if (!seen[kinds[i]]) { seen[kinds[i]] = true; used++; }
            stats.KindsUsed = used;
            var memo = new int[L.Count];
            for (int i = 0; i < memo.Length; i++) memo[i] = -1;
            int maxStack = 1;
            for (int i = 0; i < L.Count; i++) maxStack = Math.Max(maxStack, StackDepth(L, i, memo));
            stats.MaxStack = maxStack;

            int wins = 0, steps = 0, sumPairs = 0;
            int sims = DifficultyConfig.RaterSims;
            for (int s = 0; s < sims; s++)
            {
                var r = rng.Fork("sim" + s);
                var b = init.Clone();
                int step = 0;
                while (true)
                {
                    b.GetFreePairs(free, pairs);
                    if (pairs.Count == 0) break;
                    sumPairs += pairs.Count; steps++;
                    long mv;
                    if ((step & 1) == 0) mv = pairs[r.NextInt(pairs.Count)];
                    else
                    {
                        // smart move: the pair whose removal leaves the most free tiles
                        mv = pairs[0]; int bestFree = -1;
                        int lim = Math.Min(pairs.Count, 10);
                        for (int i = 0; i < lim; i++)
                        {
                            long pr = pairs[i];
                            b.Remove(Board.PairA(pr), Board.PairB(pr));
                            int f = 0;
                            for (int t = 0; t < b.Present.Length; t++) if (b.IsFree(t)) f++;
                            b.Restore(Board.PairA(pr), Board.PairB(pr));
                            if (f > bestFree) { bestFree = f; mv = pr; }
                        }
                    }
                    b.Remove(Board.PairA(mv), Board.PairB(mv));
                    step++;
                }
                if (b.Remaining == 0) wins++;
            }
            stats.WinPerMille = wins * 1000 / sims;
            stats.AvgFreePairsX100 = steps == 0 ? 0 : sumPairs * 100 / steps;

            int score = (1000 - stats.WinPerMille) * 4
                      + (stats.Tiles - 24) * 12
                      + (stats.Layers - 1) * 250
                      + Math.Max(0, 900 - stats.AvgFreePairsX100) * 2
                      - Math.Min(stats.InitialFreePairs, 12) * 60;
            stats.Score = score;
            return DifficultyConfig.Clamp(1 + (score - ScoreOffset) / ScoreDivisor, 1, 10);
        }
    }
}
