using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Voxel checkpoint "Brick Meadow" (MainScene prop camera 3.10 checkpoint 7, right after Blossom Vale, and the whole run of
/// MainScene prop camera Voxel): everything the blocky Minecraft / Super Mario meadow needs besides the look asset
/// (Look_Voxel: colours, sky, fog, sun, post).
///
///   Where      _VxLook (x: the look in force is voxel, y: the look being wiped in is, z: always) - every voxel shader
///              (VoxelCommon.hlsl) switches along the look wipe with it
///   Legs       each leg a pixel slab bridge on bricks (VoxelLeg.hlsl): ten slab colours and six brick colours, a new pair
///              every leg; the slab drips over the bricks
///   Cliffs     the checkpoint's own block mesas (grass tops with dripping turf, dirt, orange / red brick, sandstone and
///              cobble strata, trailing leaves), set further back and longer; a few of the scene's rocks stay among them,
///              repainted as blocks (VoxelRock.hlsl)
///   Props      on every mesa top: square-crowned trees in many colours, lollipop and mushroom trees, spruces, brick towers,
///              block houses, cottages, little castles, Mario fire flowers, flower / tuft patches, bushes, warp pipes, and
///              floating brick + "?" block rows (children of the rock: they fade and retire with it, nothing pops in view)
///   Backdrop   pixel sky, blocky clouds, stepped far mountains, hills with pixel trees and towers
///   Bits       square pixel bits drift round the camera; the runner kicks a few up as it rolls
/// Purely visual: it never touches gameplay, the camera's framing or any asset.
/// </summary>
[DefaultExecutionOrder(990)]   // after the flower / storybook / pixel / toon controllers
public class VoxelCheckpointController : MonoBehaviour
{
    [Header("The voxel look")]
    [Tooltip("Look_Voxel. The checkpoint is wherever this look is in force (or being wiped in).")]
    public AtmosphereLook voxelLook;
    [Tooltip("The whole run is voxel (MainScene prop camera Voxel).")]
    public bool alwaysVoxel;

    [Header("References (found when empty)")]
    public LookTransitionDirector director;
    public PathBackgroundGenerator background;
    public DynamicPathGenerator pathGen;
    public CubeRailRunner runner;
    public CameraParentRotator rig;
    public FlowerCheckpointController flower;
    public StorybookCheckpointController story;
    public PixelCheckpointController pixel;
    public ToonCheckpointController toon;

    [Header("Leg colours (sRGB) - a new slab / brick pair every leg")]
    public Color[] legTops =
    {
        new Color32(0xF6, 0xC8, 0x2E, 0xFF), new Color32(0x7E, 0xD9, 0x3A, 0xFF), new Color32(0x3C, 0xC8, 0xE8, 0xFF),
        new Color32(0xF2, 0x7A, 0x2A, 0xFF), new Color32(0xF2, 0x6E, 0xB4, 0xFF), new Color32(0xA9, 0x6B, 0xF0, 0xFF),
        new Color32(0x4F, 0x8C, 0xF5, 0xFF), new Color32(0x5E, 0xE8, 0xB0, 0xFF), new Color32(0xF5, 0xE6, 0xB8, 0xFF),
        new Color32(0xF0, 0x55, 0x55, 0xFF),
    };
    public Color[] legBricks =
    {
        new Color32(0x9A, 0x3A, 0x2A, 0xFF), new Color32(0xB8, 0x5A, 0x2E, 0xFF), new Color32(0x6E, 0x3A, 0x7A, 0xFF),
        new Color32(0x4A, 0x5A, 0x8A, 0xFF), new Color32(0x7A, 0x4A, 0x30, 0xFF), new Color32(0x2E, 0x6A, 0x6A, 0xFF),
    };
    [Tooltip("Share of the leg-side pixel columns with a drip.")]
    [Range(0f, 1f)] public float dripShare = 0.55f;
    [Tooltip("Longest drip, in texture pixels below the lip.")]
    [Range(0f, 6f)] public float dripLength = 4f;
    [Tooltip("Texture pixels per leg cube.")]
    [Range(4f, 16f)] public float legPixels = 8f;
    [Tooltip("Share of the leg side taken by the slab lip.")]
    [Range(0.1f, 0.5f)] public float lipShare = 0.3f;

    [Header("World colours (sRGB)")]
    public Color grass = new Color32(0x62, 0xC2, 0x34, 0xFF);
    public Color grassDark = new Color32(0x3A, 0x8A, 0x2A, 0xFF);
    [Range(0f, 1f)] public float grassFlowers = 0.6f;
    public Color dirt = new Color32(0x8A, 0x5A, 0x36, 0xFF);
    public Color water = new Color32(0x1E, 0x7E, 0xF0, 0xFF);
    public Color waterLight = new Color32(0x9A, 0xE4, 0xFF, 0xFF);
    public float waterSpeed = 1f;

    [Header("Light (camera relative)")]
    public bool litRight = false;
    public float lightUp = 1f, lightSide = 0.75f, lightBack = 0.5f;
    [Tooltip("Block size of the repainted scene rocks (world units).")]
    public float rockBlock = 1.6f;

    [Header("Guide line over the next leg (DynamicPathGenerator.guideTrailColor)")]
    public Color guideColor = new Color(1f, 1f, 1f, 0.9f);

    [Header("Backdrop")]
    public Material backdropMaterial;
    public float yawParallax = 0.003f;
    public float travelParallax = 0.004f;

    [Header("Cliffs while in the checkpoint: block mesas, further back and longer")]
    public bool restyleRockLayout = true;
    public PixelCheckpointController.RockLayout voxelRocks = new PixelCheckpointController.RockLayout
    {
        wallChance = 0.95f, wallsPerCell = 2, wallWidth = new Vector2(20f, 38f), wallTopRange = new Vector2(-24f, -5f),
        wallClearance = 36f, floatingChance = 0f, floatingPerCell = 0, floatingWidth = new Vector2(3f, 6f),
        floatingHeightRange = new Vector2(-20f, -8f), floatingClearance = 6f, debrisPerCell = 0, futureReach = 90f, futureDrop = 26f,
    };
    [Range(0f, 0.9f)] public float voxelVeil = 0.02f;
    [Range(0f, 1f)] public float voxelHazeMax = 0.25f;
    public float fogEaseSeconds = 1.5f;
    public bool ownCliffs = true;
    [Tooltip("The block material of the mesas and props (VoxelCheckpoint/Block).")]
    public Material blockMaterial;
    [Tooltip("How many of the scene's own rocks stay in the mix (repainted as blocks), per 6 mesa templates.")]
    [Range(0, 6)] public int sceneRocksInMix = 1;

    [Header("Props on the mesa tops")]
    public bool dressRocks = true;
    [Tooltip("World size of one prop voxel.")]
    public float propVoxel = 0.42f;
    [Range(0, 20)] public int treesMin = 6, treesMax = 11;
    [Range(0, 4)] public int housesMin = 0, housesMax = 2;
    [Range(0, 8)] public int flowersMin = 2, flowersMax = 4;
    [Range(0, 10)] public int patchesMin = 3, patchesMax = 6;
    [Range(0, 6)] public int bushesMin = 1, bushesMax = 3;
    [Range(0f, 1f)] public float pipeChance = 0.35f;
    [Range(0f, 1f)] public float blockRowChance = 0.3f;
    public float sink = 0.05f;

    [Header("Pixel bits")]
    public Material sparkMaterial;
    [Range(0, 400)] public int bitCount = 90;
    public float bitRate = 8f;
    public Vector3 bitBox = new Vector3(30f, 6f, 30f);
    public Vector2 bitSize = new Vector2(0.12f, 0.22f);
    public float runnerBitsPerUnit = 0.8f;

    static readonly int LookId = Shader.PropertyToID("_VxLook");
    static readonly int TopsId = Shader.PropertyToID("_VxLegTops");
    static readonly int BricksId = Shader.PropertyToID("_VxLegBricks");
    static readonly int GrassId = Shader.PropertyToID("_VxGrass");
    static readonly int GrassDarkId = Shader.PropertyToID("_VxGrassDark");
    static readonly int DirtId = Shader.PropertyToID("_VxDirt");
    static readonly int WaterId = Shader.PropertyToID("_VxWater");
    static readonly int WaterLightId = Shader.PropertyToID("_VxWaterLight");
    static readonly int ParamsId = Shader.PropertyToID("_VxParams");
    static readonly int LightDirId = Shader.PropertyToID("_VxLightDir");
    static readonly int BackdropId = Shader.PropertyToID("_VxBackdrop");

    readonly Vector4[] topsBuf = new Vector4[10];
    readonly Vector4[] bricksBuf = new Vector4[6];

    PixelCheckpointController.RockLayout originalRocks;
    bool rocksVx, vxOnScreen;
    Camera cam;
    GameObject backdrop;
    MeshRenderer backdropRenderer;
    ChasmFog chasm;
    float sceneVeil, sceneHazeMax, fogBlend = -1f, entryVeil, entryHaze;
    bool fogCaptured;
    Color sceneGuideColor;
    bool guideCaptured, guideVx;

    GameObject[] cliffTemplates, sceneWallPrefabs;
    GameObject cliffRoot;
    bool cliffsOn;
    readonly Dictionary<string, List<Vector4>> cliffTops = new Dictionary<string, List<Vector4>>();   // per template: x, z, top y, flat (local)
    readonly List<Mesh> madeMeshes = new List<Mesh>();
    Mesh[] treeMeshes, houseMeshes, flowerMeshes, patchMeshes, bushMeshes, pipeMeshes, rowMeshes;
    readonly List<Vector3> candidates = new List<Vector3>();
    readonly List<Vector3> flatCandidates = new List<Vector3>();
    readonly List<Vector3> placed = new List<Vector3>();
    readonly List<float> placedR = new List<float>();

    GameObject bits, runnerBits;
    ParticleSystem bitSystem, runnerBitSystem;

    public bool VoxelOnScreen => vxOnScreen;
    public bool VoxelRockLayout => rocksVx;

    void Awake()
    {
        if (director == null) director = FindFirstObjectByType<LookTransitionDirector>();
        if (background == null) background = FindFirstObjectByType<PathBackgroundGenerator>();
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (runner == null) runner = FindFirstObjectByType<CubeRailRunner>();
        if (rig == null) rig = FindFirstObjectByType<CameraParentRotator>();
        if (flower == null) flower = FindFirstObjectByType<FlowerCheckpointController>();
        if (story == null) story = FindFirstObjectByType<StorybookCheckpointController>();
        if (pixel == null) pixel = FindFirstObjectByType<PixelCheckpointController>();
        if (toon == null) toon = FindFirstObjectByType<ToonCheckpointController>();
        PushPalette();
        Shader.SetGlobalVector(LookId, Vector4.zero);
    }

    void Start()
    {
        cam = rig != null && rig.boomCamera != null ? rig.boomCamera : Camera.main;
        if (background != null) originalRocks = PixelCheckpointController.RockLayout.From(background);
        BuildBackdrop();
        BuildPropMeshes();
        BuildCliffs();
        BuildBits();
    }

    void OnDisable()
    {
        Shader.SetGlobalVector(LookId, Vector4.zero);
        if (background != null) background.RockTopBuilt -= DressRock;
        if (rocksVx && background != null && originalRocks != null) originalRocks.ApplyTo(background);
        if (cliffsOn && background != null) background.wallPrefabs = sceneWallPrefabs;
        cliffsOn = false;
        rocksVx = false;
        if (guideVx && pathGen != null) pathGen.guideTrailColor = sceneGuideColor;
        guideVx = false;
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

    bool IsVoxel(AtmosphereLook l) =>
        l != null && voxelLook != null && (l == voxelLook || (!string.IsNullOrEmpty(voxelLook.sourceScene) && l.sourceScene == voxelLook.sourceScene));

    void LateUpdate()
    {
        AtmosphereLook now = director != null ? director.CurrentLook : null;
        AtmosphereLook next = director != null ? director.NextLook : null;
        bool a = IsVoxel(now), b = IsVoxel(next);
        vxOnScreen = alwaysVoxel || a || b;
        bool zone = alwaysVoxel || (next != null ? b : a);
        Shader.SetGlobalVector(LookId, new Vector4(a ? 1f : 0f, b ? 1f : 0f, alwaysVoxel ? 1f : 0f, 0f));

        PushPalette();
        PushLight();
        UpdateGuide();
        UpdateRocks(zone);
        UpdateBackdrop();
        UpdateFog();
        UpdateBits();
    }

    // ------------------------------------------------------------------ globals

    static Vector4 Lin(Color c, float a) { Color l = c.linear; return new Vector4(l.r, l.g, l.b, a); }

    void PushPalette()
    {
        for (int i = 0; i < topsBuf.Length; i++)
            topsBuf[i] = Lin(legTops != null && legTops.Length > 0 ? legTops[i % legTops.Length] : Color.yellow, 1f);
        for (int i = 0; i < bricksBuf.Length; i++)
            bricksBuf[i] = Lin(legBricks != null && legBricks.Length > 0 ? legBricks[i % legBricks.Length] : Color.red, 1f);
        Shader.SetGlobalVectorArray(TopsId, topsBuf);
        Shader.SetGlobalVectorArray(BricksId, bricksBuf);
        Shader.SetGlobalVector(GrassId, Lin(grass, grassFlowers));
        Shader.SetGlobalVector(GrassDarkId, Lin(grassDark, 1f));
        Shader.SetGlobalVector(DirtId, Lin(dirt, 1f));
        Shader.SetGlobalVector(WaterId, Lin(water, waterSpeed));
        Shader.SetGlobalVector(WaterLightId, Lin(waterLight, 1f));
        Shader.SetGlobalVector(ParamsId, new Vector4(dripShare, dripLength, legPixels, lipShare));
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
        Shader.SetGlobalVector(LightDirId, new Vector4(l.x, l.y, l.z, rockBlock));
    }

    // ------------------------------------------------------------------ guide

    void UpdateGuide()
    {
        if (pathGen == null) return;
        if (!guideCaptured) { sceneGuideColor = pathGen.guideTrailColor; guideCaptured = true; }
        if (vxOnScreen)
        {
            pathGen.guideTrailColor = guideColor;
            guideVx = true;
        }
        else if (guideVx)
        {
            if (flower != null && flower.isActiveAndEnabled && flower.FlowerOnScreen) pathGen.guideTrailColor = flower.guideColor;
            else if (story != null && story.isActiveAndEnabled && story.StoryOnScreen) pathGen.guideTrailColor = story.guideColor;
            else if (pixel != null && pixel.isActiveAndEnabled && pixel.PixelOnScreen) pathGen.guideTrailColor = pixel.guideColor;
            else if (toon != null && toon.isActiveAndEnabled && toon.ToonOnScreen) pathGen.guideTrailColor = toon.guideColor;
            else pathGen.guideTrailColor = sceneGuideColor;
            guideVx = false;
        }
    }

    // ------------------------------------------------------------------ rocks + fog

    void UpdateRocks(bool zone)
    {
        if (background == null || originalRocks == null) return;
        if (zone == rocksVx) return;
        rocksVx = zone;
        background.RockTopBuilt -= DressRock;
        if (zone && dressRocks) background.RockTopBuilt += DressRock;
        if (restyleRockLayout)
        {
            if (zone) voxelRocks.ApplyTo(background);
            else if ((flower == null || !flower.FlowerRockLayout) && (story == null || !story.StoryRockLayout) && (pixel == null || !pixel.PixelRockLayout))
                originalRocks.ApplyTo(background);
        }
        if (ownCliffs && cliffTemplates != null)
        {
            if (zone)
            {
                sceneWallPrefabs = background.wallPrefabs;
                // The mesas, plus a few of the scene's own rocks (repainted as blocks by VoxelRock.hlsl).
                var mix = new List<GameObject>(cliffTemplates);
                if (sceneWallPrefabs != null)
                    for (int i = 0; i < sceneRocksInMix && i < sceneWallPrefabs.Length; i++)
                        if (sceneWallPrefabs[i] != null) mix.Add(sceneWallPrefabs[i]);
                background.wallPrefabs = mix.ToArray();
                cliffsOn = true;
            }
            else if (cliffsOn) { background.wallPrefabs = sceneWallPrefabs; cliffsOn = false; }
        }
        background.RebuildOutOfSight();
    }

    void UpdateFog()
    {
        if (chasm == null) chasm = ChasmFog.Active;
        if (chasm == null) return;
        if (!fogCaptured) { sceneVeil = chasm.veil; sceneHazeMax = chasm.hazeMax; fogCaptured = true; }
        float target = vxOnScreen ? 1f : 0f;
        if (fogBlend == target && !vxOnScreen) return;
        if (fogBlend <= 0f && vxOnScreen) { entryVeil = chasm.veil; entryHaze = chasm.hazeMax; }   // ease from whatever the last look left
        fogBlend = fogBlend < 0f ? target : Mathf.MoveTowards(fogBlend, target, Time.deltaTime / Mathf.Max(0.01f, fogEaseSeconds));
        float k = fogBlend * fogBlend * (3f - 2f * fogBlend);
        if (vxOnScreen)
        {
            chasm.veil = Mathf.Lerp(entryVeil, voxelVeil, k);
            chasm.hazeMax = Mathf.Lerp(entryHaze, voxelHazeMax, k);
        }
        else
        {
            chasm.veil = Mathf.Lerp(sceneVeil, voxelVeil, k);
            chasm.hazeMax = Mathf.Lerp(sceneHazeMax, voxelHazeMax, k);
        }
    }

    // ------------------------------------------------------------------ backdrop

    Mesh Quad(string name, bool horizontal)
    {
        var m = new Mesh { name = name };
        m.vertices = horizontal
            ? new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) }
            : new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        return m;
    }

    void BuildBackdrop()
    {
        if (backdropMaterial == null || cam == null) return;
        backdrop = new GameObject("VoxelBackdrop");
        backdrop.transform.SetParent(cam.transform, false);
        backdrop.transform.localPosition = new Vector3(0f, 0f, Mathf.Min(cam.farClipPlane * 0.5f, 200f));
        backdrop.AddComponent<MeshFilter>().sharedMesh = Quad("VoxelBackdropQuad", false);
        backdropRenderer = backdrop.AddComponent<MeshRenderer>();
        backdropRenderer.sharedMaterial = backdropMaterial;
        backdropRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        backdropRenderer.receiveShadows = false;
        backdropRenderer.enabled = false;
    }

    void UpdateBackdrop()
    {
        if (backdropRenderer == null) return;
        backdropRenderer.enabled = vxOnScreen;
        if (!vxOnScreen) return;
        float yaw = cam.transform.eulerAngles.y;
        Vector3 cp = cam.transform.position;
        float tx = Mathf.Repeat(Vector3.Dot(cp, Flat(cam.transform.right)) * travelParallax, 1000f);
        float ty = Mathf.Repeat(cp.y * travelParallax, 1000f);
        Shader.SetGlobalVector(BackdropId, new Vector4(-yaw * yawParallax, Time.time, tx, ty));
    }

    // ------------------------------------------------------------------ pixel bits

    ParticleSystem MakeBitSystem(string name, int max, out GameObject go)
    {
        go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startSize = new ParticleSystem.MinMaxCurve(bitSize.x, bitSize.y);
        var keys = new GradientColorKey[Mathf.Min(8, legTops.Length)];
        for (int i = 0; i < keys.Length; i++) keys[i] = new GradientColorKey(legTops[i], (i + 1f) / keys.Length);
        var g = new Gradient { mode = GradientMode.Fixed };
        g.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var life = new Gradient();
        life.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(life);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = sparkMaterial;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    void BuildBits()
    {
        if (sparkMaterial == null || bitCount <= 0 || legTops == null || legTops.Length == 0) return;
        bitSystem = MakeBitSystem("VoxelBits", bitCount, out bits);
        var main = bitSystem.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
        main.gravityModifier = -0.01f;
        var em = bitSystem.emission;
        em.rateOverTime = bitRate;
        var sh = bitSystem.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = bitBox;
        em.enabled = false;
        bitSystem.Play();

        if (runnerBitsPerUnit > 0f)
        {
            runnerBitSystem = MakeBitSystem("VoxelRunnerBits", 60, out runnerBits);
            var rm = runnerBitSystem.main;
            rm.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            rm.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            rm.startSize = new ParticleSystem.MinMaxCurve(bitSize.x * 0.8f, bitSize.y);
            rm.gravityModifier = 0.6f;
            var rem = runnerBitSystem.emission;
            rem.rateOverTime = 0f;
            rem.rateOverDistance = runnerBitsPerUnit;
            var rsh = runnerBitSystem.shape;
            rsh.shapeType = ParticleSystemShapeType.Hemisphere;
            rsh.radius = 0.3f;
            rsh.rotation = new Vector3(-90f, 0f, 0f);
            rem.enabled = false;
            runnerBitSystem.Play();
        }
    }

    void UpdateBits()
    {
        if (runner == null) return;
        Vector3 rp = runner.transform.position;
        if (bitSystem != null)
        {
            bits.transform.position = rp + Vector3.up * 4f;
            var em = bitSystem.emission;
            if (em.enabled != vxOnScreen) em.enabled = vxOnScreen;
        }
        if (runnerBitSystem != null)
        {
            runnerBits.transform.position = rp;
            var em = runnerBitSystem.emission;
            if (em.enabled != vxOnScreen) em.enabled = vxOnScreen;
        }
    }

    // ------------------------------------------------------------------ props on the mesa tops

    /// <summary>Called by PathBackgroundGenerator for every rock top it places while the checkpoint is on.</summary>
    void DressRock(Transform holder, bool wall)
    {
        if (blockMaterial == null || treeMeshes == null || holder == null || holder.childCount == 0) return;
        Transform visual = holder.GetChild(0);
        if (!cliffTops.TryGetValue(visual.name, out List<Vector4> tops)) return;
        Vector3 hp = holder.position;
        var rng = new System.Random(Mathf.RoundToInt(hp.x * 7.31f) * 73856093 ^ Mathf.RoundToInt(hp.z * 3.17f) * 19349663 ^ Mathf.RoundToInt(hp.y) * 83492791);

        candidates.Clear();
        flatCandidates.Clear();
        foreach (Vector4 t in tops)
        {
            Vector3 p = visual.TransformPoint(new Vector3(t.x, t.z, t.y));
            candidates.Add(p);
            if (t.w > 0.5f) flatCandidates.Add(p);
        }
        if (candidates.Count == 0) return;
        placed.Clear();
        placedR.Clear();
        float cell = visual.lossyScale.x / 16f;   // world size of a mesa block

        int houses = rng.Next(housesMin, housesMax + 1);
        for (int i = 0; i < houses; i++) Place(holder, houseMeshes[rng.Next(houseMeshes.Length)], propVoxel, 2.6f, flatCandidates, cell, rng, 0f);
        if (rng.NextDouble() < pipeChance) Place(holder, pipeMeshes[rng.Next(pipeMeshes.Length)], propVoxel * 1.5f, 1.4f, flatCandidates, cell, rng, 0f);
        int trees = rng.Next(treesMin, treesMax + 1);
        for (int i = 0; i < trees; i++) Place(holder, treeMeshes[rng.Next(treeMeshes.Length)], propVoxel * (0.85f + 0.4f * (float)rng.NextDouble()), 1.6f, candidates, cell, rng, 0f);
        int bushes = rng.Next(bushesMin, bushesMax + 1);
        for (int i = 0; i < bushes; i++) Place(holder, bushMeshes[rng.Next(bushMeshes.Length)], propVoxel, 1.2f, candidates, cell, rng, 0f);
        int flowers = rng.Next(flowersMin, flowersMax + 1);
        for (int i = 0; i < flowers; i++) Place(holder, flowerMeshes[rng.Next(flowerMeshes.Length)], propVoxel * 0.9f, 0.7f, candidates, cell, rng, 0f);
        int patches = rng.Next(patchesMin, patchesMax + 1);
        for (int i = 0; i < patches; i++) Place(holder, patchMeshes[rng.Next(patchMeshes.Length)], propVoxel * 0.8f, 0.9f, candidates, cell, rng, 0f, true);
        if (rng.NextDouble() < blockRowChance) Place(holder, rowMeshes[rng.Next(rowMeshes.Length)], propVoxel * 2.6f, 1.6f, candidates, cell, rng, 3.2f, true);
    }

    void Place(Transform holder, Mesh mesh, float scale, float footprint, List<Vector3> from, float cell, System.Random rng, float lift, bool overlap = false)
    {
        if (mesh == null || from.Count == 0) return;
        Vector3 ext = mesh.bounds.extents * scale;
        float r = Mathf.Max(ext.x, ext.z) * 0.8f * footprint / 1.6f + 0.2f;
        for (int tries = 0; tries < 14; tries++)
        {
            Vector3 p = from[rng.Next(from.Count)];
            bool ok = true;
            if (!overlap)
                for (int j = 0; j < placed.Count; j++)
                {
                    float g = r + placedR[j];
                    if ((new Vector2(p.x - placed[j].x, p.z - placed[j].z)).sqrMagnitude < g * g) { ok = false; break; }
                }
            if (!ok) continue;
            if (!overlap) { placed.Add(p); placedR.Add(r); }
            var go = new GameObject(mesh.name);
            go.transform.SetParent(holder, true);
            Quaternion rot = holder.rotation * Quaternion.Euler(0f, 90f * rng.Next(4), 0f);
            go.transform.SetPositionAndRotation(p + Vector3.up * (lift - sink), rot);
            go.transform.localScale = Vector3.one * scale;   // the holder is at full size when its rock is placed
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = blockMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return;
        }
    }

    Mesh Keep(Mesh m) { madeMeshes.Add(m); return m; }

    static Color H(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color c); return c; }

    // ------------------------------------------------------------------ the mesas

    // Block mesas on a 16 x 16 grid, unit footprint, base at y = 0, about 2.2 high (one block = 1/16). Ragged rounded
    // outline, two or three terraces, lower ledges round the rim; per block from the top: grass, two dirt, then strata of
    // brick / sandstone / dirt / cobble four blocks deep. Templates sit under an
    // inactive root (never drawn); the generator stacks them downward into the mist.
    void BuildCliffs()
    {
        if (!ownCliffs || background == null || blockMaterial == null) return;
        cliffRoot = new GameObject("VoxelCliffTemplates");
        cliffRoot.SetActive(false);
        cliffRoot.transform.SetParent(transform, false);
        const int N = 16, HMax = 40;
        Color[] strata = { H("C2622E"), H("9A3A26"), H("E0B26A"), H("8A5A36"), H("B04A2A"), H("7E7E88"), H("D98A3A") };
        int[] strataKind = { VoxelMesh.Brick, VoxelMesh.Brick, VoxelMesh.Sand, VoxelMesh.Dirt, VoxelMesh.Brick, VoxelMesh.Stone, VoxelMesh.Sand };
        cliffTemplates = new GameObject[6];
        var rng = new System.Random(57);
        for (int k = 0; k < cliffTemplates.Length; k++)
        {
            var vm = new VoxelMesh(N, HMax, N, 0, 0, 0);
            var height = new int[N, N];
            float sx = (float)rng.NextDouble() * 50f, sz = (float)rng.NextDouble() * 50f;
            int baseH = 28 + rng.Next(5);
            float R = 0.44f + 0.04f * (float)rng.NextDouble();
            int strataShift = rng.Next(7);
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N - 0.5f, dz = (z + 0.5f) / N - 0.5f;
                    float d = Mathf.Lerp(Mathf.Sqrt(dx * dx + dz * dz), Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)), 0.45f);
                    d += (Mathf.PerlinNoise(sx + x * 0.3f, sz + z * 0.3f) - 0.5f) * 0.12f;
                    if (d > R) { height[x, z] = 0; continue; }
                    float n = Mathf.PerlinNoise(sx + 9f + x * 0.16f, sz + 4f + z * 0.16f);
                    int h = baseH + (n > 0.62f ? 4 : n > 0.48f ? 0 : -3);
                    if (d > R - 0.06f) h -= 7;
                    else if (d > R - 0.12f) h -= 3;
                    height[x, z] = Mathf.Clamp(h, 4, HMax - 1);
                }
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    int h = height[x, z];
                    for (int y = 0; y < h; y++)
                    {
                        int fromTop = h - 1 - y;
                        if (fromTop == 0) vm.Set(x, y, z, strata[3], VoxelMesh.Grass);
                        else if (fromTop <= 2) vm.Set(x, y, z, H("8A5A36"), VoxelMesh.Dirt);
                        else
                        {
                            int band = ((y / 4) + strataShift) % strata.Length;
                            vm.Set(x, y, z, strata[band], strataKind[band]);
                        }
                    }
                }
            // Leaves trailing down some rim columns, under the turf (the reference's dripping greenery).
            for (int i = 0; i < 14; i++)
            {
                int x = rng.Next(N), z = rng.Next(N);
                int h = height[x, z];
                if (h == 0) continue;
                bool rim = x == 0 || z == 0 || x == N - 1 || z == N - 1
                    || height[x + 1, z] < h - 2 || height[x - 1, z] < h - 2 || height[x, z + 1] < h - 2 || height[x, z - 1] < h - 2;
                if (!rim) continue;
                int len = 2 + rng.Next(5);
                for (int y = h - 2; y >= Mathf.Max(0, h - 2 - len); y--) vm.Set(x, y, z, H("3E9A32"), VoxelMesh.Leaves);
            }

            var tops = new List<Vector4>();
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    int h = height[x, z];
                    if (h == 0) continue;
                    bool flat = x > 0 && z > 0 && x < N - 1 && z < N - 1
                        && height[x + 1, z] == h && height[x - 1, z] == h && height[x, z + 1] == h && height[x, z - 1] == h;
                    tops.Add(new Vector4((x + 0.5f) / N - 0.5f, (z + 0.5f) / N - 0.5f, h / (float)N, flat ? 1f : 0f));
                }

            // Mesh in unit footprint: shift so x / z run -0.5..0.5.
            vm.EmitVoxels(1f / N, 8f, grass);
            Mesh mesh = vm.Build("VxCliff" + k);
            var verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] -= new Vector3(0.5f, 0f, 0.5f);
            mesh.vertices = verts;
            mesh.RecalculateBounds();
            Keep(mesh);
            cliffTops["VxCliff" + k + "(Clone)"] = tops;
            var go = new GameObject("VxCliff" + k);
            go.transform.SetParent(cliffRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = blockMaterial;
            cliffTemplates[k] = go;
        }
    }

    // ------------------------------------------------------------------ prop meshes (built once, shared)

    Mesh Done(VoxelMesh vm, string name, float px = 4f)
    {
        vm.EmitVoxels(1f, px, grass);
        Mesh m = vm.Build(name);
        // Centre the voxel grid on its base: voxel (0, 0, 0) spans 0..1, so shift x / z by half a voxel.
        var verts = m.vertices;
        for (int i = 0; i < verts.Length; i++) verts[i] -= new Vector3(0.5f, 0f, 0.5f);
        m.vertices = verts;
        m.RecalculateBounds();
        return Keep(m);
    }

    void BuildPropMeshes()
    {
        if (treeMeshes != null) return;
        var rng = new System.Random(11);
        Color log = H("6B4426"), birch = H("E8E2D2"), stem = H("2E8A2E");

        // ---- trees ----
        var trees = new List<Mesh>();
        Color[] crowns = { H("4CB03A"), H("2F8A3A"), H("E8742A"), H("D23A32"), H("F07AA8"), H("F2C230"), H("2E6A6A"), H("8BD14A") };
        for (int i = 0; i < crowns.Length; i++)
        {
            // Oak: log trunk, two wide leaf layers, a narrower one, a cap.
            var vm = new VoxelMesh(11, 14, 11);
            int trunk = 3 + rng.Next(3);
            vm.Box(0, 0, 0, 0, trunk + 1, 0, i == 5 ? birch : log, VoxelMesh.Log);
            int y0 = trunk;
            int w = i == 6 ? 3 : 2;
            vm.Box(-w, y0, -w, w, y0 + 1, w, crowns[i], VoxelMesh.Leaves, 0.6f);
            vm.Box(-w + 1, y0 + 2, -w + 1, w - 1, y0 + 3, w - 1, crowns[i], VoxelMesh.Leaves, 1f);
            vm.Box(0, y0 + 4, 0, 0, y0 + 4, 0, crowns[i], VoxelMesh.Leaves, 1f);
            vm.Set(-1, y0 + 4, 0, crowns[i], VoxelMesh.Leaves, 1f); vm.Set(1, y0 + 4, 0, crowns[i], VoxelMesh.Leaves, 1f);
            vm.Set(0, y0 + 4, -1, crowns[i], VoxelMesh.Leaves, 1f); vm.Set(0, y0 + 4, 1, crowns[i], VoxelMesh.Leaves, 1f);
            // Ragged corners.
            for (int c = 0; c < 6; c++) vm.Clear(rng.Next(2) == 0 ? -w : w, y0 + rng.Next(2), rng.Next(2) == 0 ? -w : w);
            trees.Add(Done(vm, "VxOak" + i));
        }
        // Lollipop trees (tall thin trunk, round crown) - the reference's middle distance.
        Color[] lolly = { H("5CB848"), H("E05A3A"), H("3E8A70") };
        for (int i = 0; i < lolly.Length; i++)
        {
            var vm = new VoxelMesh(9, 16, 9);
            vm.Box(0, 0, 0, 0, 9, 0, log, VoxelMesh.Log);
            vm.Blob(new Vector3(0.5f, 11.5f, 0.5f), new Vector3(3.2f, 2.8f, 3.2f), lolly[i], VoxelMesh.Leaves, rng, 0.3f, 1f);
            trees.Add(Done(vm, "VxLolly" + i));
        }
        // Spruces.
        for (int i = 0; i < 2; i++)
        {
            var vm = new VoxelMesh(9, 14, 9);
            vm.Box(0, 0, 0, 0, 3, 0, log, VoxelMesh.Log);
            Color sp = i == 0 ? H("1F6A3E") : H("2E7A52");
            for (int l = 0; l < 4; l++)
            {
                int r = 3 - l;
                vm.Box(-r, 2 + l * 2, 0, r, 3 + l * 2, 0, sp, VoxelMesh.Leaves, 0.5f);
                vm.Box(0, 2 + l * 2, -r, 0, 3 + l * 2, r, sp, VoxelMesh.Leaves, 0.5f);
                if (r > 1) vm.Box(-r + 1, 2 + l * 2, -r + 1, r - 1, 2 + l * 2, r - 1, sp, VoxelMesh.Leaves, 0.5f);
            }
            vm.Set(0, 10, 0, sp, VoxelMesh.Leaves, 1f);
            trees.Add(Done(vm, "VxSpruce" + i));
        }
        // Mushroom trees (Mario): pale stem, spotted cap.
        Color[] caps = { H("D8282A"), H("E8742A"), H("3A7AE0") };
        for (int i = 0; i < caps.Length; i++)
        {
            var vm = new VoxelMesh(13, 12, 13);
            vm.Box(0, 0, 0, 1, 5, 1, H("F2E6CC"), VoxelMesh.Plain);
            vm.Set(0, 3, 1, H("101010"), VoxelMesh.Eye); vm.Set(1, 3, 1, H("101010"), VoxelMesh.Eye);
            vm.Blob(new Vector3(1f, 7.2f, 1f), new Vector3(4.2f, 2.2f, 4.2f), caps[i], VoxelMesh.Spots, rng, 0.1f, 0.3f);
            for (int z = -4; z <= 5; z++) for (int x = -4; x <= 5; x++)
                if (vm.Filled(x, 6, z) && !(x >= 0 && x <= 1 && z >= 0 && z <= 1)) vm.Clear(x, 5, z);
            trees.Add(Done(vm, "VxShroom" + i));
        }
        treeMeshes = trees.ToArray();

        // ---- houses ----
        var houses = new List<Mesh>();
        {
            // Brick tower with crenellations (the reference's tall red tower).
            var vm = new VoxelMesh(9, 18, 9);
            Color br = H("B4482E");
            vm.Box(-2, 0, -2, 2, 12, 2, br, VoxelMesh.Brick);
            for (int x = -2; x <= 2; x += 2) { vm.Set(x, 13, -2, br, VoxelMesh.Brick); vm.Set(x, 13, 2, br, VoxelMesh.Brick); vm.Set(-2, 13, x, br, VoxelMesh.Brick); vm.Set(2, 13, x, br, VoxelMesh.Brick); }
            vm.Box(-1, 12, -1, 1, 12, 1, H("D9A23A"), VoxelMesh.Sand);
            foreach (int y in new[] { 4, 8, 10 }) { vm.Set(0, y, -2, br, y == 8 ? VoxelMesh.LitWindow : VoxelMesh.Window); vm.Set(2, y, 0, br, VoxelMesh.Window); vm.Set(-2, y, 0, br, y == 4 ? VoxelMesh.LitWindow : VoxelMesh.Window); vm.Set(0, y, 2, br, VoxelMesh.Window); }
            vm.Box(0, 0, -2, 0, 1, -2, H("5A3420"), VoxelMesh.Plank);
            houses.Add(Done(vm, "VxTower"));
        }
        {
            // Blue block house (the reference's blue block), flat roof with a rim, windows.
            var vm = new VoxelMesh(11, 9, 9);
            Color bl = H("3F6FD8");
            vm.Box(-3, 0, -2, 3, 4, 2, bl, VoxelMesh.Stone);
            vm.Box(-4, 5, -3, 4, 5, 3, H("2E52A8"), VoxelMesh.Stone);
            vm.Box(-3, 6, -2, 3, 6, 2, H("6A8FE8"), VoxelMesh.Stone);
            vm.Box(-2, 6, -1, 2, 6, 1, H("F2C230"), VoxelMesh.QBlock);
            for (int x = -2; x <= 2; x += 2) { vm.Set(x, 2, -2, bl, x == 0 ? VoxelMesh.LitWindow : VoxelMesh.Window); vm.Set(x, 2, 2, bl, VoxelMesh.Window); }
            vm.Box(0, 0, -2, 0, 1, -2, H("5A3420"), VoxelMesh.Plank);
            houses.Add(Done(vm, "VxBlueHouse"));
        }
        {
            // Plank cottage with a stepped red roof, a chimney, a lit window.
            var vm = new VoxelMesh(11, 12, 9);
            Color pl = H("C88A4A");
            vm.Box(-3, 0, -2, 3, 3, 2, pl, VoxelMesh.Plank);
            vm.Box(-3, 0, -2, -3, 3, -2, log, VoxelMesh.Log); vm.Box(3, 0, -2, 3, 3, -2, log, VoxelMesh.Log);
            vm.Box(-3, 0, 2, -3, 3, 2, log, VoxelMesh.Log); vm.Box(3, 0, 2, 3, 3, 2, log, VoxelMesh.Log);
            Color roof = H("C83A2E");
            for (int s = 0; s < 4; s++) vm.Box(-4, 4 + s, -3 + s, 4, 4 + s, 3 - s, roof, VoxelMesh.Roof);
            vm.Box(2, 6, 1, 2, 8, 1, H("8A8A92"), VoxelMesh.Stone);
            vm.Set(-1, 2, -2, pl, VoxelMesh.LitWindow); vm.Set(1, 2, -2, pl, VoxelMesh.Window);
            vm.Box(0, 0, -2, 0, 1, -2, H("5A3420"), VoxelMesh.Plank);
            vm.Set(-3, 2, 0, pl, VoxelMesh.Window); vm.Set(3, 2, 0, pl, VoxelMesh.LitWindow);
            houses.Add(Done(vm, "VxCottage"));
        }
        {
            // Little stone castle: crenellated walls, a turret with a red roof and a flag.
            var vm = new VoxelMesh(11, 16, 11);
            Color st = H("A8A8B0");
            vm.Box(-3, 0, -3, 3, 4, 3, st, VoxelMesh.Stone);
            for (int x = -3; x <= 3; x += 2) { vm.Set(x, 5, -3, st, VoxelMesh.Stone); vm.Set(x, 5, 3, st, VoxelMesh.Stone); vm.Set(-3, 5, x, st, VoxelMesh.Stone); vm.Set(3, 5, x, st, VoxelMesh.Stone); }
            vm.Box(-1, 5, -1, 1, 8, 1, st, VoxelMesh.Stone);
            vm.Box(-1, 9, -1, 1, 9, 1, H("D8282A"), VoxelMesh.Roof);
            vm.Set(0, 10, 0, H("D8282A"), VoxelMesh.Roof);
            vm.Box(0, 11, 0, 0, 12, 0, H("5A3420"), VoxelMesh.Log);
            vm.Set(1, 12, 0, H("F2C230"), VoxelMesh.Plain, 1f);
            vm.Box(0, 0, -3, 0, 2, -3, H("4A2A18"), VoxelMesh.Plank);
            vm.Set(-2, 3, -3, st, VoxelMesh.LitWindow); vm.Set(2, 3, -3, st, VoxelMesh.Window); vm.Set(0, 7, -1, st, VoxelMesh.LitWindow);
            houses.Add(Done(vm, "VxCastle"));
        }
        houseMeshes = houses.ToArray();

        // ---- Mario fire flowers ----
        var flowers = new List<Mesh>();
        Color[][] heads = { new[] { H("E8361E"), H("F2A02A"), H("FFF4D8") }, new[] { H("E85AA8"), H("F2C230"), H("FFFFFF") }, new[] { H("8A4AE0"), H("F27A2A"), H("FFF4D8") } };
        for (int i = 0; i < heads.Length; i++)
        {
            var vm = new VoxelMesh(7, 9, 5);
            vm.Box(0, 0, 0, 0, 3, 0, stem, VoxelMesh.Plain, 0.3f);
            vm.Box(-2, 1, 0, -1, 1, 0, H("3EB03A"), VoxelMesh.Leaves, 0.3f); vm.Box(1, 2, 0, 2, 2, 0, H("3EB03A"), VoxelMesh.Leaves, 0.3f);
            vm.Box(-2, 4, 0, 2, 7, 0, heads[i][0], VoxelMesh.Petal, 0.8f);
            vm.Clear(-2, 4, 0); vm.Clear(2, 4, 0); vm.Clear(-2, 7, 0); vm.Clear(2, 7, 0);
            vm.Box(-1, 5, 0, 1, 6, 0, heads[i][1], VoxelMesh.Petal, 0.8f);
            vm.Set(0, 5, 0, heads[i][2], VoxelMesh.Plain, 0.8f); vm.Set(0, 6, 0, heads[i][2], VoxelMesh.Plain, 0.8f);
            vm.Set(-1, 6, -1, H("101010"), VoxelMesh.Eye, 0.8f); vm.Set(1, 6, -1, H("101010"), VoxelMesh.Eye, 0.8f);
            vm.Set(-1, 6, 1, H("101010"), VoxelMesh.Eye, 0.8f); vm.Set(1, 6, 1, H("101010"), VoxelMesh.Eye, 0.8f);
            flowers.Add(Done(vm, "VxFireFlower" + i));
        }
        flowerMeshes = flowers.ToArray();

        // ---- meadow patches: tufts of grass and small pixel flowers ----
        var patches = new List<Mesh>();
        Color[] petals = { H("E8361E"), H("F2C230"), H("FFFFFF"), H("F07AA8"), H("5A8AF0"), H("F27A2A"), H("B46AF0") };
        for (int i = 0; i < 5; i++)
        {
            var vm = new VoxelMesh(11, 5, 11);
            for (int j = 0; j < 22; j++)
            {
                int x = rng.Next(-5, 6), z = rng.Next(-5, 6);
                if (rng.NextDouble() < 0.55)
                {
                    int hgt = 1 + rng.Next(2);
                    vm.Box(x, 0, z, x, hgt - 1, z, rng.NextDouble() < 0.5 ? H("7ED04A") : H("4CA83A"), VoxelMesh.Leaves, 0.8f);
                }
                else
                {
                    vm.Set(x, 0, z, stem, VoxelMesh.Plain, 0.3f);
                    vm.Set(x, 1, z, petals[rng.Next(petals.Length)], VoxelMesh.Petal, 0.8f);
                }
            }
            patches.Add(Done(vm, "VxPatch" + i, 2f));
        }
        patchMeshes = patches.ToArray();

        // ---- bushes ----
        var bushes = new List<Mesh>();
        for (int i = 0; i < 3; i++)
        {
            var vm = new VoxelMesh(11, 6, 9);
            Color bc = i == 2 ? H("2F8A3A") : H("4CB03A");
            vm.Blob(new Vector3(0.5f, 1.2f, 0.5f), new Vector3(3.8f, 2.4f, 2.6f), bc, VoxelMesh.Leaves, rng, 0.3f, 0.4f);
            if (i == 1) for (int j = 0; j < 6; j++) { int x = rng.Next(-3, 4), y = rng.Next(1, 3); vm.Set(x, y, -3, H("E8361E"), VoxelMesh.Plain); }
            bushes.Add(Done(vm, "VxBush" + i));
        }
        bushMeshes = bushes.ToArray();

        // ---- warp pipes (green, and one orange) ----
        var pipes = new List<Mesh>();
        Color[] pc = { H("2EA83A"), H("2EA83A"), H("E8742A") };
        for (int i = 0; i < pc.Length; i++)
        {
            var vm = new VoxelMesh(1, 1, 1);
            float hgt = 4f + i * 1.5f;
            vm.SolidBox(new Vector3(-1.6f, 0f, -1.6f), new Vector3(1.6f, hgt, 1.6f), pc[i], VoxelMesh.Pipe);
            vm.SolidBox(new Vector3(-2.1f, hgt, -2.1f), new Vector3(2.1f, hgt + 1.6f, 2.1f), pc[i], VoxelMesh.Pipe);
            pipes.Add(Keep(vm.Build("VxPipe" + i)));
        }
        pipeMeshes = pipes.ToArray();

        // ---- floating block rows: brick, "?", brick (one block = 1 unit, scaled by propVoxel x 2.6) ----
        var rows = new List<Mesh>();
        string[] layouts = { "BQB", "BQBQB", "Q", "BBQ" };
        foreach (string l in layouts)
        {
            var vm = new VoxelMesh(1, 1, 1);
            float x0 = -l.Length * 0.5f;
            for (int i = 0; i < l.Length; i++)
            {
                bool q = l[i] == 'Q';
                vm.SolidBox(new Vector3(x0 + i, 0f, -0.5f), new Vector3(x0 + i + 1f, 1f, 0.5f), q ? H("F2B020") : H("B4482E"), q ? VoxelMesh.QBlock : VoxelMesh.Brick, 8f);
            }
            rows.Add(Keep(vm.Build("VxBlocks_" + l)));
        }
        rowMeshes = rows.ToArray();
    }
}
