using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Mahjong.Core;

namespace Mahjong.Game
{
    /// <summary>Renders a GameSession's board with sprites, handles taps, animations and the camera fit.</summary>
    public sealed class BoardView : MonoBehaviour
    {
        public const float CellW = 1f, CellH = 1.3f, LayerDx = 0.0625f, LayerDy = 0.075f;

        sealed class T
        {
            public Transform tr;
            public SpriteRenderer body, over;
            public Vector3 basePos;
            public int z;
            public bool visible;
            public int shownKind = -1;
            public float spawnDelay, spawnT = 1f, removeT = -1f, popT = -1f, flipT = -1f, shakeT = -1f, hintT = -1f, flashT = -1f;
            public Vector3 removeTo;
            public bool selected, free, assist;
            public bool flipSwapped;
        }

        struct Puff { public Transform tr; public SpriteRenderer sr; public Vector2 vel; public float t, life; public bool live; }

        public GameSession Session { get; private set; }
        public SettingsData Settings;
        public Sfx Audio;
        public Camera Cam;
        public Rect ViewportPx;                     // area of the screen the board may use (pixels)

        public Action<int, int, int> OnMatched;      // a, b, combo
        public Action OnWon;
        public Action OnStuck;
        public Action OnBlockedTap;
        public Action OnChanged;                    // any state change (undo, shuffle, match)
        public Action OnFirstInput;

        T[] tiles;
        int[] zOrder;
        TileAtlas atlas;
        Transform root;
        readonly List<Puff> puffs = new List<Puff>();
        Sprite circle;

        int selected = -1;
        float lastMatchTime = -10f;
        int combo;
        public int BestCombo { get; private set; }
        float stuckCheckAt = -1f;
        float winAt = -1f;
        bool ended;
        public bool InputEnabled = true;

        // camera state
        Vector2 worldMin, worldMax;
        float zoom = 1f;
        Vector2 pan;                                 // world units
        Vector2 lastScreen;
        Rect lastViewport;
        float basePpu = 1f;
        bool dragging, pressed, pressOverUi;
        Vector2 pressPos, lastPointer;
        float lastPinch;
        public bool CanPan { get; private set; }

        public bool IsBusy
        {
            get
            {
                if (tiles == null) return false;
                for (int i = 0; i < tiles.Length; i++) if (tiles[i].removeT >= 0f && tiles[i].removeT < 1f) return true;
                return false;
            }
        }

        void Awake()
        {
            root = new GameObject("Tiles").transform;
            root.SetParent(transform, false);
            circle = TileArt.CircleSprite();
        }

        // ------------------------------------------------------------------ setup

        public void Begin(GameSession session, TileAtlas tileAtlas, bool animateIn)
        {
            Clear();
            Session = session; atlas = tileAtlas;
            var L = session.Level.Layout;
            int n = L.Count;
            tiles = new T[n];
            zOrder = new int[n];
            var minV = new Vector2(float.MaxValue, float.MaxValue); var maxV = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < n; i++)
            {
                var p = L.Pos[i];
                var t = new T { z = p.Z };
                var go = new GameObject("t" + i);
                go.transform.SetParent(root, false);
                t.tr = go.transform;
                t.body = go.AddComponent<SpriteRenderer>();
                t.body.sortingOrder = p.Z * 10;
                var ov = new GameObject("o");
                ov.transform.SetParent(go.transform, false);
                t.over = ov.AddComponent<SpriteRenderer>();
                t.over.sprite = atlas.Outline;
                t.over.sortingOrder = p.Z * 10 + 1;
                t.over.enabled = false;
                t.basePos = WorldPos(p);
                t.visible = session.Board.Present[i];
                t.shownKind = session.Board.KindAt[i];
                t.body.sprite = atlas.Faces[t.shownKind];
                t.body.enabled = t.visible;
                tiles[i] = t;
                zOrder[i] = i;
                minV = Vector2.Min(minV, new Vector2(t.basePos.x - CellW * 0.5f, t.basePos.y - CellH * 0.5f));
                maxV = Vector2.Max(maxV, new Vector2(t.basePos.x + CellW * 0.5f, t.basePos.y + CellH * 0.5f));
            }
            Array.Sort(zOrder, (a, b) => tiles[b].z.CompareTo(tiles[a].z));
            worldMin = minV; worldMax = maxV;
            selected = -1; combo = 0; BestCombo = 0; ended = false; stuckCheckAt = -1f; winAt = -1f;
            zoom = 1f; pan = Vector2.zero;
            if (animateIn && !Settings.reducedMotion)
            {
                for (int i = 0; i < n; i++)
                {
                    var p = L.Pos[i];
                    tiles[i].spawnT = 0f;
                    tiles[i].spawnDelay = p.Z * 0.09f + (p.Y * 0.5f) * 0.012f + (p.X * 0.5f) * 0.004f;
                }
            }
            RefreshFree();
            lastViewport = new Rect(-1, -1, 0, 0);
            ApplyCamera(true);
            ApplyTints();
        }

        public void Clear()
        {
            if (tiles != null)
                for (int i = 0; i < tiles.Length; i++) if (tiles[i].tr != null) Destroy(tiles[i].tr.gameObject);
            tiles = null; Session = null;
        }

        public static Vector3 WorldPos(TilePos p)
        {
            return new Vector3(p.X * 0.5f * CellW + CellW * 0.5f + p.Z * LayerDx,
                               -(p.Y * 0.5f * CellH + CellH * 0.5f) + p.Z * LayerDy, 0f);
        }

        public void SetAtlas(TileAtlas newAtlas)
        {
            atlas = newAtlas;
            if (tiles == null) return;
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i].body.sprite = atlas.Faces[tiles[i].shownKind];
                tiles[i].over.sprite = atlas.Outline;
            }
        }

        // ------------------------------------------------------------------ actions

        public void TapTile(int pos)
        {
            if (tiles == null || ended || !InputEnabled) return;
            if (OnFirstInput != null) OnFirstInput();
            var b = Session.Board;
            if (pos < 0 || !tiles[pos].visible) { Deselect(); return; }
            if (!b.IsFree(pos))
            {
                Nudge(pos);
                if (Audio != null) Audio.Play("nope");
                if (OnBlockedTap != null) OnBlockedTap();
                return;
            }
            if (selected < 0)
            {
                Select(pos);
                if (Audio != null) Audio.Play("select");
                return;
            }
            if (selected == pos) { Deselect(); if (Audio != null) Audio.Play("click", 0.6f); return; }
            if (Kinds.Match(b.KindAt[selected], b.KindAt[pos]))
            {
                int a = selected; Deselect();
                DoMatch(a, pos);
            }
            else
            {
                Deselect(); Select(pos);
                if (Audio != null) Audio.Play("select");
            }
        }

        void DoMatch(int a, int c)
        {
            if (!Session.Match(a, c)) return;
            float now = Time.unscaledTime;
            combo = (now - lastMatchTime < 3f) ? combo + 1 : 1;
            lastMatchTime = now;
            if (combo > BestCombo) BestCombo = combo;
            bool reduce = Settings.reducedMotion;
            Vector3 mid = (tiles[a].basePos + tiles[c].basePos) * 0.5f;
            foreach (int id in new[] { a, c })
            {
                var t = tiles[id];
                t.removeT = 0f; t.removeTo = mid; t.selected = false; t.hintT = -1f;
                t.body.sortingOrder = t.z * 10 + 300;
                t.over.sortingOrder = t.z * 10 + 301;
                if (!reduce) SpawnPuffs(t.basePos, 5);
            }
            RefreshFree();
            ClearAssist();
            if (Audio != null) Audio.Play("match", 0.9f, Mathf.Min(1.6f, 1f + 0.06f * (combo - 1)));
            if (OnMatched != null) OnMatched(a, c, combo);
            if (OnChanged != null) OnChanged();
            if (Session.IsWon) { winAt = Time.unscaledTime + (reduce ? 0.2f : 0.55f); ended = true; }
            else stuckCheckAt = Time.unscaledTime + 0.7f;
        }

        public bool DoUndo()
        {
            if (Session == null || ended) return false;
            if (!Session.Undo()) return false;
            Deselect();
            Sync(true);
            if (Audio != null) Audio.Play("undo");
            stuckCheckAt = -1f;
            if (OnChanged != null) OnChanged();
            return true;
        }

        public bool DoShuffle()
        {
            if (Session == null || ended) return false;
            Deselect();
            Session.Shuffle();
            Sync(true);
            if (Audio != null) Audio.Play("shuffle");
            stuckCheckAt = Time.unscaledTime + 0.9f;
            if (OnChanged != null) OnChanged();
            return true;
        }

        public bool DoHint(out int a, out int c)
        {
            a = c = -1;
            if (Session == null || ended) return false;
            long h = Session.Hint();
            if (h < 0) return false;
            a = Board.PairA(h); c = Board.PairB(h);
            ShowHint(a, c);
            if (Audio != null) Audio.Play("hint");
            if (OnChanged != null) OnChanged();
            return true;
        }

        public void ShowHint(int a, int c)
        {
            if (tiles == null) return;
            tiles[a].hintT = 0f; tiles[c].hintT = 0f;
        }

        /// <summary>Brings the sprites in line with the session board (after undo, shuffle or a resume).</summary>
        public void Sync(bool animate)
        {
            if (tiles == null) return;
            var b = Session.Board;
            bool reduce = Settings.reducedMotion;
            for (int i = 0; i < tiles.Length; i++)
            {
                var t = tiles[i];
                bool present = b.Present[i];
                if (present && !t.visible)
                {
                    t.visible = true; t.body.enabled = true; t.removeT = -1f;
                    t.body.sortingOrder = t.z * 10; t.over.sortingOrder = t.z * 10 + 1;
                    t.popT = animate && !reduce ? 0f : -1f;
                    if (animate && reduce) t.popT = 0.8f;
                }
                else if (!present && t.visible)
                {
                    t.visible = false; t.body.enabled = false; t.over.enabled = false; t.removeT = -1f;
                }
                if (present && t.shownKind != b.KindAt[i])
                {
                    t.shownKind = b.KindAt[i];
                    if (animate && !reduce) { t.flipT = 0f; t.flipSwapped = false; }
                    else t.body.sprite = atlas.Faces[t.shownKind];
                }
                t.hintT = -1f;
            }
            RefreshFree();
        }

        void Select(int pos)
        {
            selected = pos;
            tiles[pos].selected = true;
            if (Settings.matchAssist) UpdateAssist(pos);
        }

        void Deselect()
        {
            if (selected >= 0 && tiles != null) tiles[selected].selected = false;
            selected = -1;
            ClearAssist();
        }

        void UpdateAssist(int pos)
        {
            ClearAssist();
            var b = Session.Board;
            int found = -1, count = 0;
            for (int i = 0; i < tiles.Length; i++)
                if (i != pos && tiles[i].visible && b.IsFree(i) && Kinds.Match(b.KindAt[i], b.KindAt[pos])) { found = i; count++; }
            if (count == 1) tiles[found].assist = true;
        }

        void ClearAssist()
        {
            if (tiles == null) return;
            for (int i = 0; i < tiles.Length; i++) tiles[i].assist = false;
        }

        void Nudge(int pos)
        {
            var t = tiles[pos];
            if (!Settings.reducedMotion) t.shakeT = 0f;
            if (Settings.showBlockers)
            {
                var L = Session.Level.Layout;
                var b = Session.Board;
                FlashList(L.Above[pos], b);
                if (!b.IsCovered(pos))
                {
                    FlashList(L.Left[pos], b);
                    FlashList(L.Right[pos], b);
                }
            }
        }

        void FlashList(int[] ids, Board b)
        {
            for (int i = 0; i < ids.Length; i++) if (b.Present[ids[i]]) tiles[ids[i]].flashT = 0f;
        }

        void RefreshFree()
        {
            var b = Session.Board;
            for (int i = 0; i < tiles.Length; i++) tiles[i].free = tiles[i].visible && b.IsFree(i);
        }

        // ------------------------------------------------------------------ puffs

        void SpawnPuffs(Vector3 at, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Puff p = default(Puff);
                int idx = -1;
                for (int k = 0; k < puffs.Count; k++) if (!puffs[k].live) { idx = k; break; }
                if (idx < 0)
                {
                    var go = new GameObject("puff");
                    go.transform.SetParent(transform, false);
                    p.tr = go.transform; p.sr = go.AddComponent<SpriteRenderer>(); p.sr.sprite = circle;
                    p.sr.sortingOrder = 900;
                    puffs.Add(p); idx = puffs.Count - 1;
                }
                p = puffs[idx];
                float ang = UnityEngine.Random.value * Mathf.PI * 2f;
                float sp = 1.2f + UnityEngine.Random.value * 1.8f;
                p.vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * sp;
                p.t = 0f; p.life = 0.45f + UnityEngine.Random.value * 0.2f; p.live = true;
                p.tr.position = at + new Vector3(UnityEngine.Random.value * 0.4f - 0.2f, UnityEngine.Random.value * 0.4f - 0.2f, 0);
                p.sr.enabled = true;
                p.sr.color = new Color(1f, 0.95f, 0.7f, 0.9f);
                puffs[idx] = p;
            }
        }

        void UpdatePuffs(float dt)
        {
            for (int i = 0; i < puffs.Count; i++)
            {
                var p = puffs[i];
                if (!p.live) continue;
                p.t += dt;
                if (p.t >= p.life) { p.live = false; p.sr.enabled = false; puffs[i] = p; continue; }
                float k = p.t / p.life;
                p.tr.position += (Vector3)(p.vel * dt);
                p.vel *= (1f - 2.2f * dt);
                float s = Mathf.Lerp(0.16f, 0.04f, k);
                p.tr.localScale = new Vector3(s, s, 1f);
                var c = p.sr.color; c.a = 0.9f * (1f - k); p.sr.color = c;
                puffs[i] = p;
            }
        }

        // ------------------------------------------------------------------ per frame

        static float EaseOutBack(float x)
        {
            const float c1 = 1.5f, c3 = c1 + 1f;
            x = Mathf.Clamp01(x) - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        void Update()
        {
            if (tiles == null) return;
            float dt = Time.unscaledDeltaTime;
            HandleInput();
            Animate(dt);
            UpdatePuffs(dt);
            ApplyCamera(false);

            if (winAt > 0f && Time.unscaledTime >= winAt)
            {
                winAt = -1f;
                if (OnWon != null) OnWon();
            }
            if (stuckCheckAt > 0f && Time.unscaledTime >= stuckCheckAt && !IsBusy)
            {
                stuckCheckAt = -1f;
                if (!ended && Session.IsStuck)
                {
                    if (Audio != null) Audio.Play("stuck");
                    if (OnStuck != null) OnStuck();
                }
            }
        }

        void ApplyTints()
        {
            if (tiles == null) return;
            float dim = Settings.dimBlocked == 0 ? 1f : (Settings.dimBlocked == 1 ? 0.88f : 0.72f);
            for (int i = 0; i < tiles.Length; i++)
            {
                var t = tiles[i];
                float v = t.free ? 1f : dim;
                var c = t.body.color; c.r = v; c.g = v; c.b = v; t.body.color = c;
            }
        }

        void Animate(float dt)
        {
            bool reduce = Settings.reducedMotion;
            float dim = Settings.dimBlocked == 0 ? 1f : (Settings.dimBlocked == 1 ? 0.88f : 0.72f);
            float tm = Time.unscaledTime;
            for (int i = 0; i < tiles.Length; i++)
            {
                var t = tiles[i];
                if (!t.visible && t.removeT < 0f) continue;

                Vector3 pos = t.basePos; Vector3 scale = Vector3.one; float alpha = 1f;

                if (t.spawnT < 1f)
                {
                    if (t.spawnDelay > 0f) { t.spawnDelay -= dt; alpha = 0f; }
                    else
                    {
                        t.spawnT = Mathf.Min(1f, t.spawnT + dt / 0.32f);
                        float k = EaseOutBack(t.spawnT);
                        pos.y += (1f - k) * (2.4f + 0.4f * t.z);
                        alpha = Mathf.Clamp01(t.spawnT * 4f);
                    }
                }
                if (t.removeT >= 0f)
                {
                    t.removeT += dt / (reduce ? 0.12f : 0.3f);
                    float k = Mathf.Clamp01(t.removeT);
                    pos = Vector3.Lerp(t.basePos, t.removeTo, k * 0.75f);
                    float s = reduce ? 1f : Mathf.Lerp(1f, 0.55f, k);
                    scale = new Vector3(s, s, 1f);
                    alpha = 1f - k;
                    if (t.removeT >= 1f)
                    {
                        t.removeT = -1f; t.visible = false; t.body.enabled = false; t.over.enabled = false;
                        continue;
                    }
                }
                if (t.popT >= 0f)
                {
                    t.popT += dt / 0.28f;
                    float k = Mathf.Clamp01(t.popT);
                    float s = reduce ? 1f : Mathf.Lerp(0.5f, 1f, EaseOutBack(k));
                    scale = new Vector3(s, s, 1f);
                    alpha = Mathf.Clamp01(k * 3f);
                    if (t.popT >= 1f) t.popT = -1f;
                }
                if (t.flipT >= 0f)
                {
                    t.flipT += dt / 0.4f;
                    if (t.flipT >= 0.5f && !t.flipSwapped) { t.flipSwapped = true; t.body.sprite = atlas.Faces[t.shownKind]; }
                    float sx = Mathf.Abs(Mathf.Cos(Mathf.PI * Mathf.Clamp01(t.flipT)));
                    scale.x *= Mathf.Max(0.02f, sx);
                    if (t.flipT >= 1f) { t.flipT = -1f; t.body.sprite = atlas.Faces[t.shownKind]; }
                }
                if (t.shakeT >= 0f)
                {
                    t.shakeT += dt / 0.35f;
                    pos.x += Mathf.Sin(t.shakeT * 38f) * 0.06f * (1f - Mathf.Clamp01(t.shakeT));
                    if (t.shakeT >= 1f) t.shakeT = -1f;
                }
                if (t.selected)
                {
                    float s = reduce ? 1.04f : 1.05f + 0.01f * Mathf.Sin(tm * 7f);
                    scale *= s;
                    pos.y += 0.04f;
                }

                // overlay: flash > hint > selected > assist
                Color oc = Color.clear;
                if (t.flashT >= 0f)
                {
                    t.flashT += dt / 0.7f;
                    float a = (1f - Mathf.Clamp01(t.flashT)) * (0.55f + 0.45f * Mathf.Sin(t.flashT * 28f));
                    oc = new Color(1f, 0.3f, 0.25f, Mathf.Clamp01(a));
                    if (t.flashT >= 1f) t.flashT = -1f;
                }
                else if (t.hintT >= 0f)
                {
                    t.hintT += dt;
                    float a = reduce ? 1f : 0.55f + 0.45f * Mathf.Sin(t.hintT * 7f);
                    oc = new Color(0.2f, 0.85f, 1f, a);
                    if (t.hintT > 3.5f) t.hintT = -1f;
                }
                else if (t.selected) oc = new Color(1f, 0.82f, 0.15f, 1f);
                else if (t.assist) oc = new Color(1f, 1f, 1f, 0.55f + 0.2f * Mathf.Sin(tm * 5f));
                t.over.enabled = oc.a > 0.01f;
                if (t.over.enabled) t.over.color = oc;

                float v = (t.free || t.removeT >= 0f) ? 1f : dim;
                t.body.color = new Color(v, v, v, alpha);
                t.tr.localPosition = pos;
                t.tr.localScale = scale;
            }
        }

        // ------------------------------------------------------------------ input

        static bool OverUi(Vector2 screen)
        {
            if (EventSystem.current == null) return false;
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                    if (EventSystem.current.IsPointerOverGameObject(Input.GetTouch(i).fingerId)) return true;
                return false;
            }
            return EventSystem.current.IsPointerOverGameObject();
        }

        Vector2 PointerPos()
        {
            if (Input.touchCount > 0) return Input.GetTouch(0).position;
            return Input.mousePosition;
        }

        void HandleInput()
        {
            if (Cam == null || !InputEnabled) { pressed = false; return; }

            // zoom: wheel and pinch
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && ViewportPx.Contains(Input.mousePosition) && !OverUi(Input.mousePosition))
                SetZoom(zoom * (1f + 0.12f * wheel));
            if (Input.touchCount == 2)
            {
                float d = Vector2.Distance(Input.GetTouch(0).position, Input.GetTouch(1).position);
                if (lastPinch > 1f) SetZoom(zoom * d / lastPinch);
                lastPinch = d; pressed = false; dragging = false;
                return;
            }
            lastPinch = 0f;

            bool down = Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
            bool up = Input.GetMouseButtonUp(0) || (Input.touchCount > 0 && (Input.GetTouch(0).phase == TouchPhase.Ended || Input.GetTouch(0).phase == TouchPhase.Canceled));
            Vector2 p = PointerPos();

            if (down)
            {
                pressed = true; dragging = false; pressPos = p; lastPointer = p;
                pressOverUi = OverUi(p);
            }
            else if (pressed && !pressOverUi)
            {
                float thr = Mathf.Max(10f, Mathf.Min(Screen.width, Screen.height) * 0.018f);
                if (!dragging && (p - pressPos).magnitude > thr && CanPan) dragging = true;
                if (dragging)
                {
                    float ppu = basePpu * zoom;
                    pan -= (p - lastPointer) / ppu;     // dragging right moves the board right = camera left
                    pan = ClampPan(pan);
                    lastPointer = p;
                }
            }
            if (up && pressed)
            {
                if (!dragging && !pressOverUi && (p - pressPos).magnitude < Mathf.Max(24f, Screen.height * 0.03f))
                {
                    var w = Cam.ScreenToWorldPoint(new Vector3(p.x, p.y, -Cam.transform.position.z));
                    TapTile(HitTest(w));
                }
                pressed = false; dragging = false;
            }
            if (Input.GetMouseButtonDown(1)) { Deselect(); }
        }

        public int HitTest(Vector3 w)
        {
            for (int k = 0; k < zOrder.Length; k++)
            {
                int i = zOrder[k];
                var t = tiles[i];
                if (!t.visible || t.removeT >= 0f) continue;
                if (Mathf.Abs(w.x - t.basePos.x) <= CellW * 0.5f && Mathf.Abs(w.y - t.basePos.y) <= CellH * 0.5f) return i;
            }
            return -1;
        }

        // ------------------------------------------------------------------ camera

        void SetZoom(float z)
        {
            zoom = Mathf.Clamp(z, 1f, 3.5f);
            pan = ClampPan(pan);
        }

        Vector2 ClampPan(Vector2 p)
        {
            float ppu = basePpu * zoom;
            float bw = (worldMax.x - worldMin.x), bh = (worldMax.y - worldMin.y);
            float vw = ViewportPx.width / ppu, vh = ViewportPx.height / ppu;
            float mx = Mathf.Max(0f, (bw - vw) * 0.5f + 0.5f), my = Mathf.Max(0f, (bh - vh) * 0.5f + 0.5f);
            return new Vector2(Mathf.Clamp(p.x, -mx, mx), Mathf.Clamp(p.y, -my, my));
        }

        void ApplyCamera(bool force)
        {
            if (Cam == null || tiles == null) return;
            var vp = ViewportPx;
            if (!force && lastViewport == vp && lastScreen.x == Screen.width && lastScreen.y == Screen.height && !zoomDirty) { PositionCamera(); return; }
            lastViewport = vp; lastScreen = new Vector2(Screen.width, Screen.height); zoomDirty = false;

            float margin = 0.5f;
            float bw = (worldMax.x - worldMin.x) + margin, bh = (worldMax.y - worldMin.y) + margin;
            float fit = Mathf.Min(vp.width / bw, vp.height / bh);
            float dpi = Screen.dpi > 1f ? Screen.dpi : (Application.isMobilePlatform ? 326f : 96f);
            float minMm = Settings.tileSize == 1 ? 9f : (Settings.tileSize == 2 ? 12f : 0f);
            float minPpu = minMm * dpi / 25.4f;
            basePpu = Mathf.Max(fit, minPpu);
            CanPan = basePpu > fit * 1.001f || zoom > 1.001f;
            pan = ClampPan(pan);
            PositionCamera();
        }

        bool zoomDirty;

        void PositionCamera()
        {
            float ppu = basePpu * zoom;
            Cam.orthographicSize = Screen.height * 0.5f / ppu;
            Vector2 center = (worldMin + worldMax) * 0.5f;
            Vector2 vc = new Vector2(ViewportPx.x + ViewportPx.width * 0.5f, ViewportPx.y + ViewportPx.height * 0.5f);
            Vector2 sc = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 offsetWorld = (vc - sc) / ppu;
            Cam.transform.position = new Vector3(center.x - offsetWorld.x + pan.x, center.y - offsetWorld.y + pan.y, -10f);
            CanPan = (worldMax.x - worldMin.x + 0.5f) * ppu > ViewportPx.width * 1.002f || (worldMax.y - worldMin.y + 0.5f) * ppu > ViewportPx.height * 1.002f;
        }

        /// <summary>Screen position of a tile (used by tutorials and automated tests).</summary>
        public Vector2 TileScreenPos(int pos)
        {
            return Cam.WorldToScreenPoint(tiles[pos].basePos);
        }

        /// <summary>Forces the next frame to recompute the camera fit (tile size setting changed, window resized).</summary>
        public void Refit() { lastViewport = new Rect(-1, -1, 0, 0); }

        public bool IsVisible(int pos) { return tiles != null && tiles[pos].visible; }
        public int SelectedTile { get { return selected; } }
    }
}
