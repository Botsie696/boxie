using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Flower checkpoint "Blossom Vale" (MainScene prop camera 3.10 checkpoint 6, right after Storybook Keep, and the whole run
/// of MainScene prop camera Flower): everything the painted flower-valley look needs besides the look asset (Look_Flower:
/// colours, sky, fog, sun, post). Reference: Tam Nguyen, "Somewhere in my mind".
///
///   Where      _FlLook (x: the look in force is flower, y: the look being wiped in is, z: always) - every flower shader
///              (FlowerCommon.hlsl) switches along the look wipe with it
///   Palette    grass, moss, soil, cliff stone, cliff moss, six blossom colours - constant globals read by the leg texture
///              (FlowerLeg.hlsl: flower meadow tops, moss sides, flower drips), the cliffs (FlowerRock.hlsl), the trail
///              (PixelFaceTrail's flower ribbon), the props and the petals
///   Paint      while the look is on screen the camera draws with FlowerCP_Renderer (the storybook renderer's passes + the
///              Kuwahara paint pass); the storybook pen / paper passes are switched off unless storybook is on screen too
///   Backdrop   cyan sky with white swirl strokes, karst pillars with moss caps rising out of a white valley mist, birds
///   Cliffs     the checkpoint's own karst: clusters of tall rounded pillars that run down into the valley mist (no
///              floating rocks, no debris), every top dressed with blossom trees and shrubs (children of the rock: they
///              fade and retire with it, nothing pops in view)
///   Vines      flower vines hang under the edges of the flower level's legs (made when a leg is laid, far ahead; cut away
///              round the runner by the shader so they never hide it)
///   Petals     petals drift round the camera and the runner sheds a few as it rolls
/// Purely visual: it never touches gameplay, the camera's framing or any asset (the renderer features it switches are
/// put back on disable).
/// </summary>
[DefaultExecutionOrder(980)]   // after Storybook / Pixel / Toon controllers: its renderer wins while on screen
public class FlowerCheckpointController : MonoBehaviour
{
    [Header("The flower look")]
    [Tooltip("Look_Flower. The checkpoint is wherever this look is in force (or being wiped in).")]
    public AtmosphereLook flowerLook;
    [Tooltip("The whole run is flower (MainScene prop camera Flower).")]
    public bool alwaysFlower;
    [Tooltip("Portal whose runway out starts the flower level (hanging vines are made from its leg on). 3.10: 5.")]
    public int flowerPortal = 5;

    [Header("References (found when empty)")]
    public LookTransitionDirector director;
    public PathBackgroundGenerator background;
    public DynamicPathGenerator pathGen;
    public CubeRailRunner runner;
    public CameraParentRotator rig;
    public ToonCheckpointController toon;
    public PixelCheckpointController pixel;
    public StorybookCheckpointController story;

    [Header("Palette (sRGB) - measured from the reference")]
    public Color grass = new Color32(0x7C, 0xC8, 0x3A, 0xFF);
    public Color grassDark = new Color32(0x3E, 0x8A, 0x2E, 0xFF);
    [Range(0f, 1f)] public float brush = 0.85f;
    public Color moss = new Color32(0x3F, 0x8F, 0x45, 0xFF);
    public Color mossDark = new Color32(0x1F, 0x5A, 0x35, 0xFF);
    public Color soil = new Color32(0x4A, 0x30, 0x40, 0xFF);
    public Color stone = new Color32(0xDC, 0xD6, 0xE2, 0xFF);
    public Color stoneShade = new Color32(0x8E, 0x92, 0xB8, 0xFF);
    public Color cliffMoss = new Color32(0x8F, 0xC2, 0x5A, 0xFF);
    public Color cliffMossLight = new Color32(0xC2, 0xE0, 0x5A, 0xFF);
    public Color[] blooms =
    {
        new Color32(0xE0, 0x36, 0x7A, 0xFF), new Color32(0xB5, 0x24, 0x5A, 0xFF), new Color32(0xF2, 0x7B, 0xB5, 0xFF),
        new Color32(0x9A, 0x55, 0xD6, 0xFF), new Color32(0x4F, 0x86, 0xEA, 0xFF), new Color32(0xEE, 0x8A, 0x2C, 0xFF),
    };

    [Header("Legs")]
    [Tooltip("Chance of a flower in each cell of a leg top.")]
    [Range(0f, 1f)] public float legFlowers = 0.7f;
    [Tooltip("Share of the leg-side columns with a flower drip.")]
    [Range(0f, 1f)] public float dripShare = 0.45f;
    [Tooltip("Longest painted drip, in face heights (> 0.84 runs round the bottom edge).")]
    [Range(0.3f, 1.4f)] public float dripLength = 1.1f;
    [Tooltip("Share of the cliff-top cells with a blossom clump.")]
    [Range(0f, 1f)] public float cliffFlowers = 0.6f;

    [Header("Hanging vines under the legs")]
    public bool hangingVines = true;
    [Tooltip("Chance per cube edge of a hanging vine.")]
    [Range(0f, 1f)] public float vineChance = 0.22f;
    public Vector2 vineLength = new Vector2(0.45f, 1.3f);
    [Tooltip("Vines are cut away within this many cubes of the runner.")]
    public float vineRunnerClear = 1.15f;
    [Range(0.05f, 1f)] public float scanInterval = 0.15f;

    [Header("Light (camera relative)")]
    public bool litRight = false;
    public float lightUp = 1f, lightSide = 0.75f, lightBack = 0.5f;
    [Range(0.1f, 3f)] public float rockBrushScale = 0.7f;

    [Header("Renderer")]
    [Tooltip("Renderer index of FlowerCP_Renderer on the pipeline assets. -1 = no paint pass.")]
    public int flowerRendererIndex = 5;
    [Tooltip("The camera's own renderer index, put back when the look leaves the screen.")]
    public int baseRendererIndex = 1;
    [Tooltip("FlowerCP_Renderer: its storybook pen / paper features are only switched on while storybook is on screen too.")]
    public UniversalRendererData flowerRenderer;

    [Header("Guide line over the next leg (DynamicPathGenerator.guideTrailColor)")]
    public Color guideColor = new Color(1f, 1f, 1f, 0.9f);

    [Header("Backdrop")]
    public Material backdropMaterial;
    public float yawParallax = 0.003f;
    public float travelParallax = 0.004f;

    [Header("Cliffs while in the checkpoint: tall karst running down into the mist")]
    public bool restyleRockLayout = true;
    public PixelCheckpointController.RockLayout flowerRocks = new PixelCheckpointController.RockLayout
    {
        wallChance = 0.9f, wallsPerCell = 2, wallWidth = new Vector2(14f, 28f), wallTopRange = new Vector2(-22f, 6f),
        wallClearance = 26f, floatingChance = 0f, floatingPerCell = 0, floatingWidth = new Vector2(3f, 6f),
        floatingHeightRange = new Vector2(-20f, -8f), floatingClearance = 6f, debrisPerCell = 0, futureReach = 80f, futureDrop = 24f,
    };
    [Range(0f, 0.9f)] public float flowerVeil = 0.03f;
    [Range(0f, 1f)] public float flowerHazeMax = 0.3f;
    public float fogEaseSeconds = 1.5f;
    public bool ownCliffs = true;

    [Header("Props on the cliff tops")]
    public Material propMaterial;
    public bool dressRocks = true;
    [Range(0, 12)] public int treesMin = 3, treesMax = 6;
    [Range(0, 12)] public int shrubsMin = 2, shrubsMax = 5;
    public Vector2 treeScale = new Vector2(2.2f, 3.6f);
    public Vector2 shrubScale = new Vector2(1.4f, 2.2f);
    public float sink = 0.25f;

    [Header("Petals")]
    public Material petalMaterial;
    [Range(0, 400)] public int petalCount = 140;
    public float petalRate = 14f;
    public Vector3 petalBox = new Vector3(30f, 6f, 30f);
    public Vector2 petalSize = new Vector2(0.18f, 0.32f);
    [Tooltip("Petals the runner sheds per unit rolled.")]
    public float runnerPetalsPerUnit = 1.2f;

    static readonly int LookId = Shader.PropertyToID("_FlLook");
    static readonly int GrassId = Shader.PropertyToID("_FlGrass");
    static readonly int GrassDarkId = Shader.PropertyToID("_FlGrassDark");
    static readonly int MossId = Shader.PropertyToID("_FlMoss");
    static readonly int MossDarkId = Shader.PropertyToID("_FlMossDark");
    static readonly int SoilId = Shader.PropertyToID("_FlSoil");
    static readonly int StoneId = Shader.PropertyToID("_FlStone");
    static readonly int StoneShadeId = Shader.PropertyToID("_FlStoneShade");
    static readonly int CliffMossId = Shader.PropertyToID("_FlCliffMoss");
    static readonly int CliffMossLightId = Shader.PropertyToID("_FlCliffMossLight");
    static readonly int[] BloomIds =
    {
        Shader.PropertyToID("_FlBloom0"), Shader.PropertyToID("_FlBloom1"), Shader.PropertyToID("_FlBloom2"),
        Shader.PropertyToID("_FlBloom3"), Shader.PropertyToID("_FlBloom4"), Shader.PropertyToID("_FlBloom5"),
    };
    static readonly int ParamsId = Shader.PropertyToID("_FlParams");
    static readonly int LightDirId = Shader.PropertyToID("_FlLightDir");
    static readonly int RunnerId = Shader.PropertyToID("_FlRunner");
    static readonly int BackdropId = Shader.PropertyToID("_FlBackdrop");
    const string InkFeature = "StorybookInk", PaperFeature = "StorybookPaper";

    PixelCheckpointController.RockLayout originalRocks;
    bool rocksFlower, flOnScreen, rendererFl;
    Camera cam;
    UniversalAdditionalCameraData camData;
    GameObject backdrop;
    MeshRenderer backdropRenderer;
    ChasmFog chasm;
    float sceneVeil, sceneHazeMax, fogBlend = -1f;
    bool fogCaptured;
    Color sceneGuideColor;
    bool guideCaptured, guideFl;
    ScriptableRendererFeature inkFeature, paperFeature;
    bool storyFeaturesOn = true;

    Mesh[] treeMeshes, shrubMeshes, vineMeshes;
    GameObject[] cliffTemplates, sceneWallPrefabs;
    GameObject cliffRoot;
    bool cliffsOn;
    readonly Dictionary<string, List<Vector4>> cliffColumns = new Dictionary<string, List<Vector4>>();   // per template: x, z, radius, top
    readonly List<Vector4> columns = new List<Vector4>();
    readonly List<Mesh> madeMeshes = new List<Mesh>();
    readonly List<Vector3> candidates = new List<Vector3>();
    readonly List<Vector3> placed = new List<Vector3>();
    readonly HashSet<DynamicPathGenerator.Segment> vined = new HashSet<DynamicPathGenerator.Segment>();
    readonly HashSet<DynamicPathGenerator.Segment> live = new HashSet<DynamicPathGenerator.Segment>();
    readonly List<DynamicPathGenerator.Segment> dead = new List<DynamicPathGenerator.Segment>();
    float nextScan, nextPrune;

    GameObject petals, runnerPetals;
    ParticleSystem petalSystem, runnerPetalSystem;

    public bool FlowerOnScreen => flOnScreen;
    public bool FlowerRockLayout => rocksFlower;

    void Awake()
    {
        if (director == null) director = FindFirstObjectByType<LookTransitionDirector>();
        if (background == null) background = FindFirstObjectByType<PathBackgroundGenerator>();
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (runner == null) runner = FindFirstObjectByType<CubeRailRunner>();
        if (rig == null) rig = FindFirstObjectByType<CameraParentRotator>();
        if (toon == null) toon = FindFirstObjectByType<ToonCheckpointController>();
        if (pixel == null) pixel = FindFirstObjectByType<PixelCheckpointController>();
        if (story == null) story = FindFirstObjectByType<StorybookCheckpointController>();
        PushPalette();
        Shader.SetGlobalVector(LookId, Vector4.zero);
        if (flowerRenderer != null)
            foreach (ScriptableRendererFeature f in flowerRenderer.rendererFeatures)
            {
                if (f == null) continue;
                if (f.name == InkFeature) inkFeature = f;
                else if (f.name == PaperFeature) paperFeature = f;
            }
    }

    void Start()
    {
        cam = rig != null && rig.boomCamera != null ? rig.boomCamera : Camera.main;
        if (cam != null) camData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (background != null) originalRocks = PixelCheckpointController.RockLayout.From(background);
        BuildBackdrop();
        BuildPropMeshes();
        BuildCliffs();
        BuildPetals();
    }

    void OnDisable()
    {
        Shader.SetGlobalVector(LookId, Vector4.zero);
        if (camData != null && rendererFl) camData.SetRenderer(baseRendererIndex);
        rendererFl = false;
        SetStoryFeatures(true);
        if (background != null) background.RockTopBuilt -= DressRock;
        if (rocksFlower && background != null && originalRocks != null) originalRocks.ApplyTo(background);
        if (cliffsOn && background != null) background.wallPrefabs = sceneWallPrefabs;
        cliffsOn = false;
        rocksFlower = false;
        if (guideFl && pathGen != null) pathGen.guideTrailColor = sceneGuideColor;
        guideFl = false;
        if (fogCaptured && chasm != null) { chasm.veil = sceneVeil; chasm.hazeMax = sceneHazeMax; }
    }

    void OnDestroy()
    {
        if (backdrop != null) { Destroy(backdrop.GetComponent<MeshFilter>().sharedMesh); Destroy(backdrop); }
        foreach (Mesh m in madeMeshes) if (m != null) Destroy(m);
        if (cliffRoot != null) Destroy(cliffRoot);
    }

    void OnValidate()
    {
        if (isActiveAndEnabled) PushPalette();
    }

    bool IsFlower(AtmosphereLook l) =>
        l != null && flowerLook != null && (l == flowerLook || (!string.IsNullOrEmpty(flowerLook.sourceScene) && l.sourceScene == flowerLook.sourceScene));

    void LateUpdate()
    {
        AtmosphereLook now = director != null ? director.CurrentLook : null;
        AtmosphereLook next = director != null ? director.NextLook : null;
        bool a = IsFlower(now), b = IsFlower(next);
        flOnScreen = alwaysFlower || a || b;
        bool zone = alwaysFlower || (next != null ? b : a);
        Shader.SetGlobalVector(LookId, new Vector4(a ? 1f : 0f, b ? 1f : 0f, alwaysFlower ? 1f : 0f, 0f));

        PushPalette();
        PushLight();
        UpdateRenderer();
        UpdateGuide();
        UpdateRocks(zone);
        UpdateBackdrop();
        UpdateFog();
        UpdatePetals();
        if (hangingVines && Time.time >= nextScan) { nextScan = Time.time + scanInterval; ScanVines(); }
    }

    // ------------------------------------------------------------------ globals

    static Vector4 Lin(Color c, float a) { Color l = c.linear; return new Vector4(l.r, l.g, l.b, a); }

    void PushPalette()
    {
        Shader.SetGlobalVector(GrassId, Lin(grass, brush));
        Shader.SetGlobalVector(GrassDarkId, Lin(grassDark, 1f));
        Shader.SetGlobalVector(MossId, Lin(moss, 1f));
        Shader.SetGlobalVector(MossDarkId, Lin(mossDark, 1f));
        Shader.SetGlobalVector(SoilId, Lin(soil, 1f));
        Shader.SetGlobalVector(StoneId, Lin(stone, 1f));
        Shader.SetGlobalVector(StoneShadeId, Lin(stoneShade, 1f));
        Shader.SetGlobalVector(CliffMossId, Lin(cliffMoss, 1f));
        Shader.SetGlobalVector(CliffMossLightId, Lin(cliffMossLight, 1f));
        for (int i = 0; i < BloomIds.Length; i++)
            Shader.SetGlobalVector(BloomIds[i], Lin(blooms != null && blooms.Length > 0 ? blooms[i % blooms.Length] : Color.magenta, 1f));
        Shader.SetGlobalVector(ParamsId, new Vector4(legFlowers, dripShare, dripLength, cliffFlowers));
    }

    Vector3 Flat(Vector3 v)
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
        Vector3 rp = runner != null ? runner.transform.position : new Vector3(0f, -1e5f, 0f);
        Shader.SetGlobalVector(RunnerId, new Vector4(rp.x, rp.y, rp.z, flOnScreen ? vineRunnerClear : 0f));
    }

    // ------------------------------------------------------------------ renderer + guide

    void SetStoryFeatures(bool on)
    {
        if (storyFeaturesOn == on) return;
        storyFeaturesOn = on;
        if (inkFeature != null) inkFeature.SetActive(on);
        if (paperFeature != null) paperFeature.SetActive(on);
    }

    void UpdateRenderer()
    {
        if (camData == null || flowerRendererIndex < 0) return;
        if (flOnScreen)
        {
            camData.SetRenderer(flowerRendererIndex);   // every frame: the storybook / pixel / toon controllers run earlier
            rendererFl = true;
            SetStoryFeatures(story != null && story.isActiveAndEnabled && story.StoryOnScreen);
        }
        else if (rendererFl)
        {
            int idx = baseRendererIndex;
            if (story != null && story.isActiveAndEnabled && story.StoryOnScreen) idx = story.storyRendererIndex;
            else if (pixel != null && pixel.isActiveAndEnabled && pixel.PixelOnScreen) idx = pixel.pixelRendererIndex;
            else if (toon != null && toon.isActiveAndEnabled && toon.ToonOnScreen) idx = toon.toonRendererIndex;
            camData.SetRenderer(idx);
            rendererFl = false;
            SetStoryFeatures(true);
        }
    }

    void UpdateGuide()
    {
        if (pathGen == null) return;
        if (!guideCaptured) { sceneGuideColor = pathGen.guideTrailColor; guideCaptured = true; }
        if (flOnScreen)
        {
            pathGen.guideTrailColor = guideColor;
            guideFl = true;
        }
        else if (guideFl)
        {
            if (story != null && story.isActiveAndEnabled && story.StoryOnScreen) pathGen.guideTrailColor = story.guideColor;
            else if (pixel != null && pixel.isActiveAndEnabled && pixel.PixelOnScreen) pathGen.guideTrailColor = pixel.guideColor;
            else if (toon != null && toon.isActiveAndEnabled && toon.ToonOnScreen) pathGen.guideTrailColor = toon.guideColor;
            else pathGen.guideTrailColor = sceneGuideColor;
            guideFl = false;
        }
    }

    // ------------------------------------------------------------------ rocks + fog

    void UpdateRocks(bool zone)
    {
        if (background == null || originalRocks == null) return;
        if (zone == rocksFlower) return;
        rocksFlower = zone;
        background.RockTopBuilt -= DressRock;
        if (zone && dressRocks) background.RockTopBuilt += DressRock;
        if (restyleRockLayout)
        {
            // Leaving: the scene's layout, unless an earlier-running checkpoint already put its own this frame.
            if (zone) flowerRocks.ApplyTo(background);
            else if ((story == null || !story.StoryRockLayout) && (pixel == null || !pixel.PixelRockLayout)) originalRocks.ApplyTo(background);
        }
        if (ownCliffs && cliffTemplates != null)
        {
            if (zone) { sceneWallPrefabs = background.wallPrefabs; background.wallPrefabs = cliffTemplates; cliffsOn = true; }
            else if (cliffsOn) { background.wallPrefabs = sceneWallPrefabs; cliffsOn = false; }
        }
        background.RebuildOutOfSight();
    }

    void UpdateFog()
    {
        if (chasm == null) chasm = ChasmFog.Active;
        if (chasm == null) return;
        if (!fogCaptured) { sceneVeil = chasm.veil; sceneHazeMax = chasm.hazeMax; fogCaptured = true; }
        float target = flOnScreen ? 1f : 0f;
        if (fogBlend == target && !flOnScreen) return;
        fogBlend = fogBlend < 0f ? target : Mathf.MoveTowards(fogBlend, target, Time.deltaTime / Mathf.Max(0.01f, fogEaseSeconds));
        float k = fogBlend * fogBlend * (3f - 2f * fogBlend);
        chasm.veil = Mathf.Lerp(sceneVeil, flowerVeil, k);
        chasm.hazeMax = Mathf.Lerp(sceneHazeMax, flowerHazeMax, k);
    }

    // ------------------------------------------------------------------ backdrop

    void BuildBackdrop()
    {
        if (backdropMaterial == null || cam == null) return;
        backdrop = new GameObject("FlowerBackdrop");
        backdrop.transform.SetParent(cam.transform, false);
        backdrop.transform.localPosition = new Vector3(0f, 0f, Mathf.Min(cam.farClipPlane * 0.5f, 200f));
        var m = new Mesh { name = "FlowerBackdropQuad" };
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
        backdropRenderer.enabled = flOnScreen;
        if (!flOnScreen) return;
        float yaw = cam.transform.eulerAngles.y;
        Vector3 cp = cam.transform.position;
        float tx = Mathf.Repeat(Vector3.Dot(cp, Flat(cam.transform.right)) * travelParallax, 1000f);
        float ty = Mathf.Repeat(cp.y * travelParallax, 1000f);
        Shader.SetGlobalVector(BackdropId, new Vector4(-yaw * yawParallax, Time.time, tx, ty));
    }

    // ------------------------------------------------------------------ petals

    ParticleSystem MakePetalSystem(string name, int max, out GameObject go)
    {
        go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startSize = new ParticleSystem.MinMaxCurve(petalSize.x, petalSize.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var keys = new GradientColorKey[blooms.Length];
        for (int i = 0; i < blooms.Length; i++) keys[i] = new GradientColorKey(blooms[i], (i + 1f) / blooms.Length);
        var g = new Gradient { mode = GradientMode.Fixed };
        g.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var life = new Gradient();
        life.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(life);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.2f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = petalMaterial;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    void BuildPetals()
    {
        if (petalMaterial == null || petalCount <= 0 || blooms == null || blooms.Length == 0) return;
        petalSystem = MakePetalSystem("FlowerPetals", petalCount, out petals);
        var main = petalSystem.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        main.gravityModifier = 0.03f;
        var em = petalSystem.emission;
        em.rateOverTime = petalRate;
        var sh = petalSystem.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = petalBox;
        var vel = petalSystem.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);   // the painting's wind
        vel.y = new ParticleSystem.MinMaxCurve(-0.4f, 0.1f);
        vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
        em.enabled = false;
        petalSystem.Play();

        if (runnerPetalsPerUnit > 0f)
        {
            runnerPetalSystem = MakePetalSystem("FlowerRunnerPetals", 60, out runnerPetals);
            var rm = runnerPetalSystem.main;
            rm.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            rm.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.0f);
            rm.startSize = new ParticleSystem.MinMaxCurve(petalSize.x * 0.8f, petalSize.y);
            rm.gravityModifier = 0.15f;
            var rem = runnerPetalSystem.emission;
            rem.rateOverTime = 0f;
            rem.rateOverDistance = runnerPetalsPerUnit;
            var rsh = runnerPetalSystem.shape;
            rsh.shapeType = ParticleSystemShapeType.Sphere;
            rsh.radius = 0.35f;
            rem.enabled = false;
            runnerPetalSystem.Play();
        }
    }

    void UpdatePetals()
    {
        if (runner == null) return;
        Vector3 rp = runner.transform.position;
        if (petalSystem != null)
        {
            petals.transform.position = rp + Vector3.up * 4f;
            var em = petalSystem.emission;
            if (em.enabled != flOnScreen) em.enabled = flOnScreen;
        }
        if (runnerPetalSystem != null)
        {
            runnerPetals.transform.position = rp;
            var em = runnerPetalSystem.emission;
            if (em.enabled != flOnScreen) em.enabled = flOnScreen;
        }
    }

    // ------------------------------------------------------------------ hanging vines under the legs

    bool FlowerLeg(int serial)
    {
        if (alwaysFlower) return true;
        if (pathGen == null) return false;
        return serial >= pathGen.CheckpointLeg(flowerPortal) && serial < pathGen.CheckpointLeg(flowerPortal + 1);
    }

    void ScanVines()
    {
        if (pathGen == null || vineMeshes == null || propMaterial == null) return;
        live.Clear();
        var zones = pathGen.Zones;
        for (int z = 0; z < zones.Count; z++)
        {
            var segs = zones[z].segments;
            for (int s = 0; s < segs.Count; s++)
            {
                DynamicPathGenerator.Segment seg = segs[s];
                if (seg == null || seg.serial < 0) continue;
                live.Add(seg);
                if (vined.Contains(seg)) continue;
                vined.Add(seg);
                if (FlowerLeg(seg.serial)) DressLeg(seg);
            }
        }
        if (Time.time >= nextPrune)
        {
            nextPrune = Time.time + 3f;
            dead.Clear();
            foreach (var sg in vined) if (!live.Contains(sg)) dead.Add(sg);
            for (int i = 0; i < dead.Count; i++) vined.Remove(dead[i]);
        }
    }

    void DressLeg(DynamicPathGenerator.Segment seg)
    {
        Vector3 dir = seg.direction;
        if (Mathf.Abs(dir.y) > 0.5f || seg.cubes.Count == 0) return;   // climbing legs: nothing hangs
        dir.y = 0f;
        dir.Normalize();
        Vector3 lat = Vector3.Cross(Vector3.up, dir).normalized;
        var rng = new System.Random(seg.serial * 7919 + 17);
        for (int i = 0; i < seg.cubes.Count; i++)
        {
            GameObject cube = seg.cubes[i];
            if (cube == null) continue;
            float size = cube.transform.lossyScale.x;
            for (int side = -1; side <= 1; side += 2)
            {
                if (rng.NextDouble() > vineChance) continue;
                float along = ((float)rng.NextDouble() - 0.5f) * 0.7f * size;
                Vector3 p = cube.transform.position + lat * (side * 0.5f * size) + dir * along + Vector3.down * (0.5f * size - 0.02f);
                var go = new GameObject("FlowerVine");
                go.transform.SetParent(cube.transform, true);
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(dir * side, Vector3.up));
                float len = Mathf.Lerp(vineLength.x, vineLength.y, (float)rng.NextDouble()) * size;
                go.transform.localScale = Vector3.one * (len / Mathf.Max(size, 1e-4f));   // the mesh is unit length; cubes scale uniformly
                go.AddComponent<MeshFilter>().sharedMesh = vineMeshes[rng.Next(vineMeshes.Length)];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = propMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }
    }

    // ------------------------------------------------------------------ props on the cliff tops

    /// <summary>Called by PathBackgroundGenerator for every rock top it places while the checkpoint is on.</summary>
    void DressRock(Transform holder, bool wall)
    {
        if (propMaterial == null || treeMeshes == null || holder == null || holder.childCount == 0) return;
        Transform visual = holder.GetChild(0);
        Vector3 hp = holder.position;
        var rng = new System.Random(Mathf.RoundToInt(hp.x * 7.31f) * 73856093 ^ Mathf.RoundToInt(hp.z * 3.17f) * 19349663 ^ Mathf.RoundToInt(hp.y) * 83492791);
        float F(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        if (!TopCandidates(visual)) return;
        placed.Clear();
        int trees = rng.Next(treesMin, treesMax + 1);
        for (int i = 0; i < trees; i++) Place(holder, treeMeshes[rng.Next(treeMeshes.Length)], F(treeScale.x, treeScale.y), 1.1f, rng);
        int shrubs = rng.Next(shrubsMin, shrubsMax + 1);
        for (int i = 0; i < shrubs; i++) Place(holder, shrubMeshes[rng.Next(shrubMeshes.Length)], F(shrubScale.x, shrubScale.y), 0.8f, rng);
    }

    void Place(Transform holder, Mesh mesh, float scale, float footprint, System.Random rng)
    {
        float gap = footprint * scale;
        for (int tries = 0; tries < 14 && candidates.Count > 0; tries++)
        {
            Vector3 p = candidates[rng.Next(candidates.Count)];
            bool ok = true;
            foreach (Vector3 q in placed)
                if ((new Vector2(p.x - q.x, p.z - q.z)).sqrMagnitude < gap * gap) { ok = false; break; }
            if (!ok) continue;
            placed.Add(p);
            var go = new GameObject(mesh.name);
            go.transform.SetParent(holder, true);
            go.transform.SetPositionAndRotation(p + Vector3.down * sink, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            go.transform.localScale = Vector3.one * scale;   // the holder is at full size when its rock is placed
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = propMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return;
        }
    }

    // Spots on the pillar tops (world space) into "candidates": the column tops are known exactly.
    bool TopCandidates(Transform visual)
    {
        candidates.Clear();
        if (!cliffColumns.TryGetValue(visual.name, out List<Vector4> cols)) return false;
        foreach (Vector4 c in cols)
        {
            for (int s = 0; s < 6; s++)
            {
                float a = s * 1.047f + c.x * 3f, rr = s == 0 ? 0f : c.z * 0.45f;
                float x = c.x + Mathf.Cos(a) * rr, z = c.y + Mathf.Sin(a) * rr;
                bool covered = false;
                foreach (Vector4 o in cols)
                    if (o.w > c.w + 1e-3f && (new Vector2(x - o.x, z - o.y)).sqrMagnitude < o.z * o.z * 1.3f) { covered = true; break; }
                if (!covered) candidates.Add(visual.TransformPoint(new Vector3(x, c.w, z)));
            }
        }
        return candidates.Count > 0;
    }

    // ------------------------------------------------------------------ meshes (built once, shared)

    class Kit
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> nr = new List<Vector3>();
        readonly List<Color> c = new List<Color>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();

        int Vert(Vector3 p, Vector3 n, Color col, float kind, float sway)
        {
            col.a = sway;
            v.Add(p); nr.Add(n); c.Add(col); uv.Add(new Vector2(kind, 0f));
            return v.Count - 1;
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 d, Color col, float kind, float sway = 0f)
        {
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            int i = Vert(a, n, col, kind, sway), j = Vert(b, n, col, kind, sway), k = Vert(d, n, col, kind, sway);
            t.Add(i); t.Add(j); t.Add(k);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color col, float kind, float sway = 0f)
        {
            Tri(a, b, d, col, kind, sway); Tri(a, d, e, col, kind, sway);
        }

        // A tapered, slightly bent trunk / stem from b0 to b1.
        public void Stem(Vector3 b0, Vector3 b1, float r0, float r1, int seg, Color col, float kind, float sway0, float sway1)
        {
            Vector3 axis = (b1 - b0).normalized;
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 fwd = Vector3.Cross(axis, side);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Vector3 d0 = side * Mathf.Cos(a0) + fwd * Mathf.Sin(a0), d1 = side * Mathf.Cos(a1) + fwd * Mathf.Sin(a1);
                int p0 = Vert(b0 + d0 * r0, d0, col, kind, sway0), p1 = Vert(b0 + d1 * r0, d1, col, kind, sway0);
                int p2 = Vert(b1 + d1 * r1, d1, col, kind, sway1), p3 = Vert(b1 + d0 * r1, d0, col, kind, sway1);
                t.Add(p0); t.Add(p3); t.Add(p2);
                t.Add(p0); t.Add(p2); t.Add(p1);
            }
        }

        // A lumpy low-poly blob (blossom / foliage), sway weight by height.
        public void Blob(Vector3 centre, float r, float squash, Color col, System.Random rng, float sway)
        {
            const int seg = 8, rings = 5;
            int start = v.Count;
            for (int y = 0; y <= rings; y++)
            {
                float phi = Mathf.PI * y / rings;
                for (int x = 0; x <= seg; x++)
                {
                    float th = Mathf.PI * 2f * x / seg;
                    Vector3 n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    float j = (x == seg || y == 0 || y == rings) ? 1f : 0.85f + (float)rng.NextDouble() * 0.3f;
                    Vector3 p = centre + Vector3.Scale(n * r * j, new Vector3(1f, squash, 1f));
                    Vert(p, n, col, 2f, sway);
                }
            }
            for (int y = 0; y < rings; y++)
                for (int x = 0; x < seg; x++)
                {
                    int i0 = start + y * (seg + 1) + x, i1 = i0 + 1, i2 = i0 + seg + 1, i3 = i2 + 1;
                    t.Add(i0); t.Add(i1); t.Add(i2);
                    t.Add(i1); t.Add(i3); t.Add(i2);
                }
        }

        // A small diamond cluster (cheap blossom for the hanging vines).
        public void Bud(Vector3 c0, float r, Color col, float sway)
        {
            Vector3 up = c0 + Vector3.up * r, dn = c0 + Vector3.down * r * 1.2f;
            Vector3 a = c0 + Vector3.right * r, b = c0 + Vector3.forward * r, d = c0 + Vector3.left * r, e = c0 + Vector3.back * r;
            Tri(up, b, a, col, 4f, sway); Tri(up, d, b, col, 4f, sway); Tri(up, e, d, col, 4f, sway); Tri(up, a, e, col, 4f, sway);
            Tri(dn, a, b, col, 4f, sway); Tri(dn, b, d, col, 4f, sway); Tri(dn, d, e, col, 4f, sway); Tri(dn, e, a, col, 4f, sway);
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(nr); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    Mesh Keep(Mesh m) { madeMeshes.Add(m); return m; }

    // The checkpoint's cliffs: clusters of tall rounded karst pillars (the reference's limestone towers), unit footprint,
    // base at y = 0, about 2.2 high. The generator stacks them downward into the mist, so the cluster reads as towers
    // standing on the valley floor. Templates sit under an inactive root (never drawn); the scene rock's own material is
    // used so the chasm fog / flower rock shader applies unchanged.
    void BuildCliffs()
    {
        if (!ownCliffs || background == null || background.wallPrefabs == null || background.wallPrefabs.Length == 0) return;
        Material rockMat = null;
        foreach (GameObject p in background.wallPrefabs)
        {
            var r = p != null ? p.GetComponentInChildren<Renderer>(true) : null;
            if (r != null && r.sharedMaterial != null) { rockMat = r.sharedMaterial; break; }
        }
        if (rockMat == null) return;
        cliffRoot = new GameObject("FlowerCliffTemplates");
        cliffRoot.SetActive(false);
        cliffRoot.transform.SetParent(transform, false);
        var rng = new System.Random(23);
        cliffTemplates = new GameObject[4];
        for (int k = 0; k < cliffTemplates.Length; k++)
        {
            var kit = new Kit();
            int cols = 3 + k;
            for (int i = 0; i < cols; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float d = i == 0 ? 0f : Mathf.Lerp(0.15f, 0.3f, (float)rng.NextDouble());
                Vector2 c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                float r = i == 0 ? Mathf.Lerp(0.2f, 0.26f, (float)rng.NextDouble()) : Mathf.Lerp(0.11f, 0.19f, (float)rng.NextDouble());
                float h = (i == 0 ? 2.2f : Mathf.Lerp(1.2f, 2.0f, (float)rng.NextDouble()));
                float top = Pillar(kit, c, r, h, 9, rng);
                columns.Add(new Vector4(c.x, c.y, r * 0.8f, top));
            }
            cliffColumns["FlCliff" + k + "(Clone)"] = new List<Vector4>(columns);
            columns.Clear();
            var go = new GameObject("FlCliff" + k);
            go.transform.SetParent(cliffRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Keep(kit.Build("FlCliff" + k));
            go.AddComponent<MeshRenderer>().sharedMaterial = rockMat;
            cliffTemplates[k] = go;
        }
    }

    // One karst pillar: rings that bulge and narrow up the height, a rounded dome top. Returns the flat top's height.
    static float Pillar(Kit kit, Vector2 c, float r, float h, int sides, System.Random rng)
    {
        const int rings = 6;
        float phase = (float)rng.NextDouble() * 6.283f;
        var jag = new float[sides];
        for (int i = 0; i < sides; i++) jag[i] = 0.85f + 0.3f * (float)rng.NextDouble();
        var ring = new Vector3[rings + 1, sides];
        float domeStart = h * 0.88f;
        for (int y = 0; y <= rings; y++)
        {
            float k = (float)y / rings;
            float yy = k * domeStart;
            float bulge = 1.12f - 0.25f * k + 0.08f * Mathf.Sin(k * 9f + phase) + 0.05f * (float)rng.NextDouble();
            for (int i = 0; i < sides; i++)
            {
                float a = phase + i * Mathf.PI * 2f / sides;
                float rr = r * bulge * jag[i];
                ring[y, i] = new Vector3(c.x + Mathf.Cos(a) * rr, yy, c.y + Mathf.Sin(a) * rr);
            }
        }
        Color col = Color.white;
        for (int y = 0; y < rings; y++)
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                kit.Quad(ring[y, i], ring[y + 1, i], ring[y + 1, j], ring[y, j], col, 0f);
            }
        // Rounded top: a shrunken ring, then a flat cap (where the trees stand).
        Vector3 ctr = new Vector3(c.x, h, c.y);
        var cap = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            Vector3 o = ring[rings, i];
            cap[i] = new Vector3(c.x + (o.x - c.x) * 0.72f, h, c.y + (o.z - c.y) * 0.72f);
        }
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            kit.Quad(ring[rings, i], cap[i], cap[j], ring[rings, j], col, 0f);
            kit.Tri(ctr, cap[j], cap[i], col, 0f);
        }
        return h;
    }

    void BuildPropMeshes()
    {
        if (treeMeshes != null) return;
        var rng = new System.Random(7);
        Color trunk = new Color32(0x6E, 0x5A, 0x70, 0xFF);   // lilac-grey bark, like the painting
        Color leafG = new Color32(0x6C, 0xB0, 0x48, 0xFF);
        int nb = blooms.Length;

        // One blossom tree per colour + one green: a leaning trunk, a wide flat crown of blobs (the painting's trees).
        treeMeshes = new Mesh[nb + 1];
        for (int k = 0; k <= nb; k++)
        {
            var kit = new Kit();
            Color crown = (k < nb ? blooms[k] : leafG).linear;
            Vector3 lean = new Vector3(((float)rng.NextDouble() - 0.5f) * 0.6f, 1.3f, ((float)rng.NextDouble() - 0.5f) * 0.4f);
            kit.Stem(Vector3.zero, lean, 0.12f, 0.07f, 6, trunk.linear, 0f, 0f, 0.15f);
            kit.Stem(lean * 0.7f, lean * 0.7f + new Vector3(0.5f, 0.45f, 0.1f), 0.05f, 0.03f, 5, trunk.linear, 0f, 0.1f, 0.25f);
            int blobs = 5 + rng.Next(3);
            for (int i = 0; i < blobs; i++)
            {
                float a = i * 2.4f + k;
                float rr = i == 0 ? 0f : 0.55f + (float)rng.NextDouble() * 0.2f;
                Vector3 c = lean + new Vector3(Mathf.Cos(a) * rr, 0.25f + (float)rng.NextDouble() * 0.25f - rr * 0.25f, Mathf.Sin(a) * rr);
                Color cc = Color.Lerp(crown, crown * 0.75f, (float)rng.NextDouble() * 0.6f);
                cc.a = 1f;
                kit.Blob(c, i == 0 ? 0.62f : 0.4f + (float)rng.NextDouble() * 0.15f, 0.65f, cc, rng, 0.5f);
            }
            treeMeshes[k] = Keep(kit.Build("FlTree" + k));
        }

        // Shrubs: low mounds of two colours (green + a blossom).
        shrubMeshes = new Mesh[4];
        for (int k = 0; k < shrubMeshes.Length; k++)
        {
            var kit = new Kit();
            Color bc = blooms[(k * 2 + 1) % nb].linear;
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.7f + k;
                Vector3 c = new Vector3(Mathf.Cos(a) * 0.35f, 0.2f, Mathf.Sin(a) * 0.35f) * (i == 0 ? 0f : 1f);
                kit.Blob(c, 0.38f, 0.7f, i % 2 == 0 ? leafG.linear : bc, rng, 0.25f);
            }
            shrubMeshes[k] = Keep(kit.Build("FlShrub" + k));
        }

        // Hanging vines: unit length down -Y from the origin, a few strands with clusters growing toward the tip.
        vineMeshes = new Mesh[4];
        for (int k = 0; k < vineMeshes.Length; k++)
        {
            var kit = new Kit();
            Color bc = blooms[(k * 3 + 2) % nb].linear;
            Color stem = new Color32(0x2E, 0x6E, 0x34, 0xFF);
            int strands = 2 + k % 2;
            for (int s = 0; s < strands; s++)
            {
                float ox = (s - (strands - 1) * 0.5f) * 0.14f + ((float)rng.NextDouble() - 0.5f) * 0.05f;
                float oz = ((float)rng.NextDouble() - 0.5f) * 0.06f;
                float len = 0.6f + 0.4f * (float)rng.NextDouble();
                Vector3 top = new Vector3(ox, 0f, oz), tip = new Vector3(ox + ((float)rng.NextDouble() - 0.5f) * 0.1f, -len, oz + 0.04f);
                kit.Stem(top, tip, 0.012f, 0.008f, 4, stem.linear, 4f, 0f, 0.6f);
                int buds = 6;
                for (int b = 1; b <= buds; b++)
                {
                    float f = (float)b / buds;
                    Vector3 p = Vector3.Lerp(top, tip, f) + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.05f, 0f, ((float)rng.NextDouble() - 0.5f) * 0.05f);
                    Color cc = Color.Lerp(bc, Color.white, (float)rng.NextDouble() * 0.25f);
                    kit.Bud(p, Mathf.Lerp(0.035f, 0.06f, f), cc, f * 0.6f);
                }
            }
            vineMeshes[k] = Keep(kit.Build("FlVine" + k));
        }
    }
}
