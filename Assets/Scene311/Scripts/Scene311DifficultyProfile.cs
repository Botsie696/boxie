using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SCENE 3.11 DIFFICULTY (production pass). A Scene310DifficultyProfile (portal schedule, per-level speed / T_human /
/// budget / decoys, endless identities - all as in 3.10) plus the 3.11 level SHAPE and the adaptive tier:
///
///   pos 0          the runway out of the door
///   1 .. R         release legs (R = releaseLegs + the tier's extra), no hazards
///   R+1 ..         hazard WAVES (2 for L1-3, 3 for L4-9): build -> peak -> 1-2 glide legs, a breather glide every
///                  10-12 legs, each later wave's peak +wavePeakStep budget
///   finale         the last finaleLegs before the exhale: peak budget +finalePeakAdd, T_human held at the level's END
///                  value, the level's signature pattern (FinaleSignature311)
///   2 exhale legs  cost <= 2, no specials
///   last leg       the runway into the door
///
/// After the last authored level (L9): FREEFALL, endless 60-leg cycles rotating the endlessIdentity levels, high score
/// only, adaptive OFF. Every number lives here (or in LevelExtra311); Adaptive311 carries the tier, applied only at run
/// start and at portals. Base (Average) T_human never drops under globalFloor1/2; the hardest tier never under
/// absoluteFloor1/2.
/// </summary>
[CreateAssetMenu(menuName = "Runner/Scene 3.11 Difficulty Profile", fileName = "Scene3_11_CheckpointDifficulty")]
public class Scene311DifficultyProfile : Scene310DifficultyProfile
{
    [Header("3.11 per-level extras (index-aligned with levels)")]
    public List<LevelExtra311> levels311 = new List<LevelExtra311>();

    [Header("3.11 level shape")]
    public int releaseLegs = 3;
    public int exhaleLegs = 2;
    [Tooltip("A breather glide about every this many legs inside a wave's build (10-12).")]
    public int glideEvery = 11;
    [Tooltip("Each later wave peaks this much higher (budget per leg).")]
    public float wavePeakStep = 0.2f;
    [Tooltip("The finale's budget per leg: the level's peak budget + this.")]
    public float finalePeakAdd = 0.3f;
    [Tooltip("Share of a wave (after its glides) that is PEAK.")]
    [Range(0.15f, 0.6f)] public float peakShare = 0.35f;
    [Tooltip("Short-leg bursts: leg durations x this.")]
    [Range(0.5f, 1f)] public float burstDurScale = 0.75f;
    public int burstLegs = 6;
    [Tooltip("Staircase finale (L2): leg durations x this.")]
    [Range(0.5f, 1f)] public float staircaseDurScale = 0.85f;
    [Tooltip("Decoy chance x this on decoy-rich finales (L4 gauntlet, L8 after the boost).")]
    public float decoyFinaleScale = 2.5f;
    [Tooltip("After a cap leg the next costs at most this (L1-6 / L7+).")]
    public Vector2Int costAfterCap = new Vector2Int(3, 4);
    public int costAfterCapFromLevel = 7;

    [Header("3.11 boosts")]
    [Tooltip("Legs of a classic boost at L2 (min, max); x boostGrowth per level after, capped at boostLegCap311.")]
    public Vector2 boostLegsL2 = new Vector2(5f, 6f);
    public float boostGrowth = 1.15f;
    public int boostLegCap311 = 14;
    [Tooltip("B2 long boosts are this many times as long.")]
    public float longBoost311 = 1.5f;
    [Tooltip("The newest unlocked boost mode takes this share; the older ones split the rest.")]
    [Range(0f, 1f)] public float newestModeShare = 0.4f;
    [Tooltip("B3 broken rhythm: each zig-zag leg gets up to this many extra cubes (so 5/7/5/6/7).")]
    public int brokenExtraCubes = 3;
    [Tooltip("B4 boost exit: the last boost leg lasts the 2-roll floor x this, then a 2-roll corner.")]
    public float exitLegFloorScale = 1.1f;

    [Header("3.11 spacing (legs)")]
    public int boostGap = 6;
    public int specialGap = 4;
    public int runwayGap = 8;

    [Header("3.11 slow motion")]
    [Tooltip("Most tiny legs in a finale slow-motion section (the finale has to hold the rest of the signature).")]
    public int finaleSlowMoCap = 14;

    [Header("Freefall (after the last level)")]
    public Vector2 freefallTHuman1 = new Vector2(0.42f, 0.40f);
    public float freefallTHumanPerCycle = -0.005f;
    public float freefallRescue = 0.03f;
    public float freefallRescueReach = 1.5f;
    public int freefallWaves = 3;
    public int freefallFinale = 20;
    public int freefallBoosts = 2;
    public int freefallRunways = 2;
    public int freefallSlowMos = 1;
    [Tooltip("Freefall: one more runway station every this many cycles (max 10).")]
    public int freefallStationEvery = 2;

    [Header("T_human")]
    [Tooltip("2-roll T_human = 1-roll + this.")]
    public float twoRollAdd = 0.07f;
    [Tooltip("Hardest adaptive setting / freefall: never under these.")]
    public float absoluteFloor1 = 0.38f;
    public float absoluteFloor2 = 0.45f;
    [Tooltip("Weak-feature 2-roll corners: + this (the Below value).")]
    public float weakTwoRollAdd = 0.05f;

    [Header("Development")]
    [Tooltip("RunTelemetry + the per-corner recorder in development builds (never in release).")]
    public bool devTelemetry = true;

    // ---------------------------------------------------------------------- lookups

    readonly Dictionary<long, LevelSpec311> specs = new Dictionary<long, LevelSpec311>();

    bool IsFreefall(int k) { Identity(Mathf.Max(0, k), out int e); return e > 0; }

    LevelExtra311 Extra(int k, out int endlessCycle)
    {
        LevelTuning310 t = Identity(Mathf.Max(0, k), out endlessCycle);
        int idx = levels.IndexOf(t);
        if (levels311 == null || levels311.Count == 0) return new LevelExtra311();
        return levels311[Mathf.Clamp(idx, 0, levels311.Count - 1)];
    }

    /// <summary>The tier, unless this level is freefall (adaptive off there).</summary>
    TierMods311 ModsFor(int k) => IsFreefall(k) ? TierMods311.Average : Adaptive311.Mods;

    // ---------------------------------------------------------------------- overrides

    public override float SpeedForLeg(int leg)
    {
        float v = base.SpeedForLeg(leg);
        if (!HasLevels) return v;
        return v * Mathf.Max(0.5f, ModsFor(LevelOf(leg)).speed);
    }

    public override float THuman(int taps, int leg, bool boost)
    {
        if (!HasLevels) return base.THuman(taps, leg, boost);
        if (taps <= 0) return tHuman0;
        int k = LevelOf(leg);
        LevelTuning310 t = Identity(k, out int e);
        int pos = leg - Door(k);
        float v;
        if (e > 0)
        {
            v = Mathf.Lerp(freefallTHuman1.x, freefallTHuman1.y, LevelT(pos, LevelLen(k))) + freefallTHumanPerCycle * (e - 1);
            if (taps >= 2) v += twoRollAdd;
            v = Mathf.Max(v, taps == 1 ? absoluteFloor1 : absoluteFloor2);
        }
        else
        {
            LevelSpec311 s = Spec311(k);
            // The ramp runs from the door to the finale; the finale holds the END value.
            float u = s != null ? Mathf.Clamp01(pos / (float)Mathf.Max(1, s.finaleStart)) : LevelT(pos, LevelLen(k));
            Vector2 band = taps == 1 ? t.tHuman1 : t.tHuman2;
            v = Mathf.Lerp(band.x, band.y, u);
            v = Mathf.Max(v, taps == 1 ? globalFloor1 : globalFloor2);           // the base (Average) floor
            TierMods311 m = ModsFor(k);
            v += m.tHumanAdd;
            if (taps >= 2 && m.weakTwoRoll) v += weakTwoRollAdd;
            v = Mathf.Max(v, taps == 1 ? absoluteFloor1 : absoluteFloor2);      // the hardest tier's floor
        }
        if (boost) v = Mathf.Max(v * boostWindowScale, taps == 1 ? boostMin1Tap : boostMin2Tap);
        return v;
    }

    public override Vector2 DurationRange(int leg)
    {
        Vector2 d = base.DurationRange(leg);
        if (!HasLevels) return d;
        int k = LevelOf(leg);
        LevelSpec311 s = Spec311(k);
        if (s == null) return d;
        float f = s.PlanAt(leg - Door(k)).durScale;
        return f > 0f ? d * f : d;
    }

    public override float BuildAvgForCycle(int cycle) => base.BuildAvgForCycle(cycle) * ModsFor(cycle).budget;

    public override float PeakForCycle(int cycle) => base.PeakForCycle(cycle) * ModsFor(cycle).budget;

    public override float DecoyChanceOverride(int cycle)
    {
        float c = base.DecoyChanceOverride(cycle);
        if (c < 0f) return c;
        TierMods311 m = ModsFor(cycle);
        float scale = m.weakDecoy ? Mathf.Min(m.decoys, 0.75f) * 0.7f : m.decoys;
        return Mathf.Clamp01(c * scale);
    }

    public override float RescueWindowAt(int leg)
    {
        if (!HasLevels || leg < 0) return rescueWindow;
        LevelSpec311 s = Spec311(LevelOf(leg));
        return s != null ? s.rescueWindow : rescueWindow;
    }

    public override float RescueReachAt(int leg)
    {
        if (!HasLevels || leg < 0) return rescueMaxCubes;
        LevelSpec311 s = Spec311(LevelOf(leg));
        return s != null ? s.rescueReach : rescueMaxCubes;
    }

    // The 3.11 plan replaces the 3.10 random runway cadence (the generator's specials plan places them).
    public override int[] RunwayPlan(int cycle, System.Random rng) => RunwayPlan(cycle);

    // ---------------------------------------------------------------------- the level plan

    public override LevelSpec311 Spec311(int cycle)
    {
        if (!HasLevels) return null;
        int k = Mathf.Max(0, cycle);
        long key = ((long)Adaptive311.Version << 20) | (uint)k;
        if (specs.TryGetValue(key, out LevelSpec311 s)) return s;
        if (specs.Count > 64) specs.Clear();
        s = BuildSpec(k);
        specs[key] = s;
        return s;
    }

    LevelSpec311 BuildSpec(int k)
    {
        LevelTuning310 t = Identity(k, out int e);
        LevelExtra311 x = Extra(k, out _);
        TierMods311 m = ModsFor(k);
        bool ff = e > 0;
        int len = LevelLen(k);
        int level = k + 1;
        var s = new LevelSpec311 { level = level, freefall = ff, length = len, signature = x.signature };

        int waves = ff ? freefallWaves : Mathf.Max(1, x.waves);
        int finale = ff ? freefallFinale : Mathf.Max(6, x.finaleLegs);
        int release = releaseLegs + Mathf.Max(0, m.releaseAdd);
        s.exhaleStart = len - 1 - exhaleLegs;                 // the last leg (len - 1) is the runway into the door
        s.finaleStart = Mathf.Max(release + 4, s.exhaleStart - finale);
        s.releaseEnd = release;
        s.finaleGlide = m.finaleGlide;
        s.plan = new LegPlan311[len];

        float build = BuildAvgForCycle(k);
        float peak = PeakForCycle(k);

        for (int p = 0; p < len; p++)
            s.plan[p] = new LegPlan311 { phase = WavePhase.Release, wave = -1, intensity = 0.5f, budget = 0f, wantTaps = -1, family = -1, decoyScale = 1f, durScale = 1f };

        for (int p = 1; p <= release && p < len; p++) s.plan[p].release = true;
        for (int p = s.exhaleStart; p < len - 1; p++) { s.plan[p].phase = WavePhase.Cooldown; s.plan[p].exhale = true; }
        if (len - 1 >= 0) s.plan[len - 1].phase = WavePhase.Cooldown;

        // Waves between the release and the finale.
        int a0 = release + 1, b0 = s.finaleStart - 1;
        int region = Mathf.Max(0, b0 - a0 + 1);
        for (int w = 0; w < waves; w++)
        {
            int a = a0 + region * w / waves, b = a0 + region * (w + 1) / waves - 1;
            int n = b - a + 1;
            if (n <= 0) continue;
            int glides = n >= 12 ? 2 : 1;
            int body = Mathf.Max(1, n - glides);
            int pk = Mathf.Clamp(Mathf.RoundToInt(body * peakShare), 1, Mathf.Max(1, body - 1));
            int bl = body - pk;
            for (int p = a; p <= b; p++)
            {
                ref LegPlan311 L = ref s.plan[p];
                L.wave = w;
                int i = p - a;
                if (i < bl)
                {
                    L.phase = WavePhase.Build;
                    L.intensity = Mathf.Lerp(0.5f, 1f, bl > 1 ? i / (float)(bl - 1) : 1f);
                    L.budget = build;
                }
                else if (i < body)
                {
                    L.phase = WavePhase.Peak;
                    L.intensity = 1f;
                    L.budget = peak + wavePeakStep * w;
                }
                else
                {
                    L.phase = WavePhase.Build;
                    L.glide = true;
                    L.budget = build;
                }
            }
            // A breather glide in the middle of a long build.
            if (bl > glideEvery) s.plan[a + bl / 2].glide = true;
        }

        // The finale.
        for (int p = s.finaleStart; p < s.exhaleStart; p++)
        {
            ref LegPlan311 L = ref s.plan[p];
            L.phase = WavePhase.Peak;
            L.finale = true;
            L.intensity = 1f;
            L.budget = peak + finalePeakAdd;
        }
        if (m.finaleGlide) s.plan[(s.finaleStart + s.exhaleStart) / 2].glide = true;

        // Specials.
        s.boosts = ff ? freefallBoosts : x.boosts;
        s.runways = ff ? freefallRunways : x.runways;
        s.slowMos = ff ? freefallSlowMos : x.slowMos;
        if (m.weakBoost) s.boosts = Mathf.RoundToInt(s.boosts * 0.7f);
        if (m.weakRunway) s.runways = Mathf.RoundToInt(s.runways * 0.7f);
        if (m.weakSlowMo) s.slowMos = Mathf.RoundToInt(s.slowMos * 0.7f);
        if (level < slowMoFromLevel) s.slowMos = 0;

        float boostScale = m.weakBoost ? Mathf.Min(m.boostLength, 0.85f) : m.boostLength;
        float grow = Mathf.Pow(boostGrowth, Mathf.Max(0, Mathf.Min(level, 10) - 2));
        s.boostLegs = new Vector2(Mathf.Clamp(boostLegsL2.x * grow * boostScale, 3f, boostLegCap311),
                                  Mathf.Clamp(boostLegsL2.y * grow * boostScale, 3f, boostLegCap311));
        int newest = Mathf.Clamp(x.newestBoostMode, -1, 3);
        for (int i = 0; i < 4; i++) s.modeWeights[i] = 0f;
        if (newest == 0) s.modeWeights[0] = 1f;
        else if (newest > 0)
        {
            for (int i = 0; i < newest; i++) s.modeWeights[i] = (1f - newestModeShare) / newest;
            s.modeWeights[newest] = newestModeShare;
        }
        if (newest < 0) s.boosts = 0;

        int stationsAdd = m.stationsAdd + (m.weakRunway ? -1 : 0);
        Vector2Int st = x.stations;
        if (ff) st += Vector2Int.one * Mathf.Min(10, (e - 1) / Mathf.Max(1, freefallStationEvery));
        s.stations = new Vector2Int(Mathf.Clamp(st.x + stationsAdd, m.stationsMin, Mathf.Min(10, m.stationsMax)),
                                    Mathf.Clamp(st.y + stationsAdd, m.stationsMin, Mathf.Min(10, m.stationsMax)));
        s.designWeights = x.designWeights;
        s.finaleDesigns = x.finaleDesigns;
        s.gapFactor = x.gapFactor;
        s.gapJitter = x.gapJitter;
        s.sliderSpeed = x.sliderSpeed;
        s.maxSliders = x.maxSliders;

        s.rescueWindow = ff ? freefallRescue : Mathf.Clamp(x.rescueWindow + m.rescueAdd, m.rescueMin, m.rescueCap);
        s.rescueReach = ff ? freefallRescueReach : x.rescueReachCubes;
        s.costAfterCap = level >= costAfterCapFromLevel ? costAfterCap.y : costAfterCap.x;
        float pf = Mathf.Max(0.05f, x.powerFrequency);
        s.powerGapScale = 1f / pf;
        s.shieldGapScale = 1f / Mathf.Max(0.05f, pf * m.shieldFreq);

        ApplySignature(s, x, m, ff);
        return s;
    }

    void ApplySignature(LevelSpec311 s, LevelExtra311 x, TierMods311 m, bool ff)
    {
        int fs = s.finaleStart, fe = s.exhaleStart;      // finale = [fs, fe)
        int F = fe - fs;
        int classic = Mathf.RoundToInt((s.boostLegs.x + s.boostLegs.y) * 0.5f);
        int longLegs = Mathf.Clamp(Mathf.RoundToInt(classic * longBoost311), 3, boostLegCap311);
        switch (s.signature)
        {
            case FinaleSignature311.TwoRollPairs:
                for (int p = fs + 2; p + 1 < fe; p += 6) { s.plan[p].wantTaps = 2; s.plan[p + 1].wantTaps = 2; }
                break;
            case FinaleSignature311.Staircase:
                for (int p = fs; p < fe; p++) { s.plan[p].family = 0; s.plan[p].durScale = staircaseDurScale; }
                break;
            case FinaleSignature311.LongBoostExit:
                if (s.boosts >= 0) s.fixedSpecials.Add(new Special311 { kind = 0, at = fs + 3, legs = longLegs, mode = BoostMode311.Exit, finale = true });
                break;
            case FinaleSignature311.DecoyGauntlet:
                for (int p = fs; p < fe; p++) { s.plan[p].decoyScale = decoyFinaleScale; s.plan[p].family = 4; }
                break;
            case FinaleSignature311.RunwaySlowBurst:
                s.fixedSpecials.Add(new Special311 { kind = 2, at = fs + 2, legs = 1, finale = true });
                s.fixedSpecials.Add(new Special311 { kind = 1, at = fs + 2 + 1 + specialGap, legs = -1, finale = true });
                break;
            case FinaleSignature311.SliderRunwayBurst:
                s.fixedSpecials.Add(new Special311 { kind = 2, at = fs + 3, legs = 1, finale = true });
                break;
            case FinaleSignature311.LongBoostDecoys:
                s.fixedSpecials.Add(new Special311 { kind = 0, at = fs + 3, legs = longLegs, mode = BoostMode311.Long, finale = true });
                for (int p = fs; p < fe; p++) s.plan[p].decoyScale = decoyFinaleScale;
                break;
            case FinaleSignature311.MixAll:
                s.fixedSpecials.Add(new Special311 { kind = 2, at = fs + 2, legs = 1, finale = true });
                s.fixedSpecials.Add(new Special311 { kind = 1, at = fs + 2 + 1 + specialGap, legs = -1, finale = true });
                for (int p = fs; p < fe; p++) s.plan[p].decoyScale = 1.5f;
                // One double spike near the end: two cap legs back to back (the cap rule is lifted after the first).
                int sp = Mathf.Max(fs, fe - 6);
                if (sp + 1 < fe)
                {
                    s.plan[sp].wantTaps = 2; s.plan[sp].spike = true; s.plan[sp].decoyScale = decoyFinaleScale;
                    s.plan[sp + 1].wantTaps = 2; s.plan[sp + 1].decoyScale = decoyFinaleScale;
                }
                break;
        }
        // A finale special consumes nothing in the waves: the level's own counts are for the waves.
        if (F <= 0) s.fixedSpecials.Clear();
    }

    /// <summary>Signature specials that end in a short-leg burst (the generator starts the burst when they finish).</summary>
    public bool BurstAfterFinaleSpecial(FinaleSignature311 sig, int kind) =>
        (sig == FinaleSignature311.RunwaySlowBurst && kind == 1) || (sig == FinaleSignature311.SliderRunwayBurst && kind == 2);

    protected override void OnValidate() { base.OnValidate(); specs.Clear(); }
    protected override void OnEnable() { base.OnEnable(); specs.Clear(); }
}
