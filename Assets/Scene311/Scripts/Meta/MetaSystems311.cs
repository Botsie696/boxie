using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SCENE 3.11 DAILY RETURN, MISSIONS, AWARDS (logic; MetaUI311 draws it). On the Scene311Systems object.
///
///   Daily reward   a 7-day calendar (MetaTuning311.dailyCoins, then it repeats), local-midnight days; one grace day a
///                  week keeps the streak, a second missed day resets it. Offered once per day at the first scene start.
///   Missions       3 a day (easy / medium / hard) from the pool, scaled by the highest unlocked checkpoint and the skill
///                  tier, one free reroll a day; 3 a week (Monday 00:00 local). Progress from the game's own events.
///   Awards         ~30 one-time awards with coins (first portal, first clear of each level, clean clears, perfect B3,
///                  streaks, freefall scores, every boost mode and runway design survived once).
///   Clock          the latest UTC time ever seen is saved; while the device clock is behind it nothing is granted.
///   Coins          every claim goes into the shop wallet (SaveSystem.AddCoins). Claims only on the game-over panel or in
///                  the Hub (paused / between runs) - never mid-run. Mid-run, completions are only toasts.
/// Saved to meta311.json on every change and on app pause / quit.
/// </summary>
[DefaultExecutionOrder(-700)]
public class MetaSystems311 : MonoBehaviour
{
    public MetaTuning311 tuning;

    public static MetaSystems311 Instance { get; private set; }
    public MetaSave311 Data { get; private set; }
    public bool ClockRolledBack { get; private set; }

    public event Action Changed;
    public event Action<string> Toast;
    /// <summary>The soft notification prompt should be shown at the next calm moment.</summary>
    public event Action PromptDue;

    DynamicPathGenerator gen;
    CubeRailRunner runner;
    CheckpointRunController crc;
    CoinCollector collector;
    PauseController pause;
    SkillProfile311 skill;
    bool dirty;
    int lastCoins, runLegs, levelRescues, levelShields;
    int boostId = -1, boostMode;
    bool boostClean, inBoost, inSlowMo;
    float nextClockCheck;

    public void Init(MetaTuning311 t)
    {
        if (Data != null) return;
        tuning = t != null ? t : (tuning != null ? tuning : ScriptableObject.CreateInstance<MetaTuning311>());
        Data = MetaSave311.Load();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        MetaClock311.DevOffset = TimeSpan.FromTicks(Data.devOffsetTicks);
#endif
        CheckClock();
        NoteSessionStart();
        Refresh();
    }

    void Awake() { Instance = this; }

    void Start()
    {
        Init(tuning);
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        runner = FindFirstObjectByType<CubeRailRunner>();
        crc = FindFirstObjectByType<CheckpointRunController>();
        pause = FindFirstObjectByType<PauseController>();
        skill = SkillProfile311.Instance;
        if (runner != null) { runner.Rescued += OnRescued; runner.ShieldSaved += OnShield; collector = runner.GetComponent<CoinCollector>(); }
        if (skill != null)
        {
            skill.RunStarted += OnRunStarted;
            skill.LegDone += OnLegDone;
            skill.CheckpointCleared += OnCleared;
            skill.RunDied += OnDied;
        }
    }

    void OnDestroy()
    {
        if (runner != null) { runner.Rescued -= OnRescued; runner.ShieldSaved -= OnShield; }
        if (skill != null) { skill.RunStarted -= OnRunStarted; skill.LegDone -= OnLegDone; skill.CheckpointCleared -= OnCleared; skill.RunDied -= OnDied; }
        SaveNow();
        if (Instance == this) Instance = null;
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) SaveNow();
        else { CheckClock(); Refresh(); }
    }
    void OnApplicationQuit() => SaveNow();

    // ---------------------------------------------------------------------- clock + day rollover

    void CheckClock()
    {
        long now = MetaClock311.UtcNow.Ticks;
        long tol = TimeSpan.FromMinutes(tuning.clockToleranceMinutes).Ticks;
        bool back = Data.lastUtcTicks > 0 && now < Data.lastUtcTicks - tol;
        if (back != ClockRolledBack) { ClockRolledBack = back; Changed?.Invoke(); }
        if (!back && now > Data.lastUtcTicks) { Data.lastUtcTicks = now; dirty = true; }
    }

    void NoteSessionStart()
    {
        DateTime local = MetaClock311.LocalNow;
        Data.playHours[local.Hour]++;
        int today = MetaClock311.DayKey(local);
        if (Data.lastPlayDay != today) { Data.days++; Data.lastPlayDay = today; }
        dirty = true;
    }

    /// <summary>New day / new week: fresh missions (never while the clock is behind).</summary>
    public void Refresh()
    {
        if (ClockRolledBack) return;
        int today = MetaClock311.Today;
        if (Data.missionsDay != today) { GenerateDaily(today); Data.missionsDay = today; dirty = true; }
        int week = MetaClock311.ThisWeek;
        if (Data.weeklyWeek != week) { GenerateWeekly(week); Data.weeklyWeek = week; dirty = true; }
        if (dirty) Changed?.Invoke();
    }

    void Update()
    {
        if (Time.unscaledTime >= nextClockCheck)
        {
            nextClockCheck = Time.unscaledTime + 20f;
            CheckClock();
            Refresh();
        }
        TrackCoinsAndFreefall();
        if (dirty) SaveNow();
    }

    void SaveNow()
    {
        if (Data == null || !dirty) return;
        if (SkillProfile311.RecordingOff) { dirty = false; return; }   // bot tests leave the save alone
        Data.Save();
        dirty = false;
    }

    // ---------------------------------------------------------------------- state queries

    /// <summary>Claims are allowed between runs, on the game-over panel and while paused - never during play.</summary>
    public bool CanClaim => !ClockRolledBack && (runner == null || !runner.IsRunning || runner.IsGameOver || (pause != null && pause.Current == PauseController.State.Paused));

    public bool DailyAvailable => !ClockRolledBack && Data.lastClaimDay != MetaClock311.Today;

    /// <summary>The streak the next claim would make (grace / reset applied).</summary>
    public int NextStreak(out bool usesGrace)
    {
        usesGrace = false;
        int today = MetaClock311.Today;
        if (Data.lastClaimDay <= 0) return 1;
        int gap = MetaClock311.DaysBetween(Data.lastClaimDay, today);
        if (gap <= 0) return Data.streak;
        if (gap == 1) return Data.streak + 1;
        if (gap == 2 && tuning.graceDaysPerWeek > 0 && Data.graceUsedWeek != MetaClock311.ThisWeek) { usesGrace = true; return Data.streak + 1; }
        return 1;
    }

    public int DayIndexFor(int streak) => (Mathf.Max(1, streak) - 1) % Mathf.Max(1, tuning.dailyCoins.Length);
    public int DailyCoinsFor(int streak) => tuning.dailyCoins[DayIndexFor(streak)];

    public int ClaimableCount
    {
        get
        {
            int n = DailyAvailable ? 1 : 0;
            foreach (var m in Data.daily) if (m.Done && !m.claimed) n++;
            foreach (var m in Data.weekly) if (m.Done && !m.claimed) n++;
            n += Data.awardsUnlocked.Count;
            return n;
        }
    }

    // ---------------------------------------------------------------------- claims (coins -> the shop wallet)

    void Pay(int coins)
    {
        if (coins <= 0) return;
        SaveSystem.AddCoins(coins);
        SaveSystem.Flush();
    }

    public int ClaimDaily()
    {
        if (!DailyAvailable) return 0;
        int streak = NextStreak(out bool grace);
        if (grace) Data.graceUsedWeek = MetaClock311.ThisWeek;
        bool laterDay = Data.lastClaimDay > 0;
        Data.streak = streak;
        Data.bestStreak = Mathf.Max(Data.bestStreak, streak);
        Data.lastClaimDay = MetaClock311.Today;
        Data.dailyClaims++;
        int coins = DailyCoinsFor(streak);
        Pay(coins);
        Count(MissionKind311.DailyClaims, 1);
        if (streak >= 7) Unlock("streak_7");
        if (streak >= 30) Unlock("streak_30");
        // The soft notification prompt: after the first claim on a later day (day 2), or the first L3 clear.
        if (laterDay && !Data.promptTriggered) { Data.promptTriggered = true; PromptDue?.Invoke(); }
        dirty = true;
        Changed?.Invoke();
        return coins;
    }

    public int ClaimMission(Mission311 m)
    {
        if (m == null || !m.Done || m.claimed || !CanClaim) return 0;
        m.claimed = true;
        Pay(m.coins);
        dirty = true;
        Changed?.Invoke();
        return m.coins;
    }

    public int ClaimAward(string id)
    {
        if (!CanClaim || !Data.awardsUnlocked.Contains(id)) return 0;
        AwardDef311 a = tuning.awards.Find(x => x.id == id);
        Data.awardsUnlocked.Remove(id);
        if (!Data.awardsClaimed.Contains(id)) Data.awardsClaimed.Add(id);
        int coins = a != null ? a.coins : 0;
        Pay(coins);
        dirty = true;
        Changed?.Invoke();
        return coins;
    }

    /// <summary>Everything ready (missions, weekly, awards - not the daily, which has its own moment). Returns the coins.</summary>
    public int ClaimAllReady()
    {
        if (!CanClaim) return 0;
        int coins = 0;
        foreach (var m in Data.daily) if (m.Done && !m.claimed) { m.claimed = true; coins += m.coins; }
        foreach (var m in Data.weekly) if (m.Done && !m.claimed) { m.claimed = true; coins += m.coins; }
        foreach (string id in Data.awardsUnlocked.ToArray())
        {
            AwardDef311 a = tuning.awards.Find(x => x.id == id);
            if (!Data.awardsClaimed.Contains(id)) Data.awardsClaimed.Add(id);
            coins += a != null ? a.coins : 0;
        }
        Data.awardsUnlocked.Clear();
        Pay(coins);
        dirty = true;
        Changed?.Invoke();
        return coins;
    }

    public bool RerollAvailable => !ClockRolledBack && (Data.rerollsUsedDay != MetaClock311.Today || Data.rerollsUsed < tuning.freeRerollsPerDay);

    public bool Reroll(int index)
    {
        if (!RerollAvailable || index < 0 || index >= Data.daily.Count || Data.daily[index].claimed) return false;
        if (Data.rerollsUsedDay != MetaClock311.Today) { Data.rerollsUsedDay = MetaClock311.Today; Data.rerollsUsed = 0; }
        Mission311 old = Data.daily[index];
        var rng = new System.Random(MetaClock311.Today * 7 + Data.rerollsUsed * 131 + index);
        var used = new HashSet<string>();
        foreach (var m in Data.daily) used.Add(m.id);
        Mission311 fresh = Pick(rng, false, old.difficulty, used);
        if (fresh == null) return false;
        Data.daily[index] = fresh;
        Data.rerollsUsed++;
        dirty = true;
        Changed?.Invoke();
        return true;
    }

    // ---------------------------------------------------------------------- generation

    float TierNow => skill != null && skill.Model != null ? skill.Model.Tier : 0f;
    int Highest => Mathf.Max(1, SaveSystem.HighestCheckpoint);

    void GenerateDaily(int day)
    {
        Data.daily.Clear();
        var rng = new System.Random(day * 31 + 7);
        var used = new HashSet<string>();
        for (int d = 0; d < 3; d++)
        {
            Mission311 m = Pick(rng, false, d, used);
            if (m != null) { Data.daily.Add(m); used.Add(m.id); }
        }
    }

    void GenerateWeekly(int week)
    {
        Data.weekly.Clear();
        var rng = new System.Random(week * 17 + 3);
        var used = new HashSet<string>();
        for (int d = 0; d < 3; d++)
        {
            Mission311 m = Pick(rng, true, d, used);
            if (m != null) { Data.weekly.Add(m); used.Add(m.id); }
        }
    }

    Mission311 Pick(System.Random rng, bool weekly, int difficulty, HashSet<string> used)
    {
        var pool = new List<MissionTemplate311>();
        foreach (var t in tuning.missions)
            if (t.weekly == weekly && t.difficulty == difficulty && t.minCheckpoint <= Highest && !used.Contains(t.id)) pool.Add(t);
        if (pool.Count == 0)
            foreach (var t in tuning.missions)
                if (t.weekly == weekly && t.minCheckpoint <= Highest && !used.Contains(t.id)) pool.Add(t);
        if (pool.Count == 0) return null;
        MissionTemplate311 p = pool[rng.Next(0, pool.Count)];
        float scale = 1f + p.perCheckpoint * (Highest - 1);
        if (p.tierScaled) scale *= tuning.tierTargetScale[tuning.TierIndex(TierNow)];
        int target = Mathf.Clamp(Mathf.RoundToInt(p.baseTarget * scale), 1, p.maxTarget);
        int[] coins = weekly ? tuning.weeklyCoins : tuning.missionCoins;
        return new Mission311
        {
            id = p.id, kind = (int)p.kind, difficulty = difficulty, target = target,
            coins = coins[Mathf.Clamp(difficulty, 0, coins.Length - 1)], text = string.Format(p.text, target),
        };
    }

    // ---------------------------------------------------------------------- progress

    /// <summary>Adds to every open mission of this kind (a "best" value for one-run kinds).</summary>
    public void Count(MissionKind311 kind, int amount, bool runValue = false)
    {
        if (ClockRolledBack || amount == 0) return;
        Count(Data.daily, kind, amount, runValue);
        Count(Data.weekly, kind, amount, runValue);
    }

    void Count(List<Mission311> list, MissionKind311 kind, int amount, bool runValue)
    {
        foreach (var m in list)
        {
            if (m.kind != (int)kind || m.claimed || m.Done) continue;
            int before = m.progress;
            m.progress = runValue ? Mathf.Max(m.progress, amount) : m.progress + amount;
            m.progress = Mathf.Min(m.progress, m.target);
            if (m.progress == before) continue;
            dirty = true;
            if (m.Done) { Toast?.Invoke("Mission complete  +" + m.coins + "\n" + m.text); Changed?.Invoke(); }
        }
    }

    /// <summary>An award is earned (once ever): claimable from now on.</summary>
    public void Unlock(string id)
    {
        if (Data.awardsClaimed.Contains(id) || Data.awardsUnlocked.Contains(id)) return;
        AwardDef311 a = tuning.awards.Find(x => x.id == id);
        if (a == null) return;
        Data.awardsUnlocked.Add(id);
        dirty = true;
        Toast?.Invoke("Award: " + a.title + "  +" + a.coins);
        Changed?.Invoke();
    }

    void OnRunStarted()
    {
        Count(MissionKind311.PlayRuns, 1);
        runLegs = 0;
        levelRescues = levelShields = 0;
        inBoost = inSlowMo = false;
        lastCoins = collector != null ? collector.currentScore : 0;
    }

    void OnDied(int checkpoint) { inBoost = inSlowMo = false; }

    void OnRescued(CubeRailRunner.RescueInfo info) { if (SkillProfile311.RecordingOff) return; levelRescues++; Count(MissionKind311.CloseCalls, 1); }
    void OnShield(bool obstacle) { if (SkillProfile311.RecordingOff) return; levelShields++; Count(MissionKind311.ShieldSaves, 1); }

    void OnLegDone(DynamicPathGenerator.Segment s, float result)
    {
        if (result <= 0f) return;
        runLegs++;
        Count(MissionKind311.LegsInRun, runLegs, true);

        bool boostLeg = s.boost && s.portalRole == 0;
        // a boost ends when a leg that is not part of it is done
        if (inBoost && (!boostLeg || s.boostId != boostId)) FinishBoost();
        if (boostLeg)
        {
            if (!inBoost) { inBoost = true; boostId = s.boostId; boostMode = Mathf.Max(0, s.boostMode311); boostClean = true; }
            if (result < 1f) boostClean = false;
        }
        if (inSlowMo && !s.slowMo) { inSlowMo = false; Count(MissionKind311.FinishSlowMo, 1); }
        if (s.slowMo) inSlowMo = true;

        if (boostLeg || s.slowMo) return;
        if (s.taps >= 2) Count(MissionKind311.TwoRollCorners, 1);
        if (s.decoyAtEnd) Count(MissionKind311.SurviveDecoys, 1);
        if (s.portalRole == 3)
        {
            if (result >= 1f) Count(MissionKind311.RunwayNoRescue, 1);
            if ((s.designMask311 & ((1 << 4) | (1 << 5) | (1 << 6))) != 0) Count(MissionKind311.SliderRunway, 1);
            for (int k = 0; k < 7; k++) if ((s.designMask311 & (1 << k)) != 0) Unlock("design_" + k);
        }
    }

    void FinishBoost()
    {
        inBoost = false;
        Count(MissionKind311.FinishBoosts, 1);
        Unlock("boost_" + Mathf.Clamp(boostMode, 0, 3));
        if (boostMode == (int)BoostMode311.Broken && boostClean)
        {
            Count(MissionKind311.BrokenBoostClean, 1);
            Unlock("perfect_b3");
        }
    }

    void OnCleared(int portal)
    {
        Count(MissionKind311.ClearCheckpoints, 1);
        Count(MissionKind311.ReachPortal, portal, true);
        if (crc != null && portal == crc.StartCheckpoint) Count(MissionKind311.ClearStartCheckpoint, 1);
        Unlock("first_portal");
        if (portal >= 1 && portal <= 9) Unlock("clear_L" + portal);
        if (portal == 9) Unlock("freefall");
        if (levelRescues == 0 && levelShields == 0) { Data.levelsClearedClean++; Unlock("clean_level"); }
        levelRescues = levelShields = 0;
        if (portal == 3 && !Data.promptTriggered) { Data.promptTriggered = true; PromptDue?.Invoke(); }
        dirty = true;
    }

    void TrackCoinsAndFreefall()
    {
        if (runner == null || SkillProfile311.RecordingOff) return;
        if (collector == null) collector = runner.GetComponent<CoinCollector>();
        if (collector != null)
        {
            int c = collector.currentScore;
            if (c < lastCoins) lastCoins = 0;   // a new run
            if (c > lastCoins)
            {
                Count(MissionKind311.CoinsTotal, c - lastCoins);
                Count(MissionKind311.CoinsInRun, c, true);
                lastCoins = c;
            }
        }
        if (gen != null && gen.PortalsTaken >= 9 && gen.difficultyProfile != null && runner.IsRunning)
        {
            int ff = gen.Score - gen.CheckpointLeg(9);
            if (ff > Data.bestFreefall)
            {
                Data.bestFreefall = ff;
                Count(MissionKind311.FreefallScore, ff, true);
                if (ff >= 25) Unlock("ff_25");
                if (ff >= 50) Unlock("ff_50");
                if (ff >= 100) Unlock("ff_100");
                if (ff >= 200) Unlock("ff_200");
                dirty = true;
            }
        }
    }

    // ---------------------------------------------------------------------- dev menu

    public void DevShiftClock(TimeSpan by)
    {
        MetaClock311.DevOffset += by;
        Data.devOffsetTicks = MetaClock311.DevOffset.Ticks;
        dirty = true;
        CheckClock();
        Refresh();
        Changed?.Invoke();
    }

    public void DevCompleteMission()
    {
        foreach (var m in Data.daily) if (!m.Done) { m.progress = m.target; dirty = true; Toast?.Invoke("Mission complete  +" + m.coins + "\n" + m.text); Changed?.Invoke(); return; }
        foreach (var m in Data.weekly) if (!m.Done) { m.progress = m.target; dirty = true; Changed?.Invoke(); return; }
    }

    public void DevReset()
    {
        Data = new MetaSave311();
        MetaClock311.DevOffset = TimeSpan.Zero;
        ClockRolledBack = false;
        dirty = true;
        CheckClock();
        Refresh();
        Changed?.Invoke();
    }

    public void DevTriggerPrompt() { Data.promptTriggered = true; Data.softPromptState = 0; dirty = true; PromptDue?.Invoke(); }
}
