using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Mahjong.Core;

namespace Mahjong.Game
{
    // ===================================================================== COLLECTION

    public sealed class CollectionScreen : ScreenBase
    {
        RectTransform body, content;
        readonly Dictionary<string, Sprite[]> previews = new Dictionary<string, Sprite[]>();
        readonly Dictionary<string, Texture2D> bgPreviews = new Dictionary<string, Texture2D>();

        public override void Build(App a, Transform parent)
        {
            base.Build(a, parent);
            Chrome.Scrim(Root);
            Chrome.Header(Root, "Collection", () => app.Show("home"));
            body = Chrome.Body(Root);
            var sr = UiKit.Scroll(body, "Scroll", out content);
            UiKit.Stretch((RectTransform)sr.transform);
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 14; v.padding = new RectOffset(4, 4, 4, 40);
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        }

        public override void OnShow() { Rebuild(); }
        public override bool OnBack() { app.Show("home"); return true; }
        public override void OnLayout() { if (app != null && body != null) Chrome.FitWidth(body, Root, 860); }

        Sprite[] Previews(TileStyle st)
        {
            Sprite[] sp;
            if (previews.TryGetValue(st.Id, out sp)) return sp;
            int[] kinds = { 4, 20, 31 };
            sp = new Sprite[3];
            for (int i = 0; i < 3; i++)
            {
                var r = TileArt.DrawTile(kinds[i], st);
                var t = r.ToTexture(false, FilterMode.Bilinear);
                sp[i] = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), TileAtlas.Ppu);
            }
            previews[st.Id] = sp;
            return sp;
        }

        void Rebuild()
        {
            for (int i = content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
            var d = app.Save.Data;
            Section("Tile sets");
            foreach (var st in Content.TileSets)
            {
                var style = st;
                bool open = app.Save.IsUnlocked(style.Unlock);
                bool equipped = d.settings.tileSet == style.Id;
                var card = UiKit.Btn(content, "", () => { if (open && !equipped) { app.SetTileSet(style.Id); Rebuild(); } else if (!open) app.Toast(style.Unlock.Describe()); },
                    equipped ? new Color(0.20f, 0.46f, 0.38f) : (open ? UiKit.Secondary : new Color(0.2f, 0.24f, 0.3f, 0.7f)), 30, "Set");
                UiKit.Size(card, -1, 170);
                var sp = Previews(style);
                var row = UiKit.HStack(UiKit.Face(card), "Pv", 8, TextAnchor.MiddleLeft);
                row.childForceExpandWidth = false;
                UiKit.Anchor((RectTransform)row.transform, 0, 0, 0.5f, 1, 14, 10, 0, -10);
                for (int i = 0; i < 3; i++)
                {
                    var im = UiKit.Img(row.transform, "p", open ? Color.white : new Color(1, 1, 1, 0.45f));
                    im.sprite = sp[i]; im.preserveAspect = true;
                    UiKit.Size(im, 90, 117);
                }
                Info(UiKit.Face(card), style.Name, equipped ? "Equipped" : (open ? "Tap to use" : style.Unlock.Describe()), equipped);
            }
            Section("Backgrounds");
            foreach (var bgd in Content.Backgrounds)
            {
                var def = bgd;
                bool open = app.Save.IsUnlocked(def.Unlock);
                bool equipped = d.settings.background == def.Id;
                var card = UiKit.Btn(content, "", () =>
                {
                    if (open && !equipped) { app.S.background = def.Id; app.Save.Save(); app.ApplyBackground(); Rebuild(); }
                    else if (!open) app.Toast(def.Unlock.Describe());
                }, equipped ? new Color(0.20f, 0.46f, 0.38f) : (open ? UiKit.Secondary : new Color(0.2f, 0.24f, 0.3f, 0.7f)), 30, "Bg");
                UiKit.Size(card, -1, 130);
                Texture2D tex;
                if (!bgPreviews.TryGetValue(def.Id, out tex)) { tex = TileArt.BackgroundTexture(def); bgPreviews[def.Id] = tex; }
                var rt = UiKit.NewRect("Pv", UiKit.Face(card));
                UiKit.Anchor(rt, 0, 0, 0.42f, 1, 14, 14, 0, -14);
                var raw = rt.gameObject.AddComponent<RawImage>();
                raw.texture = tex; raw.raycastTarget = false; raw.color = open ? Color.white : new Color(1, 1, 1, 0.5f);
                Info(UiKit.Face(card), def.Name, equipped ? "In use" : (open ? "Tap to use" : def.Unlock.Describe()), equipped);
            }
        }

        void Section(string title)
        {
            var l = UiKit.Label(content, title, 44, UiKit.Gold, TextAnchor.LowerLeft, FontStyle.Bold);
            UiKit.Size(l, -1, 78);
        }

        void Info(Transform card, string name, string status, bool highlight)
        {
            var n = UiKit.Label(card, name, 40, UiKit.Ink, TextAnchor.LowerLeft, FontStyle.Bold);
            UiKit.Anchor(n.rectTransform, 0.46f, 0.5f, 1, 1, 0, 0, -16, -8);
            var s = UiKit.Label(card, status, 30, highlight ? UiKit.Gold : UiKit.InkDim, TextAnchor.UpperLeft);
            UiKit.Anchor(s.rectTransform, 0.46f, 0, 1, 0.5f, 0, 8, -16, 0);
        }
    }

    // ===================================================================== SETTINGS

    public sealed class SettingsScreen : ScreenBase
    {
        public string ReturnTo = "home";
        RectTransform body, content;
        SettingsData S { get { return app.S; } }
        readonly List<Action> refreshers = new List<Action>();

        public override void Build(App a, Transform parent)
        {
            base.Build(a, parent);
            Chrome.Scrim(Root);
            Chrome.Header(Root, "Settings", Back);
            body = Chrome.Body(Root);
            var sr = UiKit.Scroll(body, "Scroll", out content);
            UiKit.Stretch((RectTransform)sr.transform);
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 12; v.padding = new RectOffset(4, 4, 8, 40);
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            BuildRows();
        }

        void Back() { app.Show(string.IsNullOrEmpty(ReturnTo) ? "home" : ReturnTo); }
        public override bool OnBack() { Back(); return true; }
        public override void OnShow() { foreach (var r in refreshers) r(); }
        public override void OnLayout() { if (app != null && body != null) Chrome.FitWidth(body, Root, 860); }

        static int Nearest(float[] vals, float v)
        {
            int best = 0; float bd = 99f;
            for (int i = 0; i < vals.Length; i++) { float d = Mathf.Abs(vals[i] - v); if (d < bd) { bd = d; best = i; } }
            return best;
        }

        void Title(string t)
        {
            var l = UiKit.Label(content, t, 40, UiKit.Gold, TextAnchor.LowerLeft, FontStyle.Bold);
            UiKit.Size(l, -1, 70);
        }

        void Cycle(string label, string[] opts, Func<int> get, Action<int> set, string help = null)
        {
            var card = UiKit.Round(content, "Row", UiKit.Card, true);
            UiKit.Size(card, -1, help != null ? 150 : 118);
            var l = UiKit.Label(card.transform, label, 38, UiKit.Ink, TextAnchor.MiddleLeft);
            UiKit.Anchor(l.rectTransform, 0, help != null ? 0.38f : 0, 0.55f, 1, 24, 0, 0, 0);
            if (help != null)
            {
                var h = UiKit.Label(card.transform, help, 24, UiKit.InkDim, TextAnchor.UpperLeft);
                UiKit.Anchor(h.rectTransform, 0, 0, 0.56f, 0.4f, 24, 8, 0, 0);
            }
            Button btn = null;
            btn = UiKit.Btn(card.transform, opts[get()], () =>
            {
                int n = (get() + 1) % opts.Length;
                set(n);
                UiKit.ButtonText(btn).text = opts[n];
                app.ApplySettings();
                app.Save.Save();
            }, UiKit.Secondary, 30, "Opt");
            UiKit.Fixed(btn.GetComponent<RectTransform>(), 1f, 0.5f, 300, 84, -14, -1);
            btn.GetComponent<RectTransform>().pivot = new Vector2(1f, 0.5f);
            refreshers.Add(() => UiKit.ButtonText(btn).text = opts[get()]);
        }

        void Toggle(string label, Func<bool> get, Action<bool> set, string help = null)
        {
            Cycle(label, new[] { "Off", "On" }, () => get() ? 1 : 0, i => set(i == 1), help);
        }

        void BuildRows()
        {
            float[] sfx = { 0f, 0.35f, 0.7f, 1f };
            float[] mus = { 0f, 0.15f, 0.3f, 0.5f };
            string[] vol = { "Off", "Low", "Medium", "High" };

            Title("Sound");
            Cycle("Sound effects", vol, () => Nearest(sfx, S.sound), i => S.sound = sfx[i]);
            Cycle("Music", vol, () => Nearest(mus, S.music), i => S.music = mus[i]);

            Title("Seeing the tiles");
            Cycle("Tile size", new[] { "Fit screen", "Large", "Extra large" }, () => S.tileSize, i => S.tileSize = i,
                "Large sizes keep tiles big and let you drag and zoom the board.");
            Cycle("Text size", new[] { "100%", "125%", "150%" }, () => S.textScale, i => S.textScale = i);
            Cycle("Blocked tiles", new[] { "Not dimmed", "Slightly dimmed", "Very dimmed" }, () => S.dimBlocked, i => S.dimBlocked = i,
                "Dimmed tiles are the ones you cannot take yet.");
            Toggle("Show what blocks a tile", () => S.showBlockers, v => S.showBlockers = v,
                "Tap a blocked tile and the tiles in the way flash.");
            Toggle("Match assist", () => S.matchAssist, v => S.matchAssist = v,
                "After you pick a tile, its only free partner glows.");
            Toggle("Reduced motion", () => S.reducedMotion, v => S.reducedMotion = v, "Fewer animations and no particles.");
            Toggle("Left-handed layout", () => S.leftHanded, v => S.leftHanded = v);

            Title("Playing");
            Cycle("Difficulty", new[] { "Much easier", "Easier", "Normal", "Harder", "Much harder" },
                () => S.difficultyOffset + 2, i => S.difficultyOffset = i - 2,
                "Shifts every level up or down. Your level number stays the same.");
            Toggle("Confirm before restarting", () => S.confirmRestart, v => S.confirmRestart = v);
            Toggle("Go to the next level automatically", () => S.autoContinue, v => S.autoContinue = v);

            Title("Your progress");
            var row = UiKit.HStack(content, "Export", 12);
            UiKit.Size(row, -1, 92);
            UiKit.Btn(row.transform, "Copy save code", () =>
            {
                GUIUtility.systemCopyBuffer = app.Save.Export();
                app.Toast("Save code copied. Paste it on another device.");
            }, UiKit.Secondary, 30);
            UiKit.Btn(row.transform, "Load save code", () =>
            {
                string msg = app.Save.Import(GUIUtility.systemCopyBuffer);
                app.ApplySettings(); app.ApplyBackground();
                foreach (var r in refreshers) r();
                app.Toast(msg);
            }, UiKit.Secondary, 30);
            var note = UiKit.Label(content, "Progress is saved on this device only. No account, no ads, no tracking, nothing to buy.", 28, UiKit.InkDim);
            UiKit.Size(note, -1, 90);
        }
    }

    // ===================================================================== STATS

    public sealed class StatsScreen : ScreenBase
    {
        RectTransform body, content;

        public override void Build(App a, Transform parent)
        {
            base.Build(a, parent);
            Chrome.Scrim(Root);
            Chrome.Header(Root, "Stats", () => app.Show("home"));
            body = Chrome.Body(Root);
            var sr = UiKit.Scroll(body, "Scroll", out content);
            UiKit.Stretch((RectTransform)sr.transform);
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 10; v.padding = new RectOffset(4, 4, 8, 40);
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        }

        public override bool OnBack() { app.Show("home"); return true; }
        public override void OnLayout() { if (app != null && body != null) Chrome.FitWidth(body, Root, 860); }

        void Line(string k, string v)
        {
            var card = UiKit.Round(content, "Line", UiKit.Card);
            UiKit.Size(card, -1, 86);
            var l = UiKit.Label(card.transform, k, 36, UiKit.InkDim, TextAnchor.MiddleLeft);
            UiKit.Anchor(l.rectTransform, 0, 0, 0.6f, 1, 22, 0, 0, 0);
            var r = UiKit.Label(card.transform, v, 40, UiKit.Ink, TextAnchor.MiddleRight, FontStyle.Bold);
            UiKit.Anchor(r.rectTransform, 0.5f, 0, 1, 1, 0, 0, -22, 0);
        }

        static string Duration(int s)
        {
            if (s >= 3600) return (s / 3600) + "h " + ((s % 3600) / 60) + "m";
            return (s / 60) + "m " + (s % 60) + "s";
        }

        public override void OnShow()
        {
            for (int i = content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
            var d = app.Save.Data;
            var t = UiKit.Label(content, "Totals", 44, UiKit.Gold, TextAnchor.LowerLeft, FontStyle.Bold); UiKit.Size(t, -1, 70);
            Line("Levels cleared", d.levelsWon.ToString());
            Line("Highest level", d.highestLevel.ToString());
            Line("Stars", d.totalStars.ToString());
            Line("Pairs removed", d.pairsRemoved.ToString());
            Line("Best combo", d.bestCombo > 0 ? "x" + d.bestCombo : "-");
            Line("Wins without a shuffle", d.noShuffleWins.ToString());
            Line("Daily challenges", d.dailiesWon.ToString());
            Line("Current streak", app.Save.CurrentStreak() + " days");
            Line("Longest streak", d.longestStreak + " days");
            Line("Month badges", d.badges.Count.ToString());
            Line("Time played", Duration(d.secondsPlayed));

            var a = UiKit.Label(content, "Achievements  " + d.achievements.Count + " / " + Achievements.All.Length, 44, UiKit.Gold, TextAnchor.LowerLeft, FontStyle.Bold);
            UiKit.Size(a, -1, 90);
            foreach (var ach in Achievements.All)
            {
                bool got = d.achievements.Contains(ach.Id);
                var card = UiKit.Round(content, "Ach", got ? new Color(0.14f, 0.30f, 0.26f, 0.96f) : UiKit.Card);
                UiKit.Size(card, -1, 100);
                var star = UiKit.Img(card.transform, "Star", got ? UiKit.Gold : new Color(1, 1, 1, 0.18f), TileArt.StarSprite());
                UiKit.Fixed(star.rectTransform, 0f, 0.5f, 56, 56, 22, 0);
                star.rectTransform.pivot = new Vector2(0f, 0.5f);
                var n = UiKit.Label(card.transform, ach.Title, 36, got ? UiKit.Ink : UiKit.InkDim, TextAnchor.LowerLeft, FontStyle.Bold);
                UiKit.Anchor(n.rectTransform, 0, 0.5f, 1, 1, 98, 0, -16, -6);
                var ds = UiKit.Label(card.transform, ach.Desc, 27, UiKit.InkDim, TextAnchor.UpperLeft);
                UiKit.Anchor(ds.rectTransform, 0, 0, 1, 0.5f, 98, 8, -16, 0);
            }
        }
    }
}
