using UnityEngine;

/// <summary>What the PEAK phase of a portal cycle holds. Auto = the original rule (odd cycles a boost, even cycles a bridge cluster).</summary>
public enum PeakKind { Auto, Boost, Bridges, None }

/// <summary>
/// Shape of one portal cycle's wave (positions are legs after the portal's exit runway). A plain DifficultyProfile returns
/// the same shape for every cycle, built from its own fields; a subclass may return a different one per cycle.
/// </summary>
[System.Serializable]
public struct WaveShape
{
    public int releaseEnd, buildEnd, peakEnd, firstHazardLeg;
    [Tooltip("Cycle 0 only: (start, legs).")]
    public Vector2Int onboardingBoost;
    public PeakKind peakKind;
    public int peakBoostLegs;
    [Tooltip("(start, legs) in order; each must start at least 7 legs after the previous boost ends.")]
    public Vector2Int[] buildBoosts;
    public int[] clusterBridgeLegs;
    public int[] glideLegs;
    public float glideLengthScale;
    [Tooltip("Featured hazard: -1 = rotate by cycle, 0 obstacle, 1 decoy, 2 bridge, 3 multi-plank, 4 mixed, 5 none.")]
    public int featured;
    [Tooltip("Late-game remix families (staircase, peel, climb/dive, plank weave, decoy gauntlet) in BUILD.")]
    public bool families;
}

/// <summary>
/// Every number of the fair-difficulty retune, in one asset per scene. Assign it on DynamicPathGenerator
/// (Difficulty Profile); CubeRailRunner and the camera read it from there. A scene with no profile
/// assigned runs exactly as it always did, so each feature below can be switched on in another scene
/// just by giving that scene a profile (or copying this one and unticking what it should not get).
///
/// Times are seconds of game time, lengths are cubes, speeds are world units per second at
/// sizeMultiplier 1 (the runner's speedScale still applies on top).
/// </summary>
[CreateAssetMenu(menuName = "Runner/Difficulty Profile", fileName = "DifficultyProfile")]
public class DifficultyProfile : ScriptableObject
{
    /// <summary>The generator's profile in the running scene (set in DynamicPathGenerator.Awake); lets the checkpoint set ask for portal legs.</summary>
    public static DifficultyProfile Active;

    /// <summary>Leg (score) of portal k's runway out. Default = uniform spacing k x uniformL; a subclass may use its own schedule.</summary>
    public virtual int PortalLeg(int k, int uniformL) => k <= 0 ? 0 : k * uniformL;

    // =====================================================================================
    [Header("Feature switches (untick to keep the old behaviour for that part)")]
    [Tooltip("Lay every leg, boost leg, plank gap and portal runway against a TIME floor (section 3) instead of fixed cube counts.")]
    public bool timeBudgetLegs = true;
    [Tooltip("Use the hold + exponential speed curve below (section 4) instead of the runner's authored AnimationCurve.")]
    public bool useProfileSpeedCurve = true;
    [Tooltip("Fair input: fire on touch-down, every finger counts, cross-corner buffer, short landing lock, double-tap flip, no hidden left/right swap (section 5).")]
    public bool fairInput = true;
    [Tooltip("Corners are decided by the runner's LOGICAL face and exact distance (no forward-probe race), and a correct-but-slightly-late input is rescued (close call).")]
    public bool fairCorners = true;
    [Tooltip("Waves tied to the portal cycle, hazard budgets and breathers (section 6). Replaces 'boost every 12-16 legs'.")]
    public bool wavePacing = true;
    [Tooltip("Camera zoom caps and rotation timing from section 7.")]
    public bool cameraOverrides = true;
    [Tooltip("Close-call effect, +1 bonus popup, chevron hold cue and success feedback.")]
    public bool feedbackFX = true;
    [Tooltip("Write per-leg and per-death CSV logs to Application.persistentDataPath (RunTelemetry). Off for release builds.")]
    public bool telemetry = false;
    [Tooltip("Log an error whenever a laid leg, plank gap or boost leg is shorter than its time floor.")]
    public bool validateFloors = true;

    // =====================================================================================
    [Header("Speed curve (section 4)")]
    [Tooltip("Speed on the opening legs, u/s (old: 14 x 0.85 = 11.9).")]
    public float vStart = 11.9f;
    [Tooltip("Speed the curve closes on, u/s. Boost x1.2 rides on top (about 38.4).")]
    public float vCap = 32f;
    [Tooltip("Legs per e-fold of the climb from vStart to vCap. Bigger = slower climb.")]
    public float tau = 90f;
    [Tooltip("Legs held at vStart before the climb starts.")]
    public int holdLegs = 6;
    [Tooltip("Seconds each per-leg speed change is eased over, so a new leg never jolts.")]
    public float speedEaseSeconds = 0.3f;
    [Tooltip("Keep the runner's old stepped/front-loaded AnimationCurve instead (overrides the curve above).")]
    public bool steppedSpeed = false;

    // =====================================================================================
    [Header("Leg duration ranges (section 4) - cubes = duration x speed")]
    [Tooltip("Legs 0-15: shortest / longest ordinary leg, seconds.")]
    public Vector2 durOnboarding = new Vector2(1.20f, 1.55f);
    [Tooltip("Eased to by leg 60 (from the onboarding range, smoothstep over legs 15-60).")]
    public Vector2 durRamp = new Vector2(0.90f, 1.25f);
    [Tooltip("Eased to by leg 150 (smoothstep over legs 60-150).")]
    public Vector2 durMastery = new Vector2(0.75f, 1.05f);
    [Tooltip("Legs 150+.")]
    public Vector2 durFloor = new Vector2(0.70f, 1.00f);
    [Tooltip("The opening leg of a run is at least this many seconds.")]
    public float firstLegSeconds = 1.8f;

    // =====================================================================================
    [Header("Human time budget T_human (section 3)")]
    [Tooltip("1-tap T_human: onboarding (legs 0-15), end of ramp (leg 60), end of mastery (leg 150), floor (150+).")]
    public Vector4 tHuman1 = new Vector4(0.90f, 0.55f, 0.40f, 0.38f);
    [Tooltip("2-tap T_human at the same four points.")]
    public Vector4 tHuman2 = new Vector4(1.00f, 0.65f, 0.48f, 0.45f);
    [Tooltip("0-tap legs: time to confirm 'stay'.")]
    public float tHuman0 = 0.35f;
    [Tooltip("Leg where onboarding ends / ramp ends / mastery ends.")]
    public Vector3Int phaseLegs = new Vector3Int(15, 60, 150);
    [Tooltip("Boost legs get this share of the ordinary T_human (they are meant to feel fast)...")]
    [Range(0.5f, 1f)] public float boostWindowScale = 0.9f;
    [Tooltip("...but never less than this for a 2-tap boost leg.")]
    public float boostMin2Tap = 0.40f;
    [Tooltip("...and never less than this for a 1-tap boost leg.")]
    public float boostMin1Tap = 0.34f;

    // =====================================================================================
    [Header("Mechanics (section 5) - the overhead in every floor uses these live values")]
    [Tooltip("Seconds a face roll plays.")]
    public float rollTime = 0.10f;
    [Tooltip("Seconds between two queued rolls.")]
    public float rollCooldown = 0.08f;
    [Tooltip("Seconds after landing on a leg before the first roll may start (old 0.10).")]
    public float landingLock = 0.03f;
    [Tooltip("Seconds before the corner commit point a roll must have started (logical face). Measured ~1 physics step.")]
    public float cornerDeadline = 0.03f;
    [Tooltip("Where the corner is decided, in cubes from the CENTRE of the leg's last cube (negative = before it). -0.05 is where the runner geometrically meets the next leg, so the visual edge and the deadline agree.")]
    public float cornerCommitOffset = -0.05f;
    [Tooltip("Fire a tap the moment the finger touches (false = old fire-on-lift).")]
    public bool fireOnTouchDown = true;
    [Tooltip("A finger that travels this far before lifting becomes a swipe (only if its roll has not started).")]
    public float swipePixels = 45f;
    [Tooltip("Every new finger is its own input (two thumbs, overlapping touches).")]
    public bool multiTouch = true;
    [Tooltip("A tap within this many seconds BEFORE landing on the next leg is kept and played at landing, for the new leg.")]
    public float preCornerBuffer = 0.15f;
    [Tooltip("Same side tapped twice within doubleTapWindow = one 180 flip. Two separate taps still work the same.")]
    public bool doubleTapFlip = true;
    public float doubleTapWindow = 0.22f;
    [Tooltip("Swipe up/down = 180 flip.")]
    public bool swipeFlip = true;
    [Tooltip("Left/right always mean screen-left/right: turns off the timer-based swap (flipRemapsControls).")]
    public bool disableFlipRemap = true;

    // =====================================================================================
    [Header("Last-moment rescue (close call)")]
    [Tooltip("A CORRECT input up to this many seconds after the corner deadline still makes the corner.")]
    [Range(0f, 0.15f)] public float rescueWindow = 0.09f;
    [Tooltip("No rescue once the runner is this many cubes past the commit point.")]
    public float rescueMaxCubes = 3.5f;
    [Tooltip("The rescued roll plays this fast (normal 0.10).")]
    public float rescueRollTime = 0.06f;
    [Tooltip("A rescue gives this many bonus points (shown as +1 over the runner).")]
    public int rescueBonusPoints = 1;
    [Tooltip("Turning onto the corner's face within this many cubes before the corner (the yellow arrow zone) also scores the +1. 0 = only last-moment rescues score.")]
    public float earlyTurnBonusCubes = 3.5f;
    [Tooltip("Tiny hit-stop on a rescue (never inside boost sections).")]
    public bool rescueHitStop = true;
    public float hitStopSeconds = 0.035f;
    [Range(0.1f, 1f)] public float hitStopTimeScale = 0.4f;
    [Tooltip("Rescue bridge planks too.")]
    public bool rescuePlanks = true;

    // =====================================================================================
    [Header("Bridges (section 3)")]
    [Tooltip("Extra seconds on top of the 1-roll overhead between planks on ADJACENT faces.")]
    public float plankGapAdjacent = 0.30f;
    [Tooltip("Extra seconds on top of the 2-roll overhead between planks on OPPOSITE faces.")]
    public float plankGapOpposite = 0.45f;
    [Tooltip("A bridge leg is never longer than this multiple of the tier's longest leg; planks are dropped to fit.")]
    public float bridgeMaxTierMultiple = 2.5f;

    // =====================================================================================
    [Header("Portal")]
    [Tooltip("Portal runways (in and out) last at least this long at the current speed.")]
    public float portalRunwaySeconds = 1.2f;

    // =====================================================================================
    [Header("Waves (section 6.1) - positions within the 30-leg portal cycle")]
    [Tooltip("Last leg of the RELEASE phase (mostly 0-1 tap, no hazards, camera may rotate).")]
    public int releaseEnd = 5;
    [Tooltip("Last leg of the BUILD phase.")]
    public int buildEnd = 20;
    [Tooltip("Last leg of the PEAK phase (boost or bridge cluster). Then COOL-DOWN up to the portal.")]
    public int peakEnd = 27;
    [Tooltip("First-cycle onboarding boost: starts on this leg and has this many legs.")]
    public Vector2Int onboardingBoost = new Vector2Int(14, 5);
    [Tooltip("Legs in a PEAK boost.")]
    public int peakBoostLegs = 7;
    [Tooltip("From this cycle on, a short BUILD boost (legs) is added at buildBoostStart, so a cycle can hold 2 boosts.")]
    public int secondBoostFromCycle = 3;
    public int buildBoostStart = 9;
    public int buildBoostLegs = 5;
    [Tooltip("Bridge-cluster peak: bridges on these cycle positions.")]
    public int[] clusterBridgeLegs = { 21, 23, 25 };
    [Tooltip("Hazards (bridges, decoys) never before this cycle position: keeps 3 legs between a release-phase camera rotation and the first hazard.")]
    public int firstHazardLeg = 9;
    [Tooltip("A glide leg (long, 0-1 tap, no hazards, coins on the right face) at these cycle positions.")]
    public int[] glideLegs = { 11, 17 };
    [Tooltip("Glide legs are this many times the longest ordinary duration.")]
    public float glideLengthScale = 1.3f;

    // =====================================================================================
    [Header("Hazard budget (section 6.2)")]
    [Tooltip("Cycle 1 BUILD-phase average cost per leg; grows by avgGrowth per cycle up to avgCap.")]
    public float buildAvg = 1.6f;
    public float avgGrowth = 0.08f;
    public float avgCap = 3.2f;
    [Tooltip("Cycle 1 PEAK budget per leg; x peakGrowth per cycle, capped at peakCap.")]
    public float peakBudget = 3.0f;
    public float peakGrowth = 1.12f;
    public float peakCap = 4.5f;
    [Tooltip("Most cost one leg may carry: before cycle 3 / cycles 3-5 / cycle 6+.")]
    public Vector3Int legCostCap = new Vector3Int(5, 6, 7);
    [Tooltip("After a leg costing this much or more, the next costs at most nextLegCostAfterHard.")]
    public int hardLegCost = 5;
    public int nextLegCostAfterHard = 2;
    [Header("Costs")]
    public int costObstacle = 2;
    public int costDecoy = 2;
    public int costBridge = 3;
    public int costExtraPlank = 2;
    public int costOppositePlank = 1;
    public int costCameraRotation = 3;
    public int costBoostLeg = 3;
    [Tooltip("Lure coins (wrong-face coin runs) never on a leg costing this much or more.")]
    public int lureMaxCost = 4;
    [Tooltip("Featured hazard in BUILD is this many times as likely; the others this share.")]
    public float featuredBoost = 1.75f;
    public float nonFeaturedScale = 0.6f;
    [Tooltip("Late-game pattern families start on this cycle.")]
    public int patternFamiliesFromCycle = 5;

    // =====================================================================================
    [Header("Slow motion (scene 3.11 - used only where the generator's slowMoSections is on)")]
    [Tooltip("First level that has a slow-motion section (1 = Start, so 3 = the third level).")]
    public int slowMoFromLevel = 3;
    [Tooltip("Tiny legs in the section at its first level (drawn between these two). The regular-length exit leg comes on top.")]
    public Vector2Int slowMoLegs = new Vector2Int(5, 7);
    [Tooltip("Every later level adds this many tiny legs (drawn between these two).")]
    public Vector2Int slowMoLegsPerLevel = new Vector2Int(2, 4);
    [Tooltip("Never more tiny legs than this.")]
    public int slowMoLegCap = 24;
    [Tooltip("Cubes in each tiny leg (drawn between these two).")]
    public Vector2Int slowMoLegCubes = new Vector2Int(5, 7);
    [Tooltip("Time scale at the first level that has it and at the hardest (higher = less real time per tiny leg).")]
    public Vector2 slowMoTimeScale = new Vector2(0.7f, 0.85f);
    [Tooltip("The runner is slowed (on top of the time scale) until the shortest tiny leg lasts this many times what a 2-roll turn needs: the mechanics in game time + a human's reaction shrunk by the time scale.")]
    [Range(1f, 1.6f)] public float slowMoLegSecondsScale = 1f;

    [Tooltip("The runner is then this much FASTER than that floor (1.2 = 20% faster: the section stays hard).")]
    [Range(1f, 1.5f)] public float slowMoSpeedBoost = 1.2f;

    [Header("Specials per level (scene 3.11 - generator specialsPerLevel)")]
    [Tooltip("Boosts in a level from level 2 on (drawn between these two).")]
    public Vector2Int boostsPerLevel = new Vector2Int(1, 2);
    [Tooltip("From level 6, chance of one more boost on top.")]
    [Range(0f, 1f)] public float boostExtraChance = 0.3f;
    [Tooltip("Legs of a boost at level 2 (drawn between these two); every later level x specialGrowth.")]
    public Vector2Int boostLegsFirst = new Vector2Int(5, 6);
    [Tooltip("Chance one boost of a level is a long one (x longBoostScale legs).")]
    [Range(0f, 1f)] public float longBoostChance = 0.3f;
    [Range(1f, 2.5f)] public float longBoostScale = 1.5f;
    [Tooltip("Never more legs in one boost.")]
    public int boostLegCap = 20;
    [Tooltip("Boost legs and slow-motion tiny legs grow by this per level (with a little jitter that never makes a level easier than the one before).")]
    [Range(1f, 1.6f)] public float specialGrowth = 1.2f;
    [Tooltip("Slow-motion sections in a level after the first that has one: chance of none / one / two (the first level always has one).")]
    public Vector3 slowMoCountChances = new Vector3(0.2f, 0.5f, 0.3f);

    /// <summary>Game seconds a tiny slow-motion leg (a 2-roll turn) must last at time scale `scale`.</summary>
    public float SlowMoLegSeconds(float scale, int leg) => (Overhead(2) + scale * THuman(2, leg, false)) * slowMoLegSecondsScale;

    // =====================================================================================
    [Header("Camera (section 7)")]
    [Tooltip("Speed zoom cap (old 1.4).")]
    public float speedZoomMax = 1.15f;
    [Tooltip("Boost zoom (old 1.3), seconds in, seconds out.")]
    public float boostZoom = 1.15f;
    public float boostZoomIn = 0.5f;
    public float boostZoomOut = 0.4f;
    [Tooltip("The camera may only turn into a zone whose first leg is in the RELEASE phase or is a portal runway.")]
    public bool restrictCameraRotation = true;
    [Tooltip("Rim around the runner so it stays readable at the widest zoom. Empty = no rim.")]
    public Material runnerRimMaterial;
    [Tooltip("Rim size as a multiple of the runner.")]
    public float runnerRimScale = 1.14f;
    [Tooltip("The face lane and the corner chevrons end at the commit point, not the cube's far edge.")]
    public bool showRealDeadline = true;

    // ---------------------------------------------------------------------- maths

    protected static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    /// <summary>Profile speed for a leg, u/s at size 1.</summary>
    public virtual float SpeedForLeg(int leg)
    {
        if (leg < holdLegs) return vStart;
        return vStart + (vCap - vStart) * (1f - Mathf.Exp(-(leg - holdLegs) / Mathf.Max(1f, tau)));
    }

    // Piecewise: flat onboarding, smoothstep to the ramp value, smoothstep to the mastery value, then the floor.
    float Phase(Vector4 v, int leg)
    {
        int a = phaseLegs.x, b = phaseLegs.y, c = phaseLegs.z;
        if (leg <= a) return v.x;
        if (leg <= b) return Mathf.Lerp(v.x, v.y, Smooth01((leg - a) / (float)Mathf.Max(1, b - a)));
        if (leg < c) return Mathf.Lerp(v.y, v.z, Smooth01((leg - b) / (float)Mathf.Max(1, c - b)));
        return v.w;
    }

    Vector2 Phase(Vector2 a0, Vector2 a1, Vector2 a2, Vector2 a3, int leg)
    {
        return new Vector2(Phase(new Vector4(a0.x, a1.x, a2.x, a3.x), leg), Phase(new Vector4(a0.y, a1.y, a2.y, a3.y), leg));
    }

    /// <summary>Human reaction-and-decision budget for a leg needing `taps` rolls.</summary>
    public virtual float THuman(int taps, int leg, bool boost)
    {
        if (taps <= 0) return tHuman0;
        float t = taps == 1 ? Phase(tHuman1, leg) : Phase(tHuman2, leg);
        if (boost) t = Mathf.Max(t * boostWindowScale, taps == 1 ? boostMin1Tap : boostMin2Tap);
        return t;
    }

    /// <summary>Mechanical cost of a leg needing `taps` rolls: landing lock + rolls + cooldowns + deadline.</summary>
    public float Overhead(int taps)
    {
        if (taps <= 0) return landingLock + cornerDeadline;
        return landingLock + taps * rollTime + (taps - 1) * rollCooldown + cornerDeadline;
    }

    /// <summary>Shortest fair duration of a leg needing `taps` rolls.</summary>
    public float MinLegSeconds(int taps, int leg, bool boost) => Overhead(taps) + THuman(taps, leg, boost);

    /// <summary>Shortest and longest ordinary leg duration for a leg index.</summary>
    public virtual Vector2 DurationRange(int leg) => Phase(durOnboarding, durRamp, durMastery, durFloor, leg);

    /// <summary>Minimum gap (seconds) between two planks whose faces are `rolls` apart (0, 1 or 2).</summary>
    public float PlankGapSeconds(int rolls)
    {
        if (rolls <= 0) return 0f;
        if (rolls == 1) return Overhead(1) - cornerDeadline + plankGapAdjacent;
        return Overhead(2) - cornerDeadline + plankGapOpposite;
    }

    public virtual float BuildAvgForCycle(int cycle) => Mathf.Min(avgCap, buildAvg * Mathf.Pow(1f + avgGrowth, Mathf.Max(0, cycle)));
    public virtual float PeakForCycle(int cycle) => Mathf.Min(peakCap, peakBudget * Mathf.Pow(peakGrowth, Mathf.Max(0, cycle)));
    public virtual int LegCostCapForCycle(int cycle) => cycle < 3 ? legCostCap.x : cycle < 6 ? legCostCap.y : legCostCap.z;

    // ---------------------------------------------------------------------- per-level hooks
    // All inert here: a plain profile (and CheckpointDifficultyProfile, scene 3.7) behaves exactly as it always did.
    // Scene310DifficultyProfile overrides them.

    /// <summary>Cycle 0 only: lay the one forced "teaching" decoy fork. False = no decoy at all in the opening level.</summary>
    public virtual bool TeachOpeningDecoy => true;
    /// <summary>The remix family a cycle must use (Staircase 0, Peel 1, ClimbDive 2, PlankWeave 3, DecoyGauntlet 4), or -1 = the usual random pick.</summary>
    public virtual int ForcedFamily(int cycle) => -1;
    /// <summary>Decoy chance (0-1) for legs laid in this cycle, or a negative number = the scene's own decoyChanceByCheckpoint.</summary>
    public virtual float DecoyChanceOverride(int cycle) => -1f;
    /// <summary>Obstacle-runway legs of this cycle (positions in legs after the portal). null = the old random 17-30 leg cadence; empty = none.</summary>
    public virtual int[] RunwayPlan(int cycle) => null;
    /// <summary>Same, rolled once per cycle with the generator's own random (so a profile can randomise the legs). Default = the fixed plan.</summary>
    public virtual int[] RunwayPlan(int cycle, System.Random rng) => RunwayPlan(cycle);
    /// <summary>Multiplier on the scene's coinSpawnChance for legs laid in this cycle.</summary>
    public virtual float CoinChanceScale(int cycle) => 1f;
    /// <summary>Leg (cycle position) from which one power-up diamond is guaranteed in this cycle, or -1 = leave it to the scheduler.</summary>
    public virtual int PowerUpGuaranteePos(int cycle) => -1;

    // ---------------------------------------------------------------------- scene 3.11 hooks
    // Inert here (null / the fields above): only Scene311DifficultyProfile overrides them.

    /// <summary>Scene 3.11: the plan of level `cycle` (leg phases, finale, specials, runways, rescue). null = the usual rules.</summary>
    public virtual LevelSpec311 Spec311(int cycle) => null;
    /// <summary>Close-call rescue window (seconds) on leg `leg`.</summary>
    public virtual float RescueWindowAt(int leg) => rescueWindow;
    /// <summary>No rescue once the runner is this many cubes past the commit point, on leg `leg`.</summary>
    public virtual float RescueReachAt(int leg) => rescueMaxCubes;

    /// <summary>The wave shape of a portal cycle (0 = the opening cycle). The same for every cycle unless a subclass says otherwise.</summary>
    public virtual WaveShape ShapeForCycle(int cycle)
    {
        return new WaveShape
        {
            releaseEnd = releaseEnd, buildEnd = buildEnd, peakEnd = peakEnd, firstHazardLeg = firstHazardLeg,
            onboardingBoost = onboardingBoost, peakKind = PeakKind.Auto, peakBoostLegs = peakBoostLegs,
            buildBoosts = cycle >= secondBoostFromCycle ? new[] { new Vector2Int(buildBoostStart, buildBoostLegs) } : System.Array.Empty<Vector2Int>(),
            clusterBridgeLegs = clusterBridgeLegs, glideLegs = glideLegs, glideLengthScale = glideLengthScale,
            featured = -1, families = cycle >= patternFamiliesFromCycle,
        };
    }
}
