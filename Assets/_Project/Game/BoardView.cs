using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using Mahjong.Core;

namespace Mahjong.Game
{
    /// <summary>
    /// Renders a GameSession's board as real 3D tiles on a slab, handles taps, animations and the camera fit.
    /// The play surface tilts about its centre by <see cref="SettingsData.tilt"/> degrees; the camera never rotates.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        public const float CellW = 1f, CellH = 1.3f;
        public const float Fov = 26f;                // vertical field of view of the board camera
        public const int MaxTilt = 60, MaxRoll = 45;
        const float SlabPad = 0.4f;                  // slab border around the outermost tiles
        const float DragDegPerScreen = 100f;         // dragging the background a full screen height tilts this many degrees
        const float AutoZoomRate = 0.7f;             // per second; the view takes a few seconds to settle on the remaining tiles
        const float AutoZoomMax = 2.5f;              // never zoom further than this multiple of the whole-board fit
        const float AutoZoomTileFrac = 0.22f;        // ...nor so far that one tile is wider than this share of the viewport

        sealed class T
        {
            public Transform tr;
            public MeshRenderer rend;
            public MeshFilter mf;
            public Vector3 basePos;
            public int z;
            public bool visible;
            public int shownKind = -1;
            public int prevKind = -1;               // the face shown until a shuffle flip turns over
            public float spawnDelay, spawnT = 1f, removeT = -1f, popT = -1f, flipT = -1f, shakeT = -1f, hintT = -1f, flashT = -1f;
            public Vector3 removeTo;
            public bool selected, free, assist;
            // what the material was last given, so unchanged tiles cost nothing
            public int faceSent = -1;
            public bool fadeSent;
            public Color tintSent = new Color(-1, -1, -1, -1), glowSent = new Color(-1, -1, -1, -1);
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
        TileAtlas atlas;
        Transform pivot;                             // rotated to tilt the play surface; sits at the world origin
        Transform root;                              // holds the tiles, offset so the board centre is at the pivot
        Transform slabTr;
        MeshFilter slabMf;
        Mesh tileMesh;
        Material matOpaque, matFade, matSlab;
        static readonly MaterialPropertyBlock Mpb = new MaterialPropertyBlock();
        static readonly int IdColor = Shader.PropertyToID("_Color"), IdFace = Shader.PropertyToID("_FaceRect"),
                            IdGlow = Shader.PropertyToID("_Glow"), IdTex = Shader.PropertyToID("_MainTex");
        readonly List<Puff> puffs = new List<Puff>();
        Sprite circle;

        int selected = -1;
        float lastMatchTime = -10f;
        int combo;
        public int BestCombo { get; private set; }
        public const float ComboWindow = 3f;         // seconds allowed between matches to keep a combo alive

        /// <summary>Chain length so far (0 when no chain is alive).</summary>
        public int Combo { get { return ComboLeft01 > 0f ? combo : 0; } }

        /// <summary>How much of the combo window is left, 1 right after a match falling to 0 when the chain lapses.</summary>
        public float ComboLeft01
        {
            get
            {
                if (combo <= 0 || ended) return 0f;
                return Mathf.Clamp01(1f - (Time.unscaledTime - lastMatchTime) / ComboWindow);
            }
        }
        float stuckCheckAt = -1f;
        float winAt = -1f;
        bool ended;
        float autoClearAt = -1f;                     // while set, the remaining (all free) tiles are being matched off one pair at a time
        const float AutoClearStep = 0.28f;
        readonly List<int> scratchFree = new List<int>();
        readonly List<long> scratchPairs = new List<long>();
        public bool InputEnabled = true;

        /// <summary>True while the board is playing out the last pairs by itself.</summary>
        public bool AutoClearing { get { return autoClearAt >= 0f; } }

        // camera state
        Vector2 worldMin, worldMax;                  // footprint of the tiles in board space
        int layers = 1;                              // height of the tallest stack, in tiles
        // what the camera is framing: the whole board, or with auto zoom the tiles still left, eased towards slowly
        Vector2 viewMin, viewMax;
        float viewLayers = 1f;
        float zoom = 1f;
        Vector2 pan;                                 // world units at the board's centre depth
        float shownTilt, shownRoll;                  // ease towards the settings so changes swing smoothly
        float basePpu = 1f;                          // pixels per world unit at the board's centre depth, zoom 1
        bool dragging, pressed, pressOverUi, pressOnTile, tiltDrag;
        float dragPitch, dragRoll;                   // unrounded tilt while dragging, so slow drags still move
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

        public int TargetTilt { get { return Mathf.Clamp(Settings != null ? Settings.tilt : 0, 0, MaxTilt); } }
        public int TargetRoll { get { return Mathf.Clamp(Settings != null ? Settings.roll : 0, -MaxRoll, MaxRoll); } }

        /// <summary>Zoom of the camera fit relative to the whole board (above 1 once auto zoom has closed in).</summary>
        public float AutoZoomFactor { get; private set; }

        /// <summary>The pixels-per-unit the camera currently fits at; grows as auto zoom closes in on the remaining tiles.</summary>
        public float ViewScale { get { return basePpu; } }

        void Awake()
        {
            pivot = new GameObject("Pivot").transform;
            pivot.SetParent(transform, false);
            root = new GameObject("Tiles").transform;
            root.SetParent(pivot, false);
            circle = TileArt.CircleSprite();
            BuildMaterials();

            var slab = new GameObject("Slab");
            slabTr = slab.transform;
            slabTr.SetParent(root, false);
            slabMf = slab.AddComponent<MeshFilter>();
            var sr = slab.AddComponent<MeshRenderer>();
            sr.sharedMaterial = matSlab;
            NoShadows(sr);
        }

        void BuildMaterials()
        {
            var sh = Resources.Load<Shader>("MahjongTile");
            if (sh == null) sh = Shader.Find("Mahjong/Tile");
            if (sh == null) Debug.LogError("Mahjong/Tile shader missing; the board will not draw.");
            matOpaque = new Material(sh) { name = "TileOpaque", enableInstancing = true };
            matFade = new Material(sh) { name = "TileFade", enableInstancing = true };
            matFade.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            matFade.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            matFade.renderQueue = 3000;
            matSlab = new Material(sh) { name = "Slab" };
            matSlab.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            matSlab.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            matSlab.SetColor(IdColor, new Color(1f, 1f, 1f, 0.55f));
            matSlab.renderQueue = 2999;          // after the solid tiles, before the fading ones
        }

        static void NoShadows(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ------------------------------------------------------------------ setup

        public void Begin(GameSession session, TileAtlas tileAtlas, bool animateIn)
        {
            Clear();
            Session = session;
            UseAtlas(tileAtlas);
            var L = session.Level.Layout;
            int n = L.Count;
            tiles = new T[n];
            var minV = new Vector2(float.MaxValue, float.MaxValue); var maxV = new Vector2(float.MinValue, float.MinValue);
            int maxZ = 0;
            for (int i = 0; i < n; i++)
            {
                var p = L.Pos[i];
                var t = new T { z = p.Z };
                var go = new GameObject("t" + i);
                go.transform.SetParent(root, false);
                t.tr = go.transform;
                t.mf = go.AddComponent<MeshFilter>();
                t.mf.sharedMesh = tileMesh;
                t.rend = go.AddComponent<MeshRenderer>();
                t.rend.sharedMaterial = matOpaque;
                NoShadows(t.rend);
                t.basePos = WorldPos(p);
                t.visible = session.Board.Present[i];
                t.shownKind = session.Board.KindAt[i];
                t.rend.enabled = t.visible;
                tiles[i] = t;
                if (p.Z > maxZ) maxZ = p.Z;
                minV = Vector2.Min(minV, new Vector2(t.basePos.x - CellW * 0.5f, t.basePos.y - CellH * 0.5f));
                maxV = Vector2.Max(maxV, new Vector2(t.basePos.x + CellW * 0.5f, t.basePos.y + CellH * 0.5f));
            }
            worldMin = minV; worldMax = maxV; layers = maxZ + 1;

            // centre the board on the pivot and size the slab under it
            Vector2 c = (worldMin + worldMax) * 0.5f;
            root.localPosition = new Vector3(-c.x, -c.y, 0f);
            if (slabMf.sharedMesh != null) Destroy(slabMf.sharedMesh);
            slabMf.sharedMesh = TileMesh.BuildSlab((worldMax.x - worldMin.x) * 0.5f + SlabPad, (worldMax.y - worldMin.y) * 0.5f + SlabPad,
                                                    new Color32(14, 34, 38, 255), new Color32(6, 16, 20, 255));
            slabTr.localPosition = new Vector3(c.x, c.y, 0f);

            selected = -1; combo = 0; BestCombo = 0; ended = false; stuckCheckAt = -1f; winAt = -1f; autoClearAt = -1f;
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
            if (!animateIn) MaybeStartAutoClear();              // a resumed board may already have nothing left blocked
            shownTilt = TargetTilt; shownRoll = TargetRoll;
            ViewTarget(out viewMin, out viewMax, out viewLayers);   // start already framing the tiles that are on the board
            ApplyCamera(true);
            Animate(0f);                            // give every tile its first look before the first frame draws
        }

        public void Clear()
        {
            if (tiles != null)
                for (int i = 0; i < tiles.Length; i++) if (tiles[i].tr != null) Destroy(tiles[i].tr.gameObject);
            tiles = null; Session = null;
        }

        /// <summary>Position of a layout cell in board space. Layers stack straight up, towards the camera (-z).</summary>
        public static Vector3 WorldPos(TilePos p)
        {
            return new Vector3(p.X * 0.5f * CellW + CellW * 0.5f,
                               -(p.Y * 0.5f * CellH + CellH * 0.5f),
                               -p.Z * TileMesh.Thick);
        }

        Vector3 ToWorld(Vector3 board) { return root.TransformPoint(board); }

        public void SetAtlas(TileAtlas newAtlas)
        {
            UseAtlas(newAtlas);
            if (tiles == null) return;
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i].mf.sharedMesh = tileMesh;
                tiles[i].faceSent = -1;
            }
        }

        void UseAtlas(TileAtlas a)
        {
            if (a == atlas && tileMesh != null) return;
            atlas = a;
            var old = tileMesh;
            tileMesh = TileMesh.BuildTile(a.Style);
            matOpaque.SetTexture(IdTex, a.Texture);
            matFade.SetTexture(IdTex, a.Texture);
            if (tiles != null) for (int i = 0; i < tiles.Length; i++) tiles[i].mf.sharedMesh = tileMesh;
            if (old != null) Destroy(old);
        }

        // ------------------------------------------------------------------ actions

        public void TapTile(int pos)
        {
            if (tiles == null || ended || !InputEnabled || AutoClearing) return;
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
            combo = (now - lastMatchTime < ComboWindow) ? combo + 1 : 1;
            lastMatchTime = now;
            if (combo > BestCombo) BestCombo = combo;
            bool reduce = Settings.reducedMotion;
            Vector3 mid = (tiles[a].basePos + tiles[c].basePos) * 0.5f;
            foreach (int id in new[] { a, c })
            {
                var t = tiles[id];
                t.removeT = 0f; t.removeTo = mid; t.selected = false; t.hintT = -1f;
                if (!reduce) SpawnPuffs(t.basePos + new Vector3(0f, 0f, -TileMesh.Thick), 5);
            }
            RefreshFree();
            ClearAssist();
            if (Audio != null) Audio.Play("match", 0.9f, Mathf.Min(1.6f, 1f + 0.06f * (combo - 1)));
            if (OnMatched != null) OnMatched(a, c, combo);
            if (OnChanged != null) OnChanged();
            if (Session.IsWon) { winAt = Time.unscaledTime + (reduce ? 0.2f : 0.55f); ended = true; autoClearAt = -1f; }
            else
            {
                stuckCheckAt = Time.unscaledTime + 0.7f;
                MaybeStartAutoClear();
            }
        }

        /// <summary>True when every tile still on the board is free, so any matching pair can simply be taken.</summary>
        bool NothingBlocked()
        {
            var b = Session.Board;
            if (b.Remaining == 0) return false;
            for (int i = 0; i < b.Present.Length; i++) if (b.Present[i] && !b.IsFree(i)) return false;
            return true;
        }

        void MaybeStartAutoClear()
        {
            if (autoClearAt >= 0f || ended || Settings == null || !Settings.autoClear || !NothingBlocked()) return;
            Deselect();
            autoClearAt = Time.unscaledTime + 0.45f;
        }

        void StepAutoClear()
        {
            autoClearAt = -1f;
            if (ended || Session == null) return;
            Session.Board.GetFreePairs(scratchFree, scratchPairs);
            if (scratchPairs.Count == 0) return;                  // cannot happen with everything free, but never loop on it
            long p = scratchPairs[0];
            DoMatch(Board.PairA(p), Board.PairB(p));              // queues the next step itself while tiles remain and nothing is blocked
        }

        public bool DoUndo()
        {
            if (Session == null || ended) return false;
            autoClearAt = -1f;                                    // stepping back takes the board off autopilot
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

        /// <summary>Brings the tiles in line with the session board (after undo, shuffle or a resume).</summary>
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
                    t.visible = true; t.rend.enabled = true; t.removeT = -1f;
                    t.popT = animate && !reduce ? 0f : -1f;
                    if (animate && reduce) t.popT = 0.8f;
                }
                else if (!present && t.visible)
                {
                    t.visible = false; t.rend.enabled = false; t.removeT = -1f;
                }
                if (present && t.shownKind != b.KindAt[i])
                {
                    // a flip already under way keeps showing the face it started from
                    if (!(t.flipT >= 0f)) t.prevKind = t.shownKind;
                    t.shownKind = b.KindAt[i];
                    if (animate && !reduce) t.flipT = 0f;
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

        /// <summary>Sparkles at a point in board space. They are flat sprites facing the camera, a little in front of the tile.</summary>
        void SpawnPuffs(Vector3 at, int count)
        {
            Vector3 w = ToWorld(at) + new Vector3(0f, 0f, -0.4f);
            const float startScale = 0.16f;
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
                p.tr.position = w + new Vector3(UnityEngine.Random.value * 0.4f - 0.2f, UnityEngine.Random.value * 0.4f - 0.2f, 0);
                p.sr.enabled = true;
                p.tr.localScale = new Vector3(startScale, startScale, 1f);
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
            if (autoClearAt >= 0f && Time.unscaledTime >= autoClearAt && InputEnabled) StepAutoClear();   // waits out pause and other panels
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

        static bool Near(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f && Mathf.Abs(a.a - b.a) < 0.004f;
        }

        /// <summary>Hands a tile its face, tint, fade and glow. Does nothing when nothing changed.</summary>
        void Paint(T t, int faceKind, Color tint, Color glow)
        {
            bool fade = tint.a < 0.995f;
            if (fade != t.fadeSent) { t.rend.sharedMaterial = fade ? matFade : matOpaque; t.fadeSent = fade; }
            if (faceKind == t.faceSent && Near(tint, t.tintSent) && Near(glow, t.glowSent)) return;
            t.faceSent = faceKind; t.tintSent = tint; t.glowSent = glow;
            Mpb.Clear();
            Mpb.SetVector(IdFace, atlas.FaceUv(faceKind));
            Mpb.SetColor(IdColor, tint);
            Mpb.SetColor(IdGlow, glow);
            t.rend.SetPropertyBlock(Mpb);
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
                Quaternion rot = Quaternion.identity;
                int face = t.shownKind;

                if (t.spawnT < 1f)
                {
                    if (t.spawnDelay > 0f) { t.spawnDelay -= dt; alpha = 0f; }
                    else
                    {
                        t.spawnT = Mathf.Min(1f, t.spawnT + dt / 0.32f);
                        float k = EaseOutBack(t.spawnT);
                        pos.z -= (1f - k) * (3.2f + 0.5f * t.z);        // settle down onto the table
                        pos.y += (1f - k) * 0.9f;
                        alpha = Mathf.Clamp01(t.spawnT * 4f);
                    }
                }
                if (t.removeT >= 0f)
                {
                    t.removeT += dt / (reduce ? 0.12f : 0.3f);
                    float k = Mathf.Clamp01(t.removeT);
                    pos = Vector3.Lerp(t.basePos, t.removeTo, k * 0.75f);
                    pos.z -= Mathf.Sin(k * Mathf.PI * 0.8f) * 0.9f;     // lift off the table
                    float s = reduce ? 1f : Mathf.Lerp(1f, 0.55f, k);
                    scale = new Vector3(s, s, s);
                    alpha = 1f - k;
                    if (t.removeT >= 1f)
                    {
                        t.removeT = -1f; t.visible = false; t.rend.enabled = false;
                        continue;
                    }
                }
                if (t.popT >= 0f)
                {
                    t.popT += dt / 0.28f;
                    float k = Mathf.Clamp01(t.popT);
                    float s = reduce ? 1f : Mathf.Lerp(0.5f, 1f, EaseOutBack(k));
                    scale = new Vector3(s, s, s);
                    alpha = Mathf.Clamp01(k * 3f);
                    if (t.popT >= 1f) t.popT = -1f;
                }
                if (t.flipT >= 0f)
                {
                    // a full turn about the tile's own vertical axis; the new face is swapped in while its back is towards us
                    t.flipT += dt / 0.5f;
                    float k = Mathf.Clamp01(t.flipT);
                    face = k >= 0.5f ? t.shownKind : t.prevKind;
                    rot = Quaternion.Euler(0f, k * 360f, 0f);
                    pos.z -= Mathf.Sin(k * Mathf.PI) * 0.8f;
                    if (t.flipT >= 1f) t.flipT = -1f;
                }
                if (t.shakeT >= 0f)
                {
                    t.shakeT += dt / 0.35f;
                    pos.x += Mathf.Sin(t.shakeT * 38f) * 0.06f * (1f - Mathf.Clamp01(t.shakeT));
                    if (t.shakeT >= 1f) t.shakeT = -1f;
                }
                if (t.selected)
                {
                    float s = reduce ? 1.02f : 1.03f + 0.01f * Mathf.Sin(tm * 7f);
                    scale *= s;
                    pos.z -= 0.22f;                                     // picked up off the table
                }

                // glow: flash > hint > selected > assist
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

                float v = (t.free || t.removeT >= 0f) ? 1f : dim;
                Paint(t, face, new Color(v, v, v, alpha), oc);
                t.tr.localPosition = pos;
                t.tr.localRotation = rot;
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

        /// <summary>Changes the tilt setting by a number of degrees and schedules a save.</summary>
        public void AdjustTilt(int delta)
        {
            int n = Mathf.Clamp(Settings.tilt + delta, 0, MaxTilt);
            if (n == Settings.tilt) return;
            Settings.tilt = n;
            if (App.Instance != null) App.Instance.SaveSoon();
        }

        /// <summary>
        /// Tips the board by a number of degrees: dPitch lays the top back (positive) or brings it forward, dRoll pushes the
        /// right edge away (positive) or the left. Both are clamped to their limits and saved shortly after.
        /// </summary>
        public void TiltBy(float dPitch, float dRoll)
        {
            if (!tiltDrag) { dragPitch = Settings.tilt; dragRoll = Settings.roll; }
            dragPitch = Mathf.Clamp(dragPitch + dPitch, 0f, MaxTilt);
            dragRoll = Mathf.Clamp(dragRoll + dRoll, -MaxRoll, MaxRoll);
            int p = Mathf.RoundToInt(dragPitch), r = Mathf.RoundToInt(dragRoll);
            if (p == Settings.tilt && r == Settings.roll) return;
            Settings.tilt = p; Settings.roll = r;
            if (App.Instance != null) App.Instance.SaveSoon();
        }

        void HandleInput()
        {
            if (Cam == null || !InputEnabled) { pressed = false; return; }

            // zoom: wheel and pinch. Shift + wheel (or PageUp / PageDown) tilts the board instead.
            float wheel = Input.mouseScrollDelta.y;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Mathf.Abs(wheel) > 0.01f && ViewportPx.Contains(Input.mousePosition) && !OverUi(Input.mousePosition))
            {
                if (shift) AdjustTilt(Mathf.RoundToInt(wheel * 5f));
                else SetZoom(zoom * (1f + 0.12f * wheel));
            }
            if (Input.GetKeyDown(KeyCode.PageUp)) AdjustTilt(5);
            if (Input.GetKeyDown(KeyCode.PageDown)) AdjustTilt(-5);
            if (Input.touchCount == 2)
            {
                float d = Vector2.Distance(Input.GetTouch(0).position, Input.GetTouch(1).position);
                if (lastPinch > 1f) SetZoom(zoom * d / lastPinch);
                lastPinch = d; pressed = false; dragging = false; tiltDrag = false;
                return;
            }
            lastPinch = 0f;

            bool down = Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
            bool up = Input.GetMouseButtonUp(0) || (Input.touchCount > 0 && (Input.GetTouch(0).phase == TouchPhase.Ended || Input.GetTouch(0).phase == TouchPhase.Canceled));
            Vector2 p = PointerPos();

            if (down)
            {
                pressed = true; dragging = false; tiltDrag = false; pressPos = p; lastPointer = p;
                pressOverUi = OverUi(p);
                pressOnTile = !pressOverUi && HitTest(Cam.ScreenPointToRay(new Vector3(p.x, p.y, 0f))) >= 0;
            }
            else if (pressed && !pressOverUi)
            {
                // a drag that starts on a tile pans a board that is bigger than the screen;
                // one that starts on the background (slab, table, anything but a tile) tips the board
                float thr = Mathf.Max(10f, Mathf.Min(Screen.width, Screen.height) * 0.018f);
                if (!dragging && !tiltDrag && (p - pressPos).magnitude > thr)
                {
                    if (pressOnTile) dragging = CanPan;
                    else { tiltDrag = true; dragPitch = Settings.tilt; dragRoll = Settings.roll; }
                }
                if (dragging)
                {
                    float ppu = basePpu * zoom;
                    pan -= (p - lastPointer) / ppu;     // dragging right moves the board right = camera left
                    pan = ClampPan(pan);
                    lastPointer = p;
                }
                else if (tiltDrag)
                {
                    // dragging up lays the top of the board back; dragging right pushes the right edge away
                    Vector2 d = (p - lastPointer) * (DragDegPerScreen / Screen.height);
                    TiltBy(d.y, d.x);
                    lastPointer = p;
                }
            }
            if (up && pressed)
            {
                if (!dragging && !tiltDrag && !pressOverUi && (p - pressPos).magnitude < Mathf.Max(24f, Screen.height * 0.03f))
                    TapTile(HitTest(Cam.ScreenPointToRay(new Vector3(p.x, p.y, 0f))));
                pressed = false; dragging = false; tiltDrag = false;
            }
            if (Input.GetMouseButtonDown(1)) { Deselect(); }
        }

        /// <summary>The nearest tile the ray passes through, or -1. Tests each tile's box in board space, so stacks pick the top tile.</summary>
        public int HitTest(Ray ray)
        {
            if (tiles == null) return -1;
            Vector3 o = root.InverseTransformPoint(ray.origin);
            Vector3 d = root.InverseTransformDirection(ray.direction);
            int best = -1; float bestT = float.MaxValue;
            for (int i = 0; i < tiles.Length; i++)
            {
                var t = tiles[i];
                if (!t.visible || t.removeT >= 0f) continue;
                Vector3 c = t.basePos;
                float tn, tf;
                if (!Slab(o.x, d.x, c.x - CellW * 0.5f, c.x + CellW * 0.5f, out tn, out tf)) continue;
                float a, b;
                if (!Slab(o.y, d.y, c.y - CellH * 0.5f, c.y + CellH * 0.5f, out a, out b)) continue;
                tn = Mathf.Max(tn, a); tf = Mathf.Min(tf, b);
                if (!Slab(o.z, d.z, c.z - TileMesh.Thick, c.z, out a, out b)) continue;
                tn = Mathf.Max(tn, a); tf = Mathf.Min(tf, b);
                if (tn > tf || tf < 0f) continue;
                if (tn < bestT) { bestT = tn; best = i; }
            }
            return best;
        }

        /// <summary>One axis of a ray against a box (slab method). False when the ray runs parallel outside it.</summary>
        static bool Slab(float o, float d, float lo, float hi, out float tNear, out float tFar)
        {
            if (Mathf.Abs(d) < 1e-7f)
            {
                tNear = float.MinValue; tFar = float.MaxValue;
                return o >= lo && o <= hi;
            }
            float t0 = (lo - o) / d, t1 = (hi - o) / d;
            tNear = Mathf.Min(t0, t1); tFar = Mathf.Max(t0, t1);
            return true;
        }

        // ------------------------------------------------------------------ camera
        //
        // The camera sits on the -z axis looking at the pivot (the middle of the whole board). The pivot is rotated by
        // the tilt (about x: lays the top back, brings the bottom edge forward) and the roll (about the vertical: pushes one
        // side away). "Pixels per unit" is measured at the pivot's depth, so zoom and tile-size logic work as they did with
        // the flat camera. To put the board in the part of the screen left over by the HUD, to pan, and to centre on the
        // tiles that are left, the frustum is shifted rather than the camera moved.

        float FocalPx { get { return Screen.height * 0.5f / Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad); } }

        Quaternion BoardRotation { get { return Quaternion.Euler(shownTilt, -shownRoll, 0f); } }

        /// <summary>
        /// Where a box of tiles (board-space footprint vmin..vmax, lay layers tall, plus the slab border) lands on screen at
        /// the given scale and board rotation, as a centre and size in pixels measured from the optical axis.
        /// </summary>
        void ProjectBounds(float ppu, Quaternion rot, Vector2 vmin, Vector2 vmax, float lay, out Vector2 center, out Vector2 size)
        {
            float f = FocalPx, dist = f / ppu;
            Vector2 mid = (vmin + vmax) * 0.5f - (worldMin + worldMax) * 0.5f;      // relative to the pivot
            float hx = (vmax.x - vmin.x) * 0.5f + SlabPad, hy = (vmax.y - vmin.y) * 0.5f + SlabPad;
            float zTop = -lay * TileMesh.Thick, zBack = TileMesh.SlabTop + TileMesh.SlabDepth;
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            for (int ix = 0; ix < 2; ix++)
                for (int iy = 0; iy < 2; iy++)
                    for (int iz = 0; iz < 2; iz++)
                    {
                        Vector3 q = rot * new Vector3(mid.x + (ix == 0 ? -hx : hx), mid.y + (iy == 0 ? -hy : hy), iz == 0 ? zTop : zBack);
                        float depth = Mathf.Max(0.1f, dist + q.z);
                        float px = f * q.x / depth, py = f * q.y / depth;
                        if (px < x0) x0 = px; if (px > x1) x1 = px;
                        if (py < y0) y0 = py; if (py > y1) y1 = py;
                    }
            center = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
            size = new Vector2(x1 - x0, y1 - y0);
        }

        /// <summary>The scale at which that box just fits a viewport of the given size.</summary>
        float FitPpu(Quaternion rot, Vector2 vmin, Vector2 vmax, float lay, float fitW, float fitH)
        {
            float ppu = Mathf.Min(fitW / (vmax.x - vmin.x + 2f * SlabPad), fitH / (vmax.y - vmin.y + 2f * SlabPad));
            for (int it = 0; it < 8; it++)
            {
                Vector2 c, size;
                ProjectBounds(ppu, rot, vmin, vmax, lay, out c, out size);
                ppu *= Mathf.Min(fitW / size.x, fitH / size.y);
            }
            return ppu;
        }

        /// <summary>What the camera should frame: the whole board, or with auto zoom just the tiles still standing.</summary>
        void ViewTarget(out Vector2 mn, out Vector2 mx, out float lay)
        {
            mn = worldMin; mx = worldMax; lay = layers;
            if (tiles == null || Settings == null || !Settings.autoZoom || Settings.reducedMotion) return;
            var a = new Vector2(float.MaxValue, float.MaxValue); var b = new Vector2(float.MinValue, float.MinValue);
            int zTop = -1;
            for (int i = 0; i < tiles.Length; i++)
            {
                var t = tiles[i];
                if (!t.visible || t.removeT >= 0f) continue;       // matched tiles are on their way out
                a = Vector2.Min(a, new Vector2(t.basePos.x - CellW * 0.5f, t.basePos.y - CellH * 0.5f));
                b = Vector2.Max(b, new Vector2(t.basePos.x + CellW * 0.5f, t.basePos.y + CellH * 0.5f));
                if (t.z > zTop) zTop = t.z;
            }
            if (zTop < 0) { mn = viewMin; mx = viewMax; lay = viewLayers; return; }   // nothing left: hold the last view
            mn = a; mx = b; lay = zTop + 1;
        }

        static float Ease(float shown, float target, float rate, float dt)
        {
            return Mathf.Abs(shown - target) > 0.05f ? Mathf.Lerp(shown, target, 1f - Mathf.Exp(-rate * dt)) : target;
        }

        void SetZoom(float z)
        {
            zoom = Mathf.Clamp(z, 1f, 3.5f);
            pan = ClampPan(pan);
        }

        Vector2 ClampPan(Vector2 p)
        {
            float ppu = basePpu * zoom;
            Vector2 c, size;
            ProjectBounds(ppu, BoardRotation, viewMin, viewMax, viewLayers, out c, out size);
            float mx = Mathf.Max(0f, (size.x - ViewportPx.width) * 0.5f + 0.5f * ppu) / ppu;
            float my = Mathf.Max(0f, (size.y - ViewportPx.height) * 0.5f + 0.5f * ppu) / ppu;
            return new Vector2(Mathf.Clamp(p.x, -mx, mx), Mathf.Clamp(p.y, -my, my));
        }

        void ApplyCamera(bool force)
        {
            if (Cam == null || tiles == null) return;

            // ease the shown tilt and roll towards the settings, and the framed area towards the tiles that are left
            float dt = Time.unscaledDeltaTime;
            shownTilt = Ease(shownTilt, TargetTilt, 14f, dt);
            shownRoll = Ease(shownRoll, TargetRoll, 14f, dt);
            Vector2 tmn, tmx; float tl;
            ViewTarget(out tmn, out tmx, out tl);
            if (force) { viewMin = tmn; viewMax = tmx; viewLayers = tl; }
            else
            {
                float k = 1f - Mathf.Exp(-AutoZoomRate * dt);
                viewMin = Vector2.Lerp(viewMin, tmn, k); viewMax = Vector2.Lerp(viewMax, tmx, k);
                viewLayers = Mathf.Lerp(viewLayers, tl, k);
            }

            // the scale at which the framed tiles just fill the viewport. Cheap enough to redo every frame, and it keeps
            // window resizes, the HUD changing size, tilting and zooming in step with each other.
            var vp = ViewportPx;
            float fitW = Mathf.Max(50f, vp.width - 8f), fitH = Mathf.Max(50f, vp.height - 8f);
            var rot = BoardRotation;
            float full = FitPpu(rot, worldMin, worldMax, layers, fitW, fitH);
            float fit = full;
            if (Settings.autoZoom && !Settings.reducedMotion)
            {
                // zoom in on what is left, but not so far that the last few tiles are enormous
                float cap = Mathf.Max(full, Mathf.Min(full * AutoZoomMax, AutoZoomTileFrac * Mathf.Min(vp.width, vp.height) / CellW));
                fit = Mathf.Min(FitPpu(rot, viewMin, viewMax, viewLayers, fitW, fitH), cap);
            }
            AutoZoomFactor = fit / full;
            float dpi = Screen.dpi > 1f ? Screen.dpi : (Application.isMobilePlatform ? 326f : 96f);
            float minMm = Settings.tileSize == 1 ? 9f : (Settings.tileSize == 2 ? 12f : 0f);
            float minPpu = minMm * dpi / 25.4f;
            basePpu = Mathf.Max(fit, minPpu);
            pan = ClampPan(pan);
            PositionCamera();
        }

        void PositionCamera()
        {
            float ppu = basePpu * zoom;
            float dist = FocalPx / ppu;
            pivot.localRotation = BoardRotation;
            Cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, -dist), Quaternion.identity);

            Vector2 c, size;
            ProjectBounds(ppu, BoardRotation, viewMin, viewMax, viewLayers, out c, out size);
            Vector2 vc = new Vector2(ViewportPx.x + ViewportPx.width * 0.5f, ViewportPx.y + ViewportPx.height * 0.5f);
            Vector2 sc = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            // where on screen the optical axis should land so the board's centre sits in the viewport (less the pan)
            Vector2 o = (vc - sc) - c - pan * ppu;

            float near = Mathf.Max(0.3f, dist * 0.25f), far = dist + 200f;
            float hn = near * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            float aspect = (float)Screen.width / Screen.height;
            float k = 2f * hn / Screen.height;       // near-plane units per pixel
            Cam.projectionMatrix = Matrix4x4.Frustum(-hn * aspect - k * o.x, hn * aspect - k * o.x, -hn - k * o.y, hn - k * o.y, near, far);

            CanPan = size.x > ViewportPx.width * 1.002f || size.y > ViewportPx.height * 1.002f;
        }

        /// <summary>Screen position of a tile's face (used by tutorials and automated tests).</summary>
        public Vector2 TileScreenPos(int pos)
        {
            return Cam.WorldToScreenPoint(ToWorld(tiles[pos].basePos + new Vector3(0f, 0f, -TileMesh.Thick)));
        }

        public bool IsVisible(int pos) { return tiles != null && tiles[pos].visible; }
        public int SelectedTile { get { return selected; } }
    }
}
