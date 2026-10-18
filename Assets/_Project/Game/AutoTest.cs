using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Mahjong.Core;

namespace Mahjong.Game
{
    /// <summary>
    /// Self-driving smoke test, only active when the player is started with -autotest.
    /// Plays real levels through the same tap path as a human, visits every screen, and writes screenshots.
    /// </summary>
    public sealed class AutoTest : MonoBehaviour
    {
        const float AutoZoomCapForTest = 2.5f;      // mirrors BoardView's cap on how far auto zoom may close in
        App app;
        string shots;
        int errors;
        readonly List<string> log = new List<string>();

        void Log(string s) { Debug.Log("[AUTOTEST] " + s); log.Add(s); }

        IEnumerator Start()
        {
            app = App.Instance;
            string[] args = Environment.GetCommandLineArgs();
            shots = Path.Combine(Application.persistentDataPath, "shots");
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-shots") shots = args[i + 1];
            Directory.CreateDirectory(shots);
            Application.logMessageReceived += (msg, stack, type) =>
            {
                if (type == LogType.Exception || type == LogType.Error) { errors++; Log("ERROR: " + msg + "\n" + stack); }
            };

            yield return WaitUntil(() => app.Atlas != null && app.CurrentName == "home", 60f, "home");
            yield return new WaitForSeconds(0.6f);
            yield return Shot("01_home");

            // --- tutorial level 1, played through taps, with a mid-game shot
            yield return PlayLevel(1, "02_tutorial", true);

            // --- every secondary screen
            foreach (var name in new[] { "daily", "levels", "collection", "settings", "stats", "freeplay" })
            {
                app.Show(name);
                yield return new WaitForSeconds(0.5f);
                yield return Shot("10_screen_" + name);
            }
            app.Show("home");

            // --- a mid-difficulty level and a hard level, played fully
            foreach (int n in new[] { 8, 40, 100, 150, 300, 600, 1200 })
                yield return PlayLevel(n, "20_level_" + n, n == 40);

            // --- resume: play some moves, flush, leave, come back through the normal entry point
            app.StartJourney(75, false);
            yield return WaitGame(75);
            yield return new WaitForSeconds(1.2f);
            {
                var g2 = app.Get<GameScreen>();
                var sol2 = g2.Session.Level.Solution;
                for (int i = 0; i < 6; i++) { app.Board.TapTile(sol2[2 * i]); app.Board.TapTile(sol2[2 * i + 1]); yield return new WaitForSeconds(0.1f); }
                ulong h = g2.Session.Board.Hash(); int pairs = g2.Session.Pairs;
                g2.FlushProgress();
                app.Show("home");
                yield return new WaitForSeconds(0.3f);
                app.StartJourney(75);
                yield return WaitGame(75);
                yield return new WaitForSeconds(0.5f);
                var g3 = app.Get<GameScreen>();
                Check(g3.Session.Pairs == pairs && g3.Session.Board.Hash() == h, "resume restores the exact board (" + pairs + " pairs)");
            }

            // --- save round trip on disk
            app.Save.Save();
            {
                int hl = app.Save.Data.highestLevel, stars = app.Save.Data.totalStars;
                var again = SaveStore.Load();
                Check(again.Data.highestLevel == hl && again.Data.totalStars == stars && again.Data.achievements.Count == app.Save.Data.achievements.Count,
                      "save file round trip (level " + hl + ", " + stars + " stars)");
                string code = app.Save.Export();
                Check(code.StartsWith("MAHJONG1:") && app.Save.Import(code).StartsWith("Progress loaded"), "export / import code");
            }

            // --- free play board
            app.StartFree(Mix.Fnv1a64("FREE:TEST:6"), 6);
            yield return WaitGame(-1);
            yield return new WaitForSeconds(1.2f);
            yield return PlaySolution("25_free", false);

            // --- hint / undo / shuffle in a real game
            app.StartJourney(30, false);
            yield return WaitGame(30);
            yield return new WaitForSeconds(1.2f);
            var gs = app.Get<GameScreen>();
            int a, c;
            app.Board.DoHint(out a, out c);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("30_hint");
            app.Board.TapTile(a); app.Board.TapTile(c);
            yield return new WaitForSeconds(0.5f);
            Check(app.Board.DoUndo(), "undo after a match");
            app.Board.DoShuffle();
            yield return new WaitForSeconds(0.8f);
            yield return Shot("31_shuffled");
            Check(gs.Session.Board.HasMove(), "shuffle leaves a move");

            // --- 3D picking at several tilts: aiming at a tile's face on screen must pick that tile. At steep angles a taller
            //     stack can genuinely stand in front of it, so a different tile only counts as right when it is on a higher layer.
            {
                var lay = gs.Session.Level.Layout;
                var angles = new[] { new Vector2(0, 0), new Vector2(25, 0), new Vector2(45, 0), new Vector2(60, 0), new Vector2(30, 25), new Vector2(40, -40) };
                foreach (var ang in angles)
                {
                    int tilt = (int)ang.x;
                    app.S.tilt = tilt; app.S.roll = (int)ang.y;
                    app.ApplySettings();                            // as the Settings screen does after any change
                    Check(Mathf.Abs(app.Board.TargetTilt - tilt) < 0.01f && Mathf.Abs(app.Board.TargetRoll - ang.y) < 0.01f,
                          "board follows the tilt and roll settings (" + tilt + ", " + ang.y + ")");
                    yield return new WaitForSeconds(1.0f);          // the board eases to the new tilt
                    int tested = 0, wrong = 0;
                    for (int i = 0; i < lay.Count; i++)
                    {
                        if (!app.Board.IsVisible(i)) continue;
                        bool covered = false;
                        foreach (int up in lay.Above[i]) if (app.Board.IsVisible(up)) covered = true;
                        if (covered) continue;
                        tested++;
                        int hit = app.Board.HitTest(app.Cam.ScreenPointToRay(app.Board.TileScreenPos(i)));
                        if (hit != i && !(hit >= 0 && lay.Pos[hit].Z > lay.Pos[i].Z)) wrong++;
                    }
                    Check(tested > 0 && wrong == 0, "ray picking at tilt " + tilt + " roll " + ang.y + " (" + tested + " tiles, " + wrong + " wrong)");
                    yield return Shot("33_tilt_" + tilt + "_roll_" + (int)ang.y);
                }

                // dragging the background goes through TiltBy: up lays the top back, right pushes the right edge away, limits hold
                app.S.tilt = 20; app.S.roll = 0;
                app.Board.TiltBy(10f, -15f);
                Check(app.S.tilt == 30 && app.S.roll == -15, "drag tilts on both axes (" + app.S.tilt + ", " + app.S.roll + ")");
                app.Board.TiltBy(500f, 500f);
                Check(app.S.tilt == BoardView.MaxTilt && app.S.roll == BoardView.MaxRoll, "tilt and roll stop at their limits");
                app.Board.TiltBy(-500f, -500f);
                Check(app.S.tilt == 0 && app.S.roll == -BoardView.MaxRoll, "tilt and roll stop at their lower limits");
                app.S.tilt = 20; app.S.roll = 0;
                app.ApplySettings();
                yield return new WaitForSeconds(0.8f);
            }

            // --- auto zoom: as tiles come off, the view closes in on the ones that are left (slowly), and picking still works
            {
                app.S.autoZoom = true; app.ApplySettings();
                app.StartJourney(150, false);
                yield return WaitGame(150);
                yield return new WaitForSeconds(1.5f);
                var gz = app.Get<GameScreen>();
                float before = app.Board.ViewScale;
                var solz = gz.Session.Level.Solution;
                int upTo = solz.Length / 2 - 2;                       // take off all but the last two pairs
                // how much smaller the remaining footprint is than the whole board (in half-cell positions, tiles are 2 wide)
                var lzo = gz.Session.Level.Layout;
                int fx0 = int.MaxValue, fx1 = int.MinValue, fy0 = int.MaxValue, fy1 = int.MinValue;
                int rx0 = int.MaxValue, rx1 = int.MinValue, ry0 = int.MaxValue, ry1 = int.MinValue;
                for (int i = 0; i < lzo.Count; i++)
                {
                    var q = lzo.Pos[i];
                    fx0 = Mathf.Min(fx0, q.X); fx1 = Mathf.Max(fx1, q.X); fy0 = Mathf.Min(fy0, q.Y); fy1 = Mathf.Max(fy1, q.Y);
                }
                for (int k = 2 * upTo; k < solz.Length; k++)
                {
                    var q = lzo.Pos[solz[k]];
                    rx0 = Mathf.Min(rx0, q.X); rx1 = Mathf.Max(rx1, q.X); ry0 = Mathf.Min(ry0, q.Y); ry1 = Mathf.Max(ry1, q.Y);
                }
                float shrink = Mathf.Max((rx1 - rx0 + 2f) / (fx1 - fx0 + 2f), (ry1 - ry0 + 2f) / (fy1 - fy0 + 2f));   // 1 = no smaller
                for (int i = 0; i < upTo; i++)
                {
                    app.Board.TapTile(solz[2 * i]); app.Board.TapTile(solz[2 * i + 1]);
                    yield return new WaitForSeconds(0.03f);
                }
                yield return new WaitForSeconds(0.5f);
                float early = app.Board.ViewScale;
                yield return new WaitForSeconds(5f);
                float after = app.Board.ViewScale;
                // the remaining tiles fit in 'shrink' of the board, so the view should close in by a good part of 1/shrink
                // (less when capped, or when the tilt makes the board's depth the limiting size)
                float want = 1f + 0.4f * (Mathf.Min(1f / shrink, AutoZoomCapForTest) - 1f);
                Check(shrink > 0.8f || after > before * want,
                      "auto zoom closes in as tiles come off (" + before.ToString("F1") + " -> " + after.ToString("F1") + " px/unit, remaining footprint " + shrink.ToString("P0") + " of the board)");
                Check(shrink > 0.8f || early < after * 0.999f, "auto zoom is gradual (" + early.ToString("F1") + " half a second in, " + after.ToString("F1") + " settled)");
                int tz = 0, wz = 0;
                var lz = gz.Session.Level.Layout;
                for (int i = 0; i < lz.Count; i++)
                {
                    if (!app.Board.IsVisible(i)) continue;
                    bool covered = false;
                    foreach (int up in lz.Above[i]) if (app.Board.IsVisible(up)) covered = true;
                    if (covered) continue;
                    tz++;
                    int hit = app.Board.HitTest(app.Cam.ScreenPointToRay(app.Board.TileScreenPos(i)));
                    if (hit != i && !(hit >= 0 && lz.Pos[hit].Z > lz.Pos[i].Z)) wz++;
                }
                Check(tz > 0 && wz == 0, "ray picking after auto zoom (" + tz + " tiles, " + wz + " wrong)");
                yield return Shot("34_autozoom");
                // undo takes tiles back, so the view eases back out
                for (int i = 0; i < upTo; i++) app.Board.DoUndo();
                yield return new WaitForSeconds(5f);
                Check(shrink > 0.8f || app.Board.ViewScale < after * 0.97f,"view eases back out after undo (" + after.ToString("F1") + " -> " + app.Board.ViewScale.ToString("F1") + ")");
                app.S.autoZoom = false; app.ApplySettings();
                yield return new WaitForSeconds(4f);
                Check(Mathf.Abs(app.Board.AutoZoomFactor - 1f) < 0.02f, "auto zoom off frames the whole board");
                app.S.autoZoom = true; app.ApplySettings();
                app.Show("home");
                yield return new WaitForSeconds(0.3f);
                app.StartJourney(30, false);        // the stuck-panel check below expects the level 30 board
                yield return WaitGame(30);
                yield return new WaitForSeconds(1.2f);
                gs = app.Get<GameScreen>();
            }

            // --- stuck panel: tap a blocked tile for the nudge, then force a stuck check
            var board = gs.Session.Board;
            for (int i = 0; i < board.Present.Length; i++)
                if (board.Present[i] && !board.IsFree(i)) { app.Board.TapTile(i); break; }
            yield return new WaitForSeconds(0.2f);
            yield return Shot("32_blocked_tap");

            // --- portrait layout
            Screen.SetResolution(540, 960, false);
            yield return new WaitForSeconds(1.2f);
            yield return Shot("40_portrait_game");
            app.Show("home"); yield return new WaitForSeconds(0.4f); yield return Shot("41_portrait_home");
            app.Show("daily"); yield return new WaitForSeconds(0.4f); yield return Shot("42_portrait_daily");
            app.Show("levels"); yield return new WaitForSeconds(0.4f); yield return Shot("43_portrait_levels");
            app.Show("settings"); yield return new WaitForSeconds(0.4f); yield return Shot("44_portrait_settings");
            app.Show("collection"); yield return new WaitForSeconds(0.4f); yield return Shot("45_portrait_collection");
            app.StartJourney(60, false);
            yield return WaitGame(60);
            yield return new WaitForSeconds(1.2f);
            yield return Shot("46_portrait_level60");

            // --- tile sets
            foreach (var st in Content.TileSets)
            {
                app.SetTileSet(st.Id);
                yield return new WaitForSeconds(1.0f);
                yield return Shot("50_tileset_" + st.Id);
            }

            // --- daily challenge, fully played
            Screen.SetResolution(1280, 800, false);
            yield return new WaitForSeconds(1.0f);
            int today = DateUtil.ToInt(DateUtil.TodayUtc());
            app.StartDaily(today, false);
            yield return WaitGame(-1);
            yield return PlaySolution("60_daily");
            Check(app.Save.GetDaily(today) != null, "daily recorded");

            Log("levels won: " + app.Save.Data.levelsWon + "  stars: " + app.Save.Data.totalStars + "  highest: " + app.Save.Data.highestLevel);
            File.WriteAllText(Path.Combine(shots, "autotest.log"), string.Join("\n", log.ToArray()));
            Debug.Log(errors == 0 ? "[AUTOTEST] PASSED" : "[AUTOTEST] FAILED errors=" + errors);
            yield return new WaitForSeconds(0.3f);
            Application.Quit(errors == 0 ? 0 : 1);
        }

        void Check(bool ok, string what)
        {
            if (!ok) { errors++; Log("CHECK FAILED: " + what); }
            else Log("ok: " + what);
        }

        IEnumerator WaitUntil(Func<bool> cond, float timeout, string what)
        {
            float t = 0f;
            while (!cond() && t < timeout) { t += Time.unscaledDeltaTime; yield return null; }
            if (!cond()) { errors++; Log("TIMEOUT waiting for " + what); }
        }

        IEnumerator WaitGame(int level)
        {
            yield return WaitUntil(() => app.CurrentName == "game" && app.Get<GameScreen>().Session != null &&
                                         (level < 0 || app.Get<GameScreen>().Session.Level.Index == level), 30f, "game " + level);
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(shots, name + ".png"));
            yield return null; yield return null;
        }

        IEnumerator PlayLevel(int n, string shotName, bool midShot)
        {
            float t0 = Time.realtimeSinceStartup;
            app.StartJourney(n, false);
            yield return WaitGame(n);
            Log("level " + n + " generated+loaded in " + (Time.realtimeSinceStartup - t0).ToString("F2") + "s");
            yield return new WaitForSeconds(1.3f);
            yield return PlaySolution(shotName, midShot);
        }

        IEnumerator PlaySolution(string shotName, bool midShot = false)
        {
            var gs = app.Get<GameScreen>();
            var sol = gs.Session.Level.Solution;
            int pairs = sol.Length / 2;
            for (int i = 0; i < pairs; i++)
            {
                app.Board.TapTile(sol[2 * i]);
                app.Board.TapTile(sol[2 * i + 1]);
                if (midShot && i == pairs / 2) yield return Shot(shotName + "_mid");
                yield return new WaitForSeconds(0.045f);
            }
            yield return new WaitForSeconds(1.6f);
            Check(gs.Session.IsWon, "level " + gs.Session.Level.Index + " solved by tapping the solution");
            yield return Shot(shotName + "_win");
            app.Show("home");
            yield return new WaitForSeconds(0.2f);
        }
    }
}
