using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Soul Cubes > Build Expressive Cubes: five one-eyed character cubes in the spirit of the yellow eye-block, only
/// matte (the SoulCubes flat-tone shaders, no gloss - they read the same in every checkpoint theme) and with a real
/// face (SoulCharacter: pupils that track the leg and swing to corners, blinks, brows, mouth, cheeks, squash, emotions
/// on coins / close calls / crashes):
///   Zuri   - afro, hair pick with a flower, lashes, colourful scarf (confident, a little smug)
///   Fuzzo  - fuzzy orange monster, ears, fangs, a huge happy grin
///   Bolt   - rusty riveted robot in a porthole, heavy brow plates, gritted teeth (grumpy)
///   Gloop  - green slime on a purple base, sweat drops, always a bit worried
///   Parcel - cardboard box with taped flaps and a label, curious and cheerful
/// Builds meshes / materials / prefabs / CubeSkins under Assets/SoulCubes and appends the skins to the shop's
/// Resources/SkinCatalog (existing skins - the main character included - are left alone). Re-runnable.
/// </summary>
public static class ExpressiveCubeBuilder
{
    const string Root = "Assets/SoulCubes";
    const string MeshDir = Root + "/Meshes", MatDir = Root + "/Materials", PrefabDir = Root + "/Prefabs", SkinDir = Root + "/Skins";
    const string CatalogPath = "Assets/Checkpoints/Resources/SkinCatalog.asset";

    static Scene preview;
    static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
    static Shader flatShader;
    static Mesh cylinder, sphere;

    [MenuItem("Tools/Soul Cubes/Build Expressive Cubes")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Expressive Cubes: leave Play mode first."); return; }
        foreach (string d in new[] { Root, MeshDir, MatDir, PrefabDir, SkinDir }) EnsureFolder(d);
        flatShader = Shader.Find("SoulCubes/SoulFlat");
        if (flatShader == null) { Debug.LogError("Expressive Cubes: SoulCubes/SoulFlat shader missing."); return; }
        cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        meshes.Clear();

        var catalog = AssetDatabase.LoadAssetAtPath<SkinCatalog>(CatalogPath);
        preview = EditorSceneManager.NewPreviewScene();
        var made = new List<CubeSkin>();
        try
        {
            made.Add(BuildZuri());
            made.Add(BuildFuzzo());
            made.Add(BuildBolt());
            made.Add(BuildGloop());
            made.Add(BuildParcel());
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }

        if (catalog != null)
        {
            foreach (CubeSkin s in made)
                if (s != null && !catalog.skins.Exists(x => x != null && x.id == s.id)) catalog.skins.Add(s);
            EditorUtility.SetDirty(catalog);
        }
        else Debug.LogWarning("Expressive Cubes: no SkinCatalog at " + CatalogPath);
        AssetDatabase.SaveAssets();
        Debug.Log("Expressive Cubes: built " + made.Count + " characters" + (catalog != null ? " and added them to the shop." : "."));
    }

    /// <summary>Contact sheet (one character per column, front three-quarter) for a quick look without opening a scene.</summary>
    public static void RenderSheet(string pngPath, int size = 360)
    {
        string[] names = { "Zuri", "Fuzzo", "Bolt", "Gloop", "Parcel" };
        var sheet = new Texture2D(size * names.Length, size, TextureFormat.RGBA32, false);
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
            cam.backgroundColor = new Color(0.2f, 0.22f, 0.3f);
            cam.fieldOfView = 30f;
            cam.transform.position = new Vector3(0f, 0.7f, -5.2f);
            cam.transform.LookAt(new Vector3(0f, 0.3f, 0f));
            cam.targetTexture = rt;
            for (int i = 0; i < names.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Soul_" + names[i] + ".prefab");
                if (prefab == null) continue;
                var go = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.rotation = Quaternion.Euler(0f, 160f, 0f);
                go.GetComponent<SoulCharacter>().PoseStill();
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                sheet.SetPixels(i * size, 0, size, size, tex.GetPixels());
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

    static CubeSkin BuildZuri()
    {
        const string n = "Zuri";
        Color skin = new Color(0.62f, 0.4f, 0.3f), hair = new Color(0.2f, 0.12f, 0.1f), hairHi = new Color(0.3f, 0.19f, 0.15f);
        Color pink = new Color(0.9f, 0.4f, 0.75f), teal = new Color(0.25f, 0.78f, 0.74f);
        var c = NewCharacter(n, Flat(n, "Body", skin, 1f), out Transform rig);
        Face(c, rig, n, new FaceSpec
        {
            eye = 0.56f, iris = 0.35f, irisColor = new Color(0.42f, 0.22f, 0.1f), pupilColor = new Color(0.09f, 0.05f, 0.05f),
            browColor = new Color(0.14f, 0.08f, 0.07f), brow = new Vector3(0.2f, 0.035f, 0.02f), browY = 0.4f, browX = 0.16f,
            mouthColor = new Color(0.62f, 0.12f, 0.2f), mouthY = -0.32f, cheekColor = new Color(0.86f, 0.4f, 0.36f), cheekX = 0.33f, cheekY = -0.13f,
        });
        // lids: purple shadow above the eye and a heavy lash line
        Material shadow = Flat(n, "Shadow", new Color(0.62f, 0.42f, 0.7f), 0.3f), lash = Flat(n, "Lash", new Color(0.1f, 0.06f, 0.06f), 0f);
        Disc(c.faceRoot, "Shadow", shadow, new Vector3(0f, 0.24f, 0.497f), new Vector3(0.56f, 0.005f, 0.2f));
        var lashes = Part(c.faceRoot, "Lashes", RBox("Box_0.6x0.035", new Vector3(0.6f, 0.035f, 0.02f), 0.01f), lash, new Vector3(0f, 0.31f, 0.515f));
        for (int s = -1; s <= 1; s += 2)
        {
            var flick = Part(c.faceRoot, "Flick", RBox("Box_0.1x0.025", new Vector3(0.1f, 0.025f, 0.02f), 0.01f), lash, new Vector3(s * 0.32f, 0.335f, 0.515f));
            flick.localRotation = Quaternion.Euler(0f, 0f, s * 28f);
        }

        // scarf band round the bottom (pink and teal) - tonal, like the reference
        Part(rig, "ScarfA", RBox("Box_1.06x0.14", new Vector3(1.06f, 0.14f, 1.06f), 0.05f), Flat(n, "ScarfA", pink, 1f), new Vector3(0f, -0.37f, 0f));
        Part(rig, "ScarfB", RBox("Box_1.08x0.07", new Vector3(1.08f, 0.07f, 1.08f), 0.03f), Flat(n, "ScarfB", teal, 1f), new Vector3(0f, -0.3f, 0f));

        // the afro: a mass of puffs on a sprung pivot (never over the face)
        Transform afro = Pivot(rig, "Afro", new Vector3(0f, 0.48f, 0f), Vector3.zero);
        Material h1 = Flat(n, "Hair", hair, 1f, 0f, 0f), h2 = Flat(n, "HairLight", hairHi, 1f);
        Vector4[] puffs =
        {
            new Vector4(0f, 0.28f, -0.02f, 0.62f), new Vector4(-0.34f, 0.2f, 0f, 0.46f), new Vector4(0.34f, 0.2f, 0f, 0.46f),
            new Vector4(0f, 0.2f, -0.32f, 0.5f), new Vector4(-0.28f, 0.2f, -0.26f, 0.42f), new Vector4(0.28f, 0.2f, -0.26f, 0.42f),
            new Vector4(-0.2f, 0.52f, 0.05f, 0.4f), new Vector4(0.2f, 0.52f, 0.05f, 0.4f), new Vector4(0f, 0.56f, -0.12f, 0.42f),
            new Vector4(-0.42f, 0.42f, -0.1f, 0.34f), new Vector4(0.42f, 0.42f, -0.1f, 0.34f), new Vector4(0f, 0.4f, 0.3f, 0.34f),
            new Vector4(-0.26f, 0.38f, 0.24f, 0.3f), new Vector4(0.26f, 0.38f, 0.24f, 0.3f),
        };
        for (int i = 0; i < puffs.Length; i++)
        {
            Transform p = Part(afro, "Puff", sphere, i % 3 == 0 ? h2 : h1, new Vector3(puffs[i].x, puffs[i].y, puffs[i].z));
            p.localScale = Vector3.one * puffs[i].w;
        }
        // the hair pick with a flower, tucked in at the top right
        Transform pick = Pivot(afro, "Pick", new Vector3(0.22f, 0.7f, 0.08f), new Vector3(0f, 0f, -24f));
        Material gold = Flat(n, "Pick", new Color(0.95f, 0.72f, 0.25f), 0.9f);
        Part(pick, "Grip", RBox("Box_0.09x0.2", new Vector3(0.09f, 0.2f, 0.03f), 0.02f), gold, new Vector3(0f, 0.05f, 0f));
        for (int k = -2; k <= 2; k++) Part(pick, "Tine", RBox("Box_0.018x0.22", new Vector3(0.018f, 0.22f, 0.03f), 0.007f), gold, new Vector3(k * 0.02f, -0.1f, 0f));
        Transform flower = Pivot(pick, "Flower", new Vector3(-0.03f, 0.18f, 0.02f), Vector3.zero);
        Material petal = Flat(n, "Petal", new Color(0.96f, 0.5f, 0.82f), 0.8f), centre = Flat(n, "FlowerCentre", new Color(1f, 0.82f, 0.25f), 0.6f);
        for (int k = 0; k < 6; k++)
        {
            float a = k * 60f * Mathf.Deg2Rad;
            Transform pt = Disc(flower, "Petal", petal, new Vector3(Mathf.Cos(a) * 0.075f, Mathf.Sin(a) * 0.075f, 0.03f), new Vector3(0.09f, 0.012f, 0.09f));
            pt.localRotation = Quaternion.identity * Quaternion.Euler(90f, 0f, 0f);
        }
        Disc(flower, "Centre", centre, new Vector3(0f, 0f, 0.045f), new Vector3(0.075f, 0.014f, 0.075f));

        c.wobblers = new[] { afro };
        Personality(c, eyeOpen: 0.92f, browTilt: -4f, smile: 0.55f, mouthWidth: 0.16f, blink: new Vector2(2.4f, 5.5f), blinkSec: 0.15f,
                    slowBlink: 0.3f, yawn: 0f, breathe: 0.016f, squash: 0.11f, cheek: 0.6f, pupilOffset: 0.06f);
        return Save(c, n, "soul_zuri", "Zuri", "Knows exactly how good she looks.", SkinRarity.Epic, 2000,
                    new Color(0.96f, 0.45f, 0.78f), new Color(0.98f, 0.55f, 0.82f), new Color(0.3f, 0.8f, 0.76f), Flat(n, "Body", skin, 1f));
    }

    static CubeSkin BuildFuzzo()
    {
        const string n = "Fuzzo";
        Color fur = new Color(0.95f, 0.5f, 0.28f), furDark = new Color(0.78f, 0.34f, 0.2f), furLight = new Color(1f, 0.64f, 0.38f), inner = new Color(0.96f, 0.6f, 0.56f);
        var body = Flat(n, "Body", fur, 1f, 38f, 0.1f);
        var c = NewCharacter(n, body, out Transform rig);
        Face(c, rig, n, new FaceSpec
        {
            eye = 0.64f, iris = 0.4f, irisColor = new Color(0.6f, 0.3f, 0.1f), pupilColor = new Color(0.12f, 0.05f, 0.04f),
            browColor = furDark, brow = new Vector3(0.2f, 0.06f, 0.03f), browY = 0.41f, browX = 0.17f,
            mouthColor = new Color(0.45f, 0.08f, 0.1f), mouthY = -0.31f, cheekColor = new Color(1f, 0.55f, 0.45f), cheekX = 0.34f, cheekY = -0.12f,
        });
        // fangs on the grin
        Material tooth = Flat(n, "Fang", new Color(1f, 0.97f, 0.9f), 0.2f);
        for (int s = -1; s <= 1; s += 2)
        {
            var f = Part(c.faceRoot, "Fang", Prism("Prism_0.07x0.09", 0.07f, 0.09f, 0.02f), tooth, new Vector3(s * 0.12f, -0.275f, 0.516f));
            f.localRotation = Quaternion.Euler(0f, 0f, 180f);
        }
        // ears: sprung pivots holding a furry ear with a pink inside
        var pivots = new List<Transform>(); var twitch = new List<Transform>();
        Mesh earOut = Prism("Prism_0.3x0.26", 0.3f, 0.26f, 0.09f), earIn = Prism("Prism_0.17x0.15", 0.17f, 0.15f, 0.02f);
        Material furMat = Flat(n, "Ear", furDark, 1f, 38f, 0.08f), inMat = Flat(n, "EarInner", inner, 0.6f);
        for (int s = -1; s <= 1; s += 2)
        {
            Transform p = Pivot(rig, s < 0 ? "EarL" : "EarR", new Vector3(s * 0.34f, 0.47f, 0.04f), new Vector3(0f, 0f, -s * 18f));
            Transform t = Pivot(p, "Twitch", Vector3.zero, Vector3.zero);
            Part(t, "Ear", earOut, furMat, Vector3.zero);
            Part(t, "Inner", earIn, inMat, new Vector3(0f, 0.025f, 0.05f));
            pivots.Add(p); twitch.Add(t);
        }
        // fur tufts along the top edge and sides, not on the face
        Mesh tuft = Prism("Prism_0.14x0.15", 0.14f, 0.15f, 0.06f);
        Material tuftMat = Flat(n, "Tuft", furLight, 1f, 38f, 0.06f);
        var rng = new System.Random(7);
        for (int i = 0; i < 16; i++)
        {
            float x = Mathf.Lerp(-0.42f, 0.42f, (i % 8) / 7f), z = i < 8 ? 0.3f : -0.3f;
            var t = Part(rig, "Tuft", tuft, tuftMat, new Vector3(x, 0.47f, z + (float)(rng.NextDouble() - 0.5) * 0.14f));
            t.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 180f, (float)(rng.NextDouble() - 0.5) * 36f);
            t.localScale = Vector3.one * (0.8f + (float)rng.NextDouble() * 0.6f);
        }
        for (int s = -1; s <= 1; s += 2)
            for (int k = 0; k < 4; k++)
            {
                var t = Part(rig, "SideTuft", tuft, tuftMat, new Vector3(s * 0.5f, 0.36f - k * 0.22f, -0.28f + (k % 2) * 0.3f));
                t.localRotation = Quaternion.Euler(0f, 90f, -s * 80f);
            }

        c.wobblers = pivots.ToArray();
        c.twitchers = twitch.ToArray();
        Personality(c, eyeOpen: 1.1f, browTilt: -3f, smile: 0.95f, mouthWidth: 0.36f, blink: new Vector2(2.2f, 5f), blinkSec: 0.12f,
                    slowBlink: 0.05f, yawn: 0f, breathe: 0.024f, squash: 0.16f, cheek: 0.85f, pupilOffset: 0.065f);
        return Save(c, n, "soul_fuzzo", "Fuzzo", "Bites nothing. Hugs everything.", SkinRarity.Rare, 1100,
                    new Color(1f, 0.55f, 0.3f), new Color(1f, 0.7f, 0.4f), new Color(0.95f, 0.45f, 0.25f), body);
    }

    static CubeSkin BuildBolt()
    {
        const string n = "Bolt";
        Color steel = new Color(0.55f, 0.58f, 0.6f), steelDark = new Color(0.36f, 0.38f, 0.42f), steelLight = new Color(0.7f, 0.72f, 0.72f), rust = new Color(0.62f, 0.36f, 0.2f);
        var body = Flat(n, "Body", steel, 1f);
        var c = NewCharacter(n, body, out Transform rig);
        Face(c, rig, n, new FaceSpec
        {
            eye = 0.5f, iris = 0.33f, irisColor = new Color(0.85f, 0.5f, 0.12f), pupilColor = new Color(0.1f, 0.06f, 0.04f),
            browColor = steelDark, brow = new Vector3(0.24f, 0.075f, 0.04f), browY = 0.36f, browX = 0.15f,
            mouthColor = new Color(0.14f, 0.12f, 0.14f), mouthY = -0.33f, cheekColor = Color.clear, cheekX = 0.33f, cheekY = -0.13f,
        });
        // porthole ring round the eye
        Material ring = Flat(n, "Ring", steelDark, 1f), ringLight = Flat(n, "RingLight", steelLight, 1f);
        Disc(c.faceRoot, "RingOuter", ring, new Vector3(0f, 0.02f, 0.488f), new Vector3(0.72f, 0.008f, 0.72f));
        Disc(c.faceRoot, "RingInner", ringLight, new Vector3(0f, 0.02f, 0.492f), new Vector3(0.62f, 0.008f, 0.62f));
        // rivets in the corners of the face, bolt heads on the lid, rust patches
        Material rivet = Flat(n, "Rivet", steelLight, 1f), rustMat = Flat(n, "Rust", rust, 1f), rustDark = Flat(n, "RustDark", new Color(0.45f, 0.26f, 0.16f), 1f);
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                Disc(c.faceRoot, "Rivet", rivet, new Vector3(sx * 0.42f, sy * 0.42f, 0.5f), new Vector3(0.07f, 0.012f, 0.07f));
        Disc(c.faceRoot, "Rust", rustMat, new Vector3(-0.3f, -0.36f, 0.497f), new Vector3(0.2f, 0.005f, 0.13f));
        Disc(c.faceRoot, "Rust", rustDark, new Vector3(0.36f, 0.12f, 0.497f), new Vector3(0.1f, 0.005f, 0.2f));
        Disc(c.faceRoot, "Rust", rustMat, new Vector3(0.18f, -0.4f, 0.497f), new Vector3(0.12f, 0.005f, 0.07f));
        Part(rig, "Lid", RBox("Box_1.04x0.09", new Vector3(1.04f, 0.09f, 1.04f), 0.03f), Flat(n, "Lid", steelDark, 1f), new Vector3(0f, 0.5f, 0f));
        Part(rig, "Base", RBox("Box_1.04x0.1", new Vector3(1.04f, 0.1f, 1.04f), 0.03f), Flat(n, "Base", steelDark, 1f), new Vector3(0f, -0.47f, 0f));
        // antenna on a spring
        Transform ant = Pivot(rig, "Antenna", new Vector3(-0.28f, 0.54f, -0.1f), new Vector3(0f, 0f, 6f));
        Part(ant, "Stalk", RBox("Box_0.04x0.3", new Vector3(0.04f, 0.3f, 0.04f), 0.015f), ring, new Vector3(0f, 0.15f, 0f));
        Part(ant, "Bulb", sphere, Flat(n, "Bulb", new Color(0.95f, 0.3f, 0.2f), 0.5f), new Vector3(0f, 0.32f, 0f)).localScale = Vector3.one * 0.1f;

        c.wobblers = new[] { ant };
        Personality(c, eyeOpen: 0.82f, browTilt: 22f, smile: -0.45f, mouthWidth: 0.28f, blink: new Vector2(3f, 7f), blinkSec: 0.1f,
                    slowBlink: 0f, yawn: 0f, breathe: 0.008f, squash: 0.06f, cheek: 0f, pupilOffset: 0.05f);
        return Save(c, n, "soul_bolt", "Bolt", "Rusty, grumpy, never skips a beat.", SkinRarity.Rare, 1400,
                    new Color(0.7f, 0.74f, 0.78f), new Color(1f, 0.7f, 0.3f), new Color(0.7f, 0.45f, 0.25f), body);
    }

    static CubeSkin BuildGloop()
    {
        const string n = "Gloop";
        Color green = new Color(0.58f, 0.8f, 0.3f), greenLight = new Color(0.7f, 0.88f, 0.4f), purple = new Color(0.5f, 0.3f, 0.7f);
        var body = Flat(n, "Body", green, 1f);
        var c = NewCharacter(n, body, out Transform rig);
        Face(c, rig, n, new FaceSpec
        {
            eye = 0.64f, iris = 0.38f, irisColor = new Color(0.34f, 0.6f, 0.22f), pupilColor = new Color(0.06f, 0.14f, 0.06f),
            browColor = new Color(0.32f, 0.5f, 0.2f), brow = new Vector3(0.17f, 0.03f, 0.02f), browY = 0.42f, browX = 0.17f,
            mouthColor = new Color(0.45f, 0.18f, 0.2f), mouthY = -0.34f, cheekColor = new Color(0.9f, 0.7f, 0.4f), cheekX = 0.34f, cheekY = -0.14f,
        });
        // purple base, slime bumps on top, mottled spots
        Part(rig, "Base", RBox("Box_1.06x0.16", new Vector3(1.06f, 0.16f, 1.06f), 0.05f), Flat(n, "Base", purple, 1f), new Vector3(0f, -0.42f, 0f));
        Material bump = Flat(n, "Bump", greenLight, 1f);
        Vector4[] bumps = { new Vector4(-0.25f, 0.5f, 0.1f, 0.3f), new Vector4(0.2f, 0.5f, -0.15f, 0.36f), new Vector4(0.34f, 0.49f, 0.22f, 0.22f), new Vector4(-0.3f, 0.49f, -0.3f, 0.24f) };
        foreach (Vector4 b in bumps) Part(rig, "Bump", sphere, bump, new Vector3(b.x, b.y, b.z)).localScale = new Vector3(b.w, b.w * 0.55f, b.w);
        Material spot = Flat(n, "Spot", new Color(0.46f, 0.68f, 0.24f), 1f);
        Disc(c.faceRoot, "Spot", spot, new Vector3(-0.3f, -0.05f, 0.497f), new Vector3(0.1f, 0.005f, 0.08f));
        Disc(c.faceRoot, "Spot", spot, new Vector3(0.28f, 0.3f, 0.497f), new Vector3(0.08f, 0.005f, 0.06f));
        Disc(c.faceRoot, "Spot", spot, new Vector3(0.12f, -0.36f, 0.497f), new Vector3(0.12f, 0.005f, 0.07f));
        // sweat drops on the face's left side, swaying on springs
        Material dropMat = Flat(n, "Drop", new Color(0.62f, 0.86f, 1f), 0.5f);
        var drops = new List<Transform>();
        for (int k = 0; k < 2; k++)
        {
            Transform d = Pivot(c.faceRoot, "DropPivot", new Vector3(-0.38f + k * 0.04f, 0.3f - k * 0.2f, 0.5f), Vector3.zero);
            Part(d, "Drop", Prism("Prism_0.07x0.09", 0.07f, 0.09f, 0.02f), dropMat, new Vector3(0f, -0.045f + (k == 0 ? 0f : -0.01f), 0.012f));
            drops.Add(d);
        }

        c.wobblers = drops.ToArray();
        Personality(c, eyeOpen: 1.1f, browTilt: -16f, smile: -0.5f, mouthWidth: 0.2f, blink: new Vector2(1.6f, 3.6f), blinkSec: 0.1f,
                    slowBlink: 0f, yawn: 0f, breathe: 0.03f, squash: 0.2f, cheek: 0.2f, pupilOffset: 0.075f);
        return Save(c, n, "soul_gloop", "Gloop", "Nervous. Runs anyway.", SkinRarity.Common, 600,
                    new Color(0.62f, 0.85f, 0.32f), new Color(0.7f, 0.9f, 0.4f), new Color(0.5f, 0.32f, 0.72f), body);
    }

    static CubeSkin BuildParcel()
    {
        const string n = "Parcel";
        Color card = new Color(0.78f, 0.58f, 0.36f), cardDark = new Color(0.66f, 0.47f, 0.28f), tape = new Color(0.9f, 0.76f, 0.5f);
        var body = Flat(n, "Body", card, 1f, 0f, 0f);
        var c = NewCharacter(n, body, out Transform rig);
        Face(c, rig, n, new FaceSpec
        {
            eye = 0.58f, iris = 0.37f, irisColor = new Color(0.3f, 0.2f, 0.12f), pupilColor = new Color(0.08f, 0.05f, 0.03f),
            browColor = new Color(0.38f, 0.26f, 0.16f), brow = new Vector3(0.17f, 0.03f, 0.02f), browY = 0.4f, browX = 0.16f,
            mouthColor = new Color(0.4f, 0.14f, 0.12f), mouthY = -0.32f, cheekColor = new Color(0.9f, 0.5f, 0.38f), cheekX = 0.33f, cheekY = -0.14f,
        });
        // taped seam across the top, a label, and the folded-over lid edge
        Material tapeMat = Flat(n, "Tape", tape, 1f);
        Part(rig, "TapeTop", RBox("Box_0.2x1.04", new Vector3(0.2f, 0.03f, 1.04f), 0.01f), tapeMat, new Vector3(0f, 0.5f, 0f));
        Part(rig, "Rim", RBox("Box_1.03x0.05", new Vector3(1.03f, 0.05f, 1.03f), 0.02f), Flat(n, "Rim", cardDark, 1f), new Vector3(0f, 0.34f, 0f));
        Disc(c.faceRoot, "Label", Flat(n, "Label", new Color(0.96f, 0.93f, 0.86f), 0.7f), new Vector3(-0.3f, -0.4f, 0.497f), new Vector3(0.2f, 0.005f, 0.12f));
        Disc(c.faceRoot, "LabelLine", Flat(n, "LabelLine", new Color(0.35f, 0.3f, 0.26f), 0.2f), new Vector3(-0.3f, -0.4f, 0.5f), new Vector3(0.14f, 0.004f, 0.015f));
        // two loose top flaps that flop with the motion
        var pivots = new List<Transform>();
        Material flap = Flat(n, "Flap", cardDark, 1f);
        for (int s = -1; s <= 1; s += 2)
        {
            Transform p = Pivot(rig, s < 0 ? "FlapL" : "FlapR", new Vector3(s * 0.5f, 0.5f, 0f), new Vector3(0f, 0f, -s * 32f));
            Part(p, "Flap", RBox("Box_0.5x0.03x1", new Vector3(0.5f, 0.03f, 0.98f), 0.01f), flap, new Vector3(-s * 0.25f, 0.015f, 0f));
            pivots.Add(p);
        }

        c.wobblers = pivots.ToArray();
        Personality(c, eyeOpen: 1.05f, browTilt: -2f, smile: 0.7f, mouthWidth: 0.22f, blink: new Vector2(2.2f, 5f), blinkSec: 0.12f,
                    slowBlink: 0.1f, yawn: 0f, breathe: 0.02f, squash: 0.18f, cheek: 0.7f, pupilOffset: 0.065f);
        return Save(c, n, "soul_parcel", "Parcel", "Special delivery. Handle with a grin.", SkinRarity.Common, 500,
                    new Color(0.85f, 0.65f, 0.4f), new Color(0.95f, 0.78f, 0.5f), new Color(0.8f, 0.58f, 0.34f), body);
    }

    // ================================================================== shared pieces

    struct FaceSpec
    {
        public float eye, iris, browY, browX, mouthY, cheekX, cheekY;
        public Vector3 brow;
        public Color irisColor, pupilColor, browColor, mouthColor, cheekColor;
    }

    static SoulCharacter NewCharacter(string name, Material body, out Transform rig)
    {
        var root = new GameObject("Soul_" + name);
        SceneManager.MoveGameObjectToScene(root, preview);
        var c = root.AddComponent<SoulCharacter>();
        rig = Pivot(root.transform, "Rig", Vector3.zero, Vector3.zero);
        var bodyMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshDir + "/SoulBody.asset") ?? RBox("SoulBody", Vector3.one, 0.07f);
        var b = Part(rig, "Body", bodyMesh, body, Vector3.zero);
        var r = b.GetComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.On;
        r.receiveShadows = true;
        c.rig = rig;
        c.bodyRenderer = r;
        return c;
    }

    // One big eye (white, iris with a dark pupil riding on it, two highlights), brows over it, a drawn mouth and cheeks.
    static void Face(SoulCharacter c, Transform rig, string n, FaceSpec f)
    {
        Transform face = Pivot(rig, "Face", Vector3.zero, Vector3.zero);
        c.faceRoot = face;
        const float eyeY = 0.02f;
        Material white = Flat(n, "EyeWhite", new Color(1f, 0.99f, 0.96f), 0.12f);
        Material iris = Flat(n, "Iris", f.irisColor, 0.1f), pupil = Flat(n, "Pupil", f.pupilColor, 0f), shine = Flat(n, "Shine", Color.white, 0f);

        Transform w = Disc(face, "EyeWhite", white, new Vector3(0f, eyeY, 0.506f), new Vector3(f.eye, 0.012f, f.eye));
        Transform ir = Disc(face, "Iris", iris, new Vector3(0f, eyeY, 0.522f), new Vector3(f.iris, 0.01f, f.iris));
        Transform pu = Part(ir, "Pupil", cylinder, pupil, new Vector3(0f, 1.1f, 0f));   // child: rides with the iris
        pu.localScale = new Vector3(0.55f, 1f, 0.55f);
        Transform s1 = Disc(face, "Shine", shine, new Vector3(-f.iris * 0.2f, eyeY + f.iris * 0.2f, 0.545f), new Vector3(f.iris * 0.3f, 0.006f, f.iris * 0.3f));
        Transform s2 = Disc(face, "Shine2", shine, new Vector3(f.iris * 0.18f, eyeY - f.iris * 0.2f, 0.545f), new Vector3(f.iris * 0.14f, 0.006f, f.iris * 0.14f));

        var brows = new List<Transform>();
        if (f.browColor.a > 0f)
            for (int s = -1; s <= 1; s += 2)
                brows.Add(Part(face, "Brow", RBox("Brow_" + n, f.brow, Mathf.Min(f.brow.y, f.brow.z) * 0.45f), Flat(n, "Brow", f.browColor, 0.4f), new Vector3(s * f.browX, f.browY, 0.514f)));
        var cheeks = new List<Transform>();
        if (f.cheekColor.a > 0f)
            for (int s = -1; s <= 1; s += 2)
                cheeks.Add(Disc(face, "Cheek", Flat(n, "Cheek", f.cheekColor, 0.5f), new Vector3(s * f.cheekX, f.cheekY, 0.503f), new Vector3(0.1f, 0.006f, 0.06f)));
        else
            for (int s = -1; s <= 1; s += 2) cheeks.Add(Disc(face, "Cheek", Flat(n, "CheekNone", Color.clear, 0f), new Vector3(s * f.cheekX, f.cheekY, 0.2f), Vector3.zero));

        var mouth = new GameObject("Mouth");
        mouth.transform.SetParent(face, false);
        mouth.transform.localPosition = new Vector3(0f, f.mouthY, 0.514f);
        var mf = mouth.AddComponent<MeshFilter>();
        var mr = mouth.AddComponent<MeshRenderer>();
        var mm = Flat(n, "Mouth", f.mouthColor, 0.2f);
        mm.SetFloat("_Cull", (float)CullMode.Off);
        mr.sharedMaterial = mm;
        Quiet(mr);

        c.eyeWhites = new[] { w };
        c.pupils = new[] { ir };
        c.shines = new[] { s1, s2 };
        c.brows = brows.ToArray();
        c.cheeks = cheeks.ToArray();
        c.mouth = mf;
        _ = pu;
    }

    static void Personality(SoulCharacter c, float eyeOpen, float browTilt, float smile, float mouthWidth, Vector2 blink, float blinkSec,
                            float slowBlink, float yawn, float breathe, float squash, float cheek, float pupilOffset)
    {
        c.eyeOpen = eyeOpen; c.browTilt = browTilt; c.smile = smile; c.mouthWidth = mouthWidth; c.blinkEvery = blink;
        c.blinkSeconds = blinkSec; c.slowBlinkChance = slowBlink; c.yawnEvery = yawn; c.breathe = breathe; c.landSquash = squash; c.cheekGlow = cheek;
        c.maxPupilOffset = pupilOffset;
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
        skin.sceneCharacter = false;
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

    // A flat disc facing out of +Z: the cylinder's axis turned onto Z. scale.x / scale.z are the DIAMETERS (the built-in
    // cylinder mesh is 2 wide), scale.y half its thickness.
    static Transform Disc(Transform parent, string name, Material mat, Vector3 pos, Vector3 scale)
    {
        Transform t = Part(parent, name, cylinder, mat, pos);
        t.localRotation = Quaternion.Euler(90f, 0f, 0f);
        t.localScale = new Vector3(scale.x * 0.5f, scale.y, scale.z * 0.5f);
        return t;
    }

    static void Quiet(Renderer r)
    {
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    // ================================================================== materials (matte, flat tone)

    static Material Flat(string n, string part, Color c, float tone, float ribFreq = 0f, float ribAmount = 0f)
    {
        string dir = MatDir + "/" + n;
        EnsureFolder(dir);
        string path = dir + "/M_" + n + "_" + part + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(flatShader); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != flatShader) m.shader = flatShader;
        m.SetColor("_BaseColor", c);
        m.SetFloat("_ToneAmount", tone);
        m.SetFloat("_RibFreq", ribFreq);
        m.SetFloat("_RibAmount", ribAmount);
        m.SetFloat("_Cull", (float)CullMode.Back);
        EditorUtility.SetDirty(m);
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

    // A box with rounded edges. Not named "Cube" on purpose: SkinApplier re-skins every runner mesh called "Cube".
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
                    Vector3 nn = d.sqrMagnitude > 1e-12f ? d.normalized : N;
                    v.Add(q + nn * r);
                    nm.Add(nn);
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

    static float[] Coords(float half, float r, int segs)
    {
        var list = new List<float>();
        float flat = half - r;
        for (int k = segs; k >= 0; k--) list.Add(-flat - r * Mathf.Tan(Mathf.PI * 0.25f * k / segs));
        for (int k = 0; k <= segs; k++) list.Add(flat + r * Mathf.Tan(Mathf.PI * 0.25f * k / segs));
        return list.ToArray();
    }

    // A triangle (base w on the pivot, apex h up) d thick - an ear, a fang, a fur tuft, a drop.
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
