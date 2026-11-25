using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dry run for the human simulator (HumanSimPlayer.RunBatch): lays `count` legs of a seeded run in one call,
/// exactly as the game would lay them for a runner that never falls (portals crossed, zones culled, the
/// camera's yaw plan solved with the rig's own settings), and hands back every leg's facts. The path is
/// then rebuilt fresh, so the scene is left as a new run would find it. Only for tooling: nothing in the
/// game calls it.
/// </summary>
public partial class DynamicPathGenerator
{
    /// <summary>One laid leg, as the simulator sees it.</summary>
    public struct LegFacts
    {
        public int serial, cubes, taps, landingSide, turnSide, cost, cycle, cyclePos, portalRole, plankCount;
        public bool boost, decoyAtEnd, obstacle, glide, cameraTurn, floorViolation;
        public WavePhase phase;
        public float speed, seconds, firstPlankSeconds, leadOutSeconds;
        public Vector3Int p0, p1, p2;      // up to three planks (first cube, last cube, face)
        public float gap01, gap12;         // seconds between planks
        public bool slowMo;                // 3.11: a slow-motion tiny leg (runner slowed by slowMoSpeed, time x slowMoScale)
        public float slowMoSpeed, slowMoScale;
        public int boostMode;              // 3.11: BoostMode311 of a boost leg (-1 = none)
    }

    // ---- Seeded runs (tooling only: LeftRightDebugTxt etc.). Null = UnityEngine.Random, exactly as always. ----
    private System.Random pathRng;
    int PathRange(int minInclusive, int maxExclusive) =>
        pathRng == null ? Random.Range(minInclusive, maxExclusive)
                        : (maxExclusive <= minInclusive ? minInclusive : pathRng.Next(minInclusive, maxExclusive));
    float PathValue => pathRng == null ? Random.value : (float)pathRng.NextDouble();

    /// <summary>
    /// Tooling: every path laid from now on (call it right before the runner restarts) comes from `seed` alone - its own
    /// stream for the layout plus seeded boost / bridge / decoy / obstacle / wave streams - so other scripts drawing from
    /// UnityEngine.Random cannot change it. Runtime only; nothing in the game calls it.
    /// </summary>
    public void SeedRuns(int seed)
    {
        // Same streams, same order as DryLay(count, seed), so DryLay right after SeedRuns(seed) previews this very run.
        var sr = new System.Random(seed);
        boostRng = new System.Random(sr.Next());
        bridgeRng = new System.Random(sr.Next());
        decoyRng = new System.Random(sr.Next());
        obstacleRng = new System.Random(sr.Next());
        waveRng = new System.Random(sr.Next());
        pathRng = new System.Random(sr.Next());
    }

    /// <summary>
    /// Tooling: DryLay with EVERY stream seeded (coins, gates, obstacle legs, power-ups, shield, slow motion, specials too),
    /// so the same seed always lays the same track - for regression checks. The streams are fresh again afterwards.
    /// </summary>
    public List<LegFacts> DryLaySeeded(int count, int seed)
    {
        var sr = new System.Random(seed * 7919 + 17);
        coinRng = new System.Random(sr.Next());
        gateRng = new System.Random(sr.Next());
        obstacleLegRng = new System.Random(sr.Next());
        powerRng = new System.Random(sr.Next());
        shieldRng = new System.Random(sr.Next());
        slowMoRng = new System.Random(sr.Next());
        specialsRng = new System.Random(sr.Next());
        try { return DryLay(count, seed); }
        finally
        {
            coinRng = new System.Random(); gateRng = new System.Random(); obstacleLegRng = new System.Random();
            powerRng = new System.Random(); shieldRng = new System.Random(); slowMoRng = new System.Random(); specialsRng = new System.Random();
        }
    }

    public List<LegFacts> DryLay(int count, int seed)
    {
        var facts = new List<LegFacts>(count + 8);
        if (player == null || firstCube == null) return facts;

        Random.InitState(seed);
        var sr = new System.Random(seed);
        boostRng = new System.Random(sr.Next());
        bridgeRng = new System.Random(sr.Next());
        decoyRng = new System.Random(sr.Next());
        obstacleRng = new System.Random(sr.Next());
        waveRng = new System.Random(sr.Next());

        var laid = new List<Segment>(count + 32);
        System.Action<Segment> grab = s => laid.Add(s);
        LegFinalized += grab;

        var rig = FindFirstObjectByType<CameraParentRotator>();
        try
        {
            GenerateNewPath();
            int guard = 0;
            while (laid.Count < count && guard++ < count * 4)
            {
                int idx = Mathf.Min(currentPlayerSegmentIdx + 1, allSegments.Count - 1);
                Segment cur = allSegments[idx];
                if (cur.portalRole == 1)
                {
                    PortalCrossed();
                    cur = PortalExitSegment ?? cur;
                }
                if (cur.cubes.Count > 0 && cur.cubes[0] != null) player.currentCube = cur.cubes[0].transform;
                currentPlayerSegmentIdx = allSegments.IndexOf(cur);
                CullZonesBehind();
                EnsureZonesAhead();
                if (rig != null) PlanCameraYaws(rig.isometricTiltX, rig.isoYawOffset, rig.quantiseZoneYaw, rig.angleChangeCost, LatestHeading);
                MarkCameraTurn(cur);
            }
        }
        finally
        {
            LegFinalized -= grab;
        }

        foreach (Segment s in laid)
        {
            var f = new LegFacts
            {
                serial = s.serial, cubes = s.cubes.Count, taps = s.taps, landingSide = s.landingSide, turnSide = s.turnSide,
                cost = s.cost, cycle = s.cycle, cyclePos = s.cyclePos, portalRole = s.portalRole, plankCount = s.plankCount,
                boost = s.boost && s.portalRole == 0, decoyAtEnd = s.decoyAtEnd, obstacle = s.obstacle, glide = s.glide,
                cameraTurn = cameraTurnSerials.Contains(s.serial), floorViolation = s.floorViolation, phase = s.phase,
                speed = s.speed, seconds = s.seconds, firstPlankSeconds = s.firstPlankSeconds, leadOutSeconds = s.leadOutSeconds,
                slowMo = s.slowMo && !s.slowMoExit, slowMoSpeed = s.slowMoSpeed, slowMoScale = s.slowMoScale, boostMode = s.boostMode311,
            };
            if (s.plankCount > 0) f.p0 = s.planks[0];
            if (s.plankCount > 1) { f.p1 = s.planks[1]; f.gap01 = (s.planks[1].x - s.planks[0].y - 1) * cubeSize / Mathf.Max(0.01f, s.speed); }
            if (s.plankCount > 2) { f.p2 = s.planks[2]; f.gap12 = (s.planks[2].x - s.planks[1].y - 1) * cubeSize / Mathf.Max(0.01f, s.speed); }
            facts.Add(f);
        }
        cameraTurnSerials.Clear();

        // Leave the scene as a fresh run finds it.
        boostRng = new System.Random();
        bridgeRng = new System.Random();
        decoyRng = new System.Random();
        obstacleRng = new System.Random();
        waveRng = new System.Random();
        Random.InitState(System.Environment.TickCount);
        GenerateNewPath();
        return facts;
    }

    // The camera moves to a new zone while the runner is on the previous zone's last leg; if the yaw changes
    // there, that leg (and the new zone's first) are "camera turning" legs.
    // Judged as the runner reaches the zone's last leg (where the real camera commits to the next shot), with
    // the plan as it stands then; the zone after it is locked so later planning cannot rewrite the answer.
    readonly HashSet<int> cameraTurnSerials = new HashSet<int>();
    void MarkCameraTurn(Segment cur)
    {
        PathZone a = cur.zone;
        if (a == null || a.FinalSegment != cur) return;
        PathZone b = GetZoneAfter(a);
        if (b == null || b.segments.Count == 0) return;
        float ya = a.yawPlanned ? a.plannedYaw : a.optimalCameraYaw;
        float yb = b.yawPlanned ? b.plannedYaw : b.optimalCameraYaw;
        b.framingSolved = true;
        b.optimalCameraYaw = yb;
        b.LockFraming();
        if (Mathf.Abs(Mathf.DeltaAngle(ya, yb)) < 1f) return;
        cameraTurnSerials.Add(cur.serial);
        cameraTurnSerials.Add(b.segments[0].serial);
    }
}
