using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Checkpoints > Add Base Totem To Shop.
/// Scenes 3.10 / 3.11 run the yellow eye-block totem (PlayerTotem + cape) as their player, but the shop only drew a plain
/// cube for the default skin. This copies the scene's own PlayerTotem into a prefab (Skins/TotemCharacter.prefab) and gives it
/// to the default skin ("classic" - the id is kept, so saves and ownership are untouched) as its shop character, flagged
/// CubeSkin.sceneCharacter so a run keeps using the scene's own totem (look transitions, cape physics) instead of spawning a copy.
/// Re-runnable. Reads the totem from the open 3.10 / 3.11 scene, or opens one additively for the copy and closes it again.
/// </summary>
public static class TotemSkinBuilder
{
    const string PrefabPath = "Assets/Checkpoints/Shop/Skins/TotemCharacter.prefab";
    const string SkinPath = "Assets/Checkpoints/Shop/Skins/Skin_classic.asset";
    static readonly string[] Scenes = { "Assets/MainScene prop camera 3.10.unity", "Assets/MainScene prop camera 3.11.unity" };

    [MenuItem("Tools/Checkpoints/Add Base Totem To Shop")]
    public static void Build()
    {
        PlayerTotem source = null;
        Scene opened = default;
        bool weOpened = false;

        // an already open scene wins (no scene change at all)
        for (int i = 0; i < SceneManager.sceneCount && source == null; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (!s.isLoaded || System.Array.IndexOf(Scenes, s.path) < 0) continue;
            source = FindTotem(s);
        }
        if (source == null)
            foreach (string path in Scenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) continue;
                opened = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                weOpened = true;
                source = FindTotem(opened);
                if (source != null) break;
                EditorSceneManager.CloseScene(opened, true);
                weOpened = false;
            }
        if (source == null) { Debug.LogError("[TotemSkin] no PlayerTotem found in scenes 3.10 / 3.11."); return; }

        GameObject copy = Object.Instantiate(source.gameObject);
        copy.name = "TotemCharacter";
        copy.SetActive(true);
        copy.transform.SetParent(null, false);
        copy.transform.position = Vector3.zero;
        copy.transform.rotation = Quaternion.identity;
        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Rigidbody rb in copy.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(copy, PrefabPath);
        Object.DestroyImmediate(copy);
        if (weOpened) EditorSceneManager.CloseScene(opened, true);

        var skin = AssetDatabase.LoadAssetAtPath<CubeSkin>(SkinPath);
        if (skin == null) { Debug.LogError("[TotemSkin] " + SkinPath + " missing - run Tools > Checkpoints > Build Skins first."); return; }
        skin.characterPrefab = prefab;
        skin.sceneCharacter = true;
        skin.keepSceneLook = true;
        skin.displayName = "Totem";
        skin.tagline = "The original runner.";
        EditorUtility.SetDirty(skin);
        AssetDatabase.SaveAssets();
        Debug.Log("[TotemSkin] default skin now shows the scene's totem in the shop (" + PrefabPath + ").");
    }

    static PlayerTotem FindTotem(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            PlayerTotem t = root.GetComponentInChildren<PlayerTotem>(true);
            if (t != null) return t;
        }
        return null;
    }
}
