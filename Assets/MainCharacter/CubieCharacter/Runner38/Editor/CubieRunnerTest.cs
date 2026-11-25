using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only play-test helper for the 3.8 runner (Cubie). Call from an MCP RunCommand while in Play mode:
///   CubieRunnerTest.Begin(outDir)                - test save file, AutoPlayer bot, start the run, keep the pause menu away
///   CubieRunnerTest.Arm(tag, afterSeconds, mode) - freeze at the next steady moment on a visible face (mode 0) or
///                                                  mid-roll (mode 1) / right after landing (mode 2), and write
///                                                  tag_full.png (game view), tag_crop.png (around the runner),
///                                                  tag_close.png (close camera, same angle) and tag.txt (numbers)
///   CubieRunnerTest.Resume()                     - unfreeze
/// Nothing is saved into the scene; temporary cameras / cubes are destroyed straight away.
/// </summary>
public static class CubieRunnerTest
{
    static string dir;
    static CubeRailRunner runner;
    static bool hooked;
    static string tag;
    static int mode, stage;
    static float armAt, steadySince = -1f, landedAt = -1f, cropAfter = -1f;
    static bool wasRev;
    static Vector3 screenPos;

    public static bool Done => stage == 0 && tag == null;

    public static void Begin(string outDir)
    {
        dir = outDir;
        runner = Object.FindFirstObjectByType<CubeRailRunner>();
        SaveSystem.UseFile("save_test.json");
        if (runner.GetComponent<AutoPlayer>() == null) { var bot = runner.gameObject.AddComponent<AutoPlayer>(); bot.report = false; }
        if (runner.IsWaitingToStart) runner.BeginRun();
        Time.timeScale = 1f;
        if (!hooked) { EditorApplication.update += Tick; hooked = true; }
    }

    public static void Arm(string t, float afterSeconds, int m)
    {
        tag = t; mode = m; stage = 1;
        armAt = Time.realtimeSinceStartup + afterSeconds;
        Time.timeScale = 1f;
    }

    public static void Resume() { Time.timeScale = 1f; }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; hooked = false; return; }
        foreach (var p in Object.FindObjectsByType<PauseController>(FindObjectsSortMode.None)) Object.Destroy(p);
        if (runner == null) return;
        float now = Time.realtimeSinceStartup;

        if (cropAfter > 0f && now > cropAfter)
        {
            cropAfter = -1f;
            Crop(Path.Combine(dir, tag + "_full.png"), Path.Combine(dir, tag + "_crop.png"), screenPos, 460);
            tag = null; stage = 0;
            return;
        }
        if (stage != 1) return;

        bool rev = runner.IsRevolving;
        if (rev) steadySince = -1f; else if (steadySince < 0f) steadySince = now;
        if (wasRev && !rev) landedAt = now;
        if (runner.LastLandingTime > Time.fixedTime - 0.05f) landedAt = now;   // corners count too
        wasRev = rev;
        if (now < armAt || !runner.IsRunning) return;
        Camera cam = Camera.main;
        bool visible = Vector3.Dot(runner.transform.up, -cam.transform.forward) > 0.3f;

        var cubie = Object.FindFirstObjectByType<CubieRunner>();
        bool midLeg = (cubie == null || cubie.TimeToCorner > 0.45f) && (landedAt < 0f || now - landedAt > 0.5f);
        bool camFront = Vector3.Dot(-cam.transform.forward, runner.transform.forward) > 0.2f;
        bool fire = mode == 0 ? (!rev && steadySince > 0f && now - steadySince > 0.3f && visible && midLeg)
                  : mode == 3 ? (!rev && steadySince > 0f && now - steadySince > 0.3f && visible && midLeg && camFront)
                  : mode == 4 ? (!rev && steadySince > 0f && now - steadySince > 0.3f && visible && midLeg && !camFront)
                  : mode == 1 ? rev
                  : (!rev && landedAt > 0f && now - landedAt > 0.03f && now - landedAt < 0.2f && visible);
        if (!fire) return;

        Time.timeScale = 0f;
        stage = 2;
        screenPos = cam.WorldToScreenPoint(runner.transform.position + runner.transform.up * 0.3f);
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, tag + "_full.png"), 1);
        CloseUp(Path.Combine(dir, tag + "_close.png"));
        File.WriteAllText(Path.Combine(dir, tag + ".txt"), Numbers());
        cropAfter = now + 0.6f;
    }

    static string Numbers()
    {
        Camera cam = Camera.main;
        Transform r = runner.transform;
        Vector3 toCam = -cam.transform.forward;
        Vector3 local = new Vector3(Vector3.Dot(toCam, r.right), Vector3.Dot(toCam, r.up), Vector3.Dot(toCam, r.forward));
        float boxi, cube;
        Silhouettes(out boxi, out cube);
        return "toCamera(runner frame r/u/f) " + local.ToString("F2") + " yawToCam " +
               (Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg).ToString("F0") + " | screen area character/cube " + (boxi / Mathf.Max(1f, cube)).ToString("F2") +
               " | speed " + runner.CurrentMoveSpeed.ToString("F1") + " ortho " + cam.orthographicSize.ToString("F1");
    }

    // Pixel areas of the character and of a plain unit cube at the runner, from the game camera's angle.
    static void Silhouettes(out float character, out float cube)
    {
        Camera main = Camera.main;
        var go = new GameObject("TmpMeasureCam");
        var c = go.AddComponent<Camera>();
        c.CopyFrom(main);
        c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = Color.black; c.cullingMask = 1 << 31;
        c.orthographic = true; c.orthographicSize = 1.6f;
        c.transform.position = runner.transform.position - main.transform.forward * 30f;
        c.transform.rotation = main.transform.rotation;
        var rt = new RenderTexture(400, 400, 24);
        c.targetTexture = rt;
        var cr = Object.FindFirstObjectByType<CubieRunner>();
        var rs = cr.GetComponentsInChildren<Renderer>();
        var old = new int[rs.Length];
        for (int i = 0; i < rs.Length; i++) { old[i] = rs[i].gameObject.layer; rs[i].gameObject.layer = 31; }
        character = Count(c, rt);
        for (int i = 0; i < rs.Length; i++) rs[i].gameObject.layer = old[i];
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.layer = 31;
        box.transform.SetPositionAndRotation(runner.transform.position, runner.transform.rotation);
        cube = Count(c, rt);
        Object.DestroyImmediate(box);
        c.targetTexture = null; rt.Release(); Object.DestroyImmediate(go);
    }

    static int Count(Camera c, RenderTexture rt)
    {
        c.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        int n = 0;
        foreach (var p in tex.GetPixels32()) if (p.r + p.g + p.b > 6) n++;
        Object.DestroyImmediate(tex);
        return n;
    }

    // Same view direction as the game camera, but close; coins / pickups hidden so the character can be judged.
    static void CloseUp(string path) => File.WriteAllBytes(path, RenderClose(420, 1.5f).EncodeToPNG());

    static Texture2D RenderClose(int px, float ortho)
    {
        Camera main = Camera.main;
        var hiddenCoins = new System.Collections.Generic.List<Renderer>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            string n = r.gameObject.name.ToLowerInvariant();
            if (r.enabled && !r.forceRenderingOff && (n.Contains("coin") || n.Contains("gem") || n.Contains("diamond") || n.Contains("ring")))
            { r.forceRenderingOff = true; hiddenCoins.Add(r); }
        }
        var go = new GameObject("TmpCloseCam");
        var c = go.AddComponent<Camera>();
        c.CopyFrom(main);
        c.orthographic = true; c.orthographicSize = ortho;
        c.transform.position = runner.transform.position + runner.transform.up * 0.4f - main.transform.forward * 30f;
        c.transform.rotation = main.transform.rotation;
        var rt = new RenderTexture(px, px, 24);
        c.targetTexture = rt;
        c.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(px, px, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, px, px), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        c.targetTexture = null; rt.Release(); Object.DestroyImmediate(go);
        foreach (var r in hiddenCoins) if (r != null) r.forceRenderingOff = false;
        return tex;
    }

    // ------------------------------------------------------------------ filmstrip
    static int stripFrames, stripEvery, stripCount, stripTick;
    static Texture2D strip;
    static string stripTag;
    static bool stripOnHop;
    static bool stripArmedOnHop;

    /// <summary>Slow motion (timeScale) and a close-up every `every` frames, `frames` of them, saved side by side as
    /// tag_strip.png. onHop: wait for the next side switch to start it (a frame before the roll).</summary>
    public static void Filmstrip(string tag, int frames, int every, float timeScale, bool onHop)
    {
        stripTag = tag; stripFrames = frames; stripEvery = Mathf.Max(1, every); stripCount = 0; stripTick = 0;
        stripOnHop = onHop; stripArmedOnHop = onHop;
        int px = 200;
        strip = new Texture2D(px * frames, px, TextureFormat.RGB24, false);
        Time.timeScale = timeScale;
        EditorApplication.update -= StripTick;
        EditorApplication.update += StripTick;
    }

    static int lastStripFrame = -1;
    static void StripTick()
    {
        if (!EditorApplication.isPlaying || strip == null) { EditorApplication.update -= StripTick; return; }
        if (Time.frameCount == lastStripFrame) return;
        lastStripFrame = Time.frameCount;
        if (stripArmedOnHop)
        {
            if (!runner.IsRevolving) return;
            stripArmedOnHop = false;
        }
        if (stripTick++ % stripEvery != 0) return;
        var f = RenderClose(200, 1.6f);
        strip.SetPixels(stripCount * 200, 0, 200, 200, f.GetPixels());
        Object.DestroyImmediate(f);
        if (++stripCount >= stripFrames)
        {
            strip.Apply();
            File.WriteAllBytes(Path.Combine(dir, stripTag + "_strip.png"), strip.EncodeToPNG());
            Object.DestroyImmediate(strip); strip = null;
            Time.timeScale = 0f;
            EditorApplication.update -= StripTick;
        }
    }

    static void Crop(string src, string dst, Vector3 sp, int size)
    {
        if (!File.Exists(src)) return;
        var full = new Texture2D(2, 2);
        full.LoadImage(File.ReadAllBytes(src));
        int x0 = Mathf.Clamp((int)sp.x - size / 2, 0, full.width - size);
        int y0 = Mathf.Clamp((int)sp.y - size / 2, 0, full.height - size);
        var crop = new Texture2D(size, size, TextureFormat.RGB24, false);
        crop.SetPixels(full.GetPixels(x0, y0, size, size));
        crop.Apply();
        File.WriteAllBytes(dst, crop.EncodeToPNG());
        Object.DestroyImmediate(full); Object.DestroyImmediate(crop);
    }
}
