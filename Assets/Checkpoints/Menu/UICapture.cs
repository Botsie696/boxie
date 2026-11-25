#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Dev only: a screenshot of the running scene at any device size, whatever the Game view is set to. The scene cameras
/// render into a w x h target, then every overlay canvas is drawn on top by a temporary UI camera (canvases switch to
/// Screen Space - Camera for one frame, CanvasScaler sees the target size), with SafeArea.debugNormalized standing in for the
/// device's notch / home bar. Usage (Play mode): UICapture.Capture(1170, 2532, UICapture.IPhoneSafe(1170, 2532), path).
/// </summary>
public class UICapture : MonoBehaviour
{
    static UICapture host;
    public static bool Busy { get; private set; }

    /// <summary>A typical notch phone: 47 pt top, 34 pt bottom at 3x (iPhone 12-15).</summary>
    public static Rect IPhoneSafe(int w, int h) => new Rect(0f, 102f / h, 1f, 1f - (141f + 102f) / h);
    /// <summary>An Android punch-hole phone: ~ 110 px status bar, gesture bar ~ 60 px (1440 x 3200).</summary>
    public static Rect AndroidSafe(int w, int h) => new Rect(0f, 60f / h, 1f, 1f - (110f + 60f) / h);

    public static void Capture(int w, int h, Rect safeNormalized, string path)
    {
        if (host == null) host = new GameObject("UICapture").AddComponent<UICapture>();
        host.StartCoroutine(host.Run(w, h, safeNormalized, path));
    }

    IEnumerator Run(int w, int h, Rect safe, string path)
    {
        while (Busy) yield return null;
        Busy = true;
        SafeArea.debugNormalized = safe.width > 0f ? safe : new Rect(0f, 0f, 1f, 1f);

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var camGo = new GameObject("UICaptureCamera");
        var ui = camGo.AddComponent<Camera>();
        ui.enabled = false;
        ui.clearFlags = CameraClearFlags.Nothing;
        ui.cullingMask = 1 << 5;
        ui.orthographic = true;
        ui.targetTexture = rt;
        var data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.renderShadows = false;

        var switched = new List<Canvas>();
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || !c.isActiveAndEnabled || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            switched.Add(c);
        }
        switched.Sort((a, b) => a.sortingOrder.CompareTo(b.sortingOrder));
        foreach (Canvas c in switched)
        {
            foreach (Transform t in c.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 5;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = ui;
            c.planeDistance = 1f;
        }
        yield return null;           // SafeArea + CanvasScaler + layout at the target size
        yield return null;
        Canvas.ForceUpdateCanvases();

        // scene cameras (the menu's band cameras keep their own targets)
        var cams = new List<Camera>(Camera.allCameras);
        cams.Sort((a, b) => a.depth.CompareTo(b.depth));
        foreach (Camera c in cams)
        {
            if (c == ui || c.targetTexture != null) continue;
            c.targetTexture = rt;
            c.Render();
            c.targetTexture = null;
        }
        // canvases, in sorting order
        ui.Render();

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex);

        foreach (Canvas c in switched) if (c != null) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; }
        SafeArea.debugNormalized = default;
        Destroy(camGo);
        rt.Release();
        Destroy(rt);
        Busy = false;
        Debug.Log("[UICapture] " + w + "x" + h + " -> " + path);
    }
}
#endif
