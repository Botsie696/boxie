using System;
using UnityEngine;

/// <summary>One row of the tier table (scene 3.11 adaptive difficulty).</summary>
[Serializable]
public struct TierRow311
{
    public string name;
    [Tooltip("Hazard budget multiplier.")] public float budget;
    [Tooltip("T_human + seconds.")] public float tHumanAdd;
    [Tooltip("Rescue window + seconds.")] public float rescueAdd;
    [Tooltip("Runway stations +/-.")] public int stations;
    [Tooltip("Boost length +/- share (0.1 = +10%).")] public float boostLength;
    [Tooltip("Decoy chance multiplier.")] public float decoys;
    [Tooltip("Extra release legs.")] public int releaseAdd;
    [Tooltip("Shield frequency multiplier.")] public float shieldFreq;
    [Tooltip("Speed multiplier (only applied at mercy step >= 1).")] public float speed;
}

/// <summary>
/// Every number of the 3.11 skill model (SkillProfile311 / SkillModel311): the tier table, the Elo-style rating, the
/// pacing targets and the mercy / push rules. Asset: Assets/Scene311/Data/SkillTuning311.asset.
/// </summary>
[CreateAssetMenu(menuName = "Runner/Scene 3.11 Skill Tuning", fileName = "SkillTuning311")]
public class SkillTuning311 : ScriptableObject
{
    [Header("Tiers (index 0 Struggling .. 4 Expert); interpolated continuously by rating")]
    public TierRow311[] tiers =
    {
        new TierRow311 { name = "Struggling", budget = 0.75f, tHumanAdd = 0.10f, rescueAdd = 0.04f, stations = -2, boostLength = -0.30f, decoys = 0.5f, releaseAdd = 2, shieldFreq = 1.5f, speed = 0.94f },
        new TierRow311 { name = "Below",      budget = 0.88f, tHumanAdd = 0.05f, rescueAdd = 0.02f, stations = -1, boostLength = -0.15f, decoys = 0.75f, releaseAdd = 1, shieldFreq = 1.25f, speed = 1f },
        new TierRow311 { name = "Average",    budget = 1.00f, tHumanAdd = 0f,    rescueAdd = 0f,    stations = 0,  boostLength = 0f,     decoys = 1f,    releaseAdd = 0, shieldFreq = 1f,    speed = 1f },
        new TierRow311 { name = "Above",      budget = 1.08f, tHumanAdd = -0.02f, rescueAdd = -0.01f, stations = 1, boostLength = 0.10f,  decoys = 1.1f,  releaseAdd = 0, shieldFreq = 1f,    speed = 1f },
        new TierRow311 { name = "Expert",     budget = 1.15f, tHumanAdd = -0.03f, rescueAdd = -0.02f, stations = 2, boostLength = 0.20f,  decoys = 1.2f,  releaseAdd = 0, shieldFreq = 0.8f,  speed = 1f },
    };
    [Tooltip("Rescue window caps: never above (Struggling) / never below (Expert).")]
    public float rescueCap = 0.12f, rescueMin = 0.02f;
    [Tooltip("Runway stations: never under (Struggling) / over.")]
    public int stationsMin = 4, stationsMax = 10;

    [Header("Rating (Elo-style)")]
    public float startRating = 1500f;
    [Tooltip("Rating points per tier step (Average = startRating, Expert = +2 steps, Struggling = -2).")]
    public float pointsPerTier = 150f;
    [Tooltip("K factor for a new player, the floor it decays to, and the legs over which it halves.")]
    public float kStart = 40f, kMin = 6f, kHalfLegs = 300f;
    [Tooltip("Feature ratings move this much slower (they see fewer legs).")]
    public float featureKScale = 1.5f;
    [Tooltip("Leg rating = legBase + costWeight x cost + slackWeight x max(0, slackRef - slack).")]
    public float legBase = 700f, costWeight = 60f, slackWeight = 900f, slackRef = 0.35f;
    [Tooltip("Logistic scale (Elo 400: 10x odds per 400 points).")]
    public float eloScale = 400f;

    [Header("Applying the tier")]
    [Tooltip("The first runs ever only collect data (Average tier).")]
    public int collectOnlyRuns = 3;
    [Tooltip("A feature this many tiers below the overall rating is a weak spot: its share -30% and the Below values.")]
    public float weakFeatureTiers = 1f;

    [Header("Pacing: target attempts from a checkpoint before its first clear (Average tier), per level")]
    public Vector2Int[] attemptTargets =
    {
        new Vector2Int(1, 3), new Vector2Int(2, 5), new Vector2Int(3, 6), new Vector2Int(5, 10), new Vector2Int(8, 14),
        new Vector2Int(4, 8), new Vector2Int(14, 24), new Vector2Int(20, 32), new Vector2Int(30, 45),
    };
    [Tooltip("Cleared below the low target: the next checkpoint starts this many tiers harder (capped at Expert).")]
    public float pushTiers = 0.5f;
    [Tooltip("Mercy: one step (one tier easier) per this share over the high target, at most mercyMaxSteps (floor Struggling).")]
    public float mercyStepShare = 0.25f;
    public int mercyMaxSteps = 2;

    [Header("Development")]
    public bool devOverlay = true;

    public Vector2Int TargetFor(int checkpoint) =>
        attemptTargets != null && attemptTargets.Length > 0 ? attemptTargets[Mathf.Clamp(checkpoint - 1, 0, attemptTargets.Length - 1)] : new Vector2Int(3, 8);

    /// <summary>The tier table at a continuous tier (-2 Struggling .. +2 Expert).</summary>
    public TierRow311 RowAt(float tier)
    {
        float x = Mathf.Clamp(tier, -2f, 2f) + 2f;
        int i = Mathf.Clamp(Mathf.FloorToInt(x), 0, tiers.Length - 2);
        float u = Mathf.Clamp01(x - i);
        TierRow311 a = tiers[i], b = tiers[i + 1];
        return new TierRow311
        {
            name = u < 0.5f ? a.name : b.name,
            budget = Mathf.Lerp(a.budget, b.budget, u),
            tHumanAdd = Mathf.Lerp(a.tHumanAdd, b.tHumanAdd, u),
            rescueAdd = Mathf.Lerp(a.rescueAdd, b.rescueAdd, u),
            stations = Mathf.RoundToInt(Mathf.Lerp(a.stations, b.stations, u)),
            boostLength = Mathf.Lerp(a.boostLength, b.boostLength, u),
            decoys = Mathf.Lerp(a.decoys, b.decoys, u),
            releaseAdd = Mathf.RoundToInt(Mathf.Lerp(a.releaseAdd, b.releaseAdd, u)),
            shieldFreq = Mathf.Lerp(a.shieldFreq, b.shieldFreq, u),
            speed = Mathf.Lerp(a.speed, b.speed, u),
        };
    }

    public static string TierName(float tier)
    {
        int i = Mathf.Clamp(Mathf.RoundToInt(tier) + 2, 0, 4);
        return i == 0 ? "Struggling" : i == 1 ? "Below" : i == 2 ? "Average" : i == 3 ? "Above" : "Expert";
    }
}
