using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cubie.EditorTools
{
    /// <summary>Import settings for anything inside a "CubieCharacter" folder.</summary>
    public class CubieCharacterPostprocessor : AssetPostprocessor
    {
        internal static bool InCubie(string p) => p.Replace('\\', '/').Contains("/CubieCharacter/");
        internal static readonly string[] LoopClips = { "Run_InPlace", "Run_InPlace_v2", "Idle", "Jump_Air" };

        void OnPreprocessModel()
        {
            if (!InCubie(assetPath) || !assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.isReadable = false;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.optimizeGameObjects = false; // physics + face scripts need the bone transforms
            mi.importAnimation = true;
            mi.resampleCurves = true;
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        void OnPreprocessAnimation()
        {
            if (!InCubie(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            if (mi.clipAnimations != null && mi.clipAnimations.Length > 0) return; // keep user edits
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                string n = c.takeName.Contains("|") ? c.takeName.Substring(c.takeName.LastIndexOf('|') + 1) : c.takeName;
                c.name = n;
                c.loopTime = LoopClips.Contains(n);
                c.loopPose = false;
            }
            mi.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            if (!InCubie(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;
        }
    }

    /// <summary>Builds materials, Animator Controller and the ready-to-use prefab.</summary>
    [InitializeOnLoad]
    public static class CubieCharacterSetup
    {
        const string PrefabName = "CubieCharacter.prefab";

        static CubieCharacterSetup() { EditorApplication.delayCall += AutoRun; }

        static void AutoRun()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += AutoRun; return; }
            string fbx = FindFbx();
            if (fbx == null) return;
            string root = RootOf(fbx);
            if (!File.Exists(root + "/Prefabs/" + PrefabName) || !File.Exists(root + "/Materials/M_Cubie_FurMain.mat")) Run();
        }

        static string FindFbx() =>
            AssetDatabase.FindAssets("Cubie t:Model").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => CubieCharacterPostprocessor.InCubie(p) && Path.GetFileName(p) == "Cubie.fbx");

        static string RootOf(string fbx) => Path.GetDirectoryName(Path.GetDirectoryName(fbx)).Replace('\\', '/');

        [MenuItem("Tools/Cubie Character/Run Setup (Materials, Animator, Prefab)")]
        public static void Run()
        {
            string fbx = FindFbx();
            if (fbx == null) { Debug.LogError("[Cubie] Could not find CubieCharacter/Models/Cubie.fbx"); return; }
            string root = RootOf(fbx);
            foreach (var f in new[] { "Materials", "Animations", "Prefabs" })
                if (!AssetDatabase.IsValidFolder(root + "/" + f)) AssetDatabase.CreateFolder(root, f);

            // Fur / skin / bow textures are grayscale: the material colour IS the character colour.
            // Change Base Color on these materials (or use CubieColors at runtime) to recolour.
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/Textures/T_Cubie_Atlas.png");
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
            foreach (var spec in MaterialSpecs)
            {
                var m = MakeMaterial(root + "/Materials/" + spec.asset + ".mat", tex, spec.smoothness, spec.cutout, spec.tint);
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), spec.source), m);
            }
            mi.SaveAndReimport();

            var ctrl = BuildController(root + "/Animations/Cubie.controller", fbx);
            BuildPrefab(fbx, ctrl, root + "/Prefabs/" + PrefabName);
            AssetDatabase.SaveAssets();
            Debug.Log("[Cubie] Setup complete: " + root + "/Prefabs/" + PrefabName);
        }

        static Shader PickShader()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            string n = rp == null ? "" : rp.GetType().Name;
            Shader s = null;
            if (n.Contains("Universal")) s = Shader.Find("Universal Render Pipeline/Lit");
            else if (n.Contains("HD")) s = Shader.Find("HDRP/Lit");
            return s != null ? s : Shader.Find("Standard");
        }

        struct MatSpec
        {
            public string source, asset; public float smoothness; public bool cutout; public Color tint;
            public MatSpec(string s, string a, float sm, bool c, Color t) { source = s; asset = a; smoothness = sm; cutout = c; tint = t; }
        }

        // source = material name inside the FBX, asset = Unity material created for it, tint = default colour (sRGB)
        static readonly MatSpec[] MaterialSpecs =
        {
            new MatSpec("M_CB_FurMain",   "M_Cubie_FurMain",   0.15f, true,  new Color(1.00f, 0.56f, 0.50f)), // body fur, tuft, lid fur
            new MatSpec("M_CB_FurAccent", "M_Cubie_FurAccent", 0.15f, true,  new Color(0.05f, 0.60f, 0.62f)), // brows
            new MatSpec("M_CB_Skin",      "M_Cubie_Skin",      0.30f, false, new Color(0.95f, 0.48f, 0.42f)), // lids, inner body
            new MatSpec("M_CB_Bow",       "M_Cubie_Bow",       0.50f, false, new Color(0.55f, 0.95f, 0.80f)), // flower petals
            new MatSpec("M_CB_Gloss",     "M_Cubie_Gloss",     0.85f, false, Color.white),                    // eyes + flower centre (fixed colours)
        };

        static Material MakeMaterial(string path, Texture2D tex, float smoothness, bool cutout, Color tint)
        {
            var shader = PickShader();
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;

            foreach (var p in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" }) if (m.HasProperty(p)) m.SetTexture(p, tex);
            foreach (var p in new[] { "_BaseColor", "_Color" }) if (m.HasProperty(p)) m.SetColor(p, tint);
            foreach (var p in new[] { "_Smoothness", "_Glossiness" }) if (m.HasProperty(p)) m.SetFloat(p, smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);

            if (cutout)
            {
                // Fur cards are already double-sided geometry, so keep back-face culling ON (no extra overdraw).
                if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
                if (m.HasProperty("_AlphaCutoff")) m.SetFloat("_AlphaCutoff", 0.5f);
                if (shader.name.StartsWith("Universal"))
                {
                    m.SetFloat("_Surface", 0f);
                    m.SetFloat("_AlphaClip", 1f);
                }
                else if (shader.name.StartsWith("HDRP"))
                {
                    m.SetFloat("_AlphaCutoffEnable", 1f);
                }
                else
                {
                    m.SetFloat("_Mode", 1f);
                    m.SetOverrideTag("RenderType", "TransparentCutout");
                }
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static AnimatorStateTransition Link(AnimatorState a, AnimatorState b, float duration, bool exitTime, float exitAt = 1f)
        {
            var t = a.AddTransition(b);
            t.duration = duration;
            t.hasFixedDuration = true;
            t.hasExitTime = exitTime;
            t.exitTime = exitAt;
            return t;
        }

        static AnimatorController BuildController(string path, string fbx)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
            AnimationClip C(string n) { clips.TryGetValue(n, out var c); if (!c) Debug.LogWarning("[Cubie] Missing clip " + n); return c; }

            AssetDatabase.DeleteAsset(path);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Smash", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Blink", AnimatorControllerParameterType.Trigger);
            var ps = ac.parameters;
            foreach (var p in ps) if (p.name == "Grounded") p.defaultBool = true;
            ac.parameters = ps;

            var sm = ac.layers[0].stateMachine;
            var idle = sm.AddState("Idle", new Vector3(300, 0));
            idle.motion = C("Idle");
            var run = sm.AddState("Run", new Vector3(300, 120)); run.motion = C("Run_InPlace_v2");
            var runV1 = sm.AddState("Run_Bouncy", new Vector3(560, 120)); runV1.motion = C("Run_InPlace");
            var jumpStart = sm.AddState("Jump_Start", new Vector3(0, 240)); jumpStart.motion = C("Jump_Start");
            var jumpAir = sm.AddState("Jump_Air", new Vector3(250, 240)); jumpAir.motion = C("Jump_Air");
            var land = sm.AddState("Land", new Vector3(500, 240)); land.motion = C("Land");
            var smash = sm.AddState("Smash", new Vector3(0, 360)); smash.motion = C("Smash");
            sm.defaultState = idle;

            Link(idle, run, 0.15f, false).AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            Link(run, idle, 0.2f, false).AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

            var toJump = sm.AddAnyStateTransition(jumpStart);
            toJump.AddCondition(AnimatorConditionMode.If, 0, "Jump");
            toJump.duration = 0.05f; toJump.hasFixedDuration = true; toJump.canTransitionToSelf = false;
            Link(jumpStart, jumpAir, 0.08f, true);
            Link(jumpAir, land, 0.03f, false).AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            Link(land, run, 0.1f, true, 0.85f).AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            Link(land, idle, 0.15f, true, 0.9f).AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

            var toSmash = sm.AddAnyStateTransition(smash);
            toSmash.AddCondition(AnimatorConditionMode.If, 0, "Smash");
            toSmash.duration = 0.05f; toSmash.hasFixedDuration = true; toSmash.canTransitionToSelf = false;
            Link(smash, run, 0.1f, true, 0.9f).AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            Link(smash, idle, 0.15f, true, 0.95f).AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

            // Additive blink layer: plays over any base clip.
            var blinkSm = new AnimatorStateMachine { name = "Blink", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(blinkSm, ac);
            ac.AddLayer(new AnimatorControllerLayer
            {
                name = "Blink",
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Additive,
                stateMachine = blinkSm
            });
            var empty = blinkSm.AddState("Empty", new Vector3(300, 0));
            var blink = blinkSm.AddState("Blink", new Vector3(300, 120)); blink.motion = C("Blink");
            blinkSm.defaultState = empty;
            Link(empty, blink, 0f, false).AddCondition(AnimatorConditionMode.If, 0, "Blink");
            Link(blink, empty, 0f, true);

            EditorUtility.SetDirty(ac);
            return ac;
        }

        static void BuildPrefab(string fbx, AnimatorController ctrl, string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = "CubieCharacter";
            var anim = go.GetComponent<Animator>();
            if (!anim) anim = go.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            if (!go.GetComponent<CubieFurPhysics>()) go.AddComponent<CubieFurPhysics>();
            if (!go.GetComponent<CubieFace>()) go.AddComponent<CubieFace>();
            if (!go.GetComponent<CubieColors>()) go.AddComponent<CubieColors>();
            var blinker = go.GetComponent<CubieBlinker>();
            if (!blinker) blinker = go.AddComponent<CubieBlinker>();
            blinker.animator = anim;
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }
    }
}
