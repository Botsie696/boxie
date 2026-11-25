using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds Poppy as the 3.9 runner: PoppyRunner39.controller and the PoppyRunner39 prefab (her own materials on Poppy/RunnerLit - her
/// glow is a soft emission inside the material, no scene light), and wires it into "MainScene prop camera 3.9" only, . Re-runnable.
/// The original PoppyCharacter assets are not modified.
/// </summary>
public static class PoppyRunnerBuilder
{
    const string Root = "Assets/MainCharacter/PoppyCharacter";
    const string Dir = Root + "/Runner39";
    const string Fbx = Root + "/Models/Poppy.fbx";
    const string ScenePath = "Assets/MainScene prop camera 3.9.unity";
    const string PrefabPath = Dir + "/PoppyRunner39.prefab";

    [MenuItem("Tools/Poppy Runner/Build And Wire Into 3.9")]
    public static void BuildAndWire()
    {
        if (!AssetDatabase.IsValidFolder(Dir + "/Animations")) AssetDatabase.CreateFolder(Dir, "Animations");
        var ctrl = BuildController();
        BuildPrefab(ctrl);
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) { Debug.LogError("[PoppyRunner] open " + ScenePath + " first"); return; }
        var runner = Object.FindFirstObjectByType<CubeRailRunner>();
        if (runner == null) { Debug.LogError("[PoppyRunner] no runner"); return; }
        foreach (var old in runner.GetComponentsInChildren<PoppyRunner>(true)) Object.DestroyImmediate(old.gameObject);
        foreach (var old in runner.GetComponentsInChildren<LumiRunner>(true)) Object.DestroyImmediate(old.gameObject);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), runner.transform);
        go.transform.localPosition = new Vector3(0f, -0.5f, 0f);
        go.transform.localRotation = Quaternion.identity;
        foreach (var t in runner.GetComponentsInChildren<PlayerTotem>(true)) t.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PoppyRunner] wired into " + ScenePath);
    }

    // Her materials on Poppy/RunnerLit: same atlas, same colours (Base Color copied), her own light rig. The originals in
    // PoppyCharacter/Materials are left alone. Per part: fur soft + rim, brows / skin less rim, eyes (Gloss) a catch-light.
    static Material LitCopy(Material src)
    {
        if (src == null) return null;
        if (!AssetDatabase.IsValidFolder(Dir + "/Materials")) AssetDatabase.CreateFolder(Dir, "Materials");
        string name = src.name.Replace("M_Poppy_", "M_PoppyRunner_");
        string path = Dir + "/Materials/" + name + ".mat";
        var sh = Shader.Find("Poppy/RunnerFlat");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
        m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
        bool cutout = src.HasProperty("_AlphaClip") && src.GetFloat("_AlphaClip") > 0.5f;
        m.SetFloat("_Cutoff", cutout ? src.GetFloat("_Cutoff") : 0f);
        float rim = 0.12f, spec = 0f, gloss = 40f, catchL = 0f;
        if (name.Contains("FurAccent")) rim = 0.2f;
        else if (name.Contains("Skin")) rim = 0.12f;
        else if (name.Contains("Gloss")) { rim = 0f; spec = 0.7f; gloss = 90f; catchL = 1.3f; }
        else if (name.Contains("Bow")) { rim = 0.25f; spec = 0.25f; gloss = 30f; }
        else if (name.EndsWith("HairGlow")) { rim = 0.3f; spec = 0.3f; gloss = 30f; }
        else if (name.EndsWith("Hair")) { rim = 0.25f; spec = 0.15f; gloss = 24f; }
        else if (name.Contains("Lip")) { rim = 0.2f; spec = 0.3f; gloss = 30f; }
        // her authored glow (HDR emission on the gold fur edges + teal brows) as a soft, bloom-safe add inside the shader
        Color em = src.HasProperty("_EmissionColor") ? src.GetColor("_EmissionColor") : Color.black;
        float emMax = Mathf.Max(em.r, Mathf.Max(em.g, em.b));
        float emK = name.Contains("FurGlow") ? 0.6f : name.Contains("FurAccent") || name.Contains("HairGlow") || name.Contains("Bow") ? 0.45f : 0.1f;
        m.SetColor("_EmitColor", emMax > 0.001f ? new Color(em.r / emMax, em.g / emMax, em.b / emMax, 1f) * emK : Color.black);
        // plum hair reads as a black hole next to the flat-lit pink: a lighter plum keeps its shape visible
        if (name.EndsWith("Hair")) m.SetColor("_BaseColor", new Color(0.55f, 0.2f, 0.78f, 1f));
        // her light rig (the same on every part): bright enough that the coral reads like her reference renders
        m.SetFloat("_Brightness", 1.1f);
        m.SetFloat("_TopShade", 1f);
        m.SetFloat("_LitShade", 0.88f);
        m.SetFloat("_ShadeShade", 0.7f);
        m.SetFloat("_BottomShade", 0.45f);
        m.SetFloat("_TopSharpness", 8f);
        m.SetFloat("_KeyStrength", 1.1f);
        m.SetFloat("_Ambient", 0.62f);
        m.SetFloat("_Wrap", 0.5f);
        m.SetFloat("_FillStrength", 0.3f);
        m.SetFloat("_AOBottom", 0.55f);
        m.SetFloat("_SceneTint", 0.2f);
        m.SetFloat("_MaxBright", 0.97f);
        m.SetFloat("_RimStrength", rim);
        m.SetFloat("_Spec", spec);
        m.SetFloat("_Gloss", gloss);
        m.SetFloat("_CatchLight", catchL);
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static AnimatorController BuildController()
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
        AnimationClip C(string n) { clips.TryGetValue(n, out var c); if (c == null) Debug.LogWarning("[PoppyRunner] missing clip " + n); return c; }

        string path = Dir + "/Animations/PoppyRunner39.controller";
        AssetDatabase.DeleteAsset(path);
        var ac = AnimatorController.CreateAnimatorControllerAtPath(path);
        ac.AddParameter("RunSpeed", AnimatorControllerParameterType.Float);
        ac.AddParameter("Blink", AnimatorControllerParameterType.Trigger);
        var ps = ac.parameters;
        foreach (var p in ps) if (p.name == "RunSpeed") p.defaultFloat = 1f;
        ac.parameters = ps;

        // PoppyRunner drives the base layer with CrossFade. Hop / Land are sped up so the 0.4-0.6 s clips fit the game's
        // 0.10-0.12 s roll and the run comes straight back.
        var sm = ac.layers[0].stateMachine;
        var idle = sm.AddState("Idle", new Vector3(300, 0)); idle.motion = C("Idle");
        var run = sm.AddState("Run", new Vector3(300, 120)); run.motion = C("Run_InPlace_v2");
        run.speedParameter = "RunSpeed"; run.speedParameterActive = true;
        var hop = sm.AddState("Hop", new Vector3(0, 240)); hop.motion = C("Jump_Air"); hop.speed = 2.5f;
        var land = sm.AddState("Land", new Vector3(500, 240)); land.motion = C("Land"); land.speed = 2.4f;
        var smash = sm.AddState("Smash", new Vector3(0, 360)); smash.motion = C("Smash"); smash.speed = 1.3f;
        sm.defaultState = idle;
        var t = land.AddTransition(run); t.hasExitTime = true; t.exitTime = 0.75f; t.duration = 0.08f; t.hasFixedDuration = true;
        t = hop.AddTransition(run); t.hasExitTime = true; t.exitTime = 1f; t.duration = 0.06f; t.hasFixedDuration = true;

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
        var root = new GameObject("PoppyRunner39");
        var yaw = new GameObject("Yaw").transform; yaw.SetParent(root.transform, false);
        var motion = new GameObject("Motion").transform; motion.SetParent(yaw, false);
        // her own prefab: the materials PoppyCharacterSetup made (URP Lit, no emission)
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/PoppyCharacter.prefab");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Poppy";
        model.transform.SetParent(motion, false);

        var anim = model.GetComponent<Animator>(); if (anim == null) anim = model.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var faceC = model.GetComponent<Poppy.PoppyFace>(); if (faceC == null) faceC = model.AddComponent<Poppy.PoppyFace>();
        var furC = model.GetComponent<Poppy.PoppyFurPhysics>(); if (furC == null) furC = model.AddComponent<Poppy.PoppyFurPhysics>();
        furC.teleportDistance = 3f;
        var blinker = model.GetComponent<Poppy.PoppyBlinker>(); if (blinker == null) blinker = model.AddComponent<Poppy.PoppyBlinker>();
        blinker.animator = anim;
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.sharedMaterials = smr.sharedMaterials.Select(LitCopy).ToArray();
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            smr.receiveShadows = false;
            smr.skinnedMotionVectors = false;
            smr.quality = SkinQuality.Bone2;
        }

        var cr = root.AddComponent<PoppyRunner>();
        cr.yaw = yaw; cr.motion = motion; cr.model = model.transform; cr.animator = anim;
        cr.face = faceC; cr.fur = furC; cr.glowLight = null;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
    }
}
