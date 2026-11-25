#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Builds the voxel checkpoint "Brick Meadow" (re-runnable): Look_Voxel (the blocky Minecraft / Super Mario meadow of the
/// reference: bright blue sky with blocky white clouds, slab bridges on red bricks, pixel grass and flowers, square-crowned
/// trees, a brick tower), its materials and post-processing, the 3.10 checkpoint set (voxel at
/// checkpoint 7 = portal 6, right after Blossom Vale; Raspberry and Jade move to 8 and 9 in 3.10 only) and both scenes:
///   Tools > Voxel Checkpoint > Build Assets         (no scene is touched)
///   Tools > Voxel Checkpoint > Wire 3.10            (opens MainScene prop camera 3.10, wires, saves it)
///   Tools > Voxel Checkpoint > Make Voxel Scene     (copies 3.10 to MainScene prop camera Voxel, wires it, saves it)
/// </summary>
public static class VoxelCheckpointBuilder
{
    public const string Root = "Assets/VoxelCheckpoint";
    public const string Scene310 = FlowerCheckpointBuilder.Scene310;
    public const string SceneVoxel = "Assets/MainScene prop camera Voxel.unity";
    public const string LookPath = "Assets/PropCameraLooks/LookTransition/Looks/Look_Voxel.asset";
    public const string LookName = "Voxel Checkpoint";
    public const string ProfilePath = Root + "/Profiles/CP07_Voxel_3_10.asset";
    public const string SetVoxel = Root + "/CheckpointSet_Voxel.asset";
    public const int Portal = 6;

    static Color H(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color c); return c; }

    /// <summary>The reference's palette (sRGB).</summary>
    public static class Palette
    {
        // Legs (the base under the slab texture): yellow slab on top, brick sides (lit / shade), dark brick under.
        public static Color Top = H("F6C82E"), LitSide = H("B85A3A"), ShadeSide = H("7A3528"), Bottom = H("4A2018");
        public static Color BlockShadow = H("4A5A9C");
        public static Color SkyTop = H("1E74DC"), SkyMid = H("4CA8F0"), SkyBottom = H("C4E8FA"), SkyGlow = H("FFFFFF");
        public static Color ChasmTop = H("CDEBF6"), ChasmBottom = H("8CC8EC"), ChasmGlow = H("FFFFFF");
        public static Color Accent = H("F6C82E");                  // slab yellow: trail flash / boost FX
        public static Color WispA = H("FFFFFF"), WispB = H("FFE07A"), Switch = H("FFF6D0");
        public static Color Sun = H("FFF2D8"), AmbientSky = H("B8DEF8"), AmbientEquator = H("D8EAC8"), AmbientGround = H("7A8A5A");
        public static Color Plank = H("C88A4A"), DoorGlow = H("F2B020"), DoorStone = H("B4482E"), Arrow = H("FFFFFF");
        public static Color Travelled = H("C8B07A");
        public static Color Danger = H("E8361E");
    }

    [MenuItem("Tools/Voxel Checkpoint/Build Assets")]
    public static void MenuBuild() => BuildAssets();

    [MenuItem("Tools/Voxel Checkpoint/Wire 3.10")]
    public static void MenuWire310() => Wire310();

    [MenuItem("Tools/Voxel Checkpoint/Make Voxel Scene")]
    public static void MenuMakeScene() => MakeVoxelScene();

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
        Folder("Assets", "VoxelCheckpoint");
        Folder(Root, "Materials");
        Folder(Root, "Settings");
        Folder(Root, "Profiles");

        string S = FlowerCheckpointBuilder.Root + "/Materials/";
        Shader ff = Shader.Find("PropLooks/FlatFaceBlock");
        Material block = MaterialAt(Root + "/Materials/M_Voxel_Leg.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Flower_Block.mat"));
        Material fade = MaterialAt(Root + "/Materials/M_Voxel_LegFade.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Flower_BlockFade.mat"));
        Material player = MaterialAt(Root + "/Materials/M_Voxel_Player.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Flower_Player.mat"));
        Material obstacle = MaterialAt(Root + "/Materials/M_Voxel_Obstacle.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Flower_Obstacle.mat"));
        Material srcSky = AssetDatabase.LoadAssetAtPath<Material>(S + "M_Flower_Sky.mat");
        Material sky = MaterialAt(Root + "/Materials/M_Voxel_Sky.mat", srcSky != null ? srcSky.shader : Shader.Find("PropLooks/RadialSky"), srcSky);

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
            m.SetFloat("_FogStart", 14f);
            m.SetFloat("_FogEnd", 60f);
            m.SetFloat("_FogMax", 0.4f);
            m.SetFloat("_ChasmFogMix", 0.2f);
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
        sky.SetFloat("_GlowStrength", 0.2f);
        sky.SetColor("_GlowColor", Palette.SkyGlow);
        sky.SetColor("_VignetteColor", H("3A80C8"));
        sky.SetFloat("_Vignette", 0.08f);

        Material backdrop = MaterialAt(Root + "/Materials/M_Voxel_Backdrop.mat", Shader.Find("VoxelCheckpoint/Backdrop"), null);
        backdrop.renderQueue = 2502;
        backdrop.SetColor("_SkyTop", Palette.SkyTop);
        backdrop.SetColor("_SkyMid", Palette.SkyMid);
        backdrop.SetColor("_SkyLow", Palette.SkyBottom);
        backdrop.SetColor("_Cloud", Color.white);
        backdrop.SetColor("_CloudShade", H("B4CCEC"));
        backdrop.SetColor("_Mountain", H("A8B4DC"));
        backdrop.SetColor("_HillGrass", H("5CB83A"));
        backdrop.SetColor("_HillDirt", H("9A6238"));
        backdrop.SetColor("_Brick", H("B8502E"));
        backdrop.SetColor("_Haze", H("D2ECF2"));
        Material blockMat = MaterialAt(Root + "/Materials/M_Voxel_Block.mat", Shader.Find("VoxelCheckpoint/Block"), null);
        MaterialAt(Root + "/Materials/M_Voxel_Spark.mat", Shader.Find("VoxelCheckpoint/Spark"), null);

        VolumeProfile vol = BuildPost();

        var look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        if (look == null) { look = ScriptableObject.CreateInstance<AtmosphereLook>(); AssetDatabase.CreateAsset(look, LookPath); }
        look.sourceScene = LookName;
        look.Capture(new AtmosphereLook.Sources { block = block, blockFade = fade, player = player, obstacle = obstacle, sky = sky });
        look.hasChasm = true;
        look.chasmTop = Palette.ChasmTop;
        look.chasmBottom = Palette.ChasmBottom;
        look.chasmGlow = Palette.ChasmGlow;
        look.chasmGlowStrength = 0.25f;
        look.chasmGlowCentre = new Vector2(0.5f, 0.6f);
        look.chasmGlowRadius = 0.65f;
        look.sunColor = Palette.Sun;
        look.sunIntensity = 1.1f;
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
        look.faceColor = H("FFF6D0");
        look.faceSwitchColor = Palette.Switch;
        look.arrowColor = Palette.Arrow;
        look.plankColor = Palette.Plank;
        look.obstacleGlowColor = H("FF5A30");
        look.obstacleGlowIntensity = 0.8f;
        look.obstacleGlowRange = 4f;
        look.skyTopColor = Palette.SkyTop;
        EditorUtility.SetDirty(look);

        BuildProfiles(look);
        RebuildSet310();
        var palette = AssetDatabase.LoadAssetAtPath<LegStagePalette>("Assets/LegStages310/LegStagePalette_3_10.asset");
        if (palette != null) { palette.FillDefaults(); EditorUtility.SetDirty(palette); }
        AssetDatabase.SaveAssets();
        Debug.Log("[VoxelCheckpoint] assets built: " + LookPath);
        return look;
    }

    static VolumeProfile BuildPost()
    {
        string path = Root + "/Settings/Voxel_Volume.asset";
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
        bloom.intensity.Override(0.2f);
        bloom.scatter.Override(0.5f);
        bloom.tint.Override(H("FFF2D8"));
        var ca = Get<ColorAdjustments>();
        ca.postExposure.Override(0.05f);
        ca.contrast.Override(12f);
        ca.saturation.Override(22f);
        ca.colorFilter.Override(Color.white);
        var vig = Get<Vignette>();
        vig.color.Override(H("163C6A"));
        vig.intensity.Override(0.18f);
        vig.smoothness.Override(0.45f);
        var grain = Get<FilmGrain>();
        grain.intensity.Override(0f);
        grain.active = false;
        EditorUtility.SetDirty(p);
        return p;
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
        var flowerProfile = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(FlowerCheckpointBuilder.ProfilePath);
        var vx = CopyProfile(flowerProfile != null ? flowerProfile : set39.Get(5), ProfilePath, 7, L);
        vx.displayName = "Brick Meadow";
        vx.look = look;
        vx.label = "";
        vx.keepObstacle = false;
        vx.takePlankColor = true;
        vx.lockedTeaseColor = Palette.Accent;
        vx.menuCamera = new MenuCameraPreset { motion = MenuShotMotion.HighOrbit, positionOffset = new Vector3(0f, 13f, -15f), angles = new Vector2(30f, 135f), fov = 42f, motionSpeed = 0.03f, motionAmplitude = 35f };
        EditorUtility.SetDirty(vx);

        // Voxel scene: voxel from the start, every later portal keeps it (the pixel scene's look-less placeholders).
        var start = CopyProfile(set39.Get(1), Root + "/Profiles/CP01_VoxelStart.asset", 1, L);
        start.displayName = "Brick Meadow";
        start.look = look;
        EditorUtility.SetDirty(start);
        var set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetVoxel);
        if (set == null) { set = Object.Instantiate(set39); AssetDatabase.CreateAsset(set, SetVoxel); }
        set.name = "CheckpointSet_Voxel";
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

    /// <summary>3.10's order: Start, Crimson, Toon, Pixel, Storybook (3.9's own profiles), Flower 6, VOXEL 7, Raspberry 8,
    /// Jade 9, placeholder 10. Also called by FlowerCheckpointBuilder so a flower rebuild keeps the voxel checkpoint.</summary>
    public static void RebuildSet310()
    {
        // Once the pencil checkpoint exists it owns the order (pencil 8, Raspberry 9, Jade 10).
        if (AssetDatabase.LoadAssetAtPath<CheckpointProfile>(PencilCheckpointBuilder.ProfilePath) != null) { PencilCheckpointBuilder.RebuildSet310(); return; }
        var vx = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(ProfilePath);
        var set310 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(FlowerCheckpointBuilder.Set310);
        var set39 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(PixelCheckpointBuilder.Set39);
        var set36 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(PixelCheckpointBuilder.Set36);
        var flower = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(FlowerCheckpointBuilder.ProfilePath);
        if (vx == null || set310 == null || set39 == null || flower == null) return;
        int L = set39.legsBetweenPortals;
        CheckpointProfile Moved(string oldPath, string newPath, int index)
        {
            if (AssetDatabase.LoadAssetAtPath<CheckpointProfile>(newPath) == null && AssetDatabase.LoadAssetAtPath<CheckpointProfile>(oldPath) != null)
                AssetDatabase.MoveAsset(oldPath, newPath);   // keeps the GUID
            var p = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(newPath);
            if (p == null) return null;
            p.index = index;
            p.legIndex = (index - 1) * L;
            EditorUtility.SetDirty(p);
            return p;
        }
        string F = FlowerCheckpointBuilder.Root + "/Profiles/";
        var rasp = Moved(F + "CP07_Raspberry_3_10.asset", F + "CP08_Raspberry_3_10.asset", 8);
        var jade = Moved(F + "CP08_JadeMoonGarden_3_10.asset", F + "CP09_JadeMoonGarden_3_10.asset", 9);
        set310.checkpoints = new List<CheckpointProfile> { set39.Get(1), set39.Get(2), set39.Get(3), set39.Get(4), set39.Get(5), flower, vx };
        if (rasp != null) set310.checkpoints.Add(rasp);
        if (jade != null) set310.checkpoints.Add(jade);
        if (set36 != null && set36.Get(10) != null) set310.checkpoints.Add(set36.Get(10));
        EditorUtility.SetDirty(set310);
    }

    // ------------------------------------------------------------------ scenes

    public static DepthCueController.CueLook VoxelCue() => new DepthCueController.CueLook
    {
        name = "Voxel (Brick Meadow)",
        ladder = 0f, litShare = 0.6f, shadeShare = 0.35f, bottomShare = 0.15f, runnerLadder = 1f, litRight = false,
        lip = 0f, lipWidth = 0.045f,
        contactColor = H("FFF6D0"), contact = 0.4f, contactSize = 0.9f, runnerRim = 0.2f,
        railColor = H("FFFFFF"), rail = 0.35f, railWidth = 0.05f,
        trailTint = Color.white, trailTintAmount = 0f, trailIntensity = 1f,
        decor = 0.6f, bloomCut = 0.4f,
    };

    static void WireOpenScene(AtmosphereLook look, CheckpointSet set, bool voxelScene)
    {
        var dir = Object.FindFirstObjectByType<LookTransitionDirector>();
        dir.checkpoints = set;
        dir.baseLookOverride = voxelScene ? look : null;
        dir.blockWipeShader = Shader.Find("PixelCheckpoint/FlatFaceBlockPixel");
        EditorUtility.SetDirty(dir);
        var crc = Object.FindFirstObjectByType<CheckpointRunController>(FindObjectsInactive.Include);
        if (crc != null) { crc.checkpoints = set; EditorUtility.SetDirty(crc); }

        var cues = Object.FindFirstObjectByType<DepthCueController>(FindObjectsInactive.Include);
        if (cues != null)
        {
            var list = new List<DepthCueController.CueLook>(cues.looks);
            if (voxelScene)
            {
                list.Clear();
                for (int i = 0; i < 10; i++) list.Add(VoxelCue());
            }
            else
            {
                int at = list.FindIndex(c => c != null && c.name != null && c.name.StartsWith("Voxel"));
                if (at >= 0) list[at] = VoxelCue();
                else list.Insert(Mathf.Min(Portal, list.Count), VoxelCue());
            }
            cues.looks = list.ToArray();
            EditorUtility.SetDirty(cues);
        }

        if (voxelScene)
        {
            var stages = Object.FindFirstObjectByType<LegStageColors>(FindObjectsInactive.Include);
            if (stages != null) { stages.enabled = false; EditorUtility.SetDirty(stages); }
            // The flower controller stays (its look never comes); nothing else to switch off.
        }

        var old = Object.FindFirstObjectByType<VoxelCheckpointController>(FindObjectsInactive.Include);
        GameObject host = old != null ? old.gameObject : new GameObject("VoxelCheckpoint");
        if (old != null) Object.DestroyImmediate(old);
        var ctl = host.AddComponent<VoxelCheckpointController>();
        ctl.voxelLook = look;
        ctl.alwaysVoxel = voxelScene;
        ctl.backdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Voxel_Backdrop.mat");
        ctl.blockMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Voxel_Block.mat");
        ctl.sparkMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Voxel_Spark.mat");
        EditorUtility.SetDirty(ctl);
    }

    public static void Wire310()
    {
        if (AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath) == null) { Debug.LogError("[VoxelCheckpoint] run Build Assets first."); return; }
        var scene = EditorSceneManager.OpenScene(Scene310, OpenSceneMode.Single);
        WireOpenScene(AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath), AssetDatabase.LoadAssetAtPath<CheckpointSet>(FlowerCheckpointBuilder.Set310), false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[VoxelCheckpoint] 3.10 wired: voxel look at checkpoint 7 (portal 6).");
    }

    public static void MakeVoxelScene()
    {
        if (AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath) == null) { Debug.LogError("[VoxelCheckpoint] run Build Assets first."); return; }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneVoxel) == null) AssetDatabase.CopyAsset(Scene310, SceneVoxel);
        var scene = EditorSceneManager.OpenScene(SceneVoxel, OpenSceneMode.Single);
        WireOpenScene(AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath), AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetVoxel), true);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[VoxelCheckpoint] " + SceneVoxel + " made: voxel look for the whole run.");
    }
}
#endif
