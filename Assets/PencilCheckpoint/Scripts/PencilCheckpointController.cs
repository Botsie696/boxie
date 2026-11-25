using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Pencil checkpoint "Graphite Sketchbook" (MainScene prop camera 3.10 checkpoint 8, right after Brick Meadow, and the whole
/// run of MainScene prop camera Pencil): everything the black-and-white pencil-on-rough-paper look needs besides the look
/// asset (Look_Pencil: colours, sky, fog, sun, post).
///
///   Where      _PcLook (x: the look in force is pencil, y: the look being wiped in is, z: always) - every pencil shader
///              (PencilCommon.hlsl) switches along the look wipe with it
///   Page       while the look is on screen the camera draws with PencilCP_Renderer: PencilLines (doubled sketch outlines
///              from depth / normals, before post) + PencilPaper (grey on rough paper, after post)
///   Legs       PencilLeg.hlsl: almost hollow drawn legs, hatching by the light, four sketchbook pages across the level
///              (_PcStage: quarters of the level's legs; a cycle in the pencil scene), art friezes on a share of the legs
///   Runner     the totem's materials swapped for hollow pencil ones while the look is in force (originals put back)
///   Trail      PixelFaceTrail's PencilRibbon: graphite rubbed onto the face behind the runner; PencilRunnerFX (same
///              GameObject) adds the front trail sketched ahead of the runner, graphite flecks and speed streaks
///   Cliffs     the checkpoint's own tiered rock columns (hatched, strata strokes) set further back; a few scene rocks stay,
///              drawn in graphite too (PencilRock.hlsl)
///   Props      on every column top: a village - half-timbered houses, cottages, towers with a clock, a church with a spire,
///              round trees, Italian cypresses, pines, bushes, grass tufts and flowers (children of the rock: they fade and
///              retire with it, nothing pops in view)
///   Backdrop   paper sky with hatching, a compass-drawn moon, scribbled clouds, jagged mountains, a far town, pencil birds
/// Purely visual: it never touches gameplay, the camera's framing or any asset (renderer features are left as they are).
/// </summary>
[DefaultExecutionOrder(1000)]   // after the voxel / flower / storybook / pixel / toon controllers: its renderer wins while on screen
public class PencilCheckpointController : MonoBehaviour
{
    [Header("The pencil look")]
    [Tooltip("Look_Pencil. The checkpoint is wherever this look is in force (or being wiped in).")]
    public AtmosphereLook pencilLook;
    [Tooltip("The whole run is pencil (MainScene prop camera Pencil).")]
    public bool alwaysPencil;
    [Tooltip("The portal the pencil level starts at (its runway out = the first pencil leg). 3.10: portal 7 = checkpoint 8.")]
    public int pencilPortal = 7;

    [Header("References (found when empty)")]
    public LookTransitionDirector director;
    public PathBackgroundGenerator background;
    public DynamicPathGenerator pathGen;
    public CubeRailRunner runner;
    public CameraParentRotator rig;
    public VoxelCheckpointController voxel;
    public FlowerCheckpointController flower;
    public StorybookCheckpointController story;
    public PixelCheckpointController pixel;
    public ToonCheckpointController toon;

    [Header("Paper and graphite (sRGB)")]
    public Color paper = new Color32(0xF1, 0xEE, 0xE6, 0xFF);
    [Range(0f, 0.5f)] public float paperTooth = 0.16f;
    public Color graphite = new Color32(0x26, 0x26, 0x2A, 0xFF);
    [Range(0.3f, 1f)] public float strokeDarkness = 0.92f;

    [Header("Strokes")]
    [Tooltip("Hatch lines per unit on the legs.")]
    [Range(4f, 30f)] public float hatchPerUnit = 7f;
    [Tooltip("Half width of a pencil line on the legs (units).")]
    [Range(0.003f, 0.06f)] public float lineWidth = 0.034f;
    [Tooltip("Share of the legs drawn as a whole-leg art frieze (fleur-de-lis, acanthus, arcade, Leonardo, swags).")]
    [Range(0f, 1f)] public float artShare = 0.35f;
    [Tooltip("How far the doubled sketch strokes wander (units).")]
    [Range(0f, 0.06f)] public float sketchWobble = 0.018f;
    [Tooltip("Sketchbook pages per level (sketch, drafting, old town, dungeon).")]
    [Range(1, 4)] public int stages = 4;
    [Tooltip("Pencil scene: legs per page before the next page.")]
    public int cycleLegs = 11;

    [Header("Light (camera relative)")]
    public bool litRight = false;
    public float lightUp = 1f, lightSide = 0.8f, lightBack = 0.4f;
    [Tooltip("Stroke scale on the rocks / cliffs (world units).")]
    public float rockStroke = 2.6f;

    [Header("Guide line over the next leg (DynamicPathGenerator.guideTrailColor)")]
    public Color guideColor = new Color(0.15f, 0.15f, 0.17f, 0.85f);

    [Header("Renderer")]
    [Tooltip("Renderer index of PencilCP_Renderer on the pipeline assets. -1 = no line / paper passes.")]
    public int pencilRendererIndex = 6;
    [Tooltip("The camera's own renderer index, put back when the look leaves the screen.")]
    public int baseRendererIndex = 1;

    [Header("Runner (swapped while the look is in force)")]
    public Material runnerBody, runnerEyeWhite, runnerPupil, runnerCape;

    [Header("Backdrop")]
    public Material backdropMaterial;
    public float yawParallax = 0.003f;
    public float travelParallax = 0.004f;

    [Header("Cliffs while in the checkpoint: tiered columns, further back")]
    public bool restyleRockLayout = true;
    public PixelCheckpointController.RockLayout pencilRocks = new PixelCheckpointController.RockLayout
    {
        wallChance = 0.95f, wallsPerCell = 2, wallWidth = new Vector2(20f, 36f), wallTopRange = new Vector2(-24f, -5f),
        wallClearance = 35f, floatingChance = 0f, floatingPerCell = 0, floatingWidth = new Vector2(3f, 6f),
        floatingHeightRange = new Vector2(-20f, -8f), floatingClearance = 6f, debrisPerCell = 0, futureReach = 90f, futureDrop = 26f,
    };
    [Range(0f, 0.9f)] public float pencilVeil = 0.03f;
    [Range(0f, 1f)] public float pencilHazeMax = 0.35f;
    public float fogEaseSeconds = 1.5f;
    public bool ownCliffs = true;
    [Tooltip("The material of the cliffs and props (PencilCheckpoint/Prop).")]
    public Material propMaterial;
    [Range(0, 6)] public int sceneRocksInMix = 1;

    [Header("Village on the column tops")]
    public bool dressRocks = true;
    [Tooltip("World size multiplier of the props.")]
    public float propScale = 1.6f;
    [Range(0, 12)] public int housesMin = 3, housesMax = 7;
    [Range(0, 20)] public int treesMin = 6, treesMax = 12;
    [Range(0, 10)] public int bushesMin = 3, bushesMax = 6;
    [Range(0, 16)] public int plantsMin = 6, plantsMax = 12;
    [Range(0f, 1f)] public float landmarkChance = 0.4f;
    public float sink = 0.05f;

    static readonly int LookId = Shader.PropertyToID("_PcLook");
    static readonly int PaperId = Shader.PropertyToID("_PcPaper");
    static readonly int GraphiteId = Shader.PropertyToID("_PcGraphite");
    static readonly int ParamsId = Shader.PropertyToID("_PcParams");
    static readonly int StageId = Shader.PropertyToID("_PcStage");
    static readonly int LightDirId = Shader.PropertyToID("_PcLightDir");
    static readonly int BackdropId = Shader.PropertyToID("_PcBackdrop");

    PixelCheckpointController.RockLayout originalRocks;
    bool rocksPc, pcOnScreen, pcInForce, rendererPc;
    Camera cam;
    UniversalAdditionalCameraData camData;
    GameObject backdrop;
    MeshRenderer backdropRenderer;
    ChasmFog chasm;
    float sceneVeil, sceneHazeMax, fogBlend = -1f, entryVeil, entryHaze;
    bool fogCaptured;
    Color sceneGuideColor;
    bool guideCaptured, guidePc;

    readonly List<Renderer> runnerRenderers = new List<Renderer>();
    readonly List<Material> runnerOriginals = new List<Material>();
    readonly List<Material> runnerPencil = new List<Material>();
    bool runnerSwapped;

    GameObject[] cliffTemplates, sceneWallPrefabs;
    GameObject cliffRoot;
    bool cliffsOn;
    readonly Dictionary<string, List<Vector4>> cliffTops = new Dictionary<string, List<Vector4>>();   // per template: x, z, top y, flat (local)
    readonly List<Mesh> madeMeshes = new List<Mesh>();
    Mesh[] houseMeshes, landmarkMeshes, treeMeshes, bushMeshes, plantMeshes;
    readonly List<Vector3> candidates = new List<Vector3>();
    readonly List<Vector3> placed = new List<Vector3>();
    readonly List<float> placedR = new List<float>();

    public bool PencilOnScreen => pcOnScreen;
    public bool PencilRockLayout => rocksPc;

    void Awake()
    {
        if (director == null) director = FindFirstObjectByType<LookTransitionDirector>();
        if (background == null) background = FindFirstObjectByType<PathBackgroundGenerator>();
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (runner == null) runner = FindFirstObjectByType<CubeRailRunner>();
        if (rig == null) rig = FindFirstObjectByType<CameraParentRotator>();
        if (voxel == null) voxel = FindFirstObjectByType<VoxelCheckpointController>();
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
        if (cam != null) camData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (background != null) originalRocks = PixelCheckpointController.RockLayout.From(background);
        CollectRunner();
        BuildBackdrop();
        BuildPropMeshes();
        BuildCliffs();
    }

    void OnDisable()
    {
        Shader.SetGlobalVector(LookId, Vector4.zero);
        if (camData != null && rendererPc) camData.SetRenderer(baseRendererIndex);
        rendererPc = false;
        SwapRunner(false);
        if (background != null) background.RockTopBuilt -= DressRock;
        if (rocksPc && background != null && originalRocks != null) originalRocks.ApplyTo(background);
        if (cliffsOn && background != null) background.wallPrefabs = sceneWallPrefabs;
        cliffsOn = false;
        rocksPc = false;
        if (guidePc && pathGen != null) pathGen.guideTrailColor = sceneGuideColor;
        guidePc = false;
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

    bool IsPencil(AtmosphereLook l) =>
        l != null && pencilLook != null && (l == pencilLook || (!string.IsNullOrEmpty(pencilLook.sourceScene) && l.sourceScene == pencilLook.sourceScene));

    void LateUpdate()
    {
        AtmosphereLook now = director != null ? director.CurrentLook : null;
        AtmosphereLook next = director != null ? director.NextLook : null;
        bool a = IsPencil(now), b = IsPencil(next);
        pcOnScreen = alwaysPencil || a || b;
        pcInForce = alwaysPencil || (a && next == null) || (b && a);
        bool zone = alwaysPencil || (next != null ? b : a);
        Shader.SetGlobalVector(LookId, new Vector4(a ? 1f : 0f, b ? 1f : 0f, alwaysPencil ? 1f : 0f, 0f));

        PushPalette();
        PushLight();
        PushStage();
        UpdateRenderer();
        UpdateGuide();
        UpdateRocks(zone);
        UpdateBackdrop();
        UpdateFog();
        // The runner turns into a drawing once the wipe is past it (the look is in force), and back when it leaves.
        bool past = next != null && WipePastRunner();
        SwapRunner(alwaysPencil || (a && (next == null || b || !past)) || (b && past));
    }

    // ------------------------------------------------------------------ globals

    static Vector4 Lin(Color c, float a) { Color l = c.linear; return new Vector4(l.r, l.g, l.b, a); }

    void PushPalette()
    {
        Shader.SetGlobalVector(PaperId, Lin(paper, paperTooth));
        Shader.SetGlobalVector(GraphiteId, Lin(graphite, strokeDarkness));
        Shader.SetGlobalVector(ParamsId, new Vector4(hatchPerUnit, lineWidth, artShare, sketchWobble));
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
        Shader.SetGlobalVector(LightDirId, new Vector4(l.x, l.y, l.z, rockStroke));
    }

    void PushStage()
    {
        if (alwaysPencil || pathGen == null)
        {
            Shader.SetGlobalVector(StageId, new Vector4(0f, 0f, Mathf.Max(1, cycleLegs), stages));
            return;
        }
        float first = pathGen.CheckpointLeg(pencilPortal);
        float end = pathGen.CheckpointLeg(pencilPortal + 1);
        Shader.SetGlobalVector(StageId, new Vector4(first, Mathf.Max(end, first + 4f), 0f, stages));
    }

    // ------------------------------------------------------------------ renderer + guide

    void UpdateRenderer()
    {
        if (camData == null || pencilRendererIndex < 0) return;
        if (pcOnScreen)
        {
            camData.SetRenderer(pencilRendererIndex);   // every frame: the other checkpoint controllers run earlier
            rendererPc = true;
        }
        else if (rendererPc)
        {
            int idx = baseRendererIndex;
            if (flower != null && flower.isActiveAndEnabled && flower.FlowerOnScreen) idx = flower.flowerRendererIndex;
            else if (story != null && story.isActiveAndEnabled && story.StoryOnScreen) idx = story.storyRendererIndex;
            else if (pixel != null && pixel.isActiveAndEnabled && pixel.PixelOnScreen) idx = pixel.pixelRendererIndex;
            else if (toon != null && toon.isActiveAndEnabled && toon.ToonOnScreen) idx = toon.toonRendererIndex;
            camData.SetRenderer(idx);
            rendererPc = false;
        }
    }

    void UpdateGuide()
    {
        if (pathGen == null) return;
        if (!guideCaptured) { sceneGuideColor = pathGen.guideTrailColor; guideCaptured = true; }
        if (pcOnScreen)
        {
            pathGen.guideTrailColor = guideColor;
            guidePc = true;
        }
        else if (guidePc)
        {
            if (voxel != null && voxel.isActiveAndEnabled && voxel.VoxelOnScreen) pathGen.guideTrailColor = voxel.guideColor;
            else if (flower != null && flower.isActiveAndEnabled && flower.FlowerOnScreen) pathGen.guideTrailColor = flower.guideColor;
            else if (story != null && story.isActiveAndEnabled && story.StoryOnScreen) pathGen.guideTrailColor = story.guideColor;
            else if (pixel != null && pixel.isActiveAndEnabled && pixel.PixelOnScreen) pathGen.guideTrailColor = pixel.guideColor;
            else if (toon != null && toon.isActiveAndEnabled && toon.ToonOnScreen) pathGen.guideTrailColor = toon.guideColor;
            else pathGen.guideTrailColor = sceneGuideColor;
            guidePc = false;
        }
    }

    // ------------------------------------------------------------------ runner

    void CollectRunner()
    {
        var totem = FindFirstObjectByType<PlayerTotem>(FindObjectsInactive.Include);
        if (totem == null) return;
        foreach (Renderer r in totem.GetComponentsInChildren<Renderer>(true))
        {
            Material swap = r.name == "EyeWhite" ? runnerEyeWhite : r.name == "Pupil" ? runnerPupil : r.name == "Cape" ? runnerCape : runnerBody;
            if (swap == null || r.sharedMaterial == null) continue;
            runnerRenderers.Add(r);
            runnerOriginals.Add(r.sharedMaterial);
            runnerPencil.Add(swap);
        }
    }

    static readonly int WipeId = Shader.PropertyToID("_LTWipe");

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
            if (runnerRenderers[i] != null) runnerRenderers[i].sharedMaterial = on ? runnerPencil[i] : runnerOriginals[i];
    }

    // ------------------------------------------------------------------ rocks + fog

    void UpdateRocks(bool zone)
    {
        if (background == null || originalRocks == null) return;
        if (zone == rocksPc) return;
        rocksPc = zone;
        background.RockTopBuilt -= DressRock;
        if (zone && dressRocks) background.RockTopBuilt += DressRock;
        if (restyleRockLayout)
        {
            if (zone) pencilRocks.ApplyTo(background);
            else if ((voxel == null || !voxel.VoxelRockLayout) && (flower == null || !flower.FlowerRockLayout)
                     && (story == null || !story.StoryRockLayout) && (pixel == null || !pixel.PixelRockLayout))
                originalRocks.ApplyTo(background);
        }
        if (ownCliffs && cliffTemplates != null)
        {
            if (zone)
            {
                sceneWallPrefabs = background.wallPrefabs;
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
        float target = pcOnScreen ? 1f : 0f;
        if (fogBlend == target && !pcOnScreen) return;
        if (fogBlend <= 0f && pcOnScreen) { entryVeil = chasm.veil; entryHaze = chasm.hazeMax; }
        fogBlend = fogBlend < 0f ? target : Mathf.MoveTowards(fogBlend, target, Time.deltaTime / Mathf.Max(0.01f, fogEaseSeconds));
        float k = fogBlend * fogBlend * (3f - 2f * fogBlend);
        if (pcOnScreen)
        {
            chasm.veil = Mathf.Lerp(entryVeil, pencilVeil, k);
            chasm.hazeMax = Mathf.Lerp(entryHaze, pencilHazeMax, k);
        }
        else
        {
            chasm.veil = Mathf.Lerp(sceneVeil, pencilVeil, k);
            chasm.hazeMax = Mathf.Lerp(sceneHazeMax, pencilHazeMax, k);
        }
    }

    // ------------------------------------------------------------------ backdrop

    Mesh Quad(string name)
    {
        var m = new Mesh { name = name };
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        return m;
    }

    void BuildBackdrop()
    {
        if (backdropMaterial == null || cam == null) return;
        backdrop = new GameObject("PencilBackdrop");
        backdrop.transform.SetParent(cam.transform, false);
        backdrop.transform.localPosition = new Vector3(0f, 0f, Mathf.Min(cam.farClipPlane * 0.5f, 200f));
        backdrop.AddComponent<MeshFilter>().sharedMesh = Quad("PencilBackdropQuad");
        backdropRenderer = backdrop.AddComponent<MeshRenderer>();
        backdropRenderer.sharedMaterial = backdropMaterial;
        backdropRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        backdropRenderer.receiveShadows = false;
        backdropRenderer.enabled = false;
    }

    void UpdateBackdrop()
    {
        if (backdropRenderer == null) return;
        backdropRenderer.enabled = pcOnScreen;
        if (!pcOnScreen) return;
        float yaw = cam.transform.eulerAngles.y;
        Vector3 cp = cam.transform.position;
        float tx = Mathf.Repeat(Vector3.Dot(cp, Flat(cam.transform.right)) * travelParallax, 1000f);
        float ty = Mathf.Repeat(cp.y * travelParallax, 1000f);
        Shader.SetGlobalVector(BackdropId, new Vector4(-yaw * yawParallax, Time.time, tx, ty));
    }

    // ------------------------------------------------------------------ the village on the column tops

    /// <summary>Called by PathBackgroundGenerator for every rock top it places while the checkpoint is on.</summary>
    void DressRock(Transform holder, bool wall)
    {
        if (propMaterial == null || houseMeshes == null || holder == null || holder.childCount == 0) return;
        Transform visual = holder.GetChild(0);
        if (!cliffTops.TryGetValue(visual.name, out List<Vector4> tops)) return;
        Vector3 hp = holder.position;
        var rng = new System.Random(Mathf.RoundToInt(hp.x * 7.31f) * 73856093 ^ Mathf.RoundToInt(hp.z * 3.17f) * 19349663 ^ Mathf.RoundToInt(hp.y) * 83492791);

        candidates.Clear();
        foreach (Vector4 t in tops) candidates.Add(visual.TransformPoint(new Vector3(t.x, t.z, t.y)));
        if (candidates.Count == 0) return;
        placed.Clear();
        placedR.Clear();
        float s = propScale;

        if (rng.NextDouble() < landmarkChance) Place(holder, landmarkMeshes[rng.Next(landmarkMeshes.Length)], s, rng);
        int houses = rng.Next(housesMin, housesMax + 1);
        for (int i = 0; i < houses; i++) Place(holder, houseMeshes[rng.Next(houseMeshes.Length)], s * (0.85f + 0.3f * (float)rng.NextDouble()), rng);
        int trees = rng.Next(treesMin, treesMax + 1);
        for (int i = 0; i < trees; i++) Place(holder, treeMeshes[rng.Next(treeMeshes.Length)], s * (0.8f + 0.45f * (float)rng.NextDouble()), rng);
        int bushes = rng.Next(bushesMin, bushesMax + 1);
        for (int i = 0; i < bushes; i++) Place(holder, bushMeshes[rng.Next(bushMeshes.Length)], s * (0.8f + 0.4f * (float)rng.NextDouble()), rng);
        int plants = rng.Next(plantsMin, plantsMax + 1);
        for (int i = 0; i < plants; i++) Place(holder, plantMeshes[rng.Next(plantMeshes.Length)], s, rng, true);
    }

    void Place(Transform holder, Mesh mesh, float scale, System.Random rng, bool overlap = false)
    {
        if (mesh == null || candidates.Count == 0) return;
        Vector3 ext = mesh.bounds.extents * scale;
        float r = Mathf.Max(ext.x, ext.z) * 0.9f + 0.2f;
        for (int tries = 0; tries < 14; tries++)
        {
            Vector3 p = candidates[rng.Next(candidates.Count)];
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
            Quaternion rot = holder.rotation * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            go.transform.SetPositionAndRotation(p + Vector3.up * -sink, rot);
            go.transform.localScale = Vector3.one * scale;   // the holder is at full size when its rock is placed
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = propMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return;
        }
    }

    Mesh Keep(Mesh m) { madeMeshes.Add(m); return m; }

    // ------------------------------------------------------------------ the cliffs

    // Tiered rock columns, unit footprint, base at y = 0, about 2.2 high: a broad jagged prism, a narrower tier on it
    // (off centre) and sometimes a third, with boulders on the ledges. Every tier top is flat ground for the village.
    // Templates sit under an inactive root (never drawn); the generator stacks them downward into the mist.
    void BuildCliffs()
    {
        if (!ownCliffs || background == null || propMaterial == null) return;
        cliffRoot = new GameObject("PencilCliffTemplates");
        cliffRoot.SetActive(false);
        cliffRoot.transform.SetParent(transform, false);
        cliffTemplates = new GameObject[6];
        var rng = new System.Random(83);
        for (int k = 0; k < cliffTemplates.Length; k++)
        {
            var pm = new PencilMesh();
            var tops = new List<Vector4>();
            int sides = 9 + rng.Next(4);
            var jit = new float[sides];
            for (int i = 0; i < sides; i++) jit[i] = 0.82f + 0.18f * (float)rng.NextDouble();
            float h0 = 1.55f + 0.25f * (float)rng.NextDouble();
            float yaw0 = (float)rng.NextDouble() * 360f;
            // Base column: flares a little toward the top like a mesa.
            pm.Prism(Vector3.zero, sides, 0.42f, 0.48f, 0f, h0, yaw0, PencilMesh.Rock, 0.1f, PencilMesh.Rock, 0f, jit);
            float r0 = 0.48f * 0.82f;
            int tiers = 1 + (rng.NextDouble() < 0.6 ? 1 : 0);
            Vector3 c1 = new Vector3(((float)rng.NextDouble() - 0.5f) * 0.18f, 0f, ((float)rng.NextDouble() - 0.5f) * 0.18f);
            float r1 = 0.2f + 0.08f * (float)rng.NextDouble();
            float h1 = h0 + 0.35f + 0.25f * (float)rng.NextDouble();
            if (tiers > 1)
            {
                var jit1 = new float[7];
                for (int i = 0; i < jit1.Length; i++) jit1[i] = 0.8f + 0.2f * (float)rng.NextDouble();
                pm.Prism(c1, 7, r1, r1 * 0.9f, h0 - 0.05f, h1, (float)rng.NextDouble() * 360f, PencilMesh.Rock, 0.15f, PencilMesh.Rock, 0f, jit1);
            }
            // Boulders on the rim.
            int boulders = 2 + rng.Next(4);
            for (int i = 0; i < boulders; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 bp = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r0 * 0.95f + Vector3.up * h0;
                float br = 0.03f + 0.03f * (float)rng.NextDouble();
                pm.Blob(bp, new Vector3(br * 1.4f, br, br * 1.2f), rng, 0.3f, PencilMesh.Rock, 0.2f);
            }
            // Ledge outcrops down the sides.
            int ledges = 3 + rng.Next(3);
            for (int i = 0; i < ledges; i++)
            {
                float a = (float)rng.NextDouble() * 360f;
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                float y = 0.3f + (float)rng.NextDouble() * (h0 - 0.6f);
                pm.Box(dir * 0.42f + Vector3.up * y, new Vector3(0.06f + 0.05f * (float)rng.NextDouble(), 0.04f, 0.08f), a, PencilMesh.Rock, 0.3f, PencilMesh.Rock, 0f);
            }
            // Flat ground for the village: the base top outside the upper tier, and the tier's top.
            for (int i = 0; i < 70; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rr = Mathf.Sqrt((float)rng.NextDouble()) * r0 * 0.92f;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                if (tiers > 1 && (p - new Vector2(c1.x, c1.z)).magnitude < r1 * 1.15f) continue;
                tops.Add(new Vector4(p.x, p.y, h0, 1f));
            }
            if (tiers > 1)
                for (int i = 0; i < 10; i++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float rr = Mathf.Sqrt((float)rng.NextDouble()) * r1 * 0.6f;
                    tops.Add(new Vector4(c1.x + Mathf.Cos(a) * rr, c1.z + Mathf.Sin(a) * rr, h1, 1f));
                }

            Mesh mesh = Keep(pm.Build("PcCliff" + k));
            cliffTops["PcCliff" + k + "(Clone)"] = tops;
            var go = new GameObject("PcCliff" + k);
            go.transform.SetParent(cliffRoot.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = propMaterial;
            cliffTemplates[k] = go;
        }
    }

    // ------------------------------------------------------------------ prop meshes (built once, shared), world units

    void BuildPropMeshes()
    {
        if (houseMeshes != null) return;
        var rng = new System.Random(29);

        // ---- houses: half-timbered, cottages, narrow town houses, an overhanging upper floor ----
        var houses = new List<Mesh>();
        for (int i = 0; i < 6; i++)
        {
            var pm = new PencilMesh();
            float w = 1.2f + 0.6f * (float)rng.NextDouble();
            float d = 0.9f + 0.4f * (float)rng.NextDouble();
            float h = 1.0f + 0.7f * (float)rng.NextDouble();
            bool jetty = i % 3 == 1;
            bool tall = i % 3 == 2;
            if (tall) { w *= 0.75f; h *= 1.6f; }
            pm.Box(new Vector3(0f, h * 0.5f, 0f), new Vector3(w * 0.5f, h * 0.5f, d * 0.5f), 0f, PencilMesh.Plaster, 0.05f);
            float top = h;
            if (jetty)
            {
                // Upper floor sticking out over the street, on a beam.
                pm.Box(new Vector3(0f, h + 0.02f, 0f), new Vector3(w * 0.56f, 0.04f, d * 0.6f), 0f, PencilMesh.Wood, 0.4f);
                pm.Box(new Vector3(0f, h + 0.42f, 0f), new Vector3(w * 0.56f, 0.38f, d * 0.6f), 0f, PencilMesh.Plaster, 0.1f);
                top = h + 0.8f;
            }
            float hx = (jetty ? w * 0.56f : w * 0.5f) + 0.1f, hz = (jetty ? d * 0.6f : d * 0.5f) + 0.12f;
            pm.Gable(Vector3.zero, hx, hz, top, top + 0.55f + 0.35f * (float)rng.NextDouble(), 0f, PencilMesh.Roof, 0.3f, PencilMesh.Plaster, 0.1f);
            // Timber posts and a beam on the long faces, windows, a door.
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * (d * 0.5f + 0.012f);
                for (int p = -1; p <= 1; p++)
                    pm.Box(new Vector3(p * (w * 0.5f - 0.05f), h * 0.5f, z), new Vector3(0.035f, h * 0.5f, 0.012f), 0f, PencilMesh.Wood, 0.5f);
                pm.Box(new Vector3(0f, h * 0.55f, z), new Vector3(w * 0.5f, 0.03f, 0.012f), 0f, PencilMesh.Wood, 0.5f);
                int wins = tall ? 2 : 1;
                for (int f = 0; f < wins; f++)
                {
                    float wy = h * (tall ? 0.3f + f * 0.4f : 0.75f);
                    pm.Box(new Vector3(-w * 0.22f, wy, z + s * 0.005f), new Vector3(0.11f, 0.13f, 0.01f), 0f, PencilMesh.Window, 0.8f);
                    pm.Box(new Vector3(w * 0.22f, wy, z + s * 0.005f), new Vector3(0.11f, 0.13f, 0.01f), 0f, PencilMesh.Window, 0.8f);
                }
                if (s < 0) pm.Box(new Vector3(0f, 0.25f, z + s * 0.005f), new Vector3(0.12f, 0.25f, 0.01f), 0f, PencilMesh.Wood, 0.7f);
            }
            // Chimney.
            pm.Box(new Vector3(w * 0.25f, top + 0.5f, d * 0.12f), new Vector3(0.08f, 0.3f, 0.08f), 0f, PencilMesh.Stone, 0.25f);
            houses.Add(Keep(pm.Build("PcHouse" + i)));
        }
        houseMeshes = houses.ToArray();

        // ---- landmarks: a clock tower, a church with a spire, a round tower, a well with a little roof ----
        var marks = new List<Mesh>();
        {
            var pm = new PencilMesh();
            pm.Box(new Vector3(0f, 2.2f, 0f), new Vector3(0.6f, 2.2f, 0.6f), 0f, PencilMesh.Stone, 0.1f);
            pm.Box(new Vector3(0f, 4.6f, 0f), new Vector3(0.7f, 0.2f, 0.7f), 0f, PencilMesh.Stone, 0.2f);
            pm.Box(new Vector3(0f, 5.2f, 0f), new Vector3(0.5f, 0.4f, 0.5f), 0f, PencilMesh.Plaster, 0.05f);
            pm.Prism(new Vector3(0f, 0f, 0f), 4, 0.75f, 0f, 5.6f, 6.8f, 45f, PencilMesh.Roof, 0.35f);
            for (int s = 0; s < 4; s++)
            {
                Quaternion q = Quaternion.Euler(0f, 90f * s, 0f);
                Vector3 face = q * new Vector3(0f, 3.7f, -0.62f);
                pm.Box(face, new Vector3(0.32f, 0.32f, 0.02f), 90f * s, PencilMesh.Plaster, 0f);                              // clock face
                pm.Box(face + q * new Vector3(0f, 0.1f, -0.02f), new Vector3(0.02f, 0.12f, 0.01f), 90f * s, PencilMesh.Window, 0.9f);   // hands
                pm.Box(face + q * new Vector3(0.07f, 0f, -0.02f), new Vector3(0.09f, 0.02f, 0.01f), 90f * s, PencilMesh.Window, 0.9f);
                pm.Box(q * new Vector3(0f, 2.2f, -0.61f), new Vector3(0.1f, 0.25f, 0.01f), 90f * s, PencilMesh.Window, 0.85f);
            }
            marks.Add(Keep(pm.Build("PcClockTower")));
        }
        {
            var pm = new PencilMesh();
            pm.Box(new Vector3(0f, 0.9f, 0f), new Vector3(1.4f, 0.9f, 0.75f), 0f, PencilMesh.Stone, 0.1f);
            pm.Gable(Vector3.zero, 1.5f, 0.85f, 1.8f, 2.7f, 0f, PencilMesh.Roof, 0.3f, PencilMesh.Stone, 0.1f);
            pm.Box(new Vector3(-1.1f, 1.8f, 0f), new Vector3(0.45f, 1.8f, 0.45f), 0f, PencilMesh.Stone, 0.1f);
            pm.Prism(new Vector3(-1.1f, 0f, 0f), 8, 0.42f, 0f, 3.6f, 5.6f, 22.5f, PencilMesh.Roof, 0.35f);
            for (int i = -1; i <= 1; i++) pm.Box(new Vector3(i * 0.5f + 0.3f, 1.0f, -0.76f), new Vector3(0.09f, 0.3f, 0.01f), 0f, PencilMesh.Window, 0.85f);
            pm.Box(new Vector3(-1.1f, 0.35f, -0.46f), new Vector3(0.16f, 0.35f, 0.01f), 0f, PencilMesh.Wood, 0.7f);
            marks.Add(Keep(pm.Build("PcChurch")));
        }
        {
            var pm = new PencilMesh();
            pm.Prism(Vector3.zero, 10, 0.6f, 0.55f, 0f, 3.2f, 0f, PencilMesh.Stone, 0.12f);
            pm.Prism(Vector3.zero, 10, 0.7f, 0.7f, 3.2f, 3.5f, 0f, PencilMesh.Stone, 0.2f);
            pm.Prism(Vector3.zero, 10, 0.75f, 0f, 3.5f, 4.7f, 0f, PencilMesh.Roof, 0.35f);
            for (int i = 0; i < 3; i++) pm.Box(Quaternion.Euler(0f, i * 120f, 0f) * new Vector3(0f, 1.2f + i * 0.6f, -0.58f), new Vector3(0.08f, 0.16f, 0.01f), i * 120f, PencilMesh.Window, 0.85f);
            marks.Add(Keep(pm.Build("PcRoundTower")));
        }
        landmarkMeshes = marks.ToArray();

        // ---- trees: round broadleaf, Italian cypress, pine ----
        var trees = new List<Mesh>();
        for (int i = 0; i < 4; i++)
        {
            var pm = new PencilMesh();
            float th = 0.7f + 0.4f * (float)rng.NextDouble();
            pm.Prism(Vector3.zero, 5, 0.08f, 0.06f, 0f, th + 0.3f, 0f, PencilMesh.Wood, 0.5f);
            float cr = 0.55f + 0.25f * (float)rng.NextDouble();
            pm.Blob(new Vector3(0f, th + cr * 0.8f, 0f), new Vector3(cr, cr * 0.9f, cr), rng, 0.22f, PencilMesh.Foliage, 0.15f);
            pm.Blob(new Vector3(cr * 0.45f, th + cr * 1.3f, 0.1f), new Vector3(cr * 0.6f, cr * 0.55f, cr * 0.6f), rng, 0.25f, PencilMesh.Foliage, 0.1f);
            trees.Add(Keep(pm.Build("PcTree" + i)));
        }
        for (int i = 0; i < 3; i++)
        {
            var pm = new PencilMesh();
            float hh = 2.0f + 0.8f * (float)rng.NextDouble();
            pm.Prism(Vector3.zero, 5, 0.06f, 0.05f, 0f, 0.3f, 0f, PencilMesh.Wood, 0.5f);
            pm.Blob(new Vector3(0f, 0.25f + hh * 0.5f, 0f), new Vector3(0.28f, hh * 0.5f, 0.28f), rng, 0.12f, PencilMesh.Foliage, 0.35f);
            trees.Add(Keep(pm.Build("PcCypress" + i)));
        }
        for (int i = 0; i < 2; i++)
        {
            var pm = new PencilMesh();
            pm.Prism(Vector3.zero, 5, 0.07f, 0.05f, 0f, 0.5f, 0f, PencilMesh.Wood, 0.5f);
            for (int l = 0; l < 3; l++)
                pm.Prism(Vector3.zero, 7, 0.6f - l * 0.15f, 0f, 0.35f + l * 0.45f, 1.15f + l * 0.45f, l * 20f, PencilMesh.Foliage, 0.3f);
            trees.Add(Keep(pm.Build("PcPine" + i)));
        }
        treeMeshes = trees.ToArray();

        // ---- bushes ----
        var bushes = new List<Mesh>();
        for (int i = 0; i < 3; i++)
        {
            var pm = new PencilMesh();
            pm.Blob(new Vector3(0f, 0.22f, 0f), new Vector3(0.45f, 0.3f, 0.35f), rng, 0.3f, PencilMesh.Foliage, 0.2f);
            if (i > 0) pm.Blob(new Vector3(0.3f, 0.18f, 0.1f), new Vector3(0.28f, 0.22f, 0.25f), rng, 0.3f, PencilMesh.Foliage, 0.25f);
            bushes.Add(Keep(pm.Build("PcBush" + i)));
        }
        bushMeshes = bushes.ToArray();

        // ---- plants: grass tufts (blades), flowers on stalks, a fence piece, a barrel ----
        var plants = new List<Mesh>();
        for (int i = 0; i < 3; i++)
        {
            var pm = new PencilMesh();
            for (int b = 0; b < 7; b++)
            {
                float a = (float)rng.NextDouble() * 360f;
                Vector3 bp = Quaternion.Euler(0f, a, 0f) * Vector3.forward * (0.05f + 0.12f * (float)rng.NextDouble());
                Vector3 lean = Quaternion.Euler(0f, a, 0f) * Vector3.forward * 0.08f;
                float bh = 0.18f + 0.18f * (float)rng.NextDouble();
                Vector3 side = Quaternion.Euler(0f, a + 90f, 0f) * Vector3.forward * 0.02f;
                pm.Tri(bp - side, bp + lean + Vector3.up * bh, bp + side, PencilMesh.Foliage, 0.3f);
            }
            plants.Add(Keep(pm.Build("PcTuft" + i)));
        }
        for (int i = 0; i < 2; i++)
        {
            var pm = new PencilMesh();
            for (int f = 0; f < 3; f++)
            {
                Vector3 fp = new Vector3((f - 1) * 0.1f, 0f, ((float)rng.NextDouble() - 0.5f) * 0.1f);
                float fh = 0.25f + 0.12f * (float)rng.NextDouble();
                pm.Box(fp + Vector3.up * fh * 0.5f, new Vector3(0.008f, fh * 0.5f, 0.008f), 0f, PencilMesh.Wood, 0.4f);
                pm.Prism(fp + Vector3.up * fh, 5, 0.06f, 0.06f, 0f, 0.02f, f * 30f, PencilMesh.Plaster, 0f);
                pm.Blob(fp + Vector3.up * (fh + 0.025f), new Vector3(0.02f, 0.02f, 0.02f), rng, 0f, PencilMesh.Window, 0.8f);
            }
            plants.Add(Keep(pm.Build("PcFlowers" + i)));
        }
        {
            var pm = new PencilMesh();
            for (int p = 0; p < 4; p++) pm.Box(new Vector3(p * 0.3f - 0.45f, 0.2f, 0f), new Vector3(0.025f, 0.2f, 0.025f), 0f, PencilMesh.Wood, 0.45f);
            pm.Box(new Vector3(0f, 0.28f, 0f), new Vector3(0.5f, 0.02f, 0.015f), 0f, PencilMesh.Wood, 0.45f);
            pm.Box(new Vector3(0f, 0.14f, 0f), new Vector3(0.5f, 0.02f, 0.015f), 0f, PencilMesh.Wood, 0.45f);
            plants.Add(Keep(pm.Build("PcFence")));
        }
        {
            var pm = new PencilMesh();
            pm.Prism(Vector3.zero, 8, 0.14f, 0.16f, 0f, 0.2f, 0f, PencilMesh.Wood, 0.3f);
            pm.Prism(Vector3.zero, 8, 0.16f, 0.14f, 0.2f, 0.4f, 0f, PencilMesh.Wood, 0.3f, PencilMesh.Wood, 0.5f);
            plants.Add(Keep(pm.Build("PcBarrel")));
        }
        plantMeshes = plants.ToArray();
    }
}
