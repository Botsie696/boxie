using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TRIANGLE GATES (on only in MainScene prop camera 3.6; gateSections defaults to false). An open A-frame stands over
/// the rail at the centre of some legs, on the face the runner LANDED on for that leg: that face's normal is the
/// frame's up, the rail passes through the gap between its two base bars, and a special coin floats in the opening.
///
/// The rule it teaches: stay on your landing face through the gate. The base bars sit beside the rail at the level of
/// the cube's axis, exactly where a runner on either ADJACENT face rides, and the roll arc toward those faces runs into
/// them in its second half, so rolling sideways before or inside the gate hits the frame. The opposite face (two rolls
/// away) passes under the frame unless gateBlockOpposite adds a keel bar beneath the rail.
///
/// Spawn rules (TryPlaceGateBehindTurn, run when the leg after it is laid so the next turn is known):
///   * only while the legs being laid belong to checkpoint gateCheckpoint (portal zones), past gateAfterLeg;
///   * ordinary legs only: no boost, portal runway, bridge, decoy fork at either corner, power-up quiet leg or
///     obstacle (the gate is placed first and the obstacle skips a gate leg);
///   * the leg is chosen when it is laid (PlanGateLeg) and stretched so the rules below can hold at its speed;
///   * the leg's centre cube, never anywhere else; the leg is skipped instead if, at its speed,
///       - the runner has less than gateMinSeeSeconds from landing to the frame, or
///       - after the frame there is less than the next corner's roll time (1 roll, or 2 for the opposite face,
///         plus cooldown, corner deadline and gateExitReactionSeconds).
///
/// Everything is pooled (frame, colliders and coin are one pooled object, the meshes are shared); nothing is allocated
/// per frame. See PathGateGuard for the per-gate behaviour.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Triangle Gates (MainScene prop camera 3.6)")]
    [Tooltip("Stand a triangle gate over the rail, on the landing face, at the centre of some legs. Off everywhere except MainScene prop camera 3.6.")]
    public bool gateSections = false;
    [Tooltip("Yellow frame gates only appear on legs that belong to this checkpoint (1 = from the start to the first door). 0 = no yellow gates anywhere.")]
    [Range(0, 10)] public int gateCheckpoint = 1;
    [Tooltip("No gates on legs before this score.")]
    [Range(0, 60)] public int gateAfterLeg = 3;
    [Tooltip("Chance (0-1) an eligible leg gets a gate.")]
    [Range(0f, 1f)] public float gateChance = 0.35f;
    [Tooltip("A leg shorter than this many cubes never gets a gate.")]
    [Range(4, 40)] public int gateMinLegCubes = 12;
    [Tooltip("Least time, at the leg's speed, from landing on the leg to reaching the frame.")]
    [Range(0.2f, 2f)] public float gateMinSeeSeconds = 0.7f;
    [Tooltip("Reaction time the next corner gets after the frame, on top of its rolls, cooldown and corner deadline (the profile's mechanics when the scene has one).")]
    [Range(0f, 1f)] public float gateExitReactionSeconds = 0.35f;
    [Tooltip("Extra seconds after the frame on top of that, either way.")]
    [Range(0f, 0.5f)] public float gateExitExtraSeconds = 0.1f;

    [Header("Triangle Gate Shape (cubes)")]
    [Tooltip("Height of the apex above the rail's axis. The runner's top is at 1.55.")]
    [Range(2.2f, 6f)] public float gateApexHeight = 3.6f;
    [Tooltip("Half the width of the base, from the rail's axis to the foot of a slanted bar.")]
    [Range(1.6f, 4f)] public float gateHalfWidth = 2.3f;
    [Tooltip("Thickness of the slanted bars.")]
    [Range(0.12f, 0.7f)] public float gateBarThickness = 0.42f;
    [Tooltip("Height of the base bars beside the rail (they are what an adjacent-face runner hits).")]
    [Range(0.3f, 1f)] public float gateBaseBarHeight = 0.6f;
    [Tooltip("Centre of the base bars above the rail's axis.")]
    [Range(-0.3f, 0.4f)] public float gateBaseBarY = 0.05f;
    [Tooltip("Depth of the frame along the rail.")]
    [Range(0.2f, 1.2f)] public float gateDepth = 0.5f;
    [Tooltip("Size of the dark bevels on every bar edge.")]
    [Range(0f, 0.2f)] public float gateBevel = 0.08f;
    [Tooltip("Gap between the rail's side and the inner end of a base bar.")]
    [Range(0.02f, 0.4f)] public float gateOpeningGap = 0.1f;
    [Tooltip("Close the opposite face too: a keel bar under the rail, so the frame can only be passed on the landing face. Off = a runner who flips to the opposite face BEFORE the frame passes under it.")]
    public bool gateBlockOpposite = false;

    [Header("Triangle Gate Look")]
    [Tooltip("Material of the frame (Gates/GateFrame shader).")]
    public Material gateFrameMaterial;
    [Tooltip("Glow of the frame's faces (multiplies the material's glow colour).")]
    [Range(0f, 3f)] public float gateGlow = 0.45f;
    [Tooltip("Pulses per second of that glow.")]
    [Range(0f, 4f)] public float gatePulseSpeed = 1.1f;
    [Tooltip("How deep the pulse goes (0 = steady).")]
    [Range(0f, 1f)] public float gatePulseDepth = 0.5f;
    [Tooltip("Extra glow flashed on the frame when the runner goes through cleanly.")]
    [Range(0f, 6f)] public float gatePassFlash = 2.5f;
    [Tooltip("A roll back onto the landing face finishing this close before the frame (or off it this soon after) is a close call.")]
    [Range(0f, 0.5f)] public float gateCloseCallSeconds = 0.15f;

    [Header("Triangle Gate Coin")]
    [Tooltip("Material of the special coin (Gates/GateFrame shader).")]
    public Material gateCoinMaterial;
    [Tooltip("Coins the special coin is worth. 0 = no special coin.")]
    [Range(0, 50)] public int gateCoinValue = 5;
    [Tooltip("Width of the special coin, in cubes.")]
    [Range(0.4f, 1.6f)] public float gateCoinSize = 1.15f;
    [Tooltip("Degrees per second the special coin spins.")]
    [Range(0f, 720f)] public float gateCoinSpinSpeed = 90f;
    [Tooltip("Ordinary coins within this many cubes of the gate are taken off the leg (the special coin is the gate's reward).")]
    [Range(0, 6)] public int gateCoinClearCubes = 3;

    [Header("Triangle Gates - Cube Style (3.6: the zone after the first door)")]
    [Tooltip("Checkpoint whose legs get CUBE-STYLE gates instead: the same triangle and rules, but every bar is a path cube's cross-section and is drawn with the leg's own cube material and tint, so it takes that zone's look. 0 = none.")]
    [Range(0, 10)] public int gateCubeCheckpoint = 0;
    [Tooltip("Cube style: apex height above the rail's axis (wider than the thin frame, so 1-cube bars keep clear of the runner).")]
    [Range(2.5f, 7f)] public float gateCubeApexHeight = 4.4f;
    [Tooltip("Cube style: half the width of the base.")]
    [Range(2f, 5f)] public float gateCubeHalfWidth = 2.8f;

    public partial class Segment
    {
        public bool gate;       // a triangle gate stands on this leg (DynamicPathGeneratorGate.cs)
    }

    public float GateCoinSpin => gateCoinSpinSpeed;

    private System.Random gateRng;
    private Transform gateRoot;
    private readonly Stack<PathGateGuard> gatePool = new Stack<PathGateGuard>();
    private readonly Stack<PathGateGuard> gateCubePool = new Stack<PathGateGuard>();
    private readonly List<PathGateGuard> liveGates = new List<PathGateGuard>();
    private Mesh gateFrameMesh, gateCoinMesh;
    private string gateMeshKey;
    private PathDepthShading gateShading;

    /// <summary>Gates standing on the track now (AutoPlayer and tooling read these).</summary>
    public IReadOnlyList<PathGateGuard> LiveGates => liveGates;

    // ------------------------------------------------------------------ placement

    int GateZoneIndex => portalZones ? PortalsTaken + 1 + (portalHold ? 1 : 0) : 1;
    // 1 = the yellow frame, 2 = cube style, 0 = no gates in the zone being laid.
    int GateStyleNow => GateZoneIndex == gateCheckpoint ? 1 : gateCubeCheckpoint > 0 && GateZoneIndex == gateCubeCheckpoint ? 2 : 0;
    float GateDepthFor(int style) => style == 2 ? 1f : gateDepth;

    // Seconds the next corner needs once the frame is behind: its rolls, the cooldown between two, the corner
    // deadline, a reaction and the extra margin. The corner has been in view the whole leg, so this is an execution
    // budget, not a full leg's decision time.
    float GateExitSeconds(int taps)
    {
        var P = difficultyProfile;
        float roll = P != null ? P.rollTime : 0.10f, cd = P != null ? P.rollCooldown : 0.08f, dl = P != null ? P.cornerDeadline : 0.03f;
        return taps * roll + Mathf.Max(0, taps - 1) * cd + dl + gateExitReactionSeconds + gateExitExtraSeconds;
    }

    /// <summary>
    /// Called by AppendSegment before an ordinary leg's turn is chosen: rolls gateChance and, if the leg is to carry a
    /// gate, stretches it so that at its speed a frame at the very centre has gateMinSeeSeconds in front and the
    /// worst-case corner (two rolls) behind. Ordinary 3.6 legs (1.0-1.3 s) are too short for both otherwise.
    /// </summary>
    void PlanGateLeg(ref int segLen)
    {
        int style = GateStyleNow;
        if (segmentSerial <= 1 || style == 0) gatePlanned.Clear();   // a restart or a new checkpoint: old plans are void
        if (!gateSections || tutorialActive || style == 0 || (style == 1 && gateFrameMaterial == null)) return;
        if (segmentSerial < gateAfterLeg || PowerQuiet || PowerHandoverLeg(segmentSerial)) return;
        gateRng ??= new System.Random();
        if (gateRng.NextDouble() >= gateChance) return;

        float speed = LegSpeed(segmentSerial, false);
        float half = Mathf.Max(gateMinSeeSeconds, GateExitSeconds(2));
        int need = Mathf.CeilToInt(2f * half * speed / Mathf.Max(0.01f, cubeSize) + GateDepthFor(style) + 2f);
        segLen = Mathf.Max(segLen, Mathf.Max(need, gateMinLegCubes));
        gatePlanned[segmentSerial] = style;
    }

    /// <summary>The decoy partial skips a fork at either corner of a leg planned for a gate.</summary>
    bool GateBlocksDecoy() => gatePlanned.Count > 0 && (gatePlanned.ContainsKey(segmentSerial) || gatePlanned.ContainsKey(segmentSerial - 1));

    private readonly Dictionary<int, int> gatePlanned = new Dictionary<int, int>();   // leg serial -> style

    void TryPlaceGateBehindTurn(Segment incoming, Segment next, Vector3 nextDir)
    {
        if (!gateSections || incoming == null) return;
        if (!gatePlanned.TryGetValue(incoming.serial, out int style)) return;
        gatePlanned.Remove(incoming.serial);
        if (tutorialActive || incoming.portalRole != 0 || incoming.boost) return;
        if (incoming.cubes.Count < gateMinLegCubes) return;
        if (incoming.plankCount > 0 || GetBridgeSpan(incoming.cubes, out _, out _)) return;
        if (incoming.decoyCubes != null || (next != null && next.decoyCubes != null) || incoming.decoyAtEnd) return;
        if (PowerBlocksObstacle(incoming)) return;

        // The landing face (the face pointing back along the leg before) and the face the next corner needs.
        int at = allSegments.IndexOf(incoming);
        Segment before = at > 0 ? allSegments[at - 1] : null;
        int landing = incoming.landingSide >= 0 ? incoming.landingSide : LandingSideFor(incoming, before);
        int turn = SideOnLeg(incoming.direction, nextDir);
        int taps = Rolls(landing, turn);

        List<GameObject> cubes = incoming.cubes;
        int index = Mathf.RoundToInt((cubes.Count - 1) * 0.5f);
        if (cubes[index] == null) return;

        // Time rules, at the speed of this leg.
        float speed = incoming.speed > 0.01f ? incoming.speed : LegSpeed(incoming.serial, false);
        float halfDepth = GateDepthFor(style) * 0.5f;
        float toFrame = (index - halfDepth - 0.5f) * cubeSize / speed;              // landing (cube 0) to the frame's front
        if (toFrame < gateMinSeeSeconds) return;
        float afterFrame = ((cubes.Count - 1) - index - halfDepth - 0.5f) * cubeSize / speed;   // frame's back to the corner
        if (afterFrame < GateExitSeconds(taps)) return;

        PlaceGate(incoming, cubes[index].transform, landing, style);
        incoming.gate = true;
        ClearCoinsNearGate(incoming, index);
    }

    void PlaceGate(Segment seg, Transform cubeT, int landing, int style)
    {
        if (gateShading == null) gateShading = GetComponent<PathDepthShading>();
        EnsureGateMeshes();

        PathGateGuard g = style == 2 ? (gateCubePool.Count > 0 ? gateCubePool.Pop() : NewCubeGate(cubeT))
                                     : (gatePool.Count > 0 ? gatePool.Pop() : NewGate());
        if (g == null) return;
        Vector3 up = SideNormalOf(cubeT, landing);
        g.transform.SetPositionAndRotation(cubeT.position, Quaternion.LookRotation(cubeT.forward, up));
        liveGates.Add(g);
        g.Spawn(this, player, seg, cubeT.gameObject, gateShading, landing, GateDepthFor(style) * 0.5f * cubeSize,
                gateCoinMaterial != null ? gateCoinValue : 0, coinPickupRadius * cubeSize, gateCloseCallSeconds,
                gateGlow, gatePulseSpeed, gatePulseDepth, gatePassFlash);
    }

    /// <summary>Called by a gate when its cube is gone or it has faded behind the runner.</summary>
    public void ReleaseGate(PathGateGuard g)
    {
        int i = liveGates.IndexOf(g);
        if (i >= 0)
        {
            int last = liveGates.Count - 1;
            liveGates[i] = liveGates[last];
            liveGates.RemoveAt(last);
        }
        bool cube = g.CubeStyle;
        g.Clear();
        (cube ? gateCubePool : gatePool).Push(g);
    }

    void ClearCoinsNearGate(Segment seg, int index)
    {
        if (gateCoinClearCubes <= 0) return;
        for (int i = coins.Count - 1; i >= 0; i--)
        {
            if (coins[i].segment != seg) continue;
            int at = seg.cubes.IndexOf(coins[i].cube);
            if (at >= 0 && Mathf.Abs(at - index) <= gateCoinClearCubes) RemoveCoinAt(i);
        }
    }

    static Vector3 SideNormalOf(Transform cubeT, int side) => side switch
    {
        1 => cubeT.right,
        2 => -cubeT.up,
        3 => -cubeT.right,
        _ => cubeT.up
    };

    /// <summary>For AutoPlayer: the nearest gate ahead of (or around) the runner on its own leg, and the face it must hold.</summary>
    public bool TryGetGateAhead(Vector3 runnerPos, out int holdSide, out float along)
    {
        holdSide = -1; along = 0f;
        Segment seg = PlayerSegment;
        float best = float.MaxValue;
        for (int i = 0; i < liveGates.Count; i++)
        {
            PathGateGuard g = liveGates[i];
            if (g.segment != seg) continue;
            float a = Vector3.Dot(g.transform.position - runnerPos, g.transform.forward);
            if (a < -(g.HalfDepth + 0.6f * cubeSize) || a >= best) continue;
            best = a;
            holdSide = g.landingSide;
        }
        along = best;
        return holdSide >= 0;
    }

    // ------------------------------------------------------------------ pooled object

    PathGateGuard NewGate()
    {
        if (gateRoot == null)
        {
            gateRoot = new GameObject("Gates").transform;
            gateRoot.SetParent(transform, false);
        }
        if (obstacleLayer < 0) obstacleLayer = ResolveObstacleLayer();

        var root = new GameObject("TriangleGate") { layer = obstacleLayer };
        root.transform.SetParent(gateRoot, false);
        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var vis = new GameObject("Frame") { layer = obstacleLayer };
        vis.transform.SetParent(root.transform, false);
        vis.AddComponent<MeshFilter>().sharedMesh = gateFrameMesh;
        var mr = vis.AddComponent<MeshRenderer>();
        mr.sharedMaterial = gateFrameMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        var coinGo = new GameObject("GateCoin") { layer = obstacleLayer };
        coinGo.transform.SetParent(vis.transform, false);
        coinGo.transform.localPosition = new Vector3(0f, (0.5f + 0.55f) * cubeSize, 0f);   // the runner's centre on the landing face
        coinGo.AddComponent<MeshFilter>().sharedMesh = gateCoinMesh;
        var cr = coinGo.AddComponent<MeshRenderer>();
        cr.sharedMaterial = gateCoinMaterial;
        cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        cr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        // Triggers on unscaled children: the base bars (where an adjacent-face runner rides) and the optional keel.
        float s = cubeSize;
        float xin = 0.5f + gateOpeningGap, xout = gateHalfWidth + gateBarThickness * 0.5f;
        var colList = new List<Collider>(3);
        colList.Add(AddGateBox(root.transform, new Vector3(-(xin + xout) * 0.5f, gateBaseBarY, 0f) * s, new Vector3(xout - xin, gateBaseBarHeight, gateDepth) * s));
        colList.Add(AddGateBox(root.transform, new Vector3((xin + xout) * 0.5f, gateBaseBarY, 0f) * s, new Vector3(xout - xin, gateBaseBarHeight, gateDepth) * s));
        if (gateBlockOpposite)
            colList.Add(AddGateBox(root.transform, new Vector3(0f, KeelY, 0f) * s, new Vector3(2f * (xin + 0.15f), KeelHeight, gateDepth) * s));

        var guard = root.AddComponent<PathGateGuard>();
        guard.Build(vis.transform, mr, coinGo.transform, cr, colList.ToArray());
        root.SetActive(false);
        return guard;
    }

    // Cube style: the same triangle built from path-cube bars (the leg's own mesh), each bar one cube across and one
    // deep, stretched to its length. PathGateGuard copies the leg cube's material and property block onto them every
    // frame, so they take the zone's look, tint and fade exactly like the leg. Colliders: the base bars, one cube tall.
    PathGateGuard NewCubeGate(Transform cubeT)
    {
        MeshFilter src = null;
        foreach (MeshFilter mf in cubeT.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) { src = mf; break; }
        if (src == null) return null;
        if (gateRoot == null)
        {
            gateRoot = new GameObject("Gates").transform;
            gateRoot.SetParent(transform, false);
        }
        if (obstacleLayer < 0) obstacleLayer = ResolveObstacleLayer();

        var root = new GameObject("TriangleGateCube") { layer = obstacleLayer };
        root.transform.SetParent(gateRoot, false);
        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var vis = new GameObject("Frame") { layer = obstacleLayer };
        vis.transform.SetParent(root.transform, false);

        float s = cubeSize, W = gateCubeHalfWidth, A = gateCubeApexHeight, xin = 0.5f + gateOpeningGap;
        var parts = new List<MeshRenderer>(5);
        for (int side = -1; side <= 1; side += 2)
        {
            parts.Add(CubeBar(vis.transform, src.sharedMesh, new Vector2(side * (xin + 0.5f), 0f), new Vector2(side * W, 0f), s));
            parts.Add(CubeBar(vis.transform, src.sharedMesh, new Vector2(side * W, 0f), new Vector2(0f, A), s));
        }
        parts.Add(CubeBar(vis.transform, src.sharedMesh, new Vector2(0f, A - 0.01f), new Vector2(0f, A), s));

        var coinGo = new GameObject("GateCoin") { layer = obstacleLayer };
        coinGo.transform.SetParent(vis.transform, false);
        coinGo.transform.localPosition = new Vector3(0f, (0.5f + 0.55f) * s, 0f);
        coinGo.AddComponent<MeshFilter>().sharedMesh = gateCoinMesh;
        var cr = coinGo.AddComponent<MeshRenderer>();
        cr.sharedMaterial = gateCoinMaterial;
        cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        float xout = W + 0.5f;
        var cols = new Collider[]
        {
            AddGateBox(root.transform, new Vector3(-(xin + xout) * 0.5f, 0f, 0f) * s, new Vector3(xout - xin, 1f, 1f) * s),
            AddGateBox(root.transform, new Vector3((xin + xout) * 0.5f, 0f, 0f) * s, new Vector3(xout - xin, 1f, 1f) * s),
        };

        var guard = root.AddComponent<PathGateGuard>();
        guard.Build(vis.transform, null, coinGo.transform, cr, cols);
        guard.SetCubeParts(parts.ToArray());
        root.SetActive(false);
        return guard;
    }

    // One path-cube bar from p0 to p1 in the frame's plane (cube units), its square ends centred on p0 and p1 (so a
    // bar is one cube longer than the distance and its ends fill the joints): local z along the bar, local y along the rail.
    MeshRenderer CubeBar(Transform parent, Mesh mesh, Vector2 p0, Vector2 p1, float s)
    {
        var go = new GameObject("CubeBar") { layer = obstacleLayer };
        go.transform.SetParent(parent, false);
        Vector3 a = new Vector3(p0.x, p0.y, 0f) * s, b = new Vector3(p1.x, p1.y, 0f) * s;
        Vector3 d = b - a;
        if (d.sqrMagnitude < 1e-6f) d = Vector3.up;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.LookRotation(d, Vector3.forward);
        go.transform.localScale = new Vector3(s, s, Vector3.Distance(a, b) + s);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return mr;
    }

    const float KeelY = -1.05f, KeelHeight = 0.5f;

    BoxCollider AddGateBox(Transform parent, Vector3 centre, Vector3 size)
    {
        var go = new GameObject("GateBar") { layer = obstacleLayer };
        go.transform.SetParent(parent, false);
        var b = go.AddComponent<BoxCollider>();
        b.center = centre;
        b.size = size;
        b.isTrigger = true;
        b.enabled = false;
        return b;
    }

    // ------------------------------------------------------------------ meshes (shared by every gate)

    readonly List<Vector3> gv = new List<Vector3>();
    readonly List<Vector3> gn = new List<Vector3>();
    readonly List<Color> gc = new List<Color>();
    readonly List<Vector2> guv = new List<Vector2>();
    readonly List<int> gt = new List<int>();
    readonly Vector2[] gProfile = new Vector2[8];

    void EnsureGateMeshes()
    {
        string key = $"{cubeSize}|{gateApexHeight}|{gateHalfWidth}|{gateBarThickness}|{gateBaseBarHeight}|{gateBaseBarY}|{gateDepth}|{gateBevel}|{gateOpeningGap}|{gateBlockOpposite}|{gateCoinSize}";
        if (gateFrameMesh != null && key == gateMeshKey) return;
        bool rebuilt = gateFrameMesh != null;
        gateMeshKey = key;

        float s = cubeSize;
        float T = gateBarThickness, D = gateDepth, b = gateBevel;
        float xin = 0.5f + gateOpeningGap, W = gateHalfWidth, A = gateApexHeight, yb = gateBaseBarY;

        BeginGateMesh();
        for (int side = -1; side <= 1; side += 2)
        {
            // Base bar: beside the rail, striped (the part that kills).
            GateBar(new Vector2(side * xin, yb), new Vector2(side * (W + T * 0.5f), yb), gateBaseBarHeight, D, b, true);
            // Slanted bar: foot on the base bar's outer end, up to the apex. A little shallower so the base reads in front.
            GateBar(new Vector2(side * W, yb), new Vector2(0f, A), T, D * 0.82f, b, false);
        }
        // Apex block over the joint.
        GateBar(new Vector2(0f, A - T * 0.95f), new Vector2(0f, A + T * 0.75f), T * 1.7f, D * 1.08f, b, false);
        if (gateBlockOpposite)
        {
            float kx = xin + 0.15f;
            GateBar(new Vector2(-kx, KeelY), new Vector2(kx, KeelY), KeelHeight, D, b, true);
            for (int side = -1; side <= 1; side += 2)
                GateBar(new Vector2(side * (xin + 0.05f), yb), new Vector2(side * (xin + 0.05f), KeelY), 0.22f, D * 0.82f, b, false);
        }
        gateFrameMesh = EndGateMesh(gateFrameMesh, "TriangleGateFrame", s);

        // The special coin: a six-point star with bevelled rims, standing on the face like a coin.
        BeginGateMesh();
        GateStar(gateCoinSize * 0.5f, gateCoinSize * 0.29f, 0.24f, 0.06f, 6);
        gateCoinMesh = EndGateMesh(gateCoinMesh, "TriangleGateCoin", s);

        // A shape change (Tune panel) rebuilds the pooled gates' colliders too: drop the pool, live gates keep theirs.
        if (rebuilt)
            while (gatePool.Count > 0) Destroy(gatePool.Pop().gameObject);
    }

    /// <summary>Tooling / Tune panel: apply shape slider changes to the gates on screen now (their colliders keep the old shape).</summary>
    public void RebuildGateMeshes() => EnsureGateMeshes();

    void BeginGateMesh() { gv.Clear(); gn.Clear(); gc.Clear(); guv.Clear(); gt.Clear(); }

    Mesh EndGateMesh(Mesh mesh, string name, float scale)
    {
        if (mesh == null) mesh = new Mesh { name = name };
        mesh.Clear();
        for (int i = 0; i < gv.Count; i++) gv[i] *= scale;
        mesh.SetVertices(gv);
        mesh.SetNormals(gn);
        mesh.SetColors(gc);
        mesh.SetUVs(0, guv);
        mesh.SetTriangles(gt, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // A straight bar from p0 to p1 in the frame's plane (x across the rail, y = landing normal), `thick` across in
    // that plane, `depth` along the rail, every long edge bevelled by `bevel`. Broad faces r = 1, bevels r = 0.
    void GateBar(Vector2 p0, Vector2 p1, float thick, float depth, float bevel, bool stripes)
    {
        Vector2 a2 = (p1 - p0).normalized;
        Vector3 axis = new Vector3(a2.x, a2.y, 0f);
        Vector3 u = new Vector3(-a2.y, a2.x, 0f);
        Vector3 z = Vector3.forward;
        float h = thick * 0.5f, d = depth * 0.5f, bv = Mathf.Min(bevel, Mathf.Min(h, d) * 0.9f);
        gProfile[0] = new Vector2(h - bv, d); gProfile[1] = new Vector2(h, d - bv);
        gProfile[2] = new Vector2(h, -d + bv); gProfile[3] = new Vector2(h - bv, -d);
        gProfile[4] = new Vector2(-h + bv, -d); gProfile[5] = new Vector2(-h, -d + bv);
        gProfile[6] = new Vector2(-h, d - bv); gProfile[7] = new Vector2(-h + bv, d);

        Vector3 s0 = new Vector3(p0.x, p0.y, 0f), s1 = new Vector3(p1.x, p1.y, 0f);
        float len = Vector3.Distance(s0, s1);
        float g = stripes ? 1f : 0f;

        for (int i = 0; i < 8; i++)
        {
            Vector2 a = gProfile[i], c = gProfile[(i + 1) % 8];
            Vector3 pa = u * a.x + z * a.y, pc = u * c.x + z * c.y;
            Vector2 e = c - a;
            Vector3 n = (u * -e.y + z * e.x).normalized;   // the profile runs clockwise: (-e.w, e.u) points out
            bool broad = (i & 1) == 1;
            Color col = new Color(broad ? 1f : 0f, g, 0f, 1f);
            float across = broad ? (Mathf.Abs(n.z) > 0.5f ? a.x : a.y) : 0f;
            GateQuad(s0 + pa, s0 + pc, s1 + pc, s1 + pa, n, col, 0f, len, across, across + (c - a).magnitude);
        }
        // End caps (flat, broad colour, no stripes).
        GateCap(s0, -axis, u, z, new Color(1f, 0f, 0f, 1f));
        GateCap(s1, axis, u, z, new Color(1f, 0f, 0f, 1f));
    }

    void GateCap(Vector3 centre, Vector3 n, Vector3 u, Vector3 z, Color col)
    {
        int c0 = gv.Count;
        gv.Add(centre); gn.Add(n); gc.Add(col); guv.Add(Vector2.zero);
        for (int i = 0; i < 8; i++)
        {
            gv.Add(centre + u * gProfile[i].x + z * gProfile[i].y); gn.Add(n); gc.Add(col); guv.Add(Vector2.zero);
        }
        for (int i = 0; i < 8; i++) GateTri(c0, c0 + 1 + i, c0 + 1 + (i + 1) % 8, n);
    }

    void GateQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 n, Color col, float x0, float x1, float y0, float y1)
    {
        int b = gv.Count;
        gv.Add(v0); gv.Add(v1); gv.Add(v2); gv.Add(v3);
        for (int k = 0; k < 4; k++) { gn.Add(n); gc.Add(col); }
        guv.Add(new Vector2(x0, y0)); guv.Add(new Vector2(x0, y1)); guv.Add(new Vector2(x1, y1)); guv.Add(new Vector2(x1, y0));
        GateTri(b, b + 1, b + 2, n);
        GateTri(b, b + 2, b + 3, n);
    }

    // Adds a triangle wound so its front face looks along n.
    void GateTri(int a, int b, int c, Vector3 n)
    {
        Vector3 f = Vector3.Cross(gv[b] - gv[a], gv[c] - gv[a]);
        gt.Add(a);
        if (Vector3.Dot(f, n) >= 0f) { gt.Add(b); gt.Add(c); }
        else { gt.Add(c); gt.Add(b); }
    }

    // A `points`-point star in the x-y plane, `depth` thick along z, with a bevelled rim: bright front and back
    // faces, dark rim (reads as a coin edge, never round).
    void GateStar(float outer, float inner, float depth, float bevel, int points)
    {
        int n = points * 2;
        float d = depth * 0.5f;
        Color face = new Color(1f, 0f, 0f, 1f), rim = new Color(0f, 0f, 0f, 1f);
        for (int side = -1; side <= 1; side += 2)
        {
            // Front/back: a fan of the star shrunk by the bevel, at z = +-d.
            Vector3 nz = new Vector3(0f, 0f, side);
            int c0 = gv.Count;
            gv.Add(new Vector3(0f, 0f, side * d)); gn.Add(nz); gc.Add(face); guv.Add(Vector2.zero);
            for (int i = 0; i < n; i++)
            {
                gv.Add(StarPoint(i, n, outer - bevel, inner - bevel * 0.6f, side * d)); gn.Add(nz); gc.Add(face); guv.Add(Vector2.zero);
            }
            for (int i = 0; i < n; i++) GateTri(c0, c0 + 1 + i, c0 + 1 + (i + 1) % n, nz);
        }
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 o0 = StarPoint(i, n, outer, inner, 0f), o1 = StarPoint(j, n, outer, inner, 0f);
            Vector3 out2 = Vector3.Cross(o1 - o0, Vector3.forward).normalized;
            if (Vector3.Dot(out2, o0 + o1) < 0f) out2 = -out2;
            for (int side = -1; side <= 1; side += 2)
            {
                // Bevel from the face's edge out to the rim's mid-line.
                Vector3 f0 = StarPoint(i, n, outer - bevel, inner - bevel * 0.6f, side * d);
                Vector3 f1 = StarPoint(j, n, outer - bevel, inner - bevel * 0.6f, side * d);
                Vector3 m0 = o0 + Vector3.forward * (side * d * 0.35f), m1 = o1 + Vector3.forward * (side * d * 0.35f);
                Vector3 bn = (out2 + Vector3.forward * side).normalized;
                GateQuad(f0, f1, m1, m0, bn, rim, 0f, 0f, 0f, 0f);
            }
            // The rim band itself.
            GateQuad(o0 + Vector3.forward * (d * 0.35f), o1 + Vector3.forward * (d * 0.35f),
                     o1 - Vector3.forward * (d * 0.35f), o0 - Vector3.forward * (d * 0.35f), out2, rim, 0f, 0f, 0f, 0f);
        }
    }

    static Vector3 StarPoint(int i, int n, float outer, float inner, float z)
    {
        float ang = Mathf.PI * 0.5f + i * Mathf.PI * 2f / n;
        float r = (i & 1) == 0 ? outer : inner;
        return new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, z);
    }
}
