using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Spice Route checkpoint (MainScene prop camera 3.10 checkpoint 10, the last level, right after Rogue Planet; and the whole
/// run of MainScene prop camera SpiceRoute): everything the painted-paper India / Arabia look needs besides the look asset
/// (Look_SpiceRoute: colours, sky, fog, sun, post). The level is split in two: its first half is India, its second the
/// Arabian / Ottoman world (endless legs after it keep alternating; the SpiceRoute scene alternates every cycleLegs).
///
///   Where      _SrLook (x: the look in force is spice, y: the look being wiped in is, z: always) - every spice shader
///              (SpiceCommon.hlsl) switches along the look wipe with it
///   Halves     _SrHalf (first leg of the level, legs per half): each leg picks India / Arabia from its own serial, so the
///              path ahead already shows the next half; the sky, fog, coins and runner follow the runner's half through a
///              torn-paper sweep (_SrTear, tearSeconds)
///   Paint      while the look is on screen the camera draws with SpiceCP_Renderer: the paper pass (pigment blotches, fibres,
///              distressed specks, warm vignette); the space oil pass rides along while Rogue Planet is still on screen
///   Backdrop   SpiceBackdrop: a Vishvarupa night sky with a colossus, hill forts, temple peaks, peacocks and kites; then a
///              Cairo / Istanbul dusk with a big moon, three skylines, flying carpets
///   Cliffs     tall single-piece sheer cliffs (never stacked: wallMaxStack 1 while in the level), far from the path; each
///              top dressed when the rock is built (out of sight) for the half of the nearest leg: Nagara temples, gopurams,
///              Rajput palaces, chhatris, shrines, elephants, peacocks, palms, banyans, bunting and Ganesha colossi on
///              pedestal cliffs - or Ottoman mosques, onion-dome palaces, sandstone towers, houses with lit windows,
///              cypresses, date palms, lanterns. One merged mesh per top.
///   Runner     the eye block and its cape swapped for SpiceRouteCheckpoint/Runner + Cape (designs and silk follow the half)
///   Coins      the coin's runtime materials tinted: gold with saffron (India) / turquoise (Arabia) enamel; put back on leave
/// Purely visual: it never touches gameplay or the camera's framing; everything it changes is put back on disable.
/// </summary>
[DefaultExecutionOrder(1020)]   // after the space / pencil / voxel / flower / storybook / pixel / toon controllers
public class SpiceRouteCheckpointController : MonoBehaviour
{
    [Header("The spice look")]
    [Tooltip("Look_SpiceRoute. The checkpoint is wherever this look is in force (or being wiped in).")]
    public AtmosphereLook spiceLook;
    [Tooltip("The whole run is the Spice Route (MainScene prop camera SpiceRoute).")]
    public bool alwaysSpice;
    [Tooltip("Portal whose runway out starts the level. 3.10: 9.")]
    public int spicePortal = 9;
    [Tooltip("Legs per half when the whole run is the Spice Route (India, Arabia, India ...).")]
    [Min(4)] public int cycleLegs = 20;

    [Header("References (found when empty)")]
    public LookTransitionDirector director;
    public PathBackgroundGenerator background;
    public DynamicPathGenerator pathGen;
    public CubeRailRunner runner;
    public CameraParentRotator rig;
    public SpaceCheckpointController space;
    public PencilCheckpointController pencil;
    public VoxelCheckpointController voxel;
    public FlowerCheckpointController flower;
    public StorybookCheckpointController story;
    public PixelCheckpointController pixel;

    [Header("Paint")]
    [Range(0f, 1f)] public float goldGlint = 1f;
    [Range(0f, 2f)] public float windowGlowIndia = 0.6f;
    [Range(0f, 2f)] public float windowGlowArabia = 1.1f;
    [Range(0f, 0.3f)] public float paperGrain = 0.07f;

    [Header("Light (camera relative)")]
    public bool litRight = false;
    public float lightUp = 1f, lightSide = 0.8f, lightBack = 0.45f;
    [Range(0.1f, 3f)] public float rockBrushScale = 0.5f;

    [Header("Halves")]
    [Tooltip("Seconds the torn-paper sweep takes to turn the sky (and fog, coins, runner) to the other half.")]
    public float tearSeconds = 3.5f;

    [Header("Renderer")]
    [Tooltip("Renderer index of SpiceCP_Renderer on the pipeline assets. -1 = no paper pass.")]
    public int spiceRendererIndex = 8;
    [Tooltip("The camera's own renderer index, put back when the look leaves the screen.")]
    public int baseRendererIndex = 1;
    [Tooltip("SpiceCP_Renderer: its space oil feature is only switched on while Rogue Planet is on screen too.")]
    public UniversalRendererData spiceRenderer;

    [Header("Guide line over the next leg (DynamicPathGenerator.guideTrailColor)")]
    public Color guideColor = new Color(1f, 0.9f, 0.62f, 0.9f);

    [Header("Backdrop")]
    public Material backdropMaterial;
    public float yawParallax = 0.003f;
    public float travelParallax = 0.004f;

    [Header("Cliffs while in the level: tall single pieces, far out")]
    public bool restyleRockLayout = true;
    public PixelCheckpointController.RockLayout spiceRocks = new PixelCheckpointController.RockLayout
    {
        wallChance = 0.9f, wallsPerCell = 2, wallWidth = new Vector2(26f, 42f), wallTopRange = new Vector2(-4f, 12f),
        wallClearance = 40f, floatingChance = 0f, floatingPerCell = 0, floatingWidth = new Vector2(3f, 6f),
        floatingHeightRange = new Vector2(-20f, -8f), floatingClearance = 6f, debrisPerCell = 0, futureReach = 80f, futureDrop = 24f,
    };
    [Tooltip("Pieces per cliff column while in the level (1 = one tall cliff, never a stack).")]
    [Range(1, 8)] public int wallMaxStack = 1;
    [Range(0f, 0.9f)] public float spiceVeil = 0.02f;
    [Range(0f, 1f)] public float spiceHazeMax = 0.25f;
    public float fogEaseSeconds = 1.5f;
    public bool ownCliffs = true;
    [Tooltip("SpiceRouteCheckpoint/Prop - the cliffs and everything on them.")]
    public Material propMaterial;

    [Header("Chasm colours per half (sRGB)")]
    public Color indiaTop = new Color32(0xF0, 0xB8, 0x8A, 0xFF);
    public Color indiaBottom = new Color32(0x7A, 0x3A, 0x62, 0xFF);
    public Color indiaGlow = new Color32(0xFF, 0xD4, 0x9A, 0xFF);
    public Color arabTop = new Color32(0xE8, 0xA0, 0x7A, 0xFF);
    public Color arabBottom = new Color32(0x3A, 0x2A, 0x5E, 0xFF);
    public Color arabGlow = new Color32(0xFF, 0xB0, 0x70, 0xFF);

    [Header("Dressing on the cliff tops")]
    public bool dressRocks = true;
    [Tooltip("World size multiplier of the buildings, trees and animals.")]
    public float propScale = 1.9f;
    [Tooltip("Ganesha colossus height as a share of its cliff's width.")]
    public float statueSize = 0.95f;
    [Tooltip("A mosque / palace on a pedestal cliff, as a share of the cliff's width.")]
    public float landmarkScale = 0.11f;
    [Range(0, 8)] public int treesMin = 3, treesMax = 6;
    [Range(0, 12)] public int housesMin = 5, housesMax = 9;
    public float sink = 0.05f;

    [Header("Runner (swapped while the look is in force)")]
    public Material runnerBody, runnerCape;

    [Header("Coins (sRGB)")]
    public Color coinRimIndia = new Color32(0xF6, 0xBE, 0x3A, 0xFF);
    public Color coinFaceIndia = new Color32(0xF2, 0x8A, 0x1E, 0xFF);
    public Color coinRimArabia = new Color32(0xF0, 0xC8, 0x60, 0xFF);
    public Color coinFaceArabia = new Color32(0x22, 0xA8, 0xB4, 0xFF);

    static readonly int LookId = Shader.PropertyToID("_SrLook");
    static readonly int HalfId = Shader.PropertyToID("_SrHalf");
    static readonly int TearId = Shader.PropertyToID("_SrTear");
    static readonly int ParamsId = Shader.PropertyToID("_SrParams");
    static readonly int LightDirId = Shader.PropertyToID("_SrLightDir");
    static readonly int BackdropId = Shader.PropertyToID("_SrBackdrop");
    static readonly int WipeId = Shader.PropertyToID("_LTWipe");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    const string OilFeature = "SpaceOil";

    PixelCheckpointController.RockLayout originalRocks;
    int originalMaxStack;
    bool rocksSr, srOnScreen, rendererSr;
    Camera cam;
    UniversalAdditionalCameraData camData;
    GameObject backdrop;
    MeshRenderer backdropRenderer;
    ChasmFog chasm;
    float sceneVeil, sceneHazeMax, fogBlend = -1f, entryVeil, entryHaze;
    bool fogCaptured, chasmTinted;
    Color sceneGuideColor;
    bool guideCaptured, guideSr;
    ScriptableRendererFeature oilFeature;
    bool oilOn = true;

    // Halves.
    int tearFrom, tearTo;
    float tearT = 1f;
    bool tearStarted;
    float envHalf;

    // Runner.
    readonly List<Renderer> runnerRenderers = new List<Renderer>();
    readonly List<Material> runnerOriginals = new List<Material>();
    readonly List<Material> runnerSpice = new List<Material>();
    bool runnerSwapped;

    // Coins.
    Material[] coinMats;
    Color[] coinBase, coinEmission;
    bool[] coinFace;
    bool coinTinted;
    float coinApplied = -1f;

    // Cliffs and props.
    GameObject[] cliffTemplates, sceneWallPrefabs;
    SpiceProps.Cliff[] cliffs;
    Mesh[] arabCliffMeshes;
    GameObject cliffRoot;
    bool cliffsOn;
    readonly List<Mesh> madeMeshes = new List<Mesh>();
    struct Prop { public SpiceMesh kit; public float radius; }
    Prop[] ganeshas, temples, palaces, gopurams, chhatris, shrines, elephants, peacocks, palms, banyans, buntings;
    Prop[] mosques, onionPalaces, houses, towers, cypresses, datePalms, lanterns;
    readonly List<Vector3> candidates = new List<Vector3>();
    readonly List<Vector3> placed = new List<Vector3>();
    readonly List<float> placedR = new List<float>();

    public bool SpiceOnScreen => srOnScreen;
    public bool SpiceRockLayout => rocksSr;
    /// <summary>0 = India .. 1 = Arabia, as the sky shows it now (eased through the sweep).</summary>
    public float EnvironmentHalf => envHalf;

    void Awake()
    {
        if (director == null) director = FindFirstObjectByType<LookTransitionDirector>();
        if (background == null) background = FindFirstObjectByType<PathBackgroundGenerator>();
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (runner == null) runner = FindFirstObjectByType<CubeRailRunner>();
        if (rig == null) rig = FindFirstObjectByType<CameraParentRotator>();
        if (space == null) space = FindFirstObjectByType<SpaceCheckpointController>();
        if (pencil == null) pencil = FindFirstObjectByType<PencilCheckpointController>();
        if (voxel == null) voxel = FindFirstObjectByType<VoxelCheckpointController>();
        if (flower == null) flower = FindFirstObjectByType<FlowerCheckpointController>();
        if (story == null) story = FindFirstObjectByType<StorybookCheckpointController>();
        if (pixel == null) pixel = FindFirstObjectByType<PixelCheckpointController>();
        Shader.SetGlobalVector(LookId, Vector4.zero);
        Shader.SetGlobalVector(TearId, new Vector4(1f, 0f, 0f, 1f));
        PushParams();
        if (spiceRenderer != null)
            foreach (ScriptableRendererFeature f in spiceRenderer.rendererFeatures)
                if (f != null && f.name == OilFeature) oilFeature = f;
        SetOil(false);
    }

    void Start()
    {
        cam = rig != null && rig.boomCamera != null ? rig.boomCamera : Camera.main;
        if (cam != null) camData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (background != null) { originalRocks = PixelCheckpointController.RockLayout.From(background); originalMaxStack = background.wallMaxStack; }
        CollectRunner();
        BuildBackdrop();
        BuildPropKits();
        BuildCliffs();
    }

    void OnDisable()
    {
        Shader.SetGlobalVector(LookId, Vector4.zero);
        if (camData != null && rendererSr) camData.SetRenderer(baseRendererIndex);
        rendererSr = false;
        SetOil(false);
        SwapRunner(false);
        RestoreCoins();
        if (background != null) background.RockTopBuilt -= DressRock;
        if (rocksSr && background != null && originalRocks != null) { originalRocks.ApplyTo(background); background.wallMaxStack = originalMaxStack; }
        if (cliffsOn && background != null) background.wallPrefabs = sceneWallPrefabs;
        cliffsOn = false;
        rocksSr = false;
        if (guideSr && pathGen != null) pathGen.guideTrailColor = sceneGuideColor;
        guideSr = false;
        if (fogCaptured && chasm != null) { chasm.veil = sceneVeil; chasm.hazeMax = sceneHazeMax; }
        RestoreChasm();
    }

    void OnDestroy()
    {
        if (backdrop != null) { Destroy(backdrop.GetComponent<MeshFilter>().sharedMesh); Destroy(backdrop); }
        foreach (Mesh m in madeMeshes) if (m != null) Destroy(m);
        if (cliffRoot != null) Destroy(cliffRoot);
    }

    bool IsSpice(AtmosphereLook l) =>
        l != null && spiceLook != null && (l == spiceLook || (!string.IsNullOrEmpty(spiceLook.sourceScene) && l.sourceScene == spiceLook.sourceScene));

    void LateUpdate()
    {
        AtmosphereLook now = director != null ? director.CurrentLook : null;
        AtmosphereLook next = director != null ? director.NextLook : null;
        bool a = IsSpice(now), b = IsSpice(next);
        srOnScreen = alwaysSpice || a || b;
        bool inForce = alwaysSpice || (a && next == null);
        bool zone = alwaysSpice || (next != null ? b : a);
        Shader.SetGlobalVector(LookId, new Vector4(a ? 1f : 0f, b ? 1f : 0f, alwaysSpice ? 1f : 0f, 0f));

        UpdateHalves(srOnScreen);
        PushParams();
        PushLight();
        UpdateRenderer();
        UpdateGuide();
        UpdateRocks(zone);
        UpdateBackdrop();
        UpdateFog(inForce);
        UpdateCoins(srOnScreen);
        // The runner turns once the wipe is past it (the look is in force), and back when it leaves.
        bool past = next != null && WipePastRunner();
        SwapRunner(alwaysSpice || (a && (next == null || b || !past)) || (b && past));
    }

    // ------------------------------------------------------------------ halves

    int LevelFirst => alwaysSpice || pathGen == null ? 0 : pathGen.CheckpointLeg(spicePortal);

    int HalfLen
    {
        get
        {
            if (alwaysSpice || pathGen == null) return Mathf.Max(4, cycleLegs);
            int first = pathGen.CheckpointLeg(spicePortal), end = pathGen.CheckpointLeg(spicePortal + 1);
            return Mathf.Max(4, (end - first) / 2);
        }
    }

    int HalfOf(int serial) => (Mathf.Max(0, Mathf.FloorToInt((serial - LevelFirst) / (float)HalfLen)) & 1);

    void UpdateHalves(bool on)
    {
        Shader.SetGlobalVector(HalfId, new Vector4(LevelFirst, HalfLen, 0f, 0f));
        int now = pathGen != null ? HalfOf(pathGen.Score) : 0;
        if (!on || !tearStarted)
        {
            // Off screen (or the first frame on): no sweep, the sky simply is the runner's half.
            tearFrom = tearTo = now;
            tearT = 1f;
            tearStarted = on;
        }
        else if (now != tearTo)
        {
            tearFrom = tearT >= 1f ? tearTo : (tearT < 0.5f ? tearFrom : tearTo);
            tearTo = now;
            tearT = tearFrom == tearTo ? 1f : 0f;
        }
        else if (tearT < 1f)
        {
            tearT = Mathf.MoveTowards(tearT, 1f, Time.deltaTime / Mathf.Max(0.1f, tearSeconds));
        }
        float eased = tearT * tearT * (3f - 2f * tearT);
        envHalf = Mathf.Lerp(tearFrom, tearTo, eased);
        Shader.SetGlobalVector(TearId, new Vector4(tearT, tearFrom, tearTo, eased));
    }

    // ------------------------------------------------------------------ globals

    void PushParams()
    {
        Shader.SetGlobalVector(ParamsId, new Vector4(goldGlint, Mathf.Lerp(windowGlowIndia, windowGlowArabia, envHalf), paperGrain, 1f));
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 1e-4f ? Vector3.right : v.normalized;
    }

    void PushLight()
    {
        Vector3 r = cam != null ? Flat(cam.transform.right) : Vector3.right;
        Vector3 f = cam != null ? Flat(cam.transform.forward) : Vector3.forward;
        Vector3 l = (Vector3.up * lightUp + r * (litRight ? lightSide : -lightSide) - f * lightBack).normalized;
        Shader.SetGlobalVector(LightDirId, new Vector4(l.x, l.y, l.z, rockBrushScale));
    }

    // ------------------------------------------------------------------ renderer + guide

    void SetOil(bool on)
    {
        if (oilOn == on) return;
        oilOn = on;
        if (oilFeature != null) oilFeature.SetActive(on);
    }

    bool SpaceVisible => space != null && space.isActiveAndEnabled && space.SpaceOnScreen;

    void UpdateRenderer()
    {
        if (camData == null || spiceRendererIndex < 0) return;
        if (srOnScreen)
        {
            camData.SetRenderer(spiceRendererIndex);   // every frame: the other checkpoint controllers run earlier
            rendererSr = true;
            SetOil(SpaceVisible);
        }
        else if (rendererSr)
        {
            camData.SetRenderer(baseRendererIndex);
            rendererSr = false;
            SetOil(false);
        }
    }

    void UpdateGuide()
    {
        if (pathGen == null) return;
        if (!guideCaptured) { sceneGuideColor = pathGen.guideTrailColor; guideCaptured = true; }
        if (srOnScreen)
        {
            pathGen.guideTrailColor = guideColor;
            guideSr = true;
        }
        else if (guideSr)
        {
            pathGen.guideTrailColor = SpaceVisible ? space.guideColor : sceneGuideColor;
            guideSr = false;
        }
    }

    // ------------------------------------------------------------------ runner

    void CollectRunner()
    {
        var totem = FindFirstObjectByType<PlayerTotem>(FindObjectsInactive.Include);
        if (totem == null) return;
        foreach (Renderer r in totem.GetComponentsInChildren<Renderer>(true))
        {
            Material swap = r.name == "Cape" ? runnerCape : r == totem.body ? runnerBody : null;
            if (swap == null || r.sharedMaterial == null) continue;
            runnerRenderers.Add(r);
            runnerOriginals.Add(r.sharedMaterial);
            runnerSpice.Add(swap);
        }
    }

    bool WipePastRunner()
    {
        // The wipe runs left to right; the new look is left of its edge (_LTWipe.x, screen widths).
        if (cam == null || runner == null) return true;
        float rx = cam.WorldToViewportPoint(runner.transform.position).x;
        return Shader.GetGlobalVector(WipeId).x > rx;
    }

    void SwapRunner(bool on)
    {
        if (on == runnerSwapped) return;
        runnerSwapped = on;
        for (int i = 0; i < runnerRenderers.Count; i++)
            if (runnerRenderers[i] != null) runnerRenderers[i].sharedMaterial = on ? runnerSpice[i] : runnerOriginals[i];
    }

    // ------------------------------------------------------------------ coins

    void UpdateCoins(bool on)
    {
        Material[] mats = pathGen != null ? pathGen.CoinRuntimeMaterials : null;
        if (mats == null) return;
        if (mats != coinMats)
        {
            coinMats = mats;
            coinBase = new Color[mats.Length];
            coinEmission = new Color[mats.Length];
            coinFace = new bool[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                coinBase[i] = mats[i].HasProperty(BaseColorId) ? mats[i].GetColor(BaseColorId) : Color.white;
                coinEmission[i] = mats[i].HasProperty(EmissionId) ? mats[i].GetColor(EmissionId) : Color.black;
                coinFace[i] = mats[i].name.Contains("Face");
            }
            coinTinted = false;
            coinApplied = -1f;
        }
        if (!on) { RestoreCoins(); return; }
        if (coinTinted && Mathf.Abs(coinApplied - envHalf) < 0.002f) return;
        coinApplied = envHalf;
        coinTinted = true;
        float glow = pathGen.coinGlow;
        for (int i = 0; i < coinMats.Length; i++)
        {
            Material m = coinMats[i];
            if (m == null) continue;
            Color c = coinFace[i] ? Color.Lerp(coinFaceIndia, coinFaceArabia, envHalf) : Color.Lerp(coinRimIndia, coinRimArabia, envHalf);
            c = c.linear;
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            if (m.HasProperty(EmissionId)) m.SetColor(EmissionId, new Color(c.r, c.g, c.b, 1f) * glow);
        }
    }

    void RestoreCoins()
    {
        if (!coinTinted || coinMats == null) return;
        coinTinted = false;
        for (int i = 0; i < coinMats.Length; i++)
        {
            Material m = coinMats[i];
            if (m == null) continue;
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, coinBase[i]);
            if (m.HasProperty(EmissionId)) m.SetColor(EmissionId, coinEmission[i]);
        }
    }

    // ------------------------------------------------------------------ rocks + fog

    void UpdateRocks(bool zone)
    {
        if (background == null || originalRocks == null) return;
        if (zone == rocksSr) return;
        rocksSr = zone;
        background.RockTopBuilt -= DressRock;
        if (zone && dressRocks) background.RockTopBuilt += DressRock;
        if (restyleRockLayout)
        {
            // Leaving: the scene's layout, unless an earlier-running checkpoint already put its own this frame.
            if (zone) { spiceRocks.ApplyTo(background); background.wallMaxStack = wallMaxStack; }
            else
            {
                background.wallMaxStack = originalMaxStack;
                if ((space == null || !space.SpaceRockLayout) && (pencil == null || !pencil.PencilRockLayout) && (voxel == null || !voxel.VoxelRockLayout)
                    && (flower == null || !flower.FlowerRockLayout) && (story == null || !story.StoryRockLayout) && (pixel == null || !pixel.PixelRockLayout))
                    originalRocks.ApplyTo(background);
            }
        }
        if (ownCliffs && cliffTemplates != null)
        {
            if (zone) { sceneWallPrefabs = background.wallPrefabs; background.wallPrefabs = cliffTemplates; cliffsOn = true; }
            else if (cliffsOn) { background.wallPrefabs = sceneWallPrefabs; cliffsOn = false; }
        }
        background.RebuildOutOfSight();
    }

    void UpdateFog(bool inForce)
    {
        if (chasm == null) chasm = ChasmFog.Active;
        if (chasm == null) return;
        if (!fogCaptured) { sceneVeil = chasm.veil; sceneHazeMax = chasm.hazeMax; fogCaptured = true; }
        float target = srOnScreen ? 1f : 0f;
        if (!(fogBlend == target && !srOnScreen))
        {
            if (fogBlend <= 0f && srOnScreen) { entryVeil = chasm.veil; entryHaze = chasm.hazeMax; }
            fogBlend = fogBlend < 0f ? target : Mathf.MoveTowards(fogBlend, target, Time.deltaTime / Mathf.Max(0.01f, fogEaseSeconds));
            float k = fogBlend * fogBlend * (3f - 2f * fogBlend);
            chasm.veil = Mathf.Lerp(srOnScreen ? entryVeil : sceneVeil, spiceVeil, k);
            chasm.hazeMax = Mathf.Lerp(srOnScreen ? entryHaze : sceneHazeMax, spiceHazeMax, k);
        }
        // The chasm's colours follow the half while the look is in force (the director only writes them during a wipe).
        if (inForce)
        {
            chasm.topColor = Color.Lerp(indiaTop, arabTop, envHalf);
            chasm.bottomColor = Color.Lerp(indiaBottom, arabBottom, envHalf);
            chasm.glowColor = Color.Lerp(indiaGlow, arabGlow, envHalf);
            chasmTinted = true;
        }
        else RestoreChasm();
    }

    void RestoreChasm()
    {
        if (!chasmTinted || chasm == null || spiceLook == null) return;
        chasmTinted = false;
        chasm.topColor = spiceLook.chasmTop;
        chasm.bottomColor = spiceLook.chasmBottom;
        chasm.glowColor = spiceLook.chasmGlow;
    }

    // ------------------------------------------------------------------ backdrop

    void BuildBackdrop()
    {
        if (backdropMaterial == null || cam == null) return;
        backdrop = new GameObject("SpiceBackdrop");
        backdrop.transform.SetParent(cam.transform, false);
        backdrop.transform.localPosition = new Vector3(0f, 0f, Mathf.Min(cam.farClipPlane * 0.5f, 200f));
        var m = new Mesh { name = "SpiceBackdropQuad" };
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        backdrop.AddComponent<MeshFilter>().sharedMesh = m;
        backdropRenderer = backdrop.AddComponent<MeshRenderer>();
        backdropRenderer.sharedMaterial = backdropMaterial;
        backdropRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        backdropRenderer.receiveShadows = false;
        backdropRenderer.enabled = false;
    }

    void UpdateBackdrop()
    {
        if (backdropRenderer == null) return;
        backdropRenderer.enabled = srOnScreen;
        if (!srOnScreen) return;
        float yaw = cam.transform.eulerAngles.y;
        Vector3 cp = cam.transform.position;
        float tx = Mathf.Repeat(Vector3.Dot(cp, Flat(cam.transform.right)) * travelParallax, 1000f);
        float ty = Mathf.Repeat(cp.y * travelParallax, 1000f);
        Shader.SetGlobalVector(BackdropId, new Vector4(-yaw * yawParallax, Time.time, tx, ty));
    }

    // ------------------------------------------------------------------ cliffs

    Mesh Keep(Mesh m) { madeMeshes.Add(m); return m; }

    void BuildCliffs()
    {
        if (!ownCliffs || background == null || propMaterial == null) return;
        cliffRoot = new GameObject("SpiceCliffTemplates");
        cliffRoot.SetActive(false);
        cliffRoot.transform.SetParent(transform, false);
        const int count = 6;
        cliffTemplates = new GameObject[count];
        cliffs = new SpiceProps.Cliff[count];
        arabCliffMeshes = new Mesh[count];
        for (int k = 0; k < count; k++)
        {
            bool pedestal = k >= 4;
            float h = pedestal ? 3.3f : 3.0f + 0.15f * k;
            SpiceProps.Cliff cl = SpiceProps.BuildCliff(101 + k * 17, h, pedestal);
            cliffs[k] = cl;
            // Room on top for what stands there (a colossus on a pedestal), so the sight-line guard keeps it clear too.
            float reserve = pedestal ? statueSize + 0.06f : 0.45f;
            Mesh india = Keep(cl.india.Build("SrCliff" + k, reserve));
            Mesh arab = Keep(cl.arabia.Build("SrCliffArab" + k));
            arab.bounds = india.bounds;   // same shape: keep the twin's bounds identical
            arabCliffMeshes[k] = arab;
            var go = new GameObject("SrCliff" + k);
            go.transform.SetParent(cliffRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = india;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = propMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cliffTemplates[k] = go;
        }
    }

    static Prop P(SpiceMesh kit)
    {
        Bounds b = kit.Bounds;
        return new Prop { kit = kit, radius = Mathf.Max(b.extents.x, b.extents.z) + 0.1f };
    }

    void BuildPropKits()
    {
        if (ganeshas != null) return;
        var rng = new System.Random(57);
        Prop[] Many(int n, System.Func<SpiceMesh> make) { var a = new Prop[n]; for (int i = 0; i < n; i++) a[i] = P(make()); return a; }
        ganeshas = new[] { P(SpiceProps.Ganesha(0)), P(SpiceProps.Ganesha(1)), P(SpiceProps.Ganesha(2)) };
        temples = Many(3, () => SpiceProps.NagaraTemple(rng));
        palaces = Many(2, () => SpiceProps.RajputPalace(rng));
        gopurams = Many(2, () => SpiceProps.Gopuram(rng));
        chhatris = Many(2, () => SpiceProps.Chhatri(rng));
        shrines = Many(3, () => SpiceProps.WhiteShrine(rng));
        elephants = Many(2, () => SpiceProps.Elephant(rng));
        peacocks = Many(2, () => SpiceProps.Peacock(rng));
        palms = Many(3, () => SpiceProps.Palm(rng, false));
        banyans = Many(2, () => SpiceProps.Banyan(rng));
        buntings = Many(2, () => SpiceProps.Bunting(rng));
        mosques = Many(3, () => SpiceProps.Mosque(rng));
        onionPalaces = Many(2, () => SpiceProps.OnionPalace(rng));
        houses = Many(8, () => SpiceProps.House(rng));
        towers = Many(3, () => SpiceProps.SandTower(rng));
        cypresses = Many(3, () => SpiceProps.Cypress(rng));
        datePalms = Many(2, () => SpiceProps.Palm(rng, true));
        lanterns = Many(2, () => SpiceProps.Lantern(rng));
    }

    int TemplateIndex(string name)
    {
        // "SrCliff3(Clone)"
        if (!name.StartsWith("SrCliff")) return -1;
        int k = name.Length > 7 ? name[7] - '0' : -1;
        return k >= 0 && cliffs != null && k < cliffs.Length ? k : -1;
    }

    static float DistXZ(Vector3 p, Vector3 a, Vector3 b, out Vector3 closest)
    {
        Vector2 P2 = new Vector2(p.x, p.z), A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z);
        Vector2 ab = B - A;
        float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(P2 - A, ab) / ab.sqrMagnitude) : 0f;
        closest = Vector3.Lerp(a, b, t);
        return (A + ab * t - P2).magnitude;
    }

    /// <summary>Serial of the leg nearest to p (horizontally) and the nearest point on it.</summary>
    int NearestLeg(Vector3 p, out Vector3 point)
    {
        point = runner != null ? runner.transform.position : p;
        int serial = pathGen != null ? pathGen.Score : 0;
        if (pathGen == null) return serial;
        float best = float.PositiveInfinity;
        foreach (var zone in pathGen.Zones)
            foreach (var seg in zone.segments)
            {
                if (seg == null || seg.serial < 0) continue;
                float d = DistXZ(p, seg.startPosition, seg.endPosition, out Vector3 c);
                if (d < best) { best = d; serial = seg.serial; point = c; }
            }
        return serial;
    }

    static Quaternion Face(Vector3 from, Vector3 to, float jitterDeg, System.Random rng)
    {
        Vector3 d = to - from;
        d.y = 0f;
        float j = ((float)rng.NextDouble() - 0.5f) * 2f * jitterDeg;
        return (d.sqrMagnitude < 1e-4f ? Quaternion.identity : Quaternion.LookRotation(d.normalized)) * Quaternion.Euler(0f, j, 0f);
    }

    void Stamp(SpiceMesh into, Prop prop, Vector3 pos, Quaternion rot, float scale, Matrix4x4 toHolder)
    {
        into.Add(prop.kit, toHolder * Matrix4x4.TRS(pos + Vector3.down * sink * scale, rot, Vector3.one * scale));
    }

    bool Place(SpiceMesh into, Prop[] set, float scale, System.Random rng, Vector3 facing, float jitterDeg, Matrix4x4 toHolder, bool central = false)
    {
        if (set == null || set.Length == 0 || candidates.Count == 0) return false;
        Prop prop = set[rng.Next(set.Length)];
        float r = prop.radius * scale;
        for (int tries = 0; tries < 16; tries++)
        {
            Vector3 p = candidates[rng.Next(candidates.Count)];
            if (central)
            {
                // The main building: the candidate nearest the middle of the top among a few.
                Vector3 best = p;
                for (int i = 0; i < 6; i++)
                {
                    Vector3 q = candidates[rng.Next(candidates.Count)];
                    if ((new Vector2(q.x - centre.x, q.z - centre.z)).sqrMagnitude < (new Vector2(best.x - centre.x, best.z - centre.z)).sqrMagnitude) best = q;
                }
                p = best;
            }
            if ((new Vector2(p.x - centre.x, p.z - centre.z)).magnitude + r > topRadiusWorld * 1.05f && !central) continue;
            bool ok = true;
            for (int j = 0; j < placed.Count; j++)
            {
                float g = r + placedR[j];
                if ((new Vector2(p.x - placed[j].x, p.z - placed[j].z)).sqrMagnitude < g * g) { ok = false; break; }
            }
            if (!ok) continue;
            placed.Add(p);
            placedR.Add(r);
            Stamp(into, prop, p, Face(p, facing, jitterDeg, rng), scale, toHolder);
            return true;
        }
        return false;
    }

    Vector3 centre;
    float topRadiusWorld;

    /// <summary>Called by PathBackgroundGenerator for every rock top it places while the level is on (out of sight).</summary>
    void DressRock(Transform holder, bool wall)
    {
        if (!wall || propMaterial == null || holder == null || holder.childCount == 0 || ganeshas == null) return;
        Transform visual = holder.GetChild(0);
        int k = TemplateIndex(visual.name);
        if (k < 0) return;
        SpiceProps.Cliff cl = cliffs[k];
        Vector3 hp = holder.position;
        int serial = NearestLeg(hp, out Vector3 pathPoint);
        bool arab = HalfOf(serial) == 1;
        if (arab)
        {
            var mf = visual.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = arabCliffMeshes[k];
        }
        var rng = new System.Random(Mathf.RoundToInt(hp.x * 7.31f) * 73856093 ^ Mathf.RoundToInt(hp.z * 3.17f) * 19349663 ^ Mathf.RoundToInt(hp.y) * 83492791);
        float s = visual.lossyScale.x;
        Matrix4x4 toHolder = holder.worldToLocalMatrix;
        centre = visual.TransformPoint(cl.anchor);
        topRadiusWorld = cl.topRadius * s * 0.82f;
        candidates.Clear();
        foreach (Vector4 t in cl.tops) candidates.Add(visual.TransformPoint(new Vector3(t.x, t.z, t.y)));
        placed.Clear();
        placedR.Clear();
        var kit = new SpiceMesh();
        float ps = propScale;

        if (cl.pedestal)
        {
            // A colossus on its plinth, facing the path (India) - or a grand mosque / palace (Arabia).
            Quaternion face = Face(centre, pathPoint, 0f, rng);
            if (!arab) Stamp(kit, ganeshas[rng.Next(ganeshas.Length)], centre, face, s * statueSize, toHolder);
            else Stamp(kit, rng.NextDouble() < 0.55 ? mosques[rng.Next(mosques.Length)] : onionPalaces[rng.Next(onionPalaces.Length)], centre, face, s * landmarkScale, toHolder);
        }
        else if (!arab)
        {
            double pick = rng.NextDouble();
            Place(kit, pick < 0.55 ? temples : pick < 0.8 ? palaces : gopurams, ps * 1.15f, rng, pathPoint, 15f, toHolder, true);
            int n = 1 + rng.Next(2);
            for (int i = 0; i < n; i++) Place(kit, chhatris, ps, rng, pathPoint, 40f, toHolder);
            n = 1 + rng.Next(3);
            for (int i = 0; i < n; i++) Place(kit, shrines, ps, rng, pathPoint, 40f, toHolder);
            if (rng.NextDouble() < 0.6) { Place(kit, elephants, ps * 1.3f, rng, pathPoint, 70f, toHolder); if (rng.NextDouble() < 0.5) Place(kit, elephants, ps * 1.3f, rng, pathPoint, 70f, toHolder); }
            n = rng.Next(treesMin, treesMax + 1);
            for (int i = 0; i < n; i++) Place(kit, rng.NextDouble() < 0.6 ? palms : banyans, ps * (0.85f + 0.3f * (float)rng.NextDouble()), rng, pathPoint, 180f, toHolder);
            if (rng.NextDouble() < 0.6) Place(kit, buntings, ps, rng, pathPoint, 30f, toHolder);
        }
        else
        {
            double pick = rng.NextDouble();
            if (pick < 0.45) Place(kit, mosques, ps * 1.1f, rng, pathPoint, 10f, toHolder, true);
            else if (pick < 0.7) Place(kit, onionPalaces, ps * 1.15f, rng, pathPoint, 10f, toHolder, true);
            else { Place(kit, towers, ps * 1.2f, rng, pathPoint, 180f, toHolder, true); Place(kit, towers, ps, rng, pathPoint, 180f, toHolder); Place(kit, towers, ps * 0.9f, rng, pathPoint, 180f, toHolder); }
            int n = rng.Next(housesMin, housesMax + 1);
            for (int i = 0; i < n; i++) Place(kit, houses, ps, rng, pathPoint, 20f, toHolder);
            n = rng.Next(treesMin, treesMax + 1);
            for (int i = 0; i < n; i++) Place(kit, rng.NextDouble() < 0.6 ? cypresses : datePalms, ps * (0.85f + 0.3f * (float)rng.NextDouble()), rng, pathPoint, 180f, toHolder);
            n = rng.Next(1, 4);
            for (int i = 0; i < n; i++) Place(kit, lanterns, ps, rng, pathPoint, 180f, toHolder);
        }

        if (kit.VertexCount == 0) return;
        Mesh mesh = kit.Build("SrDressing");
        var go = new GameObject("SrDressing");
        go.transform.SetParent(holder, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = propMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        go.AddComponent<SpiceOwnedMesh>().mesh = mesh;
    }
}
