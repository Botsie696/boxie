using UnityEngine;

/// <summary>
/// CHECKPOINT STARTS (MainScene prop camera 3.6; inert everywhere else). Two jobs:
///
///   Capture   <see cref="CaptureState"/> copies the per-run counters into a GeneratorState for a CheckpointSnapshot.
///   Start     With <see cref="checkpointStartPortal"/> above 0, GenerateNewPath does not lay the opening leg: it puts the
///             counters where a real run has them at the moment portal k's runway OUT is laid (PortalPrebuildExit) -
///             computed from the leg formulas, not copied from a save - and lays exactly that runway, through the very
///             same AppendSegment path (portalExitPending, so it is lengthened, lit, decorated and starts a wave cycle
///             like the real one). The hold stays on until PortalZoneController crosses the door, so the runner comes out
///             of the exit tower and ordinary generation carries on from there, exactly as after a real crossing.
///
/// Nominal legs (portal k, L = legsBetweenPortals): entry runway serial kL - 1 (the score when it is crossed), exit
/// runway serial kL (it scores on arrival, so the score on the runway out is kL), next portal at (k + 1)L.
/// </summary>
public partial class DynamicPathGenerator
{
    /// <summary>0 = a normal start. k > 0 = the next path built starts at the runway out of portal k (checkpoint k + 1).
    /// Runtime only (CheckpointRunController sets it); it stays set so soft restarts go back to the same checkpoint.</summary>
    [System.NonSerialized] public int checkpointStartPortal;
    /// <summary>The scene's firstCube is hidden on a checkpoint start (nothing stands behind an exit tower).</summary>
    bool firstCubeHidden;

    /// <summary>A checkpoint start has laid its runway out but the runner has not come through the door yet: nothing
    /// scores (the runner is parked off the path), so the door is crossed at the entry runway's score, as in a real run.</summary>
    bool CheckpointAwaitingCross => checkpointStartPortal > 0 && portalHold && PortalsTaken < checkpointStartPortal;

    /// <summary>Tooling (menu slice bake): the n-th leg currently laid (0 = the oldest kept), or null.</summary>
    public Segment SegmentAt(int i) => i >= 0 && i < allSegments.Count ? allSegments[i] : null;
    /// <summary>Tooling (menu slice bake): lay the legs ahead now instead of on the next Update.</summary>
    public void LayAheadNow() => EnsureZonesAhead();

    /// <summary>Score (legs reached) while on the runway out of portal k.</summary>
    public int CheckpointLeg(int portal) => PortalLegOf(portal);

    /// <summary>Leg of portal k's runway out: the profile's schedule if it has one, else k x legsBetweenPortals.</summary>
    int PortalLegOf(int k)
    {
        if (endlessLevel > 0 && k >= endlessLevel) return EndlessLegs + (k - endlessLevel) * 50;
        return NominalPortalLegOf(k);
    }

    /// <summary>The same leg ignoring endless mode (the level's real length, for the leg-stage thirds).</summary>
    public int NominalCheckpointLeg(int portal) => NominalPortalLegOf(portal);

    /// <summary>ENDLESS practice (runtime only, set by CheckpointRunController): 0 = off. N > 0 = checkpoint level N never
    /// ends - every door from portal N on is pushed out of reach, so no door is laid and every "is this leg inside level N"
    /// test (themes, leg stages) stays true. The score on screen counts from 0 (<see cref="ScoreBase"/>).</summary>
    [System.NonSerialized] public int endlessLevel;
    const int EndlessLegs = 1000000;
    /// <summary>Legs subtracted from the score on screen (an endless run on level N starts at that level's entry leg).</summary>
    public int ScoreBase { get; private set; }
    /// <summary>Scene 3.11 only (set by CheckpointRunController): a run started at level N shows 0 on the runway in.</summary>
    [System.NonSerialized] public bool levelScoreFromZero;

    int NominalPortalLegOf(int k)
    {
        if (k <= 0) return 0;
        int L = Mathf.Max(3, legsBetweenPortals);
        return difficultyProfile != null ? difficultyProfile.PortalLeg(k, L) : k * L;
    }

    public void CaptureState(GeneratorState s)
    {
        s.legsBetweenPortals = legsBetweenPortals;
        s.nextPortalSerial = nextPortalSerial;
        s.highestScoredSerial = highestScoredSerial;
        s.turnBiasBlock = turnBiasBlock;
        s.leftWeight = leftWeight; s.rightWeight = rightWeight; s.upWeight = upWeight; s.downWeight = downWeight;
        s.bonusPoints = BonusPoints;
        s.closeCallStreak = CloseCallStreak;
        s.cycleIndex = cycleIndex; s.cycleStartSerial = cycleStartSerial;
        s.breatherFrom = breatherFrom; s.breatherUntil = breatherUntil; s.lastHeavyEndSerial = lastHeavyEndSerial;
        s.taughtDecoy = taughtDecoy; s.taughtBridge = taughtBridge; s.taughtMulti = taughtMulti;
        s.peakBoostDone = peakBoostDone; s.buildBoostDone = buildBoostDone;
        s.decoysThisCycle = decoysThisCycle; s.lastDecoySerial = lastDecoySerial; s.family = (int)family; s.peelSide = peelSide;
        s.budgetCredit = budgetCredit; s.budgetSpent = budgetSpent; s.budgetPhaseKey = budgetPhaseKey;
        s.nextBoostSerial = nextBoostSerial; s.boostPlanIndex = boostPlanIndex; s.boostPlanCount = boostPlanDirs.Count;
        s.boostSerialCounter = boostSerialCounter;
        s.powerNextSerial = powerNextSerial; s.powerQuietUntil = powerQuietUntil; s.powerQuietFrom = powerQuietFrom;
        s.powerHandoverSerial = powerHandoverSerial;
        s.lastPowerUpSerial = lastPowerUpSerial; s.shieldNextSerial = shieldNextSerial; s.shieldWaitLegs = shieldWaitLegs;
        s.shieldRolled = shieldRolled; s.shieldWaitHazard = shieldWaitHazard;
    }

    /// <summary>Called by GenerateNewPath after its resets: true = it laid the checkpoint's runway out (skip the normal opening).</summary>
    bool TryLayCheckpointStart()
    {
        int k = checkpointStartPortal;
        // Every path cube is cloned from cubePrefab, which is firstCube itself by default: clone from an untouched,
        // inactive copy instead, so hiding firstCube never reaches the cubes laid from it (colliders included).
        if (k > 0 && firstCube != null && cubePrefab == firstCube.gameObject && !firstCubeHidden)
        {
            GameObject template = Instantiate(firstCube.gameObject, transform);
            template.name = "PathCubeTemplate";
            template.SetActive(false);
            cubePrefab = template;
        }
        ShowFirstCube(k <= 0);
        ScoreBase = 0;
        if (k <= 0 || !portalZones || firstCube == null) return false;

        int L = Mathf.Max(3, legsBetweenPortals);
        int exitSerial = PortalLegOf(k);
        if (endlessLevel > 0 || levelScoreFromZero) ScoreBase = exitSerial - 1;   // the screen shows 0 on the runway in, 1 as the runway out scores

        // ---- counters as a real run has them inside PortalPrebuildExit for portal k ----
        segmentSerial = exitSerial;
        PortalsTaken = k - 1;                       // PortalCrossed makes it k
        nextPortalSerial = PortalLegOf(k + 1);
        portalHold = true;                          // set by OnPortalEntryLaid; released by the crossing
        Score = exitSerial - 1;                     // the entry runway's serial: the score when the door is crossed
        highestScoredSerial = exitSerial - 1;
        lastScoredSegment = new Segment { serial = exitSerial - 1 };   // stands for the entry runway: the runway out scores on arrival
        if (player != null && !tutorialActive) player.SetLegSpeed(Score);

        // Waves: cycle k - 1 is running; laying the runway out begins cycle k (BeginCycle). The opening cycle teaches the
        // decoy and the single bridge, the next one the multi-plank bridge, so by portal 2 everything has been taught.
        cycleIndex = k - 1;
        cycleStartSerial = PortalLegOf(k - 1);
        taughtDecoy = taughtBridge = true;
        taughtMulti = k >= 2;
        lastHeavyEndSerial = -100;
        breatherFrom = breatherUntil = -1;
        lastDecoySerial = -100;

        // Boosts restart their gap from the door (as PortalPrebuildExit does).
        boostPlanDirs.Clear();
        boostPlanLengths.Clear();
        boostPlanIndex = 0;
        nextBoostSerial = exitSerial + RollBoostGap();

        // Diamond and shield: a fresh random place in their usual gap, counted from the door.
        if (powerDiamonds && powerDebugFirstLeg < 0) powerNextSerial = exitSerial + 1 + powerRng.Next(0, Mathf.Max(powerMinGapLegs, powerMaxGapLegs));
        if (enableShield && shieldNextSerial < exitSerial)
        {
            var S = ShieldCfg;
            shieldNextSerial = exitSerial + 1 + shieldRng.Next(0, Mathf.Max(1, Mathf.Max(S.gapMin, S.gapMax)));
        }

        // ---- the runway out, through the same path PortalPrebuildExit uses ----
        Vector3 outDir = FlatCardinal(firstCube.forward);
        Vector3 start = firstCube.position;
        lastPosition = start - outDir * cubeSize;
        lastDirection = outDir;
        lastHorizontalDirection = outDir;
        dirBeforeLast = Vector3.zero;
        firstSegmentPlaced = false;

        portalExitPending = true;
        AppendSegment();
        portalExitPending = false;
        for (int i = 0; i < portalLegsAfterExit; i++) AppendSegment();

        // The door zone keeps the yaw a real crossing frames it with: looking AGAINST the way out (the exit archway's front).
        if (portalIslandExit && PortalExitSegment != null)
        {
            for (int n = 0; n < 4; n++)
            {
                float y = Mathf.Repeat(portalCameraYawOffset + n * 90f, 360f);
                Vector3 f = YawForward(y);
                if (Vector3.Dot(f, outDir) < -0.3f) { LockPortalYaw(PortalExitSegment.zone, y); break; }
            }
        }
        return true;
    }

    // Hidden on a checkpoint start (renderers and colliders only - the generator still reads its pose), shown otherwise.
    void ShowFirstCube(bool show)
    {
        if (firstCube == null || firstCubeHidden == !show) return;
        firstCubeHidden = !show;
        foreach (Renderer r in firstCube.GetComponentsInChildren<Renderer>(true)) r.enabled = show;
        foreach (Collider c in firstCube.GetComponentsInChildren<Collider>(true)) c.enabled = show;
    }
}
