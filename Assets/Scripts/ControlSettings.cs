using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum ControlMode { TapDoubleTap = 0, Flick = 1 }

/// <summary>Persisted control-mode choice, read by CubeRailRunner in every level.</summary>
public static class ControlSettings
{
    const string Key = "ControlMode";

    public static ControlMode Mode
    {
        get => (ControlMode)PlayerPrefs.GetInt(Key, (int)ControlMode.TapDoubleTap);
        set { PlayerPrefs.SetInt(Key, (int)value); PlayerPrefs.Save(); }
    }
}

/// <summary>
/// Spawns itself on the MainUi scene and shows two checkboxes (radio-style) to pick the control mode.
/// No scene editing needed; the choice is saved and defaults to Tap / Double-tap.
/// </summary>
public class ControlModeSelector : MonoBehaviour
{
    const string MainScene = "MainUi";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnLoad()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Spawn(SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => Spawn(s);

    static void Spawn(Scene s)
    {
        if (s.name != MainScene || FindFirstObjectByType<ControlModeSelector>() != null) return;
        if (FindFirstObjectByType<MainMenuController>() != null) return;   // the checkpoint menu has the choice in its Settings
        new GameObject("ControlModeSelector").AddComponent<ControlModeSelector>();
    }

    void Start()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        var canvasGo = new GameObject("ControlModeCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(ToggleGroup));
        panel.transform.SetParent(canvasGo.transform, false);
        var pr = (RectTransform)panel.transform;
        pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0f);
        pr.anchoredPosition = new Vector2(0, 60);
        pr.sizeDelta = new Vector2(900, 300);
        panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.55f);
        var group = panel.GetComponent<ToggleGroup>();
        group.allowSwitchOff = false;

        MakeLabel(panel.transform, "CONTROLS", 36, new Vector2(0, 120), new Vector2(860, 50));
        var current = ControlSettings.Mode;
        MakeToggle(panel.transform, group, "Tap  (double-tap = flip to opposite side)",
            new Vector2(0, 40), current == ControlMode.TapDoubleTap, ControlMode.TapDoubleTap);
        MakeToggle(panel.transform, group, "Flick  (left / right / up-down = flip)",
            new Vector2(0, -60), current == ControlMode.Flick, ControlMode.Flick);
    }

    static Font UiFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    static Text MakeLabel(Transform parent, string text, int size, Vector2 pos, Vector2 sizeDelta)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
        var t = go.GetComponent<Text>();
        t.font = UiFont; t.text = text; t.fontSize = size; t.color = Color.white;
        t.alignment = TextAnchor.MiddleLeft;
        t.raycastTarget = false;
        return t;
    }

    static void MakeToggle(Transform parent, ToggleGroup group, string label, Vector2 pos, bool on, ControlMode mode)
    {
        var go = new GameObject("Toggle_" + mode, typeof(RectTransform), typeof(Toggle));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(860, 80);

        var box = new GameObject("Box", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(go.transform, false);
        var br = (RectTransform)box.transform;
        br.anchorMin = br.anchorMax = new Vector2(0, 0.5f);
        br.pivot = new Vector2(0, 0.5f);
        br.anchoredPosition = new Vector2(10, 0); br.sizeDelta = new Vector2(60, 60);
        var boxImg = box.GetComponent<Image>();
        boxImg.color = new Color(1, 1, 1, 0.9f);

        var check = new GameObject("Check", typeof(RectTransform), typeof(Image));
        check.transform.SetParent(box.transform, false);
        var cr = (RectTransform)check.transform;
        cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one;
        cr.offsetMin = new Vector2(10, 10); cr.offsetMax = new Vector2(-10, -10);
        var checkImg = check.GetComponent<Image>();
        checkImg.color = new Color(0.1f, 0.8f, 0.9f);

        var text = MakeLabel(go.transform, label, 32, new Vector2(40, 0), new Vector2(760, 80));

        var toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = boxImg;
        toggle.graphic = checkImg;
        toggle.group = group;
        toggle.SetIsOnWithoutNotify(on);
        toggle.onValueChanged.AddListener(v => { if (v) ControlSettings.Mode = mode; });
    }
}
