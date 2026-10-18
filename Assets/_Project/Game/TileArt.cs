using System;
using System.Collections.Generic;
using UnityEngine;
using Mahjong.Core;

namespace Mahjong.Game
{
    public struct Unlock
    {
        public int Type;   // 0 always, 1 total stars, 2 highest level reached
        public int Value;
        public Unlock(int t, int v) { Type = t; Value = v; }
        public static readonly Unlock Always = new Unlock(0, 0);
        public bool IsMet(int totalStars, int highestLevel)
        {
            if (Type == 0) return true;
            if (Type == 1) return totalStars >= Value;
            return highestLevel >= Value;
        }
        public string Describe()
        {
            if (Type == 0) return "";
            if (Type == 1) return "Unlocks at " + Value + " stars";
            return "Unlocks at level " + Value;
        }
    }

    public sealed class TileStyle
    {
        public string Id, Name;
        public Color32 Face, Side, Edge, Ink, Red, Green, Blue, Muted;
        public float Stroke = 1f;
        public Unlock Unlock;
    }

    public sealed class BackgroundDef
    {
        public string Id, Name;
        public Color32 Top, Bottom;
        public Unlock Unlock;
    }

    public static class Content
    {
        public static readonly TileStyle[] TileSets =
        {
            new TileStyle { Id = "classic", Name = "Classic Ivory",
                Face = new Color32(247, 241, 224, 255), Side = new Color32(46, 125, 94, 255), Edge = new Color32(205, 196, 170, 255),
                Ink = new Color32(38, 40, 48, 255), Red = new Color32(196, 52, 44, 255), Green = new Color32(36, 140, 84, 255),
                Blue = new Color32(40, 98, 176, 255), Muted = new Color32(120, 116, 104, 255), Unlock = Unlock.Always },
            new TileStyle { Id = "contrast", Name = "High Contrast",
                Face = new Color32(255, 255, 255, 255), Side = new Color32(20, 20, 20, 255), Edge = new Color32(0, 0, 0, 255),
                Ink = new Color32(0, 0, 0, 255), Red = new Color32(204, 0, 0, 255), Green = new Color32(0, 104, 48, 255),
                Blue = new Color32(0, 56, 176, 255), Muted = new Color32(60, 60, 60, 255), Stroke = 1.35f, Unlock = Unlock.Always },
            new TileStyle { Id = "modern", Name = "Modern Flat",
                Face = new Color32(236, 240, 244, 255), Side = new Color32(96, 108, 128, 255), Edge = new Color32(200, 208, 218, 255),
                Ink = new Color32(44, 52, 68, 255), Red = new Color32(226, 84, 96, 255), Green = new Color32(52, 168, 130, 255),
                Blue = new Color32(70, 120, 214, 255), Muted = new Color32(130, 140, 156, 255), Unlock = new Unlock(1, 25) },
            new TileStyle { Id = "midnight", Name = "Midnight",
                Face = new Color32(44, 48, 66, 255), Side = new Color32(16, 18, 28, 255), Edge = new Color32(76, 82, 108, 255),
                Ink = new Color32(236, 238, 248, 255), Red = new Color32(255, 118, 118, 255), Green = new Color32(110, 220, 160, 255),
                Blue = new Color32(120, 170, 255, 255), Muted = new Color32(150, 156, 184, 255), Unlock = new Unlock(1, 75) },
        };

        public static readonly BackgroundDef[] Backgrounds =
        {
            new BackgroundDef { Id = "random", Name = "Random", Top = new Color32(52, 70, 104, 255), Bottom = new Color32(26, 36, 62, 255), Unlock = Unlock.Always },
            new BackgroundDef { Id = "felt", Name = "Green Felt", Top = new Color32(38, 92, 74, 255), Bottom = new Color32(20, 58, 48, 255), Unlock = Unlock.Always },
            new BackgroundDef { Id = "slate", Name = "Slate Blue", Top = new Color32(52, 70, 104, 255), Bottom = new Color32(26, 36, 62, 255), Unlock = Unlock.Always },
            new BackgroundDef { Id = "sand", Name = "Warm Sand", Top = new Color32(176, 150, 112, 255), Bottom = new Color32(122, 98, 72, 255), Unlock = new Unlock(1, 25) },
            new BackgroundDef { Id = "plum", Name = "Plum", Top = new Color32(96, 54, 108, 255), Bottom = new Color32(48, 26, 62, 255), Unlock = new Unlock(1, 75) },
            new BackgroundDef { Id = "ocean", Name = "Deep Ocean", Top = new Color32(24, 112, 134, 255), Bottom = new Color32(10, 52, 74, 255), Unlock = new Unlock(2, 50) },
            new BackgroundDef { Id = "charcoal", Name = "Charcoal", Top = new Color32(58, 60, 66, 255), Bottom = new Color32(22, 22, 26, 255), Unlock = new Unlock(1, 150) },
        };

        public static TileStyle TileSet(string id)
        {
            for (int i = 0; i < TileSets.Length; i++) if (TileSets[i].Id == id) return TileSets[i];
            return TileSets[0];
        }

        public static BackgroundDef Background(string id)
        {
            for (int i = 0; i < Backgrounds.Length; i++) if (Backgrounds[i].Id == id) return Backgrounds[i];
            return Backgrounds[0];
        }
    }

    /// <summary>All sprites in the game are drawn here at startup. No imported art is required.</summary>
    public sealed class TileAtlas
    {
        public const int TileW = 160, TileH = 208, Gutter = 4;
        public const float Ppu = 160f;
        const int Cols = 6, Rows = 7;   // 42 cells
        public Texture2D Texture;
        public Sprite[] Faces = new Sprite[Kinds.Count];
        public readonly TileStyle Style;

        /// <summary>Draws the whole atlas. Pure pixel work, safe to call from a worker thread.</summary>
        public static Raster Draw(TileStyle style)
        {
            int cw = TileW + Gutter * 2, ch = TileH + Gutter * 2;
            var atlas = new Raster(Cols * cw, Rows * ch);
            for (int k = 0; k < Kinds.Count; k++)
            {
                var r = TileArt.DrawTile(k, style);
                atlas.Blit(r, (k % Cols) * cw + Gutter, (k / Cols) * ch + Gutter);
            }
            return atlas;
        }

        /// <summary>Wraps a drawn atlas into a texture and sprites. Main thread only.</summary>
        public TileAtlas(TileStyle style, Raster drawn)
        {
            Style = style;
            int cw = TileW + Gutter * 2, ch = TileH + Gutter * 2;
            // the 3D board views the faces at an angle, so filter across mip levels and along the slant
            Texture = drawn.ToTexture(true, FilterMode.Trilinear);
            Texture.name = "Atlas_" + style.Id;
            Texture.anisoLevel = 4;
            for (int k = 0; k < Kinds.Count; k++)
            {
                var rect = new Rect((k % Cols) * cw + Gutter, (k / Cols) * ch + Gutter, TileW, TileH);
                Faces[k] = Sprite.Create(Texture, rect, new Vector2(0.5f, 0.5f), Ppu, 0, SpriteMeshType.FullRect);
            }
        }

        /// <summary>
        /// Where a kind's face art sits in the atlas, as (u, v, width, height). This is the rim-and-face rectangle of the
        /// drawn tile, pulled in by a pixel so the anti-aliased outer edge is never sampled.
        /// </summary>
        public Vector4 FaceUv(int kind)
        {
            int cw = TileW + Gutter * 2, ch = TileH + Gutter * 2;
            float ox = (kind % Cols) * cw + Gutter, oy = (kind / Cols) * ch + Gutter;
            float x0 = ox + TileMesh.FaceX0 + 1f, x1 = ox + TileMesh.FaceX1 - 1f;
            float y0 = oy + TileMesh.FaceY0 + 1f, y1 = oy + TileMesh.FaceY1 - 1f;
            float tw = Texture.width, th = Texture.height;
            return new Vector4(x0 / tw, y0 / th, (x1 - x0) / tw, (y1 - y0) / th);
        }

        public void Destroy()
        {
            if (Texture != null) UnityEngine.Object.Destroy(Texture);
            for (int i = 0; i < Faces.Length; i++) if (Faces[i] != null) UnityEngine.Object.Destroy(Faces[i]);
        }
    }

    public static class TileArt
    {
        // ------------------------------------------------------------ stroke glyphs (unit box, y up)
        static readonly float[][] Digits =
        {
            null,
            new[] { 0.1f, 0.78f, 0.3f, 1f, 0.3f, 0f },
            new[] { 0f, 0.8f, 0.15f, 1f, 0.45f, 1f, 0.6f, 0.82f, 0.6f, 0.62f, 0f, 0f, 0.62f, 0f },
            new[] { 0f, 0.9f, 0.15f, 1f, 0.45f, 1f, 0.6f, 0.88f, 0.6f, 0.64f, 0.45f, 0.52f, 0.2f, 0.52f, 0.45f, 0.52f, 0.6f, 0.4f, 0.6f, 0.12f, 0.45f, 0f, 0.15f, 0f, 0f, 0.1f },
            new[] { 0.5f, 0f, 0.5f, 1f, 0f, 0.3f, 0.62f, 0.3f },
            new[] { 0.58f, 1f, 0.06f, 1f, 0f, 0.55f, 0.4f, 0.6f, 0.58f, 0.45f, 0.58f, 0.14f, 0.42f, 0f, 0.12f, 0f, 0f, 0.1f },
            new[] { 0.5f, 1f, 0.2f, 1f, 0f, 0.7f, 0f, 0.12f, 0.15f, 0f, 0.45f, 0f, 0.6f, 0.12f, 0.6f, 0.42f, 0.45f, 0.55f, 0.15f, 0.55f, 0f, 0.42f },
            new[] { 0f, 1f, 0.6f, 1f, 0.2f, 0f },
            new[] { 0.3f, 0.52f, 0.1f, 0.62f, 0.1f, 0.88f, 0.3f, 1f, 0.5f, 0.88f, 0.5f, 0.62f, 0.3f, 0.52f, 0.05f, 0.4f, 0.05f, 0.12f, 0.3f, 0f, 0.55f, 0.12f, 0.55f, 0.4f, 0.3f, 0.52f },
            new[] { 0.1f, 0f, 0.4f, 0f, 0.6f, 0.3f, 0.6f, 0.9f, 0.45f, 1f, 0.15f, 1f, 0f, 0.88f, 0f, 0.58f, 0.15f, 0.45f, 0.45f, 0.45f, 0.6f, 0.58f },
        };

        static readonly float[][] Letters = // E S W N
        {
            new[] { 0.55f, 1f, 0f, 1f, 0f, 0f, 0.55f, 0f, 0f, 0.5f, 0.45f, 0.5f },
            new[] { 0.55f, 0.9f, 0.4f, 1f, 0.15f, 1f, 0f, 0.85f, 0f, 0.65f, 0.15f, 0.52f, 0.4f, 0.48f, 0.55f, 0.35f, 0.55f, 0.15f, 0.4f, 0f, 0.15f, 0f, 0f, 0.1f },
            new[] { 0f, 1f, 0.15f, 0f, 0.3f, 0.6f, 0.45f, 0f, 0.6f, 1f },
            new[] { 0f, 0f, 0f, 1f, 0.55f, 0f, 0.55f, 1f },
        };

        // 3 x 3 grid positions (col,row) with row 0 at the top, for tiles 1..9
        static readonly int[][] Pips =
        {
            null,
            new[] { 1, 1 },
            new[] { 1, 0, 1, 2 },
            new[] { 1, 0, 0, 2, 2, 2 },
            new[] { 0, 0, 2, 0, 0, 2, 2, 2 },
            new[] { 0, 0, 2, 0, 1, 1, 0, 2, 2, 2 },
            new[] { 0, 0, 2, 0, 0, 1, 2, 1, 0, 2, 2, 2 },
            new[] { 0, 0, 1, 0, 2, 0, 1, 1, 0, 2, 1, 2, 2, 2 },
            new[] { 0, 0, 1, 0, 2, 0, 0, 1, 2, 1, 0, 2, 1, 2, 2, 2 },
            new[] { 0, 0, 1, 0, 2, 0, 0, 1, 1, 1, 2, 1, 0, 2, 1, 2, 2, 2 },
        };

        static void Strokes(Raster r, float[] pts, float x, float y, float w, float h, float width, Color32 c)
        {
            var list = new List<Vector2>();
            for (int i = 0; i + 1 < pts.Length; i += 2) list.Add(new Vector2(x + pts[i] * w, y + pts[i + 1] * h));
            r.Polyline(list, width, c);
        }

        static void DrawDigit(Raster r, int d, float x, float y, float h, float width, Color32 c)
        {
            Strokes(r, Digits[d], x, y, h * 0.6f, h, width, c);
        }

        static Color32 PipColor(TileStyle s, int row) { return row == 0 ? s.Green : (row == 1 ? s.Red : s.Blue); }

        // ------------------------------------------------------------ one tile

        public static Raster DrawTile(int kind, TileStyle s)
        {
            var r = new Raster(TileAtlas.TileW, TileAtlas.TileH);
            int W = TileAtlas.TileW, H = TileAtlas.TileH;
            // side (the tile thickness at bottom-left), then the raised face
            r.RoundRect(0, 0, W - 5, H - 5, 22, s.Side);
            r.RoundRect(9, 11, W - 1, H - 1, 21, s.Edge);
            r.RoundRect(11, 13, W - 3, H - 3, 19, s.Face);

            float fx0 = 11, fy0 = 13, fx1 = W - 3, fy1 = H - 3;
            Suit suit = Kinds.SuitOf(kind);
            int rank = Kinds.RankOf(kind);
            float st = s.Stroke;

            switch (suit)
            {
                case Suit.Dots:
                case Suit.Bamboo:
                    DrawPipTile(r, suit, rank, s, fx0, fy0, fx1, fy1);
                    break;
                case Suit.Characters:
                    {
                        float cx = (fx0 + fx1) * 0.5f;
                        float dh = 74, dw = 11f * st;
                        DrawDigit(r, rank, cx - dh * 0.3f, fy1 - 22 - dh, dh, dw, s.Ink);
                        // red "wan" style mark under the numeral
                        float my = fy0 + 50;
                        r.Capsule(cx - 30, my + 24, cx + 30, my + 24, 9 * st, s.Red);
                        r.Capsule(cx, my + 24, cx, my - 22, 9 * st, s.Red);
                        r.Capsule(cx - 26, my - 4, cx + 26, my - 4, 8 * st, s.Red);
                        r.Capsule(cx - 14, my - 22, cx - 30, my - 34, 8 * st, s.Red);
                        r.Capsule(cx + 14, my - 22, cx + 30, my - 34, 8 * st, s.Red);
                        break;
                    }
                case Suit.Wind:
                    {
                        float cx = (fx0 + fx1) * 0.5f, cy = (fy0 + fy1) * 0.5f;
                        r.Ring(cx, cy, 58, 5 * st, s.Muted);
                        float lh = 62;
                        Strokes(r, Letters[rank - 1], cx - lh * 0.3f, cy - lh * 0.5f, lh * 0.6f, lh, 11f * st, s.Ink);
                        break;
                    }
                case Suit.Dragon:
                    DrawDragon(r, rank, s, fx0, fy0, fx1, fy1);
                    break;
                case Suit.Flower:
                    {
                        float cx = (fx0 + fx1) * 0.5f, cy = (fy0 + fy1) * 0.5f + 8;
                        Color32 pc = rank == 1 ? s.Red : (rank == 2 ? s.Blue : (rank == 3 ? s.Green : s.Muted));
                        for (int i = 0; i < 5; i++)
                        {
                            float a = i * Mathf.PI * 2 / 5 + 0.3f;
                            r.Circle(cx + Mathf.Cos(a) * 28, cy + Mathf.Sin(a) * 28, 20, pc);
                        }
                        r.Circle(cx, cy, 14, s.Face);
                        DrawDigit(r, rank, fx0 + 14, fy1 - 44, 30, 5f * st, s.Ink);
                        break;
                    }
                default:
                    {
                        float cx = (fx0 + fx1) * 0.5f, cy = (fy0 + fy1) * 0.5f + 8;
                        Color32 pc = rank == 1 ? s.Green : (rank == 2 ? s.Red : (rank == 3 ? s.Blue : s.Muted));
                        r.Circle(cx, cy, 30, pc);
                        for (int i = 0; i < 8; i++)
                        {
                            float a = i * Mathf.PI / 4;
                            r.Capsule(cx + Mathf.Cos(a) * 38, cy + Mathf.Sin(a) * 38, cx + Mathf.Cos(a) * 52, cy + Mathf.Sin(a) * 52, 7, pc);
                        }
                        DrawDigit(r, rank, fx0 + 14, fy1 - 44, 30, 5f * st, s.Ink);
                        break;
                    }
            }
            return r;
        }

        static void DrawPipTile(Raster r, Suit suit, int rank, TileStyle s, float fx0, float fy0, float fx1, float fy1)
        {
            // index numeral in the top-left corner so every number tile is readable at a glance
            DrawDigit(r, rank, fx0 + 11, fy1 - 36, 25, 5.2f * s.Stroke, s.Ink);

            float ax0 = fx0 + 12, ax1 = fx1 - 12, ay0 = fy0 + 12, ay1 = fy1 - 44;
            float cw = (ax1 - ax0) / 3f, chh = (ay1 - ay0) / 3f;
            var p = Pips[rank];
            for (int i = 0; i + 1 < p.Length; i += 2)
            {
                int col = p[i], row = p[i + 1];
                float cx = ax0 + cw * (col + 0.5f);
                float cy = ay1 - chh * (row + 0.5f);
                Color32 c = PipColor(s, rank == 1 ? 1 : row);
                if (suit == Suit.Dots)
                {
                    float rad = rank == 1 ? 40 : Mathf.Min(cw, chh) * 0.44f;
                    r.Circle(cx, cy, rad, c);
                    r.Circle(cx, cy, rad * 0.62f, s.Face);
                    r.Circle(cx, cy, rad * 0.34f, c);
                }
                else
                {
                    if (rank == 1)
                    {
                        // a single bold stalk with a leaf
                        c = s.Green;
                        r.RoundRect(cx - 14, cy - 44, cx + 14, cy + 44, 12, c);
                        r.Capsule(cx - 14, cy + 8, cx - 36, cy + 30, 10, s.Red);
                        r.Capsule(cx - 14, cy - 18, cx + 8, cy - 18, 5, s.Face);
                        r.Capsule(cx - 14, cy + 10, cx + 8, cy + 10, 5, s.Face);
                    }
                    else
                    {
                        float hw = cw * 0.2f, hh = chh * 0.46f;
                        r.RoundRect(cx - hw, cy - hh, cx + hw, cy + hh, hw * 0.8f, c);
                        r.Capsule(cx - hw, cy, cx + hw, cy, 3.5f, s.Face);
                        r.Capsule(cx - hw * 0.4f, cy + hh * 0.55f, cx - hw * 0.4f, cy + hh * 0.85f, 2.5f, s.Face);
                    }
                }
            }
        }

        static void DrawDragon(Raster r, int rank, TileStyle s, float fx0, float fy0, float fx1, float fy1)
        {
            float cx = (fx0 + fx1) * 0.5f, cy = (fy0 + fy1) * 0.5f;
            float st = s.Stroke;
            if (rank == 1)
            {
                r.RoundRectOutline(cx - 44, cy - 52, cx + 44, cy + 28, 8, 11 * st, s.Red);
                r.Capsule(cx, cy + 62, cx, cy - 66, 12 * st, s.Red);
            }
            else if (rank == 2)
            {
                r.Capsule(cx - 40, cy + 54, cx + 40, cy + 54, 11 * st, s.Green);
                r.Capsule(cx - 30, cy + 54, cx - 30, cy - 50, 11 * st, s.Green);
                r.Capsule(cx, cy + 54, cx, cy - 56, 11 * st, s.Green);
                r.Capsule(cx + 30, cy + 54, cx + 30, cy - 50, 11 * st, s.Green);
                r.Capsule(cx - 40, cy - 56, cx + 40, cy - 56, 11 * st, s.Green);
            }
            else
            {
                r.RoundRectOutline(cx - 46, cy - 62, cx + 46, cy + 62, 10, 9 * st, s.Blue);
                r.RoundRectOutline(cx - 32, cy - 48, cx + 32, cy + 48, 6, 5 * st, s.Blue);
            }
        }

        // ------------------------------------------------------------ UI sprites

        static Sprite uiRound, uiCircle, uiStar, uiSoftShadow, uiRing;

        public static Sprite RoundedSprite()
        {
            if (uiRound != null) return uiRound;
            var r = new Raster(64, 64);
            r.RoundRect(0, 0, 64, 64, 22, new Color32(255, 255, 255, 255));
            var t = r.ToTexture(false, FilterMode.Bilinear);
            uiRound = Sprite.Create(t, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(24, 24, 24, 24));
            return uiRound;
        }

        /// <summary>Insets (left, bottom, right, top) from a button's rect to its visible face in <see cref="ButtonSprite"/>.</summary>
        public static readonly Vector4 ButtonFace = new Vector4(5, 12, 5, 3);
        /// <summary>Height of the darker lip under the face; the face sinks by this much when pressed.</summary>
        public const float ButtonLip = 4f;
        static Sprite uiButton;

        /// <summary>
        /// White, 9-sliced button: soft drop shadow, darker bottom lip, a top-lit gradient face and a thin highlight rim.
        /// Greyscale so the Image colour tints it; the shadow stays black under any tint.
        /// </summary>
        public static Sprite ButtonSprite()
        {
            if (uiButton != null) return uiButton;
            const int W = 64, H = 72;
            const float rad = 17f;
            float x0 = ButtonFace.x, x1 = W - ButtonFace.z, top = H - ButtonFace.w;
            float faceBot = ButtonFace.y, lipBot = faceBot - ButtonLip;
            var r = new Raster(W, H);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    // shadow: offset down, blurred
                    float ds = RectSdf(px, py, x0 + 1, lipBot - 3, x1 - 1, top - 4, rad);
                    float sa = 0.42f * Mathf.Clamp01((4.5f - ds) / 7f);
                    float cr = 0, cg = 0, cb = 0, ca = sa;
                    // lip
                    float dl = RectSdf(px, py, x0, lipBot, x1, top, rad);
                    Over(ref cr, ref cg, ref cb, ref ca, 0.52f, Mathf.Clamp01(0.5f - dl));
                    // face: top-lit gradient plus a highlight rim on the upper half
                    float df = RectSdf(px, py, x0, faceBot, x1, top, rad);
                    float t = Mathf.Clamp01((py - faceBot) / (top - faceBot));
                    float lum = Mathf.Lerp(0.80f, 0.96f, t * t * (3f - 2f * t));
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(df + 1.6f) / 1.1f) * Mathf.Clamp01((t - 0.45f) * 2.5f);
                    lum = Mathf.Lerp(lum, 1f, rim * 0.9f);
                    Over(ref cr, ref cg, ref cb, ref ca, lum, Mathf.Clamp01(0.5f - df));
                    // faint dark edge for definition on busy backgrounds
                    float edge = Mathf.Clamp01(1f - Mathf.Abs(dl + 0.6f) / 0.9f) * 0.30f;
                    Over(ref cr, ref cg, ref cb, ref ca, 0f, edge * Mathf.Clamp01(0.5f - dl + 1f));
                    r.Px[y * W + x] = new Color32((byte)(cr * 255f + 0.5f), (byte)(cg * 255f + 0.5f), (byte)(cb * 255f + 0.5f), (byte)(ca * 255f + 0.5f));
                }
            var tex = r.ToTexture(false, FilterMode.Bilinear);
            var border = new Vector4(x0 + rad + 1, faceBot + rad + 1, W - x1 + rad + 1, H - top + rad + 1);
            uiButton = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            return uiButton;
        }

        static float RectSdf(float px, float py, float x0, float y0, float x1, float y1, float r)
        {
            float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
            float qx = Mathf.Abs(px - cx) - ((x1 - x0) * 0.5f - r);
            float qy = Mathf.Abs(py - cy) - ((y1 - y0) * 0.5f - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>Straight-alpha "over" of a grey value with the given alpha.</summary>
        static void Over(ref float r, ref float g, ref float b, ref float a, float grey, float sa)
        {
            if (sa <= 0f) return;
            float oa = sa + a * (1f - sa);
            r = (grey * sa + r * a * (1f - sa)) / oa;
            g = (grey * sa + g * a * (1f - sa)) / oa;
            b = (grey * sa + b * a * (1f - sa)) / oa;
            a = oa;
        }

        public static Sprite CircleSprite()
        {
            if (uiCircle != null) return uiCircle;
            var r = new Raster(64, 64);
            r.Circle(32, 32, 31, new Color32(255, 255, 255, 255));
            var t = r.ToTexture(false, FilterMode.Bilinear);
            uiCircle = Sprite.Create(t, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f);
            return uiCircle;
        }

        public static Sprite RingSprite()
        {
            if (uiRing != null) return uiRing;
            var r = new Raster(64, 64);
            r.Ring(32, 32, 28, 5, new Color32(255, 255, 255, 255));
            var t = r.ToTexture(false, FilterMode.Bilinear);
            uiRing = Sprite.Create(t, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f);
            return uiRing;
        }

        public static Sprite StarSprite()
        {
            if (uiStar != null) return uiStar;
            var r = new Raster(96, 96);
            r.Star(48, 47, 46, 20, new Color32(255, 255, 255, 255));
            var t = r.ToTexture(false, FilterMode.Bilinear);
            uiStar = Sprite.Create(t, new Rect(0, 0, 96, 96), new Vector2(0.5f, 0.5f), 100f);
            return uiStar;
        }

        /// <summary>Soft dark glow used behind panels.</summary>
        public static Sprite ShadowSprite()
        {
            if (uiSoftShadow != null) return uiSoftShadow;
            var r = new Raster(64, 64);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Abs(x - 31.5f) - 14f), dy = Mathf.Max(0, Mathf.Abs(y - 31.5f) - 14f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d / 18f);
                    r.Px[y * 64 + x] = new Color32(0, 0, 0, (byte)(a * a * 120));
                }
            var t = r.ToTexture(false, FilterMode.Bilinear);
            uiSoftShadow = Sprite.Create(t, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(26, 26, 26, 26));
            return uiSoftShadow;
        }

        // ------------------------------------------------------------ backgrounds

        // Hues chosen to stay away from green, so the ivory tiles with green sides always stand out.
        static readonly float[] RandomHues = { 0.55f, 0.60f, 0.66f, 0.72f, 0.78f, 0.85f, 0.92f, 0.98f, 0.04f };
        public static int RandomHueCount { get { return RandomHues.Length; } }

        /// <summary>A dark, softly lit gradient with a faint pattern. Different every call; never the hue index in lastHue.</summary>
        public static Texture2D RandomBackgroundTexture(System.Random rng, ref int lastHue)
        {
            int hi;
            do { hi = rng.Next(RandomHues.Length); } while (hi == lastHue && RandomHues.Length > 1);
            lastHue = hi;
            return RandomBackgroundTexture(rng, hi);
        }

        static float Rnd(System.Random rng, float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

        static Texture2D RandomBackgroundTexture(System.Random rng, int hueIndex)
        {
            const int N = 256;
            float h = RandomHues[hueIndex];
            Color top = Color.HSVToRGB(Mathf.Repeat(h, 1f), Rnd(rng, 0.45f, 0.6f), Rnd(rng, 0.42f, 0.52f));
            Color bottom = Color.HSVToRGB(Mathf.Repeat(h + Rnd(rng, -0.05f, 0.05f), 1f), Rnd(rng, 0.6f, 0.75f), Rnd(rng, 0.17f, 0.25f));
            int blobs = rng.Next(3, 6);
            var bc = new Vector2[blobs]; var br = new float[blobs]; var bcol = new Color[blobs];
            for (int i = 0; i < blobs; i++)
            {
                bc[i] = new Vector2(Rnd(rng, 0f, 1f), Rnd(rng, 0f, 1f));
                br[i] = Rnd(rng, 0.22f, 0.5f);
                bcol[i] = Color.HSVToRGB(Mathf.Repeat(h + Rnd(rng, -0.08f, 0.08f), 1f), Rnd(rng, 0.45f, 0.7f), Rnd(rng, 0.2f, 0.32f));
            }
            int pattern = rng.Next(3);          // 0 none, 1 diamonds, 2 dots
            float freq = Rnd(rng, 7f, 11f);
            float patAmp = pattern == 0 ? 0f : 0.05f;

            var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                float fy = y / (float)(N - 1);
                for (int x = 0; x < N; x++)
                {
                    float fx = x / (float)(N - 1);
                    Color c = Color.Lerp(bottom, top, fy);
                    for (int i = 0; i < blobs; i++)
                    {
                        float d = Vector2.Distance(new Vector2(fx, fy), bc[i]) / br[i];
                        c += bcol[i] * Mathf.Exp(-d * d * 2.5f);
                    }
                    float p = 0f;
                    if (pattern == 1)
                    {
                        float u = Mathf.Abs(Mathf.Repeat((fx + fy) * freq, 1f) - 0.5f), v = Mathf.Abs(Mathf.Repeat((fx - fy) * freq, 1f) - 0.5f);
                        p = Mathf.Clamp01(1f - Mathf.Min(u, v) / 0.04f);
                    }
                    else if (pattern == 2)
                    {
                        float u = Mathf.Repeat(fx * freq * 1.5f, 1f) - 0.5f, v = Mathf.Repeat(fy * freq * 1.5f, 1f) - 0.5f;
                        p = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v) / 0.14f);
                    }
                    c += new Color(1, 1, 1, 0) * (p * patAmp);
                    float dc = Mathf.Sqrt((fx - 0.5f) * (fx - 0.5f) + (fy - 0.5f) * (fy - 0.5f)) * 1.4f;
                    float vig = 1f - 0.4f * Mathf.SmoothStep(0.45f, 1f, dc);
                    float n = ((float)rng.NextDouble() - 0.5f) * (1.5f / 255f);
                    px[y * N + x] = new Color32(
                        (byte)(Mathf.Clamp01(c.r * vig + n) * 255f),
                        (byte)(Mathf.Clamp01(c.g * vig + n) * 255f),
                        (byte)(Mathf.Clamp01(c.b * vig + n) * 255f), 255);
                }
            }
            t.SetPixels32(px);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.Apply(false, false);
            return t;
        }

        public static Texture2D BackgroundTexture(BackgroundDef def)
        {
            if (def.Id == "random") return RandomBackgroundTexture(new System.Random(7), 3);
            int w = 8, h = 256;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            uint seed = 12345;
            for (int y = 0; y < h; y++)
            {
                float f = y / (float)(h - 1);   // 0 bottom, 1 top
                for (int x = 0; x < w; x++)
                {
                    seed = seed * 1664525u + 1013904223u;
                    float n = ((seed >> 24) / 255f - 0.5f) * 1.6f;
                    float r = Mathf.Lerp(def.Bottom.r, def.Top.r, f) + n;
                    float g = Mathf.Lerp(def.Bottom.g, def.Top.g, f) + n;
                    float b = Mathf.Lerp(def.Bottom.b, def.Top.b, f) + n;
                    px[y * w + x] = new Color32((byte)Mathf.Clamp(r, 0, 255), (byte)Mathf.Clamp(g, 0, 255), (byte)Mathf.Clamp(b, 0, 255), 255);
                }
            }
            t.SetPixels32(px);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.Apply(false, false);
            return t;
        }
    }
}
