using System;
using System.Collections.Generic;

namespace Mahjong.Core
{
    /// <summary>Procedural layout families. All integer math, all driven by the supplied Xoshiro256.</summary>
    public static class Procedural
    {
        public const int FamilyCount = 4;
        public static readonly string[] FamilyNames = { "pyramid", "blob", "terraces", "towers" };

        const int GW = 14, GH = 8;   // base grid in full tiles
        const int W2 = GW * 2, H2 = GH * 2;

        public static Layout Build(int family, int targetTiles, int maxLayers, Xoshiro256 rng)
        {
            List<TilePos> pos;
            switch (family)
            {
                case 0: pos = Pyramid(targetTiles, maxLayers, rng); break;
                case 1: pos = Grown(targetTiles, maxLayers, rng, 1); break;
                case 2: pos = Grown(targetTiles, maxLayers, rng, 2); break;
                default: pos = Grown(targetTiles, maxLayers, rng, 3); break;
            }
            if (pos == null || pos.Count < 2) return null;
            return new Layout(FamilyNames[family], "procedural", Finalize(pos, targetTiles));
        }

        // ---------------------------------------------------------------- pyramid

        static int PyramidTotal(int w, int h, int maxLayers)
        {
            int t = 0;
            for (int k = 0; k < maxLayers && w - k >= 1 && h - k >= 1; k++) t += (w - k) * (h - k);
            return t;
        }

        static List<TilePos> Pyramid(int target, int maxLayers, Xoshiro256 rng)
        {
            var cands = new List<int>();   // packed w*100+h
            int bestDiff = int.MaxValue, bestPacked = 0;
            for (int h = 2; h <= 8; h++)
            {
                for (int w = h; w <= 15; w++)
                {
                    int total = PyramidTotal(w, h, maxLayers);
                    int diff = Math.Abs(total - target);
                    if (total >= target && total <= target + 6) cands.Add(w * 100 + h);
                    if (diff < bestDiff) { bestDiff = diff; bestPacked = w * 100 + h; }
                }
            }
            int pick = cands.Count > 0 ? cands[rng.NextInt(cands.Count)] : bestPacked;
            int pw = pick / 100, ph = pick % 100;
            var pos = new List<TilePos>();
            for (int k = 0; k < maxLayers && pw - k >= 1 && ph - k >= 1; k++)
                for (int r = 0; r < ph - k; r++)
                    for (int c = 0; c < pw - k; c++)
                        pos.Add(new TilePos(k + 2 * c, k + 2 * r, k));
            return pos;
        }

        // ---------------------------------------------------------------- grown families

        static List<TilePos> Grown(int target, int maxLayers, Xoshiro256 rng, int kind)
        {
            int share = maxLayers <= 2 ? 62 : (maxLayers == 3 ? 50 : (maxLayers == 4 ? 42 : 36));  // % of tiles on layer 0
            int baseCount = Math.Max(8, (target * share / 100) & ~1);
            List<TilePos> best = null;
            for (int it = 0; it < 8; it++)
            {
                var r = rng.Fork("it" + it);
                var bas = kind == 1 ? BlobBase(baseCount, r) : (kind == 2 ? TerraceBase(baseCount, r) : TowerBase(baseCount, r));
                var all = AddUpper(bas, target, maxLayers, r);
                if (best == null || all.Count > best.Count) best = all;
                if (all.Count >= target - 1) return all;
                baseCount += Math.Max(2, ((target - all.Count) / 2) & ~1);
            }
            return best;
        }

        static List<TilePos> BlobBase(int count, Xoshiro256 rng)
        {
            var occ = new bool[GW, GH];
            int n = 0;
            int cx = GW / 2 - 1, cy = GH / 2;
            occ[cx, cy] = true; occ[GW - 1 - cx, cy] = true; n = 2;
            var frontier = new List<int>();
            while (n < count)
            {
                frontier.Clear();
                for (int y = 0; y < GH; y++)
                    for (int x = 0; x < GW / 2; x++)   // left half only; mirror keeps the right half in step
                    {
                        if (occ[x, y]) continue;
                        bool adj = (x > 0 && occ[x - 1, y]) || (x < GW - 1 && occ[x + 1, y]) ||
                                   (y > 0 && occ[x, y - 1]) || (y < GH - 1 && occ[x, y + 1]);
                        if (adj) frontier.Add(y * GW + x);
                    }
                if (frontier.Count == 0) break;
                int f = frontier[rng.NextInt(frontier.Count)];
                int fx = f % GW, fy = f / GW;
                occ[fx, fy] = true; occ[GW - 1 - fx, fy] = true;
                n += 2;
            }
            return CellsToPos(occ);
        }

        static List<TilePos> TerraceBase(int count, Xoshiro256 rng)
        {
            int rows = rng.Range(4, 8);
            var len = new int[rows];
            int total = 0;
            for (int i = 0; i < rows; i++) { len[i] = rng.Range(2, GW / 2 + 1) * 2; total += len[i]; }
            int guard = 0;
            while (Math.Abs(total - count) > 1 && guard++ < 400)
            {
                int i = rng.NextInt(rows);
                if (total < count && len[i] + 2 <= GW) { len[i] += 2; total += 2; }
                else if (total > count && len[i] - 2 >= 2) { len[i] -= 2; total -= 2; }
            }
            var pos = new List<TilePos>();
            for (int r = 0; r < rows; r++)
            {
                int x0 = ((GW - len[r]) / 2) * 2 + ((r & 1) == 1 ? 1 : 0);
                for (int c = 0; c < len[r]; c++) pos.Add(new TilePos(x0 + 2 * c, r * 2, 0));
            }
            return pos;
        }

        static List<TilePos> TowerBase(int count, Xoshiro256 rng)
        {
            int cs = rng.Range(2, 4);                       // cluster edge 2 or 3
            int clusters = Math.Max(2, (count / (cs * cs)) & ~1);
            int perRow = clusters <= 4 ? clusters : (clusters + 1) / 2;
            int rowsN = clusters <= 4 ? 1 : 2;
            while (perRow * cs + (perRow - 1) > GW) perRow--;
            var occ = new bool[GW, GH];
            int width = perRow * cs + (perRow - 1);
            int x0 = (GW - width) / 2;
            int height = rowsN * cs + (rowsN - 1);
            int y0 = Math.Max(0, (GH - height) / 2);
            int placed = 0;
            for (int ry = 0; ry < rowsN; ry++)
                for (int i = 0; i < perRow && placed < clusters; i++, placed++)
                    for (int dy = 0; dy < cs; dy++)
                        for (int dx = 0; dx < cs; dx++)
                        {
                            int x = x0 + i * (cs + 1) + dx, y = y0 + ry * (cs + 1) + dy;
                            if (x < GW && y < GH) occ[x, y] = true;
                        }
            return CellsToPos(occ);
        }

        static List<TilePos> CellsToPos(bool[,] occ)
        {
            var pos = new List<TilePos>();
            for (int y = 0; y < GH; y++)
                for (int x = 0; x < GW; x++)
                    if (occ[x, y]) pos.Add(new TilePos(x * 2, y * 2, 0));
            return pos;
        }

        /// <summary>Stacks fully supported tiles on top of the base, mirrored left-right where possible.</summary>
        static List<TilePos> AddUpper(List<TilePos> bas, int target, int maxLayers, Xoshiro256 rng)
        {
            var result = new List<TilePos>(bas);
            var below = new bool[W2 + 2, H2 + 2];
            foreach (var p in bas) MarkCells(below, p.X, p.Y);
            int px = 0, py = 0;
            var cand = new List<int>();
            for (int z = 1; z < maxLayers && result.Count < target; z++)
            {
                px = (px + rng.NextInt(2)) & 1;
                py = (py + rng.NextInt(2)) & 1;
                if (px == 0 && py == 0 && z == 1) { px = 1; }   // avoid a plain duplicate of the base on layer 1
                var here = new bool[W2 + 2, H2 + 2];
                cand.Clear();
                for (int y = 0; y + 2 <= H2; y++)
                    for (int x = 0; x + 2 <= W2; x++)
                    {
                        if ((x & 1) != px || (y & 1) != py) continue;
                        if (FullySupported(below, x, y)) cand.Add(y * 64 + x);
                    }
                rng.Shuffle(cand);
                var layerTiles = new List<TilePos>();
                for (int i = 0; i < cand.Count && result.Count + layerTiles.Count < target; i++)
                {
                    int x = cand[i] % 64, y = cand[i] / 64;
                    if (Overlaps(here, x, y)) continue;
                    MarkCells(here, x, y);
                    layerTiles.Add(new TilePos(x, y, z));
                    int mx = W2 - x - 2;
                    if (mx != x && !Overlaps(here, mx, y) && FullySupported(below, mx, y))
                    {
                        MarkCells(here, mx, y);
                        layerTiles.Add(new TilePos(mx, y, z));
                    }
                }
                if (layerTiles.Count == 0) break;
                result.AddRange(layerTiles);
                below = here;
            }
            return result;
        }

        static void MarkCells(bool[,] g, int x, int y)
        {
            g[x, y] = true; g[x + 1, y] = true; g[x, y + 1] = true; g[x + 1, y + 1] = true;
        }

        static bool FullySupported(bool[,] g, int x, int y)
        {
            return g[x, y] && g[x + 1, y] && g[x, y + 1] && g[x + 1, y + 1];
        }

        static bool Overlaps(bool[,] g, int x, int y)
        {
            return g[x, y] || g[x + 1, y] || g[x, y + 1] || g[x + 1, y + 1];
        }

        // ---------------------------------------------------------------- finishing

        /// <summary>Translate to the origin, then drop top-most tiles until the count is even and not above target.</summary>
        public static List<TilePos> Finalize(List<TilePos> pos, int target)
        {
            var list = new List<TilePos>(pos);
            while (list.Count > target || (list.Count & 1) == 1)
            {
                int bi = 0;
                for (int i = 1; i < list.Count; i++)
                {
                    var a = list[i]; var b = list[bi];
                    if (a.Z > b.Z || (a.Z == b.Z && (a.Y > b.Y || (a.Y == b.Y && a.X > b.X)))) bi = i;
                }
                list.RemoveAt(bi);
            }
            int minX = int.MaxValue, minY = int.MaxValue;
            foreach (var p in list) { if (p.X < minX) minX = p.X; if (p.Y < minY) minY = p.Y; }
            for (int i = 0; i < list.Count; i++)
                list[i] = new TilePos(list[i].X - minX, list[i].Y - minY, list[i].Z);
            return list;
        }
    }
}
