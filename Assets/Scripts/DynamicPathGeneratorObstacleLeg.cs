using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// OBSTACLE LEGS (MainScene prop camera 3.9 only - off unless obstacleLegSections). Every 17-30 legs (random) the
/// path lays one very long flat runway, about 1.6x the runway out of a zone door, drawn like any ordinary leg (no boost
/// light or FX; square tiles only if obstacleLegTiles). Along it stand totem pillars (or thin poles): each station puts
/// two on OPPOSITE faces of one cube, so the runner has to be on one of the other two faces as it passes. The gap to the
/// next station is the human reaction floor (DifficultyProfile.MinLegSeconds) for the rolls the change of safe
/// faces can need, at that leg's speed. The first station leaves room for the camera's glide in, the last one
/// leaves room for the camera to hand back and for the turn at the end to be read and taken.
///
/// While the runner is on it, ObstacleLegCamera glides the camera to a chase pose behind the runner that looks up
/// the leg and back to the normal shot before the end.
///
/// The leg is a Segment with portalRole 3: every system that keeps hazards, coins, power-ups, gates, decoys and
/// bridges off the door runways (portalRole != 0) keeps them off this leg too. Poles exist nowhere else.
/// Uses its own System.Random, so the path's own random sequence is untouched.
/// </summary>
public partial class DynamicPathGenerator
{
    public partial class Segment
    {
        public float obstacleGlideInCubes;    // obstacle leg: the camera's glide in lasts this many cubes from the start
        public float obstacleGlideOutFrom;    // ... and its glide back starts here (cubes from the start)
        public float obstacleGlideOutCubes;   // ... and lasts this many cubes
    }

    [Header("Obstacle Legs (3.9)")]
    [Tooltip("Every so many legs lay one very long runway with thin poles sticking out of two faces at a time, and a chase camera while on it.")]
    public bool obstacleLegSections = false;
    [Tooltip("Fewest legs between two obstacle legs (drawn fresh each time).")]
    [Range(3, 80)] public int minLegsBetweenObstacleLegs = 17;
    [Tooltip("Most legs between two obstacle legs.")]
    [Range(3, 80)] public int maxLegsBetweenObstacleLegs = 30;
    [Tooltip("Length of an obstacle leg as a multiple of the runway out of a zone door, drawn between these two.")]
    public Vector2 obstacleLegLengthScale = new Vector2(1.45f, 1.75f);
    [Tooltip("An obstacle leg is never shorter than it takes to hold this many pole stations at its speed (the 1.6x runway length is used when it is longer).")]
    [Range(1, 20)] public int obstacleLegMinStations = 6;
    [Tooltip("Seconds at the start with no pole: the camera glides behind the runner and the first poles come into view.")]
    [Range(0.3f, 3f)] public float obstacleLegLeadSeconds = 1.1f;
    [Tooltip("Seconds after the last pole: the camera hands back to the normal shot, then the turn at the end is read and taken.")]
    [Range(0.5f, 4f)] public float obstacleLegTailSeconds = 1.6f;
    [Tooltip("Seconds the camera takes to glide back to the normal shot (inside the tail).")]
    [Range(0.1f, 2f)] public float obstacleLegGlideOutSeconds = 0.55f;
    [Tooltip("Gap between two pole stations = the reaction floor for the rolls needed (DifficultyProfile) x this.")]
    [Range(1f, 3f)] public float obstacleLegGapScale = 1.15f;
    [Tooltip("Random extra gap on top, as a share of the gap (0.35 = up to 35% longer).")]
    [Range(0f, 1f)] public float obstacleLegGapJitter = 0.35f;
    [Tooltip("Never closer than this many seconds between two stations, even when no roll is needed.")]
    [Range(0.2f, 2f)] public float obstacleLegMinGapSeconds = 0.5f;
    [Tooltip("Without a DifficultyProfile: seconds of gap per roll needed (plus the minimum gap).")]
    [Range(0.1f, 1.5f)] public float obstacleLegSecondsPerRoll = 0.4f;
    [Tooltip("Length of a pole out of the face, in cubes.")]
    [Range(0.5f, 5f)] public float obstacleLegPoleLength = 2.4f;
    [Tooltip("Thickness of a pole, as a share of a cube.")]
    [Range(0.05f, 0.5f)] public float obstacleLegPoleThickness = 0.16f;
    [Tooltip("Totem pillar: these blocks (unit-cube prefabs) are stacked out of the face, the first one against it. Empty = the thin pole below.")]
    public GameObject[] obstacleLegPillarBlocks;
    [Tooltip("Size of one pillar block, in cubes (the pillar is this thick and blocks x this long).")]
    [Range(0.2f, 1f)] public float obstacleLegPillarBlockSize = 0.55f;
    [Tooltip("Material of the poles. Empty = obstaclePyramidMaterial.")]
    public Material obstacleLegPoleMaterial;
    [Tooltip("Dress the obstacle leg with the door runways' square tiles. Off = it looks like any ordinary leg (it is never boost-lit either way).")]
    public bool obstacleLegTiles = false;
    [Tooltip("Share of stations whose two blockers are TRIANGLES (a pyramid spike out of the face) instead of pillars / poles. 0 = never (3.9); scene 3.10 uses it.")]
    [Range(0f, 1f)] public float obstacleLegTriangleChance = 0f;
    [Tooltip("Triangle spike: base width and height, in cubes.")]
    public Vector2 obstacleLegTriangleSize = new Vector2(0.62f, 1.05f);
    [Tooltip("Material of the triangle spikes. Empty = obstaclePyramidMaterial.")]
    public Material obstacleLegTriangleMaterial;
    [Tooltip("Blockers are Monument-Valley style TREES (thin trunk + bright crown) instead of totem pillars / poles. Off = 3.9. Scene 3.10 uses it; LegStageColors dresses the crown per level.")]
    public bool obstacleLegTrees = false;
    [Tooltip("Mobile: drop the rim + ring meshes of the totem rivets on obstacle pillars (the centre dot stays).")]
    public bool obstacleLegSimpleRivets = true;
    [Tooltip("Tree: total height and crown width, in cubes.")]
    public Vector2 obstacleLegTreeSize = new Vector2(0.85f, 1.7f);
    [Tooltip("Print each obstacle leg as it is laid.")]
    public bool logObstacleLegs = false;

    private System.Random obstacleLegRng;
    private int nextObstacleLegSerial;
    private int obstacleLegPlanCycle = -1, obstacleLegPlanDone;   // RunwayPlan bookkeeping (only used when a profile has a plan)
    private int[] obstacleLegPlan;
    private Mesh obstacleLegPoleMesh;
    private float obstacleLegPoleMeshKey;
    private Mesh obstacleLegTriangleMesh;
    private Vector2 obstacleLegTriangleMeshKey;

    /// <summary>True while the runner is on an obstacle leg.</summary>
    public bool PlayerOnObstacleLeg
    {
        get
        {
            Segment s = PlayerSegment;
            return s != null && s.portalRole == 3 && player != null && player.currentCube != null && player.currentCube != firstCube;
        }
    }

    void ResetObstacleLeg()
    {
        obstacleLegRng ??= new System.Random();
        obstacleLegPlanCycle = -1;
        obstacleLegPlanDone = 0;
        endlessDesignCount = 0;
        nextObstacleLegSerial = segmentSerial + RollObstacleLegGap();
        if (obstacleTestMode && slowMoSections) nextObstacleLegSerial = segmentSerial + 2;   // Test Obstacles: the slow-motion section goes first
        if (obstacleLegSections && GetComponent<ObstacleLegCamera>() == null)
            gameObject.AddComponent<ObstacleLegCamera>().pathGen = this;
    }

    int RollObstacleLegGap()
    {
        if (obstacleTestMode && obstacleLegDesigns) return 1;   // Test Obstacles: one ordinary leg between two obstacle legs
        if (EndlessDesigns)   // 3.11 endless run: one obstacle leg every 50-60 legs (...Designs.cs)
        {
            int elo = Mathf.Max(3, Mathf.Min(endlessObstacleEveryLegs.x, endlessObstacleEveryLegs.y));
            return obstacleLegRng.Next(elo, Mathf.Max(elo, Mathf.Max(endlessObstacleEveryLegs.x, endlessObstacleEveryLegs.y)) + 1);
        }
        int lo = Mathf.Max(2, Mathf.Min(minLegsBetweenObstacleLegs, maxLegsBetweenObstacleLegs));
        int hi = Mathf.Max(lo, Mathf.Max(minLegsBetweenObstacleLegs, maxLegsBetweenObstacleLegs));
        return obstacleLegRng.Next(lo, hi + 1);
    }

    /// <summary>
    /// Called by AppendSegment (after the door check, before boosts). When an obstacle leg is due, hands back a flat
    /// 90 degree heading and its length, if it keeps clear of the old track; otherwise false and the next leg tries.
    /// </summary>
    bool TakeObstacleLeg(Vector3 prevDir, ref int segLen, out Vector3 dir)
    {
        dir = Vector3.zero;
        if (!obstacleLegSections || tutorialActive) return false;
        if (NoSpecialsHere) return false;                                         // 3.11: the first level has no obstacle leg
        if (slowMoPlanIndex < slowMoPlanDirs.Count) return false;                 // let a slow-motion section finish first
        if (slowMoSections && segmentSerial < slowMoEndSerial + 4) return false;   // ... and keep 4 legs clear after it
        // A profile with a runway plan (scene 3.10) says exactly which legs of each level get one; the random cadence is off.
        if (obstacleLegPlanCycle != cycleIndex)
        {
            // Once per cycle: the profile's runway legs (it may roll them with our random: not the same legs every run).
            obstacleLegPlanCycle = cycleIndex;
            obstacleLegPlanDone = 0;
            obstacleLegRng ??= new System.Random();
            // (an endless run has no end to plan against: the random cadence below lays them, every 50-60 legs)
            obstacleLegPlan = SpecialsOn ? SpecialRunwayPlan()
                           : difficultyProfile != null && !EndlessDesigns ? difficultyProfile.RunwayPlan(cycleIndex, obstacleLegRng) : null;
        }
        int[] plan = obstacleLegPlan;
        bool planned = plan != null;
        if (planned)
        {
            int pos = segmentSerial - cycleStartSerial;
            while (obstacleLegPlanDone < plan.Length && pos > plan[obstacleLegPlanDone] + 4) obstacleLegPlanDone++;   // window slipped by: dropped
            if (obstacleLegPlanDone >= plan.Length || pos < plan[obstacleLegPlanDone]) return false;
            if (portalHold || portalExitPending) return false;
            if (portalZones && segmentSerial + 4 >= nextPortalSerial) return false;   // never within 4 legs of a door
        }
        else
        {
            // Coming out of a door (a crossing or a checkpoint start): the count starts over from here.
            Segment before = allSegments.Count > 0 ? allSegments[allSegments.Count - 1] : null;
            if (before != null && before.portalRole == 2 && nextObstacleLegSerial < segmentSerial + 3)
                nextObstacleLegSerial = segmentSerial + RollObstacleLegGap();
            if (segmentSerial < nextObstacleLegSerial) return false;
            if (portalHold || portalExitPending) return false;
            if (portalZones && segmentSerial + 3 >= nextPortalSerial) return false;   // not right before a door
        }
        if (boostPlanIndex < boostPlanDirs.Count) return false;                   // let a boost finish first
        if (RunwayBlocked311()) return false;                                     // 3.11: 4 legs clear of a boost / slow motion
        if (Mathf.Abs(prevDir.y) > 0.9f) return false;                            // off a climb/dive the landing face is awkward: next leg

        float scale = Mathf.Lerp(Mathf.Min(obstacleLegLengthScale.x, obstacleLegLengthScale.y),
                                 Mathf.Max(obstacleLegLengthScale.x, obstacleLegLengthScale.y), (float)obstacleLegRng.NextDouble());
        int want = Mathf.RoundToInt(ExitRunwayCubes() * Mathf.Max(1f, scale));
        float v = Mathf.Max(0.5f, LegSpeed(segmentSerial, false));
        // Long enough in TIME for obstacleLegMinStations stations at this speed (the gaps are seconds, the runway is cubes:
        // at the opening speeds 1.6x the door runway held only one or two).
        float half = TB ? difficultyProfile.MinLegSeconds(2, segmentSerial, false) : 0f;
        float gap = TB ? Mathf.Max(obstacleLegMinGapSeconds, half * obstacleLegGapScale)
                       : obstacleLegMinGapSeconds + 1.5f * obstacleLegSecondsPerRoll;
        gap *= 1f + obstacleLegGapJitter * 0.5f;
        float seconds = Mathf.Max(obstacleLegLeadSeconds, half) + Mathf.Max(obstacleLegTailSeconds, obstacleLegGlideOutSeconds + half)
                        + Mathf.Max(0, obstacleLegMinStations - 1) * gap + 0.3f;
        int floor = SecondsToCubes(seconds, v);
        want = Mathf.Max(want, floor);
        runwayStationsNext = 0;
        if (L311 != null && TB)
        {
            // 3.11: exactly the level's stations, so the runway is as long as they need.
            runwayStationsNext = RunwayStations311();
            float lead311 = Mathf.Max(obstacleLegLeadSeconds, half), tail311 = Mathf.Max(obstacleLegTailSeconds, obstacleLegGlideOutSeconds + half);
            floor = SecondsToCubes(RunwaySeconds311(runwayStationsNext, lead311, tail311), v);
            want = floor;
        }
        if (obstacleTestMode && obstacleLegDesigns) want = floor = SecondsToCubes(obstacleTestLegSeconds, v);   // Test Obstacles: ~10 s each

        Vector3 origin = lastPosition + prevDir * cubeSize;
        var options = new List<Vector3>();
        foreach (Vector3 d in FlatDirs)
        {
            if (Mathf.Abs(Vector3.Dot(d, prevDir)) > 0.9f) continue;
            if (lastHorizontalDirection.sqrMagnitude > 0.01f && Vector3.Dot(d, lastHorizontalDirection) < -0.9f) continue;
            options.Add(d);
        }
        for (int i = options.Count - 1; i > 0; i--)
        {
            int j = obstacleLegRng.Next(0, i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }

        // The full length if it fits, down to 75% of it (never under the floor) before giving up for this leg.
        int shortest = Mathf.Max(floor, Mathf.RoundToInt(want * 0.75f));
        for (int len = want; len >= shortest; len -= 2)
            foreach (Vector3 d in options)
            {
                if (!LegIsClear(origin, d, len)) continue;
                dir = d;
                segLen = len;
                lastHorizontalDirection = d;
                nextObstacleLegSerial = segmentSerial + 1 + RollObstacleLegGap();
                if (planned)
                {
                    obstacleLegPlanDone++;
                    // A breather straight after the runway: the next turns need one roll at most (as after a boost).
                    breatherFrom = segmentSerial + 1;
                    breatherUntil = segmentSerial + 3;
                }
                if (!Waves) nextBoostSerial = Mathf.Max(nextBoostSerial, segmentSerial + 3);   // no boost straight after
                return true;
            }
        return false;
    }

    int SecondsToCubes(float seconds, float speed) => Mathf.CeilToInt(seconds * speed / Mathf.Max(0.01f, cubeSize));

    // Before the leg joins a zone: close the shot in front of it, so the obstacle leg gets a shot of its own.
    void SealZoneBeforeObstacleLeg()
    {
        if (zones.Count > 0 && !zones[zones.Count - 1].isSealed) SealZone(zones[zones.Count - 1]);
    }

    // The leg is down and in its own zone: frame only its start (the long rest is the chase camera's), seal it,
    // dress it like a door runway and stand the poles.
    void OnObstacleLegLaid(Segment seg)
    {
        OnRunwayLaid311(seg);   // 3.11: glide after it, finale burst (no-op elsewhere)
        PathZone zone = seg.zone;
        if (zone != null)
        {
            int k = Mathf.Min(seg.cubes.Count, 16);
            Segment handover = zone.previous != null ? zone.previous.FinalSegment : null;
            bool first = true;
            void Add(Vector3 p) { if (first) { zone.zoneBounds = new Bounds(p, Vector3.zero); first = false; } else zone.Encapsulate(p); }
            if (handover != null) foreach (GameObject c in handover.cubes) if (c != null) Add(c.transform.position);
            Add(seg.startPosition);
            for (int i = 0; i < k; i++) if (seg.cubes[i] != null) Add(seg.cubes[i].transform.position);
            zone.framingSolved = false;
            if (!zone.isSealed) SealZone(zone);
        }

        if (obstacleLegTiles) DecoratePortalRunway(seg);   // square tiles, as on the door runways (off: an ordinary-looking leg)
        PlaceObstacleLegPoles(seg);
    }

    /// <summary>For the zone after an obstacle leg: how many of its last cubes the hand-over may pull into the shot (the rest stays out).</summary>
    int ObstacleLegHandoverCubes(Segment handover)
    {
        float v = Mathf.Max(0.5f, LegSpeed(handover.serial, false));
        return Mathf.Clamp(SecondsToCubes(obstacleLegTailSeconds, v), 6, handover.cubes.Count);
    }

    // ------------------------------------------------------------------ poles

    static int RollsBetween(int a, int b) { int d = Mathf.Abs(a - b) % 4; return Mathf.Min(d, 4 - d); }

    // The rolls the runner may need from any face that was safe at the last station to some face safe at the next.
    static int RollsNeeded(int prevBlockedMask, int nextBlockedMask)
    {
        int worst = 0;
        for (int p = 0; p < 4; p++)
        {
            if ((prevBlockedMask & (1 << p)) != 0) continue;
            int best = 4;
            for (int n = 0; n < 4; n++)
                if ((nextBlockedMask & (1 << n)) == 0) best = Mathf.Min(best, RollsBetween(p, n));
            worst = Mathf.Max(worst, best);
        }
        return worst;
    }

    float GapSeconds(int rolls, int serial)
    {
        float s = TB ? difficultyProfile.MinLegSeconds(rolls, serial, false) * obstacleLegGapScale
                     : obstacleLegMinGapSeconds + rolls * obstacleLegSecondsPerRoll;
        s = Mathf.Max(obstacleLegMinGapSeconds, s);
        return s * (1f + (float)obstacleLegRng.NextDouble() * obstacleLegGapJitter);
    }

    void PlaceObstacleLegPoles(Segment seg)
    {
        if (obstacleLegDesigns && PlaceObstacleLegDesigns(seg)) return;   // 3.11: the per-checkpoint designs (...Designs.cs)

        List<GameObject> cubes = seg.cubes;
        int n = cubes.Count;
        float v = Mathf.Max(0.5f, LegSpeed(seg.serial, false));
        float cps = v / Mathf.Max(0.01f, cubeSize);

        // Arriving, the runner may be on any face: the first station is at least a half turn's floor in.
        float lead = obstacleLegLeadSeconds;
        if (TB) lead = Mathf.Max(lead, difficultyProfile.MinLegSeconds(2, seg.serial, false));
        float tail = obstacleLegTailSeconds;
        if (TB) tail = Mathf.Max(tail, obstacleLegGlideOutSeconds + difficultyProfile.MinLegSeconds(2, seg.serial, false));
        int first = Mathf.Max(3, Mathf.CeilToInt(lead * cps));
        int lastAllowed = n - 1 - Mathf.CeilToInt(tail * cps);

        if (obstacleLayer < 0) obstacleLayer = ResolveObstacleLayer();
        if (obstacleShading == null) obstacleShading = GetComponent<PathDepthShading>();

        int stations = 0, lastPole = -1;
        int prevMask = 0;   // nothing blocked before the first station
        float at = first;
        var log = logObstacleLegs ? new System.Text.StringBuilder() : null;
        while (true)
        {
            // Two pillars on OPPOSITE faces of the same cube (top + bottom, or left + right): the other pair is the way through.
            int a = obstacleLegRng.Next(0, 4), b = (a + 2) % 4;
            int mask = (1 << a) | (1 << b);
            // Only rolled when triangles are on, so 3.9 keeps its exact random sequence.
            bool triangle = obstacleLegTriangleChance > 0f && obstacleLegRng.NextDouble() < obstacleLegTriangleChance;
            if (stations > 0)
            {
                int rolls = RollsNeeded(prevMask, mask);
                at += GapSeconds(rolls, seg.serial) * cps;
            }
            int index = Mathf.RoundToInt(at);
            if (index > lastAllowed || index >= n) break;
            if (cubes[index] != null)
            {
                PlacePole(cubes[index].transform, a, triangle);
                PlacePole(cubes[index].transform, b, triangle);
                log?.Append(index).Append(':').Append(a).Append(b).Append(' ');
            }
            prevMask = mask;
            lastPole = index;
            stations++;
        }

        seg.obstacleGlideInCubes = Mathf.Max(2f, Mathf.Min(first - 1f, obstacleLegLeadSeconds * 0.8f * cps));
        float outCubes = Mathf.Max(2f, obstacleLegGlideOutSeconds * cps);
        seg.obstacleGlideOutCubes = outCubes;
        seg.obstacleGlideOutFrom = Mathf.Min((lastPole >= 0 ? lastPole : first) + 1.5f, n - 1 - outCubes);

        if (logObstacleLegs)
            Debug.Log($"[ObstacleLeg] leg {seg.serial}: {n} cubes at {v:0.0} u/s, {stations} stations (cube:faces) {log}, glide back from cube {seg.obstacleGlideOutFrom:0}");
    }

    // One thin pole out of the cube's face `side` (0 up, 1 right, 2 down, 3 left), drawn and faded with the cube
    // (PathObstacleGuard, no collider), deadly by the runner's position (ObstacleLegPoleGuard).
    void PlacePole(Transform cubeT, int side, bool triangle = false)
    {
        Vector3 outward = side == 0 ? cubeT.up : side == 1 ? cubeT.right : side == 2 ? -cubeT.up : -cubeT.right;
        var go = new GameObject("ObstaclePole") { layer = obstacleLayer };
        go.transform.SetParent(cubeT, true);
        go.transform.position = cubeT.position + outward * (cubeSize * 0.5f);
        go.transform.rotation = Quaternion.LookRotation(cubeT.forward, outward);   // local +y = out of the face, +-z = along the leg

        float halfThickness;
        if (triangle)
        {
            // A four-sided spike, base flat on the face, apex outward.
            go.name = "ObstacleTriangle";
            go.AddComponent<MeshFilter>().sharedMesh = GetObstacleLegTriangleMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = obstacleLegTriangleMaterial != null ? obstacleLegTriangleMaterial : obstaclePyramidMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            halfThickness = obstacleLegTriangleSize.x * 0.4f;   // it tapers: a little less than its base, fairer than a pillar
        }
        else if (obstacleLegTrees)
        {
            // A Monument-Valley tree: thin trunk, bright crown (shape / colours per level: LegStageColors).
            float w = obstacleLegTreeSize.x * cubeSize, h = obstacleLegTreeSize.y * cubeSize;
            Material tm = obstacleLegPoleMaterial != null ? obstacleLegPoleMaterial : obstaclePyramidMaterial;
            var trunk = new GameObject("TreeTrunk") { layer = obstacleLayer };
            trunk.transform.SetParent(go.transform, false);
            trunk.AddComponent<MeshFilter>().sharedMesh = ObstacleTreeMesh.Trunk(0.16f * w, h * 0.55f);
            var tr = trunk.AddComponent<MeshRenderer>();
            tr.sharedMaterial = tm;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var crown = new GameObject("TreeCrown") { layer = obstacleLayer };
            crown.transform.SetParent(go.transform, false);
            crown.AddComponent<MeshFilter>().sharedMesh = ObstacleTreeMesh.Crown(1, w, h * 0.55f, h * 0.45f);
            var cr = crown.AddComponent<MeshRenderer>();
            cr.sharedMaterial = tm;
            cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            halfThickness = obstacleLegTreeSize.x * 0.4f;
        }
        else halfThickness = BuildPillarBody(go.transform);

        var guard = go.AddComponent<PathObstacleGuard>();
        guard.player = player;
        guard.blockedSide = side;
        guard.Init(cubeT.gameObject, obstacleShading, go.transform.localScale, new Collider[0], null);

        var kill = go.AddComponent<ObstacleLegPoleGuard>();
        kill.player = player;
        kill.guard = guard;
        kill.host = cubeT;
        kill.blockedSide = side;
        kill.reach = 0.42f + halfThickness;   // the runner's half length (a touch forgiving) + half the pillar
    }

    // The pillar itself under `go` (local +y = out of the face, +-z = along the leg): the totem blocks stacked out of the
    // face, or the thin pole when the scene has no blocks. Returns half its thickness along the leg, in cubes.
    // Also used by the 3.11 sliding pillars (ObstacleLegHazard), so they look exactly like the standing ones.
    float BuildPillarBody(Transform go)
    {
        if (HasPillarBlocks())
        {
            // A totem pillar: the blocks stacked out of the face, first one against it. Their eye / rivet faces (+-z) look along the leg.
            float s = obstacleLegPillarBlockSize * cubeSize;
            for (int k = 0; k < obstacleLegPillarBlocks.Length; k++)
            {
                GameObject prefab = obstacleLegPillarBlocks[k];
                if (prefab == null) continue;
                GameObject block = Instantiate(prefab, go, false);
                block.name = "PillarBlock" + k;
                block.transform.localPosition = new Vector3(0f, s * (k + 0.5f), 0f);
                block.transform.localRotation = Quaternion.identity;
                block.transform.localScale = Vector3.one * s;
                foreach (Collider c in block.GetComponentsInChildren<Collider>(true)) { c.enabled = false; Destroy(c); }
                foreach (Transform t in block.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = obstacleLayer;
                foreach (Renderer r in block.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    // Mobile: the rivet rim + ring are tiny extra meshes per rivet (hundreds on a long runway); the centre dot keeps the rivet.
                    if (obstacleLegSimpleRivets && (r.name.StartsWith("RivetRim") || r.name.StartsWith("RivetRing"))) Destroy(r.gameObject);
                }
            }
            return s * 0.5f / Mathf.Max(0.01f, cubeSize);
        }
        go.gameObject.AddComponent<MeshFilter>().sharedMesh = GetObstacleLegPoleMesh();
        var mr = go.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = obstacleLegPoleMaterial != null ? obstacleLegPoleMaterial : obstaclePyramidMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return obstacleLegPoleThickness * 0.5f;
    }

    // A square pyramid standing on y = 0 (the face), apex at +y.
    Mesh GetObstacleLegTriangleMesh()
    {
        if (obstacleLegTriangleMesh != null && obstacleLegTriangleMeshKey == obstacleLegTriangleSize) return obstacleLegTriangleMesh;
        float h = obstacleLegTriangleSize.x * cubeSize * 0.5f;
        Vector3 apex = Vector3.up * (obstacleLegTriangleSize.y * cubeSize);
        Vector3 c0 = new Vector3(-h, 0f, -h), c1 = new Vector3(h, 0f, -h), c2 = new Vector3(h, 0f, h), c3 = new Vector3(-h, 0f, h);
        Vector3[] verts =
        {
            c0, apex, c1,   c1, apex, c2,   c2, apex, c3,   c3, apex, c0,
            c0, c1, c2,     c0, c2, c3,
        };
        int[] tris = new int[verts.Length];
        for (int i = 0; i < tris.Length; i++) tris[i] = i;
        var mesh = new Mesh { name = "ObstacleLegTriangle", vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        obstacleLegTriangleMesh = mesh;
        obstacleLegTriangleMeshKey = obstacleLegTriangleSize;
        return mesh;
    }

    bool HasPillarBlocks()
    {
        if (obstacleLegPillarBlocks == null) return false;
        foreach (GameObject b in obstacleLegPillarBlocks) if (b != null) return true;
        return false;
    }

    // A thin box standing on y = 0 (the face), obstacleLegPoleLength cubes tall.
    Mesh GetObstacleLegPoleMesh()
    {
        float h = obstacleLegPoleThickness * cubeSize * 0.5f;
        float len = obstacleLegPoleLength * cubeSize;
        float key = h * 1000f + len;
        if (obstacleLegPoleMesh != null && Mathf.Approximately(key, obstacleLegPoleMeshKey)) return obstacleLegPoleMesh;

        Vector3 c0 = new Vector3(-h, 0f, -h), c1 = new Vector3(h, 0f, -h), c2 = new Vector3(h, 0f, h), c3 = new Vector3(-h, 0f, h);
        Vector3 up = Vector3.up * len;
        Vector3 t0 = c0 + up, t1 = c1 + up, t2 = c2 + up, t3 = c3 + up;
        Vector3[] verts =
        {
            t0, t1, t2,  t0, t2, t3,     // top
            c0, t0, t3,  c0, t3, c3,     // -x
            c1, c2, t2,  c1, t2, t1,     // +x
            c0, c1, t1,  c0, t1, t0,     // -z
            c3, t3, t2,  c3, t2, c2,     // +z
        };
        int[] tris = new int[verts.Length];
        for (int i = 0; i < tris.Length; i++) tris[i] = i;
        var mesh = new Mesh { name = "ObstacleLegPole", vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        obstacleLegPoleMesh = mesh;
        obstacleLegPoleMeshKey = key;
        return mesh;
    }
}

/// <summary>Flat-shaded meshes of the obstacle-leg trees (scene 3.10). Built in world units on y = 0 (the cube face), +y outward.
/// Crown shapes: 0 stepped blocks, 1 ball, 2 two-tier cone, 3 diamond.</summary>
public static class ObstacleTreeMesh
{
    public const int Shapes = 4;
    static readonly System.Collections.Generic.Dictionary<long, Mesh> cache = new System.Collections.Generic.Dictionary<long, Mesh>();

    static void Tri(System.Collections.Generic.List<Vector3> v, Vector3 a, Vector3 b, Vector3 c, Vector3 centre)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (Vector3.Dot(n, (a + b + c) / 3f - centre) < 0f) { Vector3 t = b; b = c; c = t; }
        v.Add(a); v.Add(b); v.Add(c);
    }

    static void Box(System.Collections.Generic.List<Vector3> v, Vector3 min, Vector3 max)
    {
        Vector3 c = (min + max) * 0.5f;
        Vector3[] p =
        {
            new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z),
        };
        int[,] f = { {0,1,2,3}, {4,5,6,7}, {0,1,5,4}, {1,2,6,5}, {2,3,7,6}, {3,0,4,7} };
        for (int i = 0; i < 6; i++)
        {
            Tri(v, p[f[i,0]], p[f[i,1]], p[f[i,2]], c);
            Tri(v, p[f[i,0]], p[f[i,2]], p[f[i,3]], c);
        }
    }

    // Pyramid / cone with `sides` sides, base ring at y0 (radius r0), apex at y1 (radius r1 = 0 for a point, else a flat top).
    static void Cone(System.Collections.Generic.List<Vector3> v, int sides, float y0, float y1, float r0, float r1)
    {
        Vector3 c = new Vector3(0f, (y0 + y1) * 0.5f, 0f);
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides + Mathf.PI / sides, a1 = (i + 1) * Mathf.PI * 2f / sides + Mathf.PI / sides;
            Vector3 b0 = new Vector3(Mathf.Cos(a0) * r0, y0, Mathf.Sin(a0) * r0), b1 = new Vector3(Mathf.Cos(a1) * r0, y0, Mathf.Sin(a1) * r0);
            Vector3 t0 = new Vector3(Mathf.Cos(a0) * r1, y1, Mathf.Sin(a0) * r1), t1 = new Vector3(Mathf.Cos(a1) * r1, y1, Mathf.Sin(a1) * r1);
            Tri(v, b0, b1, t1, c);
            if (r1 > 0f) Tri(v, b0, t1, t0, c);
            Tri(v, new Vector3(0f, y0, 0f), b0, b1, c);
            if (r1 > 0f) Tri(v, new Vector3(0f, y1, 0f), t0, t1, c);
        }
    }

    static Mesh Make(string name, System.Collections.Generic.List<Vector3> v)
    {
        int[] tris = new int[v.Count];
        for (int i = 0; i < tris.Length; i++) tris[i] = i;
        var m = new Mesh { name = name, vertices = v.ToArray(), triangles = tris };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    public static Mesh Trunk(float thick, float height)
    {
        long key = (long)(thick * 10000f) * 100000L + (long)(height * 1000f);
        if (cache.TryGetValue(key, out Mesh m) && m != null) return m;
        var v = new System.Collections.Generic.List<Vector3>();
        float h = thick * 0.5f;
        Box(v, new Vector3(-h, 0f, -h), new Vector3(h, height, h));
        m = Make("TreeTrunk", v);
        cache[key] = m;
        return m;
    }

    /// <summary>Crown of `shape`, width w, sitting on y0 and `height` tall.</summary>
    public static Mesh Crown(int shape, float w, float y0, float height)
    {
        shape = Mathf.Clamp(shape, 0, Shapes - 1);
        long key = 1000000000000L + shape * 100000000000L + (long)(w * 1000f) * 1000000L + (long)(y0 * 1000f) * 1000L + (long)(height * 1000f);
        if (cache.TryGetValue(key, out Mesh m) && m != null) return m;
        var v = new System.Collections.Generic.List<Vector3>();
        float r = w * 0.5f;
        switch (shape)
        {
            case 0:
                Box(v, new Vector3(-r, y0 - height * 0.12f, -r), new Vector3(r, y0 + height * 0.5f, r));
                Box(v, new Vector3(-r * 0.62f, y0 + height * 0.5f, -r * 0.62f), new Vector3(r * 0.62f, y0 + height, r * 0.62f));
                break;
            case 1:
            {
                // low-poly ball: 8 segments x 4 rings, squashed to the crown height
                int seg = 8, rings = 4;
                Vector3 c = new Vector3(0f, y0 + height * 0.5f, 0f);
                for (int j = 0; j < rings; j++)
                {
                    float p0 = Mathf.PI * j / rings, p1 = Mathf.PI * (j + 1) / rings;
                    for (int i = 0; i < seg; i++)
                    {
                        float a0 = Mathf.PI * 2f * i / seg, a1 = Mathf.PI * 2f * (i + 1) / seg;
                        Vector3 P(float p, float a) => c + new Vector3(Mathf.Sin(p) * Mathf.Cos(a) * r, Mathf.Cos(p) * height * 0.5f, Mathf.Sin(p) * Mathf.Sin(a) * r);
                        Vector3 q00 = P(p0, a0), q01 = P(p0, a1), q10 = P(p1, a0), q11 = P(p1, a1);
                        if (j > 0) Tri(v, q00, q01, q11, c);
                        if (j < rings - 1) Tri(v, q00, q11, q10, c);
                    }
                }
                break;
            }
            case 2:
                Cone(v, 6, y0 - height * 0.1f, y0 + height * 0.62f, r, 0f);
                Cone(v, 6, y0 + height * 0.4f, y0 + height, r * 0.68f, 0f);
                break;
            default:
            {
                Vector3 c = new Vector3(0f, y0 + height * 0.5f, 0f);
                Vector3 top = c + Vector3.up * height * 0.5f, bot = c - Vector3.up * height * 0.5f;
                Vector3[] e = { c + new Vector3(r, 0, 0), c + new Vector3(0, 0, r), c + new Vector3(-r, 0, 0), c + new Vector3(0, 0, -r) };
                for (int i = 0; i < 4; i++)
                {
                    Tri(v, top, e[i], e[(i + 1) % 4], c);
                    Tri(v, bot, e[i], e[(i + 1) % 4], c);
                }
                break;
            }
        }
        m = Make("TreeCrown" + shape, v);
        cache[key] = m;
        return m;
    }
}
