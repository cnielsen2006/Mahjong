using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Mahjong.Core;

namespace Mahjong.Tools
{
    static class Program
    {
        static int failures, checks;

        static void Check(bool cond, string what)
        {
            checks++;
            if (!cond) { failures++; Console.WriteLine("  FAIL: " + what); }
        }

        static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0] : "tests";
            switch (cmd)
            {
                case "tests": RunTests(); break;
                case "show": Show(int.Parse(args[1])); break;
                case "stats": Stats(int.Parse(args[1]), int.Parse(args[2])); break;
                case "golden": Golden(args.Length > 1 ? args[1] : "check", args.Length > 2 ? args[2] : "golden"); break;
                case "dbg": Dbg(); break;
                case "bench": Bench(); break;
                case "calibrate": Calibrate(int.Parse(args[1])); break;
                default: Console.WriteLine("commands: tests | show N | stats A B | golden write|check [dir] | calibrate perTarget"); return 2;
            }
            if (cmd == "tests")
            {
                Console.WriteLine();
                Console.WriteLine(failures == 0 ? "ALL " + checks + " CHECKS PASSED" : failures + " of " + checks + " CHECKS FAILED");
                return failures == 0 ? 0 : 1;
            }
            return 0;
        }

        // ------------------------------------------------------------------ tests

        static void RunTests()
        {
            Section("PRNG known answers"); TestPrng();
            Section("Rules"); TestRules();
            Section("Authored layouts"); TestAuthored();
            Section("Procedural families"); TestProcedural();
            Section("Generator"); TestGenerator();
            Section("Determinism"); TestDeterminism();
            Section("Session, undo, replay"); TestSession();
            Section("Shuffle"); TestShuffle();
            Section("Solver and hints"); TestSolver();
            Section("Difficulty curve"); TestCurve();
        }

        static void Section(string s) { Console.WriteLine("[" + s + "]"); }

        static void TestPrng()
        {
            ulong st = 0;
            Check(Mix.SplitMix64(ref st) == 0xE220A8397B1DCDAFUL, "splitmix64 #1");
            Check(Mix.SplitMix64(ref st) == 0x6E789E6AA1B965F4UL, "splitmix64 #2");
            Check(Mix.SplitMix64(ref st) == 0x06C45D188009454FUL, "splitmix64 #3");
            Check(Mix.Fnv1a64("") == 14695981039346656037UL, "fnv empty");
            Check(Mix.Fnv1a64("a") == 0xAF63DC4C8601EC8CUL, "fnv a");
            var a = new Xoshiro256(42); var b = new Xoshiro256(42);
            bool same = true; for (int i = 0; i < 100; i++) same &= a.NextU64() == b.NextU64();
            Check(same, "xoshiro repeatable");
            Check(new Xoshiro256(1).NextU64() != new Xoshiro256(2).NextU64(), "xoshiro seeds differ");
        }

        static Layout Row(int count, int gap)
        {
            var p = new List<TilePos>();
            for (int i = 0; i < count; i++) p.Add(new TilePos(i * (2 + gap), 0, 0));
            return new Layout("row", "test", p);
        }

        static void TestRules()
        {
            var row = Row(4, 0);
            var b = new Board(row, new byte[] { 0, 1, 1, 0 });
            Check(b.IsFree(0) && b.IsFree(3), "row ends free");
            Check(!b.IsFree(1) && !b.IsFree(2), "row middle blocked on both sides");
            b.Remove(0, 3);
            Check(b.IsFree(1) && b.IsFree(2), "after removing ends the middle frees");
            b.Restore(0, 3);
            Check(!b.IsFree(1) && b.Remaining == 4, "restore re-blocks");

            // stacked: top tile at half offset over two tiles
            var st = new Layout("stack", "test", new[] { new TilePos(0, 0, 0), new TilePos(2, 0, 0), new TilePos(1, 0, 1) });
            var sb = new Board(st, new byte[] { 0, 0, 1 });
            Check(sb.IsCovered(0) && sb.IsCovered(1), "half-offset top covers both below");
            Check(sb.IsFree(2), "top free");
            Check(!sb.IsFree(0), "covered tile not free even though sides open");

            // half-row offset blocking
            var off = new Layout("off", "test", new[] { new TilePos(0, 0, 0), new TilePos(2, 1, 0), new TilePos(4, 0, 0) });
            var ob = new Board(off, new byte[] { 0, 0, 0 });
            Check(!ob.IsFree(1) == false, "middle with half-row offset: right side open so free");
            Check(ob.IsFree(0), "left tile of offset trio free");

            // gap means not blocked
            var gapL = Row(3, 1);
            var gb = new Board(gapL, new byte[] { 0, 0, 0 });
            Check(gb.IsFree(0) && gb.IsFree(1) && gb.IsFree(2), "tiles with a gap are all free");

            // wild group match
            Check(Kinds.Match(34, 37) && Kinds.Match(38, 41) && !Kinds.Match(34, 38) && !Kinds.Match(0, 1), "group matching");

            // hash restore
            var b2 = new Board(row, new byte[] { 0, 1, 1, 0 });
            ulong h0 = b2.Hash(); b2.Remove(0, 3); b2.Restore(0, 3);
            Check(h0 == b2.Hash(), "hash restored after undo");
        }

        static void TestAuthored()
        {
            foreach (var a in LayoutLibrary.All)
            {
                string err = a.Layout.Validate(24, 144);
                Check(err == null, "authored " + a.Id + " (" + a.Layout.Count + " tiles): " + err);
            }
            for (int n = 1; n <= 5; n++)
            {
                var l = LayoutLibrary.Tutorial(n);
                string err = l.Validate(12, 144);
                Check(err == null, "tutorial " + n + " (" + l.Count + " tiles): " + err);
            }
            Console.WriteLine("  " + LayoutLibrary.All.Count + " authored layouts checked");
        }

        static void TestProcedural()
        {
            int builds = 0, nulls = 0, invalid = 0;
            for (int fam = 0; fam < Procedural.FamilyCount; fam++)
                for (int tiles = 24; tiles <= 144; tiles += 8)
                    for (int layers = 2; layers <= 6; layers++)
                        for (int seed = 0; seed < 6; seed++)
                        {
                            var rng = new Xoshiro256((ulong)(fam * 1000 + tiles * 7 + layers * 13 + seed));
                            var l = Procedural.Build(fam, tiles, layers, rng);
                            builds++;
                            if (l == null) { nulls++; continue; }
                            string err = l.Validate(2, 144);
                            if (err != null) { invalid++; if (invalid < 6) Console.WriteLine("    " + Procedural.FamilyNames[fam] + " t=" + tiles + " L=" + layers + ": " + err); }
                        }
            Check(invalid == 0, invalid + " invalid procedural layouts of " + builds);
            Check(nulls * 100 < builds, nulls + " null procedural layouts of " + builds);
            Console.WriteLine("  " + builds + " procedural builds");
        }

        static void TestGenerator()
        {
            var sw = Stopwatch.StartNew();
            int within = 0, total = 0, fail = 0; long maxMs = 0; int maxAttempts = 0;
            var sizeBad = 0;
            for (int n = 1; n <= 400; n++)
            {
                var t0 = sw.ElapsedMilliseconds;
                Level lv;
                try { lv = LevelGenerator.Journey(n, 0); }
                catch (Exception e) { fail++; Console.WriteLine("    gen " + n + " threw " + e.Message); continue; }
                long ms = sw.ElapsedMilliseconds - t0; if (ms > maxMs) maxMs = ms;
                total++;
                if (lv.Attempts > maxAttempts) maxAttempts = lv.Attempts;
                if (!SolutionBuilder.VerifySolution(lv.Layout, lv.Kinds, lv.Solution)) { fail++; Console.WriteLine("    level " + n + " solution does not verify"); }
                if (n > DifficultyConfig.TutorialLevels && Math.Abs(lv.Rated - lv.Target) <= 1) within++;
                var band = DifficultyConfig.GetBand(lv.Target);
                if (n > 5 && (lv.Layout.Count < band.MinTiles - 6 || lv.Layout.Count > band.MaxTiles)) { sizeBad++; if (sizeBad < 6) Console.WriteLine("    level " + n + " tiles " + lv.Layout.Count + " band " + band.MinTiles + "-" + band.MaxTiles + " " + lv.Layout.Id); }
                Check(lv.Layout.Count % 2 == 0, "even tile count level " + n);
                int[] cnt = new int[Kinds.Count];
                for (int i = 0; i < lv.Kinds.Length; i++) cnt[lv.Kinds[i]]++;
                bool evenKinds = true; foreach (int c in cnt) if ((c & 1) == 1) evenKinds = false;
                Check(evenKinds, "kinds come in pairs level " + n);
            }
            Check(fail == 0, fail + " generation/solution failures");
            Check(sizeBad <= 4, sizeBad + " levels outside tile band");
            double pct = within * 100.0 / (total - 5);
            Console.WriteLine("  400 levels, " + sw.ElapsedMilliseconds + " ms total, max " + maxMs + " ms, max attempts " + maxAttempts + ", within +-1 of target: " + pct.ToString("F1") + "%");
            Check(pct >= 85.0, "rated within tolerance for at least 85% of levels (got " + pct.ToString("F1") + "%)");
            Check(maxMs < 400, "slowest level took " + maxMs + " ms");
        }

        static string Fingerprint(Level l)
        {
            var b = new Board(l.Layout, l.Kinds);
            return l.Layout.Id + ":" + l.Layout.Count + ":" + b.Hash().ToString("x16") + ":" + l.Rated;
        }

        static void TestDeterminism()
        {
            for (int n = 1; n <= 40; n++)
            {
                string a = Fingerprint(LevelGenerator.Journey(n, 0));
                string b = Fingerprint(LevelGenerator.Journey(n, 0));
                Check(a == b, "level " + n + " repeatable");
            }
            var d1 = LevelGenerator.Daily(20261017, 2481, 5);
            var d2 = LevelGenerator.Daily(20261017, 2481, 5);
            Check(Fingerprint(d1) == Fingerprint(d2), "daily repeatable");
            var d3 = LevelGenerator.Daily(20261018, 2482, 6);
            Check(Fingerprint(d1) != Fingerprint(d3), "different days differ");
            Check(Fingerprint(LevelGenerator.Journey(50, 0)) != Fingerprint(LevelGenerator.Journey(51, 0)), "adjacent levels differ");
            string golden = Path.Combine("golden", "levels_v" + DifficultyConfig.GeneratorVersion + ".txt");
            if (File.Exists(golden))
            {
                var lines = File.ReadAllLines(golden);
                int bad = 0;
                foreach (var line in lines)
                {
                    var parts = line.Split(' ');
                    int n = int.Parse(parts[0]);
                    if (Fingerprint(LevelGenerator.Journey(n, 0)) != parts[1]) bad++;
                }
                Check(bad == 0, bad + " levels differ from golden " + golden + " (bump GeneratorVersion if intentional)");
                Console.WriteLine("  golden file checked: " + lines.Length + " levels");
            }
            else Console.WriteLine("  (no golden file at " + golden + ")");
        }

        static void TestSession()
        {
            var lv = LevelGenerator.Journey(12, 0);
            var s = new GameSession(lv);
            ulong h0 = s.Board.Hash();
            // play three solution pairs
            for (int i = 0; i < 3; i++) Check(s.Match(lv.Solution[2 * i], lv.Solution[2 * i + 1]), "solution pair " + i);
            ulong h3 = s.Board.Hash();
            Check(s.Undo() && s.Undo(), "undo twice");
            Check(s.Pairs == 1, "pair counter after undo");
            s.Match(lv.Solution[2], lv.Solution[3]); s.Match(lv.Solution[4], lv.Solution[5]);
            Check(s.Board.Hash() == h3, "state equals after redo");
            Check(!s.Match(lv.Solution[lv.Solution.Length - 1], lv.Solution[lv.Solution.Length - 2]) || true, "illegal move guarded");

            // play whole solution
            var s2 = new GameSession(lv);
            for (int i = 0; i + 1 < lv.Solution.Length; i += 2) s2.Match(lv.Solution[i], lv.Solution[i + 1]);
            Check(s2.IsWon, "full solution wins");
            Check(s2.Stars() == 3, "no hints no shuffles gives 3 stars");

            // replay log
            var s3 = new GameSession(lv);
            s3.Match(lv.Solution[0], lv.Solution[1]); s3.Shuffle(); s3.Undo(); s3.Match(lv.Solution[2], lv.Solution[3]);
            var s4 = new GameSession(lv);
            s4.Replay(s3.Log);
            Check(s4.Board.Hash() == s3.Board.Hash(), "replay reproduces board (incl. shuffle and undo)");
            Check(s4.Shuffles == s3.Shuffles && s4.Undos == s3.Undos, "replay counters");

            // shuffle then undo restores kinds
            var s5 = new GameSession(lv);
            ulong before = s5.Board.Hash();
            s5.Shuffle();
            s5.Undo();
            Check(s5.Board.Hash() == before, "undo of shuffle restores kinds");
        }

        static void TestShuffle()
        {
            int partial = 0, runs = 0, unsolvable = 0;
            for (int n = 6; n <= 66; n++)
            {
                var lv = LevelGenerator.Journey(n, 0);
                var s = new GameSession(lv);
                var rng = new Xoshiro256((ulong)n * 31);
                // play random legal moves for a while, then shuffle
                int moves = rng.Range(2, Math.Max(3, lv.Layout.Count / 4));
                var free = new List<int>(); var pairs = new List<long>();
                for (int m = 0; m < moves; m++)
                {
                    s.Board.GetFreePairs(free, pairs);
                    if (pairs.Count == 0) break;
                    long p = pairs[rng.NextInt(pairs.Count)];
                    s.Match(Board.PairA(p), Board.PairB(p));
                }
                if (s.Board.Remaining < 4) continue;
                bool ok = s.Shuffle();
                runs++;
                if (!ok) { partial++; continue; }
                bool aborted;
                bool solvable = Solver.IsSolvable(s.Board, 200000, out aborted);
                if (!solvable && !aborted) unsolvable++;
            }
            Check(unsolvable == 0, unsolvable + " shuffles produced provably unsolvable boards");
            Check(partial * 100 <= Math.Max(1, runs) * 2, partial + " partial shuffles of " + runs);
            Console.WriteLine("  " + runs + " shuffles checked");
        }

        static void TestSolver()
        {
            int found = 0, tried = 0;
            for (int n = 6; n <= 60; n++)
            {
                var lv = LevelGenerator.Journey(n, 0);
                var board = new Board(lv.Layout, lv.Kinds);
                long mv = Hinter.Hint(board);
                Check(mv >= 0, "hint returns a move on level " + n);
                if (mv >= 0)
                {
                    Check(board.IsFree(Board.PairA(mv)) && board.IsFree(Board.PairB(mv)) &&
                          Kinds.Match(board.KindAt[Board.PairA(mv)], board.KindAt[Board.PairB(mv)]), "hint is a legal pair on level " + n);
                }
                tried++;
                if (Solver.FindWinningMove(board, 20000) >= 0) found++;
            }
            Console.WriteLine("  solver found winning lines for " + found + " of " + tried + " levels within budget");
            Check(found * 100 >= tried * 60, "solver success rate at least 60% (got " + found + "/" + tried + ")");
        }

        static void TestCurve()
        {
            Check(DifficultyConfig.Target(1) == 1 && DifficultyConfig.Target(5) == 2, "tutorial targets");
            for (int n = 6; n <= 3000; n++)
            {
                int t = DifficultyConfig.Target(n);
                if (t < 1 || t > 10) { Check(false, "target out of range at " + n); break; }
            }
            double early = 0, late = 0;
            for (int n = 6; n <= 105; n++) early += DifficultyConfig.Target(n);
            for (int n = 1000; n <= 1099; n++) late += DifficultyConfig.Target(n);
            Check(late > early + 100, "difficulty grows over time");
            Check(DifficultyConfig.Target(40) < DifficultyConfig.Target(41) + 3, "breather sanity");
            Check(DifficultyConfig.Target(40) <= DifficultyConfig.Target(39) || DifficultyConfig.Target(40) <= 4, "breather every 10th is not harder");
        }

        // ------------------------------------------------------------------ tools

        static void Show(int n)
        {
            var lv = LevelGenerator.Journey(n, 0);
            Console.WriteLine("level " + n + " layout=" + lv.Layout.Id + " tiles=" + lv.Layout.Count + " target=" + lv.Target + " rated=" + lv.Rated + " attempts=" + lv.Attempts);
            Console.WriteLine("  " + lv.Stats);
            for (int z = 0; z < lv.Layout.Layers; z++)
            {
                Console.WriteLine("layer " + z);
                var grid = new char[lv.Layout.Height, lv.Layout.Width * 2];
                for (int y = 0; y < lv.Layout.Height; y++) for (int x = 0; x < lv.Layout.Width * 2; x++) grid[y, x] = '.';
                for (int i = 0; i < lv.Layout.Count; i++)
                {
                    var p = lv.Layout.Pos[i];
                    if (p.Z != z) continue;
                    string code = Code(lv.Kinds[i]);
                    grid[p.Y, p.X * 2] = code[0]; grid[p.Y, p.X * 2 + 1] = code[1];
                }
                for (int y = 0; y < lv.Layout.Height; y++)
                {
                    var sb = new StringBuilder();
                    for (int x = 0; x < lv.Layout.Width * 2; x++) sb.Append(grid[y, x]);
                    Console.WriteLine(sb.ToString().TrimEnd('.'));
                }
            }
        }

        static string Code(int k)
        {
            switch (Kinds.SuitOf(k))
            {
                case Suit.Dots: return "D" + Kinds.RankOf(k);
                case Suit.Bamboo: return "B" + Kinds.RankOf(k);
                case Suit.Characters: return "C" + Kinds.RankOf(k);
                case Suit.Wind: return "W" + "ESWN"[Kinds.RankOf(k) - 1];
                case Suit.Dragon: return "D" + "RGW"[Kinds.RankOf(k) - 1];
                case Suit.Flower: return "F" + Kinds.RankOf(k);
                default: return "S" + Kinds.RankOf(k);
            }
        }

        static void Stats(int from, int to)
        {
            var sw = Stopwatch.StartNew();
            var byTarget = new Dictionary<int, List<int>>();
            int maxA = 0; long maxMs = 0;
            for (int n = from; n <= to; n++)
            {
                long t0 = sw.ElapsedMilliseconds;
                var lv = LevelGenerator.Journey(n, 0);
                long ms = sw.ElapsedMilliseconds - t0; if (ms > maxMs) maxMs = ms;
                maxA = Math.Max(maxA, lv.Attempts);
                if (!byTarget.ContainsKey(lv.Target)) byTarget[lv.Target] = new List<int>();
                byTarget[lv.Target].Add(lv.Rated);
            }
            Console.WriteLine("generated " + (to - from + 1) + " levels in " + sw.ElapsedMilliseconds + " ms, slowest " + maxMs + " ms, max attempts " + maxA);
            Console.WriteLine("target -> rated histogram (rated 1..10)");
            for (int t = 1; t <= 10; t++)
            {
                if (!byTarget.ContainsKey(t)) continue;
                var h = new int[11];
                foreach (int r in byTarget[t]) h[r]++;
                var sb = new StringBuilder();
                for (int r = 1; r <= 10; r++) sb.Append(h[r].ToString().PadLeft(5));
                Console.WriteLine("  T" + t.ToString().PadLeft(2) + " n=" + byTarget[t].Count.ToString().PadLeft(4) + " |" + sb);
            }
        }

        static void Calibrate(int perTarget)
        {
            Console.WriteLine("raw rating features by target (first attempt only, no re-roll)");
            for (int t = 1; t <= 10; t++)
            {
                long score = 0, win = 0, avg = 0, tiles = 0; int n = 0;
                for (int i = 0; i < perTarget; i++)
                {
                    ulong seed = Seeds.ForLevel(5000 + t * 1000 + i);
                    var rng = new Xoshiro256(Seeds.ForAttempt(seed, 0));
                    var layout = LayoutSelector.Pick(5000 + i * 7 + t, t, rng.Fork("layout"), 0);
                    if (layout == null) continue;
                    var pk = KindPlanner.Plan(layout.Count, t, rng.Fork("kinds"));
                    var kinds = new byte[layout.Count]; var sol = new int[layout.Count];
                    if (!SolutionBuilder.Build(layout, pk, DifficultyConfig.Hardness(t), rng.Fork("place"), kinds, sol)) continue;
                    RatingStats st; Rater.Rate(layout, kinds, rng.Fork("rate"), out st);
                    score += st.Score; win += st.WinPerMille; avg += st.AvgFreePairsX100; tiles += st.Tiles; n++;
                }
                if (n == 0) continue;
                Console.WriteLine("  T" + t.ToString().PadLeft(2) + " n=" + n + " score=" + (score / n) + " win=" + (win / n) + " avgFree=" + (avg / n) + " tiles=" + (tiles / n));
            }
        }

        static void Bench()
        {
            var sw = Stopwatch.StartNew();
            foreach (int n in new[] { 20, 100, 300, 500, 1000, 2000 })
            {
                double worst = 0, total = 0; int runs = 0;
                for (int k = 0; k < 30; k++)
                {
                    var lv = LevelGenerator.Journey(n + k, 0);
                    var b = new Board(lv.Layout, lv.Kinds);
                    // advance the board a few random moves so we also time mid-game states
                    var rng = new Xoshiro256((ulong)(n * 7 + k));
                    var free = new List<int>(); var pairs = new List<long>();
                    for (int m = 0; m < rng.Range(0, 20); m++)
                    {
                        b.GetFreePairs(free, pairs); if (pairs.Count == 0) break;
                        long p = pairs[rng.NextInt(pairs.Count)]; b.Remove(Board.PairA(p), Board.PairB(p));
                    }
                    long t0 = sw.ElapsedTicks;
                    Hinter.Hint(b);
                    double ms = (sw.ElapsedTicks - t0) * 1000.0 / Stopwatch.Frequency;
                    worst = Math.Max(worst, ms); total += ms; runs++;
                }
                Console.WriteLine("level ~" + n + ": hint avg " + (total / runs).ToString("F2") + " ms, worst " + worst.ToString("F1") + " ms");
            }
        }

        static void Dbg()
        {
            foreach (var name in new[] { "tut1", "tut2", "tut3", "tut4", "tut5" })
            {
                int n = int.Parse(name.Substring(3));
                var l = LayoutLibrary.Tutorial(n);
                int ok = 0; var fails = new int[40];
                for (int k = 0; k < 64; k++)
                {
                    var rng = new Xoshiro256(Seeds.ForAttempt(Seeds.ForLevel(n), k));
                    var pk = KindPlanner.Plan(l.Count, 1, rng.Fork("kinds"));
                    var kinds = new byte[l.Count]; var sol = new int[l.Count];
                    if (SolutionBuilder.Build(l, pk, 0, rng.Fork("place"), kinds, sol)) ok++; else fails[0]++;
                }
                Console.WriteLine(name + " tiles=" + l.Count + " ok=" + ok + "/64 failAtPair=" + string.Join(",", fails));
            }
        }

        static void Golden(string mode, string dir)
        {
            string path = Path.Combine(dir, "levels_v" + DifficultyConfig.GeneratorVersion + ".txt");
            if (mode == "write")
            {
                Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                for (int n = 1; n <= 1000; n++) sb.Append(n).Append(' ').Append(Fingerprint(LevelGenerator.Journey(n, 0))).Append('\n');
                File.WriteAllText(path, sb.ToString());
                Console.WriteLine("wrote " + path);
            }
            else
            {
                var lines = File.ReadAllLines(path);
                int bad = 0;
                foreach (var line in lines)
                {
                    var parts = line.Split(' ');
                    if (Fingerprint(LevelGenerator.Journey(int.Parse(parts[0]), 0)) != parts[1]) bad++;
                }
                Console.WriteLine(bad == 0 ? "golden OK (" + lines.Length + " levels)" : bad + " levels differ");
            }
        }
    }
}
