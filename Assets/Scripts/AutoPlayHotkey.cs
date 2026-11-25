#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// Editor-only debug key: press L in Play mode and the AutoPlayer bot takes the runner, steering
/// every corner by itself for as long as it stays on; press L again to take control back.
///
/// The whole file is inside UNITY_EDITOR, so builds never contain it. It installs itself when Play
/// starts (nothing is added to any scene), works in every scene with a CubeRailRunner, and stays on
/// across a scene restart until L is pressed again.
/// </summary>
public class AutoPlayHotkey : MonoBehaviour
{
    const KeyCode ToggleKey = KeyCode.L;

    static AutoPlayHotkey instance;

    bool autoPlay;
    AutoPlayer bot;
    GUIStyle labelStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var go = new GameObject("AutoPlayHotkey (editor only)");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<AutoPlayHotkey>();
    }

    /// <summary>Same as pressing L.</summary>
    public static void Toggle()
    {
        if (instance != null) instance.SetAutoPlay(!instance.autoPlay);
    }

    public static bool IsOn => instance != null && instance.autoPlay;

    void Update()
    {
        if (Input.GetKeyDown(ToggleKey)) SetAutoPlay(!autoPlay);

        // A restart reloads the scene and takes the bot with the old runner; hand the new one over too.
        if (autoPlay && bot == null) Attach();
    }

    void SetAutoPlay(bool on)
    {
        autoPlay = on;
        if (on) Attach();
        else if (bot != null) Destroy(bot);
        Debug.Log(on ? "[AutoPlay] ON - the bot is steering (L to stop)" : "[AutoPlay] OFF - you're steering");
    }

    void Attach()
    {
        var runner = FindFirstObjectByType<CubeRailRunner>();
        if (runner == null) return;
        bot = runner.GetComponent<AutoPlayer>();
        if (bot == null)
        {
            bot = runner.gameObject.AddComponent<AutoPlayer>();
            bot.report = false;
        }
    }

    void OnGUI()
    {
        if (!autoPlay) return;
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(Screen.height * 0.018f),
            };
            labelStyle.normal.textColor = new Color(0.55f, 1f, 0.95f);
        }
        float h = Screen.height * 0.035f;
        GUI.Label(new Rect(0f, Screen.height * 0.012f, Screen.width, h), "AUTO-PLAY  (L to stop)", labelStyle);
    }
}
#endif
