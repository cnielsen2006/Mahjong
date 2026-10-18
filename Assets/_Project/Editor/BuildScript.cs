using System;
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
                cam.orthographic = true;
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
            PlayerSettings.SplashScreen.showUnityLogo = true;

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
