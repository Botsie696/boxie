using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates one continuous cube rail and groups it into ZONES - the dioramas the camera
/// frames one at a time.
///
/// A zone is a run of consecutive segments (4-5 direction changes) plus the world-space
/// Bounds that contains every cube in them. The camera holds still on one zone for as long
/// as the runner is inside it, then transitions to the next, so a zone is both a chunk of
/// level and a camera shot.
///
/// Two pieces of geometry here are load-bearing and easy to "tidy" into a broken state:
///
///   * the DIAGONAL RISER at every corner (see AppendSegment). CubeRailRunner changes cube
///     only via a forward probe cast from (cube centre + faceNormal * 1.05). A corner cube
///     placed at last + newDir sits off that probe's line and is never seen, so the runner
///     sails off the end of every leg and flies forever.
///   * the HALF-TURN budget (see GetNextDirection). The runner rotates one face per
///     switchCooldown, so a turn that needs a half rotation costs two inputs. On a leg that
///     is crossed faster than that, such a turn is not hard, it is impossible.
/// </summary>
// Build the path and its zones before the camera's Start runs. Without this the order is
// undefined: the rig can wake first, find no zone to frame, open on a fallback pose, and then
// snap to the real shot on the first LateUpdate - a visible jump on frame two.
// Coins along the track live in DynamicPathGeneratorCoins.cs, boost sections in DynamicPathGeneratorBoost.cs.
[DefaultExecutionOrder(-100)]
public partial class DynamicPathGenerator : MonoBehaviour
{
    // One direction change on the path, for anything that wants to read the route ahead.
    public struct PathCorner
    {
        public Vector3 position;           // world position of the first cube of the outgoing leg
        public Vector3 incomingDirection;  // direction of the leg arriving at the corner
        public Vector3 outgoingDirection;  // direction of the leg leaving the corner
        public int segmentIndex;           // global index of the outgoing segment
    }

    /// <summary>A straight run of cubes between two turns. (Partial: the difficulty fields live in DynamicPathGenerator.TimeBudget.cs.)</summary>
    public partial class Segment
    {
        public readonly List<GameObject> cubes = new List<GameObject>();
        public Vector3 direction;
        public Vector3 startPosition;
        public Vector3 endPosition;
        public PathZone zone;
        public bool boost;          // a leg of a boost section (DynamicPathGeneratorBoost.cs)
        public int boostId;         // which boost, counting from 1
        public int boostLeg;        // which leg of it, from 0
        public int portalRole;      // 1 = the runway INTO a zone door, 2 = the runway out of one (DynamicPathGeneratorPortal.cs)
        public List<GameObject> decoyCubes;   // a fake dead-end stub off this leg's first corner (DynamicPathGeneratorDecoy.cs), null = none
        public List<GameObject> archObjects;  // decorative sag-curve under each bridge plank span (BridgeArchDecoration.cs), null = none
    }

    /// <summary>
    /// A cluster of segments framed as a single static camera shot.
    ///
    /// optimalCameraPosition / optimalOrthoSize / optimalCameraYaw are the solved shot. They
    /// are filled by SolveFraming, which the camera rig calls: the solve needs the rig's tilt,
    /// padding and the live Camera.aspect, so the rig is the one that owns those inputs and
    /// this class is the one that caches the answer.
    /// </summary>
    public partial class PathZone
    {
        public readonly List<Segment> segments = new List<Segment>();
        public Bounds zoneBounds;
        public PathZone previous;   // the zone before this one, for the fallback yaw choice

        public Vector3 optimalCameraPosition;
        public Vector3 optimalFocus;      // the point the shot is centred on
        public float optimalBoom;         // pull-back from the focus along the view axis
        public float optimalOrthoSize;
        public float optimalCameraYaw;

        public int capacity;              // segments this zone accepts before sealing
        public bool isSealed;
        public bool boundsStarted;

        public bool framingSolved;
        private float solvedAspect;

        // Yaw chosen by DynamicPathGenerator.PlanCameraYaws, which weighs the whole run of zones
        // ahead together. A zone the plan has not reached yet scores itself alone.
        public float forcedYaw = -1f;   // >= 0: only this yaw may be chosen (the runway out of a zone door keeps the camera angle)
        public bool yawPlanned;
        public float plannedYaw;

        /// <summary>
        /// Set once the camera has committed to this shot. After that the solved values are
        /// frozen even if the zone's bounds are still growing.
        ///
        /// Without this the rig is interpolating toward, or sitting on, numbers that the
        /// generator can still rewrite - extending a zone over its hand-off enlarges its bounds
        /// and re-solves it, and the pose jumps under a camera that is supposed to be locked.
        /// That showed up as a 13-unit orthographic step in the middle of a "static" shot.
        /// </summary>
        public bool framingLocked;

        public void LockFraming() { framingLocked = true; }

        public Segment FinalSegment => segments.Count > 0 ? segments[segments.Count - 1] : null;

        public void Encapsulate(Vector3 point)
        {
            if (!boundsStarted)
            {
                zoneBounds = new Bounds(point, Vector3.zero);
                boundsStarted = true;
            }
            else
            {
                zoneBounds.Encapsulate(point);
            }
        }

        /// <summary>
        /// Solves the shot: where the camera looks, from which side, and how wide it is.
        ///
        /// The zone's Bounds are axis aligned in WORLD space, which says nothing about how much
        /// screen the zone needs - a long diagonal leg and a long straight one can share a box.
        /// So the eight corners are rotated into the camera's own space first, and the frame is
        /// fitted to THAT box. In a portrait viewport width is the scarce axis, which is what
        /// dividing the horizontal extent by aspect accounts for.
        /// </summary>
        public void SolveFraming(float tiltX, float yawOffset, bool quantiseYaw, float aspect,
                                 float padding, float minOrtho, float boomDistance,
                                 Vector3 fallbackHeading)
        {
            if (framingLocked && framingSolved) return;

            float yaw = yawPlanned
                ? plannedYaw
                : BestStandaloneYaw(tiltX, yawOffset, quantiseYaw, fallbackHeading);

            if (framingSolved && Mathf.Approximately(solvedAspect, aspect) &&
                Mathf.Approximately(yaw, optimalCameraYaw)) return;

            optimalCameraYaw = yaw;

            Quaternion rot = Quaternion.Euler(tiltX, optimalCameraYaw, 0f);
            Quaternion inv = Quaternion.Inverse(rot);

            Vector3 c = zoneBounds.center;
            Vector3 e = zoneBounds.extents;

            Bounds localBounds = new Bounds(inv * c, Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                localBounds.Encapsulate(inv * corner);
            }

            float verticalSize = localBounds.extents.y;
            float horizontalSize = localBounds.extents.x / Mathf.Max(0.01f, aspect);
            optimalOrthoSize = Mathf.Max(verticalSize, horizontalSize) + padding;
            optimalOrthoSize = Mathf.Max(optimalOrthoSize, minOrtho);

            // Centre on the PROJECTED box, not the world box, so the shot is centred on what
            // the viewer actually sees. Orthographic framing does not depend on the pull-back,
            // so it only has to clear the near plane - but it does have to clear a big zone.
            optimalBoom = Mathf.Max(boomDistance, e.magnitude + 10f);
            optimalFocus = rot * localBounds.center;
            optimalCameraPosition = optimalFocus - (rot * Vector3.forward) * optimalBoom;

            solvedAspect = aspect;
            framingSolved = true;
        }

        /// <summary>
        /// The four yaws the camera may take for this zone: the average heading, snapped to the
        /// grid, then turned by 0/90/180/270 plus the isometric offset. With the snap on these
        /// are always the four true isometric yaws (45/135/225/315), which keeps every leg
        /// receding on the diagonal and the inner corner of each turn open to camera.
        /// </summary>
        public void GetCandidateYaws(float yawOffset, bool quantiseYaw, Vector3 fallbackHeading, float[] into)
        {
            Vector3 sum = Vector3.zero;
            foreach (Segment s in segments)
                sum += new Vector3(s.direction.x, 0f, s.direction.z);

            if (sum.sqrMagnitude < 0.0001f)
                sum = new Vector3(fallbackHeading.x, 0f, fallbackHeading.z);
            if (sum.sqrMagnitude < 0.0001f)
                sum = Vector3.forward;

            float yaw = Mathf.Atan2(sum.x, sum.z) * Mathf.Rad2Deg;
            if (quantiseYaw) yaw = Mathf.Round(yaw / 90f) * 90f;

            for (int k = 0; k < into.Length; k++)
                into[k] = Mathf.Repeat(yaw + k * 90f + yawOffset, 360f);
        }

        /// <summary>
        /// How hard this zone is to read from a yaw. Lower is better. Weighs:
        ///   * places where one leg crosses in front of another on screen,
        ///   * track running TOWARD the camera - it slides down-screen and its direction is
        ///     easy to misread, whereas track receding from the camera climbs the screen and
        ///     reads as "ahead".
        /// Deliberately NOT weighed: how tightly the zone frames. Preferring compact shots pulled
        /// the camera in on every zone.
        /// </summary>
        public float ScoreYaw(float tiltX, float yaw)
        {
            if (forcedYaw >= 0f && Mathf.Abs(Mathf.DeltaAngle(yaw, forcedYaw)) > 1f) return 100000f;
            Quaternion inv = Quaternion.Inverse(Quaternion.Euler(tiltX, yaw, 0f));

            float approach = 0f;
            foreach (Segment s in segments)
            {
                Vector3 d = inv * new Vector3(s.direction.x, 0f, s.direction.z);
                if (d.z < -0.5f) approach += s.cubes.Count * 0.5f;
            }

            var pts = new List<Vector3>();
            foreach (Segment s in segments)
                foreach (GameObject cube in s.cubes)
                    if (cube != null) pts.Add(inv * cube.transform.position);

            int overlaps = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                // Cubes that land on top of each other on screen while being far apart in depth.
                // Neighbours in the same run are close in depth and are not counted.
                for (int j = i + 1; j < pts.Count; j++)
                {
                    float dx = pts[i].x - pts[j].x;
                    float dy = pts[i].y - pts[j].y;
                    if (dx * dx + dy * dy > 1.44f) continue;
                    if (Mathf.Abs(pts[i].z - pts[j].z) > 2.5f) overlaps++;
                }
            }

            return overlaps * 1.5f + approach;
        }

        // Used only until PlanCameraYaws has reached this zone.
        float BestStandaloneYaw(float tiltX, float yawOffset, bool quantiseYaw, Vector3 fallbackHeading)
        {
            var candidates = new float[4];
            GetCandidateYaws(yawOffset, quantiseYaw, fallbackHeading, candidates);

            float bestYaw = candidates[0];
            float bestScore = float.MaxValue;
            foreach (float candidate in candidates)
            {
                float score = ScoreYaw(tiltX, candidate);
                if (previous != null && previous.framingSolved)
                    score += Mathf.Abs(Mathf.DeltaAngle(candidate, previous.optimalCameraYaw)) / 90f * 6f;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestYaw = candidate;
                }
            }
            return bestYaw;
        }
    }

    [Header("References")]
    [Tooltip("Scales the whole path AND the runner together, as a multiple of the size they are authored at: cubes, spacing, first cube and runner. 1 = as authored (leaves a scene exactly as it was). Applied when the scene starts.")]
    [Range(0.5f, 4f)] public float sizeMultiplier = 1f;
    public Transform firstCube;
    public GameObject cubePrefab;
    public CubeRailRunner player;

    [Header("Zones")]
    [Tooltip("Fewest segments in a zone. A zone is one held camera shot, so this is also how often the view moves.")]
    public int minSegmentsPerZone = 2;
    [Tooltip("Most segments in a zone. This is the main lever on how far the camera has to zoom out to fit the shot - a wider zone means a smaller player on screen and more world to cross out of frame during a transition, so keep this low enough that the player stays clearly visible.")]
    public int maxSegmentsPerZone = 3;
    [Tooltip("Aim for each zone - each camera shot - to last about this many seconds at the runner's speed, by giving it more legs when they are crossed quickly. 0 = just use the segment counts above.")]
    [Range(0f, 12f)] public float targetZoneSeconds = 0f;
    [Tooltip("Most legs a zone may be stretched to by targetZoneSeconds. A bigger zone means a wider shot, so this caps how far the camera has to zoom out.")]
    [Range(2, 10)] public int maxSegmentsPerZoneCap = 6;
    [Tooltip("Sealed zones kept queued ahead of the runner. The camera needs the current one AND the next one solved, so never below 2.")]
    public int minZonesAhead = 2;
    [Tooltip("Segments always built ahead of the runner, so new track appears far outside the shot rather than in front of the viewer. Also how far ahead the camera plans its angles.")]
    [Range(4, 40)] public int minSegmentsAhead = 15;
    [Tooltip("Zones kept behind the runner before their cubes are destroyed. Must be at least 1 so the zone being transitioned away from still exists.")]
    public int zonesToKeepBehind = 1;

    [Header("Path Shape")]
    public float cubeSize = 1f;
    [Tooltip("Odds of the four turns a new leg can take, read from the heading being left behind. The four are compared against each other, so 4:4:1:1 is the same shape as 8:8:2:2. A reroll writes its draw straight into these four while the game runs, so this row is always the shape in force - and what you leave here is what the path opens with.")]
    [Range(1, 6)] public int leftWeight = 4;
    [Tooltip("Relative chance of turning right.")]
    [Range(1, 6)] public int rightWeight = 4;
    [Tooltip("Relative chance of a leg going up.")]
    [Range(0, 6)] public int upWeight = 1;
    [Tooltip("Relative chance of a leg going down.")]
    [Range(0, 6)] public int downWeight = 1;
    [Tooltip("Empty cells kept between a new leg and any older part of the track. Legs that would come closer are rejected, so separate stretches of the path never touch.")]
    [Range(1, 6)] public int pathClearance = 3;
    [Tooltip("Seconds a turn needing a HALF rotation costs: two side changes, each gated by CubeRailRunner's switch cooldown, plus reaction time.")]
    public float halfTurnSeconds = 0.80f;

    [Header("Turn Bias")]
    [Tooltip("Reroll the four odds above every few points of score, so the path keeps changing character: a stretch that climbs, then one that keeps peeling left, then a flat run. The opening stretch always uses the authored odds; only later blocks are rolled.")]
    public bool randomizeTurnBias = true;
    [Tooltip("Points of score one set of odds lasts. Score counts one per path leg reached, so this is also how many legs go by between rerolls.")]
    [Range(2, 200)] public int scorePerBiasChange = 20;
    [Tooltip("Chance each direction is drawn as the stand-out one. Only one direction can hold it in any one roll, so a stretch either favours a single way to turn or runs even.")]
    [Range(0f, 1f)] public float standoutChance = 0.34f;
    [Tooltip("Weight the stand-out direction gets. At 4 against 1s and 2s it takes roughly half the legs of that stretch.")]
    [Range(2, 6)] public int standoutWeight = 4;
    [Tooltip("Lowest weight an ordinary direction can draw.")]
    [Range(0, 4)] public int minRolledWeight = 1;
    [Tooltip("Highest weight an ordinary direction can draw. Keep it under the stand-out weight, or nothing stands out.")]
    [Range(1, 4)] public int maxRolledWeight = 2;
    [Tooltip("Print each new set of odds to the console as left:right:up:down.")]
    public bool logTurnBiasChanges = false;

    [System.Serializable]
    public struct LegTier
    {
        [Tooltip("First leg (score) this tier applies to. A tier holds until the next one starts.")]
        public int fromLeg;
        public int minCubes;
        public int maxCubes;
        public LegTier(int fromLeg, int minCubes, int maxCubes) { this.fromLeg = fromLeg; this.minCubes = minCubes; this.maxCubes = maxCubes; }
    }

    [Tooltip("Every tier's Min/Max cubes are multiplied by this. 1 = unchanged (every scene but prop camera 4).")]
    [Range(1f, 6f)] public float legLengthMultiplier = 1f;
    [Header("Leg Length Tiers (by leg count)")]
    [Tooltip("Each leg is a random whole number of cubes between the tier's Min and Max, the tier being picked by the leg's index (= the score shown when the runner reaches it). Keep them sorted by From Leg; the last one runs forever.")]
    [SerializeField] private LegTier[] legTiers =
    {
        new LegTier(0, 14, 18),
        new LegTier(16, 16, 22),
        new LegTier(36, 19, 28),
        new LegTier(66, 22, 34),
    };
    [Tooltip("The opening leg of a run is never shorter than this many cubes.")]
    [SerializeField] private int firstLegMinCubes = 20;
    [Tooltip("No double-murder: a previous leg no longer than this fraction of the way up the CURRENT tier's range counts as a short (panic) leg.")]
    [Range(0f, 0.5f)] [SerializeField] private float shortLegFraction = 0.25f;
    [Tooltip("No double-murder: the leg that follows a short one is rolled only from the top of the range, from this fraction upward (0.5 = the upper half).")]
    [Range(0.25f, 1f)] [SerializeField] private float releaseFloorFraction = 0.5f;
    [Tooltip("Tier index (0-based) from which the no-double-murder rule applies. 0 = every tier.")]
    [SerializeField] private int noDoubleMurderFromTier = 0;
    [Tooltip("Print each leg's tier, length and speed to the console.")]
    public bool logDifficultyChanges = false;

    [Header("Guide Trail")]
    [Tooltip("A thin line of light along the track just ahead of the runner, so the next move is always obvious. Brightest at the next cube, fading further along, and erased as the runner moves onto it.")]
    public bool showGuideTrail = true;
    [Tooltip("Colour at the bright end. Alpha sets how strong the line is.")]
    [ColorUsage(true, true)] public Color guideTrailColor = new Color(0.45f, 1f, 0.95f, 0.9f);
    [Tooltip("Cubes of track ahead of the runner the trail covers before it has faded out.")]
    [Range(2f, 60f)] public float guideTrailLengthCubes = 24f;
    [Tooltip("Thickness of the line in world units at its bright end; it thins toward the far end.")]
    [Range(0.02f, 0.6f)] public float guideTrailWidth = 0.2f;
    [Tooltip("How far ahead of the runner, in cubes, the trail begins, so it does not draw over the runner.")]
    [Range(0f, 3f)] public float guideTrailStartAhead = 0.6f;
    [Tooltip("Optional material for the line. Empty = an unlit Sprites/Default material made at runtime.")]
    public Material guideTrailMaterial;
    [Tooltip("How far, in cubes, the line is lifted toward the camera off the beam's centre. In this isometric view the surface of a cube in front of its centre is about 0.87 cubes away, so anything below that leaves the line buried inside its own beam.")]
    [Range(0.9f, 3f)] public float guideTrailLift = 1.3f;

    [Header("Route Shading")]
    [Tooltip("Colours each cube by how far ahead of the runner it is (light and warm next, cooler and darker further) and fades and shrinks far-ahead cubes so they grow in as the runner closes. Adds the PathDepthShading component to this object at start if it is missing; its own settings then appear here.")]
    public bool routeShading = true;
    [Tooltip("Shows a Tune button in the Game view that opens sliders for the trail, cube size, transparency, brightness and colour, live while playing. Turn off for a build you ship.")]
    public bool showTuningPanel = true;

    // ---- runtime ----
    private readonly List<PathZone> zones = new List<PathZone>();
    private readonly List<Segment> allSegments = new List<Segment>();

    private Vector3 lastPosition;
    private Vector3 lastDirection;
    private Vector3 lastHorizontalDirection;
    private Vector3 dirBeforeLast;
    private bool firstSegmentPlaced;
    private bool initialized;
    private int currentPlayerSegmentIdx;

    // Every cube cell ever placed -> serial of the segment that owns it. Kept after culling so
    // a later leg cannot loop back through ground the runner already covered.
    private readonly Dictionary<Vector3Int, int> occupiedCells = new Dictionary<Vector3Int, int>();
    private int segmentSerial;

    // Which block of scorePerBiasChange legs the odds now in the four fields were dealt for
    // (-1 = not dealt yet), and the odds as they were authored in the inspector - kept aside
    // because a reroll overwrites the fields themselves, which is what makes it visible.
    private int turnBiasBlock = -1;
    private int authoredLeft, authoredRight, authoredUp, authoredDown;
    private bool authoredCaptured;

    // The score, counted exactly as the HUD shows it: one per path leg the runner reaches,
    // with the leg they start on not counting. The score the next speed step is owed at, and
    // how many times the speed cap has been raised so far.
    private Segment lastScoredSegment;
    // Highest leg serial reached this run. A leg scores only if it is beyond this, so riding back onto an
    // earlier leg (two touching legs the probe swaps between) can never score again.
    private int highestScoredSerial = int.MinValue;
    // Length in cubes of the leg laid last (0 = none yet): the no-double-murder rule reads it.
    private int lastLegCubes;
    public int Score { get; private set; }
    public int DifficultyTier => TierIndexForLeg(Score);

    // The lengths and the speed cap as they were authored, so a fresh run starts over from
    // them rather than from whatever the last run grew them to.

    private PathGuideTrail guideTrail;
    private PathDepthShading shadingRef;
    private readonly List<Vector3> guideRoute = new List<Vector3>();

    public int pathGenerationCount = 0;

    // ---- public queries ----

    public IReadOnlyList<PathZone> Zones => zones;
    public int SegmentCount => allSegments.Count;
    public int PlayerSegmentIndex => currentPlayerSegmentIdx;

    /// <summary>The leg the runner is on. A reference, so it stays valid when culling shifts indices.</summary>
    public Segment PlayerSegment =>
        (currentPlayerSegmentIdx >= 0 && currentPlayerSegmentIdx < allSegments.Count)
            ? allSegments[currentPlayerSegmentIdx]
            : null;

    public Vector3 PlayerSegmentDirection =>
        (currentPlayerSegmentIdx >= 0 && currentPlayerSegmentIdx < allSegments.Count)
            ? allSegments[currentPlayerSegmentIdx].direction
            : Vector3.zero;

    /// <summary>Heading of the newest generated leg, so a caller can seed a fallback yaw.</summary>
    public Vector3 LatestHeading => lastHorizontalDirection;

    public Vector2Int CurrentSegmentLengthRange => GetSegmentLengthRange(segmentSerial);

    /// <summary>The zone the runner is inside right now, or null before the first segment.</summary>
    public PathZone PlayerZone
    {
        get
        {
            if (currentPlayerSegmentIdx < 0 || currentPlayerSegmentIdx >= allSegments.Count) return null;
            return allSegments[currentPlayerSegmentIdx].zone;
        }
    }

    /// <summary>The sealed zone after this one, or null if it is not generated yet.</summary>
    public PathZone GetZoneAfter(PathZone zone)
    {
        if (zone == null) return null;
        int i = zones.IndexOf(zone);
        if (i < 0) return null;

        for (int n = i + 1; n < zones.Count; n++)
            if (zones[n].isSealed && zones[n].segments.Count > 0) return zones[n];

        return null;
    }

    /// <summary>
    /// True once the runner is on the LAST segment of the zone - the camera's cue to start
    /// travelling to the next shot, so the move lands before they arrive.
    /// </summary>
    public bool IsPlayerInFinalSegment(PathZone zone) => IsPlayerNearZoneEnd(zone, 0);

    /// <summary>
    /// True once the runner is within leadSegments of the end of the zone - the camera's cue to
    /// start travelling, so the next shot is locked before they reach it.
    ///
    /// The lead matters at speed. A capped leg is crossed in about 0.8s, which is all the
    /// runway a zero-lead cue gives the move; one segment of lead roughly doubles it, and the
    /// difference is whether the runner arrives into a settled shot or into one still easing.
    /// </summary>
    public bool IsPlayerNearZoneEnd(PathZone zone, int leadSegments)
    {
        if (zone == null || zone.segments.Count == 0) return false;
        if (currentPlayerSegmentIdx < 0 || currentPlayerSegmentIdx >= allSegments.Count) return false;

        int finalIdx = allSegments.IndexOf(zone.FinalSegment);
        if (finalIdx < 0) return false;

        // Never cue before the runner is actually inside this zone.
        int firstIdx = allSegments.IndexOf(zone.segments[0]);
        if (firstIdx < 0 || currentPlayerSegmentIdx < firstIdx) return false;

        return currentPlayerSegmentIdx >= finalIdx - Mathf.Max(0, leadSegments);
    }

    public int IndexOfZone(PathZone zone) => zone == null ? -1 : zones.IndexOf(zone);

    // Applies sizeMultiplier to the spacing, the first cube (which every path cube is copied
    // from) and the runner. Done in Awake so it lands before Start builds the path.
    void Awake()
    {
        DifficultyProfile.Active = difficultyProfile;
        if (Mathf.Approximately(sizeMultiplier, 1f)) return;

        if (firstCube == null)
        {
            GameObject go = GameObject.Find("firstCube");
            if (go == null) go = GameObject.Find("FirstCube");
            if (go != null) firstCube = go.transform;
        }
        if (player == null) player = FindFirstObjectByType<CubeRailRunner>();

        cubeSize *= sizeMultiplier;
        if (firstCube != null) firstCube.localScale *= sizeMultiplier;
        if (player != null)
        {
            player.transform.localScale *= sizeMultiplier;

            // Speed follows size, so a leg takes as long to cross as it did at 1x.
            player.speedScale *= sizeMultiplier;
            player.SetLegSpeed(0);
        }

        // Coins grow with the cubes they sit on, and so does the reach that collects them.
        coinSize *= sizeMultiplier;
        coinPickupRadius *= sizeMultiplier;
    }

    void Start()
    {
        CaptureAuthoredBias();

        if (player == null) player = FindFirstObjectByType<CubeRailRunner>();

        if (firstCube == null)
        {
            GameObject go = GameObject.Find("firstCube");
            if (go == null) go = GameObject.Find("FirstCube");
            if (go != null) firstCube = go.transform;
        }

        if (cubePrefab == null && firstCube != null) cubePrefab = firstCube.gameObject;

        if (routeShading && GetComponent<PathDepthShading>() == null)
            gameObject.AddComponent<PathDepthShading>().pathGen = this;

        // The Tune button is removed from every scene; drop one if a scene has it baked in.
        var oldTuner = GetComponent<PathVisualTuner>();
        if (oldTuner != null) Destroy(oldTuner);

        CaptureAuthoredBias();
        EnsureBoostFX();
        ApplyProfileOverrides(); // DifficultyProfile: camera caps, feedback FX, telemetry (no-op without a profile)

        CleanupExistingScenePath();
        GenerateNewPath();
        initialized = true;
    }

    /// <summary>
    /// Hides the hand-authored demo track so only the generated path shows.
    ///
    /// The rule used to also match a GameObject literally named "parent" - which is the scene
    /// root firstCube lives under. Deactivating it took firstCube's collider down too, so the
    /// runner's first grounding probe found nothing, latched isFlying, and glided the whole
    /// opening leg. Ancestors are protected explicitly.
    /// </summary>
    void CleanupExistingScenePath()
    {
        if (firstCube == null) return;

        var keepAlive = new HashSet<Transform>();
        for (Transform t = firstCube; t != null; t = t.parent) keepAlive.Add(t);

        if (firstCube.parent != null)
            foreach (Transform sibling in firstCube.parent)
                if (!keepAlive.Contains(sibling)) sibling.gameObject.SetActive(false);

        var all = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (var go in all)
        {
            if (keepAlive.Contains(go.transform)) continue;

            string n = go.name.ToLower();
            if (n.StartsWith("left_") || n.StartsWith("upb_") || n.Contains("lastcube"))
                go.SetActive(false);
        }
    }

    public void GenerateNewPath()
    {
        if (firstCube == null) return;

        foreach (PathZone z in zones)
            foreach (Segment s in z.segments)
            {
                foreach (GameObject cube in s.cubes)
                    if (cube != null && cube.transform != firstCube) Destroy(cube);
                DestroyDecoy(s);
                DestroyBridgeArches(s);
            }

        zones.Clear();
        allSegments.Clear();
        occupiedCells.Clear();
        segmentSerial = 0;
        currentPlayerSegmentIdx = 0;
        turnBiasBlock = -1;

        CaptureAuthoredBias();
        Score = 0;
        BonusPoints = 0;
        ResetCloseCallStreak();
        lastLegCubes = 0;
        lastScoredSegment = null;
        highestScoredSerial = int.MinValue;
        if (player != null && !tutorialActive) player.SetLegSpeed(0); // TUTORIAL ADDITION: the tutorial owns the speed
        ResetCoins();
        ResetGems();
        ResetCornerProps();
        ResetPower();
        ResetPowerUps();
        ResetBoost();
        ResetObstacleLeg();
        ResetSlowMo();
        ResetPortal();
        ResetWaves();
        Reset311();            // scene 3.11 level-plan state (fields only; inert elsewhere)
        ResetTutorialLayout(); // TUTORIAL ADDITION

        lastPosition = firstCube.position;
        lastDirection = firstCube.forward;
        lastHorizontalDirection = Mathf.Abs(lastDirection.y) < 0.9f ? lastDirection : Vector3.zero;
        dirBeforeLast = Vector3.zero;
        firstSegmentPlaced = false;

        // CHECKPOINT START (DynamicPathGenerator.Checkpoint.cs): false unless checkpointStartPortal is set (3.6 only).
        if (!TryLayCheckpointStart()) EnsureZonesAhead();
        ResetPlayer();
    }

    void Update()
    {
        if (!initialized || player == null || allSegments.Count == 0) return;

        int newIdx = FindPlayerSegment();
        if (newIdx != currentPlayerSegmentIdx)
        {
            currentPlayerSegmentIdx = newIdx;
            CullZonesBehind();
        }

        // Counted off the leg ITSELF, not its index: culling renumbers the list under us.
        Segment onSegment = PlayerSegment;
        if (onSegment != null && onSegment != lastScoredSegment && !CheckpointAwaitingCross)
        {
            if (lastScoredSegment != null && onSegment.serial > highestScoredSerial)
            {
                Score++;
                Haptics.Selection();
                OnScoreAdvanced();
            }
            lastScoredSegment = onSegment;
            if (onSegment.serial > highestScoredSerial) highestScoredSerial = onSegment.serial;
        }

        EnsureZonesAhead();
    }

    // LateUpdate so the trail starts from where the runner is drawn this frame.
    void LateUpdate()
    {
        if (!initialized) return;

        UpdateCoins();
        UpdateGems();
        UpdateCornerProps();
        UpdatePower();
        UpdatePowerUps();

        if (!showGuideTrail || player == null)
        {
            guideTrail?.Hide();
            return;
        }

        guideTrail ??= new PathGuideTrail(transform, guideTrailMaterial);

        if (!CollectRouteAhead(guideRoute, out float across))
        {
            guideTrail.Hide();
            return;
        }

        Camera cam = Camera.main;
        Vector3 towardCamera = cam != null ? -cam.transform.forward : Vector3.up;

        // The trail never reaches past the track that is drawn (see PathDepthShading's visible
        // window), so it cannot point into empty space.
        float length = guideTrailLengthCubes;
        if (shadingRef == null) shadingRef = GetComponent<PathDepthShading>();
        if (shadingRef != null && shadingRef.enabled)
            length = Mathf.Min(length, shadingRef.VisibleAheadCubes - guideTrailStartAhead);

        if (length <= 0.2f)
        {
            guideTrail.Hide();
            return;
        }

        // Keep the line's thickness on SCREEN steady as the zoom changes: it is set for an
        // orthographic size of 30, and would shrink to a thread when the camera pulls back.
        float widthScale = cam != null ? Mathf.Clamp(cam.orthographicSize / 30f, 0.6f, 3f) : 1f;

        guideTrail.Draw(
            guideRoute,
            across + guideTrailStartAhead * cubeSize,
            length * cubeSize,
            guideTrailWidth * widthScale,
            guideTrailColor,
            towardCamera * (cubeSize * guideTrailLift));
    }

    void OnDestroy()
    {
        guideTrail?.Dispose();
        DestroyCoinRuntime();
        DestroyGemRuntime();
        DestroyCornerPropRuntime();
        DestroyPowerRuntime();
        DestroyShieldRuntime();
        DestroyBridgeRuntime();
        DestroyBridgeArchRuntime();
        DestroyPortalTilesRuntime();
    }

    // ------------------------------------------------------------------
    // Guide trail
    // ------------------------------------------------------------------

    /// <summary>
    /// Cube centres from the one under the runner onward, far enough to cover the guide trail.
    /// across is how far the runner is past the first of them, along the route.
    /// </summary>
    bool CollectRouteAhead(List<Vector3> into, out float across)
    {
        into.Clear();
        across = 0f;

        // Not hidden while the runner is momentarily "flying": that flag flickers on at corners,
        // and the trail winking out there is exactly when it is needed.
        Transform cube = player.currentCube;
        if (cube == null) return false;

        int s = 0, c = 0;
        if (cube == firstCube)
        {
            into.Add(firstCube.position);
        }
        else if (FindCube(cube, out s, out c))
        {
            into.Add(cube.position);
            c++;
        }
        else
        {
            return false;
        }

        // player.currentCube is only updated when the runner turns a corner, so it is the FIRST
        // cube of the leg they are on, not the cube under them. The route is therefore collected
        // from there and has to reach past the whole leg as well as the trail's own length.
        int legCubes = allSegments.Count > s ? allSegments[s].cubes.Count : 0;
        float budget = (guideTrailStartAhead + guideTrailLengthCubes + 2f + legCubes) * cubeSize;
        float walked = 0f;

        for (; s < allSegments.Count && walked < budget; s++, c = 0)
        {
            List<GameObject> cubes = allSegments[s].cubes;
            for (; c < cubes.Count && walked < budget; c++)
            {
                if (cubes[c] == null) continue;
                Vector3 p = cubes[c].transform.position;
                walked += Vector3.Distance(into[into.Count - 1], p);
                into.Add(p);
            }
        }

        // How far along the route line the runner is: project them onto the route and take the
        // nearest point. (The route is kept well apart from itself, so the nearest piece is
        // always the one they are on.)
        // Uses the shading's smoothed route point when there is one, so the trail's start glides
        // through corners instead of stepping with the raw projection.
        if (shadingRef == null) shadingRef = GetComponent<PathDepthShading>();
        Vector3 runnerPos = (shadingRef != null && shadingRef.enabled && shadingRef.HasRoutePoint)
            ? shadingRef.RoutePoint
            : player.transform.position;
        float bestD = float.MaxValue;
        float walkedTo = 0f;
        for (int i = 0; i + 1 < into.Count; i++)
        {
            Vector3 a = into[i];
            Vector3 ab = into[i + 1] - a;
            float len = ab.magnitude;
            float t = len > 0.0001f ? Mathf.Clamp01(Vector3.Dot(runnerPos - a, ab) / (len * len)) : 0f;
            float d = (a + ab * t - runnerPos).sqrMagnitude;
            if (d < bestD) { bestD = d; across = walkedTo + t * len; }
            walkedTo += len;
        }
        return into.Count >= 2;
    }

    // Where a cube sits in allSegments. Tries the runner's segment and the next one first -
    // the runner can reach a new segment in FixedUpdate after Update has looked.
    bool FindCube(Transform cube, out int segment, out int index)
    {
        int from = Mathf.Clamp(currentPlayerSegmentIdx, 0, Mathf.Max(0, allSegments.Count - 1));
        for (int pass = 0; pass < 2; pass++)
        {
            int start = pass == 0 ? from : 0;
            int end = pass == 0 ? Mathf.Min(from + 2, allSegments.Count) : allSegments.Count;
            for (int s = start; s < end; s++)
            {
                List<GameObject> cubes = allSegments[s].cubes;
                for (int i = 0; i < cubes.Count; i++)
                {
                    if (cubes[i] != null && cubes[i].transform == cube)
                    {
                        segment = s;
                        index = i;
                        return true;
                    }
                }
            }
        }

        segment = index = -1;
        return false;
    }

    // ------------------------------------------------------------------
    // Zone assembly
    // ------------------------------------------------------------------

    /// <summary>
    /// Extends the path until enough SEALED zones sit in front of the runner. The camera needs
    /// the zone it is holding and the one it will travel to, both fully bounded, so this queue
    /// running dry would strand a transition with no destination.
    /// </summary>
    void EnsureZonesAhead()
    {
        // A zone door ends the track: nothing is laid past it until the runner has gone through.
        if (portalHold) return;

        int guard = 0;
        while ((SealedZonesAhead() < Mathf.Max(2, minZonesAhead) || SegmentsAhead() < minSegmentsAhead) &&
               !portalHold && guard++ < 256)
            AppendSegment();
    }

    int SegmentsAhead() => allSegments.Count - 1 - currentPlayerSegmentIdx;

    int SealedZonesAhead()
    {
        PathZone playerZone = PlayerZone;
        int from = playerZone != null ? zones.IndexOf(playerZone) : -1;

        int count = 0;
        for (int i = from + 1; i < zones.Count; i++)
            if (zones[i].isSealed) count++;

        // Before the runner is placed, the opening zone has to be sealed too.
        if (playerZone != null && !playerZone.isSealed) count = 0;

        return count;
    }

    void AppendSegment()
    {
        // segmentSerial is also the score the runner will be showing when they reach this leg,
        // so rerolling the odds by it lines the changes up with the number in the corner.
        UpdateTurnBias(segmentSerial);

        float arrivalSpeed = ArrivalSpeed();

        Vector3 currentDir = lastDirection;
        Vector3 currentPos = lastPosition;

        int segLen = RollSegmentLength(segmentSerial);
        if (PowerHandoverLeg(segmentSerial)) segLen = Mathf.Max(segLen, powerHandoverMinCubes);
        int boostLeg = -1;
        bool slowMoLeg = false;
        int portalRole = 0;
        bool decoyEligible = false;
        Vector3 decoyPrevDir = Vector3.zero;
        bool bridgeActive = false;
        bridgePlanks.Clear();
        bool tutorialLeg = TutorialBeginLeg(ref segLen); // TUTORIAL ADDITION: scripted leg length (no-op outside the tutorial)
        Segment legBefore = allSegments.Count > 0 ? allSegments[allSegments.Count - 1] : null;

        if (!firstSegmentPlaced)
        {
            // The opening leg inherits firstCube's heading: there is nothing to turn away from.
            firstSegmentPlaced = true;
            if (portalExitPending)
            {
                // The runway out of a zone door: a long straight run, lit like a boost.
                portalRole = 2;
                segLen = ExitRunwayCubes();   // = PortalRunwayCubesTB(portalRunwayCubes) unless a LookTransition lengthens it
                boostLeg = 0;
                boostSerialCounter++;
            }
            else
            {
                if (!tutorialLeg) segLen = FirstLegCubesTB(segLen);
                occupiedCells[CellOf(firstCube.position)] = segmentSerial;
            }
        }
        else
        {
            Vector3 prevDir = currentDir;
            if (tutorialLeg)
            {
                // TUTORIAL ADDITION: a scripted turn instead of a random one
                currentDir = TutorialLegDirection(prevDir);
            }
            else if (!PowerQuiet && TakePortalLeg(prevDir, ref segLen, out Vector3 portalDir))
            {
                // The runway into a zone door, lit like a boost so it reads as an event.
                currentDir = portalDir;
                portalRole = 1;
                boostLeg = 0;
                boostSerialCounter++;
            }
            else if (!PowerQuiet && TakeObstacleLeg(prevDir, ref segLen, out Vector3 obstacleLegDir))
            {
                // An obstacle leg (3.9): one long runway with pillars, drawn like any ordinary leg - no boost light or FX (DynamicPathGeneratorObstacleLeg.cs).
                currentDir = obstacleLegDir;
                portalRole = 3;
            }
            else if (!PowerQuiet && TakeSlowMoLeg(prevDir, out Vector3 slowDir, out int slowLen))
            {
                // A slow-motion leg (3.11): really short, taken while time runs slow (DynamicPathGeneratorSlowMo.cs).
                currentDir = slowDir;
                segLen = slowLen;
                slowMoLeg = true;
                OnSlowMoLegTaken();
            }
            else if (!PowerQuiet && TakeBoostLeg(prevDir, arrivalSpeed, out Vector3 boostDir, out int boostLen, out boostLeg))
            {
                currentDir = boostDir;
                segLen = boostLen;
            }
            else
            {
                if (WaveGlideDue(segmentSerial)) segLen = Mathf.Max(segLen, GlideCubes(segmentSerial));
                Lengthen311(ref segLen);   // 3.11: a leg planned to end in a half turn is long enough for one
                PlanGateLeg(ref segLen);   // no-op unless gateSections (MainScene prop camera 3.6)
                bool family = ApplyFamilyWeights(out Vector4 savedWeights);
                currentDir = ChooseDirection(prevDir, dirBeforeLast, TB || HalfTurnAffordable(arrivalSpeed), ref segLen, TB ? legBefore : null);
                currentPos = lastPosition;   // TryExtendForTwoTaps may have lengthened the leg before
                if (family) RestoreWeights(savedWeights);
            }
            dirBeforeLast = prevDir;
            decoyEligible = !tutorialLeg && boostLeg < 0 && portalRole == 0 && !PowerQuiet && !slowMoLeg;
            decoyPrevDir = prevDir;

            if (decoyEligible) bridgeActive = TryPlanBridge(arrivalSpeed, ref segLen, prevDir, currentDir);

            // DIAGONAL RISER. One cube along the OLD heading and one along the NEW puts the
            // corner cube squarely under CubeRailRunner's forward probe, so the turn is taken -
            // and it reads on screen as a Monument Valley step. Placing it at last + newDir
            // instead leaves it invisible to the probe and the runner never turns at all.
            currentPos += prevDir * cubeSize;
        }

        var seg = new Segment
        {
            direction = currentDir,
            startPosition = currentPos + currentDir * cubeSize,
            boost = boostLeg >= 0,
            boostId = boostLeg >= 0 ? boostSerialCounter : 0,
            boostLeg = boostLeg,
            portalRole = portalRole,
            serial = segmentSerial,
            slowMo = slowMoLeg,
            slowMoScale = slowMoLeg ? slowMoPlanScale : 1f,
            slowMoSpeed = slowMoLeg ? slowMoPlanSpeed : 1f,
            slowMoExit = slowMoLeg && slowMoLaidExit,
            slowMoLeg = slowMoLeg ? slowMoLaidLeg : -1
        };

        // The length the leg really got (ChooseDirection can shorten it to fit), for the next roll.
        lastLegCubes = segLen;
        if (logDifficultyChanges)
            Debug.Log($"[DynamicPathGenerator] leg {segmentSerial}: tier {TierIndexForLeg(segmentSerial)}, {segLen} cubes, " +
                      $"{arrivalSpeed:0.0} u/s, {segLen * cubeSize / Mathf.Max(0.01f, arrivalSpeed):0.00}s");

        for (int i = 0; i < segLen; i++)
        {
            currentPos += currentDir * cubeSize;
            GameObject newCube = Instantiate(cubePrefab, currentPos, GetRotationForDir(currentDir));
            newCube.name = $"PathCube_S{allSegments.Count}_C{i}";
            newCube.transform.SetParent(transform);
            newCube.SetActive(true);
            seg.cubes.Add(newCube);
            occupiedCells[CellOf(currentPos)] = segmentSerial;
            if (bridgeActive && BridgePlankSideAt(i, out int plankSide)) MakeBridgePlank(newCube, plankSide);
        }

        if (bridgeActive) BuildBridgeArches(seg);

        // DifficultyProfile bookkeeping (serial, landing face, speed, wave phase, planks). Before the decoy,
        // which reads it, and before the leg joins the list. No-op without a profile.
        if (portalRole == 2 && difficultyProfile != null) BeginCycle(segmentSerial);
        if (difficultyProfile != null) OnSegmentLaidTB(seg, bridgeActive);
        if (bridgeActive) OnBridgeLaid(bridgePlanks.Count, bridgeTeaching);

        if (decoyEligible) TryPlaceDecoy(seg, decoyPrevDir, currentDir);

        segmentSerial++;
        seg.endPosition = currentPos;

        allSegments.Add(seg);
        if (tutorialLeg) TutorialEndLeg(seg); // TUTORIAL ADDITION
        if (portalRole == 3) SealZoneBeforeObstacleLeg();   // the obstacle leg gets a shot of its own
        if (slowMoLeg && slowMoLaidLeg == 0) SealZoneBeforeObstacleLeg();   // a slow-motion section too: all its legs in one shot
        AddToOpenZone(seg);
        if (slowMoLeg && slowMoLaidLeg == 0 && seg.zone != null) seg.zone.capacity = slowMoSectionLegs;
        // A door's runway out keeps a shot of its own when the doors are framed separately (DynamicPathGeneratorPortal.cs).
        if (!(portalRole == 2 && portalIslandExit && portalSeparateDoorShots) && portalRole != 3) ExtendPreviousZoneOverHandover(seg);

        // The leg behind this corner now knows which way the path turns next.
        if (allSegments.Count >= 2)
        {
            Segment behind = allSegments[allSegments.Count - 2];
            PreFinalizeLeg(behind, currentDir);
            SpawnCoinsBehindTurn(behind, currentDir);
            PlaceCornerGem(behind, currentDir);
            PlaceCornerProp(behind, currentDir);   // no-op unless spawnCornerProps (MainScene prop camera 3.7)
            PlacePowerUps(behind, seg);
            TryPlaceGateBehindTurn(behind, seg, currentDir);   // no-op unless gateSections (MainScene prop camera 3.6)
            TryPlaceObstacleBehindTurn(behind, currentDir);
            FinalizeLeg(behind);
        }

        lastPosition = currentPos;
        lastDirection = currentDir;
        pathGenerationCount++;

        if (portalRole == 1) OnPortalEntryLaid(seg);
        else if (portalRole == 2) OnPortalExitLaid(seg);
        else if (portalRole == 3) OnObstacleLegLaid(seg);
    }

    void AddToOpenZone(Segment seg)
    {
        PathZone zone = OpenZone();

        zone.segments.Add(seg);
        seg.zone = zone;
        if (zone.segments.Count == 1) MarkZoneYawPermission(zone, seg); // wave pacing: may the camera turn here?

        foreach (GameObject cube in seg.cubes)
            if (cube != null) zone.Encapsulate(cube.transform.position);

        // The diagonal riser leaves a gap with no cube in it; pulling the leg's start point in
        // keeps the shot from cropping the step itself.
        zone.Encapsulate(seg.startPosition);

        if (zone.segments.Count >= zone.capacity) SealZone(zone);
    }

    PathZone OpenZone()
    {
        if (zones.Count > 0 && !zones[zones.Count - 1].isSealed)
            return zones[zones.Count - 1];

        // Size the zone by TIME, not just segment count. At speed a leg is crossed in under a
        // second, so a fixed 2-3 legs meant the camera moved every couple of seconds. This grows
        // the zone until it lasts about targetZoneSeconds at the speed the runner will have.
        int baseCapacity = PathRange(minSegmentsPerZone, Mathf.Max(minSegmentsPerZone, maxSegmentsPerZone) + 1);
        float zoneSpeed = Mathf.Max(0.5f, ArrivalSpeed());
        Vector2Int legRange = GetSegmentLengthRange(segmentSerial);
        float legSeconds = (legRange.x + legRange.y) * 0.5f * cubeSize / zoneSpeed;
        int timedCapacity = Mathf.CeilToInt(targetZoneSeconds / Mathf.Max(0.1f, legSeconds));

        var zone = new PathZone
        {
            capacity = Mathf.Clamp(Mathf.Max(baseCapacity, timedCapacity), Mathf.Max(1, minSegmentsPerZone),
                                   Mathf.Max(maxSegmentsPerZone, maxSegmentsPerZoneCap))
        };

        // Overlap the hand-off by one segment.
        //
        // The camera starts travelling when the runner steps onto the PREVIOUS zone's final
        // segment, and the runner crosses that whole segment while the move is in flight. If
        // the incoming shot is framed only on its own cubes, it does not contain the ground the
        // runner is still standing on, and they slide out of frame for the length of the
        // transition. Seeding the new bounds with that final segment makes consecutive shots
        // share their junction, which is what a stage director frames anyway.
        if (zones.Count > 0)
        {
            zone.previous = zones[zones.Count - 1];
            Segment handover = zones[zones.Count - 1].FinalSegment;
            if (handover != null)
            {
                // After an obstacle leg only its last stretch: the chase camera covers the rest (DynamicPathGeneratorObstacleLeg.cs).
                int from = handover.portalRole == 3 ? handover.cubes.Count - ObstacleLegHandoverCubes(handover) : 0;
                for (int i = Mathf.Max(0, from); i < handover.cubes.Count; i++)
                    if (handover.cubes[i] != null) zone.Encapsulate(handover.cubes[i].transform.position);
            }
        }

        zones.Add(zone);
        return zone;
    }

    /// <summary>
    /// Makes the hand-off overlap symmetric: the outgoing zone is stretched to cover the first
    /// segment of the incoming one.
    ///
    /// Both shots then contain the whole stretch of track the runner covers during a
    /// transition. That matters because the camera does not cut between shots, it INTERPOLATES
    /// - and a lerp between two poses with different yaws is not itself a solved framing, so
    /// with only a one-sided overlap the runner can slide out of the intermediate frames even
    /// though both endpoints were correct. Overlapping both ways leaves the interpolation
    /// travelling between two shots that already agree about where the runner is, which is what
    /// stops the visibility failsafe having to grab the camera mid-move.
    /// </summary>
    void ExtendPreviousZoneOverHandover(Segment seg)
    {
        if (zones.Count < 2 || seg.zone == null) return;

        int zoneIdx = zones.IndexOf(seg.zone);
        if (zoneIdx < 1) return;

        // Only the FIRST segment of a zone reaches back into its predecessor.
        if (seg.zone.segments.Count != 1) return;

        PathZone previous = zones[zoneIdx - 1];

        // Never move a shot the camera has already committed to, even to improve it.
        if (previous.framingLocked) return;

        foreach (GameObject cube in seg.cubes)
            if (cube != null) previous.Encapsulate(cube.transform.position);

        previous.zoneBounds.Expand(cubeSize * 0.5f);

        // The shot was solved against the smaller box; make it solve again. Zones are built at
        // least minZonesAhead in front of the runner, so this normally lands long before the
        // camera has adopted the zone - and the lock above covers the case where it does not.
        previous.framingSolved = false;
    }

    void SealZone(PathZone zone)
    {
        // Encapsulate walked cube CENTRES, so the box is half a cube short on every face.
        zone.zoneBounds.Expand(cubeSize);
        zone.isSealed = true;
    }

    void CullZonesBehind()
    {
        PathZone playerZone = PlayerZone;
        if (playerZone == null) return;

        int keep = Mathf.Max(1, zonesToKeepBehind);

        while (zones.IndexOf(playerZone) > keep && zones.Count > 0)
        {
            PathZone dead = zones[0];

            foreach (Segment s in dead.segments)
            {
                foreach (GameObject cube in s.cubes)
                    if (cube != null && cube.transform != firstCube) Destroy(cube);
                DestroyDecoy(s);
                DestroyBridgeArches(s);
                ReleaseCoinsOn(s);
                ReleaseGemsOn(s);
                ReleaseCornerPropsOn(s);
                ReleasePowerOn(s);
                ReleaseShieldOn(s);
                allSegments.Remove(s);
            }

            zones.RemoveAt(0);
            if (zones.Count > 0) zones[0].previous = null; // don't keep culled zones alive
        }

        // Indices shifted under us; the runner's own cube is never culled, so this re-finds it.
        currentPlayerSegmentIdx = FindPlayerSegment();
    }

    // ------------------------------------------------------------------
    // Segment length / speed scaling
    // ------------------------------------------------------------------

    /// <summary>Index into legTiers of the tier that covers this leg (the last one whose fromLeg has been reached).</summary>
    int TierIndexForLeg(int leg)
    {
        if (legTiers == null || legTiers.Length == 0) return 0;
        int idx = 0;
        for (int i = 0; i < legTiers.Length; i++)
            if (leg >= legTiers[i].fromLeg) idx = i;
        return idx;
    }

    /// <summary>The (min, max) cubes a leg may be, from the tier its index falls in.</summary>
    Vector2Int GetSegmentLengthRange(int leg)
    {
        if (TB) return TimeBudgetRange(leg); // DifficultyProfile: lengths come from duration ranges
        if (legTiers == null || legTiers.Length == 0) return new Vector2Int(10, 14);
        LegTier t = legTiers[TierIndexForLeg(leg)];
        float mul = Mathf.Max(1f, legLengthMultiplier);
        int min = Mathf.Max(2, Mathf.RoundToInt(t.minCubes * mul));
        int max = Mathf.Max(min, Mathf.RoundToInt(t.maxCubes * mul));
        return new Vector2Int(min, max);
    }

    /// <summary>
    /// Rolls a leg length for the tier, with the no-double-murder rule: if the leg before was in the bottom
    /// shortLegFraction of this tier's range, this one comes only from the upper part (from releaseFloorFraction up).
    /// Panic (short) -> breathe (long) -> panic, never two short legs back to back at speed.
    /// </summary>
    int RollSegmentLength(int leg)
    {
        if (TB) return RollTimeBudgetLength(leg);
        Vector2Int range = GetSegmentLengthRange(leg);
        int lo = range.x, hi = range.y;
        int span = hi - lo;

        if (lastLegCubes > 0 && TierIndexForLeg(leg) >= noDoubleMurderFromTier)
        {
            int shortCeiling = lo + Mathf.FloorToInt(span * shortLegFraction);
            if (lastLegCubes <= shortCeiling)
                lo = Mathf.Min(hi, lo + Mathf.CeilToInt(span * releaseFloorFraction));
        }
        return PathRange(lo, hi + 1);
    }

    /// <summary>The speed the runner will have on the leg being generated - exact, it comes off the same curve.</summary>
    float ArrivalSpeed()
    {
        if (player == null) return 14f;
        return player.SpeedForLeg(segmentSerial);
    }

    // A leg has to be crossable in more than halfTurnSeconds before a two-input turn off it is
    // fair. Measured against the SHORTEST leg the generator could roll for this tier.
    bool HalfTurnAffordable(float arrivalSpeed)
    {
        float speed = Mathf.Max(0.01f, arrivalSpeed);
        float shortestLegSeconds = GetSegmentLengthRange(segmentSerial).x * cubeSize / speed;
        return shortestLegSeconds >= halfTurnSeconds;
    }

    // ------------------------------------------------------------------
    // Runner placement
    // ------------------------------------------------------------------

    void ResetPlayer()
    {
        if (player == null || firstCube == null) return;

        Rigidbody rb = player.GetComponent<Rigidbody>();
        if (rb == null) return;

        player.currentCube = firstCube;
        player.isFlying = false;
        player.currentSideIndex = 0;

        Vector3 targetUp = firstCube.up;
        float offset = (0.5f + 0.5f + 0.05f) * firstCube.lossyScale.x;

        Vector3 startPos = firstCube.position + targetUp * offset;
        Quaternion startRot = Quaternion.LookRotation(firstCube.forward, targetUp);

        // Both, in this order. Writing only rb.position leaves player.transform reading the
        // authored pose until the next physics sync, and CubeRailRunner's first FixedUpdate
        // grounding probe runs off transform.position - from the authored spot it fires away
        // from the rail, finds nothing, and latches isFlying on frame one.
        player.transform.SetPositionAndRotation(startPos, startRot);
        rb.position = startPos;
        rb.rotation = startRot;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    int FindPlayerSegment()
    {
        if (player == null || player.currentCube == null) return currentPlayerSegmentIdx;
        Transform pCube = player.currentCube;

        for (int s = 0; s < allSegments.Count; s++)
        {
            List<GameObject> cubes = allSegments[s].cubes;
            for (int c = 0; c < cubes.Count; c++)
                if (cubes[c] != null && cubes[c].transform == pCube) return s;
        }

        return Mathf.Clamp(currentPlayerSegmentIdx, 0, Mathf.Max(0, allSegments.Count - 1));
    }

    // ------------------------------------------------------------------
    // Direction choice
    // ------------------------------------------------------------------

    /// <summary>
    /// Picks the next leg. One continuous path - exactly one direction per segment, never a
    /// fork - and always a real 90 degree turn.
    /// </summary>
    Vector3Int CellOf(Vector3 p) => Vector3Int.RoundToInt(p / Mathf.Max(0.01f, cubeSize));

    /// <summary>
    /// True if a leg of legLen cubes leaving the corner in dir stays pathClearance cells away
    /// from every older cube. The leg just before it is exempt: a new leg is perpendicular to
    /// it and starts beside its end, so it is always close to that one and never returns to it.
    /// </summary>
    bool LegIsClear(Vector3 origin, Vector3 dir, int legLen)
    {
        int lastSerial = segmentSerial - 1;
        int c = clearanceOverride >= 0 ? clearanceOverride : Mathf.Max(1, pathClearance);

        for (int i = 0; i <= legLen; i++)
        {
            Vector3Int cell = CellOf(origin + dir * (i * cubeSize));
            for (int dx = -c; dx <= c; dx++)
                for (int dy = -c; dy <= c; dy++)
                    for (int dz = -c; dz <= c; dz++)
                        if (occupiedCells.TryGetValue(cell + new Vector3Int(dx, dy, dz), out int owner) && owner != lastSerial)
                            return false;
        }
        return true;
    }

    // Picks a turn whose leg keeps clear of the existing track, shortening the leg before
    // giving up. Only if nothing at all is clear does it fall back to an unchecked turn.
    // fitFor (DifficultyProfile only): the leg just laid. Turns its time floor and the wave caps rule out are
    // dropped first; if nothing clear is left, only the time floor is kept; only then is anything clear taken.
    // Dead-end rule. Normally a leg keeps pathClearance cells from older track. When no turn can (a spiral has
    // closed into a pocket - the next corner cell is already inside the clearance of an older leg), the leg climbs
    // or dives out at a smaller clearance, then any turn at a smaller clearance, and only as the very last resort
    // is laid unchecked. clearanceOverride < 0 = normal; climbOnly offers only up / down whatever the turn bias.
    private int clearanceOverride = -1;
    private bool climbOnly;

    Vector3 ChooseDirection(Vector3 prevDir, Vector3 twoBackDir, bool allowHalfTurn, ref int segLen, Segment fitFor = null)
    {
        if (TryChooseDirection(prevDir, twoBackDir, allowHalfTurn, ref segLen, fitFor, out Vector3 chosen)) return chosen;

        int full = Mathf.Max(1, pathClearance);
        try
        {
            // Pass 0: climb or dive only, from the full clearance down to touching-free. Pass 1: any turn, one notch tighter.
            for (int pass = 0; pass < 2; pass++)
                for (int c = pass == 0 ? full : full - 1; c >= 0; c--)
                {
                    clearanceOverride = c;
                    climbOnly = pass == 0;
                    if (TryChooseDirection(prevDir, twoBackDir, allowHalfTurn, ref segLen, fitFor, out chosen))
                    {
                        if (logDifficultyChanges)
                            Debug.Log($"[DynamicPathGenerator] leg {segmentSerial}: dead end at clearance {full}, took {(climbOnly ? "a climb/dive" : "a turn")} at clearance {c}");
                        return chosen;
                    }
                }
        }
        finally
        {
            clearanceOverride = -1;
            climbOnly = false;
        }

        Debug.LogWarning($"[DynamicPathGenerator] leg {segmentSerial}: every direction overlaps older track - laying it unchecked.");
        Vector3 origin = lastPosition + prevDir * cubeSize;
        return GetNextDirection(prevDir, twoBackDir, allowHalfTurn, origin, 0, out _);
    }

    // The old ChooseDirection minus its unchecked last line: false when no turn is clear at the current clearance.
    bool TryChooseDirection(Vector3 prevDir, Vector3 twoBackDir, bool allowHalfTurn, ref int segLen, Segment fitFor, out Vector3 chosen)
    {
        chosen = Vector3.zero;
        Prepare311TwoRoll(fitFor);   // 3.11: a leg that should end in a half turn is lengthened first if it can be
        Vector3 origin = lastPosition + prevDir * cubeSize;

        if (fitFor != null)
        {
            // DifficultyProfile: within the floors first (never shortening the new leg under its own floor),
            // then once more after lengthening the last leg for a 2-roll turn; only then the old fallback below.
            int minLen = ShortestAllowedNewLeg(segLen);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int fit = 2; fit >= 1; fit--)
                    for (int len = segLen; len >= minLen; len--)
                    {
                        Vector3 dir = GetNextDirection(prevDir, twoBackDir, allowHalfTurn, origin, len, out bool found, fitFor, fit == 1);
                        if (found) { segLen = len; chosen = dir; return true; }
                    }
                if (pass == 0 && !TryExtendForTwoTaps(fitFor)) break;
                origin = lastPosition + prevDir * cubeSize;
            }
        }

        for (int len = segLen; len >= 2; len--)
        {
            Vector3 dir = GetNextDirection(prevDir, twoBackDir, allowHalfTurn, origin, len, out bool found);
            if (found)
            {
                segLen = len;
                chosen = dir;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Deals the odds for the block of legs this one falls in. Block 0 - the opening
    /// scorePerBiasChange legs - keeps the odds authored in the inspector; every block after it
    /// is rolled, each direction taking its own weight between minRolledWeight and
    /// maxRolledWeight, so a stretch may climb, dive, peel to one side, or stay even.
    /// </summary>
    void CaptureAuthoredBias()
    {
        if (authoredCaptured) return;
        authoredLeft = leftWeight;
        authoredRight = rightWeight;
        authoredUp = upWeight;
        authoredDown = downWeight;
        authoredCaptured = true;
    }

    // ------------------------------------------------------------------
    // Difficulty ramp
    // ------------------------------------------------------------------

    /// <summary>
    /// One point of score has just been scored: the runner has reached the next leg, so its speed
    /// comes off the runner's speed curve for that leg. No timers, no random steps, no tiers to raise -
    /// the curve itself flattens against the human ceiling.
    /// </summary>
    void OnScoreAdvanced()
    {
        if (player == null) return;
        if (tutorialActive) return; // TUTORIAL ADDITION: the tutorial owns the speed
        player.SetLegSpeed(Score);
    }

    void UpdateTurnBias(int segmentIndex)
    {
        CaptureAuthoredBias();

        int block = randomizeTurnBias ? segmentIndex / Mathf.Max(1, scorePerBiasChange) : 0;
        if (block == turnBiasBlock) return;
        turnBiasBlock = block;

        if (block == 0)
        {
            // The opening stretch - and everything, with the reroll switched off - runs on the
            // odds as they were authored.
            leftWeight = authoredLeft;
            rightWeight = authoredRight;
            upWeight = authoredUp;
            downWeight = authoredDown;
        }
        else
        {
            int lo = Mathf.Min(minRolledWeight, maxRolledWeight);
            int hi = Mathf.Max(minRolledWeight, maxRolledWeight);

            int prevL = leftWeight, prevR = rightWeight, prevU = upWeight, prevD = downWeight;
            int[] w = new int[4];

            // Reroll if the draw repeats the odds just used - a "change" nobody would see.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int standouts = 0;
                for (int i = 0; i < 4; i++)
                {
                    bool big = PathValue < standoutChance;
                    w[i] = big ? standoutWeight : PathRange(lo, hi + 1);
                    if (big) standouts++;
                }

                // Only one direction may stand out. If the draw handed the big weight to
                // several, all but one randomly kept winner fall back into the ordinary range,
                // so a stretch never pulls two ways at once.
                if (standouts > 1)
                {
                    int keep = PathRange(0, standouts);
                    int seen = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        if (w[i] != standoutWeight) continue;
                        if (seen++ == keep) continue;
                        w[i] = PathRange(lo, hi + 1);
                    }
                }

                if (w[0] != prevL || w[1] != prevR || w[2] != prevU || w[3] != prevD) break;
            }

            leftWeight = w[0];
            rightWeight = w[1];
            upWeight = w[2];
            downWeight = w[3];

            // Left and right both at zero would leave a horizontal leg nowhere flat to go.
            if (leftWeight <= 0 && rightWeight <= 0) leftWeight = rightWeight = 1;
        }

        if (logTurnBiasChanges)
            Debug.Log($"[DynamicPathGenerator] score {block * Mathf.Max(1, scorePerBiasChange)}: " +
                      $"turn odds left:right:up:down = {leftWeight}:{rightWeight}:" +
                      $"{upWeight}:{downWeight}");
    }

    /// <summary>The odds in force right now, as left:right:up:down.</summary>
    public Vector4 CurrentTurnBias =>
        new Vector4(leftWeight, rightWeight, upWeight, downWeight);

    static void AddWeighted(List<Vector3> into, Vector3 dir, int weight)
    {
        for (int i = 0; i < weight; i++) into.Add(dir);
    }

    Vector3 GetNextDirection(Vector3 prevDir, Vector3 twoBackDir, bool allowHalfTurn,
                             Vector3 origin, int legLen, out bool found, Segment fitFor = null, bool timeOnly = false)
    {
        found = true;

        // Left and right are read off the heading being turned away from. A vertical leg has no
        // sideways of its own, so the last horizontal heading stands in for it - and there the
        // old heading survives as a fourth option, carrying on the way the path was already
        // going. It gets the average of the two side weights, so an even left:right stays even.
        Vector3 heading = Mathf.Abs(prevDir.y) < 0.9f ? prevDir : lastHorizontalDirection;
        if (heading.sqrMagnitude < 0.01f) heading = Vector3.forward;

        // Snapped to a cardinal axis, so the legs stay on the grid whatever angle firstCube sits at.
        heading = Mathf.Abs(heading.x) >= Mathf.Abs(heading.z)
            ? new Vector3(Mathf.Sign(heading.x), 0f, 0f)
            : new Vector3(0f, 0f, Mathf.Sign(heading.z));
        Vector3 relRight = Vector3.Cross(Vector3.up, heading);

        int leftW = Mathf.Max(0, leftWeight);
        int rightW = Mathf.Max(0, rightWeight);
        if (leftW <= 0 && rightW <= 0) leftW = rightW = 1;
        int straightW = Mathf.Max(1, Mathf.RoundToInt((leftW + rightW) * 0.5f));

        var possible = new List<Vector3>();
        if (climbOnly)
        {
            // Dead end (ChooseDirection): a climb or a dive out of the pocket, whatever the turn bias says.
            possible.Add(Vector3.up);
            possible.Add(Vector3.down);
        }
        else
        {
            AddWeighted(possible, -relRight, leftW);
            AddWeighted(possible, relRight, rightW);
            AddWeighted(possible, heading, straightW);
            AddWeighted(possible, Vector3.up, Mathf.Max(0, upWeight));
            AddWeighted(possible, Vector3.down, Mathf.Max(0, downWeight));
        }

        // 90 degree turns only.
        possible.RemoveAll(d => Vector3.Dot(d, prevDir) > 0.9f);
        possible.RemoveAll(d => Vector3.Dot(d, prevDir) < -0.9f);

        // Never reverse the last HORIZONTAL heading. Leaving a climb every compass direction is
        // perpendicular to "up" and survives the filter above, including the one that doubles
        // the track back over itself - which costs the camera a full 180 degree swing and reads
        // as the track undoing itself.
        if (lastHorizontalDirection.sqrMagnitude > 0.01f)
            possible.RemoveAll(d => Vector3.Dot(d, lastHorizontalDirection) < -0.9f);

        // The runner lands on each corner facing back along twoBackDir, so turning ONTO
        // twoBackDir is the half-rotation case: two inputs, two cooldowns. Every other legal
        // turn is one input or none.
        if (!allowHalfTurn && twoBackDir.sqrMagnitude > 0.01f)
        {
            var affordable = possible.FindAll(d => Vector3.Dot(d, twoBackDir) < 0.9f);
            if (affordable.Count > 0) possible = affordable;
        }

        // DifficultyProfile: only turns the leg just laid has the time (and the budget) for. No fallback here;
        // ChooseDirection relaxes it if nothing clear is left.
        if (fitFor != null)
        {
            possible.RemoveAll(d => !TurnFits(fitFor, d, timeOnly));
            if (possible.Count == 0) { found = false; return Vector3.zero; }
            PreferTwoRoll311(possible, fitFor);   // 3.11: planned half turns (no-op elsewhere)
        }

        if (legLen > 0)
        {
            possible.RemoveAll(d => !LegIsClear(origin, d, legLen));
            if (possible.Count == 0) { found = false; return Vector3.zero; }
        }

        if (possible.Count == 0) return Vector3.up;

        Vector3 chosen = possible[PathRange(0, possible.Count)];
        if (Mathf.Abs(chosen.y) < 0.9f) lastHorizontalDirection = chosen;
        return chosen;
    }

    Quaternion GetRotationForDir(Vector3 dir)
    {
        if (Mathf.Abs(dir.y) > 0.9f)
            return Quaternion.LookRotation(dir, Vector3.back);
        return Quaternion.LookRotation(dir, Vector3.up);
    }

    // ------------------------------------------------------------------
    // Route queries
    // ------------------------------------------------------------------

    /// <summary>
    /// Chooses the camera yaw for every zone ahead of the camera's committed shot, all at once.
    ///
    /// Picking each zone's best angle on its own makes the camera chase every local optimum,
    /// so it turns on almost every hand-off. Here every zone's four candidate yaws are scored
    /// for legibility (PathZone.ScoreYaw) and every change of angle between consecutive shots
    /// is charged turnCost per 90 degrees - a 180 degree flip far more than twice that. The
    /// cheapest path through that grid (Viterbi) is the plan: the camera keeps its angle across
    /// several zones and turns only when the track ahead is worth it, one 90 degree step at a
    /// time - a staircase rather than a spin.
    ///
    /// Zones the camera has committed to are fixed and anchor the start of the plan. The rest
    /// may be revised as more track is generated; re-choosing the same yaw is slightly favoured
    /// so the plan does not flicker between equally good answers.
    /// </summary>
    public void PlanCameraYaws(float tiltX, float yawOffset, bool quantiseYaw,
                               float turnCost, Vector3 fallbackHeading)
    {
        int start = 0;
        for (int i = zones.Count - 1; i >= 0; i--)
        {
            if (zones[i].framingLocked && zones[i].framingSolved) { start = i; break; }
        }

        var planned = new List<PathZone>();
        for (int i = start; i < zones.Count; i++)
        {
            if (!zones[i].isSealed || zones[i].segments.Count == 0) break;
            planned.Add(zones[i]);
        }
        int n = planned.Count;
        if (n == 0) return;

        const int K = 4;
        var cand = new float[n, K];
        var total = new float[n, K];
        var back = new int[n, K];
        var scratch = new float[K];

        for (int i = 0; i < n; i++)
        {
            PathZone z = planned[i];
            bool fixedYaw = z.framingLocked && z.framingSolved;

            if (fixedYaw) for (int k = 0; k < K; k++) cand[i, k] = z.optimalCameraYaw;
            else
            {
                z.GetCandidateYaws(yawOffset, quantiseYaw, fallbackHeading, scratch);
                for (int k = 0; k < K; k++) cand[i, k] = scratch[k];
            }

            for (int k = 0; k < K; k++)
            {
                float unary = fixedYaw ? 0f : z.ScoreYaw(tiltX, cand[i, k]);
                if (!fixedYaw && z.yawPlanned && Mathf.Abs(Mathf.DeltaAngle(cand[i, k], z.plannedYaw)) < 1f)
                    unary -= 0.5f;

                if (i == 0)
                {
                    total[i, k] = unary;
                    back[i, k] = -1;
                    continue;
                }

                float best = float.MaxValue;
                int bestJ = 0;
                for (int j = 0; j < K; j++)
                {
                    float c = total[i - 1, j] + TurnCost(cand[i - 1, j], cand[i, k], turnCost);
                    // Wave pacing: the camera only turns into a zone that allows it (a RELEASE leg or a portal runway).
                    if (!z.yawChangeAllowed && z.forcedYaw < 0f && Mathf.Abs(Mathf.DeltaAngle(cand[i - 1, j], cand[i, k])) > 1f)
                        c += 100000f;
                    if (c < best) { best = c; bestJ = j; }
                }
                total[i, k] = unary + best;
                back[i, k] = bestJ;
            }
        }

        int pick = 0;
        for (int k = 1; k < K; k++)
            if (total[n - 1, k] < total[n - 1, pick]) pick = k;

        for (int i = n - 1; i >= 0; i--)
        {
            PathZone z = planned[i];
            if (!z.framingLocked)
            {
                float yaw = cand[i, pick];
                if (!z.yawPlanned || Mathf.Abs(Mathf.DeltaAngle(yaw, z.plannedYaw)) > 0.01f)
                {
                    z.plannedYaw = yaw;
                    z.yawPlanned = true;
                    z.framingSolved = false;
                }
            }
            if (i > 0) pick = back[i, pick];
        }
    }

    static float TurnCost(float fromYaw, float toYaw, float perQuarter)
    {
        float quarters = Mathf.Abs(Mathf.DeltaAngle(fromYaw, toYaw)) / 90f;
        if (quarters < 0.5f) return 0f;
        if (quarters < 1.5f) return perQuarter;
        return perQuarter * 4f;   // a 180 degree flip turns the whole world around; almost never worth it
    }

    /// <summary>
    /// World positions of the cubes in front of the runner, starting with the one nearest them:
    /// at least minCount cubes, and on to the end of the leg after `corners` turns (capped at
    /// maxCount). The camera keeps these on screen, so it is shown the upcoming turns rather than
    /// a fixed slice that can stop short of the corner.
    ///
    /// The runner's own cube is found by distance. player.currentCube is no use here - it only
    /// changes at corners, so on a long leg it is the leg's FIRST cube, far behind the runner.
    /// </summary>
    public void GetPathAhead(int minCount, int corners, int maxCount, List<Vector3> into)
    {
        into.Clear();
        if (minCount <= 0 || allSegments.Count == 0) return;

        int s0 = Mathf.Clamp(currentPlayerSegmentIdx, 0, allSegments.Count - 1);
        Vector3 from = player != null ? player.transform.position : allSegments[s0].startPosition;

        int bestS = s0, bestC = 0;
        float bestD = float.MaxValue;
        for (int s = s0; s <= Mathf.Min(s0 + 1, allSegments.Count - 1); s++)
        {
            List<GameObject> cubes = allSegments[s].cubes;
            for (int c = 0; c < cubes.Count; c++)
            {
                if (cubes[c] == null) continue;
                float d = (cubes[c].transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; bestS = s; bestC = c; }
            }
        }

        int turns = 0;
        for (int s = bestS, c = bestC; s < allSegments.Count && into.Count < maxCount; s++, c = 0)
        {
            if (s > bestS && Vector3.Dot(allSegments[s].direction, allSegments[s - 1].direction) < 0.9f) turns++;
            if (into.Count >= minCount && turns >= corners) break;

            List<GameObject> cubes = allSegments[s].cubes;
            for (; c < cubes.Count && into.Count < maxCount; c++)
                if (cubes[c] != null) into.Add(cubes[c].transform.position);
        }
    }

    /// <summary>0 when the runner enters the zone, approaching 1 on its last segment.</summary>
    public float PlayerProgressThrough(PathZone zone)
    {
        if (zone == null || zone.segments.Count == 0) return 0f;

        int first = allSegments.IndexOf(zone.segments[0]);
        int last = allSegments.IndexOf(zone.FinalSegment);
        if (first < 0 || last < first) return 0f;

        return Mathf.Clamp01((currentPlayerSegmentIdx - first + 0.5f) / (last - first + 1));
    }

    /// <summary>
    /// The next REAL direction changes in front of the runner, nearest first. Joins where two
    /// consecutive legs share a heading are skipped - they are not turns.
    /// </summary>
    public List<PathCorner> GetUpcomingCorners(int maxCount)
    {
        var corners = new List<PathCorner>();
        if (maxCount <= 0) return corners;

        for (int s = currentPlayerSegmentIdx + 1; s < allSegments.Count && corners.Count < maxCount; s++)
        {
            if (!IsDirectionChange(s)) continue;

            corners.Add(new PathCorner
            {
                position = allSegments[s].startPosition,
                incomingDirection = allSegments[s - 1].direction,
                outgoingDirection = allSegments[s].direction,
                segmentIndex = s
            });
        }

        return corners;
    }

    bool IsDirectionChange(int s)
    {
        if (s <= 0 || s >= allSegments.Count) return false;
        if (allSegments[s].cubes.Count == 0) return false;
        return Vector3.Dot(allSegments[s].direction, allSegments[s - 1].direction) < 0.9f;
    }

    public List<Vector3> GetPathKeyPositions()
    {
        var positions = new List<Vector3>();
        if (allSegments.Count == 0) return positions;

        foreach (Segment seg in allSegments)
            if (seg.cubes.Count > 0) positions.Add(seg.startPosition);

        Segment last = allSegments[allSegments.Count - 1];
        if (last.cubes.Count > 0)
        {
            Vector3 endPos = last.endPosition;
            if (positions.Count == 0 || positions[positions.Count - 1] != endPos)
                positions.Add(endPos);
        }

        return positions;
    }

    public Vector3 GetNextHorizontalDirection(Transform fromCube)
    {
        int fromSegIdx = 0;
        for (int s = 0; s < allSegments.Count; s++)
        {
            if (allSegments[s].cubes.Exists(c => c != null && c.transform == fromCube))
            {
                fromSegIdx = s;
                break;
            }
        }

        for (int s = fromSegIdx; s < allSegments.Count; s++)
        {
            Vector3 dir = allSegments[s].direction;
            if (Mathf.Abs(dir.y) < 0.9f) return dir;
        }
        return Vector3.zero;
    }
}
