using UnityEngine;

/// <summary>Hidden, scene-independent object that writes the save at the end of a frame with changes and on pause/quit.</summary>
[DefaultExecutionOrder(10000)]
public class SaveSystemHost : MonoBehaviour
{
    static SaveSystemHost instance;

    public static void Ensure()
    {
        if (instance != null) return;
        var go = new GameObject("SaveSystem");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SaveSystemHost>();
    }

    void LateUpdate()
    {
        if (SaveSystem.IsDirty) SaveSystem.Flush();
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) SaveSystem.NotifySuspend();
    }

    void OnApplicationQuit() => SaveSystem.NotifySuspend();

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
