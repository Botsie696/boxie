#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Builds the pencil checkpoint "Graphite Sketchbook" (re-runnable): Look_Pencil (black-and-white graphite on rough paper,
/// from the user's references: a scribbly sketch robot, a graphite spaceship study, an ink-and-grey medieval street and a
/// hatched dungeon), its materials, post-processing and PencilCP_Renderer (sketch lines + paper passes), the 3.10
/// checkpoint set (pencil at checkpoint 8 = portal 7, right after Brick Meadow; Raspberry and Jade move to 9 and 10 in 3.10
/// only) and both scenes:
///   Tools > Pencil Checkpoint > Build Assets         (no scene is touched)
///   Tools > Pencil Checkpoint > Wire 3.10            (opens MainScene prop camera 3.10, wires, saves it)
///   Tools > Pencil Checkpoint > Make Pencil Scene    (copies 3.10 to MainScene prop camera Pencil, wires it, saves it)
/// </summary>
public static class PencilCheckpointBuilder
{
    public const string Root = "Assets/PencilCheckpoint";
    public const string Scene310 = FlowerCheckpointBuilder.Scene310;
    public const string ScenePencil = "Assets/MainScene prop camera Pencil.unity";
    public const string LookPath = "Assets/PropCameraLooks/LookTransition/Looks/Look_Pencil.asset";
    public const string LookName = "Pencil Checkpoint";
    public const string ProfilePath = Root + "/Profiles/CP08_Pencil_3_10.asset";
    public const string SetPencil = Root + "/CheckpointSet_Pencil.asset";
    public const string RendererPath = Root + "/Settings/PencilCP_Renderer.asset";
    public const int Portal = 7;
    const string LinesFeature = "PencilLines", PaperFeature = "PencilPaper";

    static Color H(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color c); return c; }

    /// <summary>Paper and graphite (sRGB). Everything is grey: the paper pass prints the page in black and white anyway.</summary>
    public static class Palette
    {
        public static Color Paper = H("F1EEE6"), PaperShade = H("DAD6CD"), PaperDeep = H("BDB9B0");
        public static Color Graphite = H("26262A"), Mid = H("6E6E70"), Light = H("A8A8A8");
        public static Color Top = H("F1EEE6"), LitSide = H("E2DED6"), ShadeSide = H("BFBBB3"), Bottom = H("8E8B85");
    }

    [MenuItem("Tools/Pencil Checkpoint/Build Assets")]
    public static void MenuBuild() => BuildAssets();

    [MenuItem("Tools/Pencil Checkpoint/Wire 3.10")]
    public static void MenuWire310() => Wire310();

    [MenuItem("Tools/Pencil Checkpoint/Make Pencil Scene")]
    public static void MenuMakeScene() => MakePencilScene();

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

    // The runner's front trail, graphite flecks and speed streaks (PencilRunnerFX).
    public static void BuildRunnerFxMaterials()
    {
        Shader fx = Shader.Find("PencilCheckpoint/RunnerFX");
        string[] names = { "M_Pencil_FrontTrail.mat", "M_Pencil_Fleck.mat", "M_Pencil_Streak.mat" };
        float[] strength = { 1.25f, 1.2f, 1f };
        for (int i = 0; i < names.Length; i++)
        {
            Material m = MaterialAt(Root + "/Materials/" + names[i], fx, null);
            m.SetFloat("_Mode", i);
            m.SetFloat("_Strength", strength[i]);
            m.SetFloat("_LineWidth", 0.022f);
            m.renderQueue = 3000 + i;
        }
    }

    // ------------------------------------------------------------------ assets

    public static AtmosphereLook BuildAssets()
    {
        Folder("Assets", "PencilCheckpoint");
        Folder(Root, "Materials");
        Folder(Root, "Settings");
        Folder(Root, "Profiles");

        string S = VoxelCheckpointBuilder.Root + "/Materials/";
        Shader ff = Shader.Find("PropLooks/FlatFaceBlock");
        Material block = MaterialAt(Root + "/Materials/M_Pencil_Leg.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Voxel_Leg.mat"));
        Material fade = MaterialAt(Root + "/Materials/M_Pencil_LegFade.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Voxel_LegFade.mat"));
        Material player = MaterialAt(Root + "/Materials/M_Pencil_Player.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Voxel_Player.mat"));
        Material obstacle = MaterialAt(Root + "/Materials/M_Pencil_Obstacle.mat", ff, AssetDatabase.LoadAssetAtPath<Material>(S + "M_Voxel_Obstacle.mat"));
        Material srcSky = AssetDatabase.LoadAssetAtPath<Material>(S + "M_Voxel_Sky.mat");
        Material sky = MaterialAt(Root + "/Materials/M_Pencil_Sky.mat", srcSky != null ? srcSky.shader : Shader.Find("PropLooks/RadialSky"), srcSky);

        foreach (Material m in new[] { block, fade, player })
        {
            m.SetColor("_TopColor", Palette.Top);
            m.SetColor("_LeftColor", Palette.ShadeSide);
            m.SetColor("_RightColor", Palette.LitSide);
            m.SetColor("_BottomColor", Palette.Bottom);
            m.SetColor("_ShadowColor", Palette.PaperDeep);
            m.SetFloat("_ShadowStrength", 0.25f);
            m.SetColor("_FogTopColor", Palette.Paper);
            m.SetColor("_FogBottomColor", Palette.PaperShade);
            m.SetFloat("_FogStart", 16f);
            m.SetFloat("_FogEnd", 70f);
            m.SetFloat("_FogMax", 0.35f);
            m.SetFloat("_ChasmFogMix", 0.2f);
            m.SetColor("_GlowTint", Palette.Mid);
            m.SetFloat("_GlowGain", 0f);
        }
        obstacle.CopyPropertiesFromMaterial(block);
        obstacle.SetColor("_TopColor", H("3A3A3C")); obstacle.SetColor("_LeftColor", H("1E1E20"));
        obstacle.SetColor("_RightColor", H("2C2C2E")); obstacle.SetColor("_BottomColor", H("141416"));

        sky.SetColor("_TopColor", Palette.PaperShade);
        sky.SetColor("_BottomColor", Palette.Paper);
        sky.SetColor("_MidColor", Palette.Paper);
        sky.SetVector("_GlowCenter", new Vector4(0.5f, 0.55f, 0f, 0f));
        sky.SetFloat("_GlowRadius", 0.7f);
        sky.SetFloat("_GlowStrength", 0.05f);
        sky.SetColor("_GlowColor", Color.white);
        sky.SetColor("_VignetteColor", Palette.PaperDeep);
        sky.SetFloat("_Vignette", 0.12f);

        Material backdrop = MaterialAt(Root + "/Materials/M_Pencil_Backdrop.mat", Shader.Find("PencilCheckpoint/Backdrop"), null);
        backdrop.renderQueue = 2502;
        Material prop = MaterialAt(Root + "/Materials/M_Pencil_Prop.mat", Shader.Find("PencilCheckpoint/Prop"), null);
        prop.SetFloat("_Scale", 1f);
        Shader rs = Shader.Find("PencilCheckpoint/Runner");
        Material body = MaterialAt(Root + "/Materials/M_Pencil_RunnerBody.mat", rs, null);
        body.SetFloat("_Tone", 0.05f); body.SetFloat("_Solid", 0f); body.SetFloat("_Spacing", 0.07f);
        Material eyeW = MaterialAt(Root + "/Materials/M_Pencil_RunnerEyeWhite.mat", rs, null);
        eyeW.SetFloat("_Tone", 0f); eyeW.SetFloat("_Solid", 0f); eyeW.SetFloat("_Spacing", 0.05f);
        Material pupil = MaterialAt(Root + "/Materials/M_Pencil_RunnerPupil.mat", rs, null);
        pupil.SetFloat("_Tone", 1f); pupil.SetFloat("_Solid", 1f); pupil.SetFloat("_Spacing", 0.03f);
        Material cape = MaterialAt(Root + "/Materials/M_Pencil_RunnerCape.mat", rs, null);
        cape.SetFloat("_Tone", 0.4f); cape.SetFloat("_Solid", 0f); cape.SetFloat("_Spacing", 0.06f);
        BuildRunnerFxMaterials();
        MaterialAt(Root + "/Materials/M_Pencil_Lines.mat", Shader.Find("Hidden/PencilCheckpoint/Lines"), null);
        MaterialAt(Root + "/Materials/M_Pencil_Paper.mat", Shader.Find("Hidden/PencilCheckpoint/Paper"), null);

        VolumeProfile vol = BuildPost();

        var look = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath);
        if (look == null) { look = ScriptableObject.CreateInstance<AtmosphereLook>(); AssetDatabase.CreateAsset(look, LookPath); }
        look.sourceScene = LookName;
        look.Capture(new AtmosphereLook.Sources { block = block, blockFade = fade, player = player, obstacle = obstacle, sky = sky });
        look.hasChasm = true;
        look.chasmTop = Palette.Paper;
        look.chasmBottom = Palette.PaperShade;
        look.chasmGlow = Color.white;
        look.chasmGlowStrength = 0.1f;
        look.chasmGlowCentre = new Vector2(0.5f, 0.6f);
        look.chasmGlowRadius = 0.65f;
        look.sunColor = Color.white;
        look.sunIntensity = 1.0f;
        look.sunShadowStrength = 0.45f;
        look.sunEuler = new Vector3(50f, 35f, 0f);
        look.ambientMode = (int)AmbientMode.Trilight;
        look.ambientSky = H("E8E6E0");
        look.ambientEquator = H("D0CEC8");
        look.ambientGround = H("8C8A86");
        look.cameraBackground = Palette.Paper;
        look.volumeProfile = vol;
        look.aheadKeys = new[] { Color.white, new Color(0.98f, 0.98f, 0.97f), new Color(0.95f, 0.95f, 0.94f), new Color(0.91f, 0.91f, 0.9f) };
        look.travelledColor = Palette.Light;
        look.doorGlowColor = Palette.Mid;
        look.doorStoneColor = Palette.Graphite;
        look.sceneAccent = Palette.Graphite;
        look.wispColorA = Palette.Light;
        look.wispColorB = Palette.Mid;
        look.trailSwitchColor = Palette.Graphite;
        look.faceColor = Palette.Paper;
        look.faceSwitchColor = Palette.Mid;
        look.arrowColor = Palette.Graphite;
        look.plankColor = Palette.Light;
        look.obstacleGlowColor = Palette.Graphite;
        look.obstacleGlowIntensity = 0f;
        look.obstacleGlowRange = 4f;
        look.skyTopColor = Palette.PaperShade;
        EditorUtility.SetDirty(look);

        EnsureRenderer();
        BuildProfiles(look);
        RebuildSet310();
        var palette = AssetDatabase.LoadAssetAtPath<LegStagePalette>("Assets/LegStages310/LegStagePalette_3_10.asset");
        if (palette != null) { palette.FillDefaults(); EditorUtility.SetDirty(palette); }
        AssetDatabase.SaveAssets();
        Debug.Log("[PencilCheckpoint] assets built: " + LookPath);
        return look;
    }

    static VolumeProfile BuildPost()
    {
        string path = Root + "/Settings/Pencil_Volume.asset";
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
        bloom.threshold.Override(1.2f);
        bloom.intensity.Override(0.05f);
        bloom.scatter.Override(0.4f);
        bloom.tint.Override(Color.white);
        var ca = Get<ColorAdjustments>();
        ca.postExposure.Override(0.1f);
        ca.contrast.Override(18f);
        ca.saturation.Override(-100f);   // the page is black and white (the menu band too, which has no paper pass)
        ca.colorFilter.Override(Color.white);
        var vig = Get<Vignette>();
        vig.color.Override(H("3A3A3A"));
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

    /// <summary>PencilCP_Renderer = StorybookCP_Renderer (SSAO kept) with its own passes off, + PencilLines before post
    /// (depth + normals) and PencilPaper after post. Registered on every pipeline asset; returns its index.</summary>
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
            if (f != null && (f.name == "PixelArtPass" || f.name == "ToonInkOutline" || f.name == "StorybookInk" || f.name == "StorybookPaper"))
            { f.SetActive(false); EditorUtility.SetDirty(f); }
        var lines = Feature(rd, LinesFeature);
        lines.passMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Pencil_Lines.mat");
        lines.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
        lines.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
        lines.fetchColorBuffer = true;
        lines.passIndex = 0;
        lines.SetActive(true);
        EditorUtility.SetDirty(lines);
        var paperPass = Feature(rd, PaperFeature);
        paperPass.passMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/M_Pencil_Paper.mat");
        paperPass.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
        paperPass.requirements = ScriptableRenderPassInput.None;
        paperPass.fetchColorBuffer = true;
        paperPass.passIndex = 0;
        paperPass.SetActive(true);
        EditorUtility.SetDirty(paperPass);
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
            else if (index != at) Debug.LogWarning("[PencilCheckpoint] renderer at index " + at + " on " + asset.name + " but " + index + " elsewhere.");
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
        var vxProfile = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(VoxelCheckpointBuilder.ProfilePath);
        var pc = CopyProfile(vxProfile != null ? vxProfile : set39.Get(5), ProfilePath, 8, L);
        pc.displayName = "Sketchbook";
        pc.look = look;
        pc.label = "";
        pc.keepObstacle = false;
        pc.takePlankColor = true;
        pc.lockedTeaseColor = Palette.Mid;
        pc.menuCamera = new MenuCameraPreset { motion = MenuShotMotion.LowSideTrack, positionOffset = new Vector3(0f, 3.5f, -12.5f), angles = new Vector2(14f, 250f), fov = 46f, motionSpeed = 0.03f, motionAmplitude = 5f };
        // Its own graphite motes in the MainUi band (the copied profile would carry another checkpoint's).
        string motes = "Assets/Checkpoints/Particles/Motes_CP08_Pencil.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(motes) == null)
        {
            var go = new GameObject("Motes_CP08_Pencil");
            MenuParticles.Configure(go.AddComponent<ParticleSystem>(), MenuParticles.ColorFor(look), AssetDatabase.LoadAssetAtPath<Material>("Assets/Checkpoints/Menu/M_MenuMote.mat"));
            PrefabUtility.SaveAsPrefabAsset(go, motes);
            Object.DestroyImmediate(go);
        }
        pc.ambientParticlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(motes);
        EditorUtility.SetDirty(pc);

        // Pencil scene: pencil from the start, every later portal keeps it (the pixel scene's look-less placeholders).
        var start = CopyProfile(set39.Get(1), Root + "/Profiles/CP01_PencilStart.asset", 1, L);
        start.displayName = "Sketchbook";
        start.look = look;
        EditorUtility.SetDirty(start);
        var set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetPencil);
        if (set == null) { set = Object.Instantiate(set39); AssetDatabase.CreateAsset(set, SetPencil); }
        set.name = "CheckpointSet_Pencil";
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

    /// <summary>3.10's order: Start, Crimson, Toon, Pixel, Storybook (3.9's own profiles), Flower 6, Voxel 7, PENCIL 8,
    /// Raspberry 9, Jade 10. VoxelCheckpointBuilder.RebuildSet310 hands over to this once the pencil profile exists.</summary>
    public static void RebuildSet310()
    {
        // The space checkpoint (3.10 checkpoint 9) replaced Raspberry there: its builder owns the order once it exists.
        if (AssetDatabase.LoadAssetAtPath<CheckpointProfile>(SpaceCheckpointBuilder.ProfilePath) != null)
        {
            SpaceCheckpointBuilder.RebuildSet310();
            return;
        }
        var pc = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(ProfilePath);
        var vx = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(VoxelCheckpointBuilder.ProfilePath);
        var set310 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(FlowerCheckpointBuilder.Set310);
        var set39 = AssetDatabase.LoadAssetAtPath<CheckpointSet>(PixelCheckpointBuilder.Set39);
        var flower = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(FlowerCheckpointBuilder.ProfilePath);
        if (pc == null || vx == null || set310 == null || set39 == null || flower == null) return;
        int L = set39.legsBetweenPortals;
        CheckpointProfile Moved(string oldPath, string newPath, int index)
        {
            if (AssetDatabase.LoadAssetAtPath<CheckpointProfile>(newPath) == null && AssetDatabase.LoadAssetAtPath<CheckpointProfile>(oldPath) != null)
            {
                string err = AssetDatabase.MoveAsset(oldPath, newPath);   // keeps the GUID
                if (!string.IsNullOrEmpty(err)) Debug.LogError("[PencilCheckpoint] move " + oldPath + ": " + err);
            }
            var p = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(newPath);
            if (p == null) return null;
            p.index = index;
            p.legIndex = (index - 1) * L;
            EditorUtility.SetDirty(p);
            return p;
        }
        string F = FlowerCheckpointBuilder.Root + "/Profiles/";
        var jade = Moved(F + "CP09_JadeMoonGarden_3_10.asset", F + "CP10_JadeMoonGarden_3_10.asset", 10);   // Jade first: frees CP09
        var rasp = Moved(F + "CP08_Raspberry_3_10.asset", F + "CP09_Raspberry_3_10.asset", 9);
        set310.checkpoints = new List<CheckpointProfile> { set39.Get(1), set39.Get(2), set39.Get(3), set39.Get(4), set39.Get(5), flower, vx, pc };
        if (rasp != null) set310.checkpoints.Add(rasp);
        if (jade != null) set310.checkpoints.Add(jade);
        EditorUtility.SetDirty(set310);
    }

    // ------------------------------------------------------------------ scenes

    public static DepthCueController.CueLook PencilCue() => new DepthCueController.CueLook
    {
        name = "Pencil (Sketchbook)",
        ladder = 0f, litShare = 0.6f, shadeShare = 0.35f, bottomShare = 0.15f, runnerLadder = 1f, litRight = false,
        lip = 0f, lipWidth = 0.045f,
        contactColor = H("3A3A3C"), contact = 0.35f, contactSize = 0.9f, runnerRim = 0f,
        railColor = H("3A3A3C"), rail = 0.25f, railWidth = 0.04f,
        trailTint = Color.white, trailTintAmount = 0f, trailIntensity = 1f,
        decor = 0.4f, bloomCut = 0.6f,
    };

    static void WireOpenScene(AtmosphereLook look, CheckpointSet set, bool pencilScene)
    {
        var dir = Object.FindFirstObjectByType<LookTransitionDirector>();
        dir.checkpoints = set;
        dir.baseLookOverride = pencilScene ? look : null;
        dir.blockWipeShader = Shader.Find("PixelCheckpoint/FlatFaceBlockPixel");
        EditorUtility.SetDirty(dir);
        var crc = Object.FindFirstObjectByType<CheckpointRunController>(FindObjectsInactive.Include);
        if (crc != null) { crc.checkpoints = set; EditorUtility.SetDirty(crc); }

        var cues = Object.FindFirstObjectByType<DepthCueController>(FindObjectsInactive.Include);
        if (cues != null)
        {
            var list = new List<DepthCueController.CueLook>(cues.looks);
            if (pencilScene)
            {
                list.Clear();
                for (int i = 0; i < 10; i++) list.Add(PencilCue());
            }
            else
            {
                int at = list.FindIndex(c => c != null && c.name != null && c.name.StartsWith("Pencil"));
                if (at >= 0) list[at] = PencilCue();
                else list.Insert(Mathf.Min(Portal, list.Count), PencilCue());
            }
            cues.looks = list.ToArray();
            EditorUtility.SetDirty(cues);
        }

        if (pencilScene)
        {
            var stages = Object.FindFirstObjectByType<LegStageColors>(FindObjectsInactive.Include);
            if (stages != null) { stages.enabled = false; EditorUtility.SetDirty(stages); }
        }

        var old = Object.FindFirstObjectByType<PencilCheckpointController>(FindObjectsInactive.Include);
        GameObject host = old != null ? old.gameObject : new GameObject("PencilCheckpoint");
        if (old != null) Object.DestroyImmediate(old);
        var ctl = host.AddComponent<PencilCheckpointController>();
        ctl.pencilLook = look;
        ctl.alwaysPencil = pencilScene;
        ctl.pencilPortal = Portal;
        string M = Root + "/Materials/";
        ctl.backdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(M + "M_Pencil_Backdrop.mat");
        ctl.propMaterial = AssetDatabase.LoadAssetAtPath<Material>(M + "M_Pencil_Prop.mat");
        ctl.runnerBody = AssetDatabase.LoadAssetAtPath<Material>(M + "M_Pencil_RunnerBody.mat");
        ctl.runnerEyeWhite = AssetDatabase.LoadAssetAtPath<Material>(M + "M_Pencil_RunnerEyeWhite.mat");
        ctl.runnerPupil = AssetDatabase.LoadAssetAtPath<Material>(M + "M_Pencil_RunnerPupil.mat");
        ctl.runnerCape = AssetDatabase.LoadAssetAtPath<Material>(M + "M_Pencil_RunnerCape.mat");
        ctl.pencilRendererIndex = EnsureRenderer();
        var fx = host.GetComponent<PencilRunnerFX>();
        if (fx == null) fx = host.AddComponent<PencilRunnerFX>();
        fx.controller = ctl;
        fx.frontMaterial = AssetDatabase.LoadAssetAtPath<Material>(M + "M_Pencil_FrontTrail.mat");
        fx.fleckMaterial = AssetDatabase.LoadAssetAtPath<Material>(M + "M_Pencil_Fleck.mat");
        fx.streakMaterial = AssetDatabase.LoadAssetAtPath<Material>(M + "M_Pencil_Streak.mat");
        EditorUtility.SetDirty(fx);
        Camera cam = Camera.main;
        var cd = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
        if (cd != null) ctl.baseRendererIndex = new SerializedObject(cd).FindProperty("m_RendererIndex").intValue;
        EditorUtility.SetDirty(ctl);
    }

    public static void Wire310()
    {
        if (AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath) == null) { Debug.LogError("[PencilCheckpoint] run Build Assets first."); return; }
        var scene = EditorSceneManager.OpenScene(Scene310, OpenSceneMode.Single);
        WireOpenScene(AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath), AssetDatabase.LoadAssetAtPath<CheckpointSet>(FlowerCheckpointBuilder.Set310), false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PencilCheckpoint] 3.10 wired: pencil look at checkpoint 8 (portal 7).");
    }

    public static void MakePencilScene()
    {
        if (AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath) == null) { Debug.LogError("[PencilCheckpoint] run Build Assets first."); return; }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePencil) == null) AssetDatabase.CopyAsset(Scene310, ScenePencil);
        var scene = EditorSceneManager.OpenScene(ScenePencil, OpenSceneMode.Single);
        WireOpenScene(AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LookPath), AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetPencil), true);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PencilCheckpoint] " + ScenePencil + " made: pencil look for the whole run.");
    }
}
#endif
