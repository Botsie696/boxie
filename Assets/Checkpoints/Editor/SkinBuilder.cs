using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Checkpoints > Build Skins: the eight shop skins (materials on Boxicle/SkinCube, attached particles, death bursts,
/// synthesized sounds), Resources/SkinCatalog, the shop's own sounds and rail material, and SkinApplier on 3.6's
/// CheckpointRun. Re-runnable: assets are refreshed in place (ids and prices too - the save keeps only ids).
/// </summary>
public static class SkinBuilder
{
    const string Root = "Assets/Checkpoints/Shop";
    const string SkinDir = Root + "/Skins", MatDir = Root + "/Materials", FxDir = Root + "/Particles", AudioDir = Root + "/Audio";
    const string CatalogPath = "Assets/Checkpoints/Resources/SkinCatalog.asset";
    public const string RailMaterialPath = MatDir + "/M_ShopRail.mat";

    struct Def
    {
        public string id, name, tag; public SkinRarity rarity; public int price; public int mode;
        public Color baseC, c2, c3, edge; public float edgeW, glow, alpha, scale, speed, squash;
        public bool trail; public Color th, tm, tt;
        public string attach; public Color burstA, burstB; public bool frost;
    }

    static Def[] Defs() => new[]
    {
        new Def { id = "classic", name = "Classic", tag = "Where it all began.", rarity = SkinRarity.Common, price = 0, mode = 0,
                  baseC = new Color(0.96f, 0.97f, 1f), edge = new Color(0.78f, 0.84f, 0.95f), edgeW = 0.03f, glow = 1f, alpha = 1f, scale = 6f, speed = 1f, squash = 0f,
                  burstA = Color.white, burstB = new Color(0.8f, 0.86f, 1f) },
        new Def { id = "neon", name = "Neon Wireframe", tag = "Lives on the edges.", rarity = SkinRarity.Rare, price = 500, mode = 1,
                  baseC = new Color(0.03f, 0.04f, 0.09f), edge = new Color(0.2f, 1f, 0.92f), edgeW = 0.075f, glow = 1.7f, alpha = 1f, scale = 6f, speed = 1f, squash = 0.04f,
                  trail = true, th = new Color(0.7f, 1f, 0.97f), tm = new Color(0.2f, 1f, 0.92f), tt = new Color(1f, 0.2f, 0.85f, 0f),
                  burstA = new Color(0.2f, 1f, 0.92f), burstB = new Color(1f, 0.25f, 0.85f) },
        new Def { id = "glass", name = "Glass", tag = "See right through it.", rarity = SkinRarity.Rare, price = 750, mode = 2,
                  baseC = new Color(0.5f, 0.82f, 1f), edge = new Color(0.92f, 0.98f, 1f), edgeW = 0.045f, glow = 1f, alpha = 0.32f, scale = 6f, speed = 1f, squash = 0.05f,
                  trail = true, th = new Color(0.95f, 1f, 1f), tm = new Color(0.6f, 0.88f, 1f), tt = new Color(0.6f, 0.88f, 1f, 0f),
                  burstA = new Color(0.85f, 0.97f, 1f), burstB = new Color(0.5f, 0.82f, 1f) },
        new Def { id = "gold", name = "Gold", tag = "Rich, shiny, a little smug.", rarity = SkinRarity.Epic, price = 1000, mode = 3,
                  baseC = new Color(1f, 0.8f, 0.3f), c2 = new Color(1f, 0.93f, 0.62f), c3 = new Color(0.55f, 0.32f, 0.06f), edge = new Color(1f, 0.96f, 0.78f),
                  edgeW = 0.04f, glow = 1f, alpha = 1f, scale = 6f, speed = 1f, squash = 0.03f,
                  trail = true, th = new Color(1f, 0.97f, 0.8f), tm = new Color(1f, 0.78f, 0.25f), tt = new Color(1f, 0.6f, 0.1f, 0f), attach = "glints",
                  burstA = new Color(1f, 0.85f, 0.3f), burstB = new Color(1f, 0.97f, 0.75f) },
        new Def { id = "lava", name = "Lava", tag = "Leaves a warm trail.", rarity = SkinRarity.Epic, price = 1500, mode = 4,
                  baseC = new Color(0.16f, 0.08f, 0.07f), edge = new Color(1f, 0.42f, 0.08f), edgeW = 0.04f, glow = 1.9f, alpha = 1f, scale = 2.6f, speed = 1f, squash = 0.08f,
                  trail = true, th = new Color(1f, 0.85f, 0.4f), tm = new Color(1f, 0.38f, 0.08f), tt = new Color(0.6f, 0.05f, 0.02f, 0f), attach = "embers",
                  burstA = new Color(1f, 0.45f, 0.08f), burstB = new Color(0.25f, 0.1f, 0.08f) },
        new Def { id = "ice", name = "Ice", tag = "Shatters in frost.", rarity = SkinRarity.Epic, price = 2500, mode = 5,
                  baseC = new Color(0.66f, 0.88f, 1f), c2 = new Color(0.95f, 1f, 1f), edge = new Color(1f, 1f, 1f), edgeW = 0.04f, glow = 1f, alpha = 1f, scale = 3f, speed = 1f, squash = 0.05f,
                  trail = true, th = Color.white, tm = new Color(0.7f, 0.92f, 1f), tt = new Color(0.7f, 0.92f, 1f, 0f), attach = "frost",
                  burstA = Color.white, burstB = new Color(0.66f, 0.88f, 1f), frost = true },
        new Def { id = "pixel", name = "Pixel", tag = "8 bits of attitude.", rarity = SkinRarity.Legendary, price = 3500, mode = 6,
                  baseC = new Color(1f, 0.34f, 0.56f), c2 = new Color(0.28f, 0.84f, 1f), c3 = new Color(1f, 0.85f, 0.24f), edge = new Color(0.14f, 0.09f, 0.24f),
                  edgeW = 0.03f, glow = 1f, alpha = 1f, scale = 6f, speed = 1f, squash = 0.12f,
                  trail = true, th = new Color(1f, 0.85f, 0.24f), tm = new Color(1f, 0.34f, 0.56f), tt = new Color(0.28f, 0.84f, 1f, 0f),
                  burstA = new Color(1f, 0.34f, 0.56f), burstB = new Color(0.28f, 0.84f, 1f) },
        new Def { id = "galaxy", name = "Galaxy", tag = "A universe on every face.", rarity = SkinRarity.Legendary, price = 5000, mode = 7,
                  baseC = new Color(0.06f, 0.03f, 0.18f), c2 = new Color(0.48f, 0.18f, 0.72f), edge = new Color(1f, 0.96f, 1f), edgeW = 0.03f, glow = 1.5f, alpha = 1f, scale = 3f, speed = 1f, squash = 0.06f,
                  trail = true, th = new Color(1f, 0.92f, 1f), tm = new Color(0.62f, 0.35f, 1f), tt = new Color(0.2f, 0.1f, 0.6f, 0f), attach = "stars",
                  burstA = new Color(0.62f, 0.35f, 1f), burstB = Color.white },
    };

    [MenuItem("Tools/Checkpoints/Build Skins")]
    public static void Build()
    {
        foreach (string d in new[] { SkinDir, MatDir, FxDir, AudioDir, "Assets/Checkpoints/Resources" }) EnsureFolder(d);
        Shader shader = Shader.Find("Boxicle/SkinCube");
        Material mote = AssetDatabase.LoadAssetAtPath<Material>("Assets/Checkpoints/Menu/M_MenuMote.mat");

        var rail = Mat(RailMaterialPath, shader);
        SetSkinMat(rail, 0, new Color(0.9f, 0.92f, 0.98f), Color.gray, Color.gray, new Color(0.66f, 0.7f, 0.82f), 0.035f, 1f, 1f, 6f, 1f);

        var catalog = AssetDatabase.LoadAssetAtPath<SkinCatalog>(CatalogPath);
        if (catalog == null) { catalog = ScriptableObject.CreateInstance<SkinCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
        catalog.skins.Clear();

        foreach (Def d in Defs())
        {
            Material m = Mat(MatDir + "/M_Skin_" + d.id + ".mat", shader);
            SetSkinMat(m, d.mode, d.baseC, d.c2, d.c3, d.edge, d.edgeW, d.glow, d.alpha, d.scale, d.speed);

            string path = SkinDir + "/Skin_" + d.id + ".asset";
            var skin = AssetDatabase.LoadAssetAtPath<CubeSkin>(path);
            if (skin == null) { skin = ScriptableObject.CreateInstance<CubeSkin>(); AssetDatabase.CreateAsset(skin, path); }
            skin.id = d.id; skin.displayName = d.name; skin.tagline = d.tag; skin.rarity = d.rarity; skin.price = d.price;
            skin.material = m;
            skin.keepSceneLook = d.id == SaveSystem.DefaultSkin;
            skin.uiColor = d.id == "neon" ? d.edge : d.mode == 7 ? d.c2 : d.baseC;
            skin.overrideTrail = d.trail; skin.trailHead = d.th; skin.trailMid = d.tm; skin.trailTail = d.tt;
            skin.landingSquash = d.squash;
            skin.attachedParticles = string.IsNullOrEmpty(d.attach) ? null : Fx(d.attach, d, mote);
            skin.deathBurstPrefab = d.id == SaveSystem.DefaultSkin ? null : Burst(d, mote);
            skin.rollSfx = d.id == SaveSystem.DefaultSkin ? null : Wav(AudioDir + "/roll_" + d.id + ".wav", RollSound(d.id));
            EditorUtility.SetDirty(skin);
            catalog.skins.Add(skin);
        }
        EditorUtility.SetDirty(catalog);
        Wav(AudioDir + "/shop_buy.wav", Chime());
        Wav(AudioDir + "/shop_equip.wav", Snap());
        Wav(AudioDir + "/shop_coin.wav", Ping(1900f, 2500f, 0.06f));
        Wav(AudioDir + "/shop_deny.wav", Deny());
        AssetDatabase.SaveAssets();
        AddApplierTo36(catalog);
        Debug.Log("[Skins] built " + catalog.skins.Count + " skins.");
    }

    /// <summary>The shop's assets, for MainMenuBuilder.</summary>
    public static void Wire(ShopController shop)
    {
        shop.catalog = AssetDatabase.LoadAssetAtPath<SkinCatalog>(CatalogPath);
        shop.railMaterial = AssetDatabase.LoadAssetAtPath<Material>(RailMaterialPath);
        shop.trailMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Checkpoints/Menu/M_MenuTrail.mat");
        shop.buySfx = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/shop_buy.wav");
        shop.equipSfx = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/shop_equip.wav");
        shop.coinSfx = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/shop_coin.wav");
        shop.denySfx = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/shop_deny.wav");
    }

    // ------------------------------------------------------------------ materials

    static Material Mat(string path, Shader shader)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        return m;
    }

    static void SetSkinMat(Material m, int mode, Color b, Color c2, Color c3, Color edge, float w, float glow, float alpha, float scale, float speed)
    {
        m.SetFloat("_Mode", mode);
        m.SetColor("_BaseColor", b); m.SetColor("_Color2", c2); m.SetColor("_Color3", c3); m.SetColor("_EdgeColor", edge);
        m.SetFloat("_EdgeWidth", w); m.SetFloat("_Glow", glow); m.SetFloat("_Alpha", alpha); m.SetFloat("_Scale", scale); m.SetFloat("_Speed", speed);
        bool glass = alpha < 0.99f;
        m.SetFloat("_SrcBlend", glass ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : 1f);
        m.SetFloat("_DstBlend", glass ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : 0f);
        m.SetFloat("_ZWrite", glass ? 0f : 1f);
        m.renderQueue = glass ? 3000 : 2000;
        m.SetOverrideTag("RenderType", glass ? "Transparent" : "Opaque");
        EditorUtility.SetDirty(m);
    }

    // ------------------------------------------------------------------ particles (squares, never round glows)

    static GameObject Fx(string kind, Def d, Material mat)
    {
        string path = FxDir + "/Attach_" + d.id + ".prefab";
        var go = new GameObject("Attach_" + d.id);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.maxParticles = 80;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI);
        var em = ps.emission;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = Vector3.one * 0.8f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0.2f)));
        switch (kind)
        {
            case "embers":
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.13f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
                main.gravityModifier = -0.25f;
                em.rateOverDistance = 7f; em.rateOverTime = 4f;
                col.color = Grad(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.25f, 0.05f));
                break;
            case "frost":
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
                em.rateOverDistance = 4f; em.rateOverTime = 3f;
                col.color = Grad(Color.white, new Color(0.66f, 0.88f, 1f));
                break;
            case "stars":
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
                em.rateOverTime = 9f;
                shape.scale = Vector3.one * 1.4f;
                col.color = Grad(Color.white, new Color(0.62f, 0.35f, 1f));
                break;
            default: // glints
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
                em.rateOverTime = 5f;
                col.color = Grad(new Color(1f, 0.98f, 0.85f), new Color(1f, 0.8f, 0.3f));
                break;
        }
        Renderer(ps, mat);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static GameObject Burst(Def d, Material mat)
    {
        string path = FxDir + "/Burst_" + d.id + ".prefab";
        var go = new GameObject("Burst_" + d.id);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, d.frost ? 1.4f : 1.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(d.frost ? 2f : 3f, d.frost ? 8f : 7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, d.frost ? 0.26f : 0.2f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI);
        main.gravityModifier = d.frost ? 0.35f : 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(d.burstA, d.burstB);
        main.maxParticles = 80;
        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(d.frost ? 56 : 34)) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.35f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = Grad(Color.white, Color.white);
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        Renderer(ps, mat);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static Gradient Grad(Color a, Color b)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    static void Renderer(ParticleSystem ps, Material mat)
    {
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sharedMaterial = mat;
    }

    // ------------------------------------------------------------------ sounds (synthesized, mono 44.1 kHz)

    const int Rate = 44100;

    static float[] Buf(float seconds) => new float[Mathf.CeilToInt(seconds * Rate)];
    static float Env(int i, int n, float attack = 0.004f) { float t = (float)i / Rate; return Mathf.Clamp01(t / attack) * Mathf.Pow(1f - (float)i / n, 2.2f); }

    static float[] RollSound(string id)
    {
        var rnd = new System.Random(id.GetHashCode());
        switch (id)
        {
            case "neon": { var b = Buf(0.07f); for (int i = 0; i < b.Length; i++) { float t = (float)i / Rate; float f = Mathf.Lerp(900f, 1400f, t / 0.07f); b[i] = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * f * t)) * 0.18f * Env(i, b.Length); } return b; }
            case "glass": return Ping(2400f, 3600f, 0.09f);
            case "gold": { var b = Buf(0.1f); for (int i = 0; i < b.Length; i++) { float t = (float)i / Rate; b[i] = (Mathf.Sin(2 * Mathf.PI * 1800f * t) + 0.6f * Mathf.Sin(2 * Mathf.PI * 2730f * t) + 0.3f * Mathf.Sin(2 * Mathf.PI * 4120f * t)) * 0.16f * Env(i, b.Length); } return b; }
            case "lava": { var b = Buf(0.09f); for (int i = 0; i < b.Length; i++) { float t = (float)i / Rate; b[i] = (Mathf.Sin(2 * Mathf.PI * 120f * t) * 0.6f + (float)(rnd.NextDouble() * 2 - 1) * 0.25f) * 0.45f * Env(i, b.Length); } return b; }
            case "ice": return Ping(3000f, 4500f, 0.07f);
            case "pixel": { var b = Buf(0.06f); for (int i = 0; i < b.Length; i++) { float t = (float)i / Rate; float f = t < 0.03f ? 660f : 880f; b[i] = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * f * t)) * 0.14f * Env(i, b.Length, 0.002f); } return b; }
            case "galaxy": { var b = Buf(0.12f); for (int i = 0; i < b.Length; i++) { float t = (float)i / Rate; float f = Mathf.Lerp(520f, 780f, t / 0.12f); b[i] = (Mathf.Sin(2 * Mathf.PI * f * t) + 0.3f * Mathf.Sin(2 * Mathf.PI * f * 2.01f * t)) * 0.2f * Env(i, b.Length, 0.01f); } return b; }
            default: return Ping(1200f, 1800f, 0.04f);
        }
    }

    static float[] Ping(float f1, float f2, float seconds)
    {
        var b = Buf(seconds);
        for (int i = 0; i < b.Length; i++) { float t = (float)i / Rate; b[i] = (Mathf.Sin(2 * Mathf.PI * f1 * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * f2 * t)) * 0.22f * Env(i, b.Length, 0.002f); }
        return b;
    }

    static float[] Chime()
    {
        float[] notes = { 1046.5f, 1318.5f, 1568f, 2093f };
        var b = Buf(0.9f);
        for (int n = 0; n < notes.Length; n++)
        {
            int start = (int)(n * 0.08f * Rate), len = b.Length - start;
            for (int i = 0; i < len; i++) { float t = (float)i / Rate; b[start + i] += (Mathf.Sin(2 * Mathf.PI * notes[n] * t) + 0.25f * Mathf.Sin(2 * Mathf.PI * notes[n] * 2f * t)) * 0.13f * Env(i, len, 0.003f); }
        }
        return b;
    }

    static float[] Snap()
    {
        var rnd = new System.Random(7);
        var b = Buf(0.09f);
        for (int i = 0; i < b.Length; i++)
        {
            float t = (float)i / Rate;
            float click = t < 0.012f ? (float)(rnd.NextDouble() * 2 - 1) * (1f - t / 0.012f) * 0.5f : 0f;
            b[i] = click + Mathf.Sin(2 * Mathf.PI * 1600f * t) * 0.2f * Env(i, b.Length, 0.002f);
        }
        return b;
    }

    static float[] Deny()
    {
        var b = Buf(0.22f);
        for (int i = 0; i < b.Length; i++) { float t = (float)i / Rate; float gate = (t < 0.08f || (t > 0.12f && t < 0.2f)) ? 1f : 0f; b[i] = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 160f * t)) * 0.16f * gate; }
        return b;
    }

    static AudioClip Wav(string path, float[] samples)
    {
        using (var fs = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            int bytes = samples.Length * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (float s in samples) w.Write((short)Mathf.Clamp(s * 32767f, -32768f, 32767f));
        }
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    // ------------------------------------------------------------------ 3.6

    static void AddApplierTo36(SkinCatalog catalog)
    {
        string scenePath = CheckpointSetup.ScenePath;
        Scene sc = SceneManager.GetSceneByPath(scenePath);
        bool wasOpen = sc.IsValid() && sc.isLoaded;
        if (!wasOpen) sc = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            CheckpointRunController ctl = null;
            foreach (GameObject r in sc.GetRootGameObjects()) { ctl = r.GetComponentInChildren<CheckpointRunController>(true); if (ctl != null) break; }
            if (ctl == null) { Debug.LogError("[Skins] no CheckpointRunController in 3.6"); return; }
            var ap = ctl.GetComponent<SkinApplier>() ?? ctl.gameObject.AddComponent<SkinApplier>();
            ap.catalog = catalog;
            EditorUtility.SetDirty(ap);
            EditorSceneManager.MarkSceneDirty(sc);
            EditorSceneManager.SaveScene(sc);
        }
        finally { if (!wasOpen) EditorSceneManager.CloseScene(sc, true); }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ------------------------------------------------------------------ dev menu

    [MenuItem("Tools/Checkpoints/Dev +1000 Coins")]
    static void DevCoins() { SaveSystem.AddCoins(1000); SaveSystem.Flush(); Debug.Log("[Dev] coins = " + SaveSystem.Coins); }

    [MenuItem("Tools/Checkpoints/Dev Reset Save")]
    static void DevReset()
    {
        if (!EditorUtility.DisplayDialog("Reset save", "Forget coins, unlocked checkpoints and skins?", "Reset", "Cancel")) return;
        SaveSystem.ResetAll();
        Debug.Log("[Dev] save reset: " + SaveSystem.FilePath);
    }
}
