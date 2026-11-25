using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One level (checkpoint) of Scene310DifficultyProfile. Everything is authored explicitly in the asset: speed, leg length,
/// human-time floors, hazard budget, wave shape and the per-level extras (decoys, runways, coins, power-up).
/// Level index 0 = checkpoint 1 (run start up to portal 1), 1 = checkpoint 2, ...
/// </summary>
[Serializable]
public class LevelTuning310
{
    public string name = "";
    [TextArea(2, 5)] public string intent = "";
    [Tooltip("Legs from this level's door to the next one. The shape / glide / runway positions below are authored for this length.")]
    public int length = 40;

    [Header("Speed (u/s at size 1, boost x1.2 on top)")]
    public float speedStart = 18f;
    public float speedEnd = 22f;
    [Tooltip("Legs held flat at speedStart before the climb.")]
    public int speedHoldLegs;
    [Tooltip("1 = linear climb after the hold; above 1 = gentler start, steeper finish.")]
    public float speedCurve = 1f;
    [Tooltip("Legs over which the speed glides from the previous level's end speed (smoothstep).")]
    [Range(0, 10)] public int blendLegs = 4;

    [Header("Ordinary leg length in seconds (min, max), start -> end of the level")]
    public Vector2 durStart = new Vector2(0.80f, 1.05f);
    public Vector2 durEnd = new Vector2(0.75f, 1.00f);

    [Header("T_human (start, end): 1-roll and 2-roll legs. Never below the profile's global floors")]
    public Vector2 tHuman1 = new Vector2(0.50f, 0.46f);
    public Vector2 tHuman2 = new Vector2(0.59f, 0.55f);

    [Header("Hazard budget")]
    public float buildAvg = 2.2f;
    public float peakBudget = 3.6f;
    public int legCostCap = 5;

    [Header("Wave: release / build / peak / boosts / glides, in legs after the door")]
    public WaveShape shape;

    [Header("Per-level extras")]
    [Range(0f, 1f)] public float decoyChance = 0f;
    [Tooltip("Remix family forced for this level (0 Staircase, 1 Peel, 2 ClimbDive, 3 PlankWeave, 4 DecoyGauntlet), -1 = random if shape.families.")]
    public int forcedFamily = -1;
    [Tooltip("Obstacle-runway legs (positions in legs after the door). Empty = none.")]
    public int[] runwayLegs = new int[0];
    [Tooltip("Multiplier on the scene's coin chance for legs laid in this level.")]
    public float coinScale = 1f;
    [Tooltip("One power-up diamond is guaranteed from this leg of the level on. -1 = leave it to the scheduler.")]
    public int powerUpFromLeg = -1;
}

/// <summary>
/// RETENTION-TUNED DIFFICULTY for "MainScene prop camera 3.10" (and nothing else). A DifficultyProfile whose levels are
/// authored one by one (see LevelTuning310) against an explicit portal schedule, with a rotating "endless" tail and an
/// optional quiet adaptive easing after repeated deaths. Everything the fairness machinery needs (overhead + T_human
/// floors, wave rules, close-call rescue) is the base class's, unchanged; the per-level hooks it exposes are inert for
/// every other profile.
/// </summary>
[CreateAssetMenu(menuName = "Runner/Scene 3.10 Difficulty Profile", fileName = "Scene3_10_CheckpointDifficulty")]
public class Scene310DifficultyProfile : DifficultyProfile
{
    [Header("Portal schedule")]
    [Tooltip("Leg of portal 1, 2, 3...; after the last entry a portal comes every portalLegsRepeat legs.")]
    public int[] portalLegs = { 32, 68, 92, 132, 172, 200, 236, 262, 306 };
    [Min(10)] public int portalLegsRepeat = 40;

    [Header("Levels (index 0 = checkpoint 1)")]
    public List<LevelTuning310> levels = new List<LevelTuning310>();

    [Header("Endless (levels past the list): each cycle takes the next identity from this list (level indices)")]
    public int[] endlessIdentity = { 1, 2, 3, 6, 4 };
    public float endlessSpeedStart = 23f;
    public float endlessSpeedEnd = 26f;
    public float endlessSpeedPerCycle = 0.4f;
    public float speedCeiling = 28f;
    public float endlessBudgetPerCycle = 0.1f;
    public float endlessPeakCap = 4.4f;
    [Tooltip("Endless: a glide leg at least this often (legs).")]
    public int endlessGlideEvery = 7;

    [Header("Fairness floors (global minimum T_human)")]
    public float globalFloor1 = 0.40f;
    public float globalFloor2 = 0.47f;

    [Header("Opening level")]
    [Tooltip("Lay the one forced teaching decoy fork in the opening level. Off = the opening level has no decoy at all.")]
    public bool teachOpeningDecoy = false;

    [Header("Quiet adaptive easing (hidden): after 3 / 5 deaths in a row in one checkpoint")]
    public bool adaptiveEasing = true;

    // ---------------------------------------------------------------------- lookup

    protected sealed class Cached
    {
        public WaveShape shape;
        public int[] runways;
        public int length;
    }

    readonly Dictionary<int, Cached> cache = new Dictionary<int, Cached>();
    int lastK = -1, lastLo, lastHi;

    protected bool HasLevels => levels != null && levels.Count > 0;

    public override int PortalLeg(int k, int uniformL)
    {
        if (k <= 0) return 0;
        int n = portalLegs != null ? portalLegs.Length : 0;
        if (n == 0) return k * Mathf.Max(10, portalLegsRepeat);
        if (k <= n) return portalLegs[k - 1];
        return portalLegs[n - 1] + (k - n) * Mathf.Max(10, portalLegsRepeat);
    }

    protected int Door(int k) => PortalLeg(k, 50);
    protected int LevelLen(int k) => Mathf.Max(3, Door(k + 1) - Door(k));

    protected int LevelOf(int leg)
    {
        leg = Mathf.Max(0, leg);
        if (lastK >= 0 && leg >= lastLo && leg < lastHi) return lastK;
        int k = 0;
        while (Door(k + 1) <= leg && k < 10000) k++;
        lastK = k; lastLo = Door(k); lastHi = Door(k + 1);
        return k;
    }

    /// <summary>The level whose authored numbers level k uses (itself, or an endless identity), and how many endless cycles in.</summary>
    protected LevelTuning310 Identity(int k, out int endlessCycle)
    {
        endlessCycle = 0;
        if (k < levels.Count) return levels[Mathf.Max(0, k)];
        endlessCycle = k - levels.Count + 1;                       // 1 = the first endless cycle
        int n = endlessIdentity != null ? endlessIdentity.Length : 0;
        int idx = n > 0 ? endlessIdentity[(endlessCycle - 1) % n] : levels.Count - 1;
        return levels[Mathf.Clamp(idx, 0, levels.Count - 1)];
    }

    protected virtual float StartSpeed(int k)
    {
        LevelTuning310 t = Identity(k, out int e);
        return e == 0 ? t.speedStart : Mathf.Min(speedCeiling, endlessSpeedStart + endlessSpeedPerCycle * (e - 1));
    }

    protected virtual float EndSpeed(int k)
    {
        LevelTuning310 t = Identity(k, out int e);
        return e == 0 ? t.speedEnd : Mathf.Min(speedCeiling, endlessSpeedEnd + endlessSpeedPerCycle * (e - 1));
    }

    protected float LevelT(int pos, int len) => Mathf.Clamp01(pos / (float)Mathf.Max(1, len - 1));

    bool EasingOn => adaptiveEasing;

    // ---------------------------------------------------------------------- overrides

    public override bool TeachOpeningDecoy => teachOpeningDecoy;
    public override int ForcedFamily(int cycle) => HasLevels ? Identity(Mathf.Max(0, cycle), out _).forcedFamily : -1;
    public override float DecoyChanceOverride(int cycle) => HasLevels ? Identity(Mathf.Max(0, cycle), out _).decoyChance : -1f;
    public override float CoinChanceScale(int cycle) => HasLevels ? Mathf.Max(0f, Identity(Mathf.Max(0, cycle), out _).coinScale) : 1f;
    public override int PowerUpGuaranteePos(int cycle) => HasLevels ? Identity(Mathf.Max(0, cycle), out _).powerUpFromLeg : -1;
    public override int[] RunwayPlan(int cycle) => HasLevels ? Get(Mathf.Max(0, cycle)).runways : null;

    /// <summary>
    /// Runways are RANDOM, about 20 legs apart (like the 3.9 cadence, 17-30), re-rolled every run and every level:
    /// the first lands 5+ legs after the release and no later than leg 20, each next one 17-30 legs on, and none
    /// within 5 legs of the door. A level whose authored runwayLegs is empty has none.
    /// </summary>
    public override int[] RunwayPlan(int cycle, System.Random rng)
    {
        if (!HasLevels) return null;
        int k = Mathf.Max(0, cycle);
        Cached c = Get(k);
        if (c.runways == null || c.runways.Length == 0) return c.runways;
        int len = LevelLen(k);
        int lo = c.shape.releaseEnd + 5;
        int hi = Mathf.Min(20, len - 5);
        if (hi < lo) return c.runways;                       // too short to roll: the authored leg
        var list = new List<int>();
        int p = rng.Next(lo, hi + 1);
        while (p <= len - 5)
        {
            list.Add(p);
            p += rng.Next(17, 31);
        }
        return list.ToArray();
    }

    public override float SpeedForLeg(int leg)
    {
        if (!HasLevels) return base.SpeedForLeg(leg);
        int k = LevelOf(leg);
        LevelTuning310 t = Identity(k, out int e);
        int len = LevelLen(k);
        int pos = Mathf.Max(0, leg - Door(k));
        float s0 = StartSpeed(k), s1 = EndSpeed(k);
        float v = s0;
        if (pos > t.speedHoldLegs)
        {
            float u = Mathf.Clamp01((pos - t.speedHoldLegs) / (float)Mathf.Max(1, len - 1 - t.speedHoldLegs));
            if (t.speedCurve > 0f && !Mathf.Approximately(t.speedCurve, 1f)) u = Mathf.Pow(u, t.speedCurve);
            v = Mathf.Lerp(s0, s1, u);
        }
        int blend = k > 0 ? t.blendLegs : 0;
        if (e > 0) blend = Mathf.Max(blend, 4);
        if (k > 0 && pos < blend)
            v = Mathf.Lerp(EndSpeed(k - 1), v, Smooth01((pos + 1f) / (blend + 1f)));
        return Mathf.Min(v, Mathf.Max(speedCeiling, s1));
    }

    public override Vector2 DurationRange(int leg)
    {
        if (!HasLevels) return base.DurationRange(leg);
        int k = LevelOf(leg);
        LevelTuning310 t = Identity(k, out _);
        return Vector2.Lerp(t.durStart, t.durEnd, LevelT(leg - Door(k), LevelLen(k)));
    }

    public override float THuman(int taps, int leg, bool boost)
    {
        if (!HasLevels) return base.THuman(taps, leg, boost);
        if (taps <= 0) return tHuman0;
        int k = LevelOf(leg);
        LevelTuning310 t = Identity(k, out _);
        int pos = leg - Door(k);
        Vector2 band = taps == 1 ? t.tHuman1 : t.tHuman2;
        float v = Mathf.Lerp(band.x, band.y, LevelT(pos, LevelLen(k)));
        v = Mathf.Max(v, taps == 1 ? globalFloor1 : globalFloor2);
        if (EasingOn && pos > Get(k).shape.releaseEnd) v += AdaptiveEasing.THumanAdd(k + 1);   // never the release legs
        if (boost) v = Mathf.Max(v * boostWindowScale, taps == 1 ? boostMin1Tap : boostMin2Tap);
        return v;
    }

    public override float BuildAvgForCycle(int cycle)
    {
        if (!HasLevels) return base.BuildAvgForCycle(cycle);
        int k = Mathf.Max(0, cycle);
        LevelTuning310 t = Identity(k, out int e);
        float v = t.buildAvg + (e > 0 ? endlessBudgetPerCycle * e : 0f);
        return v * (EasingOn ? AdaptiveEasing.BudgetScale(k + 1) : 1f);
    }

    public override float PeakForCycle(int cycle)
    {
        if (!HasLevels) return base.PeakForCycle(cycle);
        int k = Mathf.Max(0, cycle);
        LevelTuning310 t = Identity(k, out int e);
        float v = t.peakBudget + (e > 0 ? endlessBudgetPerCycle * e : 0f);
        if (e > 0) v = Mathf.Min(v, endlessPeakCap);
        return v * (EasingOn ? AdaptiveEasing.BudgetScale(k + 1) : 1f);
    }

    public override int LegCostCapForCycle(int cycle) =>
        HasLevels ? Identity(Mathf.Max(0, cycle), out _).legCostCap : base.LegCostCapForCycle(cycle);

    public override WaveShape ShapeForCycle(int cycle) =>
        HasLevels ? Get(Mathf.Max(0, cycle)).shape : base.ShapeForCycle(cycle);

    // ---------------------------------------------------------------------- shape fitting (cached: no per-call allocation)

    protected Cached Get(int k)
    {
        if (cache.TryGetValue(k, out Cached c)) return c;
        LevelTuning310 t = Identity(k, out int e);
        int len = LevelLen(k);
        c = new Cached { length = len };
        WaveShape s = t.shape;
        int[] runways = t.runwayLegs != null ? t.runwayLegs : new int[0];
        if (len != t.length || e > 0)
        {
            float f = len / (float)Mathf.Max(10, t.length);
            int tail = Mathf.Max(2, t.length - s.peakEnd);
            int peakLen = Mathf.Max(0, s.peakEnd - s.buildEnd);
            int peakEnd = Mathf.Max(6, len - tail);
            int buildEnd = Mathf.Max(s.releaseEnd + 3, peakEnd - peakLen);
            var boosts = new List<Vector2Int>();
            int prevEnd = -100;
            if (s.buildBoosts != null)
                foreach (Vector2Int b in s.buildBoosts)
                {
                    int st = Mathf.RoundToInt(b.x * f);
                    if (st >= prevEnd + 7 && st + b.y <= buildEnd) { boosts.Add(new Vector2Int(st, b.y)); prevEnd = st + b.y; }
                }
            var glides = new List<int>();
            if (e > 0)
            {
                int every = Mathf.Max(4, endlessGlideEvery);
                for (int g = 6; g < buildEnd; g += every) glides.Add(g);
            }
            else if (s.glideLegs != null)
                foreach (int g in s.glideLegs)
                {
                    int v = Mathf.RoundToInt(g * f);
                    if (v > 0 && v < buildEnd && !glides.Contains(v)) glides.Add(v);
                }
            var rw = new List<int>();
            foreach (int r in runways)
            {
                int v = Mathf.Clamp(Mathf.RoundToInt(r * f), s.releaseEnd + 2, len - 6);
                if (rw.Count == 0 || v > rw[rw.Count - 1] + 5) rw.Add(v);
            }
            s.peakEnd = peakEnd;
            s.buildEnd = buildEnd;
            s.firstHazardLeg = Mathf.Min(s.firstHazardLeg, buildEnd);
            s.peakBoostLegs = Mathf.Min(s.peakBoostLegs, peakEnd - buildEnd);
            if (s.peakKind == PeakKind.Boost && s.peakBoostLegs < 3) { s.peakKind = PeakKind.None; s.peakBoostLegs = 0; }
            s.buildBoosts = boosts.ToArray();
            s.glideLegs = glides.ToArray();
            runways = rw.ToArray();
        }
        c.shape = s;
        c.runways = runways;
        cache[k] = c;
        return c;
    }

    protected virtual void OnValidate() { cache.Clear(); lastK = -1; }
    protected virtual void OnEnable() { cache.Clear(); lastK = -1; }
}

/// <summary>
/// QUIET ADAPTIVE EASING (scene 3.10 only; Scene310DifficultyProfile.adaptiveEasing). Deaths in a row per checkpoint are
/// kept in PlayerPrefs. After 3 in the same checkpoint the next attempt's hazard budget is x0.88 and every non-release
/// T_human +0.04 s; after 5, x0.80 and +0.07 s. Clearing the checkpoint (crossing its door) resets it. The speed curve
/// and the release legs are never eased. Inert unless the running profile is a Scene310DifficultyProfile with the switch on.
/// </summary>
public static class AdaptiveEasing
{
    const string Key = "AdaptiveEasing310_";
    const int Max = 32;
    static readonly int[] deaths = new int[Max + 1];
    static bool loaded;

    public static bool Enabled => DifficultyProfile.Active is Scene310DifficultyProfile p && p.adaptiveEasing;

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        for (int i = 1; i <= Max; i++) deaths[i] = PlayerPrefs.GetInt(Key + i, 0);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { loaded = false; }

    public static int Deaths(int checkpoint)
    {
        Load();
        return checkpoint >= 1 && checkpoint <= Max ? deaths[checkpoint] : 0;
    }

    /// <summary>The run died in this checkpoint (1-based).</summary>
    public static void NoteDeath(int checkpoint)
    {
        if (!Enabled || checkpoint < 1 || checkpoint > Max) return;
        Load();
        deaths[checkpoint]++;
        PlayerPrefs.SetInt(Key + checkpoint, deaths[checkpoint]);
    }

    /// <summary>The runner crossed this checkpoint's exit door (1-based): its counter and easing reset.</summary>
    public static void NoteCleared(int checkpoint)
    {
        if (!Enabled || checkpoint < 1 || checkpoint > Max) return;
        Load();
        if (deaths[checkpoint] == 0) return;
        deaths[checkpoint] = 0;
        PlayerPrefs.SetInt(Key + checkpoint, 0);
    }

    public static float BudgetScale(int checkpoint)
    {
        int d = Deaths(checkpoint);
        return d >= 5 ? 0.80f : d >= 3 ? 0.88f : 1f;
    }

    public static float THumanAdd(int checkpoint)
    {
        int d = Deaths(checkpoint);
        return d >= 5 ? 0.07f : d >= 3 ? 0.04f : 0f;
    }
}
