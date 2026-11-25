using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds Boxi as the 3.8 runner: glow materials, BoxiRunner38.controller, the BoxiRunner38 prefab, and wires it into
/// "MainScene prop camera 3.8" (only that scene). Re-runnable. The original BoxiCharacter assets are not modified.
/// </summary>
public static class BoxiRunnerBuilder
{
    const string Root = "Assets/MainCharacter/BoxiCharacter";
    const string Dir = Root + "/Runner38";
    const string Fbx = Root + "/Models/Boxi.fbx";
    const string ScenePath = "Assets/MainScene prop camera 3.8.unity";
    const string PrefabPath = Dir + "/BoxiRunner38.prefab";

    [MenuItem("Tools/Boxi Runner/Build Assets")]
    public static void BuildAssets()
    {
        foreach (var f in new[] { "Materials", "Animations" })
            if (!AssetDatabase.IsValidFolder(Dir + "/" + f)) AssetDatabase.CreateFolder(Dir, f);
        var ctrl = BuildController();
        BuildPrefab(ctrl);
        AssetDatabase.SaveAssets();
        Debug.Log("[BoxiRunner] assets built: " + PrefabPath);
    }

    [MenuItem("Tools/Boxi Runner/Build And Wire Into 3.8")]
    public static void BuildAndWire()
    {
        BuildAssets();
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) { Debug.LogError("[BoxiRunner] open " + ScenePath + " first"); return; }
        var runner = Object.FindFirstObjectByType<CubeRailRunner>();
        if (runner == null) { Debug.LogError("[BoxiRunner] no runner"); return; }
        foreach (var old in runner.GetComponentsInChildren<BoxiRunner>(true)) Object.DestroyImmediate(old.gameObject);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), runner.transform);
        go.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        go.transform.localRotation = Quaternion.identity;
        foreach (var t in runner.GetComponentsInChildren<PlayerTotem>(true)) t.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[BoxiRunner] wired into " + ScenePath);
    }

    static Material Mat(string name, Texture tex, float cutoff, float rim, float lift)
    {
        string path = Dir + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find("Boxi/RunnerGlow");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Cutoff", cutoff);
        m.SetFloat("_RimStrength", rim);
        m.SetFloat("_GlowLift", lift);
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static AnimatorController BuildController()
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
        AnimationClip C(string n) { clips.TryGetValue(n, out var c); if (c == null) Debug.LogWarning("[BoxiRunner] missing clip " + n); return c; }

        string path = Dir + "/Animations/BoxiRunner38.controller";
        AssetDatabase.DeleteAsset(path);
        var ac = AnimatorController.CreateAnimatorControllerAtPath(path);
        ac.AddParameter("RunSpeed", AnimatorControllerParameterType.Float);
        ac.AddParameter("Blink", AnimatorControllerParameterType.Trigger);
        var ps = ac.parameters;
        foreach (var p in ps) if (p.name == "RunSpeed") p.defaultFloat = 1f;
        ac.parameters = ps;

        // BoxiRunner drives the base layer with CrossFade; only Land / Hop need a way home.
        var sm = ac.layers[0].stateMachine;
        var idle = sm.AddState("Idle", new Vector3(300, 0)); idle.motion = C("Idle");
        var run = sm.AddState("Run", new Vector3(300, 120)); run.motion = C("Run_InPlace_v2");
        run.speedParameter = "RunSpeed"; run.speedParameterActive = true;
        var hop = sm.AddState("Hop", new Vector3(0, 240)); hop.motion = C("Jump_Air"); hop.speed = 2.5f;
        var land = sm.AddState("Land", new Vector3(500, 240)); land.motion = C("Land"); land.speed = 2.4f;
        var smash = sm.AddState("Smash", new Vector3(0, 360)); smash.motion = C("Smash"); smash.speed = 1.3f;
        sm.defaultState = idle;
        var t = land.AddTransition(run); t.hasExitTime = true; t.exitTime = 0.75f; t.duration = 0.08f; t.hasFixedDuration = true;
        t = hop.AddTransition(run); t.hasExitTime = true; t.exitTime = 1f; t.duration = 0.06f; t.hasFixedDuration = true;   // safety net

        var blinkSm = new AnimatorStateMachine { name = "Blink", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(blinkSm, ac);
        ac.AddLayer(new AnimatorControllerLayer { name = "Blink", defaultWeight = 1f, blendingMode = AnimatorLayerBlendingMode.Additive, stateMachine = blinkSm });
        var empty = blinkSm.AddState("Empty", new Vector3(300, 0));
        var blink = blinkSm.AddState("Blink", new Vector3(300, 120)); blink.motion = C("Blink");
        blinkSm.defaultState = empty;
        t = empty.AddTransition(blink); t.duration = 0f; t.hasExitTime = false; t.AddCondition(AnimatorConditionMode.If, 0, "Blink");
        t = blink.AddTransition(empty); t.duration = 0f; t.hasExitTime = true; t.exitTime = 1f;
        EditorUtility.SetDirty(ac);
        return ac;
    }

    static void BuildPrefab(AnimatorController ctrl)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/T_Boxi_Atlas.png");
        var fur = Mat("M_BoxiRunner_Fur", tex, 0.5f, 1.2f, 0.2f);
        var body = Mat("M_BoxiRunner_Body", tex, 0.02f, 0.7f, 0.12f);
        var gloss = Mat("M_BoxiRunner_Gloss", tex, 0.02f, 0.25f, 0.04f);

        var root = new GameObject("BoxiRunner38");
        var yaw = new GameObject("Yaw").transform; yaw.SetParent(root.transform, false);
        var motion = new GameObject("Motion").transform; motion.SetParent(yaw, false);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Fbx));
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Boxi";
        model.transform.SetParent(motion, false);

        var anim = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var faceC = model.AddComponent<Boxi.BoxiFace>();
        var furC = model.AddComponent<Boxi.BoxiFurPhysics>();
        furC.windStrength = 0.5f;
        furC.teleportDistance = 3f;
        var blinker = model.AddComponent<Boxi.BoxiBlinker>();
        blinker.animator = anim;

        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.sharedMaterials = smr.sharedMaterials.Select(m =>
                m == null ? fur : m.name.Contains("Gloss") ? gloss : m.name.Contains("Fur") ? fur : body).ToArray();
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            smr.receiveShadows = false;
            smr.updateWhenOffscreen = false;
            smr.skinnedMotionVectors = false;
            smr.quality = SkinQuality.Bone2;
        }

        var lightGo = new GameObject("BoxiGlowLight");
        lightGo.transform.SetParent(motion, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.ForcePixel;

        var br = root.AddComponent<BoxiRunner>();
        br.yaw = yaw; br.motion = motion; br.model = model.transform; br.animator = anim;
        br.face = faceC; br.fur = furC; br.glowLight = light;
        light.color = br.glowColor; light.range = br.lightRange; light.intensity = br.lightIntensity;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
    }
}
