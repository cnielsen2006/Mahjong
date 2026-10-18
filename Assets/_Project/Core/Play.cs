using System;
using System.Collections.Generic;

namespace Mahjong.Core
{
    /// <summary>Bounded depth-first solver with perfect information. Used for hints and for tests.</summary>
    public static class Solver
    {
        sealed class Search
        {
            public Board B;
            public int Budget, Nodes;
            public bool Aborted;
            public HashSet<ulong> Failed = new HashSet<ulong>();
            public long First = -1;
            public int Depth0Pick = -1;
            public List<long>[] PairLists = new List<long>[80];
            public List<int>[] FreeLists = new List<int>[80];
        }

        /// <summary>Finds a winning first move from the current state, or -1 if none found within the node budget.</summary>
        public static long FindWinningMove(Board board, int nodeBudget)
        {
            var s = new Search { B = board.Clone(), Budget = nodeBudget };
            for (int i = 0; i < s.PairLists.Length; i++) { s.PairLists[i] = new List<long>(); s.FreeLists[i] = new List<int>(); }
            s.First = -1;
            bool ok = Dfs(s, 0);
            return ok ? s.First : -1;
        }

        public static bool IsSolvable(Board board, int nodeBudget, out bool aborted)
        {
            var s = new Search { B = board.Clone(), Budget = nodeBudget };
            for (int i = 0; i < s.PairLists.Length; i++) { s.PairLists[i] = new List<long>(); s.FreeLists[i] = new List<int>(); }
            bool ok = Dfs(s, 0);
            aborted = s.Aborted;
            return ok;
        }

        static bool Dfs(Search s, int depth)
        {
            var b = s.B;
            if (b.Remaining == 0) return true;
            if (++s.Nodes > s.Budget) { s.Aborted = true; return false; }
            ulong h = b.PresenceHash();
            if (s.Failed.Contains(h)) return false;

            var pairs = s.PairLists[depth];
            b.GetFreePairs(s.FreeLists[depth], pairs);
            if (pairs.Count == 0) { s.Failed.Add(h); return false; }

            // order: pairs of kinds with only two tiles left first, then by how many tiles the move frees
            int cnt = pairs.Count;
            var keys = new long[cnt];
            var order = new long[cnt];
            for (int i = 0; i < cnt; i++)
            {
                long pr = pairs[i];
                int a = Board.PairA(pr), c = Board.PairB(pr);
                int g = Kinds.Group(b.KindAt[a]);
                int left = 0;
                for (int t = 0; t < b.Present.Length; t++) if (b.Present[t] && Kinds.Group(b.KindAt[t]) == g) left++;
                b.Remove(a, c);
                int f = 0;
                for (int t = 0; t < b.Present.Length; t++) if (b.IsFree(t)) f++;
                b.Restore(a, c);
                keys[i] = (left == 2 ? 1000 : 0) + f;
                order[i] = pr;
            }
            // simple insertion sort, descending key, stable
            for (int i = 1; i < cnt; i++)
            {
                long kv = keys[i], ov = order[i]; int j = i - 1;
                while (j >= 0 && keys[j] < kv) { keys[j + 1] = keys[j]; order[j + 1] = order[j]; j--; }
                keys[j + 1] = kv; order[j + 1] = ov;
            }

            for (int i = 0; i < cnt; i++)
            {
                int a = Board.PairA(order[i]), c = Board.PairB(order[i]);
                b.Remove(a, c);
                if (Dfs(s, depth + 1))
                {
                    if (depth == 0) s.First = order[i];
                    return true;
                }
                b.Restore(a, c);
                if (s.Aborted) return false;
            }
            s.Failed.Add(h);
            return false;
        }
    }

    public static class Shuffler
    {
        /// <summary>
        /// Re-deals the remaining tiles so that a winning line exists from the new state. Deterministic for
        /// (levelSeed, shuffleIndex, current present set). Returns false if only a best-effort shuffle was possible.
        /// </summary>
        public static bool Shuffle(Board board, ulong levelSeed, int shuffleIndex)
        {
            var ids = new List<int>();
            for (int i = 0; i < board.Present.Length; i++) if (board.Present[i]) ids.Add(i);
            int n = ids.Count;
            if (n < 2) return true;

            var counts = new int[Kinds.Count];
            for (int i = 0; i < n; i++) counts[Kinds.Group(board.KindAt[ids[i]])]++;
            var pairKinds = new List<byte>();
            for (int g = 0; g < Kinds.Count; g++)
                for (int c = 0; c < counts[g] / 2; c++) pairKinds.Add((byte)g);

            var sub = board.Layout.Subset(ids);
            ulong baseSeed = Mix.Next(levelSeed ^ (Seeds.ShuffleSalt + (ulong)shuffleIndex));
            for (int attempt = 0; attempt < 24; attempt++)
            {
                var rng = new Xoshiro256(Seeds.ForAttempt(baseSeed, attempt));
                var pk = pairKinds.ToArray();
                rng.Shuffle(pk);
                var kinds = new byte[n]; var sol = new int[n];
                if (!SolutionBuilder.Build(sub, pk, 0, rng.Fork("place"), kinds, sol)) continue;
                if (!SolutionBuilder.VerifySolution(sub, kinds, sol)) continue;
                for (int i = 0; i < n; i++) board.KindAt[ids[i]] = kinds[i];
                return true;
            }

            // Best effort: random deal that at least has a move.
            var all = new List<byte>();
            for (int i = 0; i < n; i++) all.Add(board.KindAt[ids[i]]);
            var r2 = new Xoshiro256(baseSeed ^ 0xB5AD4ECEDA1CE2A9UL);
            for (int attempt = 0; attempt < 200; attempt++)
            {
                r2.Shuffle(all);
                for (int i = 0; i < n; i++) board.KindAt[ids[i]] = all[i];
                if (board.HasMove()) break;
            }
            return false;
        }
    }

    public static class Hinter
    {
        /// <summary>A pair that keeps the board winnable when the solver can prove one, else a heuristic pair. -1 if no move.</summary>
        public static long Hint(Board board)
        {
            long w = Solver.FindWinningMove(board, 6000);
            if (w >= 0) return w;
            var free = new List<int>(); var pairs = new List<long>();
            board.GetFreePairs(free, pairs);
            if (pairs.Count == 0) return -1;
            long best = pairs[0]; int bestScore = int.MinValue;
            for (int i = 0; i < pairs.Count; i++)
            {
                int a = Board.PairA(pairs[i]), b = Board.PairB(pairs[i]);
                int g = Kinds.Group(board.KindAt[a]);
                int left = 0;
                for (int t = 0; t < board.Present.Length; t++) if (board.Present[t] && Kinds.Group(board.KindAt[t]) == g) left++;
                board.Remove(a, b);
                int f = 0;
                for (int t = 0; t < board.Present.Length; t++) if (board.IsFree(t)) f++;
                board.Restore(a, b);
                int sc = f * 4 + left;
                if (sc > bestScore) { bestScore = sc; best = pairs[i]; }
            }
            return best;
        }
    }

    /// <summary>One playthrough of a level: board, undo history, counters and the replay log.</summary>
    public sealed class GameSession
    {
        sealed class Op
        {
            public bool IsShuffle; public int A, B; public byte[] OldKinds;
        }

        public readonly Level Level;
        public readonly Board Board;
        readonly List<Op> history = new List<Op>();
        public readonly List<int> Log = new List<int>();
        int shuffleCounter;

        public int Hints, Undos, Shuffles, Pairs, BestCombo;

        // replay codes
        public const int CodeShuffle = -1, CodeUndo = -2, CodeHint = -3;

        public GameSession(Level level)
        {
            Level = level;
            Board = new Board(level.Layout, level.Kinds);
        }

        public bool IsWon { get { return Board.Remaining == 0; } }
        public bool IsStuck { get { return Board.Remaining > 0 && !Board.HasMove(); } }
        public bool CanUndo { get { return history.Count > 0; } }

        public bool Match(int a, int b)
        {
            if (a == b || a < 0 || b < 0 || a >= Board.Present.Length || b >= Board.Present.Length) return false;
            if (!Board.IsFree(a) || !Board.IsFree(b)) return false;
            if (!Kinds.Match(Board.KindAt[a], Board.KindAt[b])) return false;
            Board.Remove(a, b);
            history.Add(new Op { A = a, B = b });
            Log.Add((a << 16) | b);
            Pairs++;
            return true;
        }

        public bool Undo()
        {
            if (history.Count == 0) return false;
            var op = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            if (op.IsShuffle) Board.SetKinds(op.OldKinds);
            else { Board.Restore(op.A, op.B); Pairs--; }
            Undos++;
            Log.Add(CodeUndo);
            return true;
        }

        /// <summary>Returns true if the shuffle came with a solvability guarantee.</summary>
        public bool Shuffle()
        {
            var old = (byte[])Board.KindAt.Clone();
            bool ok = Shuffler.Shuffle(Board, Level.Seed, shuffleCounter++);
            history.Add(new Op { IsShuffle = true, OldKinds = old });
            Shuffles++;
            Log.Add(CodeShuffle);
            return ok;
        }

        /// <summary>Returns a packed pair or -1.</summary>
        public long Hint()
        {
            long h = Hinter.Hint(Board);
            if (h >= 0) { Hints++; Log.Add(CodeHint); }
            return h;
        }

        public int Stars()
        {
            if (Shuffles == 0 && Hints <= 1) return 3;
            if (Shuffles <= 1 || Hints <= 3) return 2;
            return 1;
        }

        /// <summary>Re-applies a saved log onto a fresh session (resume support).</summary>
        public void Replay(IList<int> log)
        {
            for (int i = 0; i < log.Count; i++)
            {
                int c = log[i];
                if (c == CodeShuffle) Shuffle();
                else if (c == CodeUndo) Undo();
                else if (c == CodeHint) { Hints++; Log.Add(CodeHint); }
                else Match(c >> 16, c & 0xFFFF);
            }
        }
    }
}
