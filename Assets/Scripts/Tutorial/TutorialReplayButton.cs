using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// A small "?" on the game-over panel of a scene that runs the tutorial: clears TutorialCompleted and reloads the
/// scene, so the tutorial plays again. Added at run time by TutorialManager's boot (nothing to add to the scene);
/// RunnerHUD's tap-anywhere restart leaves it alone (see Contains).
/// </summary>
public class TutorialReplayButton : MonoBehaviour
{
    static RectTransform live;

    /// <summary>True if a screen point is on the "?" (RunnerHUD then does not restart the run for that tap).</summary>
    public static bool Contains(Vector2 screenPos) =>
        live != null && live.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(live, screenPos, null);

    bool built;

    void Update()
    {
        if (built) { enabled = false; return; }
        // RunnerHUD builds its canvas in Start; the panel itself is inactive until a run ends.
        GameObject canvas = GameObject.Find("RunnerHUDCanvas");
        Transform panel = canvas != null ? canvas.transform.Find("GameOverPanel") : null;
        if (panel == null) return;
        Build(panel);
        built = true;
    }

    void Build(Transform panel)
    {
        TutorialStyle st = TutorialStyle.Active;
        var go = new GameObject("TutorialReplayButton", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(panel, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(40f, -60f);
        rt.sizeDelta = new Vector2(100f, 100f);

        var img = go.GetComponent<Image>();
        var mat = new Material(st.SdfUI) { name = "TutorialReplayRing" };
        mat.SetFloat("_Shape", 1f);
        mat.SetFloat("_Stroke", 4f);
        mat.SetFloat("_Softness", 1.2f);
        mat.SetVector("_Size", new Vector4(100f, 100f, 0f, 0f));
        img.material = mat;
        img.color = new Color(st.neutralUI.r, st.neutralUI.g, st.neutralUI.b, 0.8f);

        var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.transform.SetParent(go.transform, false);
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 60;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(st.neutralUI.r, st.neutralUI.g, st.neutralUI.b, 0.85f);
        label.text = "?";
        label.raycastTarget = false;

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(Replay);
        live = rt;
    }

    static void Replay()
    {
        // Plays the tutorial once; the saved "completed" flag stays set (see TutorialManager.ReplayOnce).
        TutorialManager.ReplayOnce = true;
        TimeScaleMixer.ResetAll();   // hit-stop / pause / slow-mo factors left over from the run that ended
        GameLaunchRequest.Set(1);    // explicit request: checkpoint 1 = the run start, whatever checkpoint the ended run was on
        Scene s = SceneManager.GetActiveScene();
        // Same scene again, from the run start (never a checkpoint start: the tutorial runs from the start).
        if (s.buildIndex >= 0 && s.buildIndex < SceneManager.sceneCountInBuildSettings) SceneManager.LoadScene(s.buildIndex);
#if UNITY_EDITOR
        else UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(s.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        else SceneManager.LoadScene(s.name);
#endif
    }

    void OnDestroy() { if (live != null && live.IsChildOf(transform)) live = null; }
}
