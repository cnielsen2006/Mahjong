using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Mahjong.Core;

namespace Mahjong.Game
{
    /// <summary>The play screen: HUD, controls and the pause / win / stuck panels around the board.</summary>
    public sealed class GameScreen : ScreenBase
    {
        // The top bar holds the menu, title, tile count and the Hint / Undo / Shuffle buttons. Wide screens fit them in one
        // row; narrow ones (portrait, large text) put the buttons on a second row. Either way the board gets the rest.
        const float OneRowH = 120f, TwoRowH = 190f, TwoRowBelow = 1100f;

        RectTransform top, controls, pipRect, banner, comboRoot, stuckCard, comboBar, comboFill;
        Image comboFillImg;
        Text comboBarLbl;
        float topH = OneRowH;
        Text titleLbl, remainLbl, bannerLbl, comboLbl, debugLbl, stuckLbl;
        Image[] pips = new Image[5];
        Button menuBtn, hintBtn, undoBtn, shuffleBtn;
        readonly Button[] ctl = new Button[3];
        GameObject pausePanel, winPanel, confirmPanel;
        Slider tiltSlider;
        Text tiltVal;
        RectTransform winCard;

        LevelRequest req;
        Level level;
        public GameSession Session { get; private set; }
        BoardView board;
        float elapsed;
        bool paused, won, dirty;
        float lastProgress, idleSince, comboUntil, comboShownAt, comboPop, autoNextAt = -1f;
        int bannerLevel;
        float bannerUntil;
        const float BannerSeconds = 6f;

        static readonly string[] Tips =
        {
            null,
            "Tap two matching tiles to remove them. A tile is free when nothing sits on top and its left or right side is open.",
            "Not sure what to do? Tap Hint to see a pair you can remove right now.",
            "A tile with neighbours on both sides is blocked. Clear tiles from the outside in.",
            "Made a wrong move? Undo takes it back, as many times as you like.",
            "No more moves? Shuffle re-deals the remaining tiles so the board can still be cleared.",
        };

        public override void Build(App a, Transform parent)
        {
            base.Build(a, parent);
            board = a.Board;
            board.OnWon = HandleWon;
            board.OnStuck = HandleStuck;
            board.OnMatched = HandleMatched;
            board.OnChanged = () => { dirty = true; };
            board.OnFirstInput = () => { idleSince = Time.unscaledTime; };

            // ---- top bar
            top = UiKit.NewRect("Top", Root);
            UiKit.Anchor(top, 0, 1, 1, 1);
            top.pivot = new Vector2(0.5f, 1f);
            var tbg = UiKit.Img(top, "Bg", new Color(0f, 0f, 0f, 0.28f));
            UiKit.Stretch(tbg.rectTransform);

            menuBtn = UiKit.Btn(top, "", OpenPause, UiKit.Secondary, 30, "Menu");
            for (int i = 0; i < 3; i++)
            {
                var bar = UiKit.Img(UiKit.Face(menuBtn), "bar", UiKit.Ink);
                UiKit.Fixed(bar.rectTransform, 0.5f, 0.5f, 40, 6, 0, (1 - i) * 14);
                bar.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            }

            titleLbl = UiKit.Label(top, "Level 1", 46, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            var pipRow = UiKit.HStack(top, "Pips", 10, TextAnchor.MiddleCenter, false);
            pipRow.childForceExpandWidth = false;
            pipRect = (RectTransform)pipRow.transform;
            for (int i = 0; i < 5; i++)
            {
                pips[i] = UiKit.Img(pipRow.transform, "pip", Color.white, TileArt.CircleSprite());
                UiKit.Size(pips[i], 20, 20);
            }
            remainLbl = UiKit.Label(Root, "0 tiles", 30, UiKit.InkDim, TextAnchor.LowerRight);
            UiKit.Emboss(remainLbl);

            // ---- controls (Hint / Undo / Shuffle)
            controls = UiKit.NewRect("Controls", top);
            hintBtn = UiKit.Btn(controls, "Hint", DoHint, new Color(0.20f, 0.52f, 0.72f), 32, "Hint");
            undoBtn = UiKit.Btn(controls, "Undo", DoUndo, UiKit.Secondary, 32, "Undo");
            shuffleBtn = UiKit.Btn(controls, "Shuffle", DoShuffle, new Color(0.62f, 0.42f, 0.20f), 32, "Shuffle");
            ctl[0] = hintBtn; ctl[1] = undoBtn; ctl[2] = shuffleBtn;

            // ---- tutorial banner
            banner = UiKit.NewRect("Banner", Root);
            var bb = UiKit.Round(banner, "Bg", new Color(0.05f, 0.08f, 0.1f, 0.88f), true);
            UiKit.Stretch(bb.rectTransform);
            var bbtn = bb.gameObject.AddComponent<Button>();
            bbtn.targetGraphic = bb;
            bbtn.transition = Selectable.Transition.None;
            bbtn.onClick.AddListener(HideBanner);
            bannerLbl = UiKit.Label(banner, "", 30, UiKit.Ink, TextAnchor.MiddleCenter);
            UiKit.Stretch(bannerLbl.rectTransform);
            bannerLbl.rectTransform.offsetMin = new Vector2(24, 8); bannerLbl.rectTransform.offsetMax = new Vector2(-24, -8);
            banner.gameObject.SetActive(false);

            // ---- combo flourish
            comboRoot = UiKit.NewRect("Combo", Root);
            UiKit.Fixed(comboRoot, 0.5f, 1f, 600, 90, 0, -topH - 8);
            comboRoot.pivot = new Vector2(0.5f, 1f);
            comboLbl = UiKit.Label(comboRoot, "", 58, UiKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(comboLbl.rectTransform);
            comboRoot.gameObject.SetActive(false);

            // ---- combo timer: a bar down the side that drains while the chain is alive
            comboBar = UiKit.Round(Root, "ComboBar", new Color(0f, 0f, 0f, 0.35f)).rectTransform;
            comboFill = UiKit.Round(comboBar, "Fill", UiKit.Gold).rectTransform;
            comboFillImg = comboFill.GetComponent<Image>();
            comboBarLbl = UiKit.Label(Root, "", 30, UiKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Emboss(comboBarLbl);
            comboBar.gameObject.SetActive(false);
            comboBarLbl.gameObject.SetActive(false);

            debugLbl =UiKit.Label(Root, "", 24, new Color(1, 1, 1, 0.75f), TextAnchor.LowerLeft);
            UiKit.Anchor(debugLbl.rectTransform, 0, 0, 0.7f, 0.12f, 12, 6, 0, 0);
            debugLbl.resizeTextForBestFit = false;

            BuildStuckCard();
            BuildPause();
            BuildConfirm();
            winPanel = Modal("WinPanel", out winCard, 680, 760);
            winPanel.SetActive(false);
        }

        // ---------------------------------------------------------------- panel builders

        GameObject Modal(string name, out RectTransform card, float w, float h)
        {
            var root = UiKit.NewRect(name, Root);
            UiKit.Stretch(root);
            var scrim = UiKit.Img(root, "Scrim", UiKit.Scrim, null, true);
            UiKit.Stretch(scrim.rectTransform);
            card = UiKit.Round(root, "Card", UiKit.Card).rectTransform;
            card.GetComponent<Image>().raycastTarget = true;
            UiKit.Fixed(card, 0.5f, 0.5f, w, h, 0, 0);
            card.pivot = new Vector2(0.5f, 0.5f);
            return root.gameObject;
        }

        void BuildStuckCard()
        {
            stuckCard = UiKit.Round(Root, "Stuck", UiKit.Card, true).rectTransform;
            UiKit.Fixed(stuckCard, 0.5f, 1f, 760, 230, 0, -topH - 20);
            stuckCard.pivot = new Vector2(0.5f, 1f);
            stuckLbl = UiKit.Label(stuckCard, "No more moves", 44, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(stuckLbl.rectTransform, 0, 0.55f, 1, 1, 10, 0, -10, -10);
            var row = UiKit.HStack(stuckCard, "Row", 14);
            UiKit.Anchor((RectTransform)row.transform, 0, 0, 1, 0.55f, 18, 16, -18, 0);
            UiKit.Btn(row.transform, "Shuffle", DoShuffle, new Color(0.62f, 0.42f, 0.20f), 34);
            UiKit.Btn(row.transform, "Undo", DoUndo, UiKit.Secondary, 34);
            UiKit.Btn(row.transform, "Restart", () => AskRestart(), UiKit.Danger, 34);
            stuckCard.gameObject.SetActive(false);
        }

        void BuildPause()
        {
            RectTransform card;
            pausePanel = Modal("PausePanel", out card, 560, 740);
            var title = UiKit.Label(card, "Paused", 60, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, 0, 0.8f, 1, 1, 0, 0, 0, -20);
            var v = UiKit.VStack(card, "Buttons", 10, TextAnchor.UpperCenter);
            UiKit.Anchor((RectTransform)v.transform, 0, 0, 1, 0.8f, 56, 36, -56, 0);
            UiKit.Size(UiKit.Btn(v.transform, "Resume", ClosePause, UiKit.Primary, 40), -1, 100);
            UiKit.Size(UiKit.Btn(v.transform, "Restart level", () => AskRestart(), UiKit.Secondary, 34), -1, 88);
            UiKit.Size(UiKit.Btn(v.transform, "Settings", () => { app.Get<SettingsScreen>().ReturnTo = "game"; FlushProgress(); app.Show("settings"); }, UiKit.Secondary, 34), -1, 88);
            UiKit.Size(UiKit.Btn(v.transform, "Home", GoHome, UiKit.Secondary, 34), -1, 88);

            // board tilt, adjustable live: the board stays visible behind the dimmed panel
            var tiltRow = UiKit.NewRect("TiltRow", v.transform);
            UiKit.Size(tiltRow, -1, 84);
            var tl = UiKit.Label(tiltRow, "Tilt", 34, UiKit.Ink, TextAnchor.MiddleLeft);
            UiKit.Anchor(tl.rectTransform, 0, 0, 0.24f, 1, 6, 0, 0, 0);
            tiltVal = UiKit.Label(tiltRow, "", 32, UiKit.Gold, TextAnchor.MiddleRight, FontStyle.Bold);
            UiKit.Anchor(tiltVal.rectTransform, 0.8f, 0, 1, 1, 0, 0, -4, 0);
            tiltSlider = UiKit.NewSlider(tiltRow, "Slider", 0, BoardView.MaxTilt, app.S.tilt, val =>
            {
                app.S.tilt = (int)val;
                tiltVal.text = (int)val + "°";
                app.SaveSoon();
            });
            UiKit.Anchor((RectTransform)tiltSlider.transform, 0.25f, 0, 0.8f, 1, 0, 0, -6, 0);
            pausePanel.SetActive(false);
        }

        void BuildConfirm()
        {
            RectTransform card;
            confirmPanel = Modal("ConfirmPanel", out card, 600, 320);
            var t = UiKit.Label(card, "Restart this level?", 52, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(t.rectTransform, 0, 0.5f, 1, 1, 20, 0, -20, -20);
            var row = UiKit.HStack(card, "Row", 16);
            UiKit.Anchor((RectTransform)row.transform, 0, 0, 1, 0.5f, 28, 28, -28, 0);
            UiKit.Btn(row.transform, "Keep playing", () => confirmPanel.SetActive(false), UiKit.Secondary, 34);
            UiKit.Btn(row.transform, "Restart", RestartNow, UiKit.Danger, 34);
            confirmPanel.SetActive(false);
        }

        // ---------------------------------------------------------------- lifecycle

        public override void OnShow()
        {
            OnLayout();
            if (Session != null) board.InputEnabled = !paused && !won;
        }

        public override void OnHide() { }

        public void Begin(LevelRequest r, Level l)
        {
            req = r; level = l;
            Session = new GameSession(l);
            if (r.ResumeLog != null && r.ResumeLog.Count > 0) Session.Replay(r.ResumeLog);
            elapsed = r.ResumeSeconds; won = false; paused = false; dirty = false; autoNextAt = -1f;
            idleSince = Time.unscaledTime; lastProgress = Time.unscaledTime;
            pausePanel.SetActive(false); winPanel.SetActive(false); confirmPanel.SetActive(false); stuckCard.gameObject.SetActive(false);
            comboRoot.gameObject.SetActive(false);
            comboBar.gameObject.SetActive(false); comboBarLbl.gameObject.SetActive(false);

            board.Begin(Session, app.Atlas, r.ResumeLog == null || r.ResumeLog.Count == 0);
            board.InputEnabled = true;
            bannerLevel = (r.Mode == "journey" && r.Level <= DifficultyConfig.TutorialLevels) ? r.Level : 0;
            banner.gameObject.SetActive(bannerLevel > 0);
            if (bannerLevel > 0) { bannerLbl.text = Tips[bannerLevel]; bannerUntil = Time.unscaledTime + BannerSeconds; }

            if (r.Mode == "journey") titleLbl.text = "Level " + r.Level;
            else if (r.Mode == "daily") titleLbl.text = "Daily " + DateUtil.FromInt(r.Day).ToString("MMM d");
            else titleLbl.text = "Free play";
            int leaves = l.Leaves;
            for (int i = 0; i < 5; i++) pips[i].color = i < leaves ? new Color(0.55f, 0.9f, 0.5f) : new Color(1, 1, 1, 0.22f);

            if (r.Mode == "journey") { app.Prefetch(r.Level + 1); }
            if (r.Mode == "journey" && r.Level <= DifficultyConfig.TutorialLevels && !app.Save.Data.tutorialDone && r.Level == 1)
            { /* the skip button lives on the banner via the Home screen's second chance; keep play simple */ }

            RefreshDebug();
            OnLayout();
            if (Session.IsStuck && !Session.IsWon) stuckCard.gameObject.SetActive(true);
        }

        void RefreshDebug()
        {
            if (!app.DebugMode || level == null) { debugLbl.text = ""; return; }
            debugLbl.text = level.Layout.Id + "  tiles " + level.Layout.Count + "  target " + level.Target + "  rated " + level.Rated +
                            "  tries " + level.Attempts + "  seed " + level.Seed.ToString("x") + "  " + level.Stats;
        }

        // ---------------------------------------------------------------- layout

        public override void OnLayout()
        {
            if (app == null || top == null) return;
            bool portrait = app.Portrait;
            bool left = app.S.leftHanded;

            Canvas.ForceUpdateCanvases();
            float rootW = ((RectTransform)Root).rect.width;
            bool twoRow = rootW < TwoRowBelow;
            topH = twoRow ? TwoRowH : OneRowH;
            top.offsetMin = new Vector2(0, -topH); top.offsetMax = Vector2.zero;

            // Left-handed play mirrors the bar: buttons on the left, menu and tile count on the right.
            float side = left ? 0f : 1f, other = 1f - side, dir = left ? 1f : -1f;   // dir points inwards from the buttons' edge
            var menuRt = menuBtn.GetComponent<RectTransform>();
            if (twoRow)
            {
                UiKit.Fixed(menuRt, other, 1f, 86, 90, -dir * 14, -8);
                float lm = left ? 14 : 110, rm = left ? 110 : 14;                      // clear of the menu button
                UiKit.Anchor(titleLbl.rectTransform, 0, 1, 1, 1, lm, -62, -rm, -4);
                UiKit.Anchor(pipRect, 0, 1, 1, 1, lm, -98, -rm, -66);
                UiKit.Anchor(controls, 0, 0, 1, 0, 14, 10, -14, 80);
            }
            else
            {
                const float btnW = 140f, gap = 8f, margin = 16f, groupW = btnW * 3 + gap * 2;
                UiKit.Fixed(menuRt, other, 0.5f, 86, 90, -dir * 18, -2);
                UiKit.Fixed(controls, side, 0.5f, groupW, 76, dir * margin, -2);
                float near = 116f, farEdge = margin + groupW + 10;                      // the title sits between the menu and the buttons
                float lm = left ? farEdge : near, rm = left ? near : farEdge;
                UiKit.Anchor(titleLbl.rectTransform, 0, 1, 1, 1, lm, -66, -rm, -6);
                UiKit.Anchor(pipRect, 0, 1, 1, 1, lm, -108, -rm, -70);
            }
            for (int i = 0; i < 3; i++)
                UiKit.Anchor(ctl[i].GetComponent<RectTransform>(), i / 3f, 0, (i + 1) / 3f, 1, 4, 0, -4, 0);

            // banner just below the top bar
            float bannerW = Mathf.Min(portrait ? 760f : 720f, rootW - 24f), bannerH = portrait ? 150f : 130f;
            UiKit.Fixed(banner, 0.5f, 1f, bannerW, bannerH, 0, -topH - 10);
            banner.pivot = new Vector2(0.5f, 1f);
            UiKit.Fixed(comboRoot, 0.5f, 1f, 600, 90, 0, -topH - 8);

            // viewport for the board, in screen pixels: everything below the top bar
            var sa = Screen.safeArea;
            if (sa.width <= 0) sa = new Rect(0, 0, Screen.width, Screen.height);
            float s = app.UiScale;
            // the tile count floats in the lower right corner of the screen
            UiKit.Fixed(remainLbl.rectTransform, 1f, 0f, 300, 44, -(Screen.width - sa.xMax) / s - 16, sa.yMin / s + 8);
            // the combo timer runs down the screen edge on the playing hand's side
            const float barW = 14f, barGap = 8f;
            float edge = left ? sa.xMin / s + barGap : -((Screen.width - sa.xMax) / s + barGap);
            float bx = left ? 0f : 1f;
            float barTop = topH + 70f, barBottom = sa.yMin / s + 90f;                // insets from the screen top / bottom
            comboBar.anchorMin = new Vector2(bx, 0f); comboBar.anchorMax = new Vector2(bx, 1f);
            comboBar.pivot = new Vector2(bx, 0.5f);
            comboBar.sizeDelta = new Vector2(barW, -(barTop + barBottom));
            comboBar.anchoredPosition = new Vector2(edge, (barBottom - barTop) * 0.5f);
            var lr = comboBarLbl.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(bx, 1f);
            lr.pivot = new Vector2(0.5f, 1f);
            lr.sizeDelta = new Vector2(70, 40);
            lr.anchoredPosition = new Vector2(edge + (left ? 1f : -1f) * barW * 0.5f, -(topH + 24f));
            float pad = 10f * s;
            float yMax = sa.yMax - topH * s - pad;
            float yMin = sa.yMin + pad, xMin = sa.xMin + pad, xMax = sa.xMax - pad;
            if (banner.gameObject.activeSelf) yMax -= (bannerH + 10) * s;
            board.ViewportPx = new Rect(xMin, yMin, Mathf.Max(100, xMax - xMin), Mathf.Max(100, yMax - yMin));
            stuckCard.anchoredPosition = new Vector2(0, -topH - 20 - (banner.gameObject.activeSelf ? 160 : 0));
        }

        /// <summary>Drains the side bar as the time left to keep the chain alive runs out.</summary>
        void UpdateComboBar()
        {
            float f = won || !app.S.showComboTimer ? 0f : board.ComboLeft01;
            bool show = f > 0f;
            if (comboBar.gameObject.activeSelf != show) { comboBar.gameObject.SetActive(show); comboBarLbl.gameObject.SetActive(show); }
            if (!show) return;
            comboFill.anchorMin = Vector2.zero; comboFill.anchorMax = new Vector2(1f, f);
            comboFill.offsetMin = Vector2.zero; comboFill.offsetMax = Vector2.zero;
            comboFillImg.color = f > 0.5f ? UiKit.Gold : Color.Lerp(new Color(0.9f, 0.3f, 0.25f), UiKit.Gold, f * 2f);
            comboBarLbl.text = "x" + board.Combo;
        }

        void HideBanner()
        {
            if (!banner.gameObject.activeSelf) return;
            banner.gameObject.SetActive(false);
            OnLayout();
        }

        // ---------------------------------------------------------------- frame

        public override void Tick()
        {
            if (Session == null) return;
            bool modal = pausePanel.activeSelf || winPanel.activeSelf || confirmPanel.activeSelf;
            board.InputEnabled = !modal;

            if (!paused && !won && !modal) elapsed += Time.unscaledDeltaTime;
            remainLbl.text = Session.Board.Remaining + " tiles";
            undoBtn.interactable = Session.CanUndo && !won;
            hintBtn.interactable = !won; shuffleBtn.interactable = !won;

            if (comboRoot.gameObject.activeSelf)
            {
                float left = comboUntil - Time.unscaledTime;
                if (left <= 0f) comboRoot.gameObject.SetActive(false);
                else
                {
                    var c = comboLbl.color; c.a = Mathf.Clamp01(left / 0.4f); comboLbl.color = c;
                    float pop = Mathf.Clamp01(1f - (Time.unscaledTime - comboShownAt) / 0.3f);
                    float k = 1f + (app.S.reducedMotion ? 0f : comboPop * pop);
                    comboRoot.localScale = new Vector3(k, k, 1);
                }
            }

            UpdateComboBar();

            if (banner.gameObject.activeSelf && !modal && Time.unscaledTime >= bannerUntil) HideBanner();

            if (dirty && Time.unscaledTime - lastProgress > 1.0f) FlushProgress();

            // gentle nudge on the first levels: show a hint after a while with no input
            if (bannerLevel > 0 && !modal && !won && Time.unscaledTime - idleSince > 8f)
            {
                idleSince = Time.unscaledTime;
                long h = Hinter.Hint(Session.Board);
                if (h >= 0) board.ShowHint(Board.PairA(h), Board.PairB(h));
            }

            if (autoNextAt > 0f && Time.unscaledTime >= autoNextAt) { autoNextAt = -1f; NextLevel(); }

            if (!modal)
            {
                if (Input.GetKeyDown(KeyCode.H)) DoHint();
                if (Input.GetKeyDown(KeyCode.Z)) DoUndo();
                if (Input.GetKeyDown(KeyCode.S)) DoShuffle();
                if (Input.GetKeyDown(KeyCode.R)) AskRestart();
            }
            if (app.DebugMode) RefreshDebug();
        }

        public override bool OnBack()
        {
            if (confirmPanel.activeSelf) { confirmPanel.SetActive(false); return true; }
            if (winPanel.activeSelf) { GoHome(); return true; }
            if (pausePanel.activeSelf) { ClosePause(); return true; }
            OpenPause();
            return true;
        }

        // ---------------------------------------------------------------- actions

        void OpenPause()
        {
            if (won) return;
            pausePanel.SetActive(true); paused = true;
            stuckCard.gameObject.SetActive(false);
            tiltSlider.SetValueWithoutNotify(app.S.tilt);      // the shortcut keys may have moved it
            tiltVal.text = app.S.tilt + "°";
        }

        void ClosePause() { pausePanel.SetActive(false); paused = false; if (Session.IsStuck) stuckCard.gameObject.SetActive(true); }

        void GoHome()
        {
            autoNextAt = -1f;
            FlushProgress();
            pausePanel.SetActive(false); winPanel.SetActive(false);
            if (req != null && req.Mode == "daily" && won) app.Show("daily"); else app.Show("home");
        }

        void DoHint()
        {
            if (won || Session == null) return;
            idleSince = Time.unscaledTime;
            int a, c;
            if (!board.DoHint(out a, out c)) { app.Toast("No moves left. Try Undo or Shuffle."); return; }
            stuckCard.gameObject.SetActive(false);
        }

        void DoUndo()
        {
            if (won || Session == null) return;
            if (board.DoUndo()) stuckCard.gameObject.SetActive(false);
        }

        void DoShuffle()
        {
            if (won || Session == null) return;
            board.DoShuffle();
            stuckCard.gameObject.SetActive(false);
            pausePanel.SetActive(false); paused = false;
        }

        void AskRestart()
        {
            if (Session == null) return;
            if (app.S.confirmRestart && Session.Pairs > 0 && !won) { confirmPanel.SetActive(true); return; }
            RestartNow();
        }

        void RestartNow()
        {
            confirmPanel.SetActive(false); pausePanel.SetActive(false); winPanel.SetActive(false);
            app.Save.Data.inProgress = null;
            if (req.Mode == "journey") app.StartJourney(req.Level, false);
            else if (req.Mode == "daily") app.StartDaily(req.Day, false);
            else app.StartFree(req.Seed, req.Target);
        }

        void NextLevel()
        {
            winPanel.SetActive(false);
            if (req.Mode == "journey") app.StartJourney(req.Level + 1, false);
            else if (req.Mode == "free") app.StartFree(Mix.Next(req.Seed ^ 0x9E3779B97F4A7C15UL), req.Target);
            else GoHome();
        }

        public void FlushProgress()
        {
            dirty = false; lastProgress = Time.unscaledTime;
            if (Session == null || won || req == null || req.Mode == "free") return;
            if (Session.Pairs == 0 && Session.Log.Count == 0) return;
            var ip = new InProgress
            {
                mode = req.Mode, level = req.Level, day = req.Day, offset = app.S.difficultyOffset,
                gen = DifficultyConfig.GeneratorVersion, seconds = (int)elapsed, log = new List<int>(Session.Log)
            };
            app.Save.Data.inProgress = ip;
            app.Save.Save();
        }

        // ---------------------------------------------------------------- board events

        void HandleMatched(int a, int b, int combo)
        {
            idleSince = Time.unscaledTime;
            if (combo >= 2)
            {
                // the flourish grows with the chain: bigger pop, longer stay and a new colour at each milestone
                string word; Color col; float hold;
                if (combo >= 20) { word = "Legendary! x"; col = new Color(0.55f, 0.85f, 1f); comboPop = 0.45f; hold = 1.6f; }
                else if (combo >= 15) { word = "Incredible! x"; col = new Color(1f, 0.5f, 0.7f); comboPop = 0.40f; hold = 1.5f; }
                else if (combo >= 10) { word = "Blazing! x"; col = new Color(1f, 0.6f, 0.25f); comboPop = 0.35f; hold = 1.4f; }
                else if (combo >= 5) { word = "Combo x"; col = UiKit.Gold; comboPop = 0.25f; hold = 1.2f; }
                else { word = "Combo x"; col = UiKit.Gold; comboPop = 0.15f; hold = 1.1f; }
                comboLbl.text = word + combo;
                comboLbl.color = col;
                comboRoot.gameObject.SetActive(true);
                comboShownAt = Time.unscaledTime;
                comboUntil = comboShownAt + hold;
            }
            stuckCard.gameObject.SetActive(false);
        }

        void HandleStuck()
        {
            if (won || pausePanel.activeSelf) return;
            stuckCard.gameObject.SetActive(true);
        }

        void HandleWon()
        {
            if (won) return;
            won = true;
            stuckCard.gameObject.SetActive(false);
            int secs = Mathf.Max(1, (int)elapsed);
            int stars = Session.Stars();
            List<AchievementDef> fresh = null;
            bool gold = Session.Shuffles == 0;
            if (req.Mode == "journey")
                fresh = app.Save.RecordJourneyWin(req.Level, stars, secs, Session.Shuffles == 0, Session.Pairs, board.BestCombo);
            else if (req.Mode == "daily")
                fresh = app.Save.RecordDailyWin(req.Day, stars, secs, gold, Session.Pairs, board.BestCombo);
            app.Audio.Play("win");
            BuildWinCard(stars, secs);
            winPanel.SetActive(true);
            if (fresh != null) foreach (var a in fresh) app.Toast("Achievement: " + a.Title);
            if (req.Mode == "journey" && app.S.autoContinue) autoNextAt = Time.unscaledTime + 3.5f;
        }

        static string Time2(int secs) { return (secs / 60) + ":" + (secs % 60).ToString("00"); }

        void BuildWinCard(int stars, int secs)
        {
            for (int i = winCard.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(winCard.GetChild(i).gameObject);
            bool daily = req.Mode == "daily";
            var v = UiKit.VStack(winCard, "V", 16, TextAnchor.UpperCenter, 36);
            UiKit.Stretch((RectTransform)v.transform);
            var t = UiKit.Label(v.transform, daily ? "Daily complete!" : "Level cleared!", 62, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(t, -1, 90);

            var row = UiKit.HStack(v.transform, "Stars", 18, TextAnchor.MiddleCenter);
            row.childForceExpandWidth = false;
            UiKit.Size(row, -1, 130);
            var starImgs = new List<Image>();
            for (int i = 0; i < 3; i++)
            {
                var s = UiKit.Img(row.transform, "Star", i < stars ? UiKit.Gold : new Color(1, 1, 1, 0.18f), TileArt.StarSprite());
                UiKit.Size(s, 112, 112);
                starImgs.Add(s);
                s.transform.localScale = Vector3.zero;
            }
            app.StartCoroutine(PopStars(starImgs, stars));

            string info = "Time " + Time2(secs) + "   ·   " + Session.Pairs + " pairs";
            if (board.BestCombo >= 2) info += "   ·   Best combo x" + board.BestCombo;
            var il = UiKit.Label(v.transform, info, 32, UiKit.InkDim);
            UiKit.Size(il, -1, 50);
            if (daily)
            {
                var sl = UiKit.Label(v.transform, "Streak: " + app.Save.CurrentStreak() + (app.Save.CurrentStreak() == 1 ? " day" : " days"), 36, UiKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Size(sl, -1, 56);
            }
            else if (req.Mode == "journey")
            {
                var rec = app.Save.GetLevel(req.Level);
                if (rec != null && rec.stars > stars)
                {
                    var bl = UiKit.Label(v.transform, "Your best here is " + rec.stars + (rec.stars == 1 ? " star" : " stars"), 30, UiKit.InkDim);
                    UiKit.Size(bl, -1, 44);
                }
            }
            var spacer = UiKit.NewRect("Spacer", v.transform);
            UiKit.Size(spacer, -1, 4, -1, 1);

            if (daily)
            {
                UiKit.Size(UiKit.Btn(v.transform, "Back to calendar", GoHome, UiKit.Primary, 38), -1, 98);
                UiKit.Size(UiKit.Btn(v.transform, "Play again", () => { winPanel.SetActive(false); app.StartDaily(req.Day, false); }, UiKit.Secondary, 34), -1, 86);
            }
            else
            {
                UiKit.Size(UiKit.Btn(v.transform, req.Mode == "free" ? "New board" : "Next level", () => { autoNextAt = -1f; NextLevel(); }, UiKit.Primary, 40), -1, 102);
                var hr = UiKit.HStack(v.transform, "Row", 14);
                UiKit.Size(hr, -1, 86, -1, 0);
                UiKit.Btn(hr.transform, "Replay", () => { autoNextAt = -1f; RestartNow(); }, UiKit.Secondary, 34);
                UiKit.Btn(hr.transform, "Home", GoHome, UiKit.Secondary, 34);
            }
        }

        IEnumerator PopStars(List<Image> imgs, int count)
        {
            for (int i = 0; i < imgs.Count; i++)
            {
                if (imgs[i] == null) yield break;
                if (i >= count) { imgs[i].transform.localScale = Vector3.one; continue; }
                float t = 0f;
                if (!app.S.reducedMotion) yield return new WaitForSecondsRealtime(0.14f);
                while (t < 1f)
                {
                    if (imgs[i] == null) yield break;
                    t += Time.unscaledDeltaTime / (app.S.reducedMotion ? 0.05f : 0.22f);
                    float k = Mathf.Clamp01(t);
                    float s = 1f + 1.2f * Mathf.Pow(k - 1f, 3f) + 0.9f * Mathf.Pow(k - 1f, 2f);
                    imgs[i].transform.localScale = Vector3.one * s;
                    yield return null;
                }
                imgs[i].transform.localScale = Vector3.one;
                app.Audio.Play("select", 0.5f, 1f + i * 0.25f);
            }
        }
    }
}
