using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Mahjong.Game
{
    [Serializable] public class LevelRecord { public int level, stars, bestSeconds; public bool noShuffle; }
    [Serializable] public class DailyRecord { public int day, stars, seconds; public bool gold; }

    [Serializable]
    public class SettingsData
    {
        public float sound = 1f;
        public float music = 0.3f;
        public bool haptics = true;
        public int tileSize = 0;          // 0 fit to screen, 1 large, 2 extra large
        public bool leftHanded;
        public bool reducedMotion;
        public int tilt = 20;             // board tilt in degrees: 0 looks straight down, up to BoardView.MaxTilt
        public int roll;                  // sideways tilt, -BoardView.MaxRoll..MaxRoll; positive pushes the right edge away
        public bool autoZoom = true;      // slowly zoom in as the remaining tiles take up less of the board
        public int textScale;            // 0 = 100%, 1 = 125%, 2 = 150%
        public int dimBlocked = 1;        // 0 off, 1 subtle, 2 strong
        public bool showBlockers = true;
        public bool matchAssist;
        public bool confirmRestart = true;
        public bool autoContinue;
        public int difficultyOffset;      // -2 .. +2
        public string tileSet = "classic";
        public string background = "random";
    }

    [Serializable]
    public class InProgress
    {
        public string mode;               // journey | daily
        public int level;                 // journey level
        public int day;                   // daily yyyymmdd
        public int offset;
        public int gen;
        public int seconds;
        public List<int> log = new List<int>();
    }

    [Serializable]
    public class SaveData
    {
        public int schemaVersion = 1;
        public int generatorVersion = 1;
        public bool randomBgMigrated;     // the old default "felt" is replaced by "random" once
        public int currentLevel = 1;
        public int highestLevel;
        public bool tutorialDone;
        public List<LevelRecord> levels = new List<LevelRecord>();
        public List<DailyRecord> daily = new List<DailyRecord>();
        public List<int> badges = new List<int>();            // yyyymm
        public int totalStars;
        public int levelsWon, pairsRemoved, bestCombo, secondsPlayed, noShuffleWins, longestStreak, dailiesWon;
        public List<string> achievements = new List<string>();
        public SettingsData settings = new SettingsData();
        public InProgress inProgress;
        public string savedUtc;
    }

    public static class DateUtil
    {
        public static DateTime TodayUtc() { return DateTime.UtcNow.Date; }
        public static int ToInt(DateTime d) { return d.Year * 10000 + d.Month * 100 + d.Day; }
        public static DateTime FromInt(int v) { return new DateTime(v / 10000, (v / 100) % 100, v % 100, 0, 0, 0, DateTimeKind.Utc); }
        public static int DayNumber(DateTime d) { return (int)(d.Date - new DateTime(2020, 1, 1)).TotalDays; }
        public static int WeekdayMondayZero(DateTime d) { return ((int)d.DayOfWeek + 6) % 7; }
    }

    public sealed class AchievementDef
    {
        public string Id, Title, Desc;
        public Func<SaveData, bool> Check;
        public AchievementDef(string id, string title, string desc, Func<SaveData, bool> check) { Id = id; Title = title; Desc = desc; Check = check; }
    }

    public static class Achievements
    {
        public static readonly AchievementDef[] All =
        {
            new AchievementDef("win_1", "First Steps", "Clear your first level", d => d.levelsWon >= 1),
            new AchievementDef("win_10", "Warming Up", "Clear 10 levels", d => d.levelsWon >= 10),
            new AchievementDef("win_50", "Regular", "Clear 50 levels", d => d.levelsWon >= 50),
            new AchievementDef("win_100", "Centurion", "Clear 100 levels", d => d.levelsWon >= 100),
            new AchievementDef("win_500", "Marathon", "Clear 500 levels", d => d.levelsWon >= 500),
            new AchievementDef("stars_50", "Star Collector", "Earn 50 stars", d => d.totalStars >= 50),
            new AchievementDef("stars_300", "Constellation", "Earn 300 stars", d => d.totalStars >= 300),
            new AchievementDef("clean_25", "Clean Sweep", "Win 25 levels without shuffling", d => d.noShuffleWins >= 25),
            new AchievementDef("combo_5", "In The Flow", "Reach a x5 combo", d => d.bestCombo >= 5),
            new AchievementDef("daily_1", "Daily Habit", "Complete a daily challenge", d => d.dailiesWon >= 1),
            new AchievementDef("daily_10", "Dedicated", "Complete 10 daily challenges", d => d.dailiesWon >= 10),
            new AchievementDef("streak_7", "Week Streak", "Complete the daily 7 days in a row", d => d.longestStreak >= 7),
            new AchievementDef("streak_30", "Month Streak", "Complete the daily 30 days in a row", d => d.longestStreak >= 30),
            new AchievementDef("badge_1", "Calendar Complete", "Finish every daily in a month", d => d.badges.Count >= 1),
            new AchievementDef("lvl_100", "Deep Dive", "Reach level 100", d => d.highestLevel >= 100),
        };
    }

    /// <summary>Local, versioned JSON save with atomic writes and a backup copy.</summary>
    public sealed class SaveStore
    {
        public SaveData Data;
        readonly string path, tmp, bak;
        readonly Dictionary<int, LevelRecord> levelIdx = new Dictionary<int, LevelRecord>();
        readonly Dictionary<int, DailyRecord> dayIdx = new Dictionary<int, DailyRecord>();
        public bool LoadedFromBackup, StartedFresh;
        public static string OverrideDir;

        SaveStore(string dir)
        {
            path = Path.Combine(dir, "save.json");
            tmp = Path.Combine(dir, "save.json.tmp");
            bak = Path.Combine(dir, "save.bak");
        }

        public static SaveStore Load()
        {
            string dir = OverrideDir ?? Application.persistentDataPath;
            try { Directory.CreateDirectory(dir); } catch (Exception) { }
            var s = new SaveStore(dir);
            SaveData d = s.TryRead(s.path);
            if (d == null) { d = s.TryRead(s.bak); s.LoadedFromBackup = d != null; }
            if (d == null)
            {
                d = new SaveData();
                d.settings.music = Application.isMobilePlatform ? 0.3f : 0f;
                s.StartedFresh = true;
            }
            s.Data = d;
            s.Migrate();
            s.Reindex();
            return s;
        }

        SaveData TryRead(string p)
        {
            try
            {
                if (!File.Exists(p)) return null;
                var d = JsonUtility.FromJson<SaveData>(File.ReadAllText(p));
                if (d == null || d.settings == null) return null;
                return d;
            }
            catch (Exception) { return null; }
        }

        void Migrate()
        {
            if (Data.levels == null) Data.levels = new List<LevelRecord>();
            if (Data.daily == null) Data.daily = new List<DailyRecord>();
            if (Data.badges == null) Data.badges = new List<int>();
            if (Data.achievements == null) Data.achievements = new List<string>();
            if (Data.currentLevel < 1) Data.currentLevel = 1;
            if (!Data.randomBgMigrated)
            {
                if (Data.settings.background == "felt") Data.settings.background = "random";
                Data.randomBgMigrated = true;
            }
            Data.schemaVersion = 1;
        }

        void Reindex()
        {
            levelIdx.Clear(); dayIdx.Clear();
            foreach (var l in Data.levels) levelIdx[l.level] = l;
            foreach (var r in Data.daily) dayIdx[r.day] = r;
        }

        public void Save()
        {
            try
            {
                Data.savedUtc = DateTime.UtcNow.ToString("o");
                File.WriteAllText(tmp, JsonUtility.ToJson(Data));
                if (File.Exists(path))
                {
                    File.Copy(path, bak, true);
                    File.Delete(path);
                }
                File.Move(tmp, path);
            }
            catch (Exception e) { Debug.LogWarning("Save failed: " + e.Message); }
        }

        public string Export() { return "MAHJONG1:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(Data))); }

        /// <summary>Replaces the save when the imported one is at least as far along. Returns a message.</summary>
        public string Import(string text)
        {
            try
            {
                text = (text ?? "").Trim();
                if (!text.StartsWith("MAHJONG1:")) return "That is not a Mahjong save code.";
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(text.Substring(9)));
                var d = JsonUtility.FromJson<SaveData>(json);
                if (d == null || d.settings == null) return "That save code could not be read.";
                if (d.highestLevel < Data.highestLevel || (d.highestLevel == Data.highestLevel && d.totalStars < Data.totalStars))
                    return "Kept your current progress because it is further along.";
                Data = d; Migrate(); Reindex(); Save();
                return "Progress loaded. Level " + Data.currentLevel + ", " + Data.totalStars + " stars.";
            }
            catch (Exception) { return "That save code could not be read."; }
        }

        // ---------------------------------------------------------------- queries

        public LevelRecord GetLevel(int n) { LevelRecord r; return levelIdx.TryGetValue(n, out r) ? r : null; }
        public DailyRecord GetDaily(int yyyymmdd) { DailyRecord r; return dayIdx.TryGetValue(yyyymmdd, out r) ? r : null; }
        public int TotalStars { get { return Data.totalStars; } }

        public bool IsUnlocked(Unlock u) { return u.IsMet(Data.totalStars, Data.highestLevel); }

        /// <summary>Consecutive completed days ending today (or yesterday if today is not done yet).</summary>
        public int CurrentStreak()
        {
            var day = DateUtil.TodayUtc();
            if (GetDaily(DateUtil.ToInt(day)) == null) day = day.AddDays(-1);
            int n = 0;
            while (GetDaily(DateUtil.ToInt(day)) != null) { n++; day = day.AddDays(-1); }
            return n;
        }

        // ---------------------------------------------------------------- updates

        public List<AchievementDef> RecordJourneyWin(int level, int stars, int seconds, bool noShuffle, int pairs, int combo)
        {
            var rec = GetLevel(level);
            if (rec == null)
            {
                rec = new LevelRecord { level = level, stars = stars, bestSeconds = seconds, noShuffle = noShuffle };
                Data.levels.Add(rec); levelIdx[level] = rec;
                Data.totalStars += stars;
                Data.levelsWon++;
                if (noShuffle) Data.noShuffleWins++;
            }
            else
            {
                if (stars > rec.stars) { Data.totalStars += stars - rec.stars; rec.stars = stars; }
                if (seconds > 0 && (rec.bestSeconds <= 0 || seconds < rec.bestSeconds)) rec.bestSeconds = seconds;
                if (noShuffle && !rec.noShuffle) { rec.noShuffle = true; Data.noShuffleWins++; }
            }
            if (level > Data.highestLevel) Data.highestLevel = level;
            if (level >= Data.currentLevel) Data.currentLevel = level + 1;
            Data.pairsRemoved += pairs;
            if (combo > Data.bestCombo) Data.bestCombo = combo;
            Data.secondsPlayed += seconds;
            Data.inProgress = null;
            var fresh = CheckAchievements();
            Save();
            return fresh;
        }

        public List<AchievementDef> RecordDailyWin(int yyyymmdd, int stars, int seconds, bool gold, int pairs, int combo)
        {
            var rec = GetDaily(yyyymmdd);
            if (rec == null)
            {
                rec = new DailyRecord { day = yyyymmdd, stars = stars, seconds = seconds, gold = gold };
                Data.daily.Add(rec); dayIdx[yyyymmdd] = rec;
                Data.totalStars += stars;
                Data.dailiesWon++;
            }
            else
            {
                if (stars > rec.stars) { Data.totalStars += stars - rec.stars; rec.stars = stars; }
                if (gold) rec.gold = true;
                if (seconds > 0 && seconds < rec.seconds) rec.seconds = seconds;
            }
            Data.pairsRemoved += pairs;
            if (combo > Data.bestCombo) Data.bestCombo = combo;
            Data.secondsPlayed += seconds;
            int streak = CurrentStreak();
            if (streak > Data.longestStreak) Data.longestStreak = streak;
            // monthly badge
            var d = DateUtil.FromInt(yyyymmdd);
            int ym = d.Year * 100 + d.Month;
            int dim = DateTime.DaysInMonth(d.Year, d.Month);
            bool all = true;
            for (int i = 1; i <= dim; i++) if (GetDaily(ym * 100 + i) == null) { all = false; break; }
            if (all && !Data.badges.Contains(ym)) Data.badges.Add(ym);
            Data.inProgress = null;
            var fresh = CheckAchievements();
            Save();
            return fresh;
        }

        public List<AchievementDef> CheckAchievements()
        {
            var fresh = new List<AchievementDef>();
            foreach (var a in Achievements.All)
            {
                if (Data.achievements.Contains(a.Id)) continue;
                if (a.Check(Data)) { Data.achievements.Add(a.Id); fresh.Add(a); }
            }
            return fresh;
        }
    }
}
