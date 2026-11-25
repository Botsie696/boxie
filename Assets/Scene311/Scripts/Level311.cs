using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>The pattern a level's FINALE holds (scene 3.11).</summary>
public enum FinaleSignature311
{
    Mild = 0,            // no special pattern (L6)
    TwoRollPairs = 1,    // L1: pairs of 2-roll corners, no specials
    Staircase = 2,       // L2: a staircase of short alternating legs
    LongBoostExit = 3,   // L3: a long boost straight into a 2-roll exit
    DecoyGauntlet = 4,   // L4: decoys, every one followed by a 2-roll
    RunwaySlowBurst = 5, // L5: runway (Fence + Drifter) -> slow motion -> short-leg burst
    SliderRunwayBurst = 7, // L7: CornerSlider runway into a short-leg burst
    LongBoostDecoys = 8, // L8: long boost (x1.5) into decoys
    MixAll = 9,          // L9: every hazard mixed, one double spike
}

/// <summary>Boost modes (scene 3.11). B1 classic zig-zag, B2 long (x1.5), B3 broken rhythm, B4 boost exit (2-roll corner after).</summary>
public enum BoostMode311 { Classic = 0, Long = 1, Broken = 2, Exit = 3 }

/// <summary>Per-level 3.11 numbers that LevelTuning310 has no field for. Index-aligned with Scene311DifficultyProfile.levels.</summary>
[Serializable]
public class LevelExtra311
{
    [Tooltip("Hazard waves between the release legs and the finale.")]
    [Range(1, 4)] public int waves = 2;
    [Tooltip("Legs in the finale (the last legs before the 2 exhale legs).")]
    public int finaleLegs = 20;
    public FinaleSignature311 signature = FinaleSignature311.Mild;

    [Header("Close-call rescue")]
    [Range(0f, 0.15f)] public float rescueWindow = 0.09f;
    public float rescueReachCubes = 3.5f;

    [Header("Boosts")]
    public int boosts = 1;
    [Tooltip("Highest boost mode unlocked in this level (B1 Classic .. B4 Exit). -1 = no boosts.")]
    public int newestBoostMode = 0;

    [Header("Obstacle runways")]
    public int runways = 1;
    [Tooltip("Stations on each runway (x = min, y = max).")]
    public Vector2Int stations = new Vector2Int(5, 5);
    [Tooltip("Design weights for this level's runways, by DynamicPathGenerator.DesignKind: Totem, Pair, Corner, Fence, Drifter, Twin, CornerSlider.")]
    public int[] designWeights = new int[7];
    [Tooltip("Gap between stations = reaction floor x this (+ up to gapJitter).")]
    public float gapFactor = 1.2f;
    [Range(0f, 0.5f)] public float gapJitter = 0.25f;
    [Tooltip("Sliding pillars: speed as a share of the runner's (min, max). 0 = no sliders.")]
    public Vector2 sliderSpeed = Vector2.zero;
    [Tooltip("Most sliding stations on one runway (L4: 1 Drifter).")]
    public int maxSliders = 99;
    [Tooltip("Designs forced for the finale runway (signature), by DesignKind. Empty = the level's own mix.")]
    public int[] finaleDesigns = new int[0];

    [Header("Slow motion")]
    [Tooltip("Slow-motion sections in the waves (the finale one, if the signature has one, comes on top).")]
    public int slowMos = 0;

    [Header("Power-ups")]
    [Tooltip("Autopilot / shield frequency (x0.7 from L4, x0.5 from L7). Magnet unchanged.")]
    public float powerFrequency = 1f;
}

/// <summary>One leg's place in its level (scene 3.11).</summary>
public struct LegPlan311
{
    public WavePhase phase;     // Release / Build / Peak (wave peaks and the finale) / Cooldown (exhale legs)
    public bool glide, finale, exhale, release;
    public int wave;            // 0-based wave, -1 outside the waves
    public float intensity;     // 0.5 .. 1: hazard ramp inside a wave
    public float budget;        // hazard budget per leg (build average / peak), tier-scaled
    public int wantTaps;        // -1 = any; 2 = the turn at the end of this leg should be a half turn
    public int family;          // remix family forced on this leg, -1 = none
    public float decoyScale;    // multiplier on the level's decoy chance
    public float durScale;      // multiplier on the leg-duration range (short-leg bursts < 1)
    public bool spike;          // L9 double spike: the cap rule is lifted after this leg
}

/// <summary>A special the 3.11 level plan places (generator-side SpecialSlot is built from it).</summary>
public struct Special311
{
    public int kind;            // 0 boost, 1 slow motion, 2 runway
    public int at;              // cycle position (fixed) or -1 = place me inside the waves
    public int legs;            // boost legs / slow-mo tiny legs / 1 for a runway
    public BoostMode311 mode;
    public bool finale;
}

/// <summary>Everything the generator needs about one 3.11 level, worked out once when the level starts.</summary>
public class LevelSpec311
{
    public int level;           // 1-based
    public bool freefall;
    public int length;
    public LegPlan311[] plan;
    public FinaleSignature311 signature;
    public int finaleStart, exhaleStart, releaseEnd;

    // specials
    public readonly List<Special311> fixedSpecials = new List<Special311>();   // finale ones at fixed positions
    public int boosts;
    public Vector2 boostLegs;   // (min, max) legs of a classic boost
    public float[] modeWeights = new float[4];
    public int runways;
    public Vector2Int stations;
    public int[] designWeights;
    public int[] finaleDesigns;
    public float gapFactor, gapJitter;
    public Vector2 sliderSpeed;
    public int maxSliders = 99;
    public int slowMos;
    public int slowMoTinyLegs;

    // misc
    public float rescueWindow, rescueReach;
    public int costAfterCap;
    public float powerGapScale = 1f, shieldGapScale = 1f;
    public bool finaleGlide;

    public LegPlan311 PlanAt(int pos)
    {
        if (plan == null || plan.Length == 0) return default;
        return plan[Mathf.Clamp(pos, 0, plan.Length - 1)];
    }
}

/// <summary>
/// The player's adaptive tier, as modifiers the 3.11 profile reads. Written ONLY at run start and at portals
/// (SkillProfile311 via Adaptive311.Apply), so a level is laid with one set of numbers from its door to the next.
/// Default = Average (no change). Enabled = false: the profile ignores it (bots, freefall).
/// </summary>
[Serializable]
public struct TierMods311
{
    public float tier;          // continuous: -2 Struggling .. 0 Average .. +2 Expert
    public float budget;        // x
    public float tHumanAdd;     // + s
    public float rescueAdd;     // + s
    public float rescueCap;     // max rescue window
    public float rescueMin;     // min rescue window
    public int stationsAdd;
    public int stationsMin, stationsMax;
    public float boostLength;   // x
    public float decoys;        // x
    public int releaseAdd;
    public float shieldFreq;    // x
    public float speed;         // x (Struggling at mercy step >= 1 only)
    public bool finaleGlide;    // Below / Struggling: one mid-finale glide
    public int mercyStep;
    // per-feature weak spots: these features get the Below values and 30% less share
    public bool weakDecoy, weakBoost, weakRunway, weakSlowMo, weakTwoRoll;

    public static TierMods311 Average => new TierMods311
    {
        tier = 0f, budget = 1f, tHumanAdd = 0f, rescueAdd = 0f, rescueCap = 0.15f, rescueMin = 0f, stationsAdd = 0,
        stationsMin = 1, stationsMax = 10, boostLength = 1f, decoys = 1f, releaseAdd = 0, shieldFreq = 1f, speed = 1f,
    };
}

public static class Adaptive311
{
    static bool enabled = true;
    /// <summary>False = the profile uses the base (Average) numbers whatever the tier says.</summary>
    public static bool Enabled
    {
        get => enabled;
        set { if (enabled == value) return; enabled = value; Version++; }
    }
    static TierMods311 mods = TierMods311.Average;
    public static int Version { get; private set; }

    public static TierMods311 Mods => Enabled ? mods : TierMods311.Average;

    /// <summary>Called at run start and at portals only.</summary>
    public static void Apply(TierMods311 m)
    {
        mods = m;
        Version++;
    }

    public static void Reset()
    {
        mods = TierMods311.Average;
        Version++;
    }
}
