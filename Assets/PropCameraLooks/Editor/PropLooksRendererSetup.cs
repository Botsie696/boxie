using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Builds the renderer the "MainScene prop camera 1/2/3" cameras use: a copy of
/// Default_Forward_Renderer with a Screen Space Ambient Occlusion feature added, registered as an
/// extra entry on every URP pipeline asset. Other scenes keep renderer 0 (Default_Forward_Renderer),
/// which is not changed, so they render exactly as before.
/// </summary>
public static class PropLooksRendererSetup
{
    public const string SourcePath = "Assets/URPDefaultResources/Default_Forward_Renderer.asset";
    public const string RendererPath = "Assets/PropCameraLooks/Settings/PropLooks_Renderer.asset";

    [MenuItem("Tools/Prop Camera Looks/Ensure SSAO Renderer")]
    public static void MenuEnsure() => Debug.Log("[PropLooks] renderer index " + EnsureRenderer());

    /// <summary>Creates/updates the renderer and returns its index in the pipeline assets' renderer list.</summary>
    public static int EnsureRenderer()
    {
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rd == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/PropCameraLooks/Settings"))
                AssetDatabase.CreateFolder("Assets/PropCameraLooks", "Settings");
            AssetDatabase.CopyAsset(SourcePath, RendererPath);
            AssetDatabase.ImportAsset(RendererPath);
            rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        }

        ScriptableRendererFeature ssao = null;
        foreach (var f in rd.rendererFeatures)
            if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") ssao = f;

        if (ssao == null)
        {
            var type = typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
            ssao = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
            ssao.name = "ScreenSpaceAmbientOcclusion";
            AssetDatabase.AddObjectToAsset(ssao, rd);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out string _, out long localId);

            var so = new SerializedObject(rd);
            var feats = so.FindProperty("m_RendererFeatures");
            feats.arraySize++;
            feats.GetArrayElementAtIndex(feats.arraySize - 1).objectReferenceValue = ssao;
            var map = so.FindProperty("m_RendererFeatureMap");
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        ConfigureSsao(ssao);
        EditorUtility.SetDirty(rd);

        // Register it on every pipeline asset (every quality level) so the camera index is valid everywhere.
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
            else if (index != at) Debug.LogWarning("[PropLooks] renderer sits at index " + at + " on " + asset.name + " but " + index + " elsewhere.");
        }
        AssetDatabase.SaveAssets();
        return index;
    }

    // Soft, wide contact shading for the inner corners of the architecture; tuned for the
    // orthographic camera (URP SSAO supports it) - a large radius in world units because the
    // cubes are big on screen, low direct-light strength so it only darkens creases.
    static void ConfigureSsao(ScriptableRendererFeature ssao)
    {
        var so = new SerializedObject(ssao);
        var s = so.FindProperty("m_Settings");
        void SetF(string n, float v) { var p = s.FindPropertyRelative(n); if (p != null) p.floatValue = v; }
        void SetB(string n, bool v) { var p = s.FindPropertyRelative(n); if (p != null) p.boolValue = v; }
        void SetE(string n, int v) { var p = s.FindPropertyRelative(n); if (p != null) p.enumValueIndex = v; }
        SetE("AOMethod", 1);           // Interleaved gradient noise (smoother than blue noise at low sample counts)
        SetB("Downsample", false);
        SetB("AfterOpaque", false);    // the look shaders sample it themselves
        SetE("Source", 1);             // DepthNormals: exact normals from the shaders' DepthNormals pass
        SetE("NormalSamples", 1);      // Medium
        SetF("Intensity", 1.1f);
        SetF("DirectLightingStrength", 0.25f);
        SetF("Radius", 0.9f);
        SetE("Samples", 1);            // Medium
        SetE("BlurQuality", 0);        // High (bilateral)
        SetF("Falloff", 120f);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ssao);
    }
}
