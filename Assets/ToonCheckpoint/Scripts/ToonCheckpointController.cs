using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Toon checkpoint (MainScene prop camera 3.7 checkpoint 3, and the whole run of MainScene prop camera Toon): everything
/// the cartoon look needs besides the look asset itself (Look_Toon, which carries the colours, sky, fog, sun, post and the
/// "toon" flag in the block slot 15.z that the toon shaders switch on along the look wipe).
///
///   Palette      ink, moss, cartoon stone, sunlit lip, hatching, paper grain - constant globals (ToonCommon.hlsl)
///   Toon light   a camera-relative light direction for the rocks, from the same side as the path's lit faces
///   Ink pass     while the toon look is on screen the camera draws with ToonCP_Renderer (the full-screen ink outline),
///                otherwise with its own renderer - no cost outside the checkpoint
///   Clouds       painted clouds over the sky (ToonClouds.shader), a camera child
///   Ground       a meadow disc under the track that the cliffs stand in (ToonGround.shader); kept below the lowest track
///                in view and raised with the route, so the cliffs always rise out of ground
///   Rocks        while in the toon checkpoint the background generator lays grounded cliff columns instead of floating
///                rocks - more of them, closer - and the layout is restored after it. Cells are only ever rebuilt where
///                the camera cannot see them (PathBackgroundGenerator.RebuildOutOfSight), so nothing pops.
/// Purely visual: it never touches gameplay, the camera's framing or any asset.
/// </summary>
[DefaultExecutionOrder(960)]   // after LookTransitionDirector (and DepthCueController)
public class ToonCheckpointController : MonoBehaviour
{
    [System.Serializable]
    public class RockLayout
    {
        [Range(0f, 1f)] public float wallChance = 1f;
        [Range(1, 3)] public int wallsPerCell = 3;
        public Vector2 wallWidth = new Vector2(8f, 19f);
        public Vector2 wallTopRange = new Vector2(-4f, 30f);
        public float wallClearance = 6f;
        [Range(0f, 1f)] public float floatingChance = 0f;
        [Range(0, 4)] public int floatingPerCell = 0;
        [Range(0, 6)] public int debrisPerCell = 0;
        public float futureReach = 80f;
        public float futureDrop = 20f;
        public bool stopStackAtSolidFog = true;
        [Tooltip("How far a column hangs below its top (fewer stacked pieces; the fog hides the end).")]
        public float wallDepth = 110f;
        [Range(1, 8)] public int wallMaxStack = 5;

        public static RockLayout From(PathBackgroundGenerator g) => new RockLayout
        {
            wallChance = g.wallChance, wallsPerCell = g.wallsPerCell, wallWidth = g.wallWidth, wallTopRange = g.wallTopRange,
            wallClearance = g.wallClearance, floatingChance = g.floatingChance, floatingPerCell = g.floatingPerCell,
            debrisPerCell = g.debrisPerCell, futureReach = g.futureReach, futureDrop = g.futureDrop, stopStackAtSolidFog = g.stopStackAtSolidFog, wallDepth = g.wallDepth, wallMaxStack = g.wallMaxStack,
        };

        public void ApplyTo(PathBackgroundGenerator g)
        {
            g.wallChance = wallChance; g.wallsPerCell = wallsPerCell; g.wallWidth = wallWidth; g.wallTopRange = wallTopRange;
            g.wallClearance = wallClearance; g.floatingChance = floatingChance; g.floatingPerCell = floatingPerCell;
            g.debrisPerCell = debrisPerCell; g.futureReach = futureReach; g.futureDrop = futureDrop; g.stopStackAtSolidFog = stopStackAtSolidFog; g.wallDepth = wallDepth; g.wallMaxStack = wallMaxStack;
        }
    }

    [Header("The toon look")]
    [Tooltip("Look_Toon. The toon checkpoint is wherever this look is in force (or being wiped in).")]
    public AtmosphereLook toonLook;
    [Tooltip("The whole run is toon (MainScene prop camera Toon).")]
    public bool alwaysToon;
    [Tooltip("Main menu (MainUi): only push the toon palette globals for the checkpoint band dioramas - no camera, rocks, clouds or fog.")]
    public bool menuOnly;

    [Header("References (found when empty)")]
    public LookTransitionDirector director;
    public PathBackgroundGenerator background;
    public DynamicPathGenerator pathGen;
    public CubeRailRunner runner;
    public CameraParentRotator rig;

    [Header("Palette (sRGB)")]
    public Color ink = new Color32(0x1C, 0x22, 0x2E, 0xFF);
    [Range(0f, 1f)] public float inkStrength = 1f;
    public Color moss = new Color32(0x7C, 0xBF, 0x62, 0xFF);
    public Color mossDark = new Color32(0x3B, 0x85, 0x5E, 0xFF);
    [Range(0f, 1f)] public float mossAmount = 0.9f;
    public Color rockLit = new Color32(0xD8, 0xC4, 0x74, 0xFF);
    public Color rockMid = new Color32(0xA9, 0x9A, 0x5E, 0xFF);
    public Color rockShade = new Color32(0x5C, 0x7E, 0x7A, 0xFF);
    public Color highlight = new Color32(0xF2, 0xDE, 0x9A, 0xFF);
    [Range(0f, 1f)] public float highlightStrength = 0.55f;
    [Range(0f, 1f)] public float hatching = 0.6f;
    [Range(0f, 0.2f)] public float paperGrain = 0.06f;
    [Tooltip("Ink line width on the path, in cube units.")]
    [Range(0.01f, 0.1f)] public float inkWidth = 0.045f;
    [Tooltip("Brightness of the lower, shaded band of the path's side faces.")]
    [Range(0.5f, 1f)] public float sideBand = 0.84f;
    [Tooltip("Hatch strokes per unit on the path (rocks use this times rockHatchScale).")]
    public float hatchDensity = 3.2f;
    [Range(0.05f, 1f)] public float rockHatchScale = 0.3f;

    [Header("Leg line art (ToonLegDesigns)")]
    [Tooltip("Paint the leg designs (18 kinds, fitted to each leg, different per leg and per face).")]
    public bool legDesigns = true;
    [Range(0f, 1f)] public float designInk = 0.8f;
    [Range(0f, 1f)] public float designLight = 0.75f;
    [Range(0f, 1f)] public float designMoss = 0.85f;

    [Header("Guide line over the next leg (DynamicPathGenerator.guideTrailColor)")]
    [Tooltip("Its colour while the toon look is on screen (the scene's own colour comes back after). Alpha = strength.")]
    public Color guideColor = new Color(1f, 0.86f, 0.55f, 0.85f);

    [Header("Toon light (camera relative, for the rocks)")]
    [Tooltip("Light from the camera's right (else its left) - the side the path's lighter side face is on.")]
    public bool litRight = true;
    public float lightUp = 1f, lightSide = 0.8f, lightBack = 0.35f;

    [Header("Ink pass")]
    [Tooltip("Renderer index of ToonCP_Renderer on the pipeline assets (ink outline). -1 = no ink pass.")]
    public int toonRendererIndex = 2;
    [Tooltip("The camera's own renderer index, put back when the toon look leaves the screen.")]
    public int baseRendererIndex = 1;

    [Header("Clouds")]
    public Material cloudMaterial;
    [Tooltip("Cloud drift per degree of camera yaw (screen widths), a little parallax as the camera swings.")]
    public float cloudYawParallax = 0.004f;

    [Header("Ground")]
    [Tooltip("Meadow disc under the track. Off by default: with the 35 degree camera it fills the frame whenever the shot tilts; the cliffs rooted in the green valley mist read as the ground instead.")]
    public bool useGround = false;
    public Material groundMaterial;
    [Tooltip("Disc radius. With this camera the ground under the runner is below the screen: what shows is the stretch beyond it, so the disc sits groundAhead units further along the view and stops before the sky.")]
    public float groundRadius = 42f;
    [Tooltip("Units the disc centre sits beyond the runner, along the camera's flattened forward.")]
    public float groundAhead = 40f;
    [Tooltip("Units below the lowest track within groundReach of the runner.")]
    public float groundBelowLowest = 12f;
    [Tooltip("Never higher than this many units below the runner.")]
    public float groundBelowRunner = 30f;
    public float groundReach = 70f;
    public float groundRiseSpeed = 2.5f;
    public float groundFallSpeed = 30f;

    [Header("Rocks while in the toon checkpoint")]
    public bool restyleRockLayout = true;
    public RockLayout toonRocks = new RockLayout();
    [Tooltip("ChasmFog veil / haze while the toon look is on screen (the scene's own come back after): cartoon cliffs stay vivid.")]
    [Range(0f, 0.9f)] public float toonVeil = 0.1f;
    [Range(0f, 1f)] public float toonHazeMax = 0.5f;
    [Tooltip("Seconds the fog takes to ease between the scene's values and the toon ones.")]
    public float fogEaseSeconds = 1.5f;

    static readonly int InkId = Shader.PropertyToID("_ToonInk");
    static readonly int MossId = Shader.PropertyToID("_ToonMoss");
    static readonly int MossDarkId = Shader.PropertyToID("_ToonMossDark");
    static readonly int RockLitId = Shader.PropertyToID("_ToonRockLit");
    static readonly int RockMidId = Shader.PropertyToID("_ToonRockMid");
    static readonly int RockShadeId = Shader.PropertyToID("_ToonRockShade");
    static readonly int HighlightId = Shader.PropertyToID("_ToonHighlight");
    static readonly int LightDirId = Shader.PropertyToID("_ToonLightDir");
    static readonly int ParamsId = Shader.PropertyToID("_ToonParams");
    static readonly int GroundId = Shader.PropertyToID("_ToonGround");
    static readonly int CloudOffsetId = Shader.PropertyToID("_ToonCloudOffset");
    static readonly int DesignId = Shader.PropertyToID("_ToonDesign");

    RockLayout originalRocks;
    bool rocksToon;
    bool toonOnScreen, rendererToon;
    bool rendererSet;
    Camera cam;
    UniversalAdditionalCameraData camData;
    GameObject clouds, ground;
    MeshRenderer cloudRenderer, groundRenderer;
    float groundY;
    bool groundPlaced;
    ChasmFog chasm;
    float sceneVeil, sceneHazeMax, fogBlend;
    bool fogCaptured;
    Color sceneGuideColor;
    bool guideToon, guideCaptured;
    float nextLowestScan;
    float lowestTrack;

    /// <summary>True while the toon look is in force or being wiped in or out (for tooling).</summary>
    public bool ToonOnScreen => toonOnScreen;
    /// <summary>True while the toon rock layout is in use (for tooling).</summary>
    public bool ToonRockLayout => rocksToon;
    public float GroundY => groundY;

    void Awake()
    {
        if (director == null) director = FindFirstObjectByType<LookTransitionDirector>();
        if (background == null) background = FindFirstObjectByType<PathBackgroundGenerator>();
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (runner == null) runner = FindFirstObjectByType<CubeRailRunner>();
        if (rig == null) rig = FindFirstObjectByType<CameraParentRotator>();
        PushPalette();
        Shader.SetGlobalVector(GroundId, Vector4.zero);
        if (!menuOnly && GetComponent<ToonLegDesigns>() == null) gameObject.AddComponent<ToonLegDesigns>().pathGen = pathGen;
    }

    void Start()
    {
        if (menuOnly) return;
        cam = rig != null && rig.boomCamera != null ? rig.boomCamera : Camera.main;
        if (cam != null) camData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (background != null) originalRocks = RockLayout.From(background);
        BuildClouds();
        BuildGround();
    }

    void OnDisable()
    {
        Shader.SetGlobalVector(GroundId, Vector4.zero);
        if (camData != null && rendererSet && rendererToon) camData.SetRenderer(baseRendererIndex);
        rendererToon = false;
        if (rocksToon && background != null && originalRocks != null) originalRocks.ApplyTo(background);
        rocksToon = false;
        if (guideToon && pathGen != null) pathGen.guideTrailColor = sceneGuideColor;
        if (fogCaptured && chasm != null) { chasm.veil = sceneVeil; chasm.hazeMax = sceneHazeMax; }
        guideToon = false;
    }

    void OnDestroy()
    {
        if (clouds != null) { Destroy(clouds.GetComponent<MeshFilter>().sharedMesh); Destroy(clouds); }
        if (ground != null) { Destroy(ground.GetComponent<MeshFilter>().sharedMesh); Destroy(ground); }
    }

    void OnValidate()
    {
        if (isActiveAndEnabled) PushPalette();
    }

    bool IsToon(AtmosphereLook l) =>
        l != null && toonLook != null && (l == toonLook || (!string.IsNullOrEmpty(toonLook.sourceScene) && l.sourceScene == toonLook.sourceScene));

    void LateUpdate()
    {
        if (menuOnly) { PushPalette(); PushLight(); return; }
        AtmosphereLook now = director != null ? director.CurrentLook : null;
        AtmosphereLook next = director != null ? director.NextLook : null;
        toonOnScreen = alwaysToon || IsToon(now) || IsToon(next);
        bool toonZone = alwaysToon || (next != null ? IsToon(next) : IsToon(now));

        PushPalette();
        PushLight();
        UpdateRenderer();
        UpdateGuide();
        UpdateRocks(toonZone);
        UpdateClouds();
        UpdateGround();
        UpdateFog();
    }

    void UpdateFog()
    {
        if (chasm == null) chasm = ChasmFog.Active;
        if (chasm == null) return;
        if (!fogCaptured) { sceneVeil = chasm.veil; sceneHazeMax = chasm.hazeMax; fogCaptured = true; fogBlend = -1f; }
        float target = toonOnScreen ? 1f : 0f;
        if (fogBlend == target) return;                          // settled: leave the scene's fog alone
        fogBlend = fogBlend < 0f ? target : Mathf.MoveTowards(fogBlend, target, Time.deltaTime / Mathf.Max(0.01f, fogEaseSeconds));
        float k = fogBlend * fogBlend * (3f - 2f * fogBlend);
        chasm.veil = Mathf.Lerp(sceneVeil, toonVeil, k);
        chasm.hazeMax = Mathf.Lerp(sceneHazeMax, toonHazeMax, k);
    }

    // ------------------------------------------------------------------ globals

    static Vector4 Lin(Color c, float a) { Color l = c.linear; return new Vector4(l.r, l.g, l.b, a); }

    void PushPalette()
    {
        Shader.SetGlobalVector(InkId, Lin(ink, inkStrength));
        Shader.SetGlobalVector(MossId, Lin(moss, mossAmount));
        Shader.SetGlobalVector(MossDarkId, Lin(mossDark, hatching));
        Shader.SetGlobalVector(RockLitId, Lin(rockLit, 1f));
        Shader.SetGlobalVector(RockMidId, Lin(rockMid, 1f));
        Shader.SetGlobalVector(RockShadeId, Lin(rockShade, 1f));
        Shader.SetGlobalVector(HighlightId, Lin(highlight, highlightStrength));
        Shader.SetGlobalVector(ParamsId, new Vector4(inkWidth, sideBand, hatchDensity, rockHatchScale));
        Shader.SetGlobalVector(DesignId, new Vector4(legDesigns ? 1f : 0f, designInk, designLight, designMoss));
    }

    void PushLight()
    {
        Vector3 r = Vector3.right, f = Vector3.forward;
        if (cam != null)
        {
            r = cam.transform.right; r.y = 0f;
            f = cam.transform.forward; f.y = 0f;
            if (r.sqrMagnitude < 1e-4f) r = Vector3.right;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            r.Normalize(); f.Normalize();
        }
        Vector3 l = (Vector3.up * lightUp + r * (litRight ? lightSide : -lightSide) - f * lightBack).normalized;
        Shader.SetGlobalVector(LightDirId, new Vector4(l.x, l.y, l.z, paperGrain));
    }

    // ------------------------------------------------------------------ ink pass

    void UpdateRenderer()
    {
        if (camData == null || toonRendererIndex < 0) return;
        bool want = toonOnScreen;
        if (rendererSet && want == rendererToon) return;
        camData.SetRenderer(want ? toonRendererIndex : baseRendererIndex);
        rendererToon = want;
        rendererSet = true;
    }

    // ------------------------------------------------------------------ guide line

    void UpdateGuide()
    {
        if (pathGen == null) return;
        if (!guideCaptured) { sceneGuideColor = pathGen.guideTrailColor; guideCaptured = true; }
        if (toonOnScreen == guideToon) return;
        guideToon = toonOnScreen;
        pathGen.guideTrailColor = toonOnScreen ? guideColor : sceneGuideColor;
    }

    // ------------------------------------------------------------------ rocks

    void UpdateRocks(bool toonZone)
    {
        if (!restyleRockLayout || background == null || originalRocks == null) return;
        if (toonZone == rocksToon) return;
        rocksToon = toonZone;
        (toonZone ? toonRocks : originalRocks).ApplyTo(background);
        background.RebuildOutOfSight();
    }

    // ------------------------------------------------------------------ clouds

    static Mesh Quad(string name, bool horizontal)
    {
        var m = new Mesh { name = name };
        if (horizontal)
            m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
        else
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateNormals();
        return m;
    }

    void BuildClouds()
    {
        if (cloudMaterial == null || cam == null) return;
        clouds = new GameObject("ToonClouds");
        clouds.transform.SetParent(cam.transform, false);
        clouds.transform.localPosition = new Vector3(0f, 0f, Mathf.Min(cam.farClipPlane * 0.5f, 200f));
        Mesh m = Quad("ToonCloudsQuad", false);
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);   // the shader places it on screen: never cull it
        clouds.AddComponent<MeshFilter>().sharedMesh = m;
        cloudRenderer = clouds.AddComponent<MeshRenderer>();
        cloudRenderer.sharedMaterial = cloudMaterial;
        cloudRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cloudRenderer.receiveShadows = false;
        cloudRenderer.enabled = false;
    }

    void UpdateClouds()
    {
        if (cloudRenderer == null) return;
        cloudRenderer.enabled = toonOnScreen;
        if (cam != null) Shader.SetGlobalVector(CloudOffsetId, new Vector4(-cam.transform.eulerAngles.y * cloudYawParallax, 0f, 0f, 0f));
    }

    // ------------------------------------------------------------------ ground

    void BuildGround()
    {
        if (groundMaterial == null || !useGround) return;
        ground = new GameObject("ToonGround");
        ground.transform.SetParent(transform, false);
        ground.AddComponent<MeshFilter>().sharedMesh = Quad("ToonGroundQuad", true);
        groundRenderer = ground.AddComponent<MeshRenderer>();
        groundRenderer.sharedMaterial = groundMaterial;
        groundRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        groundRenderer.receiveShadows = true;
        groundRenderer.enabled = false;
    }

    // Lowest path cube near the runner (laid track only; scanned a few times a second, no allocation).
    float LowestTrack(Vector3 at)
    {
        float low = at.y;
        if (pathGen == null) return low;
        float reach2 = groundReach * groundReach;
        foreach (var zone in pathGen.Zones)
            foreach (var seg in zone.segments)
                foreach (var cube in seg.cubes)
                {
                    if (cube == null) continue;
                    Vector3 p = cube.transform.position;
                    float dx = p.x - at.x, dz = p.z - at.z;
                    if (dx * dx + dz * dz <= reach2 && p.y < low) low = p.y;
                }
        return low;
    }

    void UpdateGround()
    {
        if (groundRenderer == null || runner == null)
        {
            Shader.SetGlobalVector(GroundId, Vector4.zero);
            return;
        }
        Vector3 at = runner.transform.position;
        if (Time.time >= nextLowestScan || !groundPlaced)
        {
            lowestTrack = LowestTrack(at);
            nextLowestScan = Time.time + 0.2f;
        }
        float target = Mathf.Min(lowestTrack - groundBelowLowest, at.y - groundBelowRunner);
        // A teleport (portal, restart) or the toon look not on screen yet: go straight there.
        if (!groundPlaced || !toonOnScreen || Mathf.Abs(target - groundY) > 60f) groundY = target;
        else groundY = Mathf.MoveTowards(groundY, target, (target < groundY ? groundFallSpeed : groundRiseSpeed) * Time.deltaTime);
        groundPlaced = true;

        Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        Vector3 centre = at + fwd.normalized * groundAhead;
        centre.y = groundY;
        // Drawn a little larger than its fade radius so the rim never shows a hard edge.
        ground.transform.SetPositionAndRotation(centre, Quaternion.identity);
        ground.transform.localScale = new Vector3(groundRadius * 2.2f, 1f, groundRadius * 2.2f);
        groundRenderer.enabled = toonOnScreen;
        Shader.SetGlobalVector(GroundId, toonOnScreen ? new Vector4(centre.x, groundY, centre.z, groundRadius) : Vector4.zero);
    }
}
