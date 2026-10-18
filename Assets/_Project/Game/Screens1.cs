using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Mahjong.Core;

namespace Mahjong.Game
{
    /// <summary>Shared scaffolding for the secondary screens.</summary>
    public static class Chrome
    {
        public const float HeaderH = 120f;

        public static void Scrim(Transform parent)
        {
            var s = UiKit.Img(parent, "Scrim", new Color(0f, 0f, 0f, 0.30f));
            UiKit.Stretch(s.rectTransform);
        }

        public static RectTransform Header(Transform parent, string title, UnityAction back)
        {
            var h = UiKit.NewRect("Header", parent);
            UiKit.Anchor(h, 0, 1, 1, 1);
            h.pivot = new Vector2(0.5f, 1f);
            h.offsetMin = new Vector2(0, -HeaderH); h.offsetMax = Vector2.zero;
            var bg = UiKit.Img(h, "Bg", new Color(0, 0, 0, 0.28f));
            UiKit.Stretch(bg.rectTransform);
            var b = UiKit.Btn(h, "Back", back, UiKit.Secondary, 32, "Back");
            UiKit.Fixed(b.GetComponent<RectTransform>(), 0f, 0.5f, 160, 84, 18, -2);
            var t = UiKit.Label(h, title, 54, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(t.rectTransform, 0.2f, 0, 0.8f, 1);
            return h;
        }

        /// <summary>Content area below the header, centred and capped in width.</summary>
        public static RectTransform Body(Transform parent)
        {
            var b = UiKit.NewRect("Body", parent);
            b.anchorMin = new Vector2(0.5f, 0f); b.anchorMax = new Vector2(0.5f, 1f);
            b.pivot = new Vector2(0.5f, 1f);
            b.offsetMin = new Vector2(-400, 20); b.offsetMax = new Vector2(400, -HeaderH - 10);
            return b;
        }

        public static void FitWidth(RectTransform body, RectTransform root, float maxWidth)
        {
            float w = Mathf.Min(maxWidth, root.rect.width - 24f);
            body.offsetMin = new Vector2(-w * 0.5f, 20); body.offsetMax = new Vector2(w * 0.5f, -HeaderH - 10);
        }

        public static InputField Input(Transform parent, string placeholder, InputField.ContentType type, int limit)
        {
            var img = UiKit.Round(parent, "Input", new Color(0.10f, 0.13f, 0.18f, 1f), true);
            var f = img.gameObject.AddComponent<InputField>();
            f.targetGraphic = img;
            var text = UiKit.Label(img.transform, "", 40, UiKit.Ink, TextAnchor.MiddleCenter);
            text.resizeTextForBestFit = false; text.supportRichText = false;
            UiKit.Stretch(text.rectTransform); text.rectTransform.offsetMin = new Vector2(14, 6); text.rectTransform.offsetMax = new Vector2(-14, -6);
            var ph = UiKit.Label(img.transform, placeholder, 34, new Color(1, 1, 1, 0.35f), TextAnchor.MiddleCenter, FontStyle.Italic);
            ph.resizeTextForBestFit = false;
            UiKit.Stretch(ph.rectTransform); ph.rectTransform.offsetMin = new Vector2(14, 6); ph.rectTransform.offsetMax = new Vector2(-14, -6);
            f.textComponent = text; f.placeholder = ph;
            f.contentType = type; f.characterLimit = limit;
            return f;
        }
    }

    // ===================================================================== HOME

    public sealed class HomeScreen : ScreenBase
    {
        Text playSub, dailySub, versionLbl;
        Button skipBtn;
        Image[] decor = new Image[3];
        RectTransform column;
        int versionTaps;

        public override void Build(App a, Transform parent)
        {
            base.Build(a, parent);
            Chrome.Scrim(Root);
            var v = UiKit.VStack(Root, "Column", 8, TextAnchor.MiddleCenter);
            column = (RectTransform)v.transform;
            column.anchorMin = column.anchorMax = new Vector2(0.5f, 0.5f);
            column.pivot = new Vector2(0.5f, 0.5f);
            column.sizeDelta = new Vector2(620, 0);
            var fit = column.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // decorative tiles
            var drow = UiKit.HStack(column, "Decor", 18, TextAnchor.MiddleCenter);
            drow.childForceExpandWidth = false; drow.childForceExpandHeight = false;
            UiKit.Size(drow, -1, 190);
            float[] rot = { 8f, 0f, -8f };
            for (int i = 0; i < 3; i++)
            {
                decor[i] = UiKit.Img(drow.transform, "d" + i, Color.white);
                UiKit.Size(decor[i], 140, 182);
                decor[i].rectTransform.localRotation = Quaternion.Euler(0, 0, rot[i]);
                decor[i].preserveAspect = true;
            }
            var title = UiKit.Label(column, "Mahjong", 116, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(title, -1, 140);
            var tag = UiKit.Label(column, "A calm, endless solitaire", 34, UiKit.InkDim);
            UiKit.Size(tag, -1, 48);
            Gap(14);

            var play = UiKit.Btn(column, "", () => app.StartJourney(app.Save.Data.currentLevel), UiKit.Primary, 40, "Play");
            UiKit.Size(play, -1, 146);
            var pt = UiKit.Label(UiKit.Face(play), "Play", 64, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(pt.rectTransform, 0, 0.36f, 1, 1, 0, 0, 0, -4);
            UiKit.Emboss(pt);
            playSub = UiKit.Label(UiKit.Face(play), "Level 1", 30, new Color(1, 1, 1, 0.85f), TextAnchor.MiddleCenter);
            UiKit.Anchor(playSub.rectTransform, 0, 0, 1, 0.40f, 0, 8, 0, 0);

            skipBtn = UiKit.Btn(column, "I already know how to play", SkipTutorial, new Color(1, 1, 1, 0.14f), 26, "Skip");
            UiKit.Size(skipBtn, -1, 66);

            var daily = UiKit.Btn(column, "", () => app.Show("daily"), UiKit.Secondary, 40, "Daily");
            UiKit.Size(daily, -1, 116);
            var dt = UiKit.Label(UiKit.Face(daily), "Daily Challenge", 40, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(dt.rectTransform, 0, 0.48f, 1, 1, 0, 0, 0, -4);
            UiKit.Emboss(dt);
            dailySub = UiKit.Label(UiKit.Face(daily), "", 26, UiKit.InkDim, TextAnchor.MiddleCenter);
            UiKit.Anchor(dailySub.rectTransform, 0, 0, 1, 0.50f, 0, 6, 0, 0);

            Row("Free Play", () => app.Show("freeplay"), "Levels", () => app.Show("levels"));
            Row("Collection", () => app.Show("collection"), "Stats", () => app.Show("stats"));
            var set = UiKit.Btn(column, "Settings", () => { app.Get<SettingsScreen>().ReturnTo = "home"; app.Show("settings"); }, UiKit.Secondary, 34, "Settings");
            UiKit.Size(set, -1, 90);

            versionLbl = UiKit.Label(Root, "Mahjong " + App.Version, 24, new Color(1, 1, 1, 0.45f), TextAnchor.LowerCenter);
            UiKit.Anchor(versionLbl.rectTransform, 0.2f, 0, 0.8f, 0, 0, 10, 0, 56);
            versionLbl.resizeTextForBestFit = false;
            var vb = versionLbl.gameObject.AddComponent<Button>();
            versionLbl.raycastTarget = true;
            vb.targetGraphic = versionLbl;
            vb.transition = Selectable.Transition.None;
            vb.onClick.AddListener(() =>
            {
                if (++versionTaps >= 7) { versionTaps = 0; app.DebugMode = !app.DebugMode; app.Toast(app.DebugMode ? "Debug tools on" : "Debug tools off"); Refresh(); }
            });
        }

        void Gap(float h) { var g = UiKit.NewRect("Gap", column); UiKit.Size(g, -1, h); }

        void Row(string a, UnityAction ca, string b, UnityAction cb)
        {
            var r = UiKit.HStack(column, "Row", 8);
            UiKit.Size(r, -1, 90);
            UiKit.Btn(r.transform, a, ca, UiKit.Secondary, 34);
            UiKit.Btn(r.transform, b, cb, UiKit.Secondary, 34);
        }

        void SkipTutorial()
        {
            app.Save.Data.tutorialDone = true;
            if (app.Save.Data.currentLevel <= DifficultyConfig.TutorialLevels) app.Save.Data.currentLevel = DifficultyConfig.TutorialLevels + 1;
            app.Save.Save();
            Refresh();
        }

        public override void OnShow() { Refresh(); }

        public override void OnLayout()
        {
            if (app == null || column == null) return;
            float w = app.Portrait ? 700 : 760;
            column.sizeDelta = new Vector2(w, 0);
            // in landscape the tall column does not fit comfortably, so shrink it a touch
            float avail = Root.rect.height - 90;
            LayoutRebuilder.ForceRebuildLayoutImmediate(column);
            float need = column.rect.height;
            float k = need > avail && need > 1 ? avail / need : 1f;
            column.localScale = new Vector3(k, k, 1);
        }

        void Refresh()
        {
            if (app == null || playSub == null) return;
            var d = app.Save.Data;
            var ip = d.inProgress;
            bool resuming = ip != null && ip.mode == "journey" && ip.level == d.currentLevel;
            playSub.text = (resuming ? "Continue  ·  Level " : "Level ") + d.currentLevel;
            skipBtn.gameObject.SetActive(d.currentLevel <= DifficultyConfig.TutorialLevels);

            var today = DateUtil.TodayUtc();
            var rec = app.Save.GetDaily(DateUtil.ToInt(today));
            int streak = app.Save.CurrentStreak();
            string line;
            if (rec != null) line = "Done today" + (streak > 1 ? "  ·  " + streak + " day streak" : "");
            else if (ip != null && ip.mode == "daily" && ip.day == DateUtil.ToInt(today)) line = "In progress";
            else line = today.ToString("MMMM d") + (streak > 0 ? "  ·  " + streak + " day streak" : "");
            dailySub.text = line;

            if (app.Atlas != null)
            {
                int[] kinds = { 31, 4, 27 };
                for (int i = 0; i < 3; i++) { decor[i].sprite = app.Atlas.Faces[kinds[i]]; decor[i].color = Color.white; }
            }
            versionLbl.text = "Mahjong " + App.Version + (app.DebugMode ? "  (debug)" : "");
            OnLayout();
        }

        public override bool OnBack()
        {
#if UNITY_ANDROID
            Application.Quit();
#endif
            return true;
        }
    }

    // ===================================================================== DAILY

    public sealed class DailyScreen : ScreenBase
    {
        RectTransform body, cal, side, grid, monthRow, weekRow;
        Text monthLbl, infoLbl;
        Button prevBtn, nextBtn, todayBtn;
        readonly Button[] cells = new Button[42];
        readonly Text[] cellText = new Text[42];
        readonly Image[] cellMark = new Image[42];
        int viewYear, viewMonth;
        GridLayoutGroup glg;

        public override void Build(App a, Transform parent)
        {
            base.Build(a, parent);
            Chrome.Scrim(Root);
            Chrome.Header(Root, "Daily Challenge", () => app.Show("home"));
            body = Chrome.Body(Root);
            cal = UiKit.NewRect("Calendar", body);
            side = UiKit.NewRect("Side", body);

            var mrow = UiKit.HStack(cal, "Month", 10);
            mrow.childForceExpandWidth = false;
            monthRow = (RectTransform)mrow.transform;
            UiKit.Anchor(monthRow, 0, 1, 1, 1, 0, -92, 0, 0);
            prevBtn = UiKit.Btn(mrow.transform, "<", () => Step(-1), UiKit.Secondary, 42);
            UiKit.Size(prevBtn, 96, 86);
            monthLbl = UiKit.Label(mrow.transform, "", 52, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(monthLbl, -1, 90, 1);
            nextBtn = UiKit.Btn(mrow.transform, ">", () => Step(1), UiKit.Secondary, 42);
            UiKit.Size(nextBtn, 96, 86);

            var wk = UiKit.HStack(cal, "Weekdays", 0);
            weekRow = (RectTransform)wk.transform;
            UiKit.Anchor(weekRow, 0, 1, 1, 1, 0, -136, 0, -96);
            string[] days = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            for (int i = 0; i < 7; i++) UiKit.Label(wk.transform, days[i], 28, UiKit.InkDim);

            grid = UiKit.NewRect("Grid", cal);
            grid.anchorMin = new Vector2(0, 1); grid.anchorMax = new Vector2(1, 1); grid.pivot = new Vector2(0.5f, 1f);
            glg = grid.gameObject.AddComponent<GridLayoutGroup>();
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount; glg.constraintCount = 7;
            glg.spacing = new Vector2(6, 6);
            for (int i = 0; i < 42; i++)
            {
                int idx = i;
                var b = UiKit.Btn(grid, "", () => OnCell(idx), UiKit.Secondary, 30, "Day");
                cells[i] = b;
                cellText[i] = UiKit.Label(UiKit.Face(b), "", 36, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Anchor(cellText[i].rectTransform, 0, 0.3f, 1, 1);
                cellMark[i] = UiKit.Img(UiKit.Face(b), "Mark", UiKit.Gold, TileArt.StarSprite());
                UiKit.Fixed(cellMark[i].rectTransform, 0.5f, 0.04f, 24, 24, 0, 0);
                cellMark[i].rectTransform.pivot = new Vector2(0.5f, 0f);
            }

            infoLbl = UiKit.Label(side, "", 34, UiKit.Ink, TextAnchor.MiddleCenter);
            todayBtn = UiKit.Btn(side, "Play today's board", PlayToday, UiKit.Primary, 38, "PlayToday");
        }

        public override void OnShow()
        {
            var t = DateUtil.TodayUtc();
            viewYear = t.Year; viewMonth = t.Month;
            Refresh();
        }

        public override bool OnBack() { app.Show("home"); return true; }

        public override void OnLayout()
        {
            if (app == null || body == null) return;
            bool land = !app.Portrait;
            float bodyW = Mathf.Min(land ? 1240 : 860, Root.rect.width - 24f);
            float bodyH = Root.rect.height - Chrome.HeaderH - 30f;
            body.offsetMin = new Vector2(-bodyW * 0.5f, 20); body.offsetMax = new Vector2(bodyW * 0.5f, -Chrome.HeaderH - 10);

            float calW = land ? bodyW * 0.62f : bodyW;
            float sideH = land ? 0f : 240f;
            cal.anchorMin = new Vector2(0, 0); cal.anchorMax = new Vector2(0, 1);
            cal.pivot = new Vector2(0, 0.5f);
            cal.offsetMin = new Vector2(0, sideH); cal.offsetMax = new Vector2(calW, 0);

            var tr = todayBtn.GetComponent<RectTransform>();
            if (land)
            {
                side.anchorMin = new Vector2(1, 0); side.anchorMax = new Vector2(1, 1);
                side.pivot = new Vector2(1, 0.5f);
                side.offsetMin = new Vector2(-(bodyW - calW - 24), 0); side.offsetMax = new Vector2(0, 0);
                UiKit.Anchor(infoLbl.rectTransform, 0, 0.5f, 1, 0.5f, 0, 20, 0, 150);
                UiKit.Anchor(tr, 0, 0.5f, 1, 0.5f, 0, -110, 0, 0);
            }
            else
            {
                side.anchorMin = new Vector2(0, 0); side.anchorMax = new Vector2(1, 0);
                side.pivot = new Vector2(0.5f, 0);
                side.offsetMin = new Vector2(0, 0); side.offsetMax = new Vector2(0, sideH);
                UiKit.Anchor(infoLbl.rectTransform, 0, 0, 1, 0, 0, 124, 0, 220);
                UiKit.Anchor(tr, 0, 0, 1, 0, 0, 8, 0, 116);
            }

            float header = 144f;
            float cell = Mathf.Floor((calW - 6 * 6) / 7f);
            float avail = (bodyH - sideH) - header - 6;
            cell = Mathf.Min(cell, Mathf.Floor((avail - 5 * 6) / 6f));
            cell = Mathf.Max(40f, cell);
            glg.cellSize = new Vector2(cell, cell);
            float gw = cell * 7 + 36, gh = cell * 6 + 30;
            grid.anchorMin = new Vector2(0.5f, 1); grid.anchorMax = new Vector2(0.5f, 1);
            grid.sizeDelta = new Vector2(gw, gh);
            grid.anchoredPosition = new Vector2(0, -header);
            weekRow.offsetMin = new Vector2((calW - gw) * 0.5f, -136); weekRow.offsetMax = new Vector2(-(calW - gw) * 0.5f, -96);
        }

        void Step(int d)
        {
            var m = new DateTime(viewYear, viewMonth, 1).AddMonths(d);
            var today = DateUtil.TodayUtc();
            if (m > new DateTime(today.Year, today.Month, 1)) return;
            if (m < new DateTime(today.Year, today.Month, 1).AddMonths(-12)) return;
            viewYear = m.Year; viewMonth = m.Month;
            app.Audio.Play("page", 0.5f);
            Refresh();
        }

        bool Playable(DateTime day)
        {
            var today = DateUtil.TodayUtc();
            if (day > today) return false;
            var oldest = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
            return day >= oldest;
        }

        void OnCell(int idx)
        {
            var first = new DateTime(viewYear, viewMonth, 1);
            int offset = DateUtil.WeekdayMondayZero(first);
            var day = first.AddDays(idx - offset);
            if (day.Month != viewMonth || !Playable(day)) return;
            app.StartDaily(DateUtil.ToInt(day));
        }

        void PlayToday() { app.StartDaily(DateUtil.ToInt(DateUtil.TodayUtc())); }

        void Refresh()
        {
            var first = new DateTime(viewYear, viewMonth, 1);
            var today = DateUtil.TodayUtc();
            monthLbl.text = first.ToString("MMMM yyyy");
            prevBtn.interactable = first > new DateTime(today.Year, today.Month, 1).AddMonths(-12);
            nextBtn.interactable = first < new DateTime(today.Year, today.Month, 1);
            int offset = DateUtil.WeekdayMondayZero(first);
            int dim = DateTime.DaysInMonth(viewYear, viewMonth);
            int done = 0;
            for (int i = 0; i < 42; i++)
            {
                int dayNo = i - offset + 1;
                var cell = cells[i];
                if (dayNo < 1 || dayNo > dim) { cell.gameObject.SetActive(false); continue; }
                cell.gameObject.SetActive(true);
                var date = new DateTime(viewYear, viewMonth, dayNo, 0, 0, 0, DateTimeKind.Utc);
                var rec = app.Save.GetDaily(DateUtil.ToInt(date));
                bool play = Playable(date);
                cellText[i].text = dayNo.ToString();
                var img = cell.GetComponent<Image>();
                if (rec != null)
                {
                    done++;
                    img.color = new Color(0.20f, 0.46f, 0.38f);
                    cellMark[i].enabled = true;
                    cellMark[i].color = rec.gold ? UiKit.Gold : new Color(0.82f, 0.85f, 0.9f);
                }
                else
                {
                    img.color = play ? UiKit.Secondary : new Color(0.2f, 0.24f, 0.3f, 0.55f);
                    cellMark[i].enabled = false;
                }
                cellText[i].color = play || rec != null ? UiKit.Ink : new Color(1, 1, 1, 0.3f);
                cell.interactable = play;
                if (date == today && rec == null) img.color = new Color(0.28f, 0.52f, 0.62f);
            }
            int streak = app.Save.CurrentStreak();
            bool badge = app.Save.Data.badges.Contains(viewYear * 100 + viewMonth);
            string s = done + " of " + dim + " done";
            if (badge) s += "   ·   Month badge earned!";
            else if (viewYear == today.Year && viewMonth == today.Month) s += "   ·   Streak " + streak;
            infoLbl.text = s;
            var tr = app.Save.GetDaily(DateUtil.ToInt(today));
            UiKit.ButtonText(todayBtn).text = tr != null ? "Play today again" : "Play today's board";
        }
    }

    // ===================================================================== LEVELS

    public sealed class LevelsScreen : ScreenBase
    {
        const int PageSize = 30;
        RectTransform body, grid;
        Text pageLbl;
        Button prevBtn, nextBtn;
        InputField jump;
        readonly Button[] cells = new Button[PageSize];
        readonly Text[] nums = new Text[PageSize];
        readonly Image[][] stars = new Image[PageSize][];
        int page;
        GridLayoutGroup glg;

        public override void Build(App a, Transform parent)
        {
            base.Build(a, parent);
            Chrome.Scrim(Root);
            Chrome.Header(Root, "Levels", () => app.Show("home"));
            body = Chrome.Body(Root);

            var pr = UiKit.HStack(body, "Pager", 10);
            pr.childForceExpandWidth = false;
            UiKit.Anchor((RectTransform)pr.transform, 0, 1, 1, 1, 0, -96, 0, 0);
            prevBtn = UiKit.Btn(pr.transform, "<", () => SetPage(page - 1), UiKit.Secondary, 42);
            UiKit.Size(prevBtn, 110, 86);
            pageLbl = UiKit.Label(pr.transform, "", 44, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(pageLbl, -1, 90, 1);
            nextBtn = UiKit.Btn(pr.transform, ">", () => SetPage(page + 1), UiKit.Secondary, 42);
            UiKit.Size(nextBtn, 110, 86);

            grid = UiKit.NewRect("Grid", body);
            grid.anchorMin = new Vector2(0, 1); grid.anchorMax = new Vector2(1, 1); grid.pivot = new Vector2(0.5f, 1f);
            glg = grid.gameObject.AddComponent<GridLayoutGroup>();
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount; glg.constraintCount = 5;
            glg.spacing = new Vector2(8, 8);
            for (int i = 0; i < PageSize; i++)
            {
                int idx = i;
                var b = UiKit.Btn(grid, "", () => OnCell(idx), UiKit.Secondary, 30, "Lvl");
                cells[i] = b;
                nums[i] = UiKit.Label(UiKit.Face(b), "", 36, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Anchor(nums[i].rectTransform, 0, 0.34f, 1, 1);
                var row = UiKit.HStack(UiKit.Face(b), "Stars", 2, TextAnchor.MiddleCenter);
                row.childForceExpandWidth = false;
                UiKit.Anchor((RectTransform)row.transform, 0, 0.04f, 1, 0.36f);
                stars[i] = new Image[3];
                for (int s = 0; s < 3; s++)
                {
                    stars[i][s] = UiKit.Img(row.transform, "s", UiKit.Gold, TileArt.StarSprite());
                    UiKit.Size(stars[i][s], 22, 22);
                }
            }

            var bottom = UiKit.HStack(body, "Jump", 12);
            bottom.childForceExpandWidth = false;
            var brt = (RectTransform)bottom.transform;
            brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(1, 0); brt.pivot = new Vector2(0.5f, 0);
            brt.offsetMin = new Vector2(0, 8); brt.offsetMax = new Vector2(0, 96);
            jump = Chrome.Input(bottom.transform, "Go to level", InputField.ContentType.IntegerNumber, 6);
            UiKit.Size(jump, 300, 80, 1);
            var go = UiKit.Btn(bottom.transform, "Go", GoJump, UiKit.Primary, 36);
            UiKit.Size(go, 116, 88);
            var cur = UiKit.Btn(bottom.transform, "Current", () => { page = (app.Save.Data.currentLevel - 1) / PageSize; Refresh(); }, UiKit.Secondary, 32);
            UiKit.Size(cur, 176, 88);
        }

        public override void OnShow()
        {
            page = Mathf.Max(0, (app.Save.Data.currentLevel - 1) / PageSize);
            Refresh();
        }

        public override bool OnBack() { app.Show("home"); return true; }

        public override void OnLayout()
        {
            if (app == null || body == null) return;
            Chrome.FitWidth(body, Root, 900);
            float w = body.rect.width;
            float cw = Mathf.Floor((w - 4 * 8) / 5f);
            float maxH = body.rect.height - 100 - 110;
            float ch = Mathf.Min(cw * 1.0f, Mathf.Floor((maxH - 5 * 8) / 6f));
            glg.cellSize = new Vector2(cw, ch);
            grid.offsetMin = new Vector2(0, -100 - (ch * 6 + 40)); grid.offsetMax = new Vector2(0, -100);
        }

        void SetPage(int p)
        {
            page = Mathf.Max(0, p);
            app.Audio.Play("page", 0.5f);
            Refresh();
        }

        int MaxPlayable() { return app.DebugMode ? 999999 : app.Save.Data.highestLevel + 1; }

        void OnCell(int idx)
        {
            int n = page * PageSize + idx + 1;
            if (n > MaxPlayable()) return;
            app.StartJourney(n);
        }

        void GoJump()
        {
            int n;
            if (!int.TryParse(jump.text, out n) || n < 1) { app.Toast("Type a level number"); return; }
            if (n > MaxPlayable()) { app.Toast("Reach level " + (n - 1) + " first"); return; }
            app.StartJourney(n);
        }

        void Refresh()
        {
            int first = page * PageSize + 1;
            pageLbl.text = first + " - " + (first + PageSize - 1);
            prevBtn.interactable = page > 0;
            nextBtn.interactable = first + PageSize <= MaxPlayable() + 1 || app.DebugMode;
            int cur = app.Save.Data.currentLevel;
            for (int i = 0; i < PageSize; i++)
            {
                int n = first + i;
                nums[i].text = n.ToString();
                var rec = app.Save.GetLevel(n);
                bool open = n <= MaxPlayable();
                var img = cells[i].GetComponent<Image>();
                if (rec != null) img.color = new Color(0.20f, 0.46f, 0.38f);
                else if (n == cur) img.color = new Color(0.28f, 0.52f, 0.62f);
                else img.color = open ? UiKit.Secondary : new Color(0.2f, 0.24f, 0.3f, 0.55f);
                cells[i].interactable = open;
                nums[i].color = open ? UiKit.Ink : new Color(1, 1, 1, 0.3f);
                int st = rec != null ? rec.stars : 0;
                for (int s = 0; s < 3; s++)
                {
                    stars[i][s].gameObject.SetActive(rec != null);
                    stars[i][s].color = s < st ? UiKit.Gold : new Color(1, 1, 1, 0.2f);
                }
            }
        }
    }

    // ===================================================================== FREE PLAY

    public sealed class FreePlayScreen : ScreenBase
    {
        RectTransform body;
        Text diffLbl, codeLbl;
        int difficulty = 5;
        string code;
        InputField codeInput;

        public override void Build(App a, Transform parent)
        {
            base.Build(a, parent);
            Chrome.Scrim(Root);
            Chrome.Header(Root, "Free Play", () => app.Show("home"));
            body = Chrome.Body(Root);

            var v = UiKit.VStack(body, "V", 10, TextAnchor.UpperCenter);
            UiKit.Anchor((RectTransform)v.transform, 0, 0, 1, 1);
            var info = UiKit.Label(v.transform, "Play any board you like. No stars, no pressure. Share the board code with a friend and you will both get the same tiles.", 32, UiKit.InkDim);
            UiKit.Size(info, -1, 100);

            var l1 = UiKit.Label(v.transform, "Difficulty", 40, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(l1, -1, 56);
            var dr = UiKit.HStack(v.transform, "Diff", 12);
            dr.childForceExpandWidth = false;
            UiKit.Size(dr, -1, 96);
            var minus = UiKit.Btn(dr.transform, "-", () => SetDiff(difficulty - 1), UiKit.Secondary, 50);
            UiKit.Size(minus, 116, 96);
            diffLbl = UiKit.Label(dr.transform, "", 54, UiKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(diffLbl, -1, 96, 1);
            var plus = UiKit.Btn(dr.transform, "+", () => SetDiff(difficulty + 1), UiKit.Secondary, 50);
            UiKit.Size(plus, 116, 96);

            var l2 = UiKit.Label(v.transform, "Board code", 40, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(l2, -1, 56);
            var cr = UiKit.HStack(v.transform, "Code", 12);
            cr.childForceExpandWidth = false;
            UiKit.Size(cr, -1, 96);
            codeInput = Chrome.Input(cr.transform, "Code", InputField.ContentType.Alphanumeric, 8);
            UiKit.Size(codeInput, -1, 82, 1);
            var nb = UiKit.Btn(cr.transform, "Random", () => { code = NewCode(); codeInput.text = code; }, UiKit.Secondary, 32);
            UiKit.Size(nb, 200, 96);
            codeLbl = UiKit.Label(v.transform, "", 28, UiKit.InkDim);
            UiKit.Size(codeLbl, -1, 40);

            var sp = UiKit.NewRect("Spacer", v.transform); UiKit.Size(sp, -1, 10, -1, 1);
            var start = UiKit.Btn(v.transform, "Start", Start, UiKit.Primary, 44);
            UiKit.Size(start, -1, 110);
        }

        static string NewCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 6; i++) sb.Append(chars[UnityEngine.Random.Range(0, chars.Length)]);
            return sb.ToString();
        }

        void SetDiff(int d) { difficulty = Mathf.Clamp(d, 1, 10); diffLbl.text = difficulty + " of 10"; }

        public override void OnShow()
        {
            code = NewCode();
            codeInput.text = code;
            SetDiff(difficulty);
            codeLbl.text = "Same code and difficulty always give the same board.";
        }

        public override bool OnBack() { app.Show("home"); return true; }

        public override void OnLayout()
        {
            if (app == null || body == null) return;
            Chrome.FitWidth(body, Root, 760);
        }

        void Start()
        {
            string c = (codeInput.text ?? "").Trim().ToUpperInvariant();
            if (c.Length == 0) c = NewCode();
            ulong seed = Mix.Fnv1a64("FREE:" + c + ":" + difficulty);
            app.StartFree(seed, difficulty);
        }
    }
}
