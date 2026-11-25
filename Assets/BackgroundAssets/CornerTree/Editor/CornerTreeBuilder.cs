using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools > Corner Props > Build MV Tree
///
/// The Monument Valley golden tree in a cup (user's reference: a cream cup with a dark plum inside, a thin red
/// branching trunk, and ragged golden-yellow cube clumps for leaves), built from code so it stays light for a phone:
///   * one mesh, four submeshes - cup (cream), cup inside (plum), trunk (red), leaves (gold) - ~2.5 k triangles;
///   * the cup is a lathe with smooth sides; the trunk is tapered hexagonal tubes; each leaf clump is a cube whose
///     face grid is jittered (edges pushed out the most) and flat shaded, which gives the ragged faceted outline;
///   * URP Lit materials (+ transparent twins for CornerPropFader, so it fades with the legs), the leaves with a
///     little emission so they glow against the dark like the reference;
///   * a few gold leaf flakes drifting down out of the crown (ParticleSystem, local space, 10 max);
///   * prefab CornerTree_MV with its pivot in the middle of the cup's base, added to CornerProps_Pillars as a
///     special prop (one sideways corner in every 18-22).
/// Re-runnable: everything is overwritten. Same random seed every time, so the tree keeps its shape.
/// </summary>
public static class CornerTreeBuilder
{
    const string Dir = "Assets/BackgroundAssets/CornerTree";
    const string SetPath = "Assets/BackgroundAssets/pillars/CornerProps_Pillars.asset";

    static readonly Color Cream = new Color32(0xEE, 0xE6, 0xD3, 0xFF);
    static readonly Color Plum = new Color32(0x3A, 0x24, 0x33, 0xFF);
    static readonly Color TrunkRed = new Color32(0xC9, 0x33, 0x3F, 0xFF);
    static readonly Color Gold = new Color32(0xF8, 0xC7, 0x35, 0xFF);

    [MenuItem("Tools/Corner Props/Build MV Tree")]
    public static void BuildMenu() => Build();

    const string FlowerSetPath = Dir + "/CornerProps_FlowerTrees.asset";
    static readonly Color Pink = new Color32(0xFF, 0x6E, 0xB0, 0xFF);
    static readonly Color Cream2 = new Color32(0xFF, 0xF1, 0xC8, 0xFF);
    static readonly Color Core = new Color32(0xFF, 0xE0, 0x5A, 0xFF);

    /// <summary>
    /// The MV tree with glowing blossoms on its gold clumps (pink and cream petals, a bright core), as a one-prop
    /// CornerPropSet used at EVERY sideways corner (the pillars are replaced). LDR project: the glow is a bright base
    /// colour + strong emission that the bloom picks up. Re-runnable; the plain tree and its set are not touched.
    /// </summary>
    [MenuItem("Tools/Corner Props/Build MV Flower Tree (every corner)")]
    public static CornerPropSet BuildFlowerTree()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/BackgroundAssets", "CornerTree");

        var mb = new MeshBuild(7);
        BuildCup(mb);
        var rng = new System.Random(7177);
        BuildTrunk(mb, rng);
        BuildLeaves(mb, rng, true);
        Mesh mesh = SaveAsset(mb.ToMesh("CornerTree_MV_Flowers_Mesh"), $"{Dir}/CornerTree_MV_Flowers_Mesh.asset");

        Material cup = LitMaterial($"{Dir}/Tree_Cup.mat", Cream, 0.25f, Cream * 0.3f);
        Material inside = LitMaterial($"{Dir}/Tree_CupInside.mat", Plum, 0.1f, Color.black);
        Material trunk = LitMaterial($"{Dir}/Tree_Trunk.mat", TrunkRed, 0.15f, TrunkRed * 0.35f);
        Material leaves = LitMaterial($"{Dir}/Tree_Leaves.mat", Gold, 0.05f, Gold * 0.8f);
        Material petalA = LitMaterial($"{Dir}/Flower_Pink.mat", Pink, 0.1f, Pink * 2.6f);
        Material petalB = LitMaterial($"{Dir}/Flower_Cream.mat", Cream2, 0.1f, Cream2 * 2.4f);
        Material core = LitMaterial($"{Dir}/Flower_Core.mat", Core, 0.1f, Core * 3.2f);
        Material[] opaque = { cup, inside, trunk, leaves, petalA, petalB, core };
        var twins = new Material[opaque.Length];
        for (int i = 0; i < opaque.Length; i++)
            twins[i] = CornerPillarBuilder.SaveFadeTwin(opaque[i], $"{Dir}/{opaque[i].name}_Fade.mat");
        Material flakes = ParticleMaterial($"{Dir}/Tree_LeafFlakes.mat", Gold);

        var go = new GameObject("CornerTree_MV_Flowers");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = opaque;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        var fader = go.AddComponent<CornerPropFader>();
        fader.opaqueMaterials = opaque;
        fader.fadeTwins = twins;
        AddLeafFlakes(go.transform, flakes);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Dir}/CornerTree_MV_Flowers.prefab");
        Object.DestroyImmediate(go);

        var set = AssetDatabase.LoadAssetAtPath<CornerPropSet>(FlowerSetPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<CornerPropSet>();
            AssetDatabase.CreateAsset(set, FlowerSetPath);
        }
        set.props = new[] { prefab };
        set.legsPerProp = 5;
        set.specialProps = new GameObject[0];
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Debug.Log($"[CornerTreeBuilder] CornerTree_MV_Flowers: {mesh.triangles.Length / 3} triangles -> {FlowerSetPath}");
        return set;
    }

    public static GameObject Build()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/BackgroundAssets", "CornerTree");

        // --- mesh ---
        var mb = new MeshBuild(4);
        BuildCup(mb);
        var rng = new System.Random(7177);
        BuildTrunk(mb, rng);
        BuildLeaves(mb, rng);
        Mesh mesh = mb.ToMesh("CornerTree_MV_Mesh");
        mesh = SaveAsset(mesh, $"{Dir}/CornerTree_MV_Mesh.asset");

        // --- materials ---
        Material cup = LitMaterial($"{Dir}/Tree_Cup.mat", Cream, 0.25f, Cream * 0.3f);
        Material inside = LitMaterial($"{Dir}/Tree_CupInside.mat", Plum, 0.1f, Color.black);
        Material trunk = LitMaterial($"{Dir}/Tree_Trunk.mat", TrunkRed, 0.15f, TrunkRed * 0.35f);
        Material leaves = LitMaterial($"{Dir}/Tree_Leaves.mat", Gold, 0.05f, Gold * 0.8f);   // bright gold even in the dark chasm
        Material[] opaque = { cup, inside, trunk, leaves };
        var twins = new Material[opaque.Length];
        for (int i = 0; i < opaque.Length; i++)
            twins[i] = CornerPillarBuilder.SaveFadeTwin(opaque[i], $"{Dir}/{opaque[i].name}_Fade.mat");
        Material flakes = ParticleMaterial($"{Dir}/Tree_LeafFlakes.mat", Gold);

        // --- prefab ---
        var go = new GameObject("CornerTree_MV");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = opaque;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        var fader = go.AddComponent<CornerPropFader>();
        fader.opaqueMaterials = opaque;
        fader.fadeTwins = twins;
        AddLeafFlakes(go.transform, flakes);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Dir}/CornerTree_MV.prefab");
        Object.DestroyImmediate(go);

        // --- into the set as a special ---
        var set = AssetDatabase.LoadAssetAtPath<CornerPropSet>(SetPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<CornerPropSet>();
            AssetDatabase.CreateAsset(set, SetPath);
        }
        set.specialProps = new[] { prefab };
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        Debug.Log($"[CornerTreeBuilder] CornerTree_MV: {mesh.triangles.Length / 3} triangles, {mesh.bounds.size.y:0.00} tall, {Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z):0.00} wide -> special prop of {SetPath}");
        return prefab;
    }

    // ------------------------------------------------------------------
    // Parts (model units: the cup's base is at y = 0, the whole tree ~2.6 tall)
    // ------------------------------------------------------------------

    const int CupSides = 28;
    const float CupTop = 0.36f;

    static void BuildCup(MeshBuild mb)
    {
        // Outside profile, bottom centre -> rounded lip (r, y). Flower-pot flare like the reference.
        Vector2[] outside =
        {
            new Vector2(0f, 0f), new Vector2(0.22f, 0f), new Vector2(0.245f, 0.015f), new Vector2(0.265f, 0.05f),
            new Vector2(0.30f, 0.27f), new Vector2(0.325f, 0.30f), new Vector2(0.335f, 0.33f),
            new Vector2(0.33f, 0.352f), new Vector2(0.315f, CupTop), new Vector2(0.29f, CupTop),
        };
        // Inside: thin wall down to the plum floor.
        Vector2[] inner =
        {
            new Vector2(0.29f, CupTop), new Vector2(0.272f, 0.345f), new Vector2(0.262f, 0.31f), new Vector2(0f, 0.31f),
        };
        Lathe(mb, 0, outside);
        Lathe(mb, 1, inner);
    }

    /// <summary>Spins a (radius, height) profile around Y: smooth around, a crease at every profile point.</summary>
    static void Lathe(MeshBuild mb, int sub, Vector2[] profile)
    {
        for (int k = 0; k + 1 < profile.Length; k++)
        {
            Vector2 a = profile[k], b = profile[k + 1];
            Vector2 along = (b - a).normalized;
            Vector2 n2 = new Vector2(along.y, -along.x);   // outward for a profile that goes up and out
            for (int s = 0; s < CupSides; s++)
            {
                float t0 = s * Mathf.PI * 2f / CupSides, t1 = (s + 1) * Mathf.PI * 2f / CupSides;
                Vector3 d0 = new Vector3(Mathf.Cos(t0), 0f, Mathf.Sin(t0)), d1 = new Vector3(Mathf.Cos(t1), 0f, Mathf.Sin(t1));
                Vector3 p00 = d0 * a.x + Vector3.up * a.y, p01 = d1 * a.x + Vector3.up * a.y;
                Vector3 p10 = d0 * b.x + Vector3.up * b.y, p11 = d1 * b.x + Vector3.up * b.y;
                Vector3 n0 = (d0 * n2.x + Vector3.up * n2.y).normalized, n1 = (d1 * n2.x + Vector3.up * n2.y).normalized;
                mb.Quad(sub, p00, p10, p11, p01, n0, n0, n1, n1);
            }
        }
    }

    struct Branch { public Vector3[] points; public float r0, r1; }

    static readonly List<Vector3> leafSpots = new List<Vector3>();

    static void BuildTrunk(MeshBuild mb, System.Random rng)
    {
        // The main stem rises out of the cup with a few kinks; side branches end where a leaf clump sits.
        var stem = new[]
        {
            new Vector3(0f, 0.30f, 0f), new Vector3(0.015f, 0.62f, 0f), new Vector3(0.03f, 0.95f, 0.01f),
            new Vector3(-0.03f, 1.35f, 0f), new Vector3(0.01f, 1.72f, -0.01f), new Vector3(-0.04f, 2.08f, 0f),
            new Vector3(-0.03f, 2.30f, 0f),
        };
        Tube(mb, stem, 0.055f, 0.025f);

        Branch[] branches =
        {
            new Branch { points = new[] { new Vector3(0.025f, 0.92f, 0f), new Vector3(0.20f, 1.06f, -0.04f), new Vector3(0.33f, 1.14f, -0.08f) }, r0 = 0.03f, r1 = 0.018f },
            new Branch { points = new[] { new Vector3(0.0f, 1.12f, 0f), new Vector3(-0.15f, 1.12f, 0.06f), new Vector3(-0.27f, 1.10f, 0.10f) }, r0 = 0.028f, r1 = 0.017f },
            new Branch { points = new[] { new Vector3(-0.02f, 1.40f, 0f), new Vector3(-0.20f, 1.50f, -0.02f), new Vector3(-0.34f, 1.56f, -0.04f) }, r0 = 0.03f, r1 = 0.018f },
            new Branch { points = new[] { new Vector3(0.0f, 1.62f, 0f), new Vector3(0.20f, 1.70f, 0.03f), new Vector3(0.36f, 1.78f, 0.05f) }, r0 = 0.03f, r1 = 0.018f },
            new Branch { points = new[] { new Vector3(-0.03f, 1.98f, 0f), new Vector3(-0.17f, 2.05f, 0.05f), new Vector3(-0.29f, 2.10f, 0.09f) }, r0 = 0.025f, r1 = 0.016f },
            new Branch { points = new[] { new Vector3(0.0f, 1.30f, 0f), new Vector3(0.06f, 1.34f, 0.15f), new Vector3(0.08f, 1.36f, 0.24f) }, r0 = 0.024f, r1 = 0.015f },
        };
        leafSpots.Clear();
        foreach (Branch b in branches)
        {
            Tube(mb, b.points, b.r0, b.r1);
            leafSpots.Add(b.points[b.points.Length - 1]);
        }
        leafSpots.Add(stem[stem.Length - 1] + new Vector3(0f, 0.12f, 0f));   // crown on top of the stem
    }

    /// <summary>A tapered hexagonal tube through the points (flat sides, like the reference's faceted branches).</summary>
    static void Tube(MeshBuild mb, Vector3[] pts, float r0, float r1)
    {
        const int sides = 6;
        float total = 0f;
        for (int i = 1; i < pts.Length; i++) total += Vector3.Distance(pts[i - 1], pts[i]);
        float run = 0f;
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            Vector3 a = pts[i], b = pts[i + 1];
            float len = Vector3.Distance(a, b);
            float ra = Mathf.Lerp(r0, r1, run / total), rb = Mathf.Lerp(r0, r1, (run + len) / total);
            run += len;
            Vector3 axis = (b - a).normalized;
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 up = Vector3.Cross(side, axis);
            for (int s = 0; s < sides; s++)
            {
                float t0 = s * Mathf.PI * 2f / sides, t1 = (s + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = side * Mathf.Cos(t0) + up * Mathf.Sin(t0), d1 = side * Mathf.Cos(t1) + up * Mathf.Sin(t1);
                Vector3 n = (d0 + d1).normalized;
                mb.Quad(2, a + d0 * ra, b + d0 * rb, b + d1 * rb, a + d1 * ra, n, n, n, n);
            }
            // A small cap at each joint hides the gap where two kinked pieces meet.
            if (i + 1 < pts.Length - 1) Cap(mb, b, axis, side, up, rb * 1.05f);
        }
        Cap(mb, pts[pts.Length - 1], (pts[pts.Length - 1] - pts[pts.Length - 2]).normalized, Vector3.right, Vector3.forward, r1);
    }

    static void Cap(MeshBuild mb, Vector3 c, Vector3 axis, Vector3 side, Vector3 up, float r)
    {
        // A tiny diamond ball: 8 faces.
        Vector3[] d = { side, up, -side, -up };
        for (int k = 0; k < 4; k++)
        {
            Vector3 p0 = c + d[k] * r, p1 = c + d[(k + 1) % 4] * r;
            mb.FlatTri(2, p0, p1, c + axis * r, c);
            mb.FlatTri(2, p1, p0, c - axis * r, c);
        }
    }

    static void BuildLeaves(MeshBuild mb, System.Random rng, bool flowers = false)
    {
        // Sizes by spot, matched to the reference: big clumps left/right, the top one medium, small low ones.
        float[] sizes = { 0.40f, 0.36f, 0.50f, 0.52f, 0.40f, 0.34f, 0.44f };
        var frng = new System.Random(991);   // flowers have their own sequence: the plain tree keeps its exact shape
        int flowerCount = 0;
        for (int i = 0; i < leafSpots.Count; i++)
        {
            float s = sizes[i % sizes.Length];
            Quaternion rot = Quaternion.Euler(Rand(rng, -35f, 35f), Rand(rng, 0f, 90f), Rand(rng, -35f, 35f));
            Vector3 centre = leafSpots[i] + Vector3.up * (s * 0.25f);
            RaggedCube(mb, 3, centre, s, rot, rng);
            if (!flowers) continue;
            // Blossoms sit on the outside of the clump, mostly on the upper and outer faces where the camera sees them.
            int n = 10;
            for (int f = 0; f < n; f++)
            {
                Vector3 dir = new Vector3(Rand(frng, -1f, 1f), Rand(frng, -0.15f, 1f), Rand(frng, -1f, 1f)).normalized;
                Vector3 p = centre + dir * (s * 0.66f);
                Flower(mb, p, dir, Rand(frng, 0.12f, 0.18f), frng, flowerCount++ % 2 == 0 ? 4 : 5);
            }
        }
    }

    /// <summary>A four-petal blossom lying on a clump face: petals in `petalSub`, a bright centre in submesh 6. Faces point along n.</summary>
    static void Flower(MeshBuild mb, Vector3 p, Vector3 n, float size, System.Random rng, int petalSub)
    {
        Vector3 t = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 b = Vector3.Cross(n, t);
        float roll = Rand(rng, 0f, Mathf.PI);
        Vector3 inside = p - n * size * 2f;
        Vector3 c = p + n * 0.012f;
        for (int k = 0; k < 4; k++)
        {
            float a = roll + k * Mathf.PI * 0.5f;
            Vector3 d = t * Mathf.Cos(a) + b * Mathf.Sin(a);
            Vector3 side = Vector3.Cross(n, d);
            Vector3 tip = c + d * size;
            Vector3 mid = c + d * size * 0.5f;
            Vector3 l = mid + side * size * 0.34f, r = mid - side * size * 0.34f;
            mb.FlatTri(petalSub, c, l, tip, inside);
            mb.FlatTri(petalSub, c, tip, r, inside);
        }
        Vector3 cc = c + n * 0.014f;
        float cr = size * 0.30f;
        for (int k = 0; k < 4; k++)
        {
            float a0 = roll + k * Mathf.PI * 0.5f, a1 = a0 + Mathf.PI * 0.5f;
            Vector3 p0 = cc + (t * Mathf.Cos(a0) + b * Mathf.Sin(a0)) * cr, p1 = cc + (t * Mathf.Cos(a1) + b * Mathf.Sin(a1)) * cr;
            mb.FlatTri(6, cc, p0, p1, inside);
        }
    }

    /// <summary>
    /// A cube of side s whose faces are a 4 x 4 grid, every grid point jittered - the face edges and corners pushed
    /// outward much more than the middle - and flat shaded, so it reads as a clump of faceted leaves with a ragged
    /// outline, still clearly a cube.
    /// </summary>
    static void RaggedCube(MeshBuild mb, int sub, Vector3 centre, float s, Quaternion rot, System.Random rng)
    {
        const int n = 4;
        var cache = new Dictionary<Vector3Int, Vector3>();   // shared grid points stay welded between faces
        Vector3 Point(Vector3Int g)
        {
            if (cache.TryGetValue(g, out Vector3 p)) return p;
            Vector3 u = new Vector3(g.x, g.y, g.z) / n - Vector3.one * 0.5f;   // -0.5 .. 0.5
            int onEdge = (Mathf.Abs(u.x) > 0.49f ? 1 : 0) + (Mathf.Abs(u.y) > 0.49f ? 1 : 0) + (Mathf.Abs(u.z) > 0.49f ? 1 : 0);
            float push = onEdge >= 3 ? 0.16f : onEdge == 2 ? 0.12f : 0.035f;     // corners > edges > face middles
            Vector3 jitter = new Vector3(Rand(rng, -1f, 1f), Rand(rng, -1f, 1f), Rand(rng, -1f, 1f)) * push;
            Vector3 outward = u.normalized * Rand(rng, 0f, push);
            p = centre + rot * ((u + jitter * 0.6f + outward) * s);
            cache[g] = p;
            return p;
        }

        // Six faces, each as a grid in integer cube coords (0..n).
        for (int face = 0; face < 6; face++)
        {
            int axis = face / 2; bool positive = face % 2 == 1;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    Vector3Int G(int a, int b)
                    {
                        int fixedV = positive ? n : 0;
                        return axis == 0 ? new Vector3Int(fixedV, a, b) : axis == 1 ? new Vector3Int(b, fixedV, a) : new Vector3Int(a, b, fixedV);
                    }
                    Vector3 p00 = Point(G(i, j)), p10 = Point(G(i + 1, j)), p11 = Point(G(i + 1, j + 1)), p01 = Point(G(i, j + 1));
                    mb.FlatTri(sub, p00, p10, p11, centre); mb.FlatTri(sub, p00, p11, p01, centre);
                }
        }
    }

    static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    // ------------------------------------------------------------------
    // Particles: gold flakes drifting down out of the crown
    // ------------------------------------------------------------------

    static void AddLeafFlakes(Transform parent, Material material)
    {
        var go = new GameObject("LeafFlakes");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.8f, 3.6f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.075f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(Gold, Color.Lerp(Gold, Color.white, 0.25f));
        main.gravityModifier = 0.025f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;   // pooled trees move between corners
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;        // sized with the tree
        main.maxParticles = 10;
        main.playOnAwake = true;

        var emission = ps.emission;
        emission.rateOverTime = 1.6f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.9f, 0.9f, 0.6f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.12f, -0.06f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    // ------------------------------------------------------------------
    // Materials and assets
    // ------------------------------------------------------------------

    static Material LitMaterial(string path, Color color, float smoothness, Color emission)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); }
        else m.shader = lit;
        m.SetColor("_BaseColor", color);
        m.SetColor("_Color", color);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Smoothness", smoothness);
        if (emission.maxColorComponent > 0.001f)
        {
            m.SetColor("_EmissionColor", emission);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else m.DisableKeyword("_EMISSION");
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material ParticleMaterial(string path, Color color)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        else m.shader = sh;
        m.SetColor("_BaseColor", color);
        CornerPropFader.MakeTransparent(m);   // flakes fade in and out by vertex alpha, and with the legs
        EditorUtility.SetDirty(m);
        return m;
    }

    static Mesh SaveAsset(Mesh mesh, string path)
    {
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (old == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        EditorUtility.CopySerialized(mesh, old);
        return old;
    }

    class MeshBuild
    {
        readonly List<Vector3> pos = new List<Vector3>();
        readonly List<Vector3> nrm = new List<Vector3>();
        readonly List<int>[] subs;

        public MeshBuild(int subCount)
        {
            subs = new List<int>[subCount];
            for (int i = 0; i < subCount; i++) subs[i] = new List<int>();
        }

        int V(Vector3 p, Vector3 n) { pos.Add(p); nrm.Add(n); return pos.Count - 1; }

        /// <summary>Quad a-b-c-d with per-corner normals (clockwise seen from the front, Unity's winding).</summary>
        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
        {
            // Make the winding agree with the normals, whatever order the caller walked the corners in.
            Vector3 faceN = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(faceN, na + nb + nc + nd) < 0f) { (b, d) = (d, b); (nb, nd) = (nd, nb); }
            int ia = V(a, na), ib = V(b, nb), ic = V(c, nc), id = V(d, nd);
            subs[sub].AddRange(new[] { ia, ib, ic, ia, ic, id });
        }

        /// <summary>Flat-shaded triangle, wound so it faces away from `inside`.</summary>
        public void FlatTri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(n, (a + b + c) / 3f - inside) < 0f) { (b, c) = (c, b); n = -n; }
            subs[sub].Add(V(a, n)); subs[sub].Add(V(b, n)); subs[sub].Add(V(c, n));
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.subMeshCount = subs.Length;
            for (int i = 0; i < subs.Length; i++) mesh.SetTriangles(subs[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
