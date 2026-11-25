#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Builds the space checkpoint "Rogue Planet" (re-runnable): Look_Space (an oil-painted deep-space look from the user's
/// references: a thick-impasto purple/blue spaceship over a planet, a Saturn oil painting over an orange sunset, shuttles
/// lifting off in teal / violet / amber smoke, a pale red planet with mesas), its materials, post-processing and
/// SpaceCP_Renderer (the oil paint pass), the 3.10 checkpoint set (space at checkpoint 9 = portal 8, replacing Raspberry
/// there; Jade stays 10) and both scenes:
///   Tools > Space Checkpoint > Build Assets         (no scene is touched)
///   Tools > Space Checkpoint > Wire 3.10            (opens MainScene prop camera 3.10, wires, saves it)
///   Tools > Space Checkpoint > Make Space Scene     (copies 3.10 to MainScene prop camera Space, wires it, saves it)
/// </summary>
public static class SpaceCheckpointBuilder
{
    public const string Root = "Assets/SpaceCheckpoint";
    public const string Scene310 = FlowerCheckpointBuilder.Scene310;
    public const string SceneSpace = "Assets/MainScene prop camera Space.unity";
    public const string LookPath = "Assets/PropCameraLooks/LookTransition/Looks/Look_Space.asset";
    public const string LookName = "Space Checkpoint";
    public const string ProfilePath = Root + "/Profiles/CP09_Space_3_10.asset";
    public const string SetSpace = Root + "/CheckpointSet_Space.asset";
    public const string RendererPath = Root + "/Settings/SpaceCP_Renderer.asset";
    public const int Portal = 8;
    const string OilFeature = "SpaceOil";

    static Color H(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color c); return c; }

    /// <summary>Measured from the references (sRGB).</summary>
    public static class Palette
    {
        public static Color Top = H("9AA8F2"), TopDark = H("5662C4");
        public static Color Side = H("3A5CE0"), SideDark = H("1C2284");
        public static Color Bottom = H("E8623C"), BottomDark = H("8A2A58");
        public static Color Accent = H("EC7440"), Glow = H("5FE8FF"), Cream = H("EFE6CD"), Magenta = H("D040A8");
        public static Color Indigo = H("0E1648"), Violet = H("7876E9"), Plum = H("7A2C62"), Rust = H("D8583C");
    }

    [MenuItem("Tools/Space Checkpoint/Build Assets")]
    public static void MenuBuild() => BuildAssets();

    [MenuItem("Tools/Space Checkpoint/Wire 3.10")]
    public static void MenuWire310() => Wire310();

    [MenuItem("Tools/Space Checkpoint/Make Space Scene")]
    public static void MenuMakeScene() => MakeSpaceScene();

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
        Folder("Assets", "SpaceCheckpoint");
        Folder(Root, "Materials");
        Folder(Root, "Settings");
        Folder(Root, "Profiles");

        string S = PencilCheckpointBuilder.Root + "/Materials/";
        Shader ff = Shader.Find("PropLooks/FlatFaceBlock");
        Material block = MaterialAt(Root + "/Materials/M_Space_Leg.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Pencil_Leg.mat"));
        Material fade = MaterialAt(Root + "/Materials/M_Space_LegFade.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Pencil_LegFade.mat"));
        Material player = MaterialAt(Root + "/Materials/M_Space_Player.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Pencil_Player.mat"));
        Material obstacle = MaterialAt(Root + "/Materials/M_Space_Obstacle.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Pencil_Obstacle.mat"));
        Material srcSky = AssetDatabase.LoadAssetAtPath<Material>(S + "M_Pencil_Sky.mat");
        Material sky = MaterialAt(Root + "/Materials/M_Space_Sky.mat", srcSky != null ? srcSky.shader : Shader.Find("PropLooks/RadialSky"), srcSky);

        foreach (Material m in new[] { block, fade, player })
        {
            m.SetColor("_TopColor", Palette.Top);
            m.SetColor("_LeftColor", Palette.SideDark);
            m.SetColor("_RightColor", Palette.Side);
            m.SetColor("_BottomColor", Palette.Bottom);
            m.SetColor("_ShadowColor", Palette.SideDark);
            m.SetFloat("_ShadowStrength", 0.3f);
            m.SetColor("_FogTopColor", H("B04A70"));
            m.SetColor("_FogBottomColor", Palette.Rust);
            m.SetFloat("_FogStart", 16f);
            m.SetFloat("_FogEnd", 70f);
            m.SetFloat("_FogMax", 0.35f);
            m.SetFloat("_ChasmFogMix", 0.3f);
            m.SetColor("_GlowTint", Palette.Accent);
            m.SetFloat("_GlowGain", 0f);
        }
        obstacle.CopyPropertiesFromMaterial(block);
        obstacle.SetColor("_TopColor", H("F0405A")); obstacle.SetColor("_LeftColor", H("A01A44"));
        obstacle.SetColor("_RightColor", H("D02A52")); obstacle.SetColor("_BottomColor", H("700F30"));

        sky.SetColor("_TopColor", Palette.Indigo);
        sky.SetColor("_BottomColor", Palette.Plum);
        sky.SetColor("_MidColor", Palette.Violet);
        sky.SetVector("_GlowCenter", new Vector4(0.5f, 0.25f, 0f, 0f));
        sky.SetFloat("_GlowRadius", 0.7f);
        sky.SetFloat("_GlowStrength", 0.25f);
        sky.SetColor("_GlowColor", H("F29A4C"));
        sky.SetColor("_VignetteColor", Palette.Indigo);
        sky.SetFloat("_Vignette", 0.2f);

        Material backdrop = MaterialAt(Root + "/Materials/M_Space_Backdrop.mat", Shader.Find("SpaceCheckpoint/Backdrop"), null);
        backdrop.renderQueue = 2502;
        MaterialAt(Root + "/Materials/M_Space_Oil.mat", Shader.Find("Hidden/SpaceCheckpoint/Oil"), null);

        VolumeProfile vol = BuildPost();

        var look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        if (look == null) { look = ScriptableObject.CreateInstance<AtmosphereLook>(); AssetDatabase.CreateAsset(look, LookPath); }
        look.sourceScene = LookName;
        look.Capture(new AtmosphereLook.Sources { block = block, blockFade = fade, player = player, obstacle = obstacle, sky = sky });
        look.hasChasm = true;
        look.chasmTop = Palette.Plum;
        look.chasmBottom = Palette.Rust;
        look.chasmGlow = H("FFB060");
        look.chasmGlowStrength = 0.35f;
        look.chasmGlowCentre = new Vector2(0.5f, 0.3f);
        look.chasmGlowRadius = 0.7f;
        look.sunColor = H("FFD7A0");
        look.sunIntensity = 1.0f;
        look.sunShadowStrength = 0.5f;
        look.sunEuler = new Vector3(42f, 28f, 0f);
        look.ambientMode = (int)AmbientMode.Trilight;
        look.ambientSky = H("6A74D8");
        look.ambientEquator = H("A07AB0");
        look.ambientGround = H("6A2A4A");
        look.cameraBackground = H("2A1C60");
        look.volumeProfile = vol;
        look.aheadKeys = new[] { Color.white, H("F8F4FF"), H("F0ECFF"), H("E8E4FF") };
        look.travelledColor = H("B8B0E8");
        look.doorGlowColor = Palette.Accent;
        look.doorStoneColor = H("2A2A80");
        look.sceneAccent = Palette.Accent;
        look.wispColorA = Palette.Glow;
        look.wispColorB = Palette.Magenta;
        look.trailSwitchColor = Palette.Glow;
        look.faceColor = Palette.Cream;
        look.faceSwitchColor = Palette.Accent;
        look.arrowColor = Palette.Cream;
        look.plankColor = H("B8B0E8");
        look.obstacleGlowColor = Palette.Magenta;
        look.obstacleGlowIntensity = 0f;
        look.obstacleGlowRange = 4f;
        look.skyTopColor = Palette.Indigo;
        EditorUtility.SetDirty(look);

        EnsureRenderer();
        BuildProfiles(look);
        RebuildSet310();
        var palette = AssetDatabase.LoadAssetAtPath<LegStagePalette>("Assets/LegStages310/LegStagePalette_3_10.asset");
        if (palette != null) { palette.FillDefaults(); EditorUtility.SetDirty(palette); }
        AssetDatabase.SaveAssets();
        Debug.Log("[SpaceCheckpoint] assets built: " + LookPath);
        return look;
    }

    static VolumeProfile BuildPost()
    {
        string path = Root + "/Settings/Space_Volume.asset";
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
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(0.2f);
        bloom.scatter.Override(0.55f);
        bloom.tint.Override(Color.white);
        var ca = Get<ColorAdjustments>();
        ca.postExposure.Override(0.05f);
        ca.contrast.Override(14f);
        ca.saturation.Override(10f);
        ca.colorFilter.Override(Color.white);
        var vig = Get<Vignette>();
        vig.color.Override(H("0A0C2C"));
        vig.intensity.Override(0.2f);
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

    /// <summary>SpaceCP_Renderer = PencilCP_Renderer (SSAO kept, its other checkpoints' passes off, the sketchbook lines /
    /// paper off until the controller needs them while the sketchbook is still on screen) + the oil pass after post.
    /// Registered on every pipeline asset; returns its index.</summary>
    public static int EnsureRenderer()
    {
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rd == null)
        {
            AssetDatabase.CopyAsset(PencilCheckpointBuilder.RendererPath, RendererPath);
            AssetDatabase.ImportAsset(RendererPath);
            rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        }
        foreach (var f in rd.rendererFeatures)
        {
            // Every full-screen pass of the other checkpoints is off (SSAO stays); the controller switches the sketchbook
            // pair back on while that look is still on screen during the wipe.
            if (f == null || f.name == OilFeature || !(f is FullScreenPassRendererFeature)) continue;
            f.SetActive(false);
            EditorUtility.SetDirty(f);
        }
        var oil = Feature(rd, OilFeature);
        oil.passMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Space_Oil.mat");
        oil.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
        oil.requirements = ScriptableRenderPassInput.None;
        oil.fetchColorBuffer = true;
        oil.passIndex = 0;
        oil.SetActive(true);
        EditorUtility.SetDirty(oil);
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
            else if (index != at) Debug.LogWarning("[SpaceCheckpoint] renderer at index " + at + " on " + asset.name + " but " + index + " elsewhere.");
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
        var pcProfile = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(PencilCheckpointBuilder.ProfilePath);
        var sp = CopyProfile(pcProfile != null ? pcProfile : set39.Get(5), ProfilePath, 9, L);
        sp.displayName = "Rogue Planet";
        sp.look = look;
        sp.label = "";
        sp.keepObstacle = false;
        sp.takePlankColor = true;
        sp.lockedTeaseColor = Palette.Violet;
        sp.menuCamera = new MenuCameraPreset { motion = MenuShotMotion.HighOrbit, positionOffset = new Vector3(0f, 13f, -15f), angles = new Vector2(32f, 150f), fov = 42f, motionSpeed = 0.03f, motionAmplitude = 35f };
        // Its own motes in the MainUi band (the copied profile would carry another checkpoint's).
        string motes = "Assets/Checkpoints/Particles/Motes_CP09_Space.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(motes) == null)
        {
            var go = new GameObject("Motes_CP09_Space");
            MenuParticles.Configure(go.AddComponent<ParticleSystem>(), MenuParticles.ColorFor(look), AssetDatabase.LoadAssetAtPath<Material>("Assets/Checkpoints/Menu/M_MenuMote.mat"));
            PrefabUtility.SaveAsPrefabAsset(go, motes);
            Object.DestroyImmediate(go);
        }
        sp.ambientParticlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(motes);
        EditorUtility.SetDirty(sp);

        // Space scene: space from the start, every later portal keeps it (the pixel scene's look-less placeholders).
        var start = CopyProfile(set39.Get(1), Root + "/Profiles/CP01_SpaceStart.asset", 1, L);
        start.displayName = "Rogue Planet";
        start.look = look;
        EditorUtility.SetDirty(start);
        var set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetSpace);
        if (set == null) { set = Object.Instantiate(set39); AssetDatabase.CreateAsset(set, SetSpace); }
        set.name = "CheckpointSet_Space";
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

    /// <summary>3.10's order: Start, Crimson, Toon, Pixel, Storybook (3.9's own profiles), Flower 6, Voxel 7, Pencil 8,
    /// SPACE 9, Spice Route 10 (Jade 10 until the Spice Route is built). (Raspberry's 3.10 profile stays on disk, no longer in the set.) PencilCheckpointBuilder.RebuildSet310
    /// hands over to this once the space profile exists.</summary>
    public static void RebuildSet310()
    {
        var sp = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(ProfilePath);
        var pc = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(PencilCheckpointBuilder.ProfilePath);
        var vx = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(VoxelCheckpointBuilder.ProfilePath);
        var set310 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(FlowerCheckpointBuilder.Set310);
        var set39 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(PixelCheckpointBuilder.Set39);
        var flower = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(FlowerCheckpointBuilder.ProfilePath);
        var jade = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(FlowerCheckpointBuilder.Root + "/Profiles/CP10_JadeMoonGarden_3_10.asset");
        if (sp == null || pc == null || vx == null || set310 == null || set39 == null || flower == null) return;
        set310.checkpoints = new List<CheckpointProfile> { set39.Get(1), set39.Get(2), set39.Get(3), set39.Get(4), set39.Get(5), flower, vx, pc, sp };
        // Checkpoint 10: the Spice Route once it is built (SpiceRouteCheckpointBuilder), else Jade.
        var spice = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(SpiceRouteCheckpointBuilder.ProfilePath);
        if (spice != null) set310.checkpoints.Add(spice);
        else if (jade != null) set310.checkpoints.Add(jade);
        EditorUtility.SetDirty(set310);
    }

    // ------------------------------------------------------------------ scenes

    public static DepthCueController.CueLook SpaceCue() => new DepthCueController.CueLook
    {
        name = "Space (Rogue Planet)",
        ladder = 0f, litShare = 0.6f, shadeShare = 0.35f, bottomShare = 0.15f, runnerLadder = 1f, litRight = false,
        lip = 0f, lipWidth = 0.045f,
        contactColor = H("141858"), contact = 0.35f, contactSize = 0.9f, runnerRim = 0f,
        railColor = H("141858"), rail = 0.25f, railWidth = 0.04f,
        trailTint = Color.white, trailTintAmount = 0f, trailIntensity = 1f,
        decor = 0.4f, bloomCut = 0.6f,
    };

    static void WireOpenScene(AtmosphereLook look, CheckpointSet set, bool spaceScene)
    {
        var dir = Object.FindFirstObjectByType<LookTransitionDirector>();
        dir.checkpoints = set;
        dir.baseLookOverride = spaceScene ? look : null;
        dir.blockWipeShader = Shader.Find("PixelCheckpoint/FlatFaceBlockPixel");
        EditorUtility.SetDirty(dir);
        var crc = Object.FindFirstObjectByType<CheckpointRunController>(FindObjectsInactive.Include);
        if (crc != null) { crc.checkpoints = set; EditorUtility.SetDirty(crc); }

        var cues = Object.FindFirstObjectByType<DepthCueController>(FindObjectsInactive.Include);
        if (cues != null)
        {
            var list = new List<DepthCueController.CueLook>(cues.looks);
            if (spaceScene)
            {
                list.Clear();
                for (int i = 0; i < 10; i++) list.Add(SpaceCue());
            }
            else
            {
                int at = list.FindIndex(c => c != null && c.name != null && c.name.StartsWith("Space"));
                if (at < 0 && list.Count > Portal) at = Portal;   // 3.10's slot 9 held Raspberry's cue
                if (at >= 0) list[at] = SpaceCue();
                else list.Add(SpaceCue());
            }
            cues.looks = list.ToArray();
            EditorUtility.SetDirty(cues);
        }

        if (spaceScene)
        {
            var stages = Object.FindFirstObjectByType<LegStageColors>(FindObjectsInactive.Include);
            if (stages != null) { stages.enabled = false; EditorUtility.SetDirty(stages); }
        }

        var old = Object.FindFirstObjectByType<SpaceCheckpointController>(FindObjectsInactive.Include);
        GameObject host = old != null ? old.gameObject : new GameObject("SpaceCheckpoint");
        if (old != null) Object.DestroyImmediate(old);
        var ctl = host.AddComponent<SpaceCheckpointController>();
        ctl.spaceLook = look;
        ctl.alwaysSpace = spaceScene;
        ctl.spacePortal = Portal;
        ctl.backdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Space_Backdrop.mat");
        ctl.spaceRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        ctl.spaceRendererIndex = EnsureRenderer();
        Camera cam = Camera.main;
        var cd = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
        if (cd != null) ctl.baseRendererIndex = new SerializedObject(cd).FindProperty("m_RendererIndex").intValue;
        EditorUtility.SetDirty(ctl);
    }

    public static void Wire310()
    {
        if (AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath) == null) { Debug.LogError("[SpaceCheckpoint] run Build Assets first."); return; }
        var scene = EditorSceneManager.OpenScene(Scene310, OpenSceneMode.Single);
        WireOpenScene(AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath), AssetDatabase.LoadAssetAtPath<CheckpointSet>(FlowerCheckpointBuilder.Set310), false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[SpaceCheckpoint] 3.10 wired: space look at checkpoint 9 (portal 8).");
    }

    public static void MakeSpaceScene()
    {
        if (AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath) == null) { Debug.LogError("[SpaceCheckpoint] run Build Assets first."); return; }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneSpace) == null) AssetDatabase.CopyAsset(Scene310, SceneSpace);
        var scene = EditorSceneManager.OpenScene(SceneSpace, OpenSceneMode.Single);
        WireOpenScene(AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath), AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetSpace), true);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[SpaceCheckpoint] " + SceneSpace + " made: space look for the whole run.");
    }
}
#endif
