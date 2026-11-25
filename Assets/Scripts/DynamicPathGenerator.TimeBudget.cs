using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TIME-BUDGET LEGS (DifficultyProfile, section 3). With a profile assigned and timeBudgetLegs ticked,
/// every leg is laid against the time a human needs for it, not a cube count:
///
///   minLegTime(taps) = landing lock + taps x roll + (taps - 1) x cooldown + corner deadline + T_human(taps, leg)
///   minCubes        = the cubes that take minLegTime to cross at the speed the runner will have on that leg
///
/// A leg's random length (rolled from DURATION ranges, so the time per leg falls smoothly instead of in
/// tier steps) is floored at the 1-tap minimum when it is laid. Its turn is chosen afterwards, so the
/// turn itself is filtered: a turn needing 2 rolls is only offered when the leg is long enough for 2.
/// Bridges, boosts and portal runways get the same rule (see the Bridge, Boost and Portal partials).
///
/// Every laid leg is checked once its turn is known (FinalizeLeg): any leg, plank gap or boost leg shorter
/// than its floor is logged as an error and counted in FloorViolations.
///
/// Nothing here runs without a profile, so every other scene lays exactly the path it always did.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Difficulty Profile (fair retune)")]
    [Tooltip("The scene's DifficultyProfile asset. Empty = the original behaviour everywhere (legs by cube tiers, old speed curve, old input). Assign one to switch on the time-budget legs, the new speed curve, fair input, close-call rescue, wave pacing and the camera caps (each can be unticked in the asset).")]
    public DifficultyProfile difficultyProfile;

    public partial class Segment
    {
        public int serial = -1;          // segmentSerial when laid (= the score shown on reaching it)
        public int cycle, cyclePos;      // portal cycle and position in it (DynamicPathGenerator.Waves.cs)
        public WavePhase phase;
        public float speed;              // the runner's speed on this leg (boost included)
        public int taps = -1;            // rolls from the landing face to the turn face (-1 = turn not known yet)
        public int landingSide = -1;     // face the runner lands on, in this leg's cube frame
        public int turnSide = -1;        // face it must be on at the corner
        public int hazardCost;           // bridge / decoy / obstacle cost on this leg (section 6.2)
        public int cost = -1;            // full cost, once the next leg is laid
        public bool glide, breather, teaching;
        public bool decoyAtEnd;          // the next leg starts with a decoy fork: this leg's corner has a wrong branch
        public bool obstacle;
        public int plankCount;
        public bool multiPlank;
        public readonly List<Vector3Int> planks = new List<Vector3Int>(); // (first cube, last cube, face)
        public float firstPlankSeconds;  // seconds from landing to the first plank
        public float leadOutSeconds;     // seconds from the last plank to the corner
        public bool floorViolation;
        public float floorSeconds;       // the floor this leg was held to
        public float seconds;            // how long it takes to cross
    }

    public DifficultyProfile Profile => difficultyProfile;
    bool TB => difficultyProfile != null && difficultyProfile.timeBudgetLegs;

    /// <summary>A leg's turn (and so its taps, cost and floor check) is now known. Telemetry and the human sim listen.</summary>
    public event System.Action<Segment> LegFinalized;
    public int FloorViolations { get; private set; }
    public int LegsValidated { get; private set; }

    // ------------------------------------------------------------------ faces

    /// <summary>Face (0 top, 1 right, 2 bottom, 3 left) of a leg heading `legDir` whose normal is closest to `worldNormal`.</summary>
    public int SideOnLeg(Vector3 legDir, Vector3 worldNormal)
    {
        Vector3 l = Quaternion.Inverse(GetRotationForDir(legDir)) * worldNormal;
        if (Mathf.Abs(l.y) > Mathf.Abs(l.x)) return l.y > 0f ? 0 : 2;
        return l.x > 0f ? 1 : 3;
    }

    /// <summary>Rolls from face a to face b: 0, 1 or 2.</summary>
    public static int Rolls(int a, int b)
    {
        if (a < 0 || b < 0 || a == b) return 0;
        return (a + 2) % 4 == b ? 2 : 1;
    }

    // The face the runner lands on: the one facing back along the leg before (the corner cube's back face),
    // or the top for the opening leg and a portal's exit runway (the runner is set down on top there).
    int LandingSideFor(Segment seg, Segment before)
    {
        if (before == null || seg.portalRole == 2) return 0;
        return SideOnLeg(seg.direction, -before.direction);
    }

    // ------------------------------------------------------------------ speeds and lengths

    float LegSpeed(int serial, bool zigZagBoost)
    {
        float v = player != null ? player.SpeedForLeg(serial) : 14f;
        return zigZagBoost ? v * Mathf.Max(1f, boostSpeedMultiplier) : v;
    }

    /// <summary>Cubes a leg needs so that its crossing (landing on cube 0 to the corner on the last cube, about cubes - 1 long) lasts `seconds`.</summary>
    int CubesForSeconds(float seconds, float speed) =>
        Mathf.Max(2, Mathf.CeilToInt(seconds * Mathf.Max(0.01f, speed) / Mathf.Max(0.01f, cubeSize) + 1.1f - 0.0001f));

    float SecondsForCubes(int cubes, float speed) =>
        Mathf.Max(0f, cubes - 1.1f) * cubeSize / Mathf.Max(0.01f, speed);

    int MinCubes(int taps, int serial, bool boost, float speed) =>
        CubesForSeconds(difficultyProfile.MinLegSeconds(taps, serial, boost), speed);

    Vector2Int TimeBudgetRange(int leg)
    {
        Vector2 d = difficultyProfile.DurationRange(leg) * BurstScale311(leg);   // 3.11 short-leg bursts (1 elsewhere)
        float v = LegSpeed(leg, false);
        int lo = CubesForSeconds(d.x, v);
        int hi = Mathf.Max(lo, CubesForSeconds(d.y, v));
        return new Vector2Int(lo, hi);
    }

    int RollTimeBudgetLength(int leg)
    {
        Vector2Int range = TimeBudgetRange(leg);
        int lo = range.x, hi = range.y, span = hi - lo;
        if (lastLegCubes > 0)
        {
            int shortCeiling = lo + Mathf.FloorToInt(span * shortLegFraction);
            if (lastLegCubes <= shortCeiling) lo = Mathf.Min(hi, lo + Mathf.CeilToInt(span * releaseFloorFraction));
        }
        int len = PathRange(lo, hi + 1);
        return Mathf.Max(len, MinCubes(1, leg, false, LegSpeed(leg, false)));
    }

    int FirstLegCubesTB(int segLen)
    {
        if (!TB) return Mathf.Max(segLen, firstLegMinCubes);
        return Mathf.Max(segLen, CubesForSeconds(difficultyProfile.firstLegSeconds, LegSpeed(segmentSerial, false)));
    }

    /// <summary>Portal runway length: the scene's cube count, but never under the profile's runway seconds or the 2-roll floor.</summary>
    int PortalRunwayCubesTB(int cubes)
    {
        if (!TB) return cubes;
        float v = LegSpeed(segmentSerial, false);
        float s = Mathf.Max(difficultyProfile.portalRunwaySeconds, difficultyProfile.MinLegSeconds(2, segmentSerial, false));
        return Mathf.Max(cubes, CubesForSeconds(s, v));
    }

    // ------------------------------------------------------------------ turn filter

    // Most rolls the turn at the end of leg s may need (time floor, then the wave / budget caps).
    int MaxTapsByTime(Segment s)
    {
        float dur = s.seconds;
        int m = 2;
        while (m > 0 && dur + 0.0005f < difficultyProfile.MinLegSeconds(m, s.serial, s.boost && s.portalRole == 0)) m--;
        return m;
    }

    /// <summary>True if turning into `dir` at the end of leg s is within every floor and cap.</summary>
    bool TurnFits(Segment s, Vector3 dir, bool timeOnly)
    {
        int turn = SideOnLeg(s.direction, dir);
        int taps = Rolls(s.landingSide, turn);
        if (s.plankCount > 0)
        {
            // A bridge: what matters is the face change from the last plank to the turn face.
            int lastFace = s.planks[s.planks.Count - 1].z;
            int r = Rolls(lastFace, turn);
            if (difficultyProfile.PlankGapSeconds(r) > s.leadOutSeconds + 0.0005f) return false;
            return timeOnly || taps <= WaveMaxTaps(s, 2);
        }
        int byTime = MaxTapsByTime(s);
        return taps <= (timeOnly ? byTime : WaveMaxTaps(s, byTime));
    }

    /// <summary>Shortest the leg being laid may be shortened to (to keep clear of old track) before the floor is broken.</summary>
    int ShortestAllowedNewLeg(int segLen) => TB ? Mathf.Min(segLen, MinCubes(1, segmentSerial, false, LegSpeed(segmentSerial, false))) : 2;

    /// <summary>
    /// The newest leg is too short for the only turns that keep clear (2 rolls): lengthen it in place, so the
    /// turn gets its time instead of the floor being broken. Only while nothing has been hung on its end and
    /// the camera has not committed to its shot. True if it grew.
    /// </summary>
    bool TryExtendForTwoTaps(Segment s)
    {
        if (!TB || s == null || s.plankCount > 0 || s.portalRole != 0 || s.cubes.Count == 0) return false;
        if (s.zone != null && s.zone.framingLocked) return false;
        int need = CubesForSeconds(difficultyProfile.MinLegSeconds(2, s.serial, s.boost && s.portalRole == 0), s.speed) - s.cubes.Count;
        if (need <= 0 || need > 12) return false;
        Vector3 dir = s.direction;
        Vector3 from = lastPosition;
        // The extension must keep clear like any leg (the leg itself is exempt: LegIsClear skips the last serial).
        if (!LegIsClear(from + dir * cubeSize, dir, need - 1)) return false;

        Vector3 p = from;
        for (int i = 0; i < need; i++)
        {
            p += dir * cubeSize;
            GameObject c = Instantiate(cubePrefab, p, GetRotationForDir(dir));
            c.name = $"PathCube_S{allSegments.Count - 1}_C{s.cubes.Count}";
            c.transform.SetParent(transform);
            c.SetActive(true);
            s.cubes.Add(c);
            occupiedCells[CellOf(p)] = segmentSerial - 1;
            if (s.zone != null) { s.zone.Encapsulate(p); s.zone.framingSolved = false; }
        }
        s.endPosition = p;
        lastPosition = p;
        lastLegCubes = s.cubes.Count;
        s.seconds = SecondsForCubes(s.cubes.Count, s.speed);
        return true;
    }

    // Fills the per-leg facts the floors and the waves need, right after the leg is laid.
    void OnSegmentLaidTB(Segment seg, bool bridgeActive)
    {
        seg.serial = segmentSerial;
        Segment before = allSegments.Count > 0 ? allSegments[allSegments.Count - 1] : null;
        if (seg.portalRole == 2) before = null;
        seg.landingSide = LandingSideFor(seg, before);
        seg.speed = LegSpeed(segmentSerial, seg.boost && seg.portalRole == 0);
        seg.seconds = seg.cubes.Count > 0 ? SecondsForCubes(seg.cubes.Count + (before == null && seg.portalRole == 0 ? 1 : 0), seg.speed) : 0f;
        AssignWaveInfo(seg);
        Tag311(seg);   // 3.11 skill-model tags (no-op elsewhere)

        if (bridgeActive && bridgePlanks.Count > 0 && seg.portalRole == 0 && !seg.boost)
        {
            seg.plankCount = bridgePlanks.Count;
            seg.multiPlank = bridgePlanks.Count > 1;
            seg.planks.Clear();
            foreach (var p in bridgePlanks) seg.planks.Add(new Vector3Int(p.first, p.last, p.side));
            seg.firstPlankSeconds = (bridgePlanks[0].first - 0.55f) * cubeSize / Mathf.Max(0.01f, seg.speed);
            seg.leadOutSeconds = (seg.cubes.Count - bridgePlanks[bridgePlanks.Count - 1].last - 1.55f) * cubeSize / Mathf.Max(0.01f, seg.speed);
            seg.hazardCost += BridgeCost(seg);
        }
    }

    int BridgeCost(Segment seg)
    {
        if (difficultyProfile == null || seg.plankCount <= 0) return 0;
        int c = difficultyProfile.costBridge + (seg.plankCount - 1) * difficultyProfile.costExtraPlank;
        for (int i = 1; i < seg.planks.Count; i++)
            if (Rolls(seg.planks[i - 1].z, seg.planks[i].z) == 2) c += difficultyProfile.costOppositePlank;
        return c;
    }

    // ------------------------------------------------------------------ finalising a leg

    // The leg after `prev` has just been laid in `nextDir`: its turn, taps and partial cost are now known.
    // Runs before the coins are placed so the lure rule can read the cost.
    void PreFinalizeLeg(Segment prev, Vector3 nextDir)
    {
        if (difficultyProfile == null || prev == null) return;
        prev.turnSide = prev.portalRole == 1 ? 0 : SideOnLeg(prev.direction, nextDir);
        prev.taps = Rolls(prev.landingSide, prev.turnSide);
        prev.cost = LegCost(prev);
    }

    int LegCost(Segment s)
    {
        int c = Mathf.Max(0, s.taps) + s.hazardCost;
        if (s.boost && s.portalRole == 0) c += difficultyProfile.costBoostLeg;
        if (!s.boost && s.portalRole == 0 && s.plankCount == 0 && TB)
        {
            Vector2Int r = TimeBudgetRange(s.serial);
            if (s.cubes.Count < (r.x + r.y) * 0.5f) c += 1;
        }
        return c;
    }

    // After the coins, gems and obstacle of `prev` are down: full cost, the floor check, the budget, the event.
    void FinalizeLeg(Segment prev)
    {
        if (difficultyProfile == null || prev == null || prev.taps < 0) return;
        prev.cost = LegCost(prev);
        if (TB) ValidateLeg(prev);
        SpendBudget(prev);
        LegFinalized?.Invoke(prev);
    }

    void ValidateLeg(Segment s)
    {
        if (TutorialLegOf(s) >= 0) return;   // TUTORIAL ADDITION: scripted lesson legs, run at the tutorial's own (slower) speed
        LegsValidated++;
        var P = difficultyProfile;
        bool boost = s.boost && s.portalRole == 0;
        string why = null;

        if (s.plankCount > 0)
        {
            int r0 = Rolls(s.landingSide, s.planks[0].z);
            float need0 = r0 > 0 ? P.MinLegSeconds(r0, s.serial, false) : 0f;
            if (s.firstPlankSeconds + 0.0005f < need0) why = $"first plank {s.firstPlankSeconds:0.000}s < {need0:0.000}s ({r0} rolls)";
            for (int i = 1; i < s.planks.Count && why == null; i++)
            {
                int r = Rolls(s.planks[i - 1].z, s.planks[i].z);
                int gapCubes = s.planks[i].x - s.planks[i - 1].y - 1;
                float gap = gapCubes * cubeSize / Mathf.Max(0.01f, s.speed);
                float need = r == 0 ? 2f * cubeSize / Mathf.Max(0.01f, s.speed) : P.PlankGapSeconds(r);
                if (gap + 0.0005f < need) why = $"plank gap {i} {gap:0.000}s < {need:0.000}s ({r} rolls)";
            }
            int rOut = Rolls(s.planks[s.planks.Count - 1].z, s.turnSide);
            if (why == null && s.leadOutSeconds + 0.0005f < P.PlankGapSeconds(rOut)) why = $"lead-out {s.leadOutSeconds:0.000}s < {P.PlankGapSeconds(rOut):0.000}s";
            s.floorSeconds = need0;
        }
        else if (s.slowMo && !s.slowMoExit && s.slowMoSpeed < 0.999f)
        {
            // A slow-motion tiny leg (3.11): crossed with the runner slowed, while time runs at slowMoScale - the rolls
            // run in game time, the human reacts in real time (DifficultyProfile.SlowMoLegSeconds, by its own taps).
            float need = P.Overhead(s.taps) + s.slowMoScale * P.THuman(s.taps, s.serial, false);
            float have = s.seconds / Mathf.Max(0.05f, s.slowMoSpeed);
            s.floorSeconds = need;
            if (have + 0.0005f < need) why = $"slow-motion {have:0.000}s < floor {need:0.000}s ({s.taps} taps, time x{s.slowMoScale:0.00})";
        }
        else
        {
            float need = P.MinLegSeconds(s.taps, s.serial, boost);
            s.floorSeconds = need;
            if (s.seconds + 0.0005f < need) why = $"{s.seconds:0.000}s < floor {need:0.000}s ({s.taps} taps{(boost ? ", boost" : "")})";
        }

        if (why == null) return;
        s.floorViolation = true;
        FloorViolations++;
        if (P.validateFloors)
            Debug.LogError($"[TimeBudget] floor violation on leg {s.serial} ({s.cubes.Count} cubes, {s.speed:0.0} u/s{(s.portalRole != 0 ? ", portal " + s.portalRole : "")}): {why}");
    }
}
