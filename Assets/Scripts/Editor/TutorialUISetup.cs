using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TutorialUISetup
{
    [MenuItem("Tools/Build Tutorial Split UI")]
    static void Build()
    {
        var existing = Object.FindFirstObjectByType<TutorialUIController>(FindObjectsInactive.Include);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
        var ui = TutorialUIController.Build();
        Undo.RegisterCreatedObjectUndo(ui.gameObject, "Build Tutorial Split UI");
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        Selection.activeGameObject = ui.gameObject;
    }
}
