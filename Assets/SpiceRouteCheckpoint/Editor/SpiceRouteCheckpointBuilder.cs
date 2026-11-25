#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Builds the Spice Route checkpoint (re-runnable): Look_SpiceRoute (a painted-paper look from the user's references:
/// a Rajasthani mixed-media collage and a Mughal courtyard watercolour as the art form; a Ganesha cave temple, a
/// Vishvarupa miniature, hill temples and Nagara shikharas for the Indian half; Cairo minarets at dusk, Moebius sandstone
/// towers and an Arabian-nights palace for the second half), its materials, post-processing and SpiceCP_Renderer (the
/// paper pass), the runner / cape materials, the 3.10 checkpoint set (spice at checkpoint 10 = portal 9, after Rogue
/// Planet, replacing Jade there) and both scenes:
///   Tools > Spice Route Checkpoint > Build Assets         (no scene is touched)
///   Tools > Spice Route Checkpoint > Wire 3.10            (opens MainScene prop camera 3.10, wires, saves it)
///   Tools > Spice Route Checkpoint > Make Spice Route Scene (copies 3.10 to MainScene prop camera SpiceRoute, wires it, saves it)
/// </summary>
public static class SpiceRouteCheckpointBuilder
{
    public const string Root = "Assets/SpiceRouteCheckpoint";
    public const string Scene310 = FlowerCheckpointBuilder.Scene310;
    public const string SceneSpice = "Assets/MainScene prop camera SpiceRoute.unity";
    public const string LookPath = "Assets/PropCameraLooks/LookTransition/Looks/Look_SpiceRoute.asset";
    public const string LookName = "Spice Route Checkpoint";
    public const string ProfilePath = Root + "/Profiles/CP10_SpiceRoute_3_10.asset";
    public const string SetSpice = Root + "/CheckpointSet_SpiceRoute.asset";
    public const string RendererPath = Root + "/Settings/SpiceCP_Renderer.asset";
    public const int Portal = 9;
    const string PaperFeature = "SpicePaper";

    static Color H(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color c); return c; }

    /// <summary>Measured from the references (sRGB).</summary>
    public static class Palette
    {
        public static Color Ivory = H("F2E2C2"), Saffron = H("E8902E"), Maroon = H("8A2A3A"), Plum = H("5A1E3A");
        public static Color Rani = H("E0457B"), Gold = H("E0A83A"), Night = H("142A5E"), Peach = H("F0A07A"), Rose = H("8A3A5A");
        public static Color Apricot = H("F0B88A"), Turquoise = H("27A3B5"), Cobalt = H("1C3F9E");
    }

    [MenuItem("Tools/Spice Route Checkpoint/Build Assets")]
    public static void MenuBuild() => BuildAssets();

    [MenuItem("Tools/Spice Route Checkpoint/Wire 3.10")]
    public static void MenuWire310() => Wire310();

    [MenuItem("Tools/Spice Route Checkpoint/Make Spice Route Scene")]
    public static void MenuMakeScene() => MakeSpiceScene();

    static void Folder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }

    static Material MaterialAt(string path, Shader shader, Material from)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = from != null ? new Material(from) : new Material(shader);
            m.shader = shader;
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader != shader) m.shader = shader;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ assets

    public static AtmosphereLook BuildAssets()
    {
        Folder("Assets", "SpiceRouteCheckpoint");
        Folder(Root, "Materials");
        Folder(Root, "Settings");
        Folder(Root, "Profiles");

        string S = SpaceCheckpointBuilder.Root + "/Materials/";
        Shader ff = Shader.Find("PropLooks/FlatFaceBlock");
        Material block = MaterialAt(Root + "/Materials/M_Spice_Leg.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Space_Leg.mat"));
        Material fade = MaterialAt(Root + "/Materials/M_Spice_LegFade.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Space_LegFade.mat"));
        Material player = MaterialAt(Root + "/Materials/M_Spice_Player.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Space_Player.mat"));
        Material obstacle = MaterialAt(Root + "/Materials/M_Spice_Obstacle.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Space_Obstacle.mat"));
        Material srcSky = AssetDatabase.LoadAssetAtPath<Material>(S + "M_Space_Sky.mat");
        Material sky = MaterialAt(Root + "/Materials/M_Spice_Sky.mat", srcSky != null ? srcSky.shader : Shader.Find("PropLooks/RadialSky"), srcSky);

        foreach (Material m in new[] { block, fade, player })
        {
            m.SetColor("_TopColor", Palette.Ivory);
            m.SetColor("_LeftColor", Palette.Maroon);
            m.SetColor("_RightColor", Palette.Saffron);
            m.SetColor("_BottomColor", Palette.Plum);
            m.SetColor("_ShadowColor", Palette.Maroon);
            m.SetFloat("_ShadowStrength", 0.3f);
            m.SetColor("_FogTopColor", Palette.Apricot);
            m.SetColor("_FogBottomColor", Palette.Rose);
            m.SetFloat("_FogStart", 16f);
            m.SetFloat("_FogEnd", 70f);
            m.SetFloat("_FogMax", 0.3f);
            m.SetFloat("_ChasmFogMix", 0.3f);
            m.SetColor("_GlowTint", Palette.Gold);
            m.SetFloat("_GlowGain", 0f);
        }
        obstacle.CopyPropertiesFromMaterial(block);
        obstacle.SetColor("_TopColor", H("E8283C")); obstacle.SetColor("_LeftColor", H("98142E"));
        obstacle.SetColor("_RightColor", H("C81E36")); obstacle.SetColor("_BottomColor", H("6A0E20"));

        sky.SetColor("_TopColor", Palette.Night);
        sky.SetColor("_BottomColor", Palette.Rose);
        sky.SetColor("_MidColor", Palette.Peach);
        sky.SetVector("_GlowCenter", new Vector4(0.5f, 0.3f, 0f, 0f));
        sky.SetFloat("_GlowRadius", 0.7f);
        sky.SetFloat("_GlowStrength", 0.25f);
        sky.SetColor("_GlowColor", H("FFD49A"));
        sky.SetColor("_VignetteColor", Palette.Plum);
        sky.SetFloat("_Vignette", 0.2f);

        Material backdrop = MaterialAt(Root + "/Materials/M_Spice_Backdrop.mat", Shader.Find("SpiceRouteCheckpoint/Backdrop"), null);
        backdrop.renderQueue = 2502;
        MaterialAt(Root + "/Materials/M_Spice_Paper.mat", Shader.Find("Hidden/SpiceRouteCheckpoint/Paper"), null);
        MaterialAt(Root + "/Materials/M_Spice_Prop.mat", Shader.Find("SpiceRouteCheckpoint/Prop"), null);
        Material totem = AssetDatabase.LoadAssetAtPath<Material>("Assets/DepthCues37/Materials/M_PlayerTotem_37.mat");
        Material body = MaterialAt(Root + "/Materials/M_Spice_RunnerBody.mat", Shader.Find("SpiceRouteCheckpoint/Runner"), totem);
        body.SetColor("_BaseColor", H("F4B82C"));
        body.SetColor("_AccentColor", H("D7261E"));
        Material capeSrc = AssetDatabase.LoadAssetAtPath<Material>("Assets/DepthCues37/Materials/M_RunnerCape_37.mat");
        MaterialAt(Root + "/Materials/M_Spice_RunnerCape.mat", Shader.Find("SpiceRouteCheckpoint/Cape"), capeSrc);

        VolumeProfile vol = BuildPost();

        var look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        if (look == null) { look = ScriptableObject.CreateInstance<AtmosphereLook>(); AssetDatabase.CreateAsset(look, LookPath); }
        look.sourceScene = LookName;
        look.Capture(new AtmosphereLook.Sources { block = block, blockFade = fade, player = player, obstacle = obstacle, sky = sky });
        look.hasChasm = true;
        look.chasmTop = Palette.Apricot;
        look.chasmBottom = H("7A3A62");
        look.chasmGlow = H("FFD49A");
        look.chasmGlowStrength = 0.35f;
        look.chasmGlowCentre = new Vector2(0.5f, 0.35f);
        look.chasmGlowRadius = 0.7f;
        look.sunColor = H("FFE2B4");
        look.sunIntensity = 1.05f;
        look.sunShadowStrength = 0.5f;
        look.sunEuler = new Vector3(40f, 30f, 0f);
        look.ambientMode = (int)AmbientMode.Trilight;
        look.ambientSky = H("8A90C8");
        look.ambientEquator = H("E0A890");
        look.ambientGround = H("7A4A5A");
        look.cameraBackground = H("3A2040");
        look.volumeProfile = vol;
        look.aheadKeys = new[] { Color.white, H("FFF6E8"), H("FFF0DC"), H("FCE8D0") };
        look.travelledColor = H("E8C8A8");
        look.doorGlowColor = Palette.Gold;
        look.doorStoneColor = H("8A3A3A");
        look.sceneAccent = Palette.Gold;
        look.wispColorA = Palette.Gold;
        look.wispColorB = Palette.Rani;
        look.trailSwitchColor = Palette.Saffron;
        look.faceColor = Palette.Ivory;
        look.faceSwitchColor = Palette.Gold;
        look.arrowColor = Palette.Ivory;
        look.plankColor = H("C8784A");
        look.obstacleGlowColor = Palette.Rani;
        look.obstacleGlowIntensity = 0f;
        look.obstacleGlowRange = 4f;
        look.skyTopColor = Palette.Night;
        EditorUtility.SetDirty(look);

        EnsureRenderer();
        BuildProfiles(look);
        RebuildSet310();
        var palette = AssetDatabase.LoadAssetAtPath<LegStagePalette>("Assets/LegStages310/LegStagePalette_3_10.asset");
        if (palette != null) { palette.FillDefaults(); EditorUtility.SetDirty(palette); }
        AssetDatabase.SaveAssets();
        Debug.Log("[SpiceRoute] assets built: " + LookPath);
        return look;
    }

    static VolumeProfile BuildPost()
    {
        string path = Root + "/Settings/Spice_Volume.asset";
        var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (p == null) { p = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(p, path); }
        T Get<T>() where T : VolumeComponent
        {
            if (!p.TryGet(out T c))
            {
                c = p.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, p);
            }
            return c;
        }
        var bloom = Get<Bloom>();
        bloom.threshold.Override(0.95f);
        bloom.intensity.Override(0.18f);
        bloom.scatter.Override(0.55f);
        bloom.tint.Override(H("FFE8C8"));
        var ca = Get<ColorAdjustments>();
        ca.postExposure.Override(0.05f);
        ca.contrast.Override(10f);
        ca.saturation.Override(8f);
        ca.colorFilter.Override(Color.white);
        var vig = Get<Vignette>();
        vig.color.Override(H("3A1020"));
        vig.intensity.Override(0.18f);
        vig.smoothness.Override(0.5f);
        var grain = Get<FilmGrain>();
        grain.intensity.Override(0f);
        grain.active = false;
        EditorUtility.SetDirty(p);
        return p;
    }

    static FullScreenPassRendererFeature Feature(UniversalRendererData rd, string name)
    {
        foreach (var f in rd.rendererFeatures) if (f is FullScreenPassRendererFeature fs && fs.name == name) return fs;
        var pass = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
        pass.name = name;
        AssetDatabase.AddObjectToAsset(pass, rd);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(pass, out string _, out long localId);
        var so = new SerializedObject(rd);
        var feats = so.FindProperty("m_RendererFeatures");
        feats.arraySize++;
        feats.GetArrayElementAtIndex(feats.arraySize - 1).objectReferenceValue = pass;
        var map = so.FindProperty("m_RendererFeatureMap");
        map.arraySize++;
        map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
        so.ApplyModifiedPropertiesWithoutUndo();
        return pass;
    }

    /// <summary>SpiceCP_Renderer = SpaceCP_Renderer (SSAO kept, every other checkpoint's full-screen pass off - the space oil
    /// pass is switched back on by the controller while Rogue Planet is still on screen) + the paper pass after post.
    /// Registered on every pipeline asset; returns its index.</summary>
    public static int EnsureRenderer()
    {
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rd == null)
        {
            AssetDatabase.CopyAsset(SpaceCheckpointBuilder.RendererPath, RendererPath);
            AssetDatabase.ImportAsset(RendererPath);
            rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        }
        foreach (var f in rd.rendererFeatures)
        {
            if (f == null || f.name == PaperFeature || !(f is FullScreenPassRendererFeature)) continue;
            f.SetActive(false);
            EditorUtility.SetDirty(f);
        }
        var paper = Feature(rd, PaperFeature);
        paper.passMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Spice_Paper.mat");
        paper.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
        paper.requirements = ScriptableRenderPassInput.None;
        paper.fetchColorBuffer = true;
        paper.passIndex = 0;
        paper.SetActive(true);
        EditorUtility.SetDirty(paper);
        EditorUtility.SetDirty(rd);

        int index = -1;
        foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
            var so = new SerializedObject(asset);
            var list = so.FindProperty("m_RendererDataList");
            int at = -1;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == rd) at = i;
            if (at < 0)
            {
                list.arraySize++;
                at = list.arraySize - 1;
                list.GetArrayElementAtIndex(at).objectReferenceValue = rd;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
            if (index < 0) index = at;
            else if (index != at) Debug.LogWarning("[SpiceRoute] renderer at index " + at + " on " + asset.name + " but " + index + " elsewhere.");
        }
        AssetDatabase.SaveAssets();
        return index;
    }

    static CheckpointProfile CopyProfile(CheckpointProfile src, string path, int index, int L)
    {
        var p = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(path);
        if (p == null)
        {
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(src), path);
            p = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(path);
        }
        p.index = index;
        p.legIndex = (index - 1) * L;
        EditorUtility.SetDirty(p);
        return p;
    }

    static void BuildProfiles(AtmosphereLook look)
    {
        var set39 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(PixelCheckpointBuilder.Set39);
        int L = set39.legsBetweenPortals;
        var spProfile = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(SpaceCheckpointBuilder.ProfilePath);
        var sr = CopyProfile(spProfile != null ? spProfile : set39.Get(5), ProfilePath, 10, L);
        sr.displayName = "Spice Route";
        sr.look = look;
        sr.label = "";
        sr.keepObstacle = false;
        sr.takePlankColor = true;
        sr.lockedTeaseColor = Palette.Gold;
        sr.menuCamera = new MenuCameraPreset { motion = MenuShotMotion.LowSideTrack, positionOffset = new Vector3(0f, 3.5f, -12.5f), angles = new Vector2(12f, 40f), fov = 46f, motionSpeed = 0.03f, motionAmplitude = 5f };
        string motes = "Assets/Checkpoints/Particles/Motes_CP10_SpiceRoute.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(motes) == null)
        {
            var go = new GameObject("Motes_CP10_SpiceRoute");
            MenuParticles.Configure(go.AddComponent<ParticleSystem>(), MenuParticles.ColorFor(look), AssetDatabase.LoadAssetAtPath<Material>("Assets/Checkpoints/Menu/M_MenuMote.mat"));
            PrefabUtility.SaveAsPrefabAsset(go, motes);
            Object.DestroyImmediate(go);
        }
        sr.ambientParticlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(motes);
        EditorUtility.SetDirty(sr);

        // The Spice Route scene: spice from the start, every later portal keeps it (the pixel scene's look-less placeholders).
        var start = CopyProfile(set39.Get(1), Root + "/Profiles/CP01_SpiceStart.asset", 1, L);
        start.displayName = "Spice Route";
        start.look = look;
        EditorUtility.SetDirty(start);
        var set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetSpice);
        if (set == null) { set = Object.Instantiate(set39); AssetDatabase.CreateAsset(set, SetSpice); }
        set.name = "CheckpointSet_SpiceRoute";
        set.legsBetweenPortals = L;
        set.defaultTiming = set39.defaultTiming;
        set.checkpoints = new List<CheckpointProfile> { start };
        for (int i = 2; i <= 10; i++)
        {
            var ph = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(PixelCheckpointBuilder.Root + "/Profiles/CP" + i.ToString("00") + "_PixelPlaceholder.asset");
            if (ph != null) set.checkpoints.Add(ph);
        }
        EditorUtility.SetDirty(set);
    }

    /// <summary>3.10's order: Start, Crimson, Toon, Pixel, Storybook, Flower 6, Voxel 7, Pencil 8, Space 9, SPICE ROUTE 10.
    /// (Jade's 3.10 profile stays on disk, no longer in the set.) SpaceCheckpointBuilder.RebuildSet310 takes the spice
    /// profile at 10 whenever it exists, so either builder gives the same set.</summary>
    public static void RebuildSet310()
    {
        SpaceCheckpointBuilder.RebuildSet310();
    }

    // ------------------------------------------------------------------ scenes

    public static DepthCueController.CueLook SpiceCue() => new DepthCueController.CueLook
    {
        name = "Spice Route (India / Arabia)",
        ladder = 0f, litShare = 0.6f, shadeShare = 0.35f, bottomShare = 0.15f, runnerLadder = 1f, litRight = false,
        lip = 0f, lipWidth = 0.045f,
        contactColor = H("4A1428"), contact = 0.35f, contactSize = 0.9f, runnerRim = 0f,
        railColor = H("4A1428"), rail = 0.25f, railWidth = 0.04f,
        trailTint = Color.white, trailTintAmount = 0f, trailIntensity = 1f,
        decor = 0.4f, bloomCut = 0.6f,
    };

    static void WireOpenScene(AtmosphereLook look, CheckpointSet set, bool spiceScene)
    {
        var dir = Object.FindFirstObjectByType<LookTransitionDirector>();
        dir.checkpoints = set;
        dir.baseLookOverride = spiceScene ? look : null;
        dir.blockWipeShader = Shader.Find("PixelCheckpoint/FlatFaceBlockPixel");
        EditorUtility.SetDirty(dir);
        var crc = Object.FindFirstObjectByType<CheckpointRunController>(FindObjectsInactive.Include);
        if (crc != null) { crc.checkpoints = set; EditorUtility.SetDirty(crc); }

        var cues = Object.FindFirstObjectByType<DepthCueController>(FindObjectsInactive.Include);
        if (cues != null)
        {
            var list = new List<DepthCueController.CueLook>(cues.looks);
            if (spiceScene)
            {
                list.Clear();
                for (int i = 0; i < 10; i++) list.Add(SpiceCue());
            }
            else
            {
                int at = list.FindIndex(c => c != null && c.name != null && c.name.StartsWith("Spice"));
                if (at < 0 && list.Count > Portal) at = Portal;
                if (at >= 0) list[at] = SpiceCue();
                else
                {
                    while (list.Count < Portal) list.Add(list[list.Count - 1]);
                    list.Add(SpiceCue());
                }
            }
            cues.looks = list.ToArray();
            EditorUtility.SetDirty(cues);
        }

        if (spiceScene)
        {
            var stages = Object.FindFirstObjectByType<LegStageColors>(FindObjectsInactive.Include);
            if (stages != null) { stages.enabled = false; EditorUtility.SetDirty(stages); }
        }

        var old = Object.FindFirstObjectByType<SpiceRouteCheckpointController>(FindObjectsInactive.Include);
        GameObject host = old != null ? old.gameObject : new GameObject("SpiceRouteCheckpoint");
        if (old != null) Object.DestroyImmediate(old);
        var ctl = host.AddComponent<SpiceRouteCheckpointController>();
        ctl.spiceLook = look;
        ctl.alwaysSpice = spiceScene;
        ctl.spicePortal = Portal;
        string M = Root + "/Materials/";
        ctl.backdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(M + "M_Spice_Backdrop.mat");
        ctl.propMaterial = AssetDatabase.LoadAssetAtPath<Material>(M + "M_Spice_Prop.mat");
        ctl.runnerBody = AssetDatabase.LoadAssetAtPath<Material>(M + "M_Spice_RunnerBody.mat");
        ctl.runnerCape = AssetDatabase.LoadAssetAtPath<Material>(M + "M_Spice_RunnerCape.mat");
        ctl.spiceRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        ctl.spiceRendererIndex = EnsureRenderer();
        Camera cam = Camera.main;
        var cd = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
        if (cd != null) ctl.baseRendererIndex = new SerializedObject(cd).FindProperty("m_RendererIndex").intValue;
        EditorUtility.SetDirty(ctl);
    }

    public static void Wire310()
    {
        if (AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath) == null) { Debug.LogError("[SpiceRoute] run Build Assets first."); return; }
        var scene = EditorSceneManager.OpenScene(Scene310, OpenSceneMode.Single);
        WireOpenScene(AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath), AssetDatabase.LoadAssetAtPath<CheckpointSet>(FlowerCheckpointBuilder.Set310), false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[SpiceRoute] 3.10 wired: spice route look at checkpoint 10 (portal 9).");
    }

    public static void MakeSpiceScene()
    {
        if (AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath) == null) { Debug.LogError("[SpiceRoute] run Build Assets first."); return; }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneSpice) == null) AssetDatabase.CopyAsset(Scene310, SceneSpice);
        var scene = EditorSceneManager.OpenScene(SceneSpice, OpenSceneMode.Single);
        WireOpenScene(AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath), AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetSpice), true);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[SpiceRoute] " + SceneSpice + " made: spice route look for the whole run.");
    }
}
#endif
