using System;
using System.Collections.Generic;

namespace Mahjong.Core
{
    /// <summary>Position on the half-tile grid. A tile covers cells [X,X+2) x [Y,Y+2) on layer Z.</summary>
    public struct TilePos
    {
        public short X, Y, Z;
        public TilePos(int x, int y, int z) { X = (short)x; Y = (short)y; Z = (short)z; }
        public override string ToString() { return "(" + X + "," + Y + "," + Z + ")"; }
    }

    /// <summary>Immutable set of tile positions plus precomputed adjacency. Position id = index in Pos.</summary>
    public sealed class Layout
    {
        public readonly string Id;
        public readonly string Family;
        public readonly TilePos[] Pos;
        public readonly int[][] Below, Above, Left, Right;
        public readonly int Width, Height, Layers;

        public int Count { get { return Pos.Length; } }

        public Layout(string id, string family, IList<TilePos> positions)
        {
            Id = id; Family = family;
            var list = new List<TilePos>(positions);
            list.Sort((a, b) =>
            {
                if (a.Z != b.Z) return a.Z.CompareTo(b.Z);
                if (a.Y != b.Y) return a.Y.CompareTo(b.Y);
                return a.X.CompareTo(b.X);
            });
            Pos = list.ToArray();
            int n = Pos.Length;
            var below = new List<int>[n]; var above = new List<int>[n];
            var left = new List<int>[n]; var right = new List<int>[n];
            int w = 0, h = 0, layers = 0;
            for (int i = 0; i < n; i++)
            {
                below[i] = new List<int>(); above[i] = new List<int>();
                left[i] = new List<int>(); right[i] = new List<int>();
                if (Pos[i].X + 2 > w) w = Pos[i].X + 2;
                if (Pos[i].Y + 2 > h) h = Pos[i].Y + 2;
                if (Pos[i].Z + 1 > layers) layers = Pos[i].Z + 1;
            }
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    int dx = Pos[j].X - Pos[i].X, dy = Pos[j].Y - Pos[i].Y, dz = Pos[j].Z - Pos[i].Z;
                    int ady = dy < 0 ? -dy : dy, adx = dx < 0 ? -dx : dx;
                    if (dz == 1 && adx < 2 && ady < 2) above[i].Add(j);
                    else if (dz == -1 && adx < 2 && ady < 2) below[i].Add(j);
                    else if (dz == 0 && ady < 2)
                    {
                        if (dx == 2) right[i].Add(j);
                        else if (dx == -2) left[i].Add(j);
                    }
                }
            }
            Below = new int[n][]; Above = new int[n][]; Left = new int[n][]; Right = new int[n][];
            for (int i = 0; i < n; i++)
            {
                Below[i] = below[i].ToArray(); Above[i] = above[i].ToArray();
                Left[i] = left[i].ToArray(); Right[i] = right[i].ToArray();
            }
            Width = w; Height = h; Layers = layers;
        }

        /// <summary>New layout made of the given position ids (must be ascending). Sub-id i maps to ids[i].</summary>
        public Layout Subset(IList<int> ids)
        {
            var p = new List<TilePos>(ids.Count);
            for (int i = 0; i < ids.Count; i++) p.Add(Pos[ids[i]]);
            return new Layout(Id, Family, p);
        }

        public int CountOnLayer(int z)
        {
            int c = 0;
            for (int i = 0; i < Pos.Length; i++) if (Pos[i].Z == z) c++;
            return c;
        }

        /// <summary>Checks the rules in TRD 4.9 items 1-4. Returns null when valid.</summary>
        public string Validate(int minTiles, int maxTiles)
        {
            int n = Pos.Length;
            if (n % 2 != 0) return "odd tile count " + n;
            if (n < minTiles || n > maxTiles) return "tile count " + n + " outside " + minTiles + ".." + maxTiles;
            if (Width > 40 || Height > 24) return "bounding box " + Width + "x" + Height + " too large";
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (Pos[i].Z != Pos[j].Z) continue;
                    int dx = Math.Abs(Pos[i].X - Pos[j].X), dy = Math.Abs(Pos[i].Y - Pos[j].Y);
                    if (dx < 2 && dy < 2) return "overlap at " + Pos[i] + " and " + Pos[j];
                }
                if (Pos[i].Z > 0)
                {
                    int area = 0;
                    foreach (int q in Below[i])
                    {
                        int dx = Math.Abs(Pos[i].X - Pos[q].X), dy = Math.Abs(Pos[i].Y - Pos[q].Y);
                        area += (2 - dx) * (2 - dy);
                    }
                    if (area < 2) return "unsupported tile at " + Pos[i];
                }
            }
            return null;
        }
    }
}
