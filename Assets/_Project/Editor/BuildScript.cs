using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Mahjong.Game;

namespace Mahjong.EditorTools
{
    /// <summary>Project setup and player builds. Run headless with -executeMethod Mahjong.EditorTools.BuildScript.BuildWindows</summary>
    public static class BuildScript
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Mahjong/Configure Project")]
        public static void Configure()
        {
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = false;      // the board is 3D; App.SetupCamera configures the rest at runtime
                cam.clearFlags = CameraClearFlags.SolidColor;
                camGo.AddComponent<AudioListener>();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.companyName = "Craig";
            PlayerSettings.productName = "Mahjong";
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.SplashScreen.show = false;          // the game draws its own splash (Splash.cs)
            PlayerSettings.SplashScreen.showUnityLogo = false;

            try
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.craig.mahjong");
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.craig.mahjong");
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.craig.mahjong");
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
                // Unity 6000.6 only supports ARM64 on Android (x86_64 is obsolete)
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
                PlayerSettings.Android.startInFullscreen = true;
                PlayerSettings.Android.renderOutsideSafeArea = true;
                PlayerSettings.Android.optimizedFramePacing = true;
                PlayerSettings.bundleVersion = "1.0";
            }
            catch (Exception e) { Debug.LogWarning("Platform settings: " + e.Message); }

            try { MakeIcon(); } catch (Exception e) { Debug.LogWarning("Icon: " + e.Message); }
            AssetDatabase.SaveAssets();
            Debug.Log("Mahjong: project configured");
        }


        /// <summary>Draws the app icon from the game's own tile art and assigns it for every platform.</summary>
        static void MakeIcon()
        {
            const string path = "Assets/Icons/icon.png";
            if (!File.Exists(path))
            {
                Directory.CreateDirectory("Assets/Icons");
                const int S = 512;
                var r = new Raster(S, S);
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float f = y / (float)(S - 1);
                        r.Px[y * S + x] = new Color32((byte)Mathf.Lerp(22, 48, f), (byte)Mathf.Lerp(78, 134, f), (byte)Mathf.Lerp(62, 104, f), 255);
                    }
                // round the corners by erasing outside a rounded rectangle
                var mask = new Raster(S, S);
                mask.RoundRect(0, 0, S, S, 112, new Color32(255, 255, 255, 255));
                for (int i = 0; i < r.Px.Length; i++) r.Px[i].a = mask.Px[i].a;
                var tile = TileArt.DrawTile(31, Content.TileSets[0]);
                float sc = 2.15f;
                r.BlitScaled(tile, (S - tile.W * sc) * 0.5f - 6f, (S - tile.H * sc) * 0.5f - 4f, sc);
                var tex = r.ToTexture(false, FilterMode.Bilinear);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            MakeAndroidIcons();
        }

        /// <summary>Android launchers need their own icon slots: an adaptive icon made of a background and a foreground layer.</summary>
        static void MakeAndroidIcons()
        {
            const string bgPath = "Assets/Icons/android_bg.png", fgPath = "Assets/Icons/android_fg.png";
            if (!File.Exists(bgPath) || !File.Exists(fgPath))
            {
                const int S = 512;
                var bg = new Raster(S, S);
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float f = y / (float)(S - 1);
                        bg.Px[y * S + x] = new Color32((byte)Mathf.Lerp(22, 48, f), (byte)Mathf.Lerp(78, 134, f), (byte)Mathf.Lerp(62, 104, f), 255);
                    }
                // the launcher masks the outer third of an adaptive icon, so the tile stays inside the middle 66%
                var fg = new Raster(S, S);
                var tile = TileArt.DrawTile(31, Content.TileSets[0]);
                float sc = 0.56f * S / tile.H;
                fg.BlitScaled(tile, (S - tile.W * sc) * 0.5f, (S - tile.H * sc) * 0.5f, sc);
                foreach (var pair in new[] { new KeyValuePair<string, Raster>(bgPath, bg), new KeyValuePair<string, Raster>(fgPath, fg) })
                {
                    var tex = pair.Value.ToTexture(false, FilterMode.Bilinear);
                    File.WriteAllBytes(pair.Key, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                    AssetDatabase.ImportAsset(pair.Key);
                }
            }
            var bgTex = AssetDatabase.LoadAssetAtPath<Texture2D>(bgPath);
            var fgTex = AssetDatabase.LoadAssetAtPath<Texture2D>(fgPath);
            var android = NamedBuildTarget.Android;

            var adaptive = PlayerSettings.GetPlatformIcons(android, UnityEditor.Android.AndroidPlatformIconKind.Adaptive);
            foreach (var p in adaptive) { p.SetTexture(bgTex, 0); p.SetTexture(fgTex, 1); }
            PlayerSettings.SetPlatformIcons(android, UnityEditor.Android.AndroidPlatformIconKind.Adaptive, adaptive);
        }

        public static void BuildWindows()
        {
            Configure();
            Build(BuildTarget.StandaloneWindows64, "Builds/Windows/Mahjong.exe");
        }

        public static void BuildAndroid()
        {
            Configure();
            EditorUserBuildSettings.buildAppBundle = false;
            Build(BuildTarget.Android, "Builds/Android/Mahjong.apk");
        }

        public static void BuildWebGL()
        {
            Configure();
            // runs from any static host: Gzip with the JS fallback needs no special server headers
            PlayerSettings.WebGL.template = "PROJECT:Mahjong";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = false;       // the template's service worker caches the files instead
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            Build(BuildTarget.WebGL, "Builds/WebGL");
        }

        static void Build(BuildTarget target, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Builds");
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = target,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log("Build " + target + ": " + s.result + "  size=" + (s.totalSize / (1024 * 1024)) + "MB  errors=" + s.totalErrors + "  time=" + s.totalTime);
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
