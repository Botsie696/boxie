using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Soul Cubes > Build Soul Cubes: builds the four character cubes - meshes, materials, prefabs
/// (Assets/SoulCubes/Prefabs) and their CubeSkin assets (Assets/SoulCubes/Skins) - and appends them to the shop's
/// Resources/SkinCatalog after the existing skins (which are left as they are). Re-runnable: it overwrites its own assets.
/// Everything is assembled in a preview scene, so no open scene is touched.
///   Pip        - knit beanie with a floppy cloth top + pompom, striped shirt with a flapping tail, jeans
///   Momo       - a cat: ears that twitch, whiskers, cosy sweater, red scarf with two cloth tails
///   Nori       - a sleepyhead: striped pyjamas with buttons, a long cloth nightcap, droopy eyes, yawns
///   Sir Cubert - a tiny knight: crown, tunic with a belt, a cloth cape with a gold hem, determined brows
/// </summary>
public static class SoulCubeBuilder
{
    const string Root = "Assets/SoulCubes";
    const string MeshDir = Root + "/Meshes", MatDir = Root + "/Materials", PrefabDir = Root + "/Prefabs", SkinDir = Root + "/Skins";
    const string CatalogPath = "Assets/Checkpoints/Resources/SkinCatalog.asset";

    static Scene preview;
    static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
    static Shader bodyShader, flatShader, clothShader;
    static Mesh cylinder;

    [MenuItem("Tools/Soul Cubes/Build Soul Cubes")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Soul Cubes: leave Play mode first."); return; }
        foreach (string d in new[] { Root, MeshDir, MatDir, PrefabDir, SkinDir }) EnsureFolder(d);
        bodyShader = Shader.Find("SoulCubes/SoulBody");
        flatShader = Shader.Find("SoulCubes/SoulFlat");
        clothShader = Shader.Find("SoulCubes/SoulCloth");
        if (bodyShader == null || flatShader == null || clothShader == null) { Debug.LogError("Soul Cubes: shaders missing (compile errors?)."); return; }
        cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        meshes.Clear();

        var catalog = AssetDatabase.LoadAssetAtPath<SkinCatalog>(CatalogPath);
        preview = EditorSceneManager.NewPreviewScene();
        var made = new List<CubeSkin>();
        try
        {
            made.Add(BuildPip());
            made.Add(BuildMomo());
            made.Add(BuildNori());
            made.Add(BuildCubert());
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }

        if (catalog != null)
        {
            foreach (CubeSkin s in made)
                if (s != null && !catalog.skins.Exists(x => x != null && x.id == s.id)) catalog.skins.Add(s);
            EditorUtility.SetDirty(catalog);
        }
        else Debug.LogWarning("Soul Cubes: no SkinCatalog at " + CatalogPath + " - skins built but not added to the shop.");
        AssetDatabase.SaveAssets();
        Debug.Log("Soul Cubes: built " + made.Count + " characters" + (catalog != null ? " and added them to the shop." : "."));
    }

    /// <summary>
    /// Renders every soul prefab off-screen (preview scene, nothing opened) into one contact sheet: a row per character,
    /// columns = front three-quarter at rest, back three-quarter after a few seconds of wind, the side after the wind.
    /// </summary>
    public static void RenderPreviewSheet(string pngPath, int size = 320) =>
        RenderSheet(pngPath, size, new[] { 150f, 30f, 260f }, new[] { 0f, 3f, 3f }, false);

    /// <summary>Wind check: each row is one character from behind, each column 0.5 s more of standing in the wind.</summary>
    public static void RenderWindStrip(string pngPath, int size = 220) =>
        RenderSheet(pngPath, size, new[] { 35f, 35f, 35f, 35f, 35f }, new[] { 2f, 0.5f, 0.5f, 0.5f, 0.5f }, true);

    static void RenderSheet(string pngPath, int size, float[] yaws, float[] windSeconds, bool keepSimulating)
    {
        string[] names = { "Soul_Pip", "Soul_Momo", "Soul_Nori", "Soul_SirCubert" };
        var sheet = new Texture2D(size * yaws.Length, size * names.Length, TextureFormat.RGBA32, false);
        Scene scene = EditorSceneManager.NewPreviewScene();
        var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        try
        {
            var camGo = new GameObject("PreviewCam");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.66f, 0.74f);
            cam.fieldOfView = 30f;
            cam.transform.position = new Vector3(0f, 0.55f, -5f);
            cam.transform.LookAt(new Vector3(0f, 0.25f, 0f));
            cam.targetTexture = rt;
            for (int row = 0; row < names.Length; row++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/" + names[row] + ".prefab");
                if (prefab == null) continue;
                var go = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(go, scene);
                var soul = go.GetComponent<SoulCharacter>();
                var cloths = go.GetComponentsInChildren<SoulCloth>(true);
                for (int col = 0; col < yaws.Length; col++)
                {
                    go.transform.rotation = Quaternion.Euler(0f, yaws[col], 0f);
                    if (!keepSimulating || col == 0) soul.PoseStill();
                    for (float t = 0f; t < windSeconds[col]; t += 1f / 60f)
                        foreach (SoulCloth c in cloths) c.Tick(1f / 60f);
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    tex.Apply();
                    sheet.SetPixels(col * size, (names.Length - 1 - row) * size, size, size, tex.GetPixels());
                }
                Object.DestroyImmediate(go);
            }
        }
        finally
        {
            RenderTexture.active = null;
            if (scene.IsValid()) foreach (GameObject g in scene.GetRootGameObjects()) { var c = g.GetComponent<Camera>(); if (c != null) c.targetTexture = null; }
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        sheet.Apply();
        System.IO.File.WriteAllBytes(pngPath, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
    }

    // ================================================================== characters

    static CubeSkin BuildPip()
    {
        const string n = "Pip";
        var body = BodyMat(n, new Color(0.99f, 0.84f, 0.66f), new Color(0.96f, 0.45f, 0.32f), new Color(0.27f, 0.42f, 0.72f), m =>
        {
            m.SetColor("_StripeColor", new Color(1f, 0.93f, 0.8f));
            m.SetFloat("_ShirtTop", -0.12f); m.SetFloat("_CollarDepth", 0.05f);
            m.SetFloat("_ShirtStripes", 1f); m.SetFloat("_StripeFreq", 10f);
            m.SetFloat("_PantsTop", -0.3f); m.SetFloat("_LegGap", 1f);
        });
        var c = NewCharacter(n, body, out Transform rig);
        Face(c, rig, n, new FaceSpec
        {
            eyeX = 0.17f, eyeY = 0.19f, eye = new Vector2(0.2f, 0.24f), pupil = new Vector2(0.12f, 0.15f),
            pupilColor = new Color(0.12f, 0.1f, 0.14f), browColor = new Color(0.36f, 0.22f, 0.14f), browY = 0.36f,
            brow = new Vector3(0.13f, 0.03f, 0.02f), mouthY = -0.01f, cheekColor = new Color(1f, 0.55f, 0.5f), cheekX = 0.32f, cheekY = 0.03f,
        });

        // the beanie: knit cuff + dome on a sprung pivot, a floppy cloth top with a pompom that blows in the wind
        Color mustard = new Color(0.98f, 0.74f, 0.24f), mustardDark = new Color(0.9f, 0.6f, 0.16f), cream = new Color(1f, 0.95f, 0.85f);
        Transform beanie = Pivot(rig, "Beanie", new Vector3(0f, 0.5f, 0f), new Vector3(0f, 0f, -6f));
        Part(beanie, "Cuff", RBox("Box_1.08x0.16", new Vector3(1.08f, 0.16f, 1.08f), 0.05f), Flat(n, "Cuff", mustardDark, 1f, 34f, 0.22f), new Vector3(0f, -0.02f, 0f));
        Part(beanie, "Dome", RBox("Box_1x0.26_r13", new Vector3(1f, 0.26f, 1f), 0.13f), Flat(n, "Dome", mustard, 1f, 30f, 0.12f), new Vector3(0f, 0.14f, 0f));
        Transform pom = Part(rig, "Pompom", RBox("Box_0.2", new Vector3(0.2f, 0.2f, 0.2f), 0.07f), Flat(n, "Pompom", cream, 1f, 10f, 0.15f), new Vector3(0f, 1.2f, -0.6f));
        var top = Cloth(rig, "BeanieTop", ClothMat(n, "BeanieTop", mustard, mustardDark, m =>
        {
            m.SetColor("_StripeColor", cream); m.SetFloat("_StripeFreq", 3f); m.SetFloat("_StripeAmount", 1f);
            m.SetFloat("_RibFreq", 8f); m.SetFloat("_RibAmount", 0.14f);
        }));
        top.shape = SoulCloth.Shape.Tube; top.frame = beanie; top.body = rig;
        top.anchor = new Vector3(0f, 0.2f, 0f); top.restDirection = new Vector3(0f, 1f, -0.15f);
        top.curl = -115f; top.curlAxis = new Vector3(1f, 0f, 0.25f);
        top.width = 0.6f; top.length = 0.85f; top.hemScale = 0.22f; top.columns = 8; top.rows = 8;
        top.gravity = 6f; top.drag = 2f; top.wind = 3.6f; top.gustiness = 0.85f; top.flutter = 0.9f; top.flutterFrequency = 4f;
        top.breeze = new Vector3(1f, 0.3f, -0.4f); top.breezeStrength = 3f; top.sway = 1.6f;
        top.shapeMemory = 1.8f; top.bend = 0.55f; top.margin = 0.02f; top.tip = pom; top.tipOffset = 0.06f;

        // shirt tail flapping out the back
        var tail = Cloth(rig, "ShirtTail", ClothMat(n, "ShirtTail", new Color(0.96f, 0.45f, 0.32f), new Color(0.8f, 0.34f, 0.24f), m =>
        {
            m.SetColor("_HemColor", new Color(1f, 0.93f, 0.8f)); m.SetFloat("_HemWidth", 0.25f);
        }));
        tail.frame = rig; tail.body = rig;
        tail.anchor = new Vector3(0f, -0.2f, -0.525f); tail.restDirection = new Vector3(0f, -1f, -0.3f);
        tail.width = 0.78f; tail.length = 0.2f; tail.hemScale = 1.05f; tail.columns = 6; tail.rows = 4;
        tail.gravity = 6f; tail.drag = 2.5f; tail.wind = 3.2f; tail.gustiness = 0.85f; tail.flutter = 1.6f; tail.shapeMemory = 1f;
        tail.keepBehind = true; tail.behindZ = -0.515f;
        tail.breeze = new Vector3(0f, 0.15f, -1f); tail.breezeStrength = 2f; tail.sway = 1.2f;

        c.wobblers = new[] { beanie };
        c.cloths = new[] { top, tail };
        Personality(c, eyeOpen: 1f, browTilt: -2f, smile: 0.65f, mouthWidth: 0.13f, blink: new Vector2(2f, 5f), blinkSec: 0.12f,
                    slowBlink: 0.05f, yawn: 0f, breathe: 0.02f, squash: 0.14f, cheek: 0.7f);
        return Save(c, n, "soul_pip", "Pip", "Knit hat, big heart, never stops.", SkinRarity.Rare, 900,
                    new Color(1f, 0.62f, 0.3f), new Color(1f, 0.78f, 0.38f), new Color(1f, 0.55f, 0.32f), body);
    }

    static CubeSkin BuildMomo()
    {
        const string n = "Momo";
        Color fur = new Color(0.99f, 0.76f, 0.72f), pinkIn = new Color(1f, 0.55f, 0.62f), red = new Color(0.9f, 0.24f, 0.3f), redDark = new Color(0.7f, 0.16f, 0.22f);
        var body = BodyMat(n, fur, new Color(0.62f, 0.78f, 0.98f), fur, m =>
        {
            // a cosy ribbed sweater to the bottom, no pants (a cat)
            m.SetColor("_StripeColor", new Color(0.5f, 0.66f, 0.9f));
            m.SetFloat("_ShirtTop", -0.17f); m.SetFloat("_CollarDepth", 0f);
            m.SetFloat("_ShirtStripes", 0.45f); m.SetFloat("_StripeFreq", 14f); m.SetFloat("_VerticalStripes", 1f);
            m.SetFloat("_PantsTop", -0.6f); m.SetFloat("_LegGap", 0f);
        });
        var c = NewCharacter(n, body, out Transform rig);
        Face(c, rig, n, new FaceSpec
        {
            eyeX = 0.18f, eyeY = 0.21f, eye = new Vector2(0.22f, 0.26f), pupil = new Vector2(0.15f, 0.18f),
            pupilColor = new Color(0.2f, 0.08f, 0.18f), browColor = Color.clear, mouthY = -0.035f,
            cheekColor = new Color(1f, 0.52f, 0.6f), cheekX = 0.31f, cheekY = 0.0f,
        });
        Transform face = c.faceRoot;
        // nose + whiskers ride on the face
        var nose = Part(face, "Nose", Prism("Prism_0.07x0.05", 0.07f, 0.05f, 0.02f), Flat(n, "Nose", new Color(0.95f, 0.42f, 0.52f), 0.2f), new Vector3(0f, 0.035f, 0.51f));
        nose.localRotation = Quaternion.Euler(0f, 0f, 180f);
        Material whisk = Flat(n, "Whisker", new Color(0.36f, 0.28f, 0.32f), 0.2f);
        Mesh whiskMesh = RBox("Box_0.16x0.012", new Vector3(0.16f, 0.012f, 0.01f), 0.005f);
        for (int s = -1; s <= 1; s += 2)
            for (int k = 0; k < 2; k++)
            {
                var w = Part(face, "Whisker", whiskMesh, whisk, new Vector3(s * 0.38f, 0.02f - k * 0.045f, 0.512f));
                w.localRotation = Quaternion.Euler(0f, 0f, s * (k == 0 ? 8f : -6f));
            }

        // ears: a sprung pivot (sways) holding a twitching ear
        var pivots = new List<Transform>(); var twitch = new List<Transform>();
        Mesh earOut = Prism("Prism_0.3x0.26", 0.3f, 0.26f, 0.09f), earIn = Prism("Prism_0.17x0.15", 0.17f, 0.15f, 0.02f);
        Material furMat = Flat(n, "Ear", fur, 1f), inMat = Flat(n, "EarInner", pinkIn, 0.6f);
        for (int s = -1; s <= 1; s += 2)
        {
            Transform p = Pivot(rig, s < 0 ? "EarL" : "EarR", new Vector3(s * 0.26f, 0.48f, 0.04f), new Vector3(0f, 0f, -s * 10f));
            Transform t = Pivot(p, "Twitch", Vector3.zero, Vector3.zero);
            Part(t, "Ear", earOut, furMat, Vector3.zero);
            Part(t, "Inner", earIn, inMat, new Vector3(0f, 0.025f, 0.05f));
            pivots.Add(p); twitch.Add(t);
        }

        // the scarf: a knit wrap round the neck, two cloth tails out the back
        Part(rig, "ScarfWrap", RBox("Box_1.08x0.15", new Vector3(1.08f, 0.15f, 1.08f), 0.05f), Flat(n, "Scarf", red, 1f, 40f, 0.2f), new Vector3(0f, -0.15f, 0f));
        Material tailMat = ClothMat(n, "ScarfTail", red, redDark, m =>
        {
            m.SetColor("_StripeColor", new Color(1f, 0.92f, 0.82f)); m.SetFloat("_StripeFreq", 4f); m.SetFloat("_StripeAmount", 0.8f);
            m.SetColor("_HemColor", new Color(1f, 0.92f, 0.82f)); m.SetFloat("_HemWidth", 0.1f);
        });
        var tails = new List<SoulCloth>();
        for (int k = 0; k < 2; k++)
        {
            var t = Cloth(rig, "ScarfTail" + (k + 1), tailMat);
            t.frame = rig; t.body = rig;
            t.anchor = new Vector3(k == 0 ? -0.24f : -0.07f, k == 0 ? -0.15f : -0.18f, -0.55f);
            t.restDirection = new Vector3(k == 0 ? -0.15f : 0.05f, -0.6f, -1f);
            t.width = 0.13f; t.length = k == 0 ? 0.52f : 0.42f; t.hemScale = 0.9f; t.columns = 3; t.rows = 7;
            t.gravity = 7f; t.drag = 2.6f; t.wind = 3.6f; t.gustiness = 0.85f; t.flutter = 1.6f; t.flutterFrequency = 5f + k;
            t.shapeMemory = 1.5f; t.keepBehind = true; t.behindZ = -0.52f;
            t.breeze = new Vector3(0.3f, 0.35f, -1f); t.breezeStrength = 4f; t.sway = 1.6f;
            tails.Add(t);
        }

        c.wobblers = pivots.ToArray();
        c.twitchers = twitch.ToArray();
        c.cloths = tails.ToArray();
        Personality(c, eyeOpen: 1.05f, browTilt: 0f, smile: 0.45f, mouthWidth: 0.09f, blink: new Vector2(2.5f, 6f), blinkSec: 0.15f,
                    slowBlink: 0.35f, yawn: 0f, breathe: 0.02f, squash: 0.16f, cheek: 0.8f);
        return Save(c, n, "soul_momo", "Momo", "Purrs at every corner.", SkinRarity.Epic, 1800,
                    new Color(1f, 0.62f, 0.7f), new Color(1f, 0.75f, 0.8f), new Color(0.95f, 0.5f, 0.62f), body);
    }

    static CubeSkin BuildNori()
    {
        const string n = "Nori";
        Color navy = new Color(0.36f, 0.4f, 0.78f), navyDark = new Color(0.26f, 0.29f, 0.6f), cream = new Color(0.94f, 0.93f, 1f);
        var body = BodyMat(n, new Color(0.86f, 0.84f, 1f), navy, navy, m =>
        {
            m.SetColor("_StripeColor", cream);
            m.SetFloat("_ShirtTop", -0.1f); m.SetFloat("_CollarDepth", 0.06f);
            m.SetFloat("_ShirtStripes", 1f); m.SetFloat("_PantsStripes", 1f); m.SetFloat("_StripeFreq", 9f); m.SetFloat("_VerticalStripes", 1f);
            m.SetFloat("_PantsTop", -0.28f); m.SetFloat("_LegGap", 1f);
            m.SetFloat("_Buttons", 1f); m.SetColor("_ButtonColor", new Color(1f, 0.9f, 0.6f));
        });
        var c = NewCharacter(n, body, out Transform rig);
        Face(c, rig, n, new FaceSpec
        {
            eyeX = 0.17f, eyeY = 0.18f, eye = new Vector2(0.2f, 0.24f), pupil = new Vector2(0.12f, 0.14f),
            pupilColor = new Color(0.16f, 0.14f, 0.3f), browColor = new Color(0.45f, 0.4f, 0.6f), browY = 0.33f,
            brow = new Vector3(0.12f, 0.026f, 0.02f), mouthY = -0.03f, cheekColor = new Color(0.98f, 0.62f, 0.78f), cheekX = 0.32f, cheekY = 0.04f,
        });

        // the nightcap: fluffy band + dome on a pivot, a long striped cloth tip that droops and sways, pompom on the end
        Transform cap = Pivot(rig, "Nightcap", new Vector3(0f, 0.5f, 0f), new Vector3(0f, 0f, 8f));
        Part(cap, "Band", RBox("Box_1.07x0.13", new Vector3(1.07f, 0.13f, 1.07f), 0.05f), Flat(n, "Band", cream, 1f, 24f, 0.08f), Vector3.zero);
        Part(cap, "Dome", RBox("Box_0.98x0.22_r18", new Vector3(0.98f, 0.22f, 0.98f), 0.18f), Flat(n, "Dome", navy, 1f), new Vector3(0f, 0.11f, 0f));
        Transform pom = Part(rig, "Pompom", RBox("Box_0.2", new Vector3(0.2f, 0.2f, 0.2f), 0.07f), Flat(n, "Pompom", cream, 1f, 10f, 0.12f), new Vector3(0f, 1.2f, -0.6f));
        var tip = Cloth(rig, "CapTip", ClothMat(n, "CapTip", navy, navyDark, m =>
        {
            m.SetColor("_StripeColor", cream); m.SetFloat("_StripeFreq", 4f); m.SetFloat("_StripeAmount", 1f);
        }));
        tip.shape = SoulCloth.Shape.Tube; tip.frame = cap; tip.body = rig;
        tip.anchor = new Vector3(0f, 0.18f, 0f); tip.restDirection = new Vector3(0f, 1f, -0.2f);
        tip.curl = -135f; tip.curlAxis = new Vector3(1f, 0f, -0.5f);
        tip.width = 0.62f; tip.length = 0.95f; tip.hemScale = 0.14f; tip.columns = 8; tip.rows = 9;
        tip.gravity = 8f; tip.drag = 1.6f; tip.wind = 3.4f; tip.gustiness = 0.85f; tip.flutter = 0.8f; tip.flutterFrequency = 3f;
        tip.breeze = new Vector3(1f, 0.2f, -0.3f); tip.breezeStrength = 3f; tip.sway = 1.6f;
        tip.shapeMemory = 2.2f; tip.bend = 0.6f; tip.margin = 0.02f; tip.tip = pom; tip.tipOffset = 0.07f;

        c.wobblers = new[] { cap };
        c.cloths = new[] { tip };
        Personality(c, eyeOpen: 0.42f, browTilt: -6f, smile: 0.25f, mouthWidth: 0.1f, blink: new Vector2(3f, 7f), blinkSec: 0.3f,
                    slowBlink: 0.3f, yawn: 7f, breathe: 0.028f, squash: 0.12f, cheek: 0.65f);
        return Save(c, n, "soul_nori", "Nori", "Five more minutes... then runs.", SkinRarity.Epic, 2400,
                    new Color(0.62f, 0.68f, 1f), new Color(0.78f, 0.8f, 1f), new Color(0.55f, 0.6f, 0.95f), body);
    }

    static CubeSkin BuildCubert()
    {
        const string n = "SirCubert";
        Color gold = new Color(1f, 0.78f, 0.25f), crimson = new Color(0.8f, 0.14f, 0.2f);
        var body = BodyMat(n, new Color(1f, 0.87f, 0.72f), new Color(0.2f, 0.36f, 0.86f), new Color(0.32f, 0.32f, 0.4f), m =>
        {
            m.SetFloat("_ShirtTop", -0.1f); m.SetFloat("_CollarDepth", 0.08f);
            m.SetFloat("_PantsTop", -0.3f); m.SetFloat("_LegGap", 1f);
            m.SetFloat("_BeltWidth", 0.07f); m.SetColor("_BeltColor", new Color(0.42f, 0.25f, 0.12f)); m.SetColor("_BuckleColor", gold);
        });
        var c = NewCharacter(n, body, out Transform rig);
        Face(c, rig, n, new FaceSpec
        {
            eyeX = 0.17f, eyeY = 0.18f, eye = new Vector2(0.17f, 0.21f), pupil = new Vector2(0.11f, 0.14f),
            pupilColor = new Color(0.1f, 0.12f, 0.22f), browColor = new Color(0.4f, 0.24f, 0.12f), browY = 0.34f,
            brow = new Vector3(0.16f, 0.045f, 0.022f), mouthY = -0.02f, cheekColor = new Color(1f, 0.6f, 0.55f), cheekX = 0.32f, cheekY = 0.03f,
        });

        // the crown: a square band, eight points, a ruby on the front - on a sprung pivot, a little askew
        Transform crown = Pivot(rig, "Crown", new Vector3(0.04f, 0.5f, 0f), new Vector3(0f, 0f, -7f));
        Material goldMat = Flat(n, "Gold", gold, 1f);
        Mesh bandX = RBox("Box_0.56x0.1x0.05", new Vector3(0.56f, 0.1f, 0.05f), 0.015f), bandZ = RBox("Box_0.05x0.1x0.56", new Vector3(0.05f, 0.1f, 0.56f), 0.015f);
        Part(crown, "BandF", bandX, goldMat, new Vector3(0f, 0.05f, 0.255f));
        Part(crown, "BandB", bandX, goldMat, new Vector3(0f, 0.05f, -0.255f));
        Part(crown, "BandL", bandZ, goldMat, new Vector3(-0.255f, 0.05f, 0f));
        Part(crown, "BandR", bandZ, goldMat, new Vector3(0.255f, 0.05f, 0f));
        Mesh spike = Pyramid("Pyramid_0.12x0.15", 0.12f, 0.15f), spikeSmall = Pyramid("Pyramid_0.09x0.1", 0.09f, 0.1f);
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                if (x == 0 && z == 0) continue;
                bool corner = x != 0 && z != 0;
                Part(crown, "Point", corner ? spike : spikeSmall, goldMat, new Vector3(x * 0.255f, 0.1f, z * 0.255f));
            }
        var gem = Part(crown, "Ruby", RBox("Box_0.07x0.07x0.03", new Vector3(0.07f, 0.07f, 0.03f), 0.01f), Flat(n, "Ruby", new Color(0.95f, 0.15f, 0.3f), 0.4f), new Vector3(0f, 0.05f, 0.285f));
        gem.localRotation = Quaternion.Euler(0f, 0f, 45f);

        // the cape: hangs from the shoulders, streams out at speed, gold lining and hem
        var cape = Cloth(rig, "Cape", ClothMat(n, "Cape", crimson, gold, m =>
        {
            m.SetColor("_HemColor", gold); m.SetFloat("_HemWidth", 0.1f);
        }));
        cape.frame = rig; cape.body = rig;
        cape.anchor = new Vector3(0f, 0.42f, -0.53f); cape.restDirection = new Vector3(0f, -0.55f, -0.84f);
        cape.width = 0.8f; cape.length = 0.72f; cape.hemScale = 1.08f; cape.columns = 6; cape.rows = 7;
        cape.gravity = 9f; cape.drag = 1.8f; cape.wind = 3.8f; cape.gustiness = 0.85f; cape.flutter = 1.3f; cape.flutterFrequency = 5f;
        cape.shapeMemory = 1.2f; cape.keepBehind = true; cape.behindZ = -0.515f;
        cape.breeze = new Vector3(0f, 0.45f, -1f); cape.breezeStrength = 4.5f; cape.sway = 1.2f;

        c.wobblers = new[] { crown };
        c.cloths = new[] { cape };
        Personality(c, eyeOpen: 0.95f, browTilt: 10f, smile: 0.4f, mouthWidth: 0.12f, blink: new Vector2(2.5f, 6f), blinkSec: 0.12f,
                    slowBlink: 0.02f, yawn: 0f, breathe: 0.015f, squash: 0.1f, cheek: 0.4f);
        return Save(c, n, "soul_cubert", "Sir Cubert", "Small cube. Very big oath.", SkinRarity.Legendary, 4000,
                    new Color(1f, 0.82f, 0.36f), new Color(1f, 0.9f, 0.5f), new Color(0.95f, 0.4f, 0.3f), body);
    }

    // ================================================================== shared pieces

    struct FaceSpec
    {
        public float eyeX, eyeY, browY, mouthY, cheekX, cheekY;
        public Vector2 eye, pupil;
        public Vector3 brow;
        public Color pupilColor, browColor, cheekColor;
    }

    static SoulCharacter NewCharacter(string name, Material body, out Transform rig)
    {
        var root = new GameObject("Soul_" + name);
        SceneManager.MoveGameObjectToScene(root, preview);
        var c = root.AddComponent<SoulCharacter>();
        rig = Pivot(root.transform, "Rig", Vector3.zero, Vector3.zero);
        var b = Part(rig, "Body", RBox("SoulBody", Vector3.one, 0.07f), body, Vector3.zero);
        var r = b.GetComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.On;
        r.receiveShadows = true;
        c.rig = rig;
        c.bodyRenderer = r;
        return c;
    }

    static void Face(SoulCharacter c, Transform rig, string n, FaceSpec f)
    {
        Transform face = Pivot(rig, "Face", Vector3.zero, Vector3.zero);
        c.faceRoot = face;
        Material white = Flat(n, "EyeWhite", new Color(1f, 0.99f, 0.96f), 0.15f);
        Material pupil = Flat(n, "Pupil", f.pupilColor, 0.1f);
        Material shine = Flat(n, "Shine", Color.white, 0f);
        var whites = new List<Transform>(); var pupils = new List<Transform>(); var shines = new List<Transform>(); var brows = new List<Transform>(); var cheeks = new List<Transform>();
        for (int s = -1; s <= 1; s += 2)
        {
            float x = s * f.eyeX;
            whites.Add(Disc(face, "EyeWhite", white, new Vector3(x, f.eyeY, 0.506f), new Vector3(f.eye.x, 0.012f, f.eye.y)));
            pupils.Add(Disc(face, "Pupil", pupil, new Vector3(x, f.eyeY, 0.522f), new Vector3(f.pupil.x, 0.01f, f.pupil.y)));
            shines.Add(Disc(face, "Shine", shine, new Vector3(x + f.pupil.x * 0.22f, f.eyeY + f.pupil.y * 0.22f, 0.536f), new Vector3(f.pupil.x * 0.32f, 0.006f, f.pupil.x * 0.32f)));
            if (f.browColor.a > 0f)
                brows.Add(Part(face, "Brow", RBox("Brow_" + n, f.brow, Mathf.Min(f.brow.y, f.brow.z) * 0.45f), Flat(n, "Brow", f.browColor, 0.3f), new Vector3(x, f.browY, 0.512f)));
            cheeks.Add(Disc(face, "Cheek", Flat(n, "Cheek", f.cheekColor, 0.5f), new Vector3(s * f.cheekX, f.cheekY, 0.503f), new Vector3(0.11f, 0.006f, 0.065f)));
        }
        var mouth = new GameObject("Mouth");
        mouth.transform.SetParent(face, false);
        mouth.transform.localPosition = new Vector3(0f, f.mouthY, 0.512f);
        var mf = mouth.AddComponent<MeshFilter>();
        var mr = mouth.AddComponent<MeshRenderer>();
        var mm = Flat(n, "Mouth", new Color(0.3f, 0.08f, 0.12f), 0.2f);
        mm.SetFloat("_Cull", (float)CullMode.Off);
        mr.sharedMaterial = mm;
        Quiet(mr);
        c.eyeWhites = whites.ToArray(); c.pupils = pupils.ToArray(); c.shines = shines.ToArray();
        c.brows = brows.ToArray(); c.cheeks = cheeks.ToArray(); c.mouth = mf;
    }

    static void Personality(SoulCharacter c, float eyeOpen, float browTilt, float smile, float mouthWidth, Vector2 blink, float blinkSec,
                            float slowBlink, float yawn, float breathe, float squash, float cheek)
    {
        c.eyeOpen = eyeOpen; c.browTilt = browTilt; c.smile = smile; c.mouthWidth = mouthWidth; c.blinkEvery = blink;
        c.blinkSeconds = blinkSec; c.slowBlinkChance = slowBlink; c.yawnEvery = yawn; c.breathe = breathe; c.landSquash = squash; c.cheekGlow = cheek;
    }

    static CubeSkin Save(SoulCharacter c, string n, string id, string display, string tagline, SkinRarity rarity, int price,
                         Color ui, Color trailHead, Color trailMid, Material body)
    {
        string prefabPath = PrefabDir + "/Soul_" + n + ".prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(c.gameObject, prefabPath);
        Object.DestroyImmediate(c.gameObject);

        string skinPath = SkinDir + "/Skin_" + id + ".asset";
        var skin = AssetDatabase.LoadAssetAtPath<CubeSkin>(skinPath);
        bool isNew = skin == null;
        if (isNew) skin = ScriptableObject.CreateInstance<CubeSkin>();
        skin.id = id;
        skin.displayName = display;
        skin.tagline = tagline;
        skin.rarity = rarity;
        skin.price = price;
        skin.mesh = null;
        skin.material = body;
        skin.keepSceneLook = false;
        skin.uiColor = ui;
        skin.characterPrefab = prefab;
        skin.overrideTrail = true;
        skin.trailHead = trailHead;
        skin.trailMid = trailMid;
        skin.trailTail = new Color(trailMid.r, trailMid.g, trailMid.b, 0f);
        skin.attachedParticles = null;
        skin.rollSfx = null;
        skin.landingSquash = 0f;   // the character squashes itself
        skin.deathBurstPrefab = null;
        if (isNew) AssetDatabase.CreateAsset(skin, skinPath);
        else EditorUtility.SetDirty(skin);
        return skin;
    }

    // ================================================================== objects

    static Transform Pivot(Transform parent, string name, Vector3 pos, Vector3 euler)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        else SceneManager.MoveGameObjectToScene(go, preview);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        return go.transform;
    }

    static Transform Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos)
    {
        Transform t = Pivot(parent, name, pos, Vector3.zero);
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = t.gameObject.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        Quiet(r);
        return t;
    }

    // A flat disc facing out of the face (+Z): the cylinder's axis turned onto Z; scale.z is its height on the face.
    static Transform Disc(Transform parent, string name, Material mat, Vector3 pos, Vector3 scale)
    {
        Transform t = Part(parent, name, cylinder, mat, pos);
        t.localRotation = Quaternion.Euler(90f, 0f, 0f);
        t.localScale = scale;
        return t;
    }

    static SoulCloth Cloth(Transform parent, string name, Material mat)
    {
        Transform t = Pivot(parent, name, Vector3.zero, Vector3.zero);
        t.gameObject.AddComponent<MeshFilter>();
        var r = t.gameObject.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        Quiet(r);
        return t.gameObject.AddComponent<SoulCloth>();
    }

    static void Quiet(Renderer r)
    {
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    // ================================================================== materials

    static Material BodyMat(string n, Color head, Color shirt, Color pants, System.Action<Material> extra)
    {
        var m = LoadOrNewMat(n, "Body", bodyShader);
        m.SetColor("_HeadColor", head);
        m.SetColor("_ShirtColor", shirt);
        m.SetColor("_PantsColor", pants);
        extra?.Invoke(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Flat(string n, string part, Color c, float tone, float ribFreq = 0f, float ribAmount = 0f)
    {
        var m = LoadOrNewMat(n, part, flatShader);
        m.SetColor("_BaseColor", c);
        m.SetFloat("_ToneAmount", tone);
        m.SetFloat("_RibFreq", ribFreq);
        m.SetFloat("_RibAmount", ribAmount);
        m.SetFloat("_Cull", (float)CullMode.Back);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material ClothMat(string n, string part, Color outer, Color inner, System.Action<Material> extra)
    {
        var m = LoadOrNewMat(n, part, clothShader);
        m.SetColor("_OuterColor", outer);
        m.SetColor("_InnerColor", inner);
        extra?.Invoke(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material LoadOrNewMat(string n, string part, Shader shader)
    {
        string dir = MatDir + "/" + n;
        EnsureFolder(dir);
        string path = dir + "/M_" + n + "_" + part + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != shader) m.shader = shader;
        return m;
    }

    // ================================================================== meshes

    static Mesh SaveMesh(Mesh mesh)
    {
        string path = MeshDir + "/" + mesh.name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        existing.Clear();
        EditorUtility.CopySerialized(mesh, existing);
        existing.name = mesh.name;
        Object.DestroyImmediate(mesh);
        return existing;
    }

    // A box with rounded edges (radius r), its vertices spaced evenly round each bevel. Not named "Cube" on purpose:
    // SkinApplier re-skins every runner mesh called "Cube".
    static Mesh RBox(string name, Vector3 size, float radius, int segs = 3)
    {
        if (meshes.TryGetValue(name, out Mesh cached)) return cached;
        Vector3 h = size * 0.5f;
        float r = Mathf.Min(radius, Mathf.Min(h.x, Mathf.Min(h.y, h.z)));
        Vector3 inner = h - Vector3.one * r;
        var v = new List<Vector3>(); var nm = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        foreach (Vector3 N in normals)
        {
            Vector3 U = Mathf.Abs(N.x) > 0.5f ? Vector3.forward : Vector3.right;
            Vector3 V = Mathf.Abs(N.y) > 0.5f ? Vector3.forward : Vector3.up;
            float hn = Mathf.Abs(Vector3.Dot(h, N)), hu = Mathf.Abs(Vector3.Dot(h, U)), hv = Mathf.Abs(Vector3.Dot(h, V));
            float[] cu = Coords(hu, r, segs), cv = Coords(hv, r, segs);
            int b = v.Count, nu = cu.Length;
            for (int j = 0; j < cv.Length; j++)
                for (int i = 0; i < nu; i++)
                {
                    Vector3 p = N * hn + U * cu[i] + V * cv[j];
                    Vector3 q = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                    Vector3 d = p - q;
                    Vector3 n = d.sqrMagnitude > 1e-12f ? d.normalized : N;
                    v.Add(q + n * r);
                    nm.Add(n);
                    uv.Add(new Vector2(cu[i] / (2f * hu) + 0.5f, cv[j] / (2f * hv) + 0.5f));
                }
            bool front = Vector3.Dot(Vector3.Cross(U, V), N) > 0f;
            for (int j = 0; j < cv.Length - 1; j++)
                for (int i = 0; i < nu - 1; i++)
                {
                    int a = b + j * nu + i, bb = a + 1, c = a + nu, dd = c + 1;
                    if (front) { tri.Add(a); tri.Add(bb); tri.Add(c); tri.Add(bb); tri.Add(dd); tri.Add(c); }
                    else { tri.Add(a); tri.Add(c); tri.Add(bb); tri.Add(bb); tri.Add(c); tri.Add(dd); }
                }
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v); mesh.SetNormals(nm); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
        mesh.RecalculateBounds();
        mesh = SaveMesh(mesh);
        meshes[name] = mesh;
        return mesh;
    }

    // Grid lines along one face axis: tight round the bevels (even angles), one wide span between.
    static float[] Coords(float half, float r, int segs)
    {
        var list = new List<float>();
        float flat = half - r;
        for (int k = segs; k >= 0; k--) list.Add(-flat - r * Mathf.Tan(Mathf.PI * 0.25f * k / segs));
        for (int k = 0; k <= segs; k++) list.Add(flat + r * Mathf.Tan(Mathf.PI * 0.25f * k / segs));
        return list.ToArray();
    }

    // A triangle (base w on the pivot, apex h up) d thick - an ear, a nose.
    static Mesh Prism(string name, float w, float h, float d)
    {
        if (meshes.TryGetValue(name, out Mesh cached)) return cached;
        Vector3 a = new Vector3(-w * 0.5f, 0f, 0f), b = new Vector3(w * 0.5f, 0f, 0f), c = new Vector3(0f, h, 0f), f = Vector3.forward * (d * 0.5f);
        var polys = new List<Vector3[]>
        {
            new[] { a + f, b + f, c + f }, new[] { a - f, b - f, c - f },
            new[] { a + f, a - f, b - f, b + f }, new[] { b + f, b - f, c - f, c + f }, new[] { c + f, c - f, a - f, a + f },
        };
        Mesh m = SaveMesh(Convex(name, polys));
        meshes[name] = m;
        return m;
    }

    // A square pyramid standing on its base (w wide, h tall) - a crown point.
    static Mesh Pyramid(string name, float w, float h)
    {
        if (meshes.TryGetValue(name, out Mesh cached)) return cached;
        float s = w * 0.5f;
        Vector3 p0 = new Vector3(-s, 0, -s), p1 = new Vector3(s, 0, -s), p2 = new Vector3(s, 0, s), p3 = new Vector3(-s, 0, s), top = new Vector3(0, h, 0);
        var polys = new List<Vector3[]> { new[] { p0, p1, p2, p3 }, new[] { p0, p1, top }, new[] { p1, p2, top }, new[] { p2, p3, top }, new[] { p3, p0, top } };
        Mesh m = SaveMesh(Convex(name, polys));
        meshes[name] = m;
        return m;
    }

    // Flat-shaded convex solid from its faces (each wound outward automatically).
    static Mesh Convex(string name, List<Vector3[]> polys)
    {
        Vector3 centre = Vector3.zero; int count = 0;
        foreach (var p in polys) foreach (var q in p) { centre += q; count++; }
        centre /= Mathf.Max(1, count);
        var v = new List<Vector3>(); var nm = new List<Vector3>(); var tri = new List<int>();
        foreach (var p in polys)
        {
            Vector3 n = Vector3.Cross(p[1] - p[0], p[2] - p[0]).normalized;
            bool flip = Vector3.Dot(n, p[0] - centre) < 0f;
            if (flip) n = -n;
            int b = v.Count;
            foreach (var q in p) { v.Add(q); nm.Add(n); }
            for (int k = 1; k < p.Length - 1; k++)
            {
                tri.Add(b);
                if (!flip) { tri.Add(b + k); tri.Add(b + k + 1); }
                else { tri.Add(b + k + 1); tri.Add(b + k); }
            }
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v); mesh.SetNormals(nm); mesh.SetTriangles(tri, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
