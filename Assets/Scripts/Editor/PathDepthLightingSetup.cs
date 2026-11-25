using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// One-click lighting pass for reading depth in the orthographic rail scene, with a backup and
/// a restore.
///
/// Apply:
///   1. Backs up the active scene's lighting and environment - RenderSettings, every light, and
///      the renderer state this changes - to Assets/LightingBackup/&lt;Scene&gt;_lighting.json, and
///      saves a full copy of the scene next to it. An existing backup is never overwritten, so
///      applying twice cannot replace the original with an already-modified state.
///   2. Leaves ONE directional light casting crisp, hard, full-strength shadows and switches the
///      other directional lights off. The cool fill they gave moves into the ambient light.
///   3. Adds Screen Space Ambient Occlusion to the active URP renderer. URP does SSAO as a
///      Renderer Feature, not a Volume override - and the scene's PostProcessVolume is the legacy
///      Post-Processing v2 package, which URP does not run at all.
///   4. Adds PathDepthShading next to DynamicPathGenerator for the route colour gradient.
///
/// Restore puts every backed-up value back and removes what Apply added.
/// </summary>
public static class PathDepthLightingSetup
{
    const string BackupFolder = "Assets/LightingBackup";
    const string FeatureName = "PathDepth SSAO";
    const string FadeMaterialPath = "Assets/Materials/PathCube_Fade.mat";

    // Shadows are a light hint of depth, not the main read: dark, hard shadows criss-crossing an
    // orthographic view make it harder to tell which beam is which. The route colour and reveal
    // in PathDepthShading do the heavy lifting.
    const float KeyShadowStrength = 0.4f;

    [Serializable]
    class LightState
    {
        public string path;
        public bool gameObjectActive;
        public bool enabled;
        public LightType type;
        public Color color;
        public float intensity;
        public LightShadows shadows;
        public float shadowStrength;
        public Vector3 position;
        public Quaternion rotation;
    }

    [Serializable]
    class Backup
    {
        public string scenePath;
        public string sceneCopyPath;
        public string createdAt;

        public AmbientMode ambientMode;
        public Color ambientSkyColor;
        public Color ambientEquatorColor;
        public Color ambientGroundColor;
        public Color ambientLight;
        public float ambientIntensity;

        public bool fog;
        public Color fogColor;
        public FogMode fogMode;
        public float fogDensity;
        public float fogStartDistance;
        public float fogEndDistance;
        public float reflectionIntensity;

        public string sunPath;
        public List<LightState> lights = new List<LightState>();

        public string rendererDataPath;
        public bool addedSsao;
        public string shadingHostPath;
        public bool addedShading;
        public string createdFadeMaterialPath;
    }

    // ------------------------------------------------------------------
    // Apply
    // ------------------------------------------------------------------

    [MenuItem("Tools/Path Depth Lighting/Apply (backs up first)")]
    static void Apply()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Path Depth Lighting", "Exit Play mode first - changes made in Play mode are thrown away.", "OK");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
        {
            EditorUtility.DisplayDialog("Path Depth Lighting", "Save the scene once before applying, so it has a path to back up.", "OK");
            return;
        }

        string jsonPath = JsonPathFor(scene);
        bool haveBackup = File.Exists(jsonPath);
        Backup backup = haveBackup ? JsonUtility.FromJson<Backup>(File.ReadAllText(jsonPath)) : Capture(scene);

        // 1. Backup - only ever from the untouched state.
        if (!haveBackup)
        {
            EnsureFolder();
            backup.sceneCopyPath = $"{BackupFolder}/{scene.name}_before_depth_lighting.unity";
            EditorSceneManager.SaveScene(scene, backup.sceneCopyPath, true);
        }

        // 2. One directional light, crisp shadows.
        Light key = ApplySingleKeyLight(scene);

        // Ambient takes over the cool fill the extra directional lights gave: cooler and a touch
        // brighter from above, darker from below, so upper faces separate from lower ones.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.12f, 0.13f, 0.22f);
        RenderSettings.ambientEquatorColor = new Color(0.09f, 0.10f, 0.15f);
        RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.04f);

        // 3. SSAO on the active renderer.
        ScriptableRendererData rendererData = ActiveRendererData();
        if (rendererData != null)
        {
            backup.rendererDataPath = AssetDatabase.GetAssetPath(rendererData);
            if (AddSsao(rendererData)) backup.addedSsao = true;
        }
        else
        {
            Debug.LogWarning("Path Depth Lighting: no active URP renderer found, SSAO was not added.");
        }

        // 4. Route colour, reveal and fade.
        DynamicPathGenerator gen = Object.FindFirstObjectByType<DynamicPathGenerator>(FindObjectsInactive.Include);
        if (gen != null)
        {
            PathDepthShading shading = gen.GetComponent<PathDepthShading>();
            if (shading == null)
            {
                shading = Undo.AddComponent<PathDepthShading>(gen.gameObject);
                backup.shadingHostPath = PathOf(gen.transform);
                backup.addedShading = true;
            }

            Undo.RecordObject(shading, "Path Depth Lighting");
            shading.pathGen = gen;
            if (shading.fadeMaterial == null)
            {
                shading.fadeMaterial = GetOrCreateFadeMaterial(gen, out bool created);
                if (created) backup.createdFadeMaterialPath = FadeMaterialPath;
            }
        }

        EnsureFolder();
        File.WriteAllText(jsonPath, JsonUtility.ToJson(backup, true));
        AssetDatabase.ImportAsset(jsonPath);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"Path Depth Lighting applied. Key light: {(key != null ? key.name : "none")}. " +
                  $"Backup: {jsonPath} and {backup.sceneCopyPath}. Undo with Tools > Path Depth Lighting > Restore Backup.");
    }

    static Light ApplySingleKeyLight(Scene scene)
    {
        var directional = new List<Light>();
        foreach (Light l in SceneLights(scene))
            if (l.type == LightType.Directional) directional.Add(l);

        if (directional.Count == 0) return null;

        Light key = RenderSettings.sun != null && directional.Contains(RenderSettings.sun) ? RenderSettings.sun : null;
        if (key == null)
        {
            foreach (Light l in directional)
            {
                bool on = l.enabled && l.gameObject.activeInHierarchy;
                bool keyOn = key != null && key.enabled && key.gameObject.activeInHierarchy;
                if (key == null || (on && !keyOn) || (on == keyOn && l.intensity > key.intensity)) key = l;
            }
        }

        foreach (Light l in directional)
        {
            Undo.RecordObject(l, "Path Depth Lighting");
            if (l != key) l.enabled = false;
        }

        Undo.RecordObject(key.gameObject, "Path Depth Lighting");
        key.gameObject.SetActive(true);
        key.enabled = true;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = KeyShadowStrength;

        RenderSettings.sun = key;
        return key;
    }

    /// <summary>
    /// Adds this tool's SSAO feature if the renderer has no SSAO yet, and (re)tunes it every
    /// time Apply runs, so settings changed here reach a renderer set up by an earlier Apply.
    /// An SSAO feature someone else added is left exactly as it is. True if one was added.
    /// </summary>
    static bool AddSsao(ScriptableRendererData rendererData)
    {
        var so = new SerializedObject(rendererData);
        SerializedProperty features = so.FindProperty("m_RendererFeatures");
        SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
        if (features == null || map == null) return false;

        ScreenSpaceAmbientOcclusion ssao = null;
        for (int i = 0; i < features.arraySize; i++)
        {
            if (features.GetArrayElementAtIndex(i).objectReferenceValue is ScreenSpaceAmbientOcclusion existing)
            {
                if (existing.name != FeatureName)
                {
                    Debug.Log($"Path Depth Lighting: renderer already has SSAO ('{existing.name}'), leaving it as it is.");
                    return false;
                }
                ssao = existing;
            }
        }

        bool added = false;
        if (ssao == null)
        {
            ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ssao.name = FeatureName;
            AssetDatabase.AddObjectToAsset(ssao, rendererData);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out string _, out long localId);

            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = ssao;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedProperties();
            added = true;
        }

        // A soft contact darkening where beams meet, not a dark outline: a radius around a third
        // of a 1-unit beam, modest intensity, a little bleed into direct light so the crevice
        // still reads on the lit side, and the falloff pushed past the long orthographic boom.
        var s = new SerializedObject(ssao);
        SetFloat(s, "m_Settings.Intensity", 1.5f);
        SetFloat(s, "m_Settings.Radius", 0.3f);
        SetFloat(s, "m_Settings.DirectLightingStrength", 0.35f);
        SetFloat(s, "m_Settings.Falloff", 1000f);
        s.ApplyModifiedProperties();

        rendererData.SetDirty();
        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();
        return added;
    }

    /// <summary>
    /// A transparent copy of the path cube material, saved as an asset. Being referenced from
    /// the scene is what keeps URP's transparent shader variant in a build - a material made
    /// only at runtime can find it stripped.
    /// </summary>
    static Material GetOrCreateFadeMaterial(DynamicPathGenerator gen, out bool created)
    {
        created = false;
        var existing = AssetDatabase.LoadAssetAtPath<Material>(FadeMaterialPath);
        if (existing != null) return existing;

        GameObject source = gen.cubePrefab != null ? gen.cubePrefab : (gen.firstCube != null ? gen.firstCube.gameObject : null);
        Renderer r = source != null ? source.GetComponent<Renderer>() : null;
        if (r == null || r.sharedMaterial == null)
        {
            Debug.LogWarning("Path Depth Lighting: could not find the path cube material, the fade material will be made at runtime.");
            return null;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");

        var fade = new Material(r.sharedMaterial) { name = "PathCube_Fade" };
        PathDepthShading.ConfigureAsFade(fade);
        AssetDatabase.CreateAsset(fade, FadeMaterialPath);
        AssetDatabase.SaveAssets();
        created = true;
        return fade;
    }

    // ------------------------------------------------------------------
    // Restore
    // ------------------------------------------------------------------

    [MenuItem("Tools/Path Depth Lighting/Restore Backup")]
    static void Restore()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Path Depth Lighting", "Exit Play mode first.", "OK");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        string jsonPath = JsonPathFor(scene);
        if (!File.Exists(jsonPath))
        {
            EditorUtility.DisplayDialog("Path Depth Lighting", $"No backup found at {jsonPath}.", "OK");
            return;
        }

        Backup b = JsonUtility.FromJson<Backup>(File.ReadAllText(jsonPath));

        RenderSettings.ambientMode = b.ambientMode;
        RenderSettings.ambientSkyColor = b.ambientSkyColor;
        RenderSettings.ambientEquatorColor = b.ambientEquatorColor;
        RenderSettings.ambientGroundColor = b.ambientGroundColor;
        RenderSettings.ambientLight = b.ambientLight;
        RenderSettings.ambientIntensity = b.ambientIntensity;
        RenderSettings.fog = b.fog;
        RenderSettings.fogColor = b.fogColor;
        RenderSettings.fogMode = b.fogMode;
        RenderSettings.fogDensity = b.fogDensity;
        RenderSettings.fogStartDistance = b.fogStartDistance;
        RenderSettings.fogEndDistance = b.fogEndDistance;
        RenderSettings.reflectionIntensity = b.reflectionIntensity;

        foreach (LightState st in b.lights)
        {
            GameObject go = FindByPath(scene, st.path);
            Light l = go != null ? go.GetComponent<Light>() : null;
            if (l == null) { Debug.LogWarning($"Path Depth Lighting: light '{st.path}' no longer exists, skipped."); continue; }

            Undo.RecordObject(go, "Restore lighting");
            Undo.RecordObject(go.transform, "Restore lighting");
            Undo.RecordObject(l, "Restore lighting");
            go.SetActive(st.gameObjectActive);
            l.enabled = st.enabled;
            l.type = st.type;
            l.color = st.color;
            l.intensity = st.intensity;
            l.shadows = st.shadows;
            l.shadowStrength = st.shadowStrength;
            go.transform.SetPositionAndRotation(st.position, st.rotation);
        }

        GameObject sunGo = string.IsNullOrEmpty(b.sunPath) ? null : FindByPath(scene, b.sunPath);
        RenderSettings.sun = sunGo != null ? sunGo.GetComponent<Light>() : null;

        if (b.addedSsao && !string.IsNullOrEmpty(b.rendererDataPath))
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(b.rendererDataPath);
            if (data != null) RemoveSsao(data);
        }

        if (b.addedShading && !string.IsNullOrEmpty(b.shadingHostPath))
        {
            GameObject host = FindByPath(scene, b.shadingHostPath);
            PathDepthShading shading = host != null ? host.GetComponent<PathDepthShading>() : null;
            if (shading != null) Undo.DestroyObjectImmediate(shading);
        }

        if (!string.IsNullOrEmpty(b.createdFadeMaterialPath) &&
            AssetDatabase.LoadAssetAtPath<Material>(b.createdFadeMaterialPath) != null)
        {
            AssetDatabase.DeleteAsset(b.createdFadeMaterialPath);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"Path Depth Lighting: restored the lighting backed up on {b.createdAt} from {jsonPath}. " +
                  $"A full copy of the scene from before the change is also kept at {b.sceneCopyPath}.");
    }

    static void RemoveSsao(ScriptableRendererData rendererData)
    {
        var so = new SerializedObject(rendererData);
        SerializedProperty features = so.FindProperty("m_RendererFeatures");
        SerializedProperty map = so.FindProperty("m_RendererFeatureMap");

        for (int i = features.arraySize - 1; i >= 0; i--)
        {
            Object feature = features.GetArrayElementAtIndex(i).objectReferenceValue;
            if (!(feature is ScreenSpaceAmbientOcclusion) || feature.name != FeatureName) continue;

            // Clear the reference first, or DeleteArrayElementAtIndex only nulls the slot.
            features.GetArrayElementAtIndex(i).objectReferenceValue = null;
            features.DeleteArrayElementAtIndex(i);
            if (i < map.arraySize) map.DeleteArrayElementAtIndex(i);
            so.ApplyModifiedProperties();

            AssetDatabase.RemoveObjectFromAsset(feature);
            Object.DestroyImmediate(feature, true);
        }

        rendererData.SetDirty();
        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    static Backup Capture(Scene scene)
    {
        var b = new Backup
        {
            scenePath = scene.path,
            createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),

            ambientMode = RenderSettings.ambientMode,
            ambientSkyColor = RenderSettings.ambientSkyColor,
            ambientEquatorColor = RenderSettings.ambientEquatorColor,
            ambientGroundColor = RenderSettings.ambientGroundColor,
            ambientLight = RenderSettings.ambientLight,
            ambientIntensity = RenderSettings.ambientIntensity,

            fog = RenderSettings.fog,
            fogColor = RenderSettings.fogColor,
            fogMode = RenderSettings.fogMode,
            fogDensity = RenderSettings.fogDensity,
            fogStartDistance = RenderSettings.fogStartDistance,
            fogEndDistance = RenderSettings.fogEndDistance,
            reflectionIntensity = RenderSettings.reflectionIntensity,

            sunPath = RenderSettings.sun != null ? PathOf(RenderSettings.sun.transform) : null,
        };

        foreach (Light l in SceneLights(scene))
        {
            b.lights.Add(new LightState
            {
                path = PathOf(l.transform),
                gameObjectActive = l.gameObject.activeSelf,
                enabled = l.enabled,
                type = l.type,
                color = l.color,
                intensity = l.intensity,
                shadows = l.shadows,
                shadowStrength = l.shadowStrength,
                position = l.transform.position,
                rotation = l.transform.rotation,
            });
        }

        return b;
    }

    static ScriptableRendererData ActiveRendererData()
    {
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null) return null;

        var so = new SerializedObject(urp);
        SerializedProperty list = so.FindProperty("m_RendererDataList");
        SerializedProperty index = so.FindProperty("m_DefaultRendererIndex");
        if (list == null || list.arraySize == 0) return null;

        int i = index != null ? Mathf.Clamp(index.intValue, 0, list.arraySize - 1) : 0;
        return list.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
    }

    static IEnumerable<Light> SceneLights(Scene scene)
    {
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (l.gameObject.scene == scene) yield return l;
    }

    static void SetFloat(SerializedObject so, string path, float value)
    {
        SerializedProperty p = so.FindProperty(path);
        if (p != null) p.floatValue = value;
        else Debug.LogWarning($"Path Depth Lighting: SSAO setting '{path}' not found in this URP version, left at its default.");
    }

    static string JsonPathFor(Scene scene) => $"{BackupFolder}/{scene.name}_lighting.json";

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(BackupFolder))
            AssetDatabase.CreateFolder("Assets", "LightingBackup");
    }

    static string PathOf(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    static GameObject FindByPath(Scene scene, string path)
    {
        string[] parts = path.Split('/');
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;

            Transform t = root.transform;
            for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
            if (t != null) return t.gameObject;
        }
        return null;
    }
}
