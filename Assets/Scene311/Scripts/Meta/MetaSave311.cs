using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[Serializable]
public class Mission311
{
    public string id;                 // template id
    public int kind;                  // MissionKind311
    public int difficulty;
    public int target;
    public int progress;
    public int runBest;               // one-run missions: this run's count
    public int coins;
    public bool claimed;
    public string text;
    public bool Done => progress >= target;
}

/// <summary>
/// The 3.11 meta save (persistentDataPath/meta311.json): daily calendar and streak, daily and weekly missions, awards,
/// notification consent and the learned play hour. Versioned; Migrate() brings older files up to date. Written on every
/// change and on app pause / quit (atomic: .tmp then replace, the old one kept as .bak). Coins never live here - every
/// reward goes straight into the shop wallet (SaveSystem.AddCoins) when it is claimed.
/// </summary>
[Serializable]
public class MetaSave311
{
    public const int CurrentVersion = 2;

    public int version = CurrentVersion;

    // clock (anti-cheat): the latest UTC time ever seen; nothing is granted while the clock is behind it
    public long lastUtcTicks;
    public long devOffsetTicks;       // dev menu only

    // daily reward
    public int lastClaimDay;          // yyyymmdd (local) of the last claim, 0 = never
    public int streak;                // consecutive days (grace included)
    public int bestStreak;
    public int graceUsedWeek;         // week key (Monday yyyymmdd) whose grace day is spent
    public int dailyShownDay;         // the popup's last day (shown once per day)
    public int dailyClaims;

    // missions
    public int missionsDay;
    public List<Mission311> daily = new List<Mission311>();
    public int rerollsUsedDay;
    public int rerollsUsed;
    public int weeklyWeek;
    public List<Mission311> weekly = new List<Mission311>();

    // awards: unlocked (claimable) and claimed ids
    public List<string> awardsUnlocked = new List<string>();
    public List<string> awardsClaimed = new List<string>();

    // lifetime stats (for awards)
    public int bestFreefall;
    public int levelsClearedClean;

    // notifications (Phase 5)
    public bool notifyEnabled;        // the pause-settings toggle
    public int permission;            // 0 unknown, 1 granted, 2 denied
    public int softPromptState;       // 0 never shown, 1 "not now" once, 2 answered for good / asked twice
    public long softPromptAtTicks;    // when "not now" was answered (UTC)
    public bool promptTriggered;      // day-2 claim or first L3 clear happened: show the soft prompt at the next calm moment
    public int[] playHours = new int[24];
    public int lastPlayDay;
    public int days;                  // distinct days played

    // v1 had no weekly list / play hours; v2 adds them
    public void Migrate()
    {
        if (daily == null) daily = new List<Mission311>();
        if (weekly == null) weekly = new List<Mission311>();
        if (awardsUnlocked == null) awardsUnlocked = new List<string>();
        if (awardsClaimed == null) awardsClaimed = new List<string>();
        if (playHours == null || playHours.Length != 24) { var h = new int[24]; if (playHours != null) Array.Copy(playHours, h, Mathf.Min(24, playHours.Length)); playHours = h; }
        version = CurrentVersion;
    }

    public static string FilePath => Path.Combine(Application.persistentDataPath, "meta311.json");

    public static MetaSave311 Load()
    {
        foreach (string p in new[] { FilePath, FilePath + ".bak" })
        {
            try
            {
                if (!File.Exists(p)) continue;
                string json = File.ReadAllText(p, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) continue;
                var s = JsonUtility.FromJson<MetaSave311>(json);
                if (s == null) continue;
                s.Migrate();
                return s;
            }
            catch (Exception e) { Debug.LogWarning("[Meta311] could not read " + p + ": " + e.Message); }
        }
        return new MetaSave311();
    }

    public void Save()
    {
        try
        {
            string tmp = FilePath + ".tmp", bak = FilePath + ".bak";
            File.WriteAllText(tmp, JsonUtility.ToJson(this), Encoding.UTF8);
            if (File.Exists(FilePath)) { if (File.Exists(bak)) File.Delete(bak); File.Move(FilePath, bak); }
            File.Move(tmp, FilePath);
        }
        catch (Exception e) { Debug.LogWarning("[Meta311] could not save: " + e.Message); }
    }
}

/// <summary>
/// The meta clock: local midnight days, Monday weeks, and the anti-cheat rule - the latest UTC time ever seen is kept,
/// and while the device clock is behind it (by more than a small sync tolerance) nothing is granted.
/// A dev offset (DevMenu311) shifts "now" for testing.
/// </summary>
public static class MetaClock311
{
    public static TimeSpan DevOffset;

    public static DateTime UtcNow => DateTime.UtcNow + DevOffset;
    public static DateTime LocalNow => DateTime.Now + DevOffset;

    public static int DayKey(DateTime local) => local.Year * 10000 + local.Month * 100 + local.Day;
    public static int Today => DayKey(LocalNow);

    public static DateTime FromKey(int key) => new DateTime(key / 10000, key / 100 % 100, key % 100);

    /// <summary>Whole days from day a to day b (local dates).</summary>
    public static int DaysBetween(int a, int b) => a <= 0 || b <= 0 ? int.MaxValue : (int)(FromKey(b) - FromKey(a)).TotalDays;

    /// <summary>The Monday (yyyymmdd) of the week a day is in.</summary>
    public static int WeekKey(DateTime local)
    {
        int back = ((int)local.DayOfWeek + 6) % 7;   // Monday = 0
        return DayKey(local.Date.AddDays(-back));
    }
    public static int ThisWeek => WeekKey(LocalNow);
}
