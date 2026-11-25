using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Makes the route readable in an orthographic view that has no perspective scaling to say what
/// is near and what is far. Everything here is decided PER CUBE from how far ahead of the runner
/// that cube is along the route, measured continuously (the runner's progress across their own
/// cube is included), so the effect glides along the track one cube at a time rather than
/// switching a whole leg at once.
///
///   Colour  - the stretch the runner is about to take is the lightest, warmest thing on screen;
///             further along, cubes shift through warm midtones into cool blue-grey. Covered
///             track settles to a cool slate that is still readable, not black.
///   Reveal  - far-ahead cubes are small and mostly transparent. Each one grows to full size and
///             full opacity as the runner closes in, so upcoming track "arrives" in sequence.
///   Cues    - beams further from the camera, and beams below the runner, are darkened a little,
///             so where two legs cross on screen the one in front pops forward. Both are weakest
///             on the immediate route so they never dim it.
///
/// Only the LOOK of a cube is scaled. The runner steers by raycasting against the cube colliders,
/// and a shrunken next cube would drop out of its forward probe and send it flying off the
/// track - so each path cube's renderer is handed to a visual-only child, and the cube itself,
/// collider included, stays full size. firstCube is left on its own renderer: it is the prefab the
/// generator clones, and it is behind the runner after the first second anyway.
///
/// Colours go through a MaterialPropertyBlock, so the shared cube material is never modified.
/// A fading cube swaps to fadeMaterial (a transparent copy of the cube material) and back to the
/// opaque original once it is fully visible, so solid track keeps depth writes, shadows and SSAO.
/// </summary>
[DefaultExecutionOrder(50)] // after DynamicPathGenerator has spawned and culled this frame's cubes
public class PathDepthShading : MonoBehaviour
{
    [Header("References")]
    public DynamicPathGenerator pathGen;
    [Tooltip("Transparent version of the path cube material, used while a cube is fading in. Created by Tools > Path Depth Lighting > Apply. Left empty, one is made at runtime - fine in the editor, but a build may strip the transparent shader variant.")]
    public Material fadeMaterial;

    [Header("Visible Window")]
    [Tooltip("Only the next few LEGS of the path (stretches between turns) are drawn. Everything keeps spawning, but track further on than this stays hidden until the runner gets close. Off = draw all of it.")]
    public bool limitVisibleLegs = true;
    [Tooltip("How many legs ahead of the one the runner is on are shown.")]
    [Range(1, 8)] public int visibleLegsAhead = 3;
    [Tooltip("Cubes at the far edge of the shown track over which it fades from visible to hidden.")]
    [Range(1f, 20f)] public float edgeFadeCubes = 5f;
    [Tooltip("How fast a newly shown leg draws itself in, in cubes per second, starting from the runner's end. Higher = the leg appears sooner.")]
    [Range(5f, 150f)] public float revealCubesPerSecond = 45f;
    [Tooltip("How many legs BEHIND the one the runner is on stay drawn (the part of the current leg already crossed always stays). Exactly this many - no more, no less - and the runner's own trail is cut to end where the last of them ends.")]
    [Range(0, 6)] public int visibleLegsBehind = 2;
    [Tooltip("How fast track behind the visible legs goes away, in cubes per second, oldest cube first.")]
    [Range(5f, 200f)] public float hideCubesPerSecond = 60f;
    [Tooltip("Cubes at the rear edge of the drawn track over which it fades from visible to gone.")]
    [Range(1f, 30f)] public float behindFadeOutCubes = 5f;
    [Tooltip("Cut the runner's own trail so it ends exactly where the last drawn cube ends, instead of trailing off into empty space.")]
    public bool matchRunnerTrailToVisibleTrack = true;
    [Tooltip("While a zone door is ahead, also draw the runway out of it and the legs beyond it - from the moment the runway in is laid, which is many legs before the runner gets there. Off = the door gets the same visibleLegsAhead window as any other leg.")]
    public bool portalWidensWindow = true;

    [Header("Cube Size")]
    [Tooltip("Visual thickness of every path cube, as a share of full size. Only the look changes - the collider the runner steers by stays full size - so going much thinner than 0.6 makes the runner look like it floats above the beam.")]
    [Range(0.3f, 1.5f)] public float cubeVisualScale = 1f;

    [Header("Self Light")]
    [Tooltip("How much each cube glows in its own route colour on top of the scene lighting, so beams whose visible faces get no sun still read clearly. 0 = lit by the scene only.")]
    [Range(0f, 1.5f)] public float selfLight = 0.55f;

    [Header("Route Colour")]
    [Tooltip("Track colour by distance ahead of the runner. Left = the cube under the runner, right = colourSpanCubes ahead and beyond. Light and warm on the left, cooler and deeper to the right.")]
    public Gradient aheadGradient = DefaultAheadGradient();
    [Tooltip("Cubes of route the colour gradient is spread over.")]
    [Range(4f, 80f)] public float colourSpanCubes = 40f;
    [Tooltip("How much brighter than the gradient colour the cube right ahead of the runner is. It eases back to the plain gradient over brightSpanCubes, cube by cube, so the next few cubes visibly light up as the runner closes in. 1 = off.")]
    [Range(1f, 2.5f)] public float nearBrightness = 1.7f;
    [Tooltip("Cubes ahead of the runner over which nearBrightness fades out.")]
    [Range(2f, 40f)] public float brightSpanCubes = 40f;
    [Tooltip("Tone for track the runner has already covered. Also the tone the depth and layer cues darken toward.")]
    public Color travelledColor = new Color32(0x3A, 0x45, 0x60, 0xFF);
    [Tooltip("Cubes behind the runner over which covered track settles to the travelled colour.")]
    [Range(1f, 30f)] public float behindFadeCubes = 5f;

    [Header("Reveal As It Approaches")]
    [Tooltip("Cubes ahead of the runner that are full size, exactly like the cube they are standing on. Beyond this, cubes shrink smoothly, one at a time.")]
    [Range(0f, 80f)] public float fullSizeAheadCubes = 30f;
    [Tooltip("Cubes over which the track shrinks from full size to farScale, beyond the full-size stretch.")]
    [Range(1f, 80f)] public float shrinkSpanCubes = 25f;
    [Tooltip("Size of the farthest track, as a share of a full cube.")]
    [Range(0.05f, 1f)] public float farScale = 0.6f;
    [Tooltip("Cubes ahead of the runner that are fully opaque. Beyond this, cubes turn slightly transparent, one at a time.")]
    [Range(0f, 80f)] public float opaqueAheadCubes = 45f;
    [Tooltip("Cubes over which the track fades from opaque to farAlpha, beyond the opaque stretch.")]
    [Range(1f, 80f)] public float fadeSpanCubes = 30f;
    [Tooltip("Opacity of the farthest track. Kept high so the route is always readable.")]
    [Range(0f, 1f)] public float farAlpha = 0.6f;
    [Tooltip("Opacity of the legs the runner has already crossed, so track that overlaps what is ahead can be seen through. 1 = off (crossed legs stay solid until they fade away). Crossed legs also stop writing depth, so they never hide the track behind them.")]
    [Range(0.05f, 1f)] public float crossedAlpha = 1f;
    [Tooltip("Seconds a leg takes to fade to crossedAlpha once the runner has left it.")]
    [Range(0.05f, 2f)] public float crossedFadeSeconds = 0.35f;
    [Tooltip("Brightness multiplier the farthest track is held at, so even the most distant cube stays clearly lit against the dark background instead of fading into it. The gradient and fade only make it recede, never vanish.")]
    [Range(0.5f, 2f)] public float farBrightness = 1.5f;

    [Header("Depth & Layer Cues")]
    [Tooltip("How much track further from the camera than the runner is darkened, so the beam in front reads as in front. 0 = off.")]
    [Range(0f, 0.7f)] public float depthDarkening = 0.2f;
    [Tooltip("World units behind the runner (along the view) at which depthDarkening is fully applied.")]
    [Range(2f, 80f)] public float depthRange = 25f;
    [Tooltip("How much track lower than the runner is darkened, so upper layers read above lower ones. 0 = off.")]
    [Range(0f, 0.7f)] public float lowerLayerDarkening = 0.15f;
    [Tooltip("World units below the runner at which lowerLayerDarkening is fully applied.")]
    [Range(2f, 60f)] public float lowerLayerRange = 12f;
    [Tooltip("Share of the cues applied right at the runner. They ramp to full strength along the colour gradient, so the immediate route always stays bright.")]
    [Range(0f, 1f)] public float cuesOnImmediateRoute = 0.3f;

    [Header("Boost Sections")]
    [Tooltip("While the runner is in a boost (DynamicPathGeneratorBoost.cs), every leg of that boost - ahead and behind - is drawn in full: full size, fully opaque, lit up, until the runner reaches the ordinary leg after it.")]
    public bool lightUpBoost = true;
    [Tooltip("Brightness of a lit boost, as a multiple of the cube right in front of the runner (route's nearest colour x nearBrightness). 1 = every boost cube as bright as that one. Much above 1 can wash a pale scene out to flat white.")]
    [Range(0.5f, 2f)] public float boostBrightness = 1f;
    [Tooltip("How much a lit boost glows in its own colour, on top of the scene lighting (compare Self Light).")]
    [Range(0f, 3f)] public float boostSelfLight = 0.9f;
    [Tooltip("How much a lit boost's glow pulses: 0 = steady, 1 = swells to double.")]
    [Range(0f, 2f)] public float boostPulseDepth = 0.8f;
    [Tooltip("Pulse speed of a lit boost, radians per second.")]
    [Range(0f, 20f)] public float boostPulseSpeed = 6f;
    [Tooltip("Seconds a boost takes to light up, and to settle back to the normal look once it is over.")]
    [Range(0.05f, 1.5f)] public float boostLightSeconds = 0.3f;

    [Header("Motion")]
    [Tooltip("How quickly a cube's colour follows its target. Size and opacity follow the runner's position directly.")]
    [Range(0.5f, 20f)] public float colorResponse = 6f;

    const string VisualName = "PathVisual";
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    class Entry
    {
        public Renderer renderer;
        public Transform visual;          // null for a cube that keeps its own renderer
        public Material opaque;
        public ShadowCastingMode shadowMode;
        public Color shown;
        public float scale = 1f;
        public float shownAmount;         // last frame's reveal, 0 = hidden .. 1 = fully drawn
        public bool fading;
        public bool ghost;                // drawn with the no-depth-write material
        public float crossFade;           // 0 = not yet left behind .. 1 = fully a crossed leg
        public float boostLit;            // 0 = normal look .. 1 = lit as part of the boost under way
        public float glow;                // self-light last written to the cube
        public bool initialised;
        public int seenFrame;
        public bool plank;                // a bridge plank (DynamicPathGeneratorBridge.cs): tinted as its own material
        public float farAlpha = 1f;       // opacity from distance alone (far track fades), for things that sit on the cube
        public float farSize = 1f;        // size from distance alone (far track shrinks)
    }

    private readonly Dictionary<GameObject, Entry> entries = new Dictionary<GameObject, Entry>();
    private readonly List<GameObject> route = new List<GameObject>();
    private readonly List<int> routeLeg = new List<int>();          // leg number of each route cube
    private readonly List<int> routeBoost = new List<int>();        // boost id of each route cube, 0 = ordinary leg
    // Decoy stub cubes (dead-end forks). Not part of the route; each is shaded as if it were the
    // real leg's cube at the same distance along, so it fades, shrinks and turns see-through in step.
    private readonly List<GameObject> extraCube = new List<GameObject>();
    private readonly List<float> extraPos = new List<float>();
    private readonly List<int> extraLeg = new List<int>();
    private int activeBoost;                                     // boost the runner is in, 0 = none
    private int activeBoostFirst = -1, activeBoostLast = -1;         // its first and last route index
    private readonly Dictionary<GameObject, int> routeIndex = new Dictionary<GameObject, int>();
    private GameObject frontierCube;                                 // last cube drawn in so far
    private float frontierFrac;
    private GameObject rearCube;                                     // oldest cube still drawn
    private float rearFrac;
    private TrailRenderer runnerTrail;
    private float runnerTrailTime0 = -1f;
    private Vector3 smoothedPoint;
    private bool haveSmoothedPoint;
    private readonly List<GameObject> dead = new List<GameObject>();
    private MaterialPropertyBlock block;
    private Material runtimeFade;
    private int playerIndex;
    private float lastProgress;

    /// <summary>World positions of the cubes that are drawn right now (visible enough to see).</summary>
    public void CollectVisible(List<Vector3> into, float minAlpha = 0.12f)
    {
        foreach (var kv in entries)
        {
            Entry e = kv.Value;
            if (kv.Key == null || e.renderer == null || !e.renderer.enabled || e.shown.a < minAlpha) continue;
            into.Add(kv.Key.transform.position);
        }
    }

    /// <summary>
    /// How much of a path cube is drawn: 0 hidden (or not shaded yet) .. 1 fully shown. Things that
    /// sit on the track, like coins, use it to appear and go away with their cube.
    /// </summary>
    public float ShownAmount(GameObject cube)
    {
        if (cube == null || !entries.TryGetValue(cube, out Entry e) || e.renderer == null || !e.renderer.enabled) return 0f;
        return e.shownAmount;
    }

    /// <summary>How see-through the track is at this cube from distance alone (1 near, farAlpha far ahead), and how much it
    /// has shrunk (1 near, farScale far ahead) - so coins and props on it can fade and shrink exactly like it.</summary>
    public float TrackOpacity(GameObject cube) => cube != null && entries.TryGetValue(cube, out Entry e) ? e.farAlpha : 1f;
    public float TrackSize(GameObject cube) => cube != null && entries.TryGetValue(cube, out Entry e) ? e.farSize : 1f;

    /// <summary>0 = the runner has not left this cube's leg .. 1 = left behind (eased over crossedFadeSeconds).</summary>
    public float CrossedAmount(GameObject cube)
    {
        if (cube == null || !entries.TryGetValue(cube, out Entry e)) return 0f;
        return e.crossFade;
    }

    /// <summary>The point on the route the runner is currently projected to.</summary>
    public Vector3 RoutePoint { get; private set; }
    public bool HasRoutePoint => haveSmoothedPoint;

    /// <summary>How many cubes ahead of the runner the drawn track currently reaches.</summary>
    public float VisibleAheadCubes { get; private set; } = 1000f;
    private bool fadeConfigured;

    void Start()
    {
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        block = new MaterialPropertyBlock();
    }

    void LateUpdate()
    {
        if (pathGen == null) return;

        BuildRoute();
        if (route.Count == 0) return;

        Transform runner = pathGen.player != null ? pathGen.player.transform : null;
        Transform runnerCube = pathGen.player != null ? pathGen.player.currentCube : null;
        if (runnerCube != null)
        {
            for (int i = 0; i < route.Count; i++)
                if (route[i].transform == runnerCube) { playerIndex = i; break; } // else keep the last spot while airborne
        }
        playerIndex = Mathf.Clamp(playerIndex, 0, route.Count - 1);

        // The runner's position ALONG THE ROUTE, as a continuous number: the cube index plus how
        // far between that cube and the next they are. It is found by projecting the runner onto
        // the line joining consecutive cube centres, so it flows through corners and the diagonal
        // step at each one. (Measuring "how far across the current cube" along the cube's own
        // facing direction jumps every time the facing changes at a corner - and with it every
        // cube's colour, size and opacity - which showed as the whole next leg lighting at once.)
        Vector3 runnerPos = runner != null ? runner.position : route[playerIndex].transform.position;
        float progress = RouteProgress(runnerPos);

        Camera cam = Camera.main;
        Vector3 viewDir = cam != null ? cam.transform.forward : Vector3.forward;

        float frontier = UpdateFrontier(progress);
        float rear = limitVisibleLegs ? UpdateRear(progress) : -1000f;
        VisibleAheadCubes = limitVisibleLegs ? Mathf.Max(0f, frontier - progress) : 1000f;
        UpdateRunnerTrail(progress, rear);

        float k = 1f - Mathf.Exp(-Mathf.Max(0.01f, colorResponse) * Time.deltaTime);
        int frame = Time.frameCount;
        Transform first = pathGen.firstCube;

        int routeCount = route.Count;
        for (int i = 0; i < routeCount + extraCube.Count; i++)
        {
            bool extra = i >= routeCount;
            GameObject cube = extra ? extraCube[i - routeCount] : route[i];
            if (cube == null) continue;
            float pos = extra ? extraPos[i - routeCount] : i;
            int legNo = extra ? extraLeg[i - routeCount] : routeLeg[i];
            int boostNo = extra ? 0 : routeBoost[i];

            Entry e = GetEntry(cube, cube.transform != first);
            e.seenFrame = frame;
            if (e.renderer == null) continue;

            float ahead = pos - progress;

            // How much of this cube is shown. Ahead: it appears as the drawn frontier reaches it
            // and fades in over edgeFadeCubes. Behind: once it is older than the last
            // visibleLegsBehind legs, it fades out on its own, cube by cube, over behindFadeOutCubes.
            float edge = limitVisibleLegs
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((frontier - pos) / Mathf.Max(0.5f, edgeFadeCubes)))
                : 1f;
            float gone = limitVisibleLegs
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rear - pos) / Mathf.Max(0.5f, behindFadeOutCubes)))
                : 0f;
            float shownAmount = edge * (1f - gone);
            e.shownAmount = shownAmount;
            // A cube outside the drawn track that is already hidden costs nothing more this frame (a long runway leg keeps
            // hundreds of them alive): skip the colour / size / material work until the frontier reaches it.
            if (shownAmount <= 0.001f && e.initialised && e.visual != null && !e.renderer.enabled) continue;

            // Part of the boost under way: eases into the lit look, and back out once it is over.
            bool inBoost = activeBoost > 0 && boostNo == activeBoost;
            e.boostLit = Mathf.MoveTowards(e.boostLit, inBoost ? 1f : 0f,
                                           Time.deltaTime / Mathf.Max(0.05f, boostLightSeconds));
            float lit = Mathf.SmoothStep(0f, 1f, e.boostLit);

            Color target = TargetColor(ahead, cube.transform.position, runnerPos, viewDir, cam != null);
            if (e.plank && pathGen.bridgePlankColorStrength > 0f)
            {
                // A bridge plank is drawn in its own colour so the side it is on reads at a glance.
                float a = target.a;
                target = Color.Lerp(target, pathGen.bridgePlankColor, pathGen.bridgePlankColorStrength);
                target.a = a;
            }
            if (lit > 0f) target = Color.Lerp(target, BoostLitColor(), lit);
            Color c = e.initialised ? Color.Lerp(e.shown, target, k) : target;

            // Size and opacity are separate ranges: the cubes far ahead shrink first, and the
            // ones nearest stay fully opaque, so the route close by is always solid.
            float fullness = e.visual != null ? Falloff(ahead, fullSizeAheadCubes, shrinkSpanCubes) : 1f;
            float opacity = e.visual != null ? Falloff(ahead, opaqueAheadCubes, fadeSpanCubes) : 1f;
            fullness = Mathf.Lerp(fullness, 1f, lit);
            opacity = Mathf.Lerp(opacity, 1f, lit);
            c.a = Mathf.Lerp(farAlpha, 1f, opacity) * shownAmount;
            e.farAlpha = Mathf.Lerp(farAlpha, 1f, opacity);
            e.farSize = Mathf.Lerp(farScale, 1f, fullness);

            // Legs the runner has left see-through, so the track ahead shows through them.
            // Not the boost under way: its crossed legs stay solid until the boost is over.
            {
                bool crossed = legNo < routeLeg[playerIndex] && !inBoost;
                e.crossFade = Mathf.MoveTowards(e.crossFade, crossed ? 1f : 0f,
                                                Time.deltaTime / Mathf.Max(0.05f, crossedFadeSeconds));
                if (crossedAlpha < 0.999f) c.a *= Mathf.Lerp(1f, crossedAlpha, e.crossFade);
            }
            bool ghost = e.crossFade > 0.01f;
            float scale = Mathf.Lerp(farScale, 1f, fullness) * cubeVisualScale * Mathf.Lerp(0.55f, 1f, shownAmount);

            if (e.visual != null) e.renderer.enabled = shownAmount > 0.01f;
            else e.renderer.enabled = gone < 0.995f; // the start cube keeps its own renderer

            // The swap to the transparent material only happens at the very end of the fade, where
            // the two look the same (the transparent one writes depth too - see ConfigureAsFade),
            // and the shadow mode is never toggled: either would show as a pop when a cube
            // crosses the boundary.
            bool fading = c.a < 0.999f;
            if (fading != e.fading || ghost != e.ghost || !e.initialised)
            {
                e.fading = fading;
                e.ghost = ghost;
                e.renderer.sharedMaterial = !fading ? e.opaque : ghost ? GhostMaterialFor(e.opaque) : FadeMaterialFor(e.opaque);
            }

            // Only the cross-section shrinks (x and y); the length along the beam (z) stays full,
            // so far track stays one continuous thin bar instead of breaking into a chain of tiny
            // separate cubes. The cube's forward is always along the route, climbs included.
            if (e.visual != null && Mathf.Abs(scale - e.scale) > 0.001f)
            {
                e.scale = scale;
                e.visual.localScale = new Vector3(scale, scale, 1f);
            }

            float boostGlow = boostSelfLight * (1f + boostPulseDepth * (0.5f + 0.5f * Mathf.Sin(Time.time * boostPulseSpeed)));
            float glowAmount = Mathf.Lerp(selfLight, boostGlow, lit);
            if (!e.initialised || MaxDelta(c, e.shown) > 0.002f || Mathf.Abs(glowAmount - e.glow) > 0.002f)
            {
                e.glow = glowAmount;
                e.shown = c;
                e.initialised = true;
                e.renderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                block.SetColor(ColorId, c);
                // Self-light: the beams glow in their own route colour, so a face turned away
                // from the sun is dim, not black.
                block.SetColor(EmissionId, new Color(c.r, c.g, c.b, 1f) * glowAmount);
                e.renderer.SetPropertyBlock(block);
            }
        }

        // Forget cubes the generator has culled.
        dead.Clear();
        foreach (var kv in entries)
            if (kv.Key == null || kv.Value.seenFrame != frame) dead.Add(kv.Key);
        foreach (GameObject g in dead) entries.Remove(g);
    }

    void OnDisable()
    {
        if (runnerTrail != null && runnerTrailTime0 > 0f) runnerTrail.time = runnerTrailTime0;
        // Leave every cube solid, full size and in its own material.
        foreach (Entry e in entries.Values)
        {
            if (e.renderer == null) continue;
            e.renderer.enabled = true;
            if (e.visual != null) e.visual.localScale = Vector3.one;
            e.renderer.sharedMaterial = e.opaque;
            e.renderer.shadowCastingMode = e.shadowMode;
            e.renderer.SetPropertyBlock(null);
        }
        entries.Clear();
    }

    void OnDestroy()
    {
        if (runtimeFade != null) Destroy(runtimeFade);
        foreach (Material m in litCopies.Values) if (m != null) Destroy(m);
    }

    // Route position of a point: index of the cube it is at or just past, plus the fraction
    // toward the next. Searches a small window around the runner's cube, so a distant stretch of
    // track that happens to pass close on screen can never capture it.
    float RouteProgress(Vector3 p)
    {
        // The raw projection steps about a cube forward at each corner (the runner turns while
        // the route line cuts the corner diagonally). Chasing it at a little over the runner's own
        // speed turns that step into a short glide, so nothing keyed to it lurches.
        Project(p, out Vector3 rawPoint);
        float maxStep = Mathf.Max(0.05f, (pathGen.player != null ? pathGen.player.forwardSpeed : 5f) * 1.15f * Time.deltaTime);
        if (!haveSmoothedPoint || (smoothedPoint - rawPoint).sqrMagnitude > 64f) smoothedPoint = rawPoint;
        else smoothedPoint = Vector3.MoveTowards(smoothedPoint, rawPoint, maxStep);
        haveSmoothedPoint = true;

        float progress = Project(smoothedPoint, out Vector3 onRoute);
        RoutePoint = onRoute;
        return progress;
    }

    float Project(Vector3 p, out Vector3 point)
    {
        point = p;
        int last = route.Count - 2;
        if (last < 0) return 0f;

        // The whole route is searched, not a window around player.currentCube: that only changes
        // at corners, so on a long leg it is the leg's first cube and the runner soon leaves any
        // window around it. The generator keeps separate stretches of track well apart, so the
        // nearest piece of route to the runner is always the one they are on.
        int lo = 0;
        int hi = last;

        float bestD = float.MaxValue;
        float best = Mathf.Clamp(lastProgress, 0f, last);
        for (int i = lo; i <= hi; i++)
        {
            Vector3 a = route[i].transform.position;
            Vector3 ab = route[i + 1].transform.position - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 0.0001f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
            float d = (a + ab * t - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i + t; }
        }

        lastProgress = best;

        int bi = Mathf.Clamp(Mathf.FloorToInt(best), 0, last);
        point = Vector3.Lerp(route[bi].transform.position, route[bi + 1].transform.position, best - bi);
        return best;
    }

    // 1 out to `full` cubes ahead, easing down to 0 over the next `span` cubes. Behind the runner = 1.
    static float Falloff(float ahead, float full, float span)
    {
        if (ahead <= full) return 1f;
        float t = (ahead - full) / Mathf.Max(0.01f, span);
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
    }

    Color TargetColor(float ahead, Vector3 pos, Vector3 runnerPos, Vector3 viewDir, bool haveCamera)
    {
        Color c;
        float cueScale;

        if (ahead >= 0f)
        {
            float t = Mathf.Clamp01(ahead / Mathf.Max(1f, colourSpanCubes));
            c = aheadGradient.Evaluate(t);
            cueScale = Mathf.Lerp(cuesOnImmediateRoute, 1f, t);
        }
        else
        {
            float t = Mathf.Clamp01(-ahead / Mathf.Max(1f, behindFadeCubes));
            c = Color.Lerp(aheadGradient.Evaluate(0f), travelledColor, t);
            cueScale = 1f;
        }

        float dark = 0f;
        if (haveCamera && depthDarkening > 0f)
        {
            float behind = Vector3.Dot(pos - runnerPos, viewDir);
            dark += depthDarkening * Mathf.Clamp01(behind / Mathf.Max(0.01f, depthRange));
        }
        if (lowerLayerDarkening > 0f)
        {
            float below = runnerPos.y - pos.y;
            dark += lowerLayerDarkening * Mathf.Clamp01(below / Mathf.Max(0.01f, lowerLayerRange));
        }

        // Darken TOWARD the cool travelled tone rather than toward black, so value and
        // temperature drop together.
        Color result = Color.Lerp(c, travelledColor, Mathf.Clamp01(dark * cueScale));

        // The cubes right ahead are lifted above the gradient and settle back to it with
        // distance, one cube at a time.
        if (ahead >= 0f)
        {
            // Bright next to the runner, easing to a lower but still lit level, then holding
            // farBrightness out to the end of the route rather than dropping to dark.
            float t = Mathf.Clamp01(ahead / Mathf.Max(1f, brightSpanCubes));
            float glow = Mathf.Lerp(nearBrightness, 1f, t);
            float farT = Mathf.Clamp01((ahead - brightSpanCubes) / Mathf.Max(1f, opaqueAheadCubes + fadeSpanCubes));
            glow = Mathf.Lerp(glow, farBrightness, farT);
            result.r *= glow; result.g *= glow; result.b *= glow;
        }
        return result;
    }

    /// <summary>
    /// First sight of a cube. Path cubes get a visual-only child carrying the mesh, so it can be
    /// scaled without touching the collider the runner steers by.
    /// </summary>
    Entry GetEntry(GameObject cube, bool useVisualProxy)
    {
        if (entries.TryGetValue(cube, out Entry e)) return e;

        e = new Entry();
        Renderer own = cube.GetComponent<Renderer>();
        MeshFilter mf = cube.GetComponent<MeshFilter>();

        if (useVisualProxy && own != null && mf != null)
        {
            Transform existing = cube.transform.Find(VisualName);
            GameObject vis = existing != null ? existing.gameObject : null;
            if (vis == null)
            {
                vis = new GameObject(VisualName) { layer = cube.layer };
                vis.transform.SetParent(cube.transform, false);
                vis.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                MeshRenderer r = vis.AddComponent<MeshRenderer>();
                r.sharedMaterial = own.sharedMaterial;
                r.shadowCastingMode = own.shadowCastingMode;
                r.receiveShadows = own.receiveShadows;
            }
            own.enabled = false;

            e.visual = vis.transform;
            e.renderer = vis.GetComponent<Renderer>();
        }
        else
        {
            e.renderer = own;
        }

        if (e.renderer != null)
        {
            // Beams get their own copy of the cube material with emission switched on, so the
            // shared asset is never edited. The un-proxied firstCube keeps its own material.
            e.opaque = e.visual != null ? LitMaterialFor(e.renderer.sharedMaterial) : e.renderer.sharedMaterial;
            if (e.visual != null) e.renderer.sharedMaterial = e.opaque;
            e.shadowMode = e.renderer.shadowCastingMode;
        }

        e.plank = cube.GetComponent<BridgePlankGuard>() != null;

        entries[cube] = e;
        return e;
    }

    readonly Dictionary<Material, Material> litCopies = new Dictionary<Material, Material>();

    Material LitMaterialFor(Material source)
    {
        if (source == null) return null;
        if (!litCopies.TryGetValue(source, out Material lit))
        {
            lit = new Material(source) { name = source.name + " (Path Lit)" };
            lit.EnableKeyword("_EMISSION");
            lit.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            lit.SetColor(EmissionId, Color.black);
            litCopies[source] = lit;
        }
        return lit;
    }

    Material FadeMaterialFor(Material opaque)
    {
        if (fadeMaterial != null)
        {
            if (!fadeConfigured) { ConfigureAsFade(fadeMaterial); fadeConfigured = true; }
            return fadeMaterial;
        }
        if (runtimeFade == null && opaque != null)
        {
            runtimeFade = new Material(opaque) { name = opaque.name + " (Path Fade)" };
            ConfigureAsFade(runtimeFade);
        }
        return runtimeFade != null ? runtimeFade : opaque;
    }

    Material ghostMat;

    // The fade material with depth writing off: a see-through crossed leg must not hide the
    // track drawn behind it, which is the whole point of making it see-through.
    Material GhostMaterialFor(Material opaque)
    {
        if (ghostMat == null)
        {
            Material fade = FadeMaterialFor(opaque);
            if (fade == null) return opaque;
            ghostMat = new Material(fade) { name = fade.name + " (Ghost)" };
            ghostMat.SetFloat("_ZWrite", 0f);
            ghostMat.renderQueue = (int)RenderQueue.Transparent;
        }
        return ghostMat;
    }

    /// <summary>Turns a URP Lit material into its alpha-blended transparent form.</summary>
    public static void ConfigureAsFade(Material m)
    {
        m.SetFloat("_Surface", 1f);   // Transparent
        m.SetFloat("_Blend", 0f);     // Alpha
        m.SetFloat("_AlphaClip", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        // Depth ON. These are 1-unit beams that only ever fade to ~65%, so writing depth costs
        // nothing visible, and it makes a barely-transparent cube look the same as an opaque one -
        // no pop when it changes material - and stops beams sorting wrongly against each other.
        m.SetFloat("_ZWrite", 1f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.EnableKeyword("_EMISSION"); // route colour self-light, set per cube
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    // Cubes in route order: firstCube, then every segment of every live zone.
    void BuildRoute()
    {
        route.Clear();
        routeLeg.Clear();
        routeBoost.Clear();
        routeIndex.Clear();
        extraCube.Clear();
        extraPos.Clear();
        extraLeg.Clear();

        // A leg is one Segment. The start cube sits at the head of the first leg.
        int leg = 0;
        bool firstSeg = true;
        if (pathGen.firstCube != null) { routeIndex[pathGen.firstCube.gameObject] = 0; route.Add(pathGen.firstCube.gameObject); routeLeg.Add(0); routeBoost.Add(0); }

        foreach (var zone in pathGen.Zones)
        {
            foreach (var seg in zone.segments)
            {
                if (!firstSeg) leg++;
                firstSeg = false;
                int legStart = route.Count;
                foreach (GameObject cube in seg.cubes)
                {
                    if (cube == null) continue;
                    routeIndex[cube] = route.Count;
                    route.Add(cube);
                    routeLeg.Add(leg);
                    routeBoost.Add(seg.boost ? seg.boostId : 0);
                }
                if (seg.decoyCubes != null)
                    for (int d = 0; d < seg.decoyCubes.Count; d++)
                    {
                        if (seg.decoyCubes[d] == null) continue;
                        extraCube.Add(seg.decoyCubes[d]);
                        extraPos.Add(legStart + d);
                        extraLeg.Add(leg);
                    }
            }
        }

        // The boost the runner is in, and where it starts and ends on the route. It stays active
        // until the runner reaches the ordinary leg after it.
        activeBoost = lightUpBoost ? Mathf.Max(0, pathGen.PlayerBoostId) : 0;
        activeBoostFirst = activeBoostLast = -1;
        if (activeBoost > 0)
        {
            for (int i = 0; i < route.Count; i++)
            {
                if (routeBoost[i] != activeBoost) continue;
                if (activeBoostFirst < 0) activeBoostFirst = i;
                activeBoostLast = i;
            }
        }
    }

    // The rear edge of the drawn track, as a route position: the start of the leg visibleLegsBehind
    // before the runner's. When the runner enters a new leg the target jumps forward by a leg, but
    // the edge only advances at hideCubesPerSecond, so the oldest cubes go one after another
    // rather than a whole leg vanishing at once. Tracked by cube, like the frontier, so culling
    // (which renumbers the route) cannot move it.
    float UpdateRear(float progress)
    {
        int n = route.Count;
        int runnerIdx = Mathf.Clamp(Mathf.RoundToInt(progress), 0, n - 1);
        int minLeg = routeLeg[runnerIdx] - Mathf.Max(0, visibleLegsBehind);

        int target = 0;
        while (target < n - 1 && routeLeg[target] < minLeg) target++;

        // ...and kept from its first cube, so the legs already crossed stay drawn too. Once the
        // boost is over the edge catches up at hideCubesPerSecond, oldest cube first, as usual.
        if (activeBoostFirst >= 0) target = Mathf.Min(target, activeBoostFirst);

        float pos;
        if (rearCube != null && routeIndex.TryGetValue(rearCube, out int ri))
        {
            pos = ri + rearFrac;
            pos = target > pos ? Mathf.MoveTowards(pos, target, hideCubesPerSecond * Time.deltaTime) : target;
        }
        else
        {
            pos = target; // first frame, or the rear cube was culled
        }

        pos = Mathf.Clamp(pos, 0f, n - 1);
        int floor = Mathf.Min(n - 1, Mathf.FloorToInt(pos));
        rearCube = route[floor];
        rearFrac = pos - floor;
        return pos;
    }

    // Sets the runner's trail so it is exactly as long as the drawn track behind them: its
    // lifetime is the arc length from the rear edge to the runner, divided by their speed.
    void UpdateRunnerTrail(float progress, float rear)
    {
        if (!matchRunnerTrailToVisibleTrack || !limitVisibleLegs || pathGen.player == null) return;

        if (runnerTrail == null)
        {
            runnerTrail = pathGen.player.GetComponentInChildren<TrailRenderer>(true);
            if (runnerTrail == null) return;
            runnerTrailTime0 = runnerTrail.time;
        }

        int n = route.Count;
        int from = Mathf.Clamp(Mathf.CeilToInt(rear), 0, n - 1);
        int to = Mathf.Clamp(Mathf.FloorToInt(progress), 0, n - 1);

        float length = 0f;
        for (int i = from; i < to; i++)
            length += Vector3.Distance(route[i].transform.position, route[i + 1].transform.position);
        length += Mathf.Max(0f, (progress - to)) * pathGen.cubeSize;

        float speed = Mathf.Max(0.5f, pathGen.player.forwardSpeed);
        runnerTrail.time = Mathf.Clamp(length / speed, 0.1f, 10f);
    }

    // The far edge of the drawn track, as a route index. It jumps out to the end of the leg
    // three ahead when the runner enters a new leg, but is only allowed to advance at
    // revealCubesPerSecond - so a new leg draws itself in from the runner's end, cube by cube,
    // instead of appearing all at once. Tracked by cube (not by number) so culling behind the
    // runner, which renumbers the route, cannot move it.
    float UpdateFrontier(float progress)
    {
        int n = route.Count;
        int runnerIdx = Mathf.Clamp(Mathf.RoundToInt(progress), 0, n - 1);
        int maxLeg = routeLeg[runnerIdx] + Mathf.Max(1, visibleLegsAhead);

        // A zone door ahead: also show the runway out of it and the legs laid beyond it.
        int afterExit = portalWidensWindow ? pathGen.PortalLegsShownAfterExit : 0;
        if (afterExit > 0)
        {
            var entry = pathGen.PortalEntrySegment;
            GameObject entryCube = entry != null && entry.cubes.Count > 0 ? entry.cubes[0] : null;
            if (entryCube != null && routeIndex.TryGetValue(entryCube, out int entryIdx))
                maxLeg = Mathf.Max(maxLeg, routeLeg[entryIdx] + 1 + afterExit);
        }

        int target = runnerIdx;
        while (target + 1 < n && routeLeg[target + 1] <= maxLeg) target++;

        // A boost under way is drawn to its end, however many legs that is.
        if (activeBoostLast >= 0) target = Mathf.Max(target, activeBoostLast);

        float pos;
        if (frontierCube != null && routeIndex.TryGetValue(frontierCube, out int fi))
        {
            pos = fi + frontierFrac;
            pos = target < pos ? target : Mathf.MoveTowards(pos, target, revealCubesPerSecond * Time.deltaTime);
        }
        else
        {
            pos = target; // first frame, or the frontier cube was culled
        }

        pos = Mathf.Clamp(pos, 0f, n - 1);
        int floor = Mathf.Min(n - 1, Mathf.FloorToInt(pos));
        frontierCube = route[floor];
        frontierFrac = pos - floor;
        return pos;
    }

    // A lit boost cube: as bright as the cube right in front of the runner, in the route's own colour.
    Color BoostLitColor()
    {
        Color c = aheadGradient.Evaluate(0f) * (nearBrightness * boostBrightness);
        c.a = 1f;
        return c;
    }

    static float MaxDelta(Color a, Color b) =>
        Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Max(Mathf.Abs(a.b - b.b), Mathf.Abs(a.a - b.a))));

    // Light and warm where the runner is going next, cooling and deepening further along -
    // through warm midtones rather than grey, so the shift reads as distance, not as dirt.
    static Gradient DefaultAheadGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color32(0xFF, 0xF1, 0xD9, 0xFF), 0.00f), // warm cream - the next move
                new GradientColorKey(new Color32(0xF4, 0xC8, 0xA3, 0xFF), 0.25f), // soft apricot
                new GradientColorKey(new Color32(0xC9, 0x9A, 0xA0, 0xFF), 0.50f), // dusty rose
                new GradientColorKey(new Color32(0x92, 0x9F, 0xC4, 0xFF), 0.75f), // soft periwinkle
                new GradientColorKey(new Color32(0x6E, 0x7E, 0xA8, 0xFF), 1.00f), // cool blue - far track, still clearly visible
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}
