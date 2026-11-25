using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ZONE DOORS (TrialMonument2 and every scene with portalZones ticked). Every legsBetweenPortals legs
/// the path lays a straight runway (portalRunwayCubes cubes) that ends at a door. The moment that
/// happens, PortalPrebuildExit lays the runway OUT of the door too - at least portalMinDoorDistanceCubes
/// away, never heading the way of the last legs in (PortalExitHeadings) - right away, so both doors and both short legs already exist and can be seen together well
/// before the runner ever reaches the first one: a portal connects two real places, it doesn't conjure
/// the far one into existence on arrival. Generation is held (portalHold) again as soon as that eager
/// lay is done, so nothing beyond it is built until the door is actually crossed.
///
/// The runner cannot simply run there early: the two runways are not connected (a short unlaid gap
/// sits between them), so only PortalZoneController's teleport on crossing gets them across it. The
/// old runway is NOT cleared once they do: it is left standing and fades out the same way any two
/// adjacent legs already coexist during an ordinary run (PathDepthShading's crossed-leg fade).
///
/// Off unless portalZones is ticked, so every other scene generates exactly as before. The three
/// hooks in DynamicPathGenerator.cs are ResetPortal (GenerateNewPath), the portalHold early-out in
/// EnsureZonesAhead, and the portalRole handling in AppendSegment.
///
/// Both runways are marked as boost legs (Segment.boost), so PathDepthShading draws them full size
/// and lit and RunnerBoostFX dresses the runner while it is on them.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Portal Zones")]
    [Tooltip("Every N legs the path ends in a zone door: a long runway in, a black doorway, then a long runway out into a new colour zone. Needs a PortalZoneController in the scene to play the transition.")]
    public bool portalZones = false;
    [Tooltip("Path legs between zone doors.")]
    [Range(3, 200)] public int legsBetweenPortals = 10;
    [Tooltip("Length in cubes of the runway into a door and of the runway out of one.")]
    [Range(8, 60)] public int portalRunwayCubes = 24;
    [Tooltip("Cover the runway out of a door in an unbroken line of coins (a bonus stage).")]
    public bool portalBonusCoins = true;
    [Tooltip("Also line the runway OUT of a door with coins. Off = the exit runway is coin-free (the runway into the door keeps its coins).")]
    public bool portalExitCoins = false;
    [Tooltip("Cubes at the start of the exit runway left free of coins, so the burst out of the door is clear.")]
    [Range(0, 10)] public int portalCoinLeadCubes = 2;
    [Tooltip("Cubes at the end of the exit runway left free of coins, so there is room to read the next turn.")]
    [Range(0, 10)] public int portalCoinTailCubes = 2;

    [Tooltip("Ordinary legs laid (and drawn) beyond the runway out of a door, before the door is crossed, so the way on is visible from the entry runway. 0 = nothing past the exit runway until the door is crossed (the original behaviour).")]
    [Range(0, 6)] public int portalLegsAfterExit = 0;
    [Tooltip("Classic layout only (portalIslandExit off): cubes ahead of the end of the entry runway that the exit runway starts (it is also set off to the side, see portalMinDoorDistanceCubes).")]
    [Range(1f, 8f)] public float portalDoorGapCubes = 2f;
    [Tooltip("When the runway into a door is laid (many legs before the runner gets there), take the coins and corner gems off every leg already laid, the runner's own included. Off = those legs keep theirs; they still go when their track is culled. The island layout's entry runway is cleared and gets its bonus line either way.")]
    public bool portalClearsCoinsOnLaidTrack = true;

    [Header("Portal Exit Heading (all portal scenes)")]
    [Tooltip("The runway out of a door never heads the way any of the last N legs into it went (the runway in counts as one). It goes the opposite way to the runway in where it can, else the opposite way to the legs before it.")]
    [Range(1, 3)] public int portalExitAvoidLegs = 3;
    [Tooltip("Least ground distance, in cubes, between the entry door (last cube of the runway in) and the exit door (first cube of the runway out), so the two read as two places. Height difference does not count.")]
    [Range(2f, 30f)] public float portalMinDoorDistanceCubes = 13f;

    [Header("Portal Island (MainScene prop)")]
    [Tooltip("The entry runway is a dead end. The runway out of the door starts on its own island in empty space, at a fixed offset from the end of the entry runway, heading a different way (the entry heading turned 90, -90 or 180 degrees), and is not joined to it. Both door zones are widened so one camera shot holds both structures.")]
    public bool portalIslandExit = false;
    [Tooltip("Where the first cube of the exit runway goes, in cubes, measured from the LAST CUBE of the entry runway in the entry runway's own frame: x = sideways (positive = to the runner's right), y = up (negative = down), z = ahead along the entry heading. If the spot is blocked the sideways part is mirrored, then the island is pushed further ahead; if nothing at all is clear it is placed here anyway.")]
    public Vector3 portalIslandOffsetCubes = new Vector3(-4f, 6f, 8f);
    [Tooltip("Must match CameraParentRotator.isoYawOffset: the camera yaws it can take are this + k * 90.")]
    public float portalCameraYawOffset = 45f;
    [Tooltip("Island layout: the runway out heads SIDEWAYS (90 degrees to the runway in), on the side its island is set off to, so it leads away from the entry door; straight back the way you came is only a fallback. Off = straight back is tried first.")]
    public bool portalExitPrefersSideways = false;
    [Tooltip("Island layout: each door keeps its own camera shot instead of one wide shot holding both, so the doors can stand far apart and the camera glides over to the exit door after the crossing (same yaw, no turn). Off = one shot holds both doors.")]
    public bool portalSeparateDoorShots = false;

    // ---- LookTransition hooks (MainScene prop camera 3.2 only; runtime, 0 = today's behaviour) ----
    /// <summary>When above 0: the next runway OUT of a door lasts at least this many seconds at the speed of its leg (a portal
    /// cinematic needs the room). 0 = portalRunwayCubes, as before.</summary>
    [System.NonSerialized] public float exitRunwaySecondsOverride;
    /// <summary>When above 0: only the first this-many cubes of the runway out count toward the two-door camera shot, so a
    /// lengthened runway does not widen that shot. 0 = all of them, as before.</summary>
    [System.NonSerialized] public int exitRunwayFrameCubes;

    // Length of the runway out of a door: the scene's runway, or longer while a LookTransition asks for it.
    int ExitRunwayCubes()
    {
        int cubes = PortalRunwayCubesTB(Mathf.Max(4, portalRunwayCubes));
        if (exitRunwaySecondsOverride > 0f)
            cubes = Mathf.Max(cubes, CubesForSeconds(exitRunwaySecondsOverride, LegSpeed(segmentSerial, false)));
        return cubes;
    }

    /// <summary>While a door is ahead: how many legs past the runway out of it should be drawn (0 = the normal window).</summary>
    public int PortalLegsShownAfterExit => portalHold ? portalLegsAfterExit : 0;

    private bool portalHold;             // the runway into a door is laid: generation stops here
    private bool portalExitPending;      // the next leg laid is the runway out of a door
    private int nextPortalSerial;

    /// <summary>The runway into the door the runner is heading for, or null.</summary>
    public Segment PortalEntrySegment { get; private set; }
    /// <summary>Centre of the last cube of that runway, and its heading.</summary>
    public Vector3 PortalEntryEnd { get; private set; }
    public Vector3 PortalEntryDir { get; private set; }
    /// <summary>The face (0=Top,1=Right,2=Bottom,3=Left) the entry door was built to stand on - set by
    /// PortalZoneController once it builds the door, so anything steering toward it (the player, or
    /// AutoPlayer) knows which face to actually be on, not just "whichever I happen to be on".</summary>
    public int PortalEntrySide { get; set; }
    /// <summary>The runway out of the door the runner has just come through, or null.</summary>
    public Segment PortalExitSegment { get; private set; }
    public Vector3 PortalExitDir { get; private set; }
    /// <summary>How many doors the runner has been through.</summary>
    public int PortalsTaken { get; private set; }
    /// <summary>The checkpoint the run is at: 0 from the start, 1 after the first door (leg 30), and so on. Scenes without portalZones stay at 0.</summary>
    public int Checkpoint => PortalsTaken;
    /// <summary>True from the moment the runway into a door is laid until the runner is through.</summary>
    public bool PortalAhead => portalHold;

    static readonly Vector3[] FlatDirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

    void ResetPortal()
    {
        portalHold = false;
        portalExitPending = false;
        PortalEntrySegment = null;
        PortalExitSegment = null;
        PortalsTaken = 0;
        nextPortalSerial = PortalLegOf(1);
    }

    /// <summary>
    /// Called by AppendSegment for every leg after the first. When a door is due, hands back the
    /// heading and length of the runway into it: a flat 90 degree turn that keeps pathClearance
    /// away from the older track. If nothing fits, false, and the next leg tries again.
    /// </summary>
    bool TakePortalLeg(Vector3 prevDir, ref int segLen, out Vector3 dir)
    {
        dir = Vector3.zero;
        if (!portalZones || portalHold || portalExitPending) return false;
        if (segmentSerial + 1 < nextPortalSerial) return false;
        if (boostPlanIndex < boostPlanDirs.Count) return false;   // let a boost finish first

        int want = PortalRunwayCubesTB(Mathf.Max(8, portalRunwayCubes));
        Vector3 origin = lastPosition + prevDir * cubeSize;

        var options = new List<Vector3>();
        foreach (Vector3 d in FlatDirs)
        {
            if (Mathf.Abs(Vector3.Dot(d, prevDir)) > 0.9f) continue;   // 90 degree turns only
            if (lastHorizontalDirection.sqrMagnitude > 0.01f && Vector3.Dot(d, lastHorizontalDirection) < -0.9f) continue;
            options.Add(d);
        }
        for (int i = options.Count - 1; i > 0; i--)
        {
            int j = PathRange(0, i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }
        // Wave pacing: prefer a runway that runs AWAY from the camera's current yaw, so the island shot can
        // keep that yaw and the camera does not turn on the way into the door (it may turn after it).
        if (Waves && difficultyProfile.restrictCameraRotation)
        {
            float yaw = LatestPlannedYaw();
            if (yaw >= 0f)
            {
                Vector3 f = YawForward(yaw);
                options.Sort((x, y) => (Vector3.Dot(f, y) > 0.3f ? 1 : 0).CompareTo(Vector3.Dot(f, x) > 0.3f ? 1 : 0));
            }
        }
        // 3.11: turns the leg before has the time for come first (the corner onto the runway was laid unchecked before).
        if (L311 != null && TB && allSegments.Count > 0)
        {
            Segment before = allSegments[allSegments.Count - 1];
            options.Sort((x, y) => (TurnFits(before, y, true) ? 1 : 0).CompareTo(TurnFits(before, x, true) ? 1 : 0));
        }
        // The runway's own floor: the rolls from the landing face to the top (the door's face).
        // Landing on a flat runway from a dive puts the runner on top (0 rolls), from a climb underneath (2), else on a side (1).
        if (TB)
        {
            int taps = prevDir.y < -0.9f ? 0 : prevDir.y > 0.9f ? 2 : 1;
            want = Mathf.Max(want, CubesForSeconds(difficultyProfile.MinLegSeconds(taps, segmentSerial, false), LegSpeed(segmentSerial, false)));
        }

        // The full length if it fits, and up to 6 cubes shorter before giving up for this leg.
        for (int len = want; len >= Mathf.Max(8, want - 8); len--)
        {
            foreach (Vector3 d in options)
            {
                if (!LegIsClear(origin, d, len)) continue;
                dir = d;
                segLen = len;
                lastHorizontalDirection = d;
                return true;
            }
        }
        return false;
    }

    // The runway into the door is down: make sure the camera's shot for it is sealed and takes in the
    // door as well as the cubes, then eagerly lay the runway OUT of the door too (PortalPrebuildExit),
    // so both doors already exist together.
    void OnPortalEntryLaid(Segment seg)
    {
        PortalEntrySegment = seg;
        PortalEntryDir = seg.direction;
        PortalEntryEnd = seg.endPosition;
        portalHold = true;

        DecoratePortalRunway(seg);   // square tiles (MainScene prop; off elsewhere)

        // The door is on the up-facing side of the beam, so the last stretch carries a line of coins down
        // the TOP face: the runner has to be up there to get through.
        if (portalBonusCoins && spawnCoins && CoinSystemReady())
            for (int i = 4; i <= seg.cubes.Count - 3; i++)
                if (seg.cubes[i] != null) PlaceSingleCoin(seg, seg.cubes[i].transform, Vector3.up, i - 4);

        PathZone zone = seg.zone;
        if (zone != null)
        {
            Vector3 right = Vector3.Cross(Vector3.up, seg.direction);
            Vector3 doorMid = seg.endPosition;
            for (int i = 0; i < 8; i++)
            {
                zone.Encapsulate(doorMid
                    + right * ((i & 1) == 0 ? -1.4f : 1.4f) * cubeSize
                    + Vector3.up * ((i & 2) == 0 ? -0.8f : 5.4f) * cubeSize
                    + seg.direction * ((i & 4) == 0 ? -1.4f : 1.4f) * cubeSize);
            }
            if (!zone.isSealed) SealZone(zone);
        }

        PortalPrebuildExit();
    }

    // The runway out: an unbroken line of coins down the top face.
    void OnPortalExitLaid(Segment seg)
    {
        PortalExitSegment = seg;
        PortalExitDir = seg.direction;
        portalExitPending = false;

        DecoratePortalRunway(seg);   // square tiles (MainScene prop; off elsewhere)

        if (!portalExitCoins || !portalBonusCoins || !spawnCoins || !CoinSystemReady()) return;

        int first = Mathf.Max(0, portalCoinLeadCubes);
        int last = seg.cubes.Count - 1 - Mathf.Max(0, portalCoinTailCubes);
        for (int i = first; i <= last; i++)
            if (seg.cubes[i] != null) PlaceSingleCoin(seg, seg.cubes[i].transform, Vector3.up, i - first);
    }

    /// <summary>
    /// The runner has gone through the door: throws away all the old track (it is out of sight - the
    /// transition covers the screen). Nothing is laid until PortalLayExit, so the void between zones
    /// really is empty.
    /// </summary>
    public void PortalClearTrack()
    {
        if (!portalHold) return;

        foreach (PathZone z in zones)
            foreach (Segment s in z.segments)
            {
                foreach (GameObject cube in s.cubes)
                    if (cube != null && cube.transform != firstCube) Destroy(cube);
                DestroyDecoy(s);
                ReleaseCoinsOn(s);
                ReleaseGemsOn(s);
            }
        zones.Clear();
        allSegments.Clear();
        occupiedCells.Clear();
        currentPlayerSegmentIdx = 0;
        lastScoredSegment = null;   // the runway out is where the runner starts; it does not score twice
        PortalEntrySegment = null;
    }

    /// <summary>
    /// Lays exactly the runway out of the door right away - a couple of cubes past where the door
    /// stands, straight on - so the far door already exists to be seen alongside the near one. Nothing
    /// past that runway is built until the door is actually crossed (PortalCrossed then lets ordinary
    /// generation carry on from its end). Also stretches the two doors' zones to share one camera shot.
    /// </summary>
    void PortalPrebuildExit()
    {
        // Coins and corner gems on the old track go with it.
        if (portalClearsCoinsOnLaidTrack)
            foreach (PathZone z in zones)
                foreach (Segment s in z.segments) { ReleaseCoinsOn(s); ReleaseGemsOn(s); }

        // Both layouts: the runway out heads away from the way in (PortalExitHeadings) and starts at least
        // portalMinDoorDistanceCubes from the entry door, so the two doors never read as one place.
        // Island layout (MainScene prop): the runway out starts on its own island, raised, at an offset from
        // the end of the entry runway. Classic: level with it, off to the side the runway out heads for.
        bool island = portalIslandExit;
        float islandYaw = -1f;
        Vector3 outDir;
        Vector3 start = island ? PlanIsland(out outDir, out islandYaw) : PlanClassicExit(out outDir);

        boostPlanDirs.Clear();
        boostPlanLengths.Clear();
        boostPlanIndex = 0;
        nextBoostSerial = segmentSerial + 1 + RollBoostGap();
        // Fixed now, not at the crossing: otherwise the ordinary legs laid a few lines down (to keep
        // lookahead topped up) could themselves roll due for another door before this one is crossed.
        nextPortalSerial = segmentSerial + Mathf.Max(3, PortalLegOf(PortalsTaken + 2) - PortalLegOf(PortalsTaken + 1));

        lastPosition = start - outDir * cubeSize;
        lastDirection = outDir;
        lastHorizontalDirection = outDir;
        dirBeforeLast = Vector3.zero;
        firstSegmentPlaced = false;

        // Straight to AppendSegment, not EnsureZonesAhead: sealing the entry door's zone a moment ago
        // can already satisfy EnsureZonesAhead's own lookahead thresholds, which would make its loop
        // skip laying anything at all. portalHold stays true throughout (set by OnPortalEntryLaid) -
        // AppendSegment doesn't gate on it, only EnsureZonesAhead's loop and TakePortalLeg do, and
        // portalExitPending already keeps TakePortalLeg from firing on this same segment.
        portalExitPending = true;
        AppendSegment();
        portalExitPending = false;

        if (island)
        {
            // Both door zones look from the same yaw: the entry archway faces the camera, the exit
            // archway faces it too, and there is no camera turn between the two.
            LockPortalYaw(PortalEntrySegment.zone, islandYaw);
            LockPortalYaw(PortalExitSegment.zone, islandYaw);
            // AppendSegment just treated the exit runway as the leg after the entry runway: the entry
            // runway is a dead end (the runner is teleported off it), so it gets no corner gem and no
            // ordinary coins, only the bonus line.
            ReleaseGemsOn(PortalEntrySegment);
            ReleaseCoinsOn(PortalEntrySegment);
            if (portalBonusCoins && spawnCoins && CoinSystemReady())
                for (int i = 4; i <= PortalEntrySegment.cubes.Count - 3; i++)
                    if (PortalEntrySegment.cubes[i] != null) PlaceSingleCoin(PortalEntrySegment, PortalEntrySegment.cubes[i].transform, Vector3.up, i - 4);
        }
        else
        {
            ExtendDoorZonesToEachOther(PortalEntrySegment, PortalExitSegment);
        }

        // Legs beyond the runway out, laid now so the way on can be seen from the entry runway.
        // portalHold is still true, so no second door can be chosen among them.
        int extrasFrom = allSegments.Count;
        for (int i = 0; i < portalLegsAfterExit; i++) AppendSegment();

        // Island: one camera shot that holds BOTH structures - the runway in, the runway out, the legs
        // beyond it and both doorways - so the doors are seen together before the runner reaches the first.
        if (island && !portalSeparateDoorShots) FrameBothStructures(extrasFrom);
    }

    // ------------------------------------------------------------------ island layout

    // The camera yaws the rig can take are portalCameraYawOffset + k * 90. Picks one that looks ALONG the
    // way in (the runner recedes into the entry archway and its front is seen) and AGAINST the way out
    // (the exit archway's front is seen, with its light spilling toward the camera).
    bool PickPortalYaw(Vector3 dirIn, Vector3 dirOut, out float yaw)
    {
        // Wave pacing: the yaw the camera already holds, if it works, so the door shot needs no turn.
        if (Waves && difficultyProfile.restrictCameraRotation)
        {
            float cur = LatestPlannedYaw();
            if (cur >= 0f)
            {
                Vector3 cf = YawForward(cur);
                if (Vector3.Dot(cf, dirIn) > 0.3f && Vector3.Dot(cf, dirOut) < -0.3f) { yaw = cur; return true; }
            }
        }
        for (int k = 0; k < 4; k++)
        {
            float y = Mathf.Repeat(portalCameraYawOffset + k * 90f, 360f);
            var f = new Vector3(Mathf.Sin(y * Mathf.Deg2Rad), 0f, Mathf.Cos(y * Mathf.Deg2Rad));
            if (Vector3.Dot(f, dirIn) > 0.3f && Vector3.Dot(f, dirOut) < -0.3f) { yaw = y; return true; }
        }
        yaw = -1f;
        return false;
    }

    // Only ever touches a zone the camera has not committed to; a zone it has keeps the yaw it has.
    void LockPortalYaw(PathZone zone, float yaw)
    {
        if (zone == null || zone.framingLocked || yaw < 0f) return;
        zone.forcedYaw = yaw;
        zone.yawPlanned = false;
        zone.framingSolved = false;
    }

    static Vector3 FlatCardinal(Vector3 d) =>
        Mathf.Abs(d.x) >= Mathf.Abs(d.z) ? new Vector3(Mathf.Sign(d.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(d.z));

    /// <summary>
    /// Headings the runway out of a door may take, best first. Never the way any of the last
    /// portalExitAvoidLegs legs into the door went (the runway in is the newest of them; a climb or dive
    /// has no ground heading and rules nothing out). First the opposite of the runway in (it went left,
    /// the way out goes right), then the opposite of the legs before it, then anything else left. With at
    /// most three legs ruled out of four headings, at least one is always left.
    /// </summary>
    List<Vector3> PortalExitHeadings()
    {
        var recent = new List<Vector3>();
        for (int k = 0; k < Mathf.Max(1, portalExitAvoidLegs) && k < allSegments.Count; k++)
        {
            Vector3 d = allSegments[allSegments.Count - 1 - k].direction;
            if (Mathf.Abs(d.y) > 0.9f) continue;
            recent.Add(FlatCardinal(d));
        }
        if (recent.Count == 0) recent.Add(FlatCardinal(PortalEntryDir));

        var result = new List<Vector3>();
        void Add(Vector3 h)
        {
            foreach (Vector3 r in recent) if (Vector3.Dot(h, r) > 0.9f) return;
            foreach (Vector3 r in result) if (Vector3.Dot(h, r) > 0.9f) return;
            result.Add(h);
        }
        foreach (Vector3 r in recent) Add(-r);
        int from = result.Count;
        foreach (Vector3 d in FlatDirs) Add(d);
        for (int i = result.Count - 1; i > from; i--)
        {
            int j = PathRange(from, i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }
        if (result.Count == 0) result.Add(-recent[0]);
        return result;
    }

    /// <summary>
    /// Classic layout: the first cube of the exit runway and the way it faces. Level with the entry runway,
    /// portalDoorGapCubes ahead of its end and far enough to the side that the doors are at least
    /// portalMinDoorDistanceCubes apart - on the side the runway out heads for, so it runs away from the
    /// entry door. Pushed further ahead, two cubes at a time, until the whole runway is clear of older track.
    /// </summary>
    Vector3 PlanClassicExit(out Vector3 outDir)
    {
        Vector3 fwd = FlatCardinal(PortalEntryDir);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        int len = ExitRunwayCubes();
        float gap = portalDoorGapCubes;
        float sideD = Mathf.Sqrt(Mathf.Max(0f, portalMinDoorDistanceCubes * portalMinDoorDistanceCubes - gap * gap));
        List<Vector3> headings = PortalExitHeadings();

        Vector3 At(float side, float ahead) => PortalEntryEnd + (right * side + fwd * ahead) * cubeSize;
        // A sideways heading starts on its own side; the way back can start on either.
        float[] SidesFor(Vector3 h)
        {
            float s = Vector3.Dot(h, right);
            return Mathf.Abs(s) > 0.5f ? new[] { Mathf.Sign(s) * sideD } : new[] { sideD, -sideD };
        }

        for (int extra = 0; extra <= 16; extra += 2)
            foreach (Vector3 h in headings)
                foreach (float side in SidesFor(h))
                {
                    Vector3 p = At(side, gap + extra);
                    if (!LegIsClear(p, h, len + 2)) continue;
                    outDir = h;
                    return p;
                }

        // Nothing clear: the best heading at the nearest far-enough spot anyway.
        outDir = headings[0];
        return At(SidesFor(outDir)[0], gap);
    }

    /// <summary>
    /// Island layout: the first cube of the exit runway AND the way it faces. The runway starts at the last
    /// cube of the entry runway plus portalIslandOffsetCubes in the entry runway's own frame (sideways, up,
    /// ahead): the entry runway ends there, so this is empty space, joined to nothing. Its heading comes from
    /// PortalExitHeadings (never the way of the last legs in). The first spot where the whole runway out is
    /// clear of older track and at least portalMinDoorDistanceCubes (on the ground) from the entry door is
    /// used: pushed further ahead in steps of two cubes; for each spot the headings in their order, each on
    /// its own side first. If nothing is clear it is placed at the nearest far-enough offset anyway.
    /// </summary>
    Vector3 PlanIsland(out Vector3 outDir, out float yaw)
    {
        Vector3 fwd = FlatCardinal(PortalEntryDir);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        int len = ExitRunwayCubes();
        Vector3 o = portalIslandOffsetCubes;
        float minD2 = portalMinDoorDistanceCubes * portalMinDoorDistanceCubes - 0.01f;

        Vector3 At(float side, float ahead) =>
            PortalEntryEnd + (right * side + Vector3.up * o.y + fwd * ahead) * cubeSize;

        List<Vector3> headings = PortalExitHeadings();
        // Wave pacing: headings the camera's current yaw can frame go first (no camera turn at the door),
        // each group keeping its own order.
        if (Waves && difficultyProfile.restrictCameraRotation)
        {
            float cur = LatestPlannedYaw();
            if (cur >= 0f)
            {
                Vector3 cf = YawForward(cur);
                bool Keeps(Vector3 h) => Vector3.Dot(cf, fwd) > 0.3f && Vector3.Dot(cf, h) < -0.3f;
                var ordered = headings.FindAll(Keeps);
                ordered.AddRange(headings.FindAll(h => !Keeps(h)));
                headings = ordered;
            }
        }
        // Sideways first (each group keeping its order), so the way out leads away from the entry door
        // rather than running back alongside the runway in.
        if (portalExitPrefersSideways)
        {
            var sideways = headings.FindAll(h => Mathf.Abs(Vector3.Dot(h, fwd)) < 0.5f);
            sideways.AddRange(headings.FindAll(h => Mathf.Abs(Vector3.Dot(h, fwd)) >= 0.5f));
            headings = sideways;
        }

        // The island sits on the side the runway out heads for, so it runs away from the entry door.
        float[] SidesFor(Vector3 h)
        {
            float s = Vector3.Dot(h, right);
            float a = Mathf.Abs(o.x);
            return Mathf.Abs(s) > 0.5f ? new[] { Mathf.Sign(s) * a, -Mathf.Sign(s) * a } : new[] { o.x, -o.x };
        }

        for (int extra = 0; extra <= 16; extra += 2)
            foreach (Vector3 h in headings)
            {
                if (!PickPortalYaw(fwd, h, out float y)) continue;
                foreach (float side in SidesFor(h))
                {
                    float ahead = o.z + extra;
                    if (side * side + ahead * ahead < minD2) continue;
                    Vector3 p = At(side, ahead);
                    if (!LegIsClear(p, h, len + 2)) continue;
                    outDir = h;
                    yaw = y;
                    return p;
                }
            }

        // Nothing clear: the first heading that has a camera yaw, pushed just far enough ahead.
        float fallbackAhead = Mathf.Max(o.z, Mathf.Sqrt(Mathf.Max(0f, portalMinDoorDistanceCubes * portalMinDoorDistanceCubes - o.x * o.x)));
        foreach (Vector3 h in headings)
        {
            if (!PickPortalYaw(fwd, h, out float y)) continue;
            outDir = h;
            yaw = y;
            return At(SidesFor(h)[0], fallbackAhead);
        }
        outDir = headings[0];
        yaw = -1f;
        return At(SidesFor(outDir)[0], fallbackAhead);
    }

    /// <summary>
    /// Widens the entry and exit zones to cover everything the shot must show, so the camera solve
    /// (which fits a zone's bounds) pulls back to hold both separate structures: every cube of the
    /// runway in and out and of the legs laid beyond it, and the box each doorway takes up. A zone the
    /// camera has committed to is left alone.
    /// </summary>
    void FrameBothStructures(int extrasFrom)
    {
        var pts = new List<Vector3>();
        void AddSeg(Segment s, int maxCubes = int.MaxValue)
        {
            if (s == null) return;
            int n = 0;
            foreach (GameObject cube in s.cubes)
            {
                if (n++ >= maxCubes) break;
                if (cube != null) pts.Add(cube.transform.position);
            }
        }
        // A tower takes up its footprint plus the steps on one flank, and stands about 7 tall (shaft,
        // crown, battlements, dome, finial); the exit's light reaches 6 cubes out ahead of it.
        void AddArchBox(Vector3 centre, Vector3 dir, float aheadCubes)
        {
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            for (int i = 0; i < 8; i++)
                pts.Add(centre
                        + right * ((i & 1) == 0 ? -PortalDoor.FootprintHalfWidth : PortalDoor.FootprintHalfWidth) * cubeSize
                        + Vector3.up * ((i & 2) == 0 ? -1f : PortalDoor.TowerTopY) * cubeSize
                        + dir * ((i & 4) == 0 ? -PortalDoor.FootprintHalfDepth : aheadCubes) * cubeSize);
        }

        AddSeg(PortalEntrySegment);
        bool capped = exitRunwayFrameCubes > 0 && PortalExitSegment != null && PortalExitSegment.cubes.Count > exitRunwayFrameCubes;
        AddSeg(PortalExitSegment, capped ? exitRunwayFrameCubes : int.MaxValue);
        // A lengthened runway (LookTransition) keeps the shot it had: the legs past its far end are off it, as the far cubes are.
        if (!capped) for (int i = extrasFrom; i < allSegments.Count; i++) AddSeg(allSegments[i]);

        // The entry archway is built round the last cube of the entry runway, the exit archway round the first of the exit.
        AddArchBox(PortalEntryEnd, PortalEntryDir, PortalDoor.FootprintHalfDepth);
        if (PortalExitSegment.cubes.Count > 0 && PortalExitSegment.cubes[0] != null)
            AddArchBox(PortalExitSegment.cubes[0].transform.position, PortalExitDir, 6.5f);

        PathZone entryZone = PortalEntrySegment.zone;
        PathZone exitZone = PortalExitSegment.zone;
        foreach (PathZone z in new[] { entryZone, exitZone })
        {
            if (z == null || z.framingLocked) continue;
            foreach (Vector3 p in pts) z.Encapsulate(p);
            z.framingSolved = false;
            if (z == exitZone && z == entryZone) break;
        }
    }

    /// <summary>The door has actually been crossed: releases the hold so ordinary generation carries
    /// on past the runway out (already standing), and counts the door.</summary>
    public void PortalCrossed()
    {
        PortalsTaken++;
        portalHold = false;
    }

    /// <summary>
    /// Keeps the door the runner just came through in the same shot as the runway leading away from
    /// it, the same way ExtendPreviousZoneOverHandover keeps ordinary zone hand-offs framed together:
    /// widen whichever zone each door landed in to also cover the other door's cubes, so both stay
    /// visible in one camera solve when they land in different zones. Never shrinks a shot, and never
    /// touches one the camera has already committed to (framingLocked).
    /// </summary>
    void ExtendDoorZonesToEachOther(Segment entrySeg, Segment exitSeg)
    {
        if (entrySeg == null || exitSeg == null) return;
        PathZone entryZone = entrySeg.zone;
        PathZone exitZone = exitSeg.zone;
        if (entryZone == null || exitZone == null || entryZone == exitZone) return;

        if (!entryZone.framingLocked)
        {
            foreach (GameObject cube in exitSeg.cubes)
                if (cube != null) entryZone.Encapsulate(cube.transform.position);
            entryZone.framingSolved = false;
        }
        if (!exitZone.framingLocked)
        {
            foreach (GameObject cube in entrySeg.cubes)
                if (cube != null) exitZone.Encapsulate(cube.transform.position);
            exitZone.framingSolved = false;
        }
    }
}
