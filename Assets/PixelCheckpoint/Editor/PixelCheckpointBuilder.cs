#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Builds the pixel checkpoint "Jellyfish Grotto" (re-runnable): Look_Pixel (the pixel-art underwater cave from the
/// reference: indigo cave, glowing blue moss with drips, coral mushrooms, jellyfish, blue fireflies),
/// its materials and post-processing, the pixel renderer (PixelCP_Renderer = ToonCP_Renderer + the pixel-art pass, an extra
/// renderer index on the pipeline assets), the 3.9 checkpoint set (pixel at checkpoint 4 = portal 3, right after the toon
/// checkpoint; Raspberry and Jade move to 5 and 6) and both scenes:
///   Tools > Pixel Checkpoint > Build Assets         (no scene is touched)
///   Tools > Pixel Checkpoint > Wire 3.9             (opens MainScene prop camera 3.9, wires, puts the 3.7 runner back, saves it)
///   Tools > Pixel Checkpoint > Make Pixel Scene     (copies 3.9 to MainScene prop camera Pixel, wires it, saves it)
/// 3.7, the Toon scene and their assets are never changed: the block / rock / trail shaders here are 3.9-only copies.
/// </summary>
public static class PixelCheckpointBuilder
{
    public const string Root = "Assets/PixelCheckpoint";
    public const string Scene39 = "Assets/MainScene prop camera 3.9.unity";
    public const string ScenePixel = "Assets/MainScene prop camera Pixel.unity";
    public const string LookPath = "Assets/PropCameraLooks/LookTransition/Looks/Look_Pixel.asset";
    public const string LookName = "Pixel Checkpoint";
    public const string Set36 = "Assets/Checkpoints/CheckpointSet_3_6.asset";
    public const string Set37 = "Assets/ToonCheckpoint/CheckpointSet_3_7.asset";
    public const string Set39 = Root + "/CheckpointSet_3_9.asset";
    public const string SetPixel = Root + "/CheckpointSet_Pixel.asset";
    const string RendererSource = "Assets/ToonCheckpoint/Settings/ToonCP_Renderer.asset";
    public const string RendererPath = Root + "/Settings/PixelCP_Renderer.asset";
    const string PostFeatureName = "PixelArtPass";

    static Color H(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color c); return c; }

    /// <summary>The measured reference palette (sRGB). Reference samples in the comments.</summary>
    public static class Palette
    {
        // Legs: glowing jade moss on top (blue read too close to the cave), coral stone sides - the reference's
        // mushroom coral (#E06981 / #DD8C64), the opposite of the blue cave so the track always stands out.
        public static Color Top = H("3FD08A"), LitSide = H("E58A68"), ShadeSide = H("B04E5E"), Bottom = H("5E2448");
        public static Color BlockShadow = H("6A5AB0");
        // Cave: ceiling #1A1B7C / #0F015F, the open water-cave #4367D9..#4D8FEA, lower walls #2C31A1, floor #10035F.
        public static Color SkyTop = H("1A1B7C"), SkyMid = H("3B47CC"), SkyBottom = H("1B1B7D"), SkyGlow = H("4B92EA");
        public static Color ChasmTop = H("1B1F8A"), ChasmBottom = H("2C31A1"), ChasmGlow = H("4676ED");
        public static Color Accent = H("7DF0F9");                 // the trail / boost FX: glowing moss light
        public static Color WispA = H("50C8EF"), WispB = H("B4FCFF"), Switch = H("E8FFFF");
        public static Color Sun = H("B8C8FF"), AmbientSky = H("6A7CF0"), AmbientEquator = H("3A47B0"), AmbientGround = H("1A1240");
        public static Color Plank = H("C05A6A"), DoorGlow = H("7DF0F9"), DoorStone = H("E58A68"), Arrow = H("FFE08A");
        public static Color Travelled = H("7A78C0");
        public static Color Danger = H("E0304E");
    }

    [MenuItem("Tools/Pixel Checkpoint/Build Assets")]
    public static void MenuBuild() => BuildAssets();

    [MenuItem("Tools/Pixel Checkpoint/Wire 3.9")]
    public static void MenuWire39() => Wire39();

    [MenuItem("Tools/Pixel Checkpoint/Make Pixel Scene")]
    public static void MenuMakePixel() => MakePixelScene();

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
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ assets

    public static AtmosphereLook BuildAssets()
    {
        Folder("Assets", "PixelCheckpoint");
        Folder(Root, "Materials");
        Folder(Root, "Settings");
        Folder(Root, "Profiles");

        // The 3.7 look materials (the same ones the toon look started from): the pixel block starts as a copy of them.
        Material srcBlock = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("2cfcdc4db1ad76f46a6c8bde2a989a46"));
        Material srcFade = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("3f35963beac0de64b8ccc39075527ea2"));
        Material srcPlayer = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("d9d2bca30491764498ad26a765044ca5"));
        Material srcObstacle = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("38c4b0d08a173f54e8db9b5e976bf037"));
        Material srcSky = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("a1b8d2d13e6f68145b14a34be2073e2d"));

        Shader flat = Shader.Find("PropLooks/FlatFaceBlock");
        Shader radial = srcSky != null ? srcSky.shader : Shader.Find("PropLooks/RadialSky");

        Material block = MaterialAt(Root + "/Materials/M_Pixel_Block.mat", flat, srcBlock);
        Material fade = MaterialAt(Root + "/Materials/M_Pixel_BlockFade.mat", flat, srcFade);
        Material player = MaterialAt(Root + "/Materials/M_Pixel_Player.mat", flat, srcPlayer);
        Material obstacle = MaterialAt(Root + "/Materials/M_Pixel_Obstacle.mat", flat, srcObstacle);
        Material sky = MaterialAt(Root + "/Materials/M_Pixel_Sky.mat", radial, srcSky);

        foreach (Material m in new[] { block, fade })
        {
            m.SetColor("_TopColor", Palette.Top);
            m.SetColor("_LeftColor", Palette.ShadeSide);
            m.SetColor("_RightColor", Palette.LitSide);
            m.SetColor("_BottomColor", Palette.Bottom);
            m.SetFloat("_SideSoftness", 0.0f);
            m.SetFloat("_TopSharpness", 12f);
            m.SetColor("_ShadowColor", Palette.BlockShadow);
            m.SetFloat("_ShadowStrength", 0.45f);
            m.SetFloat("_EdgeStrength", 0f);       // the moss rim / bricks replace the rim
            m.SetFloat("_SeamStrength", 0f);
            m.SetFloat("_TileTop", 0f);
            m.SetFloat("_TileSide", 0f);
            m.SetFloat("_GrainStrength", 0f);      // the texel jitter replaces it
            m.SetColor("_FogTopColor", Palette.ChasmTop);
            m.SetColor("_FogBottomColor", Palette.ChasmBottom);
            m.SetFloat("_FogStart", 8f);
            m.SetFloat("_FogEnd", 46f);
            m.SetFloat("_FogMax", 0.6f);
            m.SetFloat("_FogColorFromChasm", 1f);
            m.SetFloat("_ChasmFogMix", 0.3f);
            m.SetColor("_GlowTint", H("7DF0F9"));
            m.SetFloat("_GlowGain", 0.45f);
            m.SetColor("_EmissiveColor", Color.black);
            m.SetFloat("_PointLightStrength", 0.6f);
        }
        player.CopyPropertiesFromMaterial(block);
        obstacle.CopyPropertiesFromMaterial(block);
        // Danger = red (the user's colour rule), bright against the indigo cave.
        obstacle.SetColor("_TopColor", Palette.Danger); obstacle.SetColor("_LeftColor", H("8A1834"));
        obstacle.SetColor("_RightColor", H("B82242")); obstacle.SetColor("_BottomColor", H("4A0C24"));

        sky.SetColor("_TopColor", Palette.SkyTop);
        sky.SetColor("_BottomColor", Palette.SkyBottom);
        sky.SetColor("_MidColor", Palette.SkyMid);
        sky.SetVector("_GlowCenter", new Vector4(0.5f, 0.6f, 0f, 0f));
        sky.SetFloat("_GlowRadius", 0.7f);
        sky.SetFloat("_GlowStrength", 0.5f);
        sky.SetFloat("_GlowFalloff", 1.4f);
        sky.SetColor("_GlowColor", Palette.SkyGlow);
        sky.SetFloat("_HazeHeight", 0.3f);
        sky.SetFloat("_HazeStrength", 0.25f);
        sky.SetColor("_VignetteColor", H("0A0448"));
        sky.SetFloat("_Vignette", 0.3f);
        sky.SetFloat("_Rings", 0f);
        sky.SetFloat("_Grain", 0f);

        // Pixel-only materials.
        Material backdrop = MaterialAt(Root + "/Materials/M_Pixel_Backdrop.mat", Shader.Find("PixelCheckpoint/Backdrop"), null);
        Material mote = MaterialAt(Root + "/Materials/M_Pixel_Mote.mat", Shader.Find("PixelCheckpoint/Mote"), null);
        Material post = MaterialAt(Root + "/Materials/M_Pixel_Post.mat", Shader.Find("Hidden/PixelCheckpoint/PixelPost"), null);
        Material trailSrc = AssetDatabase.LoadAssetAtPath<Material>("Assets/ToonCheckpoint/Materials/M_Toon_FaceTrail.mat");
        MaterialAt(Root + "/Materials/M_Pixel_FaceTrail.mat", Shader.Find("PixelCheckpoint/TrailFX Face Ribbon Pixel"), trailSrc);
        backdrop.renderQueue = 2502;
        mote.renderQueue = 3000;

        VolumeProfile vol = BuildPost();

        var look = LoadOrCreate(LookPath, () => ScriptableObject.CreateInstance<AtmosphereLook>());
        look.sourceScene = LookName;
        look.Capture(new AtmosphereLook.Sources { block = block, blockFade = fade, player = player, obstacle = obstacle, sky = sky });
        look.hasChasm = true;
        look.chasmTop = Palette.ChasmTop;
        look.chasmBottom = Palette.ChasmBottom;
        look.chasmGlow = Palette.ChasmGlow;
        look.chasmGlowStrength = 0.5f;
        look.chasmGlowCentre = new Vector2(0.5f, 0.58f);
        look.chasmGlowRadius = 0.62f;
        look.sunColor = Palette.Sun;
        look.sunIntensity = 0.9f;
        look.sunShadowStrength = 0.7f;
        look.sunEuler = new Vector3(55f, -35f, 0f);
        look.ambientMode = (int)AmbientMode.Trilight;
        look.ambientSky = Palette.AmbientSky;
        look.ambientEquator = Palette.AmbientEquator;
        look.ambientGround = Palette.AmbientGround;
        look.cameraBackground = H("10035F");
        look.volumeProfile = vol;
        look.aheadKeys = new[] { Color.white, new Color(0.98f, 0.98f, 1f), new Color(0.94f, 0.94f, 0.99f), new Color(0.88f, 0.88f, 0.97f) };
        look.travelledColor = Palette.Travelled;
        look.doorGlowColor = Palette.DoorGlow;
        look.doorStoneColor = Palette.DoorStone;
        look.sceneAccent = Palette.Accent;
        look.wispColorA = Palette.WispA;
        look.wispColorB = Palette.WispB;
        look.trailSwitchColor = Palette.Switch;
        look.faceColor = H("B4FCFF");
        look.faceSwitchColor = Palette.Switch;
        look.arrowColor = Palette.Arrow;
        look.plankColor = Palette.Plank;
        look.obstacleGlowColor = H("FF4060");
        look.obstacleGlowIntensity = 1f;
        look.obstacleGlowRange = 4f;
        look.skyTopColor = Palette.SkyTop;
        EditorUtility.SetDirty(look);

        EnsureRenderer();
        BuildSets(look);
        AssetDatabase.SaveAssets();
        Debug.Log("[PixelCheckpoint] assets built: " + LookPath);
        return look;
    }

    static VolumeProfile BuildPost()
    {
        string path = Root + "/Settings/Pixel_Volume.asset";
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
        bloom.threshold.Override(0.92f);
        bloom.intensity.Override(0.2f);
        bloom.scatter.Override(0.6f);
        bloom.tint.Override(H("A8E8FF"));
        var ca = Get<ColorAdjustments>();
        ca.postExposure.Override(0f);
        ca.contrast.Override(12f);
        ca.saturation.Override(18f);
        ca.colorFilter.Override(Color.white);
        var vig = Get<Vignette>();
        vig.color.Override(H("0A0448"));
        vig.intensity.Override(0.28f);
        vig.smoothness.Override(0.45f);
        var grain = Get<FilmGrain>();
        grain.intensity.Override(0f);
        grain.active = false;
        EditorUtility.SetDirty(p);
        return p;
    }

    /// <summary>PixelCP_Renderer = ToonCP_Renderer (SSAO + toon ink, which only draws where the toon look is) + the pixel-art
    /// pass after post-processing, registered on every pipeline asset. Returns its index.</summary>
    public static int EnsureRenderer()
    {
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rd == null)
        {
            AssetDatabase.CopyAsset(RendererSource, RendererPath);
            AssetDatabase.ImportAsset(RendererPath);
            rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        }
        FullScreenPassRendererFeature pass = null;
        foreach (var f in rd.rendererFeatures) if (f is FullScreenPassRendererFeature fs && fs.name == PostFeatureName) pass = fs;
        if (pass == null)
        {
            pass = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            pass.name = PostFeatureName;
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
        }
        pass.passMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Pixel_Post.mat");
        pass.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
        pass.requirements = ScriptableRenderPassInput.None;
        pass.fetchColorBuffer = true;
        pass.passIndex = 0;
        EditorUtility.SetDirty(pass);
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
            else if (index != at) Debug.LogWarning("[PixelCheckpoint] renderer at index " + at + " on " + asset.name + " but " + index + " elsewhere.");
        }
        AssetDatabase.SaveAssets();
        return index;
    }

    static CheckpointProfile ProfileCopy(CheckpointProfile src, string dstPath, int index, int legs)
    {
        var p = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(dstPath);
        if (p == null)
        {
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(src), dstPath);
            p = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(dstPath);
        }
        p.index = index;
        p.legIndex = (index - 1) * legs;
        EditorUtility.SetDirty(p);
        return p;
    }

    /// <summary>The 3.9 order: Start, Crimson, Toon, Pixel, then the storybook checkpoint at 5 once it is built
    /// (StorybookCheckpointBuilder), Raspberry, Jade, then 3.6 placeholders up to 10.</summary>
    public static void RebuildSet39()
    {
        var set36 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(Set36);
        var set37 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(Set37);
        int L = set37.legsBetweenPortals;
        var pixel = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(Root + "/Profiles/CP04_Pixel_3_9.asset");
        var story = AssetDatabase.LoadAssetAtPath<CheckpointProfile>("Assets/StorybookCheckpoint/Profiles/CP05_Storybook_3_9.asset");
        int next = story != null ? 6 : 5;
        // Raspberry / Jade: renamed (same GUID) when their index moves.
        CheckpointProfile Moved(CheckpointProfile src, string name, int index)
        {
            string want = Root + "/Profiles/CP" + index.ToString("00") + "_" + name + "_3_9.asset";
            foreach (int i in new[] { 5, 6, 7 })
            {
                string old = Root + "/Profiles/CP" + i.ToString("00") + "_" + name + "_3_9.asset";
                if (old != want && AssetDatabase.LoadAssetAtPath<CheckpointProfile>(old) != null && AssetDatabase.LoadAssetAtPath<CheckpointProfile>(want) == null)
                    AssetDatabase.RenameAsset(old, System.IO.Path.GetFileNameWithoutExtension(want));
            }
            return ProfileCopy(src, want, index, L);
        }
        var rasp = Moved(set37.Get(4), "Raspberry", next);
        var jade = Moved(set37.Get(5), "JadeMoonGarden", next + 1);

        var set39 = LoadOrCreate(Set39, () => Object.Instantiate(set37));
        set39.name = "CheckpointSet_3_9";
        set39.legsBetweenPortals = L;
        set39.defaultTiming = set37.defaultTiming;
        set39.checkpoints = new List<CheckpointProfile> { set37.Get(1), set37.Get(2), set37.Get(3), pixel };
        if (story != null) set39.checkpoints.Add(story);
        set39.checkpoints.Add(rasp);
        set39.checkpoints.Add(jade);
        for (int i = next + 2; i <= 10; i++) if (set36.Get(i) != null) set39.checkpoints.Add(set36.Get(i));
        EditorUtility.SetDirty(set39);
    }

    static void BuildSets(AtmosphereLook look)
    {
        var set36 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(Set36);
        var set37 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(Set37);
        int L = set37.legsBetweenPortals;

        // 3.9: Start, Crimson, Toon, PIXEL, Raspberry, Jade, placeholders 7-10 (the shared 3.6 profiles that keep their index).
        var pixel = ProfileCopy(set37.Get(3), Root + "/Profiles/CP04_Pixel_3_9.asset", 4, L);
        pixel.displayName = "Jellyfish Grotto";
        pixel.look = look;
        pixel.label = "";
        pixel.keepObstacle = false;
        pixel.takePlankColor = true;
        pixel.lockedTeaseColor = Palette.Accent;
        pixel.menuCamera = new MenuCameraPreset { motion = MenuShotMotion.TopDownPushIn, positionOffset = new Vector3(0f, 21f, -5f), angles = new Vector2(62f, 210f), fov = 40f, motionSpeed = 0.03f, motionAmplitude = 18f };
        EditorUtility.SetDirty(pixel);
        RebuildSet39();

        // Pixel scene: pixel from the start, and every portal keeps it (later checkpoints are placeholders).
        var start = ProfileCopy(set37.Get(1), Root + "/Profiles/CP01_PixelStart.asset", 1, L);
        start.displayName = "Jellyfish Grotto";
        start.look = look;
        EditorUtility.SetDirty(start);
        var setPixel = LoadOrCreate(SetPixel, () => Object.Instantiate(set37));
        setPixel.name = "CheckpointSet_Pixel";
        setPixel.legsBetweenPortals = L;
        setPixel.defaultTiming = set37.defaultTiming;
        setPixel.checkpoints = new List<CheckpointProfile> { start };
        for (int i = 2; i <= 10; i++)
        {
            string path = Root + "/Profiles/CP" + i.ToString("00") + "_PixelPlaceholder.asset";
            var ph = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(path);
            if (ph == null) { ph = ScriptableObject.CreateInstance<CheckpointProfile>(); AssetDatabase.CreateAsset(ph, path); }
            ph.index = i;
            ph.displayName = "Checkpoint " + i;
            ph.look = null;
            ph.legIndex = (i - 1) * L;
            EditorUtility.SetDirty(ph);
            setPixel.checkpoints.Add(ph);
        }
        EditorUtility.SetDirty(setPixel);
    }

    // ------------------------------------------------------------------ scenes

    /// <summary>The depth-cue colours of the pixel checkpoint (DepthCueController.CueLook): no face ladder (the coral
    /// sides and the moss top already differ in hue), cyan contact and rail.</summary>
    public static DepthCueController.CueLook PixelCue() => new DepthCueController.CueLook
    {
        name = "Pixel (Jellyfish Grotto)",
        ladder = 0f, litShare = 0.6f, shadeShare = 0.35f, bottomShare = 0.15f, runnerLadder = 1f, litRight = true,
        lip = 0f, lipWidth = 0.045f,
        contactColor = H("B4FCFF"), contact = 0.5f, contactSize = 0.9f, runnerRim = 0.25f,
        railColor = H("B4FCFF"), rail = 0.55f, railWidth = 0.06f,
        trailTint = Color.white, trailTintAmount = 0f, trailIntensity = 1f,
        decor = 0.6f, bloomCut = 0.4f,
    };

    /// <summary>The 3.7 runner back (the yellow eye block + cape): any character runner the 3.8 / 3.9 work added is removed.</summary>
    static void RestoreTotemRunner()
    {
        var runner = Object.FindFirstObjectByType<CubeRailRunner>(FindObjectsInactive.Include);
        if (runner == null) return;
        foreach (var old in runner.GetComponentsInChildren<PoppyRunner>(true)) Object.DestroyImmediate(old.gameObject);
        foreach (var old in runner.GetComponentsInChildren<LumiRunner>(true)) Object.DestroyImmediate(old.gameObject);
        foreach (var t in runner.GetComponentsInChildren<PlayerTotem>(true))
        {
            t.gameObject.SetActive(true);
            t.mirrorCubeVisibility = true;   // shown again after a checkpoint start's door (3.7 leaves it hidden there)
            EditorUtility.SetDirty(t);
        }
    }

    static PixelCheckpointController WireOpenScene(AtmosphereLook look, CheckpointSet set, bool pixelScene)
    {
        var dir = Object.FindFirstObjectByType<LookTransitionDirector>();
        dir.checkpoints = set;
        dir.baseLookOverride = pixelScene ? look : null;
        dir.blockWipeShader = Shader.Find("PixelCheckpoint/FlatFaceBlockPixel");
        EditorUtility.SetDirty(dir);
        var crc = Object.FindFirstObjectByType<CheckpointRunController>(FindObjectsInactive.Include);
        if (crc != null) { crc.checkpoints = set; EditorUtility.SetDirty(crc); }

        var chasm = Object.FindFirstObjectByType<ChasmFog>();
        chasm.fogLitShader = Shader.Find("Custom/PixelCheckpoint/PixelRockLit");
        EditorUtility.SetDirty(chasm);

        var trail = Object.FindFirstObjectByType<FaceHuggingTrail>(FindObjectsInactive.Include);
        if (trail != null) { trail.material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Pixel_FaceTrail.mat"); EditorUtility.SetDirty(trail); }

        var cues = Object.FindFirstObjectByType<DepthCueController>(FindObjectsInactive.Include);
        if (cues != null)
        {
            var list = new List<DepthCueController.CueLook>(cues.looks);
            if (pixelScene)
            {
                list.Clear();
                for (int i = 0; i < 10; i++) list.Add(PixelCue());
            }
            else
            {
                int at = list.FindIndex(c => c != null && c.name != null && c.name.StartsWith("Pixel"));
                if (at >= 0) list[at] = PixelCue();
                else list.Insert(Mathf.Min(3, list.Count), PixelCue());
            }
            cues.looks = list.ToArray();
            EditorUtility.SetDirty(cues);
        }

        RestoreTotemRunner();

        // Rebuilt every time: the code defaults are the tuned values (re-run Wire after changing them).
        var old = Object.FindFirstObjectByType<PixelCheckpointController>(FindObjectsInactive.Include);
        GameObject host = old != null ? old.gameObject : new GameObject("PixelCheckpoint");
        if (old != null) Object.DestroyImmediate(old);
        var ctl = host.AddComponent<PixelCheckpointController>();
        ctl.pixelLook = look;
        ctl.alwaysPixel = pixelScene;
        ctl.pixelRocks = new PixelCheckpointController.RockLayout();   // the code defaults are the tuned layout
        ctl.backdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Pixel_Backdrop.mat");
        ctl.moteMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Pixel_Mote.mat");
        ctl.palette = PixelCheckpointController.DefaultPalette();
        ctl.pixelRendererIndex = EnsureRenderer();
        Camera cam = Camera.main;
        var cd = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
        if (cd != null) ctl.baseRendererIndex = new SerializedObject(cd).FindProperty("m_RendererIndex").intValue;
        EditorUtility.SetDirty(ctl);
        return ctl;
    }

    public static void Wire39()
    {
        var look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        var set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(Set39);
        if (look == null || set == null) { Debug.LogError("[PixelCheckpoint] run Build Assets first."); return; }
        var scene = EditorSceneManager.OpenScene(Scene39, OpenSceneMode.Single);
        look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(Set39);
        WireOpenScene(look, set, false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PixelCheckpoint] 3.9 wired: pixel look at checkpoint 4 (portal 3), 3.7 runner back.");
    }

    public static void MakePixelScene()
    {
        var look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        var set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetPixel);
        if (look == null || set == null) { Debug.LogError("[PixelCheckpoint] run Build Assets first."); return; }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePixel) == null) AssetDatabase.CopyAsset(Scene39, ScenePixel);
        var scene = EditorSceneManager.OpenScene(ScenePixel, OpenSceneMode.Single);
        look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetPixel);
        WireOpenScene(look, set, true);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PixelCheckpoint] " + ScenePixel + " made: pixel look for the whole run.");
    }
}
#endif
