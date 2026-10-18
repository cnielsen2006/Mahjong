using System.Collections.Generic;
using UnityEngine;

namespace Mahjong.Game
{
    /// <summary>
    /// Procedural 3D geometry: one shared beveled tile and the slab the board sits on.
    /// Tile space: x right, y up, the tile's back on z = 0 and its face at z = -Thick (towards the camera).
    /// Vertex colour alpha says how much of the face texture shows (see MahjongTile.shader).
    /// </summary>
    public static class TileMesh
    {
        public const float HalfW = 0.495f, HalfH = 0.645f;   // footprint, a hair under one cell so neighbours never fuse
        public const float Thick = 0.55f;                    // height of one layer
        public const float Bevel = 0.055f;                   // chamfer around the face
        public const float SlabTop = 0.03f;                  // the slab's surface sits just behind the tiles' backs
        public const float SlabDepth = 0.32f;
        const float Radius = 0.195f;                         // plan-view corner radius
        const int ArcSteps = 4;                              // segments per corner

        // Where the face art sits inside one atlas cell: the rim-and-face rectangle drawn by TileArt.DrawTile.
        public const float FaceX0 = 9f, FaceY0 = 11f, FaceX1 = 159f, FaceY1 = 207f;

        // Glow weights: how much of a tile's glow colour shows. The rim is brightest, the middle of the face faintest.
        const float GlowRim = 0.9f, GlowCenter = 0.28f, GlowWall = 0.5f, GlowBack = 0.2f;

        sealed class Builder
        {
            public readonly List<Vector3> P = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Color32> C = new List<Color32>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<Vector2> UV1 = new List<Vector2>();
            public readonly List<int> T = new List<int>();

            public int Vert(Vector3 p, Vector3 n, Color32 c, Vector2 uv, float glow)
            {
                P.Add(p); N.Add(n.normalized); C.Add(c); UV.Add(uv); UV1.Add(new Vector2(glow, 0f));
                return P.Count - 1;
            }

            /// <summary>Adds a triangle facing the way the vertex normals point, whatever order it is given in.</summary>
            public void Tri(int a, int b, int c)
            {
                Vector3 n = Vector3.Cross(P[b] - P[a], P[c] - P[a]);
                if (n.sqrMagnitude < 1e-12f) return;
                if (Vector3.Dot(n, N[a] + N[b] + N[c]) < 0f) { int t = b; b = c; c = t; }
                T.Add(a); T.Add(b); T.Add(c);
            }

            /// <summary>A ring of vertices along an outline. uvPts, when given, are face-space points that map to UV 0..1.</summary>
            public int Ring(Vector2[] pts, Vector2[] dirs, float z, float nxy, float nz, Color32 col, Vector2[] uvPts, Vector2 uvHalf, float glow)
            {
                int first = P.Count;
                for (int i = 0; i < pts.Length; i++)
                {
                    var uv = uvPts == null ? new Vector2(0.5f, 0.5f)
                                           : new Vector2(uvPts[i].x / (2f * uvHalf.x) + 0.5f, uvPts[i].y / (2f * uvHalf.y) + 0.5f);
                    Vert(new Vector3(pts[i].x, pts[i].y, z), new Vector3(dirs[i].x * nxy, dirs[i].y * nxy, nz), col, uv, glow);
                }
                return first;
            }

            public void Band(int ringA, int ringB, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;
                    Tri(ringA + i, ringA + j, ringB + j);
                    Tri(ringA + i, ringB + j, ringB + i);
                }
            }

            public void Fan(int center, int ring, int count)
            {
                for (int i = 0; i < count; i++) Tri(center, ring + i, ring + (i + 1) % count);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(P);
                m.SetNormals(N);
                m.SetColors(C);
                m.SetUVs(0, UV);
                m.SetUVs(1, UV1);
                m.SetTriangles(T, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>Rounded-rectangle outline, counter-clockwise from the right-hand end, plus the outward direction at each point.</summary>
        static void Outline(float hw, float hh, float r, out Vector2[] pts, out Vector2[] dirs)
        {
            int per = ArcSteps + 1;
            pts = new Vector2[4 * per];
            dirs = new Vector2[4 * per];
            var centers = new[] { new Vector2(hw - r, hh - r), new Vector2(-(hw - r), hh - r), new Vector2(-(hw - r), -(hh - r)), new Vector2(hw - r, -(hh - r)) };
            for (int c = 0; c < 4; c++)
                for (int k = 0; k < per; k++)
                {
                    float a = (c * 90f + 90f * k / ArcSteps) * Mathf.Deg2Rad;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    pts[c * per + k] = centers[c] + d * r;
                    dirs[c * per + k] = d;
                }
        }

        /// <summary>The tile: coloured back and lower wall, ivory upper wall, a chamfer, and the textured face.</summary>
        public static Mesh BuildTile(TileStyle s)
        {
            var b = new Builder();
            Vector2[] pF, dF, pT, dT;
            Outline(HalfW, HalfH, Radius, out pF, out dF);
            Outline(HalfW - Bevel, HalfH - Bevel, Radius - Bevel, out pT, out dT);
            int n = pF.Length;
            Color32 side = s.Side, face = s.Face;
            side.a = 0; face.a = 0;                 // alpha 0 = plain vertex colour, never the face texture
            float split = Thick * 0.36f;          // where the coloured back meets the ivory slab
            float wallTop = Thick - Bevel;
            var none = Vector2.zero;
            const float R2 = 0.70710678f;

            // back
            int cb = b.Vert(new Vector3(0, 0, 0), Vector3.forward, side, new Vector2(0.5f, 0.5f), GlowBack);
            int rb = b.Ring(pF, dF, 0f, 0f, 1f, side, null, none, GlowBack);
            b.Fan(cb, rb, n);
            // coloured lower wall
            int w0 = b.Ring(pF, dF, 0f, 1f, 0f, side, null, none, GlowWall * 0.6f);
            int w1 = b.Ring(pF, dF, -split, 1f, 0f, side, null, none, GlowWall * 0.6f);
            b.Band(w0, w1, n);
            // ivory upper wall
            int u0 = b.Ring(pF, dF, -split, 1f, 0f, face, null, none, GlowWall);
            int u1 = b.Ring(pF, dF, -wallTop, 1f, 0f, face, null, none, GlowWall);
            b.Band(u0, u1, n);
            // chamfer: flat colour at the wall, fading into the face art at the top edge
            var uvHalf = new Vector2(HalfW - Bevel, HalfH - Bevel);
            var faceCol = new Color32(face.r, face.g, face.b, 0);
            var white = new Color32(255, 255, 255, 255);
            int c0 = b.Ring(pF, dF, -wallTop, R2, -R2, faceCol, pT, uvHalf, GlowRim);
            int c1 = b.Ring(pT, dT, -Thick, R2, -R2, white, pT, uvHalf, GlowRim);
            b.Band(c0, c1, n);
            // face
            int ct = b.Vert(new Vector3(0, 0, -Thick), -Vector3.forward, white, new Vector2(0.5f, 0.5f), GlowCenter);
            int f0 = b.Ring(pT, dT, -Thick, 0f, -1f, white, pT, uvHalf, GlowRim);
            b.Fan(ct, f0, n);
            return b.ToMesh("Tile3D");
        }

        /// <summary>The raised surface the tiles rest on. Plain vertex colours; the material supplies the see-through look.</summary>
        public static Mesh BuildSlab(float hw, float hh, Color32 top, Color32 wall)
        {
            var b = new Builder();
            top.a = 0; wall.a = 0;                  // alpha 0 = plain vertex colour, never the face texture
            float radius = Mathf.Min(0.7f, Mathf.Min(hw, hh) * 0.5f);
            const float bevel = 0.09f;
            Vector2[] pF, dF, pT, dT;
            Outline(hw, hh, radius, out pF, out dF);
            Outline(hw - bevel, hh - bevel, radius - bevel, out pT, out dT);
            int n = pF.Length;
            var none = Vector2.zero;
            const float R2 = 0.70710678f;
            float z0 = SlabTop, z1 = SlabTop + bevel, z2 = SlabTop + SlabDepth;

            int ct = b.Vert(new Vector3(0, 0, z0), -Vector3.forward, top, new Vector2(0.5f, 0.5f), 0f);
            int f0 = b.Ring(pT, dT, z0, 0f, -1f, top, null, none, 0f);
            b.Fan(ct, f0, n);
            int c0 = b.Ring(pT, dT, z0, R2, -R2, top, null, none, 0f);
            int c1 = b.Ring(pF, dF, z1, R2, -R2, wall, null, none, 0f);
            b.Band(c0, c1, n);
            int w0 = b.Ring(pF, dF, z1, 1f, 0f, wall, null, none, 0f);
            int w1 = b.Ring(pF, dF, z2, 1f, 0f, wall, null, none, 0f);
            b.Band(w0, w1, n);
            return b.ToMesh("Slab3D");
        }
    }
}
