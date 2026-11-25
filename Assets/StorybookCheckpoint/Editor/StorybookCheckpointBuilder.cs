#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Builds the storybook checkpoint "Storybook Keep" (re-runnable): Look_Storybook (the pen-and-watercolour castle from the
/// reference: pale limestone, orange tile roofs, half-timbered houses, teal sky with cumulus, green trees on the rocks),
/// its materials and post-processing, StorybookCP_Renderer (= PixelCP_Renderer + the storybook pen pass + the paper pass, an
/// extra renderer index on the pipeline assets), the 3.9 checkpoint set (storybook at checkpoint 5 = portal 4, right after
/// the pixel checkpoint; Raspberry and Jade move to 6 and 7) and both scenes:
///   Tools > Storybook Checkpoint > Build Assets           (no scene is touched)
///   Tools > Storybook Checkpoint > Wire 3.9               (opens MainScene prop camera 3.9, wires, saves it)
///   Tools > Storybook Checkpoint > Make Storybook Scene   (copies 3.9 to MainScene prop camera Storybook, wires it, saves it)
/// 3.7, the Toon / Pixel scenes' looks and their assets are never changed.
/// </summary>
public static class StorybookCheckpointBuilder
{
    public const string Root = "Assets/StorybookCheckpoint";
    public const string Scene39 = PixelCheckpointBuilder.Scene39;
    public const string SceneStory = "Assets/MainScene prop camera Storybook.unity";
    public const string LookPath = "Assets/PropCameraLooks/LookTransition/Looks/Look_Storybook.asset";
    public const string LookName = "Storybook Checkpoint";
    public const string ProfilePath = Root + "/Profiles/CP05_Storybook_3_9.asset";
    public const string SetStory = Root + "/CheckpointSet_Storybook.asset";
    public const string RendererPath = Root + "/Settings/StorybookCP_Renderer.asset";
    const string InkFeature = "StorybookInk", PaperFeature = "StorybookPaper";

    static Color H(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color c); return c; }

    /// <summary>The reference's palette (sRGB).</summary>
    public static class Palette
    {
        // Legs (the base under the house texture): roof on top, limestone sides (lit / cool shade), dark timber under.
        public static Color Top = H("DB632C"), LitSide = H("E6DABE"), ShadeSide = H("A2A6A0"), Bottom = H("5C3C26");
        public static Color BlockShadow = H("8C8C86");
        // Sky: teal #92B9B3 / #7FA8A6 above, cream haze #E2D9BE low; the mist under the bridge #7FA4A6.
        public static Color SkyTop = H("7FA8A6"), SkyMid = H("A9C6BE"), SkyBottom = H("E2D9BE"), SkyGlow = H("F2EBD3");
        public static Color ChasmTop = H("A9C6BE"), ChasmBottom = H("7FA4A6"), ChasmGlow = H("F2EBD3");
        public static Color Accent = H("F2C14E");                 // trail / boost FX: warm gold ink
        public static Color WispA = H("F2EBD3"), WispB = H("FFFFFF"), Switch = H("FFF4D0");
        public static Color Sun = H("FFF1D6"), AmbientSky = H("CFE0DA"), AmbientEquator = H("D8CDB0"), AmbientGround = H("8C8770");
        public static Color Plank = H("B47C4A"), DoorGlow = H("F2C14E"), DoorStone = H("E6DABE"), Arrow = H("FFD34A");
        public static Color Travelled = H("C9BFA4");
        public static Color Danger = H("C8302A");
    }

    [MenuItem("Tools/Storybook Checkpoint/Build Assets")]
    public static void MenuBuild() => BuildAssets();

    [MenuItem("Tools/Storybook Checkpoint/Wire 3.9")]
    public static void MenuWire39() => Wire39();

    [MenuItem("Tools/Storybook Checkpoint/Make Storybook Scene")]
    public static void MenuMakeScene() => MakeStoryScene();

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
        Folder("Assets", "StorybookCheckpoint");
        Folder(Root, "Materials");
        Folder(Root, "Settings");
        Folder(Root, "Profiles");

        // Start from the pixel checkpoint's materials (same roles / shaders), then repaint.
        string P = PixelCheckpointBuilder.Root + "/Materials/";
        Material block = MaterialAt(Root + "/Materials/M_Story_Block.mat", Shader.Find("PropLooks/FlatFaceBlock"), AssetDatabase.LoadAssetAtPath<Material>(P + "M_Pixel_Block.mat"));
        Material fade = MaterialAt(Root + "/Materials/M_Story_BlockFade.mat", Shader.Find("PropLooks/FlatFaceBlock"), AssetDatabase.LoadAssetAtPath<Material>(P + "M_Pixel_BlockFade.mat"));
        Material player = MaterialAt(Root + "/Materials/M_Story_Player.mat", Shader.Find("PropLooks/FlatFaceBlock"), AssetDatabase.LoadAssetAtPath<Material>(P + "M_Pixel_Player.mat"));
        Material obstacle = MaterialAt(Root + "/Materials/M_Story_Obstacle.mat", Shader.Find("PropLooks/FlatFaceBlock"), AssetDatabase.LoadAssetAtPath<Material>(P + "M_Pixel_Obstacle.mat"));
        Material srcSky = AssetDatabase.LoadAssetAtPath<Material>(P + "M_Pixel_Sky.mat");
        Material sky = MaterialAt(Root + "/Materials/M_Story_Sky.mat", srcSky != null ? srcSky.shader : Shader.Find("PropLooks/RadialSky"), srcSky);

        foreach (Material m in new[] { block, fade })
        {
            m.SetColor("_TopColor", Palette.Top);
            m.SetColor("_LeftColor", Palette.ShadeSide);
            m.SetColor("_RightColor", Palette.LitSide);
            m.SetColor("_BottomColor", Palette.Bottom);
            m.SetFloat("_SideSoftness", 0f);
            m.SetFloat("_TopSharpness", 12f);
            m.SetColor("_ShadowColor", Palette.BlockShadow);
            m.SetFloat("_ShadowStrength", 0.35f);
            m.SetFloat("_EdgeStrength", 0f);
            m.SetFloat("_SeamStrength", 0f);
            m.SetFloat("_TileTop", 0f);
            m.SetFloat("_TileSide", 0f);
            m.SetFloat("_GrainStrength", 0f);
            m.SetColor("_FogTopColor", Palette.ChasmTop);
            m.SetColor("_FogBottomColor", Palette.ChasmBottom);
            m.SetFloat("_FogStart", 10f);
            m.SetFloat("_FogEnd", 50f);
            m.SetFloat("_FogMax", 0.55f);
            m.SetFloat("_FogColorFromChasm", 1f);
            m.SetFloat("_ChasmFogMix", 0.3f);
            m.SetColor("_GlowTint", Palette.Accent);
            m.SetFloat("_GlowGain", 0.35f);
            m.SetColor("_EmissiveColor", Color.black);
            m.SetFloat("_PointLightStrength", 0.5f);
        }
        player.CopyPropertiesFromMaterial(block);
        player.SetColor("_TopColor", H("FFD34A")); player.SetColor("_LeftColor", H("C8962E"));
        player.SetColor("_RightColor", H("F2C14E")); player.SetColor("_BottomColor", H("8A6420"));
        obstacle.CopyPropertiesFromMaterial(block);
        obstacle.SetColor("_TopColor", Palette.Danger); obstacle.SetColor("_LeftColor", H("7E1A16"));
        obstacle.SetColor("_RightColor", H("A8231E")); obstacle.SetColor("_BottomColor", H("4A0E0C"));

        sky.SetColor("_TopColor", Palette.SkyTop);
        sky.SetColor("_BottomColor", Palette.SkyBottom);
        sky.SetColor("_MidColor", Palette.SkyMid);
        sky.SetVector("_GlowCenter", new Vector4(0.5f, 0.55f, 0f, 0f));
        sky.SetFloat("_GlowRadius", 0.7f);
        sky.SetFloat("_GlowStrength", 0.3f);
        sky.SetFloat("_GlowFalloff", 1.4f);
        sky.SetColor("_GlowColor", Palette.SkyGlow);
        sky.SetFloat("_HazeHeight", 0.3f);
        sky.SetFloat("_HazeStrength", 0.3f);
        sky.SetColor("_VignetteColor", H("B8AD8E"));
        sky.SetFloat("_Vignette", 0.15f);
        sky.SetFloat("_Rings", 0f);
        sky.SetFloat("_Grain", 0f);

        Material backdrop = MaterialAt(Root + "/Materials/M_Story_Backdrop.mat", Shader.Find("StorybookCheckpoint/Backdrop"), null);
        backdrop.renderQueue = 2502;
        backdrop.SetColor("_SkyTop", H("7FA8A6"));
        backdrop.SetColor("_SkyMid", H("9DC0B9"));
        backdrop.SetColor("_SkyLow", H("E2D9BE"));
        backdrop.SetColor("_CloudColor", H("F4EEDC"));
        backdrop.SetColor("_CloudShade", H("C9CCC0"));
        backdrop.SetColor("_FarColor", H("9FB4B2"));
        backdrop.SetColor("_InkColor", H("3B2A1C"));
        backdrop.SetFloat("_CloudCover", 0.52f);
        MaterialAt(Root + "/Materials/M_Story_Prop.mat", Shader.Find("StorybookCheckpoint/Prop"), null);
        MaterialAt(Root + "/Materials/M_Story_Ink.mat", Shader.Find("Hidden/StorybookCheckpoint/Ink"), null);
        MaterialAt(Root + "/Materials/M_Story_Paper.mat", Shader.Find("Hidden/StorybookCheckpoint/Paper"), null);

        VolumeProfile vol = BuildPost();

        var look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        if (look == null) { look = ScriptableObject.CreateInstance<AtmosphereLook>(); AssetDatabase.CreateAsset(look, LookPath); }
        look.sourceScene = LookName;
        look.Capture(new AtmosphereLook.Sources { block = block, blockFade = fade, player = player, obstacle = obstacle, sky = sky });
        look.hasChasm = true;
        look.chasmTop = Palette.ChasmTop;
        look.chasmBottom = Palette.ChasmBottom;
        look.chasmGlow = Palette.ChasmGlow;
        look.chasmGlowStrength = 0.35f;
        look.chasmGlowCentre = new Vector2(0.5f, 0.6f);
        look.chasmGlowRadius = 0.65f;
        look.sunColor = Palette.Sun;
        look.sunIntensity = 1f;
        look.sunShadowStrength = 0.55f;
        look.sunEuler = new Vector3(50f, 35f, 0f);
        look.ambientMode = (int)AmbientMode.Trilight;
        look.ambientSky = Palette.AmbientSky;
        look.ambientEquator = Palette.AmbientEquator;
        look.ambientGround = Palette.AmbientGround;
        look.cameraBackground = Palette.SkyMid;
        look.volumeProfile = vol;
        look.aheadKeys = new[] { Color.white, new Color(0.99f, 0.98f, 0.96f), new Color(0.96f, 0.95f, 0.92f), new Color(0.92f, 0.91f, 0.88f) };
        look.travelledColor = Palette.Travelled;
        look.doorGlowColor = Palette.DoorGlow;
        look.doorStoneColor = Palette.DoorStone;
        look.sceneAccent = Palette.Accent;
        look.wispColorA = Palette.WispA;
        look.wispColorB = Palette.WispB;
        look.trailSwitchColor = Palette.Switch;
        look.faceColor = H("FFF4D0");
        look.faceSwitchColor = Palette.Switch;
        look.arrowColor = Palette.Arrow;
        look.plankColor = Palette.Plank;
        look.obstacleGlowColor = H("FF5040");
        look.obstacleGlowIntensity = 0.8f;
        look.obstacleGlowRange = 4f;
        look.skyTopColor = Palette.SkyTop;
        EditorUtility.SetDirty(look);

        EnsureRenderer();
        BuildSets(look);
        AssetDatabase.SaveAssets();
        Debug.Log("[StorybookCheckpoint] assets built: " + LookPath);
        return look;
    }

    static VolumeProfile BuildPost()
    {
        string path = Root + "/Settings/Storybook_Volume.asset";
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
        bloom.threshold.Override(1.05f);
        bloom.intensity.Override(0.1f);
        bloom.scatter.Override(0.5f);
        bloom.tint.Override(H("FFF1D6"));
        var ca = Get<ColorAdjustments>();
        ca.postExposure.Override(0.05f);
        ca.contrast.Override(4f);
        ca.saturation.Override(-6f);
        ca.colorFilter.Override(Color.white);
        var vig = Get<Vignette>();
        vig.color.Override(H("5A4630"));
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

    /// <summary>StorybookCP_Renderer = PixelCP_Renderer (SSAO, toon ink, pixel pass - each only draws in its own look) + the
    /// storybook pen pass before the transparents + the paper pass after post. Registered on every pipeline asset; returns its index.</summary>
    public static int EnsureRenderer()
    {
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rd == null)
        {
            AssetDatabase.CopyAsset(PixelCheckpointBuilder.RendererPath, RendererPath);
            AssetDatabase.ImportAsset(RendererPath);
            rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        }
        var ink = Feature(rd, InkFeature);
        ink.passMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Story_Ink.mat");
        ink.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents;
        ink.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
        ink.fetchColorBuffer = true;
        ink.passIndex = 0;
        EditorUtility.SetDirty(ink);
        var paper = Feature(rd, PaperFeature);
        paper.passMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Story_Paper.mat");
        paper.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
        paper.requirements = ScriptableRenderPassInput.None;
        paper.fetchColorBuffer = true;
        paper.passIndex = 0;
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
            else if (index != at) Debug.LogWarning("[StorybookCheckpoint] renderer at index " + at + " on " + asset.name + " but " + index + " elsewhere.");
        }
        AssetDatabase.SaveAssets();
        return index;
    }

    /// <summary>The storybook profile (checkpoint 5 of 3.9), made from the pixel profile the first time.</summary>
    public static CheckpointProfile StoryProfile(AtmosphereLook look, int legs)
    {
        var p = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(ProfilePath);
        if (p == null)
        {
            AssetDatabase.CopyAsset(PixelCheckpointBuilder.Root + "/Profiles/CP04_Pixel_3_9.asset", ProfilePath);
            p = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(ProfilePath);
        }
        p.index = 5;
        p.legIndex = 4 * legs;
        p.displayName = "Storybook Keep";
        p.look = look;
        p.label = "";
        p.keepObstacle = false;
        p.takePlankColor = true;
        p.lockedTeaseColor = Palette.Accent;
        p.menuCamera = new MenuCameraPreset { motion = MenuShotMotion.HighOrbit, positionOffset = new Vector3(0f, 13f, -15f), angles = new Vector2(38f, 140f), fov = 42f, motionSpeed = 0.03f, motionAmplitude = 35f };
        EditorUtility.SetDirty(p);
        return p;
    }

    static void BuildSets(AtmosphereLook look)
    {
        var set37 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(PixelCheckpointBuilder.Set37);
        int L = set37.legsBetweenPortals;
        // 3.9: the pixel builder owns the list; it puts the storybook profile at 5 once it exists.
        StoryProfile(look, L);
        PixelCheckpointBuilder.RebuildSet39();

        // Storybook scene: storybook from the start, every later portal keeps it (placeholders).
        string startPath = Root + "/Profiles/CP01_StoryStart.asset";
        var start = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(startPath);
        if (start == null) { AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(set37.Get(1)), startPath); start = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(startPath); }
        start.index = 1;
        start.legIndex = 0;
        start.displayName = "Storybook Keep";
        start.look = look;
        EditorUtility.SetDirty(start);
        var set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetStory);
        if (set == null) { set = Object.Instantiate(set37); AssetDatabase.CreateAsset(set, SetStory); }
        set.name = "CheckpointSet_Storybook";
        set.legsBetweenPortals = L;
        set.defaultTiming = set37.defaultTiming;
        set.checkpoints = new List<CheckpointProfile> { start };
        for (int i = 2; i <= 10; i++)
        {
            // The pixel scene's placeholders are look-less, index-only: shared.
            var ph = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(PixelCheckpointBuilder.Root + "/Profiles/CP" + i.ToString("00") + "_PixelPlaceholder.asset");
            if (ph != null) set.checkpoints.Add(ph);
        }
        EditorUtility.SetDirty(set);
    }

    // ------------------------------------------------------------------ scenes

    public static DepthCueController.CueLook StoryCue() => new DepthCueController.CueLook
    {
        name = "Storybook (Storybook Keep)",
        ladder = 0f, litShare = 0.6f, shadeShare = 0.35f, bottomShare = 0.15f, runnerLadder = 1f, litRight = false,
        lip = 0f, lipWidth = 0.045f,
        contactColor = H("FFF4D0"), contact = 0.4f, contactSize = 0.9f, runnerRim = 0.2f,
        railColor = H("FFD34A"), rail = 0.4f, railWidth = 0.05f,
        trailTint = Color.white, trailTintAmount = 0f, trailIntensity = 1f,
        decor = 0.6f, bloomCut = 0.4f,
    };

    static StorybookCheckpointController WireOpenScene(AtmosphereLook look, CheckpointSet set, bool storyScene)
    {
        var dir = Object.FindFirstObjectByType<LookTransitionDirector>();
        dir.checkpoints = set;
        dir.baseLookOverride = storyScene ? look : null;
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
            if (storyScene)
            {
                list.Clear();
                for (int i = 0; i < 10; i++) list.Add(StoryCue());
            }
            else
            {
                int at = list.FindIndex(c => c != null && c.name != null && c.name.StartsWith("Storybook"));
                if (at >= 0) list[at] = StoryCue();
                else list.Insert(Mathf.Min(4, list.Count), StoryCue());
            }
            cues.looks = list.ToArray();
            EditorUtility.SetDirty(cues);
        }

        var old = Object.FindFirstObjectByType<StorybookCheckpointController>(FindObjectsInactive.Include);
        GameObject host = old != null ? old.gameObject : new GameObject("StorybookCheckpoint");
        if (old != null) Object.DestroyImmediate(old);
        var ctl = host.AddComponent<StorybookCheckpointController>();
        ctl.storyLook = look;
        ctl.alwaysStory = storyScene;
        ctl.backdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Story_Backdrop.mat");
        ctl.propMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Story_Prop.mat");
        ctl.storyRendererIndex = EnsureRenderer();
        Camera cam = Camera.main;
        var cd = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
        if (cd != null) ctl.baseRendererIndex = new SerializedObject(cd).FindProperty("m_RendererIndex").intValue;
        EditorUtility.SetDirty(ctl);
        return ctl;
    }

    public static void Wire39()
    {
        if (AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath) == null) { Debug.LogError("[StorybookCheckpoint] run Build Assets first."); return; }
        var scene = EditorSceneManager.OpenScene(Scene39, OpenSceneMode.Single);
        var look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        var set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(PixelCheckpointBuilder.Set39);
        WireOpenScene(look, set, false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[StorybookCheckpoint] 3.9 wired: storybook look at checkpoint 5 (portal 4).");
    }

    public static void MakeStoryScene()
    {
        if (AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath) == null) { Debug.LogError("[StorybookCheckpoint] run Build Assets first."); return; }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneStory) == null) AssetDatabase.CopyAsset(Scene39, SceneStory);
        var scene = EditorSceneManager.OpenScene(SceneStory, OpenSceneMode.Single);
        var look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        var set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetStory);
        WireOpenScene(look, set, true);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[StorybookCheckpoint] " + SceneStory + " made: storybook look for the whole run.");
    }
}
#endif
