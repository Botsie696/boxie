#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Builds the flower checkpoint "Blossom Vale" (re-runnable): Look_Flower (the painted flower valley of the reference: cyan
/// sky with white swirls, pale lilac karst with moss caps, blossom trees of every colour, lime meadow), its materials and
/// post-processing, FlowerCP_Renderer (= StorybookCP_Renderer without the pixel / toon passes + the Kuwahara paint pass,
/// an extra renderer index on the pipeline assets), the 3.10 checkpoint set (flower at checkpoint 6 = portal 5, right
/// after Storybook Keep; Raspberry and Jade move to 7 and 8 in 3.10 only - 3.9's set is untouched) and both scenes:
///   Tools > Flower Checkpoint > Build Assets         (no scene is touched)
///   Tools > Flower Checkpoint > Wire 3.10            (opens MainScene prop camera 3.10, wires, saves it)
///   Tools > Flower Checkpoint > Make Flower Scene    (copies 3.10 to MainScene prop camera Flower, wires it, saves it)
/// </summary>
public static class FlowerCheckpointBuilder
{
    public const string Root = "Assets/FlowerCheckpoint";
    public const string Scene310 = "Assets/MainScene prop camera 3.10.unity";
    public const string SceneFlower = "Assets/MainScene prop camera Flower.unity";
    public const string LookPath = "Assets/PropCameraLooks/LookTransition/Looks/Look_Flower.asset";
    public const string LookName = "Flower Checkpoint";
    public const string ProfilePath = Root + "/Profiles/CP06_Flower_3_10.asset";
    public const string Set310 = Root + "/CheckpointSet_3_10.asset";
    public const string SetFlower = Root + "/CheckpointSet_Flower.asset";
    public const string RendererPath = Root + "/Settings/FlowerCP_Renderer.asset";
    const string PaintFeature = "FlowerPaint";

    static Color H(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color c); return c; }

    /// <summary>The reference's palette (sRGB).</summary>
    public static class Palette
    {
        // Legs (the base under the meadow texture): lime grass on top, moss sides (lit / shade), root soil under.
        public static Color Top = H("7CC83A"), LitSide = H("4F9E4A"), ShadeSide = H("2E6E3A"), Bottom = H("4A3040");
        public static Color BlockShadow = H("5A6A8C");
        public static Color SkyTop = H("2FA6E0"), SkyMid = H("6CC8EE"), SkyBottom = H("CDEFF2"), SkyGlow = H("FFFFFF");
        public static Color ChasmTop = H("D8F1EC"), ChasmBottom = H("9ED8D2"), ChasmGlow = H("FFFFFF");
        public static Color Accent = H("F27BB5");                  // blossom pink: trail flash / boost FX
        public static Color WispA = H("FFFFFF"), WispB = H("F7C8E0"), Switch = H("FFF0F6");
        public static Color Sun = H("FFF6E0"), AmbientSky = H("BFE6F5"), AmbientEquator = H("D8EBD0"), AmbientGround = H("7A9A6A");
        public static Color Plank = H("C0703A"), DoorGlow = H("F27BB5"), DoorStone = H("DCD6E2"), Arrow = H("FFD34A");
        public static Color Travelled = H("A9C79A");
        public static Color Danger = H("E8361E");
    }

    [MenuItem("Tools/Flower Checkpoint/Build Assets")]
    public static void MenuBuild() => BuildAssets();

    [MenuItem("Tools/Flower Checkpoint/Wire 3.10")]
    public static void MenuWire310() => Wire310();

    [MenuItem("Tools/Flower Checkpoint/Make Flower Scene")]
    public static void MenuMakeScene() => MakeFlowerScene();

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
        Folder("Assets", "FlowerCheckpoint");
        Folder(Root, "Materials");
        Folder(Root, "Settings");
        Folder(Root, "Profiles");

        string S = StorybookCheckpointBuilder.Root + "/Materials/";
        Shader ff = Shader.Find("PropLooks/FlatFaceBlock");
        Material block = MaterialAt(Root + "/Materials/M_Flower_Block.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Story_Block.mat"));
        Material fade = MaterialAt(Root + "/Materials/M_Flower_BlockFade.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Story_BlockFade.mat"));
        Material player = MaterialAt(Root + "/Materials/M_Flower_Player.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Story_Player.mat"));
        Material obstacle = MaterialAt(Root + "/Materials/M_Flower_Obstacle.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Story_Obstacle.mat"));
        Material srcSky = AssetDatabase.LoadAssetAtPath<Material>(S + "M_Story_Sky.mat");
        Material sky = MaterialAt(Root + "/Materials/M_Flower_Sky.mat", srcSky != null ? srcSky.shader : Shader.Find("PropLooks/RadialSky"), srcSky);

        foreach (Material m in new[] { block, fade })
        {
            m.SetColor("_TopColor", Palette.Top);
            m.SetColor("_LeftColor", Palette.ShadeSide);
            m.SetColor("_RightColor", Palette.LitSide);
            m.SetColor("_BottomColor", Palette.Bottom);
            m.SetColor("_ShadowColor", Palette.BlockShadow);
            m.SetFloat("_ShadowStrength", 0.3f);
            m.SetColor("_FogTopColor", Palette.ChasmTop);
            m.SetColor("_FogBottomColor", Palette.ChasmBottom);
            m.SetFloat("_FogStart", 12f);
            m.SetFloat("_FogEnd", 55f);
            m.SetFloat("_FogMax", 0.45f);
            m.SetFloat("_ChasmFogMix", 0.25f);
            m.SetColor("_GlowTint", Palette.Accent);
            m.SetFloat("_GlowGain", 0.3f);
        }
        obstacle.CopyPropertiesFromMaterial(block);
        obstacle.SetColor("_TopColor", Palette.Danger); obstacle.SetColor("_LeftColor", H("8A1A10"));
        obstacle.SetColor("_RightColor", H("C42A16")); obstacle.SetColor("_BottomColor", H("4A0E08"));

        sky.SetColor("_TopColor", Palette.SkyTop);
        sky.SetColor("_BottomColor", Palette.SkyBottom);
        sky.SetColor("_MidColor", Palette.SkyMid);
        sky.SetVector("_GlowCenter", new Vector4(0.5f, 0.55f, 0f, 0f));
        sky.SetFloat("_GlowRadius", 0.7f);
        sky.SetFloat("_GlowStrength", 0.25f);
        sky.SetColor("_GlowColor", Palette.SkyGlow);
        sky.SetColor("_VignetteColor", H("5FA8C8"));
        sky.SetFloat("_Vignette", 0.1f);

        Material backdrop = MaterialAt(Root + "/Materials/M_Flower_Backdrop.mat", Shader.Find("FlowerCheckpoint/Backdrop"), null);
        backdrop.renderQueue = 2502;
        backdrop.SetColor("_SkyTop", Palette.SkyTop);
        backdrop.SetColor("_SkyMid", Palette.SkyMid);
        backdrop.SetColor("_SkyLow", Palette.SkyBottom);
        backdrop.SetColor("_SwirlColor", H("F6FBFD"));
        backdrop.SetColor("_SwirlShade", H("A6D6EC"));
        backdrop.SetColor("_FarStone", H("D2D0DE"));
        backdrop.SetColor("_FarShade", H("9EA6C6"));
        backdrop.SetColor("_FarMoss", H("8CC25E"));
        backdrop.SetColor("_MistColor", H("E4F6F2"));
        MaterialAt(Root + "/Materials/M_Flower_Prop.mat", Shader.Find("FlowerCheckpoint/Prop"), null);
        MaterialAt(Root + "/Materials/M_Flower_Petal.mat", Shader.Find("FlowerCheckpoint/Petal"), null);
        MaterialAt(Root + "/Materials/M_Flower_Paint.mat", Shader.Find("Hidden/FlowerCheckpoint/Paint"), null);

        VolumeProfile vol = BuildPost();

        var look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        if (look == null) { look = ScriptableObject.CreateInstance<AtmosphereLook>(); AssetDatabase.CreateAsset(look, LookPath); }
        look.sourceScene = LookName;
        look.Capture(new AtmosphereLook.Sources { block = block, blockFade = fade, player = player, obstacle = obstacle, sky = sky });
        look.hasChasm = true;
        look.chasmTop = Palette.ChasmTop;
        look.chasmBottom = Palette.ChasmBottom;
        look.chasmGlow = Palette.ChasmGlow;
        look.chasmGlowStrength = 0.3f;
        look.chasmGlowCentre = new Vector2(0.5f, 0.6f);
        look.chasmGlowRadius = 0.65f;
        look.sunColor = Palette.Sun;
        look.sunIntensity = 1.05f;
        look.sunShadowStrength = 0.5f;
        look.sunEuler = new Vector3(50f, 35f, 0f);
        look.ambientMode = (int)AmbientMode.Trilight;
        look.ambientSky = Palette.AmbientSky;
        look.ambientEquator = Palette.AmbientEquator;
        look.ambientGround = Palette.AmbientGround;
        look.cameraBackground = Palette.SkyMid;
        look.volumeProfile = vol;
        look.aheadKeys = new[] { Color.white, new Color(0.99f, 0.99f, 0.98f), new Color(0.96f, 0.97f, 0.95f), new Color(0.92f, 0.94f, 0.92f) };
        look.travelledColor = Palette.Travelled;
        look.doorGlowColor = Palette.DoorGlow;
        look.doorStoneColor = Palette.DoorStone;
        look.sceneAccent = Palette.Accent;
        look.wispColorA = Palette.WispA;
        look.wispColorB = Palette.WispB;
        look.trailSwitchColor = Palette.Switch;
        look.faceColor = H("FFF0F6");
        look.faceSwitchColor = Palette.Switch;
        look.arrowColor = Palette.Arrow;
        look.plankColor = Palette.Plank;
        look.obstacleGlowColor = H("FF5A30");
        look.obstacleGlowIntensity = 0.8f;
        look.obstacleGlowRange = 4f;
        look.skyTopColor = Palette.SkyTop;
        EditorUtility.SetDirty(look);

        EnsureRenderer();
        BuildSets(look);
        AssetDatabase.SaveAssets();
        Debug.Log("[FlowerCheckpoint] assets built: " + LookPath);
        return look;
    }

    static VolumeProfile BuildPost()
    {
        string path = Root + "/Settings/Flower_Volume.asset";
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
        bloom.threshold.Override(1.0f);
        bloom.intensity.Override(0.15f);
        bloom.scatter.Override(0.55f);
        bloom.tint.Override(H("FFF0F6"));
        var ca = Get<ColorAdjustments>();
        ca.postExposure.Override(0.08f);
        ca.contrast.Override(8f);
        ca.saturation.Override(14f);
        ca.colorFilter.Override(Color.white);
        var vig = Get<Vignette>();
        vig.color.Override(H("1E4A6A"));
        vig.intensity.Override(0.16f);
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

    /// <summary>FlowerCP_Renderer = StorybookCP_Renderer (SSAO + storybook pen / paper, used during the storybook -> flower
    /// wipe; the controller switches them off otherwise) without the toon ink / pixel-art passes, + the flower paint pass
    /// after post. Registered on every pipeline asset; returns its index.</summary>
    public static int EnsureRenderer()
    {
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rd == null)
        {
            AssetDatabase.CopyAsset(StorybookCheckpointBuilder.RendererPath, RendererPath);
            AssetDatabase.ImportAsset(RendererPath);
            rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        }
        foreach (var f in rd.rendererFeatures)
            if (f != null && (f.name == "PixelArtPass" || f.name == "ToonInkOutline")) { f.SetActive(false); EditorUtility.SetDirty(f); }
        var paint = Feature(rd, PaintFeature);
        paint.passMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Flower_Paint.mat");
        paint.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
        paint.requirements = ScriptableRenderPassInput.None;
        paint.fetchColorBuffer = true;
        paint.passIndex = 0;
        EditorUtility.SetDirty(paint);
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
            else if (index != at) Debug.LogWarning("[FlowerCheckpoint] renderer at index " + at + " on " + asset.name + " but " + index + " elsewhere.");
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

    static void BuildSets(AtmosphereLook look)
    {
        var set39 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(PixelCheckpointBuilder.Set39);
        var set36 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(PixelCheckpointBuilder.Set36);
        int L = set39.legsBetweenPortals;

        // 3.10: Start, Crimson, Toon, Pixel, Storybook (3.9's own profiles), FLOWER = 6, Raspberry 7, Jade 8, placeholders 9-10.
        var flower = CopyProfile(set39.Get(5), ProfilePath, 6, L);
        flower.displayName = "Blossom Vale";
        flower.look = look;
        flower.label = "";
        flower.keepObstacle = false;
        flower.takePlankColor = true;
        flower.lockedTeaseColor = Palette.Accent;
        flower.menuCamera = new MenuCameraPreset { motion = MenuShotMotion.LowSideTrack, positionOffset = new Vector3(0f, 3.5f, -12.5f), angles = new Vector2(14f, 215f), fov = 46f, motionSpeed = 0.03f, motionAmplitude = 5f };
        EditorUtility.SetDirty(flower);
        // Voxel (Brick Meadow) sits at 7 once built, so Raspberry / Jade are 8 / 9 (VoxelCheckpointBuilder.RebuildSet310).
        bool voxel = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(VoxelCheckpointBuilder.ProfilePath) != null;
        var rasp = voxel ? null : CopyProfile(set39.Get(6), Root + "/Profiles/CP07_Raspberry_3_10.asset", 7, L);
        var jade = voxel ? null : CopyProfile(set39.Get(7), Root + "/Profiles/CP08_JadeMoonGarden_3_10.asset", 8, L);

        var set310 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(Set310);
        if (set310 == null) { set310 = Object.Instantiate(set39); AssetDatabase.CreateAsset(set310, Set310); }
        set310.name = "CheckpointSet_3_10";
        set310.legsBetweenPortals = L;
        set310.defaultTiming = set39.defaultTiming;
        set310.checkpoints = new List<CheckpointProfile> { set39.Get(1), set39.Get(2), set39.Get(3), set39.Get(4), set39.Get(5), flower, rasp, jade };
        for (int i = 9; i <= 10; i++) if (set36.Get(i) != null) set310.checkpoints.Add(set36.Get(i));
        EditorUtility.SetDirty(set310);
        if (voxel) VoxelCheckpointBuilder.RebuildSet310();

        // Flower scene: flower from the start, every later portal keeps it (the pixel scene's look-less placeholders).
        string startPath = Root + "/Profiles/CP01_FlowerStart.asset";
        var start = CopyProfile(set39.Get(1), startPath, 1, L);
        start.displayName = "Blossom Vale";
        start.look = look;
        EditorUtility.SetDirty(start);
        var set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetFlower);
        if (set == null) { set = Object.Instantiate(set39); AssetDatabase.CreateAsset(set, SetFlower); }
        set.name = "CheckpointSet_Flower";
        set.legsBetweenPortals = L;
        set.defaultTiming = set39.defaultTiming;
        set.checkpoints = new List<CheckpointProfile> { start };
        for (int i = 2; i <= 10; i++)
        {
            var ph = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(PixelCheckpointBuilder.Root + "/Profiles/CP" + i.ToString("00") + "_PixelPlaceholder.asset");
            if (ph != null) set.checkpoints.Add(ph);
        }
        EditorUtility.SetDirty(set);

        // The 3.10 leg stages get their flower level (index 5 = portals crossed).
        var palette = AssetDatabase.LoadAssetAtPath<LegStagePalette>("Assets/LegStages310/LegStagePalette_3_10.asset");
        if (palette != null) { palette.FillDefaults(); EditorUtility.SetDirty(palette); }
    }

    // ------------------------------------------------------------------ scenes

    public static DepthCueController.CueLook FlowerCue() => new DepthCueController.CueLook
    {
        name = "Flower (Blossom Vale)",
        ladder = 0f, litShare = 0.6f, shadeShare = 0.35f, bottomShare = 0.15f, runnerLadder = 1f, litRight = false,
        lip = 0f, lipWidth = 0.045f,
        contactColor = H("FFF6E0"), contact = 0.4f, contactSize = 0.9f, runnerRim = 0.2f,
        railColor = H("FFD34A"), rail = 0.4f, railWidth = 0.05f,
        trailTint = Color.white, trailTintAmount = 0f, trailIntensity = 1f,
        decor = 0.6f, bloomCut = 0.4f,
    };

    static void WireOpenScene(AtmosphereLook look, CheckpointSet set, bool flowerScene)
    {
        var dir = Object.FindFirstObjectByType<LookTransitionDirector>();
        dir.checkpoints = set;
        dir.baseLookOverride = flowerScene ? look : null;
        dir.blockWipeShader = Shader.Find("PixelCheckpoint/FlatFaceBlockPixel");
        EditorUtility.SetDirty(dir);
        var crc = Object.FindFirstObjectByType<CheckpointRunController>(FindObjectsInactive.Include);
        if (crc != null) { crc.checkpoints = set; EditorUtility.SetDirty(crc); }
        var chasm = Object.FindFirstObjectByType<ChasmFog>();
        chasm.fogLitShader = Shader.Find("Custom/PixelCheckpoint/PixelRockLit");
        EditorUtility.SetDirty(chasm);

        var cues = Object.FindFirstObjectByType<DepthCueController>(FindObjectsInactive.Include);
        if (cues != null)
        {
            var list = new List<DepthCueController.CueLook>(cues.looks);
            if (flowerScene)
            {
                list.Clear();
                for (int i = 0; i < 10; i++) list.Add(FlowerCue());
            }
            else
            {
                int at = list.FindIndex(c => c != null && c.name != null && c.name.StartsWith("Flower"));
                if (at >= 0) list[at] = FlowerCue();
                else list.Insert(Mathf.Min(5, list.Count), FlowerCue());
            }
            cues.looks = list.ToArray();
            EditorUtility.SetDirty(cues);
        }

        if (flowerScene)
        {
            // Leg stages are 3.10's level progression; a single-look scene keeps the look as authored.
            var stages = Object.FindFirstObjectByType<LegStageColors>(FindObjectsInactive.Include);
            if (stages != null) { stages.enabled = false; EditorUtility.SetDirty(stages); }
        }

        var old = Object.FindFirstObjectByType<FlowerCheckpointController>(FindObjectsInactive.Include);
        GameObject host = old != null ? old.gameObject : new GameObject("FlowerCheckpoint");
        if (old != null) Object.DestroyImmediate(old);
        var ctl = host.AddComponent<FlowerCheckpointController>();
        ctl.flowerLook = look;
        ctl.alwaysFlower = flowerScene;
        ctl.flowerPortal = 5;
        ctl.backdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Flower_Backdrop.mat");
        ctl.propMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Flower_Prop.mat");
        ctl.petalMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Flower_Petal.mat");
        ctl.flowerRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        ctl.flowerRendererIndex = EnsureRenderer();
        Camera cam = Camera.main;
        var cd = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
        if (cd != null) ctl.baseRendererIndex = new SerializedObject(cd).FindProperty("m_RendererIndex").intValue;
        EditorUtility.SetDirty(ctl);
    }

    public static void Wire310()
    {
        if (AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath) == null) { Debug.LogError("[FlowerCheckpoint] run Build Assets first."); return; }
        var scene = EditorSceneManager.OpenScene(Scene310, OpenSceneMode.Single);
        WireOpenScene(AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath), AssetDatabase.LoadAssetAtPath<CheckpointSet>(Set310), false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[FlowerCheckpoint] 3.10 wired: flower look at checkpoint 6 (portal 5).");
    }

    public static void MakeFlowerScene()
    {
        if (AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath) == null) { Debug.LogError("[FlowerCheckpoint] run Build Assets first."); return; }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneFlower) == null) AssetDatabase.CopyAsset(Scene310, SceneFlower);
        var scene = EditorSceneManager.OpenScene(SceneFlower, OpenSceneMode.Single);
        WireOpenScene(AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath), AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetFlower), true);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[FlowerCheckpoint] " + SceneFlower + " made: flower look for the whole run.");
    }
}
#endif
