using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Checkpoints: builds (or refreshes) the ten CheckpointProfile assets and CheckpointSet_3_6, and wires the set
/// into MainScene prop camera 3.6's LookTransitionDirector. Only 3.6 is opened/saved; 3.5 is never touched.
/// Re-running keeps hand edits to fields it does not own (it only fills empty ones, except the leg index).
/// </summary>
public static class CheckpointSetup
{
    public const string Root = "Assets/Checkpoints";
    public const string ProfilesDir = Root + "/Profiles";
    public const string SetPath = Root + "/CheckpointSet_3_6.asset";
    public const string ScenePath = "Assets/MainScene prop camera 3.6.unity";
    const string LooksDir = "Assets/PropCameraLooks/LookTransition/Looks";

    struct Spec
    {
        public string name, look, label;
        public bool keepObstacle;
        public MenuCameraPreset shot;
    }

    static MenuCameraPreset Shot(MenuShotMotion m, Vector3 offset, float pitch, float yaw, float fov, float speed, float amp) =>
        new MenuCameraPreset { motion = m, positionOffset = offset, angles = new Vector2(pitch, yaw), fov = fov, motionSpeed = speed, motionAmplitude = amp };

    // Checkpoint 1-4 are real (3.6: portal 1 -> scene 1, portal 2 -> scene 4, portal 3 -> scene 6); 5-10 are placeholders.
    static Spec[] Specs(string baseLook) => new[]
    {
        new Spec { name = "Start", look = baseLook, keepObstacle = true,
                   shot = Shot(MenuShotMotion.HighOrbit, new Vector3(0f, 13f, -15f), 40f, 45f, 42f, 0.025f, 35f) },
        new Spec { name = "Crimson", look = "Look_MainScene prop camera 1", keepObstacle = true,
                   shot = Shot(MenuShotMotion.LowSideTrack, new Vector3(0f, 3.5f, -12.5f), 14f, 20f, 46f, 0.035f, 5f) },
        new Spec { name = "Raspberry", look = "Look_MainScene prop camera 4", keepObstacle = true,
                   shot = Shot(MenuShotMotion.TopDownPushIn, new Vector3(0f, 21f, -5f), 72f, 15f, 40f, 0.04f, 18f) },
        new Spec { name = "Jade Moon Garden", look = "Look_MainScene prop camera 6", keepObstacle = false,
                   shot = Shot(MenuShotMotion.TelephotoPan, new Vector3(0f, 20f, -48f), 22f, 35f, 15f, 0.03f, 10f) },
        new Spec { shot = Shot(MenuShotMotion.HighOrbit, new Vector3(0f, 14f, -14f), 42f, -45f, 42f, 0.025f, 30f) },
        new Spec { shot = Shot(MenuShotMotion.LowSideTrack, new Vector3(0f, 3.5f, -12.5f), 14f, 200f, 46f, 0.035f, 5f) },
        new Spec { shot = Shot(MenuShotMotion.TopDownPushIn, new Vector3(0f, 21f, 5f), 70f, 195f, 40f, 0.04f, 18f) },
        new Spec { shot = Shot(MenuShotMotion.TelephotoPan, new Vector3(0f, 20f, 48f), 20f, 215f, 15f, 0.03f, 10f) },
        new Spec { shot = Shot(MenuShotMotion.HighOrbit, new Vector3(0f, 12f, -16f), 34f, 135f, 42f, 0.025f, 35f) },
        new Spec { shot = Shot(MenuShotMotion.LowSideTrack, new Vector3(0f, 3.5f, -12.5f), 14f, -60f, 46f, 0.035f, 5f) },
    };


    [MenuItem("Tools/Checkpoints/Build 3.6 Checkpoint Set")]
    public static void BuildMenu() => Build(true);

    /// <summary>Creates/refreshes the profiles and the set; with wireScene, also points 3.6's director at the set.</summary>
    /// <summary>Writes the default menu shots into the profiles (overwrites hand edits of menuCamera).</summary>
    public static void ApplyDefaultShots(CheckpointSet set)
    {
        Spec[] specs = Specs(null);
        foreach (CheckpointProfile p in set.checkpoints)
        {
            if (p == null || p.index < 1 || p.index > specs.Length) continue;
            p.menuCamera = specs[p.index - 1].shot;
            EditorUtility.SetDirty(p);
        }
        AssetDatabase.SaveAssets();
    }

    public static CheckpointSet Build(bool wireScene)
    {
        EnsureFolder(Root);
        EnsureFolder(ProfilesDir);

        // Checkpoint 1's look: 3.6's own (the director still reads it live in gameplay; this copy is for the menu).
        AtmosphereLook own = LookTransitionTools.CaptureToAsset(ScenePath);
        Spec[] specs = Specs(own != null ? own.name : null);

        CheckpointSet set = AssetDatabase.LoadAssetAtPath<CheckpointSet>(SetPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<CheckpointSet>();
            set.legsBetweenPortals = 50;
            AssetDatabase.CreateAsset(set, SetPath);
        }
        set.checkpoints.Clear();

        for (int i = 0; i < specs.Length; i++)
        {
            int index = i + 1;
            Spec s = specs[i];
            bool placeholder = string.IsNullOrEmpty(s.look);
            string file = ProfilesDir + "/CP" + index.ToString("00") + "_" + (placeholder ? "Placeholder" : s.name.Replace(" ", "")) + ".asset";
            CheckpointProfile p = FindProfile(index);
            bool fresh = p == null;
            if (fresh)
            {
                p = ScriptableObject.CreateInstance<CheckpointProfile>();
                AssetDatabase.CreateAsset(p, file);
            }
            p.index = index;
            p.legIndex = set.NominalLeg(index);
            if (fresh || string.IsNullOrEmpty(p.displayName)) p.displayName = placeholder ? "Checkpoint " + index : s.name;
            if (fresh || p.look == null)
                p.look = placeholder ? null : AssetDatabase.LoadAssetAtPath<AtmosphereLook>(LooksDir + "/" + s.look + ".asset");
            if (fresh)
            {
                p.keepObstacle = s.keepObstacle;
                p.takePlankColor = true;
                p.cameraCinematic = true;
                p.label = "";
                p.menuCamera = s.shot;
                p.lockedTeaseColor = p.look != null ? Opaque(p.look.doorGlowColor) : new Color(0.92f, 0.9f, 0.85f, 1f);
                if (p.look != null)
                    p.fog = new CheckpointFog { enabled = false, mode = FogMode.Exponential, color = p.look.cameraBackground, density = 0.01f };
            }
            if (!placeholder && p.look == null) Debug.LogError("[Checkpoints] look '" + s.look + "' not found for checkpoint " + index);
            EditorUtility.SetDirty(p);
            set.checkpoints.Add(p);
        }
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();

        if (wireScene) WireScene(set);
        Debug.Log("[Checkpoints] set built: " + set.checkpoints.Count + " profiles, " + set.legsBetweenPortals + " legs between portals.");
        return set;
    }

    static CheckpointProfile FindProfile(int index)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:CheckpointProfile", new[] { ProfilesDir }))
        {
            var p = AssetDatabase.LoadAssetAtPath<CheckpointProfile>(AssetDatabase.GUIDToAssetPath(guid));
            if (p != null && p.index == index) return p;
        }
        return null;
    }

    static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

    /// <summary>Points 3.6's LookTransitionDirector at the set (its Sequence asset is dropped: the set replaces it). Saves 3.6 only.</summary>
    static void WireScene(CheckpointSet set)
    {
        Scene sc = SceneManager.GetSceneByPath(ScenePath);
        bool wasOpen = sc.IsValid() && sc.isLoaded;
        if (!wasOpen) sc = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            LookTransitionDirector dir = null;
            foreach (GameObject r in sc.GetRootGameObjects())
            {
                dir = r.GetComponentInChildren<LookTransitionDirector>(true);
                if (dir != null) break;
            }
            if (dir == null) { Debug.LogError("[Checkpoints] no LookTransitionDirector in " + ScenePath); return; }
            Undo.RecordObject(dir, "Wire checkpoint set");
            dir.checkpoints = set;
            dir.sequence = null;
            EditorUtility.SetDirty(dir);
            EditorSceneManager.MarkSceneDirty(sc);
            EditorSceneManager.SaveScene(sc);
            Debug.Log("[Checkpoints] " + ScenePath + ": LookTransitionDirector.checkpoints = " + set.name);
        }
        finally
        {
            if (!wasOpen) EditorSceneManager.CloseScene(sc, true);
        }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
