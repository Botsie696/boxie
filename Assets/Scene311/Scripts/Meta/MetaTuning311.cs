using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What a mission counts (scene 3.11).</summary>
public enum MissionKind311
{
    CoinsInRun, ReachPortal, RunwayNoRescue, FinishBoosts, CloseCalls, PlayRuns, ClearStartCheckpoint, FreefallScore,
    FinishSlowMo, SurviveDecoys, TwoRollCorners, LegsInRun, ClearCheckpoints, CoinsTotal, BrokenBoostClean,
    SliderRunway, ShieldSaves, DailyClaims,
}

[Serializable]
public class MissionTemplate311
{
    public string id;
    public MissionKind311 kind;
    [Tooltip("0 easy, 1 medium, 2 hard (daily); weekly templates use 0..2 the same way.")]
    public int difficulty;
    public bool weekly;
    [Tooltip("Text, {0} = the target.")]
    public string text;
    public int baseTarget = 1;
    [Tooltip("Target x (1 + this x (highest unlocked checkpoint - 1)).")]
    public float perCheckpoint = 0.1f;
    [Tooltip("Only offered once this checkpoint is unlocked (10 = freefall).")]
    public int minCheckpoint = 1;
    [Tooltip("Counts within one run (the best run wins), not across runs.")]
    public bool oneRun;
    [Tooltip("Target scales with the skill tier (Struggling x0.7 .. Expert x1.3).")]
    public bool tierScaled = true;
    public int maxTarget = 999999;
}

[Serializable]
public class AwardDef311
{
    public string id;
    public string title;
    public string text;
    public int coins;
}

/// <summary>
/// Every number of the 3.11 meta systems: the 7-day calendar, mission rewards and pool, weekly missions, awards,
/// toasts and the notification texts / timings. Asset: Assets/Scene311/Data/MetaTuning311.asset.
/// </summary>
[CreateAssetMenu(menuName = "Runner/Scene 3.11 Meta Tuning", fileName = "MetaTuning311")]
public class MetaTuning311 : ScriptableObject
{
    [Header("Daily reward (7-day calendar, then it repeats)")]
    public int[] dailyCoins = { 50, 75, 100, 150, 200, 250, 500 };
    [Tooltip("Missed days per week that still keep the streak.")]
    public int graceDaysPerWeek = 1;

    [Header("Daily missions")]
    public int[] missionCoins = { 50, 100, 150 };      // easy, medium, hard
    public int freeRerollsPerDay = 1;
    [Header("Weekly missions")]
    public int[] weeklyCoins = { 300, 500, 800 };
    [Tooltip("Mission targets x this by skill tier, Struggling .. Expert.")]
    public float[] tierTargetScale = { 0.7f, 0.85f, 1f, 1.15f, 1.3f };

    [Header("Clock")]
    [Tooltip("The clock may run back this many minutes (time sync) before it counts as moved backwards.")]
    public float clockToleranceMinutes = 5f;

    [Header("UI")]
    public float toastSeconds = 2.4f;

    [Header("Notifications")]
    public Vector2Int notifyHourWindow = new Vector2Int(10, 21);
    public int defaultPlayHour = 18;
    public int streakRiskHour = 19;
    public int streakRiskMinStreak = 3;
    public int[] reengageDays = { 1, 3, 7 };
    public int softPromptRepeatDays = 7;
    public string androidChannelId = "daily";
    public string androidChannelName = "Daily reward";
    public string titleDaily = "Your daily reward is ready";
    public string textDaily = "Day {0} reward: {1} coins is waiting";
    public string titleStreak = "Keep your {0}-day streak";
    public string textStreak = "Day {1} reward: {2} coins is waiting - the streak ends tonight";
    public string titleReengage = "The path is waiting";
    public string textReengage = "Day {0} reward: {1} coins is waiting";
    public string softPromptText = "Get a reminder when your daily reward is ready?";

    [Header("Mission pool (15+). Edit freely; ids must stay unique.")]
    public List<MissionTemplate311> missions = DefaultMissions();

    [Header("Awards (one-time, with coins)")]
    public List<AwardDef311> awards = DefaultAwards();

    public int TierIndex(float tier) => Mathf.Clamp(Mathf.RoundToInt(tier) + 2, 0, 4);

    public static List<MissionTemplate311> DefaultMissions() => new List<MissionTemplate311>
    {
        // daily, easy
        new MissionTemplate311 { id = "d_runs",      kind = MissionKind311.PlayRuns,        difficulty = 0, text = "Play {0} runs",                         baseTarget = 3, perCheckpoint = 0f, tierScaled = false },
        new MissionTemplate311 { id = "d_coins1",    kind = MissionKind311.CoinsInRun,      difficulty = 0, text = "Collect {0} coins in one run",          baseTarget = 20, perCheckpoint = 0.15f, oneRun = true },
        new MissionTemplate311 { id = "d_close1",    kind = MissionKind311.CloseCalls,      difficulty = 0, text = "Make {0} close calls",                  baseTarget = 3, perCheckpoint = 0.1f },
        new MissionTemplate311 { id = "d_legs1",     kind = MissionKind311.LegsInRun,       difficulty = 0, text = "Pass {0} legs in one run",              baseTarget = 25, perCheckpoint = 0.12f, oneRun = true },
        new MissionTemplate311 { id = "d_twoRoll1",  kind = MissionKind311.TwoRollCorners,  difficulty = 0, text = "Take {0} half-turn corners",            baseTarget = 15, perCheckpoint = 0.1f },
        // daily, medium
        new MissionTemplate311 { id = "d_boosts",    kind = MissionKind311.FinishBoosts,    difficulty = 1, text = "Finish {0} boosts",                     baseTarget = 2, perCheckpoint = 0.1f, minCheckpoint = 2 },
        new MissionTemplate311 { id = "d_runway",    kind = MissionKind311.RunwayNoRescue,  difficulty = 1, text = "Clear {0} obstacle runways with no rescue", baseTarget = 1, perCheckpoint = 0.05f, minCheckpoint = 2, maxTarget = 3 },
        new MissionTemplate311 { id = "d_decoys",    kind = MissionKind311.SurviveDecoys,   difficulty = 1, text = "Get past {0} decoy forks",              baseTarget = 4, perCheckpoint = 0.1f, minCheckpoint = 2 },
        new MissionTemplate311 { id = "d_coins2",    kind = MissionKind311.CoinsInRun,      difficulty = 1, text = "Collect {0} coins in one run",          baseTarget = 45, perCheckpoint = 0.15f, oneRun = true },
        new MissionTemplate311 { id = "d_slowmo",    kind = MissionKind311.FinishSlowMo,    difficulty = 1, text = "Get through {0} slow-motion sections",  baseTarget = 1, perCheckpoint = 0.05f, minCheckpoint = 3, maxTarget = 3 },
        // daily, hard
        new MissionTemplate311 { id = "d_portal",    kind = MissionKind311.ReachPortal,     difficulty = 2, text = "Reach portal {0}",                      baseTarget = 1, perCheckpoint = 0.9f, oneRun = true, tierScaled = false, maxTarget = 9 },
        new MissionTemplate311 { id = "d_clearStart",kind = MissionKind311.ClearStartCheckpoint, difficulty = 2, text = "Clear the checkpoint you start from", baseTarget = 1, perCheckpoint = 0f, tierScaled = false, maxTarget = 1 },
        new MissionTemplate311 { id = "d_legs2",     kind = MissionKind311.LegsInRun,       difficulty = 2, text = "Pass {0} legs in one run",              baseTarget = 60, perCheckpoint = 0.12f, oneRun = true },
        new MissionTemplate311 { id = "d_close2",    kind = MissionKind311.CloseCalls,      difficulty = 2, text = "Make {0} close calls",                  baseTarget = 10, perCheckpoint = 0.1f },
        new MissionTemplate311 { id = "d_broken",    kind = MissionKind311.BrokenBoostClean,difficulty = 2, text = "Finish a broken-rhythm boost with no rescue", baseTarget = 1, perCheckpoint = 0f, minCheckpoint = 5, tierScaled = false, maxTarget = 1 },
        new MissionTemplate311 { id = "d_slider",    kind = MissionKind311.SliderRunway,    difficulty = 2, text = "Clear {0} runways with sliding pillars", baseTarget = 1, perCheckpoint = 0.05f, minCheckpoint = 4, maxTarget = 3 },
        new MissionTemplate311 { id = "d_freefall",  kind = MissionKind311.FreefallScore,   difficulty = 2, text = "Reach {0} in freefall",                 baseTarget = 30, perCheckpoint = 0f, minCheckpoint = 10, oneRun = true },
        // weekly
        new MissionTemplate311 { id = "w_clears",    kind = MissionKind311.ClearCheckpoints, difficulty = 0, weekly = true, text = "Clear {0} checkpoints", baseTarget = 5, perCheckpoint = 0f, tierScaled = false },
        new MissionTemplate311 { id = "w_runs",      kind = MissionKind311.PlayRuns,        difficulty = 0, weekly = true, text = "Play {0} runs",          baseTarget = 30, perCheckpoint = 0f, tierScaled = false },
        new MissionTemplate311 { id = "w_close",     kind = MissionKind311.CloseCalls,      difficulty = 1, weekly = true, text = "Make {0} close calls",   baseTarget = 50, perCheckpoint = 0.05f },
        new MissionTemplate311 { id = "w_boosts",    kind = MissionKind311.FinishBoosts,    difficulty = 1, weekly = true, text = "Finish {0} boosts",      baseTarget = 20, perCheckpoint = 0.05f, minCheckpoint = 2 },
        new MissionTemplate311 { id = "w_coins",     kind = MissionKind311.CoinsTotal,      difficulty = 2, weekly = true, text = "Collect {0} coins",      baseTarget = 2000, perCheckpoint = 0.05f },
        new MissionTemplate311 { id = "w_daily",     kind = MissionKind311.DailyClaims,     difficulty = 2, weekly = true, text = "Claim the daily reward {0} times", baseTarget = 5, perCheckpoint = 0f, tierScaled = false },
    };

    public static List<AwardDef311> DefaultAwards()
    {
        var a = new List<AwardDef311>
        {
            new AwardDef311 { id = "first_portal", title = "Through the door", text = "Cross your first portal", coins = 100 },
        };
        string[] names = { "Start", "Crimson", "Toon", "Pixel", "Storybook", "Blossom", "Brick Meadow", "Sketchbook", "Rogue Planet" };
        for (int i = 1; i <= 9; i++)
            a.Add(new AwardDef311 { id = "clear_L" + i, title = names[i - 1] + " cleared", text = "Clear level " + i + " for the first time", coins = 100 * i });
        a.Add(new AwardDef311 { id = "clean_level", title = "Untouched", text = "Clear a level with no rescue", coins = 300 });
        a.Add(new AwardDef311 { id = "perfect_b3", title = "Off-beat", text = "A perfect broken-rhythm boost", coins = 250 });
        a.Add(new AwardDef311 { id = "streak_7", title = "One week", text = "A 7-day streak", coins = 500 });
        a.Add(new AwardDef311 { id = "streak_30", title = "One month", text = "A 30-day streak", coins = 2000 });
        a.Add(new AwardDef311 { id = "freefall", title = "Freefall", text = "Reach Freefall", coins = 1000 });
        int[] ff = { 25, 50, 100, 200 };
        foreach (int s in ff) a.Add(new AwardDef311 { id = "ff_" + s, title = "Freefall " + s, text = "Score " + s + " in Freefall", coins = s * 5 });
        string[] modes = { "Classic boost", "Long boost", "Broken-rhythm boost", "Boost exit" };
        for (int i = 0; i < 4; i++) a.Add(new AwardDef311 { id = "boost_" + i, title = modes[i], text = "Survive your first " + modes[i].ToLower(), coins = 50 + 25 * i });
        string[] designs = { "Pillar", "Pillar pair", "L corner", "Pillar fence", "Sliding pillar", "Twin sliders", "Sliding L" };
        for (int i = 0; i < 7; i++) a.Add(new AwardDef311 { id = "design_" + i, title = designs[i], text = "Get through your first " + designs[i].ToLower(), coins = 50 + 25 * i });
        return a;
    }
}
