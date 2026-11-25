using UnityEngine;

/// <summary>Fits its RectTransform inside Screen.safeArea (notch, rounded corners, home bar).</summary>
public class SafeArea : MonoBehaviour
{
    Rect last;
    /// <summary>Dev captures only (UICapture): a safe area as fractions of the screen. Zero size = the real Screen.safeArea.</summary>
    public static Rect debugNormalized;

    void Update()
    {
        Rect r = Screen.safeArea;
        if (debugNormalized.width > 0f) r = new Rect(debugNormalized.x * Screen.width, debugNormalized.y * Screen.height, debugNormalized.width * Screen.width, debugNormalized.height * Screen.height);
        if (r == last) return;
        last = r;
        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(r.xMin / Screen.width, r.yMin / Screen.height);
        rt.anchorMax = new Vector2(r.xMax / Screen.width, r.yMax / Screen.height);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
