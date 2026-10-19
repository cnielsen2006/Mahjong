using UnityEngine;
using UnityEngine.UI;

namespace Mahjong.Game
{
    /// <summary>
    /// The opening splash: four ivory tiles drop onto the table one after another, the title fades in under them and a
    /// gold bar fills while the game loads. It covers the screen until the tiles are ready and the minimum time has passed,
    /// then fades out. A tap skips what is left of it once the game is ready.
    /// </summary>
    public sealed class Splash
    {
        const float MinSeconds = 2.6f, SkipAfter = 1.0f, FadeOut = 0.45f;
        const float TileW = 150f, TileH = 200f, TileGap = 24f, LayoutW = TileW * 4 + TileGap * 3 + 80f;
        const int Sparkles = 16;

        sealed class Drop
        {
            public RectTransform rt;
            public CanvasGroup cg;
            public float delay, targetY, tilt;
            public bool landed;
        }

        readonly System.Action<string, float, float> sfx;
        RectTransform root, content, bar, underline;
        CanvasGroup rootGroup, titleGroup, tagGroup;
        RectTransform title, tag;
        Image glow;
        readonly Drop[] drops = new Drop[4];
        readonly RectTransform[] sparks = new RectTransform[Sparkles];
        readonly Image[] sparkImg = new Image[Sparkles];
        float start = -1f, leaving = -1f;

        public bool Active { get; private set; }

        public Splash(System.Action<string, float, float> playSfx) { sfx = playSfx; }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.6f, c3 = c1 + 1f;
            x = Mathf.Clamp01(x) - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        static float Hash(int i) { return Mathf.Abs(Mathf.Sin(i * 12.9898f) * 43758.5453f) % 1f; }

        // ------------------------------------------------------------------ build

        public void Build(Transform canvas)
        {
            root = UiKit.NewRect("Splash", canvas);
            UiKit.Stretch(root);
            rootGroup = root.gameObject.AddComponent<CanvasGroup>();

            // deep green felt, lighter towards the middle
            var bg = UiKit.NewRect("Bg", root);
            UiKit.Stretch(bg);
            var ri = bg.gameObject.AddComponent<RawImage>();
            ri.texture = GradientTexture();
            ri.raycastTarget = true;                                  // swallows taps meant for the screen underneath

            glow = UiKit.Img(root, "Glow", new Color(1f, 0.82f, 0.35f, 0f), TileArt.CircleSprite());
            UiKit.Fixed(glow.rectTransform, 0.5f, 0.5f, 900, 900, 0, 60);
            glow.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < Sparkles; i++)
            {
                var s = UiKit.Img(root, "spark", new Color(1f, 0.9f, 0.55f, 0f), TileArt.CircleSprite());
                float sz = 6f + Hash(i + 40) * 10f;
                s.rectTransform.sizeDelta = new Vector2(sz, sz);
                sparkImg[i] = s; sparks[i] = s.rectTransform;
            }

            content = UiKit.NewRect("Content", root);
            UiKit.Fixed(content, 0.5f, 0.5f, LayoutW, 640, 0, 0);
            content.pivot = new Vector2(0.5f, 0.5f);

            float x0 = -(TileW * 4 + TileGap * 3) * 0.5f + TileW * 0.5f;
            float[] tilts = { 7f, 2f, -2f, -7f };
            for (int i = 0; i < 4; i++)
            {
                var d = new Drop { delay = 0.2f + i * 0.16f, targetY = 120f, tilt = tilts[i] };
                d.rt = BuildTile(content, i);
                d.rt.anchoredPosition = new Vector2(x0 + i * (TileW + TileGap), 900f);
                d.cg = d.rt.gameObject.AddComponent<CanvasGroup>();
                d.cg.alpha = 0f;
                drops[i] = d;
            }

            var t = UiKit.Label(content, "Mahjong", 132, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Emboss(t);
            title = t.rectTransform; UiKit.Fixed(title, 0.5f, 0.5f, LayoutW, 170, 0, -120);
            title.pivot = new Vector2(0.5f, 0.5f);
            titleGroup = t.gameObject.AddComponent<CanvasGroup>();

            var line = UiKit.Round(content, "Underline", UiKit.Gold);
            underline = line.rectTransform; UiKit.Fixed(underline, 0.5f, 0.5f, 4, 6, 0, -214);
            underline.pivot = new Vector2(0.5f, 0.5f);

            var tg = UiKit.Label(content, "A calm, endless solitaire", 40, UiKit.InkDim, TextAnchor.MiddleCenter);
            tag = tg.rectTransform; UiKit.Fixed(tag, 0.5f, 0.5f, LayoutW, 60, 0, -262);
            tag.pivot = new Vector2(0.5f, 0.5f);
            tagGroup = tg.gameObject.AddComponent<CanvasGroup>();
            titleGroup.alpha = 0f; tagGroup.alpha = 0f;

            // loading bar along the bottom
            var track = UiKit.Round(root, "Track", new Color(1f, 1f, 1f, 0.12f));
            UiKit.Fixed(track.rectTransform, 0.5f, 0f, 360, 8, 0, 70);
            track.rectTransform.pivot = new Vector2(0.5f, 0f);
            var fill = UiKit.Round(track.transform, "Fill", UiKit.Gold);
            bar = fill.rectTransform;
            bar.pivot = new Vector2(0, 0.5f);
            bar.anchorMin = Vector2.zero; bar.anchorMax = new Vector2(0f, 1f);
            bar.offsetMin = Vector2.zero; bar.offsetMax = Vector2.zero;

            Active = true;
            start = Time.unscaledTime;
            Tick(false);
        }

        /// <summary>One ivory tile with a green edge below it and a pattern of dots on its face.</summary>
        static RectTransform BuildTile(Transform parent, int kind)
        {
            var rt = UiKit.NewRect("Tile" + kind, parent);
            UiKit.Fixed(rt, 0.5f, 0.5f, TileW, TileH, 0, 0);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var shadow = UiKit.Img(rt, "Shadow", new Color(0f, 0f, 0f, 0.35f), TileArt.ShadowSprite());
            UiKit.Fixed(shadow.rectTransform, 0.5f, 0.5f, TileW * 1.5f, TileH * 1.3f, 0, -26);
            shadow.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            var side = UiKit.Round(rt, "Side", new Color32(46, 125, 94, 255));
            UiKit.Fixed(side.rectTransform, 0.5f, 0.5f, TileW, TileH, 0, -9);
            side.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var face = UiKit.Round(rt, "Face", new Color32(247, 241, 224, 255));
            UiKit.Fixed(face.rectTransform, 0.5f, 0.5f, TileW - 4, TileH - 8, 0, 4);
            face.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            Color red = new Color32(196, 52, 44, 255), green = new Color32(36, 140, 84, 255), blue = new Color32(40, 98, 176, 255);
            Vector2[] pts; float size; Color[] cols;
            switch (kind)
            {
                case 0: pts = new[] { Vector2.zero }; size = 84; cols = new[] { red }; break;
                case 1: pts = new[] { new Vector2(0, 46), new Vector2(0, -46) }; size = 56; cols = new[] { green, red }; break;
                case 2: pts = new[] { new Vector2(-30, 54), new Vector2(0, 0), new Vector2(30, -54) }; size = 44; cols = new[] { blue, red, green }; break;
                default: pts = new[] { new Vector2(-30, 46), new Vector2(30, 46), new Vector2(-30, -46), new Vector2(30, -46) }; size = 44; cols = new[] { green, red, red, green }; break;
            }
            for (int i = 0; i < pts.Length; i++)
            {
                var ring = UiKit.Img(face.transform, "dot", cols[i], TileArt.CircleSprite());
                UiKit.Fixed(ring.rectTransform, 0.5f, 0.5f, size, size, pts[i].x, pts[i].y + 2);
                ring.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                var hole = UiKit.Img(ring.transform, "hole", new Color32(247, 241, 224, 255), TileArt.CircleSprite());
                UiKit.Fixed(hole.rectTransform, 0.5f, 0.5f, size * 0.42f, size * 0.42f, 0, 0);
                hole.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            }
            return rt;
        }

        static Texture2D GradientTexture()
        {
            const int h = 128;
            var t = new Texture2D(1, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                float k = y / (h - 1f);                              // 0 bottom .. 1 top
                float mid = 1f - Mathf.Abs(k - 0.55f) * 1.8f;
                Color c = Color.Lerp(new Color(0.03f, 0.08f, 0.09f), new Color(0.07f, 0.24f, 0.22f), Mathf.Clamp01(mid));
                t.SetPixel(0, y, c);
            }
            t.wrapMode = TextureWrapMode.Clamp;
            t.Apply(false, true);
            return t;
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>Advances the animation. 'ready' means the tiles are drawn and the home screen is up behind the splash.</summary>
        public void Tick(bool ready)
        {
            if (!Active) return;
            float now = Time.unscaledTime, t = now - start;

            // fit the layout to narrow screens
            float w = root.rect.width > 1f ? root.rect.width : LayoutW;
            float sc = Mathf.Min(1f, w / LayoutW);
            content.localScale = new Vector3(sc, sc, 1f);

            float lastLand = 0f;
            for (int i = 0; i < drops.Length; i++)
            {
                var d = drops[i];
                float p = Mathf.Clamp01((t - d.delay) / 0.6f);
                float k = EaseOutBack(p);
                var pos = d.rt.anchoredPosition;
                pos.y = Mathf.Lerp(700f, d.targetY, k);
                d.rt.anchoredPosition = pos;
                d.rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(d.tilt * 5f, d.tilt, k));
                float squash = p > 0.55f && p < 1f ? 1f + 0.04f * Mathf.Sin((p - 0.55f) / 0.45f * Mathf.PI) : 1f;
                d.rt.localScale = new Vector3(squash, 2f - squash, 1f);
                d.cg.alpha = Mathf.Clamp01(p * 5f);
                if (!d.landed && p >= 0.6f)
                {
                    d.landed = true;
                    if (sfx != null) sfx("select", 0.5f, 0.85f + 0.12f * i);
                }
                lastLand = d.delay + 0.6f;
            }

            // glow blooms once the tiles are down, then breathes
            float gk = Mathf.Clamp01((t - lastLand + 0.2f) / 0.6f);
            glow.color = new Color(1f, 0.82f, 0.35f, gk * (0.09f + 0.02f * Mathf.Sin(t * 2.4f)));

            float tk = Mathf.Clamp01((t - (lastLand - 0.15f)) / 0.5f);
            titleGroup.alpha = tk;
            title.localScale = Vector3.one * Mathf.Lerp(0.88f, 1f, EaseOutBack(tk));
            float uk = Mathf.Clamp01((t - (lastLand + 0.1f)) / 0.5f);
            underline.sizeDelta = new Vector2(Mathf.Lerp(4f, 420f, 1f - (1f - uk) * (1f - uk)), 6f);
            tagGroup.alpha = Mathf.Clamp01((t - (lastLand + 0.25f)) / 0.5f);

            // sparkles drifting up
            for (int i = 0; i < Sparkles; i++)
            {
                float speed = 0.05f + Hash(i + 7) * 0.07f;
                float f = (Hash(i) + t * speed) % 1f;
                float x = Hash(i + 20);
                sparks[i].anchorMin = sparks[i].anchorMax = new Vector2(x, f);
                sparks[i].anchoredPosition = new Vector2(Mathf.Sin(t * 0.8f + i) * 14f, 0f);
                sparkImg[i].color = new Color(1f, 0.9f, 0.55f, Mathf.Sin(f * Mathf.PI) * 0.5f * Mathf.Clamp01(t / 0.8f));
            }

            // progress: fills over the minimum time, and waits near the end for the game
            float prog = Mathf.Clamp01(t / MinSeconds);
            if (!ready) prog = Mathf.Min(prog, 0.92f);
            bar.anchorMax = new Vector2(prog, 1f);

            if (leaving < 0f)
            {
                bool skip = t > SkipAfter && (Input.GetMouseButtonDown(0) || Input.anyKeyDown ||
                                              (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began));
                if (ready && (t >= MinSeconds || skip)) leaving = now;
            }
            else
            {
                float k = (now - leaving) / FadeOut;
                rootGroup.alpha = 1f - Mathf.Clamp01(k);
                content.localScale = new Vector3(sc, sc, 1f) * (1f + 0.06f * Mathf.Clamp01(k));
                if (k >= 1f) { Active = false; root.gameObject.SetActive(false); }
            }
            if (leaving >= 0f) rootGroup.blocksRaycasts = false;
        }
    }
}
