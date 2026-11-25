using UnityEngine;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
/// <summary>
/// Debug aid: press P to toggle slow motion (SlowMoHotkey.slowScale, 0.1). Self-installs at load in every scene, so
/// nothing needs adding to a scene. Goes through TimeScaleMixer (its own Hotkey factor), so it stacks with hit-stop,
/// the shield dip, focus and DebugTimeScale. CubeRailRunner shortens flipRemapDelay by the same timeScale while it is on.
/// </summary>
public class SlowMoHotkey : MonoBehaviour
{
    public const float slowScale = 0.1f;
    public const KeyCode key = KeyCode.P;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (FindFirstObjectByType<SlowMoHotkey>() != null) return;
        var go = new GameObject("SlowMoHotkey");
        DontDestroyOnLoad(go);
        go.AddComponent<SlowMoHotkey>();
    }

    void Update()
    {
        if (!Input.GetKeyDown(key)) return;
        bool on = TimeScaleMixer.Get(TimeScaleMixer.Source.Hotkey) < 0.999f;
        TimeScaleMixer.Set(TimeScaleMixer.Source.Hotkey, on ? 1f : slowScale);
    }

    void OnDestroy() { TimeScaleMixer.Set(TimeScaleMixer.Source.Hotkey, 1f); }
}
#endif
