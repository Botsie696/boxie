using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// LookTransition editor tools (Tools > Look Transition).
///
///   Survey Scene              Logs every look value of the selected scene (or, from code, a side-by-side table of two).
///   Capture Look From Scene   Writes Looks/Look_[scene].asset and copies the scene's post-processing profile to Looks/Profiles.
///   Install In Open Scene     Sets up the open scene (MainScene prop camera 3.2 only) for a portal look change.
///
/// Source scenes are only READ: a scene that is not open is opened additively and closed again without saving, and the
/// tool checks that it was never marked dirty and that its file on disk is byte-for-byte unchanged.
/// </summary>
public static class LookTransitionTools
{
    const string Root = "Assets/PropCameraLooks/LookTransition";
    const string LooksDir = Root + "/Looks";
    const string ProfilesDir = LooksDir + "/Profiles";
    const string WorkingScene = "Assets/MainScene prop camera 3.2.unity";
    const string BaseScene = "Assets/MainScene prop camera 3.1.unity";

    // ------------------------------------------------------------------ menu

    [MenuItem("Tools/Look Transition/Survey Scene")]
    static void SurveySelected()
    {
        string path = SelectedScenePath();
        if (path == null) { Debug.LogWarning("[LookTransition] select a .unity scene in the Project window first."); return; }
        AtmosphereLook l = CaptureScene(path);
        var rows = new List<string>();
        l.Describe(rows);
        Debug.Log("[LookTransition] survey of " + path + "\n" + string.Join("\n", rows));
        Object.DestroyImmediate(l);
    }

    [MenuItem("Tools/Look Transition/Capture Look From Scene")]
    static void CaptureSelected()
    {
        string path = SelectedScenePath();
        if (path == null) { Debug.LogWarning("[LookTransition] select a .unity scene in the Project window first."); return; }
        CaptureToAsset(path);
    }

    [MenuItem("Tools/Look Transition/Install In Open Scene (3.2, portal 1 -> prop camera 1)")]
    static void InstallMenu() => Install("Assets/MainScene prop camera 1.unity", 1);

    static string SelectedScenePath()
    {
        string p = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
        return p != null && p.EndsWith(".unity") ? p : null;
    }

    // ------------------------------------------------------------------ survey

    /// <summary>A markdown table of every look value of two scenes; rows that differ are marked. Logged and returned.</summary>
    public static string Survey(string pathA, string pathB, bool differencesOnly = false)
    {
        AtmosphereLook a = CaptureScene(pathA), b = CaptureScene(pathB);
        var ra = new List<string>(); a.Describe(ra);
        var rb = new List<string>(); b.Describe(rb);
        var mapB = new Dictionary<string, string>();
        foreach (string r in rb) { Split(r, out string k, out string v); mapB[k] = v; }
        var sb = new StringBuilder();
        sb.AppendLine("| Section | Value | " + a.sourceScene + " | " + b.sourceScene + " | |");
        sb.AppendLine("|---|---|---|---|---|");
        var seen = new HashSet<string>();
        foreach (string r in ra)
        {
            Split(r, out string k, out string va);
            seen.Add(k);
            mapB.TryGetValue(k, out string vb);
            bool diff = va != vb;
            if (differencesOnly && !diff) continue;
            string[] kk = k.Split('|');
            sb.AppendLine("| " + kk[0].Trim() + " | " + kk[1].Trim() + " | " + va + " | " + (vb ?? "-") + " | " + (diff ? "≠" : "") + " |");
        }
        foreach (string r in rb)
        {
            Split(r, out string k, out string vb);
            if (seen.Contains(k)) continue;
            string[] kk = k.Split('|');
            sb.AppendLine("| " + kk[0].Trim() + " | " + kk[1].Trim() + " | - | " + vb + " | ≠ |");
        }
        Object.DestroyImmediate(a);
        Object.DestroyImmediate(b);
        string table = sb.ToString();
        Debug.Log("[LookTransition] survey " + pathA + " vs " + pathB + "\n" + table);
        return table;
    }

    static void Split(string row, out string key, out string value)
    {
        int i = row.LastIndexOf(" | ");
        key = i > 0 ? row.Substring(0, i) : row;
        value = i > 0 ? row.Substring(i + 3) : "";
    }

    // ------------------------------------------------------------------ capture

    /// <summary>Reads a scene's look into a new (unsaved) AtmosphereLook. The scene is opened additively if it is not open, then closed unsaved.</summary>
    public static AtmosphereLook CaptureScene(string scenePath)
    {
        string full = Path.GetFullPath(scenePath);
        string hashBefore = Hash(full);
        Scene open = SceneManager.GetSceneByPath(scenePath);
        bool wasOpen = open.IsValid() && open.isLoaded;
        Scene sc = wasOpen ? open : EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        bool dirtyBefore = sc.isDirty;

        AtmosphereLook look = ScriptableObject.CreateInstance<AtmosphereLook>();
        look.sourceScene = Path.GetFileNameWithoutExtension(scenePath);
        try
        {
            GameObject[] roots = sc.GetRootGameObjects();
            T Find<T>() where T : Component
            {
                foreach (GameObject r in roots) { T c = r.GetComponentInChildren<T>(true); if (c != null) return c; }
                return null;
            }
            var gen = Find<DynamicPathGenerator>();
            var zones = Find<PortalZoneController>();
            var shading = Find<PathDepthShading>();
            var runner = Find<CubeRailRunner>();
            var sky = Find<MonumentSkyBackdrop>();
            var rig = Find<CameraParentRotator>();
            Light sun = roots.SelectMany(r => r.GetComponentsInChildren<Light>(true)).FirstOrDefault(l => l.type == LightType.Directional);
            Volume vol = roots.SelectMany(r => r.GetComponentsInChildren<Volume>(true)).Where(v => v.isGlobal).OrderByDescending(v => v.priority).FirstOrDefault();
            Camera cam = rig != null && rig.boomCamera != null ? rig.boomCamera : roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).FirstOrDefault();

            Material block = zones != null && zones.blockMaterial != null ? zones.blockMaterial
                           : gen != null && gen.firstCube != null && gen.firstCube.GetComponent<Renderer>() != null ? gen.firstCube.GetComponent<Renderer>().sharedMaterial : null;
            look.Capture(new AtmosphereLook.Sources
            {
                block = block,
                blockFade = shading != null ? shading.fadeMaterial : null,
                player = RunnerMaterial(runner),
                obstacle = gen != null ? gen.obstaclePyramidMaterial : null,
                sky = sky != null ? sky.skyMaterial : null,
                chasm = Find<ChasmFog>(),
                sun = sun,
                camera = cam,
                volume = vol,
                shading = shading,
                zones = zones,
                trail = runner != null ? runner.GetComponent<RunnerTrailFX>() : null,
                faceHighlight = runner != null ? runner.GetComponent<ActiveFaceHighlight>() : null,
                arrows = Find<TurnFaceArrows>(),
                generator = gen,
            });
            ReadRenderSettingsYaml(full, look);
        }
        finally
        {
            bool dirtyAfter = sc.isDirty;
            if (!wasOpen) EditorSceneManager.CloseScene(sc, true);
            if (dirtyAfter && !dirtyBefore) Debug.LogError("[LookTransition] " + scenePath + " was marked dirty while it was read - it was closed WITHOUT saving.");
            string hashAfter = Hash(full);
            if (hashAfter != hashBefore) Debug.LogError("[LookTransition] " + scenePath + " changed on disk while it was read!");
        }
        return look;
    }

    static Material RunnerMaterial(CubeRailRunner runner)
    {
        if (runner == null) return null;
        var own = runner.GetComponent<MeshRenderer>();
        if (own != null && own.sharedMaterial != null) return own.sharedMaterial;
        foreach (MeshRenderer mr in runner.GetComponentsInChildren<MeshRenderer>(true))
            if (mr.sharedMaterial != null && mr.sharedMaterial.shader != null && mr.sharedMaterial.shader.name.Contains("FlatFaceBlock")) return mr.sharedMaterial;
        return null;
    }

    // RenderSettings belong to the active scene only, so a scene that is merely open alongside is read from its file.
    static void ReadRenderSettingsYaml(string fullPath, AtmosphereLook look)
    {
        string text = File.ReadAllText(fullPath);
        int i = text.IndexOf("\nRenderSettings:");
        if (i < 0) return;
        int end = text.IndexOf("\n--- ", i);
        string block = end > 0 ? text.Substring(i, end - i) : text.Substring(i);
        look.ambientSky = YamlColor(block, "m_AmbientSkyColor", look.ambientSky);
        look.ambientEquator = YamlColor(block, "m_AmbientEquatorColor", look.ambientEquator);
        look.ambientGround = YamlColor(block, "m_AmbientGroundColor", look.ambientGround);
        Match m = Regex.Match(block, @"m_AmbientMode: (\d+)");
        if (m.Success) look.ambientMode = int.Parse(m.Groups[1].Value);
    }

    static Color YamlColor(string block, string key, Color fallback)
    {
        Match m = Regex.Match(block, key + @": \{r: ([-\d.e]+), g: ([-\d.e]+), b: ([-\d.e]+), a: ([-\d.e]+)\}");
        if (!m.Success) return fallback;
        float F(int g) => float.Parse(m.Groups[g].Value, System.Globalization.CultureInfo.InvariantCulture);
        return new Color(F(1), F(2), F(3), F(4));
    }

    static string Hash(string fullPath)
    {
        using (var sha = SHA256.Create())
        using (var s = File.OpenRead(fullPath))
            return System.BitConverter.ToString(sha.ComputeHash(s));
    }

    /// <summary>Captures a scene's look into Looks/Look_[scene].asset (updated in place if it exists) with its post-processing profile copied to Looks/Profiles.</summary>
    public static AtmosphereLook CaptureToAsset(string scenePath)
    {
        EnsureFolder(LooksDir);
        EnsureFolder(ProfilesDir);
        AtmosphereLook look = CaptureScene(scenePath);
        string name = Path.GetFileNameWithoutExtension(scenePath);
        look.name = "Look_" + name;

        if (look.volumeProfile != null)
        {
            string src = AssetDatabase.GetAssetPath(look.volumeProfile);
            string dst = ProfilesDir + "/" + name + "_Volume.asset";
            if (src != dst)
            {
                if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(dst) != null) AssetDatabase.DeleteAsset(dst);   // our own copy only
                AssetDatabase.CopyAsset(src, dst);
                look.volumeProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(dst);
            }
        }

        string assetPath = LooksDir + "/Look_" + name + ".asset";
        AtmosphereLook existing = AssetDatabase.LoadAssetAtPath<AtmosphereLook>(assetPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(look, existing);
            existing.name = look.name;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(look);
            look = existing;
        }
        else AssetDatabase.CreateAsset(look, assetPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[LookTransition] captured " + scenePath + " -> " + assetPath);
        return look;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ------------------------------------------------------------------ install

    /// <summary>
    /// Sets up MainScene prop camera 3.2 (it must be the open, active scene): captures the base look (3.1) and the target look,
    /// writes the sequence entry for the portal, adds/updates the LookTransition director and points 3.2's ChasmFog at the
    /// LookTransition shader copies. Saves 3.2 only.
    /// </summary>
    public static void Install(string targetScenePath, int portal)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != WorkingScene)
        {
            Debug.LogError("[LookTransition] open " + WorkingScene + " first (active scene is " + scene.path + "). Nothing was changed.");
            return;
        }

        AtmosphereLook baseLook = CaptureToAsset(BaseScene);
        AtmosphereLook target = CaptureToAsset(targetScenePath);

        string seqPath = Root + "/Sequence_MainScenePropCamera3_2.asset";
        var seq = AssetDatabase.LoadAssetAtPath<CheckpointLookSequence>(seqPath);
        if (seq == null)
        {
            seq = ScriptableObject.CreateInstance<CheckpointLookSequence>();
            AssetDatabase.CreateAsset(seq, seqPath);
        }
        seq.baseLook = baseLook;
        seq.entries.RemoveAll(e => e == null || e.portal == portal);
        seq.entries.Add(new CheckpointLookSequence.Entry
        {
            portal = portal, look = target, label = "", cameraCinematic = true,
            keepObstacle = true, takePlankColor = true, overrideTiming = false,
            timing = CheckpointLookSequence.Timing.Default,
        });
        seq.entries.Sort((x, y) => x.portal.CompareTo(y.portal));
        EditorUtility.SetDirty(seq);
        AssetDatabase.SaveAssets();

        GameObject[] roots = scene.GetRootGameObjects();
        T Find<T>() where T : Component
        {
            foreach (GameObject r in roots) { T c = r.GetComponentInChildren<T>(true); if (c != null) return c; }
            return null;
        }

        var dir = Find<LookTransitionDirector>();
        if (dir == null)
        {
            var go = new GameObject("LookTransition");
            SceneManager.MoveGameObjectToScene(go, scene);
            dir = go.AddComponent<LookTransitionDirector>();
        }
        var gen = Find<DynamicPathGenerator>();
        var zones = Find<PortalZoneController>();
        var shading = Find<PathDepthShading>();
        var runner = Find<CubeRailRunner>();
        var sky = Find<MonumentSkyBackdrop>();
        var chasm = Find<ChasmFog>();
        var rig = Find<CameraParentRotator>();

        dir.sequence = seq;
        dir.blockWipeShader = Shader.Find("PropLooks/LookTransition/FlatFaceBlockWipe");
        dir.skyWipeShader = Shader.Find("PropLooks/LookTransition/RadialSkyWipe");
        dir.blockMaterial = zones != null ? zones.blockMaterial : null;
        dir.blockFadeMaterial = shading != null ? shading.fadeMaterial : null;
        dir.playerMaterial = RunnerMaterial(runner);
        dir.obstacleMaterial = gen != null ? gen.obstaclePyramidMaterial : null;
        dir.skyMaterial = sky != null ? sky.skyMaterial : null;
        dir.pathGen = gen; dir.runner = runner; dir.rig = rig; dir.zones = zones; dir.shading = shading; dir.chasm = chasm; dir.skyBackdrop = sky;
        dir.sun = roots.SelectMany(r => r.GetComponentsInChildren<Light>(true)).FirstOrDefault(l => l.type == LightType.Directional);
        dir.baseVolume = roots.SelectMany(r => r.GetComponentsInChildren<Volume>(true)).Where(v => v.isGlobal).OrderByDescending(v => v.priority).FirstOrDefault();
        if (runner != null)
        {
            dir.trailFx = runner.GetComponent<RunnerTrailFX>();
            dir.boostFx = runner.GetComponent<RunnerBoostFX>();
            dir.faceHighlight = runner.GetComponent<ActiveFaceHighlight>();
        }
        dir.arrows = Find<TurnFaceArrows>();
        EditorUtility.SetDirty(dir);

        if (chasm != null)
        {
            chasm.fogLitShader = Shader.Find("Custom/LookTransition/ChasmFogLitWipe");
            chasm.backdropShader = Shader.Find("Hidden/LookTransition/ChasmFogBackdropWipe");
            EditorUtility.SetDirty(chasm);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[LookTransition] installed in " + scene.path + ": portal " + portal + " -> " + target.name
                  + " (block " + (dir.blockMaterial ? dir.blockMaterial.name : "-") + ", fade " + (dir.blockFadeMaterial ? dir.blockFadeMaterial.name : "-")
                  + ", player " + (dir.playerMaterial ? dir.playerMaterial.name : "-") + ", obstacle " + (dir.obstacleMaterial ? dir.obstacleMaterial.name : "-")
                  + ", sky " + (dir.skyMaterial ? dir.skyMaterial.name : "-") + ", volume " + (dir.baseVolume ? dir.baseVolume.name : "-") + ")");
    }
}
