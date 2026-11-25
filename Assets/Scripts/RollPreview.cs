using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ScreenProjected pre-tap readability: a '&lt;' and a '&gt;' chevron on the two roll results at the runner's position and a faint glow
/// on the matching screen half. Yellow = the face the coming corner needs. Fades with correct play, always shown when stacked.
/// Drawn on a screen overlay (so a result on the far side of the track is never hidden by the track), pooled (created once),
/// unscaled time, never takes a touch, no per-frame allocation. No text.
/// </summary>
public class RollPreview : MonoBehaviour
{
    [System.NonSerialized] public CubeRailRunner runner;

    Image leftMark, rightMark, leftGlow, rightGlow;
    RectTransform leftRt, rightRt;
    Camera cam;
    static readonly Color White = new Color(1f, 1f, 1f, 1f);
    static readonly Color Yellow = new Color(1f, 0.85f, 0.2f, 1f);

    void Awake()
    {
        var canvasGO = new GameObject("RollPreviewOverlay", typeof(Canvas));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        var glow = MakeGradient();
        leftGlow = MakeGlow(canvasGO.transform, "L", glow, false);
        rightGlow = MakeGlow(canvasGO.transform, "R", glow, true);
        leftMark = MakeMark(canvasGO.transform, "RollMark_L", MakeChevron(false), out leftRt);
        rightMark = MakeMark(canvasGO.transform, "RollMark_R", MakeChevron(true), out rightRt);
        SetVisible(false);
    }

    static Image MakeMark(Transform parent, string name, Sprite s, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;   // positioned in screen pixels by LateUpdate
        rt.pivot = new Vector2(0.5f, 0.5f);
        var img = go.GetComponent<Image>();
        img.sprite = s;
        img.raycastTarget = false;
        img.color = new Color(1f, 1f, 1f, 0f);
        return img;
    }

    static Image MakeGlow(Transform parent, string name, Sprite s, bool right)
    {
        var go = new GameObject("Glow_" + name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(right ? 0.5f : 0f, 0f);
        rt.anchorMax = new Vector2(right ? 1f : 0.5f, 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        if (right) rt.localScale = new Vector3(-1f, 1f, 1f);   // the gradient is bright on its left edge: mirror it
        var img = go.GetComponent<Image>();
        img.sprite = s;
        img.raycastTarget = false;
        img.color = new Color(1f, 1f, 1f, 0f);
        return img;
    }

    // '>' (right) or '<' (left): two arms meeting at a point. Cornered, no round shapes.
    static Sprite MakeChevron(bool right)
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "RollChevron" };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = right ? x : n - 1 - x;
                float dy = Mathf.Abs(y - (n - 1) * 0.5f);
                float arm = Mathf.Abs(dy - (50f - u));
                float a = (u > 12f && u < 52f) ? Mathf.Clamp01(1f - (arm - 5f) / 3f) : 0f;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }

    static Sprite MakeGradient()
    {
        const int w = 64;
        var tex = new Texture2D(w, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "RollGlow" };
        var px = new Color32[w * 2];
        for (int x = 0; x < w; x++)
        {
            float a = Mathf.Pow(1f - x / (w - 1f), 2f);
            for (int y = 0; y < 2; y++) px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, w, 2), new Vector2(0.5f, 0.5f), 100f);
    }

    void SetVisible(bool on)
    {
        if (leftMark.enabled != on) { leftMark.enabled = on; rightMark.enabled = on; }
        if (leftGlow.enabled != on) { leftGlow.enabled = on; rightGlow.enabled = on; }
    }

    void LateUpdate()
    {
        if (runner == null || runner.currentCube == null || runner.IsGameOver || !runner.PreviewCandidates(out int rightFace, out int leftFace, out bool stacked))
        {
            SetVisible(false);
            return;
        }
        if (cam == null) cam = Camera.main;
        if (cam == null) { SetVisible(false); return; }
        SetVisible(true);

        // Fades as the player gets corners right; the stacked case is the ambiguous one, so it is always shown.
        float fade = Mathf.Lerp(1f, 0.06f, Mathf.Clamp01(runner.CornersMadeThisRun / 8f));
        if (stacked) fade = Mathf.Max(fade, 0.85f);
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 5f);

        bool near = runner.PreviewNeededFace(out int need);
        Color lc = near && need == leftFace ? Yellow : White;
        Color rc = near && need == rightFace ? Yellow : White;
        lc.a = fade * pulse; rc.a = fade * pulse;

        Place(leftMark, leftRt, runner.PreviewFacePoint(leftFace, 0f), lc);
        Place(rightMark, rightRt, runner.PreviewFacePoint(rightFace, 0f), rc);

        leftGlow.color = new Color(lc.r, lc.g, lc.b, 0.10f * fade);
        rightGlow.color = new Color(rc.r, rc.g, rc.b, 0.10f * fade);
    }

    void Place(Image img, RectTransform rt, Vector3 world, Color c)
    {
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) { img.color = new Color(c.r, c.g, c.b, 0f); return; }
        // About one cube wide on screen, kept readable.
        Vector3 sp2 = cam.WorldToScreenPoint(world + cam.transform.right * runner.currentCube.lossyScale.x);
        float px = Mathf.Clamp(Mathf.Abs(sp2.x - sp.x) * 0.9f, 36f, 120f);
        rt.position = new Vector3(sp.x, sp.y, 0f);
        rt.sizeDelta = new Vector2(px, px);
        img.color = c;
    }
}
