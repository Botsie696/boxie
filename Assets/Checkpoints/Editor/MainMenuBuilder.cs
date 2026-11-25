using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Tools > Checkpoints > Build MainUi Menu. Rebuilds MainUi as the checkpoint-stack menu (re-runnable):
///   1. MainUi_backup.unity is made once (never touched again).
///   2. Font assets (Sniglet ExtraBold, Varela Round), band / trail materials, ambient mote prefabs for real checkpoints.
///   3. The gameplay materials, shaders, coin and fog values are READ from MainScene prop camera 3.6 (opened additively,
///      closed unsaved).
///   4. MainUi keeps only its camera and EventSystem; a "MainMenu" object (MainMenuController + MenuDioramaRig), a sun and a
///      global Volume are added. 3.6 is added to Build Settings.
/// </summary>
public static class MainMenuBuilder
{
    const string MenuScene = "Assets/MainUi.unity";
    const string BackupScene = "Assets/MainUi_backup.unity";
    const string GameScene = "Assets/MainScene prop camera 3.11.unity";
    const string Dir = "Assets/Checkpoints/Menu";
    const string FontDir = "Assets/Checkpoints/Fonts";
    const string ParticleDir = "Assets/Checkpoints/Particles";

    [MenuItem("Tools/Checkpoints/Build MainUi Menu")]
    public static void Build()
    {
        if (!File.Exists(BackupScene)) { AssetDatabase.CopyAsset(MenuScene, BackupScene); Debug.Log("[Menu] backup made: " + BackupScene); }

        TMP_FontAsset title = FontAsset("Sniglet-ExtraBold");
        TMP_FontAsset body = FontAsset("VarelaRound-Regular");
        Material bandMat = MaterialAt(Dir + "/M_CheckpointBand.mat", Shader.Find("UI/CheckpointBand"));
        Material trailMat = MaterialAt(Dir + "/M_MenuTrail.mat", Shader.Find("Sprites/Default"));
        Material moteMat = MaterialAt(Dir + "/M_MenuMote.mat", Shader.Find("Sprites/Default"));
        CheckpointSet set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(CheckpointSetup.SetPath);
        MotePrefabs(set, moteMat);
        FixShotUnits(set);

        // ---- read the game scene (never saved) ----
        var src = ReadGameScene();

        // ---- MainUi ----
        Scene active = SceneManager.GetActiveScene();
        if (active.isDirty && active.path != MenuScene) { Debug.LogError("[Menu] save or discard " + active.path + " first."); return; }
        Scene menu = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
        var keep = new HashSet<string> { "Main Camera", "EventSystem" };
        foreach (GameObject r in menu.GetRootGameObjects())
            if (!keep.Contains(r.name)) { Debug.Log("[Menu] removed " + r.name); Object.DestroyImmediate(r); }

        Camera main = menu.GetRootGameObjects().Select(g => g.GetComponent<Camera>()).FirstOrDefault(c => c != null);
        if (main != null)
        {
            main.clearFlags = CameraClearFlags.SolidColor;
            main.backgroundColor = new Color(0.035f, 0.05f, 0.11f, 1f);
            main.cullingMask = 0;
            var data = main.GetComponent<UniversalAdditionalCameraData>() ?? main.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
        }

        var sunGo = new GameObject("MenuSun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.shadows = LightShadows.None;
        sun.transform.rotation = src.sunRotation;
        sun.color = src.sunColor;
        sun.intensity = src.sunIntensity;

        var volGo = new GameObject("MenuVolume");
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = src.volume;

        var rootGo = new GameObject("MainMenu");
        var rig = rootGo.AddComponent<MenuDioramaRig>();
        rig.checkpoints = set;
        rig.blockMaterial = src.block;
        rig.playerMaterial = src.player;
        rig.blockWipeShader = src.blockWipe;
        rig.backdropShader = src.backdrop;
        rig.skyMaterial = src.sky;
        rig.skyWipeShader = src.skyWipe;
        rig.voidMaterial = src.voidMat;
        rig.glideLength = 9999f;
        rig.slices = AssetDatabase.LoadAssetAtPath<MenuSliceData>(MenuSliceBaker.AssetPath);
        if (rig.slices == null) Debug.LogWarning("[Menu] no baked slices: run MenuSliceBaker.Bake() in 3.6 Play mode.");
        rig.coinPrefab = src.coinPrefab;
        rig.coinMaterial = src.coinMaterial;
        rig.trailMaterial = trailMat;
        rig.particleMaterial = moteMat;
        rig.fog = src.fog;
        rig.sun = sun;
        rig.volume = vol;
        var menuCtl = rootGo.AddComponent<MainMenuController>();
        menuCtl.checkpoints = set;
        menuCtl.rig = rig;
        menuCtl.bandMaterial = bandMat;
        menuCtl.titleFont = title;
        menuCtl.bodyFont = body;
        SkinBuilder.Wire(rootGo.AddComponent<ShopController>());

        EditorSceneManager.MarkSceneDirty(menu);
        EditorSceneManager.SaveScene(menu);
        AddToBuildSettings();
        Debug.Log("[Menu] MainUi rebuilt.");
    }

    // ------------------------------------------------------------------ game scene (read only)

    struct Sources
    {
        public Material block, player, coinMaterial, sky, voidMat;
        public Shader blockWipe, backdrop, skyWipe;
        public GameObject coinPrefab;
        public MenuDioramaRig.FogSettings fog;
        public Quaternion sunRotation;
        public Color sunColor;
        public float sunIntensity;
        public VolumeProfile volume;
    }

    static Sources ReadGameScene()
    {
        var s = new Sources { fog = MenuDioramaRig.FogSettings.Default, sunRotation = Quaternion.Euler(50f, -30f, 0f), sunColor = Color.white, sunIntensity = 1f };
        Scene sc = SceneManager.GetSceneByPath(GameScene);
        bool wasOpen = sc.IsValid() && sc.isLoaded;
        if (!wasOpen) sc = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Additive);
        try
        {
            GameObject[] roots = sc.GetRootGameObjects();
            T Find<T>() where T : Component
            {
                foreach (GameObject r in roots) { T c = r.GetComponentInChildren<T>(true); if (c != null) return c; }
                return null;
            }
            var dir = Find<LookTransitionDirector>();
            if (dir != null) { s.block = dir.blockMaterial; s.player = dir.playerMaterial; s.blockWipe = dir.blockWipeShader; s.sky = dir.skyMaterial; s.skyWipe = dir.skyWipeShader; }
            var zonesC = Find<PortalZoneController>();
            if (zonesC != null) s.voidMat = zonesC.voidMaterial;
            var gen = Find<DynamicPathGenerator>();
            if (gen != null) { s.coinPrefab = gen.coinPrefab; s.coinMaterial = gen.coinMaterials != null && gen.coinMaterials.Length > 0 ? gen.coinMaterials[0] : null; }
            var chasm = Find<ChasmFog>();
            if (chasm != null)
            {
                s.backdrop = chasm.backdropShader;
                s.fog = new MenuDioramaRig.FogSettings
                {
                    fogStart = chasm.fogStart, fogSolid = chasm.fogSolid, billowHeight = chasm.billowHeight, billowSize = chasm.billowSize,
                    billowSpeed = chasm.billowSpeed, windDirection = chasm.windDirection, hazeStart = chasm.hazeStart, hazeEnd = chasm.hazeEnd,
                    hazeMax = chasm.hazeMax, screenFogSolid = chasm.screenFogSolid, screenFogTop = chasm.screenFogTop,
                    screenFogStrength = chasm.screenFogStrength, veil = chasm.veil, houseFogShare = chasm.houseFogShare,
                    houseFogAtHouse = chasm.houseFogAtHouse, houseFogDepth = chasm.houseFogDepth,
                };
            }
            Light sun = roots.SelectMany(r => r.GetComponentsInChildren<Light>(true)).FirstOrDefault(l => l.type == LightType.Directional);
            if (sun != null) { s.sunRotation = sun.transform.rotation; s.sunColor = sun.color; s.sunIntensity = sun.intensity; }
            Volume v = roots.SelectMany(r => r.GetComponentsInChildren<Volume>(true)).Where(x => x.isGlobal).OrderByDescending(x => x.priority).FirstOrDefault();
            if (v != null) s.volume = v.sharedProfile;
        }
        finally { if (!wasOpen) EditorSceneManager.CloseScene(sc, true); }
        if (s.block == null || s.blockWipe == null) Debug.LogError("[Menu] could not read the block material / wipe shader from " + GameScene);
        return s;
    }

    // ------------------------------------------------------------------ assets

    static TMP_FontAsset FontAsset(string file)
    {
        string path = FontDir + "/" + file + " SDF.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing != null) return existing;
        Font font = AssetDatabase.LoadAssetAtPath<Font>(FontDir + "/" + file + ".ttf");
        if (font == null) { Debug.LogError("[Menu] missing font " + file); return null; }
        TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        fa.name = file + " SDF";
        AssetDatabase.CreateAsset(fa, path);
        fa.material.name = file + " SDF Material";
        fa.atlasTexture.name = file + " SDF Atlas";
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa);
        fa.TryAddCharacters(" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~");
        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        return fa;
    }

    static Material MaterialAt(string path, Shader shader)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void MotePrefabs(CheckpointSet set, Material mat)
    {
        if (set == null) return;
        if (!AssetDatabase.IsValidFolder(ParticleDir)) AssetDatabase.CreateFolder("Assets/Checkpoints", "Particles");
        foreach (CheckpointProfile p in set.checkpoints)
        {
            if (p == null || p.look == null) continue;
            string path = ParticleDir + "/Motes_CP" + p.index.ToString("00") + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                var go = new GameObject("Motes_CP" + p.index.ToString("00"));
                MenuParticles.Configure(go.AddComponent<ParticleSystem>(), MenuParticles.ColorFor(p.look), mat);
                PrefabUtility.SaveAsPrefabAsset(go, path);
                Object.DestroyImmediate(go);
            }
            if (p.ambientParticlePrefab == null)
            {
                p.ambientParticlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                EditorUtility.SetDirty(p);
            }
        }
        AssetDatabase.SaveAssets();
    }

    // Menu shots: motionSpeed is cycles per second for every motion (an early setup wrote units per second for the side track).
    static void FixShotUnits(CheckpointSet set)
    {
        if (set == null) return;
        foreach (CheckpointProfile p in set.checkpoints)
        {
            if (p == null || p.menuCamera.motionSpeed <= 0.5f) continue;
            MenuCameraPreset c = p.menuCamera;
            c.motionSpeed = 0.035f;
            c.motionAmplitude = 6f;
            p.menuCamera = c;
            EditorUtility.SetDirty(p);
        }
        AssetDatabase.SaveAssets();
    }

    static void AddToBuildSettings()
    {
        var list = EditorBuildSettings.scenes.ToList();
        if (!list.Any(s => s.path == GameScene)) list.Add(new EditorBuildSettingsScene(GameScene, true));
        int menuIdx = list.FindIndex(s => s.path == MenuScene);
        if (menuIdx > 0) { var m = list[menuIdx]; list.RemoveAt(menuIdx); list.Insert(0, m); }
        EditorBuildSettings.scenes = list.ToArray();
    }
}
