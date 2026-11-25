using UnityEngine;
using UnityEngine.UI;

/// <summary>Scene 3.11 only: a hairline bar flush with the top of the screen showing how far through the current level
/// the runner is (legs scored / legs in the level). Tinted by the HudThemer311 look. Hidden on the last level.</summary>
public class LevelProgressBar311 : MonoBehaviour
{
    public float height = 5f;
    DynamicPathGenerator gen;
    HudThemer311 theme;
    Image track, fill;
    float shown;

    void Start()
    {
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        theme = GetComponent<HudThemer311>();
        var go = new GameObject("LevelProgressCanvas", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var cv = go.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 40;
        track = NewImage(go.transform, "Track");
        fill = NewImage(track.transform, "Fill");
        var tr = track.rectTransform;
        tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f);
        tr.pivot = new Vector2(0.5f, 1f); tr.anchoredPosition = Vector2.zero; tr.sizeDelta = new Vector2(0f, height);
        var fr = fill.rectTransform;
        fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(0f, 1f);
        fr.pivot = new Vector2(0f, 0.5f); fr.anchoredPosition = Vector2.zero; fr.sizeDelta = Vector2.zero;
    }

    static Image NewImage(Transform parent, string n)
    {
        var g = new GameObject(n, typeof(RectTransform), typeof(Image));
        g.transform.SetParent(parent, false);
        var i = g.GetComponent<Image>();
        i.raycastTarget = false;
        return i;
    }

    void LateUpdate()
    {
        if (gen == null || track == null) return;
        int level = Mathf.Max(1, gen.PortalsTaken + 1);
        int a = level <= 1 ? 0 : gen.NominalCheckpointLeg(level - 1);
        int b = gen.NominalCheckpointLeg(level);
        bool show = b > a && level < 10 && gen.endlessLevel == 0;
        track.enabled = fill.enabled = show;
        if (!show) return;
        float t = Mathf.Clamp01((float)(gen.Score - a) / (b - a));
        shown = Mathf.MoveTowards(shown, t, Time.unscaledDeltaTime * 2f);
        if (t < shown) shown = t;
        Color c = theme != null && theme.Current != null ? theme.Current.rim : Color.white;
        Color bars = theme != null && theme.Current != null ? theme.Current.bars : Color.white;
        c.a = 1f;
        track.color = new Color(c.r, c.g, c.b, 0.22f);
        fill.color = new Color(bars.r, bars.g, bars.b, 0.9f);
        fill.rectTransform.anchorMax = new Vector2(shown, 1f);
    }
}
