using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Mahjong.Core;

namespace Mahjong.Game
{
    public abstract class ScreenBase
    {
        protected App app;
        public RectTransform Root;
        public string Name;
        public virtual void Build(App a, Transform parent)
        {
            app = a;
            Root = UiKit.NewRect(Name, parent);
            UiKit.Stretch(Root);
        }
        public virtual void OnShow() { }
        public virtual void OnHide() { }
        public virtual void Tick() { }
        public virtual void OnLayout() { }
        /// <summary>Back button / Escape. Return true if handled.</summary>
        public virtual bool OnBack() { return false; }
    }

    public sealed class LevelRequest
    {
        public string Mode;                 // journey | daily | free
        public int Level, Day, Target;
        public ulong Seed;
        public List<int> ResumeLog;
        public int ResumeSeconds;
    }

    /// <summary>Root of the whole game. Everything is created from code at startup; the scene is empty.</summary>
    public sealed class App : MonoBehaviour
    {
        public static App Instance { get; private set; }
        public const string Version = "1.0";

        public SaveStore Save;
        public SettingsData S { get { return Save.Data.settings; } }
        public Sfx Audio;
        public BoardView Board;
        public Camera Cam;
        public Canvas Canvas;
        public CanvasScaler Scaler;
        public RectTransform SafeRoot;
        public bool Portrait { get; private set; }
        public float UiScale { get { return Canvas.scaleFactor; } }
        public bool DebugMode;
        public TileAtlas Atlas { get; private set; }

        RawImage bgImage;
        Texture2D bgTexture;
        string bgId;
        int lastBgHue = -1;
        readonly System.Random bgRng = new System.Random();
        readonly Dictionary<string, ScreenBase> screens = new Dictionary<string, ScreenBase>();
        ScreenBase current;
        string currentName;
        public string Previous { get; private set; }

        Task<Raster> atlasTask;
        string atlasTaskId;
        GameObject loadingOverlay;
        float loadingSince = -1f;

        Task<Level> levelTask;
        LevelRequest pendingRequest;
        readonly Dictionary<string, Task<Level>> prefetch = new Dictionary<string, Task<Level>>();

        RectTransform toastRoot;
        Text toastText;
        float toastUntil;
        readonly Queue<string> toastQueue = new Queue<string>();

        int lastW, lastH;
        float saveTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("Mahjong App");
            DontDestroyOnLoad(go);
            go.AddComponent<App>();
        }

        // ------------------------------------------------------------------ startup

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Input.simulateMouseWithTouches = true;
            UiKit.Init();

            string dir = null;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-savedir") dir = args[i + 1];
            if (dir != null) SaveStore.OverrideDir = dir;
            Save = SaveStore.Load();

            SetupCamera();
            SetupCanvas();

            Audio = new GameObject("Audio").AddComponent<Sfx>();
            Audio.transform.SetParent(transform, false);
            Audio.Init();
            Audio.SoundVolume = S.sound; Audio.MusicVolume = S.music;
            UiKit.PlaySfx = n => Audio.Play(n, 0.8f);

            var bv = new GameObject("Board");
            bv.transform.SetParent(transform, false);
            Board = bv.AddComponent<BoardView>();
            Board.Settings = S; Board.Audio = Audio; Board.Cam = Cam;

            BuildScreens();
            ApplyBackground();
            BeginAtlasBuild(S.tileSet);
            ShowLoading(true);
            UpdateLayoutMetrics(true);

            if (Array.IndexOf(args, "-autotest") >= 0) gameObject.AddComponent<AutoTest>();
        }

        void SetupCamera()
        {
            Cam = Camera.main;
            if (Cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                Cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            Cam.orthographic = true;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.08f, 0.12f, 0.12f);
            Cam.transform.position = new Vector3(0, 0, -10);
            Cam.nearClipPlane = 0.1f; Cam.farClipPlane = 50f;
        }

        void SetupCanvas()
        {
            var cgo = new GameObject("Canvas", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            Canvas = cgo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            Scaler = cgo.AddComponent<CanvasScaler>();
            Scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            Scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            cgo.AddComponent<GraphicRaycaster>();

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }

            // The background must sit BEHIND the board sprites, and overlay canvases always draw on top of the camera.
            // So it gets its own camera-space canvas pushed far behind the tiles.
            var bgc = new GameObject("BackgroundCanvas", typeof(RectTransform));
            bgc.transform.SetParent(transform, false);
            var bcv = bgc.AddComponent<Canvas>();
            bcv.renderMode = RenderMode.ScreenSpaceCamera;
            bcv.worldCamera = Cam;
            bcv.planeDistance = 30f;
            bcv.sortingOrder = -10;
            var bg = UiKit.NewRect("Background", bgc.transform);
            UiKit.Stretch(bg);
            bgImage = bg.gameObject.AddComponent<RawImage>();
            bgImage.raycastTarget = false;

            SafeRoot = UiKit.NewRect("SafeArea", Canvas.transform);
            UiKit.Stretch(SafeRoot);
        }

        void BuildScreens()
        {
            Register(new HomeScreen());
            Register(new GameScreen());
            Register(new DailyScreen());
            Register(new LevelsScreen());
            Register(new CollectionScreen());
            Register(new SettingsScreen());
            Register(new StatsScreen());
            Register(new FreePlayScreen());

            // toast above everything
            toastRoot = UiKit.NewRect("Toast", Canvas.transform);
            UiKit.Anchor(toastRoot, 0.5f, 1f, 0.5f, 1f);
            toastRoot.pivot = new Vector2(0.5f, 1f);
            toastRoot.sizeDelta = new Vector2(900, 110);
            toastRoot.anchoredPosition = new Vector2(0, -24);
            var bgi = UiKit.Round(toastRoot, "Bg", new Color(0.05f, 0.07f, 0.1f, 0.96f));
            UiKit.Stretch(bgi.rectTransform);
            toastText = UiKit.Label(toastRoot, "", 34, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(toastText.rectTransform);
            toastText.rectTransform.offsetMin = new Vector2(24, 8); toastText.rectTransform.offsetMax = new Vector2(-24, -8);
            toastRoot.gameObject.SetActive(false);

            // loading overlay
            var lo = UiKit.NewRect("Loading", Canvas.transform);
            UiKit.Stretch(lo);
            var dim = lo.gameObject.AddComponent<Image>(); dim.color = new Color(0.04f, 0.07f, 0.08f, 0.96f); dim.raycastTarget = true;
            var t = UiKit.Label(lo, "Mahjong", 96, UiKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Fixed(t.rectTransform, 0.5f, 0.5f, 900, 140, 0, 40);
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var t2 = UiKit.Label(lo, "Preparing tiles...", 36, UiKit.InkDim);
            UiKit.Fixed(t2.rectTransform, 0.5f, 0.5f, 900, 60, 0, -60);
            t2.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            loadingOverlay = lo.gameObject;
        }

        void Register(ScreenBase s)
        {
            s.Name = s.GetType().Name.Replace("Screen", "").ToLowerInvariant();
            s.Build(this, SafeRoot);
            s.Root.gameObject.SetActive(false);
            screens[s.Name] = s;
        }

        public T Get<T>() where T : ScreenBase
        {
            foreach (var s in screens.Values) if (s is T) return (T)s;
            return null;
        }

        // ------------------------------------------------------------------ navigation

        public void Show(string name)
        {
            if (current != null) { current.OnHide(); current.Root.gameObject.SetActive(false); }
            Previous = currentName;
            currentName = name;
            current = screens[name];
            current.Root.gameObject.SetActive(true);
            current.OnLayout();
            current.OnShow();
            bool inGame = name == "game";
            Board.gameObject.SetActive(inGame);
        }

        public string CurrentName { get { return currentName; } }

        // ------------------------------------------------------------------ appearance

        /// <summary>Applies the chosen background. With "Random" selected, reroll draws a fresh one.</summary>
        public void ApplyBackground(bool reroll = false)
        {
            var def = Content.Background(S.background);
            if (bgId == def.Id && bgTexture != null && !(reroll && def.Id == "random")) return;
            if (bgTexture != null) Destroy(bgTexture);
            bgTexture = def.Id == "random" ? TileArt.RandomBackgroundTexture(bgRng, ref lastBgHue) : TileArt.BackgroundTexture(def);
            bgImage.texture = bgTexture;
            bgId = def.Id;
        }

        void BeginAtlasBuild(string id)
        {
            var style = Content.TileSet(id);
            atlasTaskId = style.Id;
            atlasTask = Task.Run(() => TileAtlas.Draw(style));
        }

        public void SetTileSet(string id)
        {
            S.tileSet = id;
            Save.Save();
            BeginAtlasBuild(id);
            loadingSince = Time.unscaledTime;
        }

        void ShowLoading(bool on)
        {
            if (loadingOverlay != null) loadingOverlay.SetActive(on);
            if (!on) loadingSince = -1f;
        }

        public void ApplySettings()
        {
            Board.Settings = S;
            Board.Refit();
            Audio.SoundVolume = S.sound; Audio.MusicVolume = S.music;
            UpdateLayoutMetrics(true);
            ApplyBackground();
            foreach (var s in screens.Values) s.OnLayout();
        }

        // ------------------------------------------------------------------ layout

        void UpdateLayoutMetrics(bool force)
        {
            if (!force && lastW == Screen.width && lastH == Screen.height) return;
            lastW = Screen.width; lastH = Screen.height;
            Portrait = Screen.height >= Screen.width;
            float scale = S.textScale == 1 ? 1.25f : (S.textScale == 2 ? 1.5f : 1f);
            Scaler.referenceResolution = (Portrait ? new Vector2(800, 1400) : new Vector2(1400, 800)) / scale;
            Scaler.matchWidthOrHeight = Portrait ? 0f : 1f;
            // safe area
            var sa = Screen.safeArea;
            if (sa.width <= 0 || sa.height <= 0) sa = new Rect(0, 0, Screen.width, Screen.height);
            SafeRoot.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            SafeRoot.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            SafeRoot.offsetMin = Vector2.zero; SafeRoot.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            foreach (var s in screens.Values) s.OnLayout();
        }

        // ------------------------------------------------------------------ toast

        public void Toast(string text)
        {
            toastQueue.Enqueue(text);
        }

        // ------------------------------------------------------------------ levels

        static string Key(string mode, int a, int b) { return mode + ":" + a + ":" + b; }

        static Level MakeLevel(LevelRequest r, int offset)
        {
            if (r.Mode == "daily")
            {
                var d = DateUtil.FromInt(r.Day);
                return LevelGenerator.Daily(r.Day, DateUtil.DayNumber(d), DateUtil.WeekdayMondayZero(d));
            }
            if (r.Mode == "free") return LevelGenerator.Free(r.Seed, r.Target);
            return LevelGenerator.Journey(r.Level, offset);
        }

        public void StartJourney(int level, bool resume = true)
        {
            var r = new LevelRequest { Mode = "journey", Level = level };
            var ip = Save.Data.inProgress;
            if (resume && ip != null && ip.mode == "journey" && ip.level == level && ip.gen == DifficultyConfig.GeneratorVersion && ip.offset == S.difficultyOffset)
            { r.ResumeLog = new List<int>(ip.log); r.ResumeSeconds = ip.seconds; }
            Begin(r);
        }

        public void StartDaily(int yyyymmdd, bool resume = true)
        {
            var r = new LevelRequest { Mode = "daily", Day = yyyymmdd };
            var ip = Save.Data.inProgress;
            if (resume && ip != null && ip.mode == "daily" && ip.day == yyyymmdd && ip.gen == DifficultyConfig.GeneratorVersion)
            { r.ResumeLog = new List<int>(ip.log); r.ResumeSeconds = ip.seconds; }
            Begin(r);
        }

        public void StartFree(ulong seed, int target)
        {
            Begin(new LevelRequest { Mode = "free", Seed = seed, Target = target });
        }

        void Begin(LevelRequest r)
        {
            if (levelTask != null) return;
            pendingRequest = r;
            int offset = S.difficultyOffset;
            string key = r.Mode == "journey" ? Key("j", r.Level, offset) : null;
            if (key != null && prefetch.ContainsKey(key)) { levelTask = prefetch[key]; prefetch.Remove(key); }
            else levelTask = Task.Run(() => MakeLevel(r, offset));
            loadingSince = Time.unscaledTime;
        }

        public void Prefetch(int level)
        {
            int offset = S.difficultyOffset;
            string key = Key("j", level, offset);
            if (prefetch.ContainsKey(key)) return;
            if (prefetch.Count > 3) prefetch.Clear();
            var r = new LevelRequest { Mode = "journey", Level = level };
            prefetch[key] = Task.Run(() => MakeLevel(r, offset));
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            UpdateLayoutMetrics(false);

            // atlas build finished?
            if (atlasTask != null && atlasTask.IsCompleted)
            {
                if (atlasTask.IsFaulted) { Debug.LogError(atlasTask.Exception); atlasTask = null; }
                else
                {
                    var old = Atlas;
                    Atlas = new TileAtlas(Content.TileSet(atlasTaskId), atlasTask.Result);
                    atlasTask = null;
                    Board.SetAtlas(Atlas);
                    if (old != null) old.Destroy();
                    if (currentName == null) Show("home");
                    else foreach (var s in screens.Values) s.OnLayout();
                    ShowLoading(false);
                }
            }

            // level generation finished?
            if (levelTask != null && levelTask.IsCompleted && Atlas != null)
            {
                var t = levelTask; levelTask = null;
                var req = pendingRequest;
                if (t.IsFaulted) { Debug.LogError(t.Exception); Toast("Could not build that level."); ShowLoading(false); }
                else
                {
                    ShowLoading(false);
                    ApplyBackground(true);
                    Show("game");
                    Get<GameScreen>().Begin(req, t.Result);
                }
            }

            // loading overlay only if it takes a visible amount of time
            bool waiting = (atlasTask != null) || (levelTask != null);
            if (loadingOverlay != null)
            {
                bool want = waiting && (currentName == null || Time.unscaledTime - loadingSince > 0.35f);
                if (want != loadingOverlay.activeSelf) loadingOverlay.SetActive(want);
            }

            UpdateToast();

            if (current != null) current.Tick();
            if (Input.GetKeyDown(KeyCode.Escape)) { if (current == null || !current.OnBack()) { } }
            if (Input.GetKeyDown(KeyCode.F11) || (Input.GetKey(KeyCode.LeftAlt) && Input.GetKeyDown(KeyCode.Return)))
                Screen.fullScreen = !Screen.fullScreen;

            saveTimer += Time.unscaledDeltaTime;
        }

        void UpdateToast()
        {
            if (toastRoot.gameObject.activeSelf)
            {
                float left = toastUntil - Time.unscaledTime;
                if (left <= 0f) toastRoot.gameObject.SetActive(false);
                else
                {
                    var p = toastRoot.anchoredPosition;
                    p.y = Mathf.Lerp(-24f, 120f, Mathf.Clamp01(1f - Mathf.Min(left, 0.25f) / 0.25f) * (left < 0.25f ? 1f : 0f));
                    toastRoot.anchoredPosition = p;
                }
            }
            else if (toastQueue.Count > 0)
            {
                toastText.text = toastQueue.Dequeue();
                toastRoot.anchoredPosition = new Vector2(0, -24);
                toastRoot.gameObject.SetActive(true);
                toastUntil = Time.unscaledTime + 3.2f;
                Audio.Play("hint", 0.5f);
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && Save != null) { var g = Get<GameScreen>(); if (g != null) g.FlushProgress(); Save.Save(); }
        }

        void OnApplicationQuit()
        {
            if (Save != null) { var g = Get<GameScreen>(); if (g != null) g.FlushProgress(); Save.Save(); }
        }
    }
}
