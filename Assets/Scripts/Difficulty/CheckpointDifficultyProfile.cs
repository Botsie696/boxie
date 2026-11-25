using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One checkpoint stage of CheckpointDifficultyProfile: everything that makes that portal cycle feel like itself.
/// Stage 1 = checkpoint 2 (the cycle after portal 1), stage 2 = checkpoint 3, and so on. Checkpoint 1 (stage 0) is not here:
/// it keeps the base profile's own numbers, so the opening is exactly what it always was.
/// </summary>
[Serializable]
public class StageTuning
{
    public string name = "";
    [TextArea(2, 6)] public string intent = "";

    [Header("Speed (u/s at size 1, boost x1.2 rides on top) - ramps linearly over the stage")]
    public float speedStart = 16.5f;
    public float speedEnd = 19.5f;

    [Header("Ordinary leg length in seconds (min, max), start -> end of the stage. Cubes = seconds x speed")]
    public Vector2 durStart = new Vector2(0.74f, 0.98f);
    public Vector2 durEnd = new Vector2(0.72f, 0.96f);

    [Header("Human time budget T_human (start, end): 1-roll and 2-roll legs. Floors are never below these")]
    public Vector2 tHuman1 = new Vector2(0.50f, 0.46f);
    public Vector2 tHuman2 = new Vector2(0.60f, 0.55f);

    [Header("Hazard budget")]
    [Tooltip("BUILD-phase cost per leg earned (taps + hazards + 1 for a short leg).")]
    public float buildAvg = 2.2f;
    [Tooltip("PEAK-phase cost per leg earned.")]
    public float peakBudget = 3.6f;
    [Tooltip("Most cost any one leg may carry.")]
    public int legCostCap = 5;

    [Header("Wave: where the release / build / peak / boosts / glides sit in this stage's 50 legs")]
    public WaveShape shape;
}

/// <summary>
/// DIFFICULTY PER CHECKPOINT (scene 3.7 only). A DifficultyProfile whose speed, leg lengths, human-time floors, hazard
/// budgets and wave shape are looked up by the portal cycle the leg belongs to, so every checkpoint gets its own
/// character while the fairness machinery (time floors, wave rules, close-call rescue) stays the one the game already has.
///
/// Stage 0 (the run start up to the first portal = checkpoints 0 and 1) calls straight into the base class, so it is
/// the original profile. Stage k >= 1 reads stages[min(k, Count) - 1]; past the last stage the final one repeats and its
/// speed climbs a little per extra checkpoint, up to speedCeiling.
///
/// Why these shapes (see the design table in the stage names): one axis leads in each checkpoint, the others stay
/// moderate, and every checkpoint opens below the speed the one before it ended on, so the curve is a sawtooth with a rising
/// ceiling instead of a ramp. Checkpoint 2 is the "short legs, mid speed" dip the brief asked for.
/// </summary>
[CreateAssetMenu(menuName = "Runner/Checkpoint Difficulty Profile", fileName = "CheckpointDifficultyProfile")]
public class CheckpointDifficultyProfile : DifficultyProfile
{
    [Header("Stages (index 0 = checkpoint 2)")]
    [Tooltip("Legs per portal cycle. MUST equal the CheckpointSet's legsBetweenPortals (50): the stage of a leg is leg / this.")]
    [Min(10)] public int legsPerStage = 50;
    [Tooltip("The first legs of a stage glide from the previous stage's end speed to the new start speed, so the portal exit never jolts.")]
    [Range(0, 10)] public int speedBlendLegs = 4;
    [Tooltip("Past the last stage: extra u/s per additional checkpoint, and the most speed any leg may have.")]
    public float speedPerExtraStage = 0.6f;
    public float speedCeiling = 28f;
    [Tooltip("Force the yellow triangle gates off in every checkpoint (overrides the scene checkbox at run start).")]
    public bool noGates = true;
    [Tooltip("Force the red pyramid obstacles off in every checkpoint (overrides the scene checkbox at run start).")]
    public bool noObstacles = true;
    [Tooltip("Checkpoint 1 (run start up to the first portal): the base profile's speed curve times this. 1 = the original speed.")]
    [Min(0.5f)] public float stage0SpeedScale = 1.3f;

    public List<StageTuning> stages = DefaultStages();

    // ---------------------------------------------------------------------- defaults

    static List<StageTuning> DefaultStages()
    {
        return new List<StageTuning>
        {
            // CHECKPOINT 2 - TIGHT CORNERS. Leg length goes back to the opening's (12-16 cubes) but at 16-20 u/s, so each leg
            // is ~0.75-1.0 s: more corners per screen than the end of checkpoint 1, fewer 2-rolls (the floors keep them off
            // short legs), decoy forks as the featured hazard. Three long glide legs and a post-boost breather give the rest.
            new StageTuning
            {
                name = "Tight Corners",
                intent = "Rhythm. Short legs at mid speed, decoy forks, one short boost late. Three glide breathers.",
                speedStart = 16.5f, speedEnd = 19.5f,
                durStart = new Vector2(0.74f, 0.98f), durEnd = new Vector2(0.72f, 0.96f),
                tHuman1 = new Vector2(0.50f, 0.46f), tHuman2 = new Vector2(0.60f, 0.55f),
                buildAvg = 2.4f, peakBudget = 3.8f, legCostCap = 5,
                shape = new WaveShape
                {
                    releaseEnd = 4, buildEnd = 38, peakEnd = 45, firstHazardLeg = 7,
                    onboardingBoost = new Vector2Int(14, 5), peakKind = PeakKind.Boost, peakBoostLegs = 5,
                    buildBoosts = new Vector2Int[0], clusterBridgeLegs = new int[0],
                    glideLegs = new[] { 12, 21, 30 }, glideLengthScale = 1.45f, featured = 1, families = false,
                },
            },
            // CHECKPOINT 3 - BOOST RUSH. The speed checkpoint: ordinary legs are long and clean (19-26 cubes), hazards are
            // thin, but four boost sections (5 + 5 + 5 + 7 legs) hit at x1.2 speed, each followed by a breather and a glide.
            new StageTuning
            {
                name = "Boost Rush",
                intent = "Speed. Long clean legs, sparse hazards, four boosts, glide after each one.",
                speedStart = 18.5f, speedEnd = 22.5f,
                durStart = new Vector2(1.05f, 1.40f), durEnd = new Vector2(1.00f, 1.30f),
                tHuman1 = new Vector2(0.46f, 0.44f), tHuman2 = new Vector2(0.55f, 0.52f),
                buildAvg = 1.5f, peakBudget = 3.0f, legCostCap = 5,
                shape = new WaveShape
                {
                    releaseEnd = 3, buildEnd = 40, peakEnd = 47, firstHazardLeg = 10,
                    onboardingBoost = new Vector2Int(14, 5), peakKind = PeakKind.Boost, peakBoostLegs = 7,
                    buildBoosts = new[] { new Vector2Int(7, 5), new Vector2Int(18, 5), new Vector2Int(29, 5) },
                    clusterBridgeLegs = new int[0],
                    glideLegs = new[] { 14, 25, 36 }, glideLengthScale = 1.3f, featured = 4, families = false,
                },
            },
            // CHECKPOINT 4 - FORK MAZE. The precision checkpoint: no boosts at all, medium legs (long enough for 2-roll turns),
            // decoy forks as the featured hazard from early on and no PEAK spike (bridges are off in 3.7, so there is no
            // cluster; the build simply runs to the portal). Five glides keep it from becoming a wall.
            new StageTuning
            {
                name = "Fork Maze",
                intent = "Precision. No boosts. Frequent decoy forks and 2-roll turns on medium legs, five glide breathers.",
                speedStart = 17.5f, speedEnd = 20.5f,
                durStart = new Vector2(0.95f, 1.25f), durEnd = new Vector2(0.90f, 1.20f),
                tHuman1 = new Vector2(0.46f, 0.42f), tHuman2 = new Vector2(0.54f, 0.50f),
                buildAvg = 2.7f, peakBudget = 3.8f, legCostCap = 6,
                shape = new WaveShape
                {
                    releaseEnd = 4, buildEnd = 42, peakEnd = 45, firstHazardLeg = 6,
                    onboardingBoost = new Vector2Int(14, 5), peakKind = PeakKind.None, peakBoostLegs = 0,
                    buildBoosts = new Vector2Int[0], clusterBridgeLegs = new int[0],
                    glideLegs = new[] { 10, 17, 24, 31, 38 }, glideLengthScale = 1.35f, featured = 1, families = false,
                },
            },
            // CHECKPOINT 5 - REMIX FINALE. The fastest ordinary legs and the tightest floors, but it is the only stage that
            // mixes everything: a build boost, a 7-leg peak boost, random remix families (staircase, peel, climb/dive, plank
            // weave, decoy gauntlet) and three glides. Different every run, so it does not feel like a repeat of 2-4.
            new StageTuning
            {
                name = "Remix Finale",
                intent = "Mastery. Fastest speed, short-medium legs, random remix families, two boosts, three glide breathers.",
                speedStart = 20.0f, speedEnd = 24.0f,
                durStart = new Vector2(0.72f, 0.98f), durEnd = new Vector2(0.68f, 0.94f),
                tHuman1 = new Vector2(0.44f, 0.40f), tHuman2 = new Vector2(0.52f, 0.47f),
                buildAvg = 2.5f, peakBudget = 4.0f, legCostCap = 6,
                shape = new WaveShape
                {
                    releaseEnd = 4, buildEnd = 33, peakEnd = 45, firstHazardLeg = 6,
                    onboardingBoost = new Vector2Int(14, 5), peakKind = PeakKind.Boost, peakBoostLegs = 7,
                    buildBoosts = new[] { new Vector2Int(11, 5) }, clusterBridgeLegs = new int[0],
                    glideLegs = new[] { 8, 21, 28 }, glideLengthScale = 1.35f, featured = -1, families = true,
                },
            },
        };
    }

    // ---------------------------------------------------------------------- stage lookup

    [Header("Portal schedule")]
    [Tooltip("Leg of portal 1, 2, 3...; after the last entry a portal comes every portalLegsRepeat legs.")]
    public int[] portalLegs = { 35, 70, 100 };
    [Min(10)] public int portalLegsRepeat = 40;

    public override int PortalLeg(int k, int uniformL)
    {
        if (k <= 0) return 0;
        int n = portalLegs != null ? portalLegs.Length : 0;
        if (n == 0) return k * Mathf.Max(10, portalLegsRepeat);
        if (k <= n) return portalLegs[k - 1];
        return portalLegs[n - 1] + (k - n) * Mathf.Max(10, portalLegsRepeat);
    }

    int StageLen(int k) => Mathf.Max(3, PortalLeg(k + 1, legsPerStage) - PortalLeg(k, legsPerStage));
    int StageOfLeg(int leg)
    {
        leg = Mathf.Max(0, leg);
        int k = 0;
        while (PortalLeg(k + 1, legsPerStage) <= leg && k < 10000) k++;
        return k;
    }
    bool HasStages => stages != null && stages.Count > 0;
    StageTuning StageFor(int k) => stages[Mathf.Clamp(k, 1, stages.Count) - 1];

    // 0..1 across the stage.
    float StageT(int leg, int k) => Mathf.Clamp01((leg - PortalLeg(k, legsPerStage)) / (float)Mathf.Max(1, StageLen(k) - 1));

    // Stages were tuned for 50 legs; the first one (base curve) is stretched so it ends at the same difficulty.
    int Stage0Leg(int leg) => Mathf.RoundToInt(leg * (float)legsPerStage / StageLen(0));

    // Stage shapes are authored for a 50-leg cycle: squeeze positions into the real cycle length.
    WaveShape Fit(WaveShape s, int len)
    {
        float f = len / (float)Mathf.Max(10, legsPerStage);
        if (Mathf.Approximately(f, 1f)) return s;
        int peakEnd = Mathf.Min(Mathf.RoundToInt(s.peakEnd * f), len - 2);
        int buildEnd = Mathf.Clamp(Mathf.RoundToInt(s.buildEnd * f), 2, Mathf.Max(2, peakEnd - 2));
        s.releaseEnd = Mathf.Min(Mathf.RoundToInt(s.releaseEnd * f), buildEnd - 1);
        s.firstHazardLeg = Mathf.Min(Mathf.RoundToInt(s.firstHazardLeg * f), buildEnd);
        s.peakEnd = peakEnd;
        s.buildEnd = buildEnd;
        s.peakBoostLegs = Mathf.Min(s.peakBoostLegs, peakEnd - buildEnd);
        if (s.peakKind == PeakKind.Boost && s.peakBoostLegs < 3) { s.peakKind = PeakKind.None; s.peakBoostLegs = 0; }
        s.onboardingBoost = new Vector2Int(Mathf.RoundToInt(s.onboardingBoost.x * f), s.onboardingBoost.y);
        var boosts = new List<Vector2Int>();
        int prevEnd = -100;
        if (s.buildBoosts != null)
            foreach (var b in s.buildBoosts)
            {
                int st = Mathf.RoundToInt(b.x * f);
                if (st >= prevEnd + 7 && st + b.y <= buildEnd) { boosts.Add(new Vector2Int(st, b.y)); prevEnd = st + b.y; }
            }
        s.buildBoosts = boosts.ToArray();
        var glides = new List<int>();
        if (s.glideLegs != null)
            foreach (int g in s.glideLegs)
            {
                int v = Mathf.RoundToInt(g * f);
                if (v > 0 && v < buildEnd && !glides.Contains(v)) glides.Add(v);
            }
        s.glideLegs = glides.ToArray();
        return s;
    }

    float ExtraSpeed(int k) => k > stages.Count ? speedPerExtraStage * (k - stages.Count) : 0f;

    float StageSpeedAt(int k, float t)
    {
        StageTuning st = StageFor(k);
        return Mathf.Min(speedCeiling, Mathf.Lerp(st.speedStart, st.speedEnd, t) + ExtraSpeed(k));
    }

    float StageEndSpeed(int k) => k <= 0 ? base.SpeedForLeg(Mathf.Max(10, legsPerStage) - 1) * stage0SpeedScale : StageSpeedAt(k, 1f);

    // ---------------------------------------------------------------------- overrides

    public override float SpeedForLeg(int leg)
    {
        int k = StageOfLeg(leg);
        if (k <= 0 || !HasStages) return base.SpeedForLeg(k <= 0 ? Stage0Leg(leg) : leg) * (k <= 0 ? stage0SpeedScale : 1f);
        float v = StageSpeedAt(k, StageT(leg, k));
        int j = leg - PortalLeg(k, legsPerStage);
        if (j < speedBlendLegs)
            v = Mathf.Lerp(StageEndSpeed(k - 1), v, Smooth01((j + 1f) / (speedBlendLegs + 1f)));
        return v;
    }

    public override Vector2 DurationRange(int leg)
    {
        int k = StageOfLeg(leg);
        if (k <= 0 || !HasStages) return base.DurationRange(k <= 0 ? Stage0Leg(leg) : leg);
        StageTuning st = StageFor(k);
        return Vector2.Lerp(st.durStart, st.durEnd, StageT(leg, k));
    }

    public override float THuman(int taps, int leg, bool boost)
    {
        int k = StageOfLeg(leg);
        if (k <= 0 || !HasStages) return base.THuman(taps, k <= 0 ? Stage0Leg(leg) : leg, boost);
        if (taps <= 0) return tHuman0;
        StageTuning st = StageFor(k);
        Vector2 band = taps == 1 ? st.tHuman1 : st.tHuman2;
        float t = Mathf.Lerp(band.x, band.y, StageT(leg, k));
        if (boost) t = Mathf.Max(t * boostWindowScale, taps == 1 ? boostMin1Tap : boostMin2Tap);
        return t;
    }

    public override float BuildAvgForCycle(int cycle) => cycle <= 0 || !HasStages ? base.BuildAvgForCycle(cycle) : StageFor(cycle).buildAvg;
    public override float PeakForCycle(int cycle) => cycle <= 0 || !HasStages ? base.PeakForCycle(cycle) : StageFor(cycle).peakBudget;
    public override int LegCostCapForCycle(int cycle) => cycle <= 0 || !HasStages ? base.LegCostCapForCycle(cycle) : StageFor(cycle).legCostCap;
    public override WaveShape ShapeForCycle(int cycle) => cycle <= 0 || !HasStages ? Fit(base.ShapeForCycle(cycle), StageLen(0)) : Fit(StageFor(cycle).shape, StageLen(cycle));
}
