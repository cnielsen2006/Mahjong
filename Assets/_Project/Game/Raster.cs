using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mahjong.Game
{
    /// <summary>
    /// Tiny anti-aliased software rasteriser used to draw every sprite in the game (tiles, icons, backgrounds).
    /// Pixel origin is bottom-left, y grows upwards, matching Unity textures.
    /// </summary>
    public sealed class Raster
    {
        public readonly int W, H;
        public readonly Color32[] Px;

        public Raster(int w, int h)
        {
            W = w; H = h;
            Px = new Color32[w * h];
        }

        public void Clear(Color32 c)
        {
            for (int i = 0; i < Px.Length; i++) Px[i] = c;
        }

        static float Clamp01(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        void Blend(int x, int y, Color32 c, float cov)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            float a = cov * (c.a / 255f);
            if (a <= 0.0005f) return;
            int i = y * W + x;
            Color32 d = Px[i];
            float da = d.a / 255f;
            float oa = a + da * (1f - a);
            if (oa <= 0f) return;
            float r = (c.r * a + d.r * da * (1f - a)) / oa;
            float g = (c.g * a + d.g * da * (1f - a)) / oa;
            float b = (c.b * a + d.b * da * (1f - a)) / oa;
            Px[i] = new Color32((byte)(r + 0.5f), (byte)(g + 0.5f), (byte)(b + 0.5f), (byte)(oa * 255f + 0.5f));
        }

        public void RoundRect(float x0, float y0, float x1, float y1, float r, Color32 c)
        {
            float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
            float hx = (x1 - x0) * 0.5f, hy = (y1 - y0) * 0.5f;
            r = Mathf.Min(r, Mathf.Min(hx, hy));
            int ix0 = Mathf.Max(0, (int)Mathf.Floor(x0) - 1), ix1 = Mathf.Min(W - 1, (int)Mathf.Ceil(x1) + 1);
            int iy0 = Mathf.Max(0, (int)Mathf.Floor(y0) - 1), iy1 = Mathf.Min(H - 1, (int)Mathf.Ceil(y1) + 1);
            for (int y = iy0; y <= iy1; y++)
                for (int x = ix0; x <= ix1; x++)
                {
                    float px = Mathf.Abs(x + 0.5f - cx) - (hx - r);
                    float py = Mathf.Abs(y + 0.5f - cy) - (hy - r);
                    float ox = Mathf.Max(px, 0f), oy = Mathf.Max(py, 0f);
                    float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(px, py), 0f) - r;
                    float cov = Clamp01(0.5f - d);
                    if (cov > 0f) Blend(x, y, c, cov);
                }
        }

        /// <summary>Outline of a rounded rectangle with the stroke centred on the edge.</summary>
        public void RoundRectOutline(float x0, float y0, float x1, float y1, float r, float stroke, Color32 c)
        {
            float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
            float hx = (x1 - x0) * 0.5f, hy = (y1 - y0) * 0.5f;
            r = Mathf.Min(r, Mathf.Min(hx, hy));
            float half = stroke * 0.5f;
            int ix0 = Mathf.Max(0, (int)Mathf.Floor(x0 - half) - 1), ix1 = Mathf.Min(W - 1, (int)Mathf.Ceil(x1 + half) + 1);
            int iy0 = Mathf.Max(0, (int)Mathf.Floor(y0 - half) - 1), iy1 = Mathf.Min(H - 1, (int)Mathf.Ceil(y1 + half) + 1);
            for (int y = iy0; y <= iy1; y++)
                for (int x = ix0; x <= ix1; x++)
                {
                    float px = Mathf.Abs(x + 0.5f - cx) - (hx - r);
                    float py = Mathf.Abs(y + 0.5f - cy) - (hy - r);
                    float ox = Mathf.Max(px, 0f), oy = Mathf.Max(py, 0f);
                    float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(px, py), 0f) - r;
                    float cov = Clamp01(0.5f - (Mathf.Abs(d) - half));
                    if (cov > 0f) Blend(x, y, c, cov);
                }
        }

        public void Circle(float cx, float cy, float r, Color32 c)
        {
            int ix0 = Mathf.Max(0, (int)(cx - r) - 1), ix1 = Mathf.Min(W - 1, (int)(cx + r) + 1);
            int iy0 = Mathf.Max(0, (int)(cy - r) - 1), iy1 = Mathf.Min(H - 1, (int)(cy + r) + 1);
            for (int y = iy0; y <= iy1; y++)
                for (int x = ix0; x <= ix1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - r;
                    float cov = Clamp01(0.5f - d);
                    if (cov > 0f) Blend(x, y, c, cov);
                }
        }

        public void Ring(float cx, float cy, float r, float stroke, Color32 c)
        {
            float half = stroke * 0.5f;
            int ix0 = Mathf.Max(0, (int)(cx - r - half) - 1), ix1 = Mathf.Min(W - 1, (int)(cx + r + half) + 1);
            int iy0 = Mathf.Max(0, (int)(cy - r - half) - 1), iy1 = Mathf.Min(H - 1, (int)(cy + r + half) + 1);
            for (int y = iy0; y <= iy1; y++)
                for (int x = ix0; x <= ix1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float d = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r) - half;
                    float cov = Clamp01(0.5f - d);
                    if (cov > 0f) Blend(x, y, c, cov);
                }
        }

        public void Capsule(float x0, float y0, float x1, float y1, float width, Color32 c)
        {
            float rad = width * 0.5f;
            int ix0 = Mathf.Max(0, (int)(Mathf.Min(x0, x1) - rad) - 1), ix1 = Mathf.Min(W - 1, (int)(Mathf.Max(x0, x1) + rad) + 1);
            int iy0 = Mathf.Max(0, (int)(Mathf.Min(y0, y1) - rad) - 1), iy1 = Mathf.Min(H - 1, (int)(Mathf.Max(y0, y1) + rad) + 1);
            float vx = x1 - x0, vy = y1 - y0;
            float len2 = vx * vx + vy * vy;
            for (int y = iy0; y <= iy1; y++)
                for (int x = ix0; x <= ix1; x++)
                {
                    float px = x + 0.5f - x0, py = y + 0.5f - y0;
                    float t = len2 > 0f ? Clamp01((px * vx + py * vy) / len2) : 0f;
                    float dx = px - vx * t, dy = py - vy * t;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - rad;
                    float cov = Clamp01(0.5f - d);
                    if (cov > 0f) Blend(x, y, c, cov);
                }
        }

        public void Polyline(IList<Vector2> pts, float width, Color32 c)
        {
            for (int i = 0; i + 1 < pts.Count; i++) Capsule(pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y, width, c);
        }

        /// <summary>Filled polygon using 4x4 supersampled even-odd coverage.</summary>
        public void Polygon(IList<Vector2> pts, Color32 c)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < pts.Count; i++)
            {
                minX = Mathf.Min(minX, pts[i].x); maxX = Mathf.Max(maxX, pts[i].x);
                minY = Mathf.Min(minY, pts[i].y); maxY = Mathf.Max(maxY, pts[i].y);
            }
            int ix0 = Mathf.Max(0, (int)minX - 1), ix1 = Mathf.Min(W - 1, (int)maxX + 1);
            int iy0 = Mathf.Max(0, (int)minY - 1), iy1 = Mathf.Min(H - 1, (int)maxY + 1);
            for (int y = iy0; y <= iy1; y++)
                for (int x = ix0; x <= ix1; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            if (Inside(pts, x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f)) hit++;
                    if (hit > 0) Blend(x, y, c, hit / 16f);
                }
        }

        static bool Inside(IList<Vector2> p, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            {
                if (((p[i].y > y) != (p[j].y > y)) &&
                    (x < (p[j].x - p[i].x) * (y - p[i].y) / (p[j].y - p[i].y) + p[i].x))
                    inside = !inside;
            }
            return inside;
        }

        public void Star(float cx, float cy, float outer, float inner, Color32 c)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i < 10; i++)
            {
                float ang = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float rr = (i & 1) == 0 ? outer : inner;
                pts.Add(new Vector2(cx + Mathf.Cos(ang) * rr, cy + Mathf.Sin(ang) * rr));
            }
            Polygon(pts, c);
        }

        public void Blit(Raster src, int dx, int dy)
        {
            for (int y = 0; y < src.H; y++)
            {
                int ty = y + dy; if (ty < 0 || ty >= H) continue;
                for (int x = 0; x < src.W; x++)
                {
                    int tx = x + dx; if (tx < 0 || tx >= W) continue;
                    Px[ty * W + tx] = src.Px[y * src.W + x];
                }
            }
        }

        /// <summary>Draws src scaled by 'scale' with its bottom-left at (x,y), bilinear filtered, alpha-over.</summary>
        public void BlitScaled(Raster src, float x, float y, float scale)
        {
            int x0 = Mathf.Max(0, (int)x), x1 = Mathf.Min(W - 1, (int)(x + src.W * scale) + 1);
            int y0 = Mathf.Max(0, (int)y), y1 = Mathf.Min(H - 1, (int)(y + src.H * scale) + 1);
            for (int dy = y0; dy <= y1; dy++)
                for (int dx = x0; dx <= x1; dx++)
                {
                    float u = (dx + 0.5f - x) / scale - 0.5f, v = (dy + 0.5f - y) / scale - 0.5f;
                    if (u < -0.5f || v < -0.5f || u > src.W - 0.5f || v > src.H - 0.5f) continue;
                    int ux = Mathf.Clamp((int)Mathf.Floor(u), 0, src.W - 1), vy = Mathf.Clamp((int)Mathf.Floor(v), 0, src.H - 1);
                    int ux2 = Mathf.Min(ux + 1, src.W - 1), vy2 = Mathf.Min(vy + 1, src.H - 1);
                    float fx = Mathf.Clamp01(u - ux), fy = Mathf.Clamp01(v - vy);
                    Color32 c00 = src.Px[vy * src.W + ux], c10 = src.Px[vy * src.W + ux2], c01 = src.Px[vy2 * src.W + ux], c11 = src.Px[vy2 * src.W + ux2];
                    float a = Bi(c00.a, c10.a, c01.a, c11.a, fx, fy);
                    if (a <= 0.5f) continue;
                    float rr = Bi(c00.r * c00.a, c10.r * c10.a, c01.r * c01.a, c11.r * c11.a, fx, fy) / a;
                    float gg = Bi(c00.g * c00.a, c10.g * c10.a, c01.g * c01.a, c11.g * c11.a, fx, fy) / a;
                    float bb = Bi(c00.b * c00.a, c10.b * c10.a, c01.b * c01.a, c11.b * c11.a, fx, fy) / a;
                    Blend(dx, dy, new Color32((byte)Mathf.Clamp(rr, 0, 255), (byte)Mathf.Clamp(gg, 0, 255), (byte)Mathf.Clamp(bb, 0, 255), 255), a / 255f);
                }
        }

        static float Bi(float a, float b, float c, float d, float fx, float fy)
        {
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        public Texture2D ToTexture(bool mipmaps, FilterMode filter)
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, mipmaps);
            t.SetPixels32(Px);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = filter;
            t.Apply(mipmaps, false);
            return t;
        }
    }
}
