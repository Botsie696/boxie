#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Builds the toon checkpoint (re-runnable): Look_Toon (the cartoon look from the Pokemon-ruins reference: slate beam with
/// moss and a sunlit lip, ochre cliffs with teal shade, teal-to-mint sky with painted clouds, meadow floor, ink outlines),
/// its materials and post-processing, the ink renderer (ToonCP_Renderer, an extra renderer index on the pipeline assets),
/// 3.7's own checkpoint set (toon at checkpoint 3 = portal 2; Raspberry and Jade move to 4 and 5) and both scenes:
///   Tools > Toon Checkpoint > Build Assets            (no scene is touched)
///   Tools > Toon Checkpoint > Wire 3.7                (opens MainScene prop camera 3.7, wires, saves it)
///   Tools > Toon Checkpoint > Make Toon Scene         (copies 3.7 to MainScene prop camera Toon, wires it, saves it)
/// The palette is in <see cref="Palette"/>; the toon-only shading constants are on the scene's ToonCheckpointController.
/// </summary>
public static class ToonCheckpointBuilder
{
    public const string Root = "Assets/ToonCheckpoint";
    public const string Scene37 = "Assets/MainScene prop camera 3.7.unity";
    public const string SceneToon = "Assets/MainScene prop camera Toon.unity";
    public const string LookPath = "Assets/PropCameraLooks/LookTransition/Looks/Look_Toon.asset";
    public const string LookName = "Toon Checkpoint";
    public const string Set36 = "Assets/Checkpoints/CheckpointSet_3_6.asset";
    public const string Set37 = Root + "/CheckpointSet_3_7.asset";
    public const string SetToon = Root + "/CheckpointSet_Toon.asset";
    const string RendererSource = "Assets/PropCameraLooks/Settings/PropLooks_Renderer.asset";
    public const string RendererPath = Root + "/Settings/ToonCP_Renderer.asset";

    static Color H(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color c); return c; }

    /// <summary>The measured reference palette (sRGB). Reference samples in the comments.</summary>
    public static class Palette
    {
        // Path = the stone beam the characters sit on (top #363E50..#648892, side #293F45..#1C202C), lifted for readability.
        public static Color Top = H("6E8C98"), LitSide = H("4D6878"), ShadeSide = H("34475A"), Bottom = H("222D3C");
        public static Color BlockShadow = H("7F9CB0");
        // Sky: top #2797A6, mid #7ED6B9, horizon #C6FBE4 / #D1F4C6.
        public static Color SkyTop = H("2797A6"), SkyMid = H("6CCDB8"), SkyBottom = H("C6EFD5"), SkyGlow = H("E8FAD8");
        // The valley mist the cliffs stand in: greener than the sky's horizon, so the bottom of the screen reads as ground.
        public static Color Valley = H("97CFA2");
        public static Color Accent = H("FF8A3D");               // flame orange: the trail (the runner is the yellow block)
        public static Color WispA = H("FFC46B"), WispB = H("FF7A45"), Switch = H("FFF1C2");
        public static Color Sun = H("FFF1D0"), AmbientSky = H("A9D8CC"), AmbientEquator = H("8CC2B4"), AmbientGround = H("5E8C7A");
        public static Color Plank = H("8B5A3C"), DoorGlow = H("FFE9A8"), DoorStone = H("D8C474"), Arrow = H("FFE08A");
        public static Color Travelled = H("9FB4B8");
    }

    [MenuItem("Tools/Toon Checkpoint/Build Assets")]
    public static void MenuBuild() => BuildAssets();

    [MenuItem("Tools/Toon Checkpoint/Wire 3.7")]
    public static void MenuWire37() => Wire37();

    [MenuItem("Tools/Toon Checkpoint/Make Toon Scene")]
    public static void MenuMakeToon() => MakeToonScene();

    static void Folder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }

    static T LoadOrCreate<T>(string path, System.Func<T> make) where T : Object
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a != null) return a;
        a = make();
        AssetDatabase.CreateAsset(a, path);
        return a;
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
        return m;
    }

    // ------------------------------------------------------------------ assets

    /// <summary>Everything except the scenes. Needs the 3.7 scene's look materials as a starting point (loaded by path).</summary>
    public static AtmosphereLook BuildAssets()
    {
        Folder("Assets", "ToonCheckpoint");
        Folder(Root, "Materials");
        Folder(Root, "Settings");
        Folder(Root, "Profiles");

        // The 3.7 look materials (what LookTransitionDirector in 3.7 points at): the toon block starts as a copy of them,
        // so every setting the look does not carry (fog distances, glow baseline...) is 3.7's.
        Material srcBlock = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("2cfcdc4db1ad76f46a6c8bde2a989a46"));
        Material srcFade = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("3f35963beac0de64b8ccc39075527ea2"));
        Material srcPlayer = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("d9d2bca30491764498ad26a765044ca5"));
        Material srcObstacle = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("38c4b0d08a173f54e8db9b5e976bf037"));
        Material srcSky = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("a1b8d2d13e6f68145b14a34be2073e2d"));

        Shader flat = Shader.Find("PropLooks/FlatFaceBlock");
        Shader radial = srcSky != null ? srcSky.shader : Shader.Find("PropLooks/RadialSky");

        Material block = MaterialAt(Root + "/Materials/M_Toon_Block.mat", flat, srcBlock);
        Material fade = MaterialAt(Root + "/Materials/M_Toon_BlockFade.mat", flat, srcFade);
        Material player = MaterialAt(Root + "/Materials/M_Toon_Player.mat", flat, srcPlayer);
        Material obstacle = MaterialAt(Root + "/Materials/M_Toon_Obstacle.mat", flat, srcObstacle);
        Material sky = MaterialAt(Root + "/Materials/M_Toon_Sky.mat", radial, srcSky);

        foreach (Material m in new[] { block, fade })
        {
            m.SetColor("_TopColor", Palette.Top);
            m.SetColor("_LeftColor", Palette.ShadeSide);
            m.SetColor("_RightColor", Palette.LitSide);
            m.SetColor("_BottomColor", Palette.Bottom);
            m.SetFloat("_SideSoftness", 0.0f);
            m.SetFloat("_TopSharpness", 10f);
            m.SetColor("_ShadowColor", Palette.BlockShadow);
            m.SetFloat("_ShadowStrength", 0.5f);
            m.SetFloat("_EdgeStrength", 0f);       // the toon lip / ink replace the rim
            m.SetFloat("_SeamStrength", 0f);       // never a line between cubes
            m.SetFloat("_TileTop", 0f);
            m.SetFloat("_TileSide", 0f);
            m.SetFloat("_GrainStrength", 0f);      // the toon paper grain replaces it
            m.SetColor("_FogTopColor", Palette.SkyTop);
            m.SetColor("_FogBottomColor", Palette.Valley);
            m.SetFloat("_FogStart", 8f);
            m.SetFloat("_FogEnd", 46f);
            m.SetFloat("_FogMax", 0.55f);
            m.SetFloat("_FogColorFromChasm", 1f);
            m.SetFloat("_ChasmFogMix", 0.3f);
            m.SetColor("_GlowTint", H("FFC46B"));
            m.SetFloat("_GlowGain", 0.5f);
            m.SetColor("_EmissiveColor", Color.black);
            m.SetFloat("_PointLightStrength", 0.6f);
        }
        player.CopyPropertiesFromMaterial(block);
        obstacle.CopyPropertiesFromMaterial(block);
        obstacle.SetColor("_TopColor", H("3A2E3A")); obstacle.SetColor("_LeftColor", H("241C26"));
        obstacle.SetColor("_RightColor", H("2E2430")); obstacle.SetColor("_BottomColor", H("171218"));

        sky.SetColor("_TopColor", Palette.SkyTop);
        sky.SetColor("_BottomColor", Palette.SkyBottom);
        sky.SetColor("_MidColor", Palette.SkyMid);
        sky.SetVector("_GlowCenter", new Vector4(0.5f, 0.3f, 0f, 0f));
        sky.SetFloat("_GlowRadius", 0.75f);
        sky.SetFloat("_GlowStrength", 0.6f);
        sky.SetFloat("_GlowFalloff", 1.3f);
        sky.SetColor("_GlowColor", Palette.SkyGlow);
        sky.SetFloat("_HazeHeight", 0.3f);
        sky.SetFloat("_HazeStrength", 0.35f);
        sky.SetColor("_VignetteColor", H("1B6E7A"));
        sky.SetFloat("_Vignette", 0.18f);
        sky.SetFloat("_Rings", 0f);
        sky.SetFloat("_Grain", 0.012f);

        // Toon-only materials.
        Material clouds = MaterialAt(Root + "/Materials/M_Toon_Clouds.mat", Shader.Find("ToonCheckpoint/Clouds"), null);
        Material ground = MaterialAt(Root + "/Materials/M_Toon_Ground.mat", Shader.Find("ToonCheckpoint/Ground"), null);
        Material trailSrc = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/RunnerTrail/RunnerFaceTrail.mat");
        MaterialAt(Root + "/Materials/M_Toon_FaceTrail.mat", Shader.Find("ToonCheckpoint/TrailFX Face Ribbon Toon"), trailSrc);
        MaterialAt(Root + "/Materials/M_Toon_InkOutline.mat", Shader.Find("Hidden/ToonCheckpoint/InkOutline"), null);
        EditorUtility.SetDirty(clouds); EditorUtility.SetDirty(ground);

        VolumeProfile post = BuildPost();

        // The look.
        var look = LoadOrCreate(LookPath, () => ScriptableObject.CreateInstance<AtmosphereLook>());
        look.sourceScene = LookName;
        look.Capture(new AtmosphereLook.Sources { block = block, blockFade = fade, player = player, obstacle = obstacle, sky = sky });
        // Toon flag (slot 15.z) and ink (15.w) for the path and the crossed legs; the runner / obstacle keep their own shading.
        foreach (int role in new[] { AtmosphereLook.RoleBlock, AtmosphereLook.RoleBlockFade })
        {
            Vector4 v = look.blockRoles[role * AtmosphereLook.BlockStride + 15];
            v.z = 1f; v.w = 1f;
            look.blockRoles[role * AtmosphereLook.BlockStride + 15] = v;
        }
        look.hasChasm = true;
        look.chasmTop = Palette.SkyTop;
        look.chasmBottom = Palette.Valley;
        look.chasmGlow = Palette.SkyGlow;
        look.chasmGlowStrength = 0.55f;
        look.chasmGlowCentre = new Vector2(0.55f, 0.22f);
        look.chasmGlowRadius = 0.6f;
        look.sunColor = Palette.Sun;
        look.sunIntensity = 1.05f;
        look.sunShadowStrength = 0.75f;
        look.sunEuler = new Vector3(50f, -30f, 0f);
        look.ambientMode = (int)AmbientMode.Trilight;
        look.ambientSky = Palette.AmbientSky;
        look.ambientEquator = Palette.AmbientEquator;
        look.ambientGround = Palette.AmbientGround;
        look.cameraBackground = Palette.SkyTop;
        look.volumeProfile = post;
        look.aheadKeys = new[] { Color.white, new Color(0.98f, 0.99f, 0.99f), new Color(0.94f, 0.97f, 0.97f), new Color(0.9f, 0.94f, 0.95f) };
        look.travelledColor = Palette.Travelled;
        look.doorGlowColor = Palette.DoorGlow;
        look.doorStoneColor = Palette.DoorStone;
        look.sceneAccent = Palette.Accent;
        look.wispColorA = Palette.WispA;
        look.wispColorB = Palette.WispB;
        look.trailSwitchColor = Palette.Switch;
        look.faceColor = H("FFE7A8");
        look.faceSwitchColor = Palette.Switch;
        look.arrowColor = Palette.Arrow;
        look.plankColor = Palette.Plank;
        look.obstacleGlowColor = Palette.Accent;
        look.obstacleGlowIntensity = 1f;
        look.obstacleGlowRange = 4f;
        look.skyTopColor = Palette.SkyTop;
        EditorUtility.SetDirty(look);

        EnsureRenderer();
        BuildSets(look);
        AssetDatabase.SaveAssets();
        Debug.Log("[ToonCheckpoint] assets built: " + LookPath);
        return look;
    }

    static VolumeProfile BuildPost()
    {
        string path = Root + "/Settings/Toon_Volume.asset";
        var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
        }
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
        bloom.intensity.Override(0.3f);
        bloom.scatter.Override(0.55f);
        bloom.tint.Override(H("FFF4D8"));
        var ca = Get<ColorAdjustments>();
        ca.postExposure.Override(0.05f);
        ca.contrast.Override(10f);
        ca.saturation.Override(14f);
        ca.colorFilter.Override(Color.white);
        var vig = Get<Vignette>();
        vig.color.Override(H("0F3B44"));
        vig.intensity.Override(0.22f);
        vig.smoothness.Override(0.45f);
        var grain = Get<FilmGrain>();
        grain.intensity.Override(0f);
        grain.active = false;
        EditorUtility.SetDirty(p);
        return p;
    }

    /// <summary>ToonCP_Renderer = PropLooks_Renderer (SSAO) + the full-screen ink pass, registered on every pipeline asset. Returns its index.</summary>
    public static int EnsureRenderer()
    {
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rd == null)
        {
            AssetDatabase.CopyAsset(RendererSource, RendererPath);
            AssetDatabase.ImportAsset(RendererPath);
            rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        }
        FullScreenPassRendererFeature ink = null;
        foreach (var f in rd.rendererFeatures) if (f is FullScreenPassRendererFeature fs) ink = fs;
        if (ink == null)
        {
            ink = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            ink.name = "ToonInkOutline";
            AssetDatabase.AddObjectToAsset(ink, rd);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ink, out string _, out long localId);
            var so = new SerializedObject(rd);
            var feats = so.FindProperty("m_RendererFeatures");
            feats.arraySize++;
            feats.GetArrayElementAtIndex(feats.arraySize - 1).objectReferenceValue = ink;
            var map = so.FindProperty("m_RendererFeatureMap");
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        ink.passMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Toon_InkOutline.mat");
        ink.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents;
        ink.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
        ink.fetchColorBuffer = true;
        ink.passIndex = 0;
        EditorUtility.SetDirty(ink);
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
            else if (index != at) Debug.LogWarning("[ToonCheckpoint] renderer at index " + at + " on " + asset.name + " but " + index + " elsewhere.");
        }
        AssetDatabase.SaveAssets();
        return index;
    }

    static CheckpointProfile ProfileCopy(string srcPath, string dstPath, int index)
    {
        var p = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(dstPath);
        if (p == null)
        {
            AssetDatabase.CopyAsset(srcPath, dstPath);
            p = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(dstPath);
        }
        p.index = index;
        EditorUtility.SetDirty(p);
        return p;
    }

    static void BuildSets(AtmosphereLook look)
    {
        var set36 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(Set36);
        string P(int i) => AssetDatabase.GetAssetPath(set36.Get(i));

        // 3.7: Start, Crimson, TOON, Raspberry, Jade, placeholders 6-10 (the shared 3.6 profiles that keep their index).
        var toon = ProfileCopy(P(3), Root + "/Profiles/CP03_Toon_3_7.asset", 3);
        toon.displayName = "Overgrown Ruins";
        toon.look = look;
        toon.label = "";
        toon.keepObstacle = false;
        toon.takePlankColor = true;
        toon.lockedTeaseColor = Palette.Accent;
        toon.menuCamera = new MenuCameraPreset { motion = MenuShotMotion.LowSideTrack, positionOffset = new Vector3(0f, 3.5f, -12.5f), angles = new Vector2(14f, 150f), fov = 46f, motionSpeed = 0.03f, motionAmplitude = 5f };
        EditorUtility.SetDirty(toon);
        var rasp = ProfileCopy(P(3), Root + "/Profiles/CP04_Raspberry_3_7.asset", 4);
        var jade = ProfileCopy(P(4), Root + "/Profiles/CP05_JadeMoonGarden_3_7.asset", 5);

        var set37 = LoadOrCreate(Set37, () => Object.Instantiate(set36));
        set37.name = "CheckpointSet_3_7";
        set37.legsBetweenPortals = set36.legsBetweenPortals;
        set37.defaultTiming = set36.defaultTiming;
        set37.checkpoints = new List<CheckpointProfile> { set36.Get(1), set36.Get(2), toon, rasp, jade };
        for (int i = 6; i <= 10; i++) if (set36.Get(i) != null) set37.checkpoints.Add(set36.Get(i));
        EditorUtility.SetDirty(set37);

        // Toon scene: toon from the start, and every portal keeps it (later checkpoints are placeholders).
        var start = ProfileCopy(P(1), Root + "/Profiles/CP01_ToonStart.asset", 1);
        start.displayName = "Overgrown Ruins";
        start.look = look;
        EditorUtility.SetDirty(start);
        var setToon = LoadOrCreate(SetToon, () => Object.Instantiate(set36));
        setToon.name = "CheckpointSet_Toon";
        setToon.legsBetweenPortals = set36.legsBetweenPortals;
        setToon.defaultTiming = set36.defaultTiming;
        setToon.checkpoints = new List<CheckpointProfile> { start };
        for (int i = 2; i <= 10; i++)
        {
            string path = Root + "/Profiles/CP" + i.ToString("00") + "_ToonPlaceholder.asset";
            var ph = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(path);
            if (ph == null) { ph = ScriptableObject.CreateInstance<CheckpointProfile>(); AssetDatabase.CreateAsset(ph, path); }
            ph.index = i;
            ph.displayName = "Checkpoint " + i;
            ph.look = null;
            EditorUtility.SetDirty(ph);
            setToon.checkpoints.Add(ph);
        }
        EditorUtility.SetDirty(setToon);
    }

    // ------------------------------------------------------------------ scenes

    /// <summary>The depth-cue colours of the toon checkpoint (DepthCueController.CueLook).</summary>
    public static DepthCueController.CueLook ToonCue() => new DepthCueController.CueLook
    {
        name = "Toon (Overgrown Ruins)",
        ladder = 0.35f, litShare = 0.46f, shadeShare = 0.22f, bottomShare = 0.1f, runnerLadder = 1f, litRight = true,
        lip = 0f, lipWidth = 0.045f,
        contactColor = H("FFF0C0"), contact = 0.7f, contactSize = 0.9f, runnerRim = 0.6f,
        railColor = H("FFE6A0"), rail = 0.5f, railWidth = 0.04f,
        trailTint = Color.white, trailTintAmount = 0f, trailIntensity = 1f,
        decor = 1f, bloomCut = 0.2f,
    };

    static ToonCheckpointController WireOpenScene(AtmosphereLook look, CheckpointSet set, bool toonScene)
    {
        var dir = Object.FindFirstObjectByType<LookTransitionDirector>();
        dir.checkpoints = set;
        dir.baseLookOverride = toonScene ? look : null;
        EditorUtility.SetDirty(dir);
        var crc = Object.FindFirstObjectByType<CheckpointRunController>(FindObjectsInactive.Include);
        if (crc != null) { crc.checkpoints = set; EditorUtility.SetDirty(crc); }

        var chasm = Object.FindFirstObjectByType<ChasmFog>();
        chasm.fogLitShader = Shader.Find("Custom/ToonCheckpoint/ToonRockLit");
        EditorUtility.SetDirty(chasm);

        var trail = Object.FindFirstObjectByType<FaceHuggingTrail>(FindObjectsInactive.Include);
        if (trail != null) { trail.material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Toon_FaceTrail.mat"); EditorUtility.SetDirty(trail); }

        var cues = Object.FindFirstObjectByType<DepthCueController>(FindObjectsInactive.Include);
        if (cues != null)
        {
            var list = new List<DepthCueController.CueLook>(cues.looks);
            if (toonScene)
            {
                list.Clear();
                for (int i = 0; i < 10; i++) list.Add(ToonCue());
            }
            else
            {
                int at = list.FindIndex(c => c != null && c.name != null && c.name.StartsWith("Toon"));
                if (at >= 0) list[at] = ToonCue();
                else list.Insert(Mathf.Min(2, list.Count), ToonCue());
            }
            cues.looks = list.ToArray();
            EditorUtility.SetDirty(cues);
        }

        var ctl = Object.FindFirstObjectByType<ToonCheckpointController>(FindObjectsInactive.Include);
        if (ctl == null)
        {
            var go = new GameObject("ToonCheckpoint");
            ctl = go.AddComponent<ToonCheckpointController>();
        }
        ctl.toonLook = look;
        ctl.alwaysToon = toonScene;
        ctl.toonRocks = new ToonCheckpointController.RockLayout();   // the code defaults are the tuned layout
        ctl.useGround = false;
        ctl.cloudMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Toon_Clouds.mat");
        ctl.groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Toon_Ground.mat");
        ctl.toonRendererIndex = EnsureRenderer();
        Camera cam = Camera.main;
        var cd = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
        if (cd != null) ctl.baseRendererIndex = new SerializedObject(cd).FindProperty("m_RendererIndex").intValue;
        EditorUtility.SetDirty(ctl);
        return ctl;
    }

    public static void Wire37()
    {
        var look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        var set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(Set37);
        if (look == null || set == null) { Debug.LogError("[ToonCheckpoint] run Build Assets first."); return; }
        var scene = EditorSceneManager.OpenScene(Scene37, OpenSceneMode.Single);
        // Load again after the scene switch (it unloads unused assets).
        look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(Set37);
        WireOpenScene(look, set, false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[ToonCheckpoint] 3.7 wired: toon look at checkpoint 3 (portal 2).");
    }

    public static void MakeToonScene()
    {
        var look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        var set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetToon);
        if (look == null || set == null) { Debug.LogError("[ToonCheckpoint] run Build Assets first."); return; }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneToon) == null) AssetDatabase.CopyAsset(Scene37, SceneToon);
        var scene = EditorSceneManager.OpenScene(SceneToon, OpenSceneMode.Single);
        look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetToon);
        WireOpenScene(look, set, true);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[ToonCheckpoint] " + SceneToon + " made: toon look for the whole run.");
    }
}
#endif
