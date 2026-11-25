using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Props standing in the corner gaps of the path - the pillars of MainScene prop camera 3.7.
///
/// Every leg is laid with a DIAGONAL RISER (see AppendSegment): the last cube of the leg arriving at a turn and the
/// first cube of the leg leaving it only touch along one edge, so of the 2 x 2 cells around the corner two are empty.
/// One of them (cornerPropCell) gets a flat tile facing up, level with the top of the track and drawn with the corner
/// cube's own material and property block - so it takes every look, fade and wipe the track takes - and a prop
/// standing on that tile.
///
/// SIDEWAYS TURNS ONLY, like the corner gems: nothing on a climb, a drop, or the leg that leaves one.
///
/// Which prop: each checkpoint has its own CornerPropSet (cornerPropSets[checkpoint - 1], the last entry carries on
/// for the checkpoints after it, a null entry = no props there). A set's props are used in order, legsPerProp legs
/// each, counting the legs from the start of the checkpoint.
///
/// Lifetime is the gems' (DynamicPathGeneratorGems.cs): data until the corner cube is drawn (PathDepthShading), then
/// a pooled instance that grows up off the tile with the cube's ShownAmount and goes back to its pool when the corner
/// is hidden or the leg is culled - so a prop is never seen popping in or out. No colliders: the runner's forward
/// probe raycasts the Default layer and would take a prop or a tile for a path cube.
/// </summary>
public partial class DynamicPathGenerator
{
    public enum CornerPropCell { Outside, Inside }

    [Header("Corner Props")]
    [Tooltip("Stand a prop on a tile in the empty corner cell of every sideways turn. Off everywhere but MainScene prop camera 3.7.")]
    public bool spawnCornerProps = false;
    [Tooltip("Also fill the empty corner cell of every climb / drop (and the leg leaving one) with a plain cube, no prop. Off = 3.7-3.9; scene 3.10 uses it.")]
    public bool cornerFillOtherTurns = false;
    [Tooltip("One set per checkpoint: element 0 = checkpoint 1, element 1 = checkpoint 2, ... The last set carries on for the checkpoints after it. A null element = no props in that checkpoint.")]
    public CornerPropSet[] cornerPropSets;
    [Tooltip("Which empty cell of the corner holds the prop. Outside = the cell straight on from the arriving leg (the tip of the V); Inside = the cell between the two legs. Only new corners take a change.")]
    public CornerPropCell cornerPropCell = CornerPropCell.Outside;
    // Size is measured in path cubes (the leg is 1 cube wide and 1 tall), so a prop keeps the same proportion to the
    // legs at any cubeSize or camera zoom. Every model is fitted the same way, whatever units it was made in:
    // scaled to cornerPropHeight, then narrowed to fit the tile, widened if it would be a stick, and capped in height.
    // Never stretched - one uniform scale.
    [Tooltip("Height every prop is aimed at, in cubes (a leg is 1 cube tall). The camera shows ~60 cubes of track, so props need to be several cubes tall to read. Squat models end up shorter because of the width cap.")]
    [Range(0.2f, 12f)] public float cornerPropHeight = 7f;
    [Tooltip("Never taller than this, in cubes.")]
    [Range(0.2f, 14f)] public float cornerPropMaxHeight = 8f;
    [Tooltip("Widest a prop may be, in cubes. Past 1 cube the tile grows with it, spreading AWAY from the track (outward from the corner) so it never covers the legs.")]
    [Range(0.1f, 6f)] public float cornerPropFootprint = 4f;
    [Tooltip("A slender prop is grown until it is at least this wide, in cubes, so it does not read as a stick next to the legs.")]
    [Range(0.05f, 3f)] public float cornerPropMinFootprint = 1f;
    [Tooltip("Widest the prop's foot (bottom 12 %) may be, in cubes. The tile is one cube, so this keeps the prop on it.")]
    [Range(0.2f, 1f)] public float cornerPropFootCubes = 0.95f;
    [Tooltip("Put the flat tile under the prop. Off = the prop stands in the air at track-top height.")]
    public bool cornerPropTile = true;
    [Tooltip("Smallest width of the tile, in cubes. It is always at least as wide as the prop standing on it (+ a small margin).")]
    [Range(0.2f, 4f)] public float cornerPropTileSize = 1f;
    [Tooltip("Turns each prop by this many degrees about its own up axis (on top of facing out of the corner).")]
    [Range(0f, 360f)] public float cornerPropYaw = 0f;
    [Tooltip("Let the props cast shadows. Off for mobile.")]
    public bool cornerPropShadows = false;

    [Header("Corner Prop Size / Glow (scene 3.8)")]
    [Tooltip("Stretches the PROP upward only (1 = as authored); the tile stays exactly one cube and the foot stays inside it.")]
    [Range(0.5f, 5f)] public float cornerPropSizeMul = 1f;
    [Tooltip("Multiplies the props' own emission (their base map x glow), lighting only the prop - unlike the point light, it cannot reach the legs. 1 = as authored.")]
    [Range(0.5f, 6f)] public float cornerPropGlow = 1f;

    [Header("Corner Prop Light")]
    [Tooltip("A small warm point light in front of every prop on screen (camera side, half way up), so the props glow out of the dark. Pooled; only props being drawn hold one.")]
    public bool cornerPropLight = true;
    [ColorUsage(false, false)] public Color cornerPropLightColor = new Color(1f, 0.82f, 0.55f, 1f);
    [Range(0f, 20f)] public float cornerPropLightIntensity = 6f;
    [Tooltip("Range as a share of the prop's height (+ 2 cubes).")]
    [Range(0.2f, 3f)] public float cornerPropLightRange = 1.1f;
    [Tooltip("How far in front of the prop (toward the camera) the light floats, in cubes.")]
    [Range(0f, 6f)] public float cornerPropLightForward = 1.8f;

    class CornerProp
    {
        public Segment segment;
        public GameObject cube;          // the corner cube: the prop is drawn exactly as much as this is
        public GameObject prefab;
        public Vector3 position;         // middle of the tile, on top of the track
        public Quaternion rotation;
        public Quaternion tileRotation;  // square to the track, not to the diagonal
        public Renderer host;            // the corner cube's renderer, whose material the tile wears
        public float scale;              // full-grown uniform scale of the prefab
        public float tileSize;           // tile width in cubes (grows with a wide prop)
        public GameObject instance;
        public CornerPropFader fader;
        public Light light;
        public float height;             // full-grown height in world units
        public MeshRenderer tile;
    }

    const int CornerPropPoolWarm = 2;
    const int CornerTilePoolWarm = 6;

    private readonly List<CornerProp> cornerProps = new List<CornerProp>();
    private readonly Stack<CornerProp> spareCornerProps = new Stack<CornerProp>();
    private readonly Dictionary<GameObject, Stack<GameObject>> cornerPropPools = new Dictionary<GameObject, Stack<GameObject>>();
    private readonly Dictionary<GameObject, Bounds> cornerPropBounds = new Dictionary<GameObject, Bounds>();
    private readonly Stack<MeshRenderer> cornerTilePool = new Stack<MeshRenderer>();
    private readonly Stack<Light> cornerPropLightPool = new Stack<Light>();
    private MaterialPropertyBlock cornerTileBlock;
    private Transform cornerPropRoot;
    private Mesh cornerTileMesh;
    private Mesh cornerFillMesh;         // a unit cube, for the climb / drop filler
    private int cornerPropLeg;           // legs laid since the checkpoint began
    private int cornerPropZone = -1;     // the checkpoint they are counted in
    private int cornerSpecialGap = -1;   // sideways corners left until the next special prop (-1 = not drawn yet)
    private int cornerSpecialCount;      // specials used in this checkpoint, for their order

    // ------------------------------------------------------------------
    // Placement
    // ------------------------------------------------------------------

    /// <summary>
    /// Called for EVERY leg as it is finalized (incoming = the leg that ends at the corner, nextDir = the heading of
    /// the leg after it), so the leg count runs on through climbs and drops; only sideways corners get a prop.
    /// </summary>
    void PlaceCornerProp(Segment incoming, Vector3 nextDir)
    {
        if ((!spawnCornerProps && !cornerFillOtherTurns) || incoming == null || incoming.cubes.Count == 0) return;

        int zone = GateZoneIndex;
        if (zone != cornerPropZone) { cornerPropZone = zone; cornerPropLeg = 0; cornerSpecialGap = -1; cornerSpecialCount = 0; }
        int leg = cornerPropLeg++;

        Vector3 from = incoming.direction;
        bool vertical = Mathf.Abs(from.y) > 0.5f || Mathf.Abs(nextDir.y) > 0.5f;
        if (Vector3.Dot(from, nextDir) > 0.9f) return;                          // straight on: no corner
        if (vertical)
        {
            // Climbs / drops: no prop, just a cube in the empty cell so the corner is not a hole.
            if (!cornerFillOtherTurns) return;
            GameObject fillCube = incoming.cubes[incoming.cubes.Count - 1];
            if (fillCube == null) return;
            CornerPropSystemReady();
            CornerProp f = spareCornerProps.Count > 0 ? spareCornerProps.Pop() : new CornerProp();
            f.segment = incoming; f.cube = fillCube; f.prefab = null;
            f.position = incoming.endPosition + (cornerPropCell == CornerPropCell.Outside ? from : nextDir) * cubeSize;
            f.tileRotation = Quaternion.LookRotation(Mathf.Abs(from.y) > 0.5f ? nextDir : from, Vector3.up);
            f.rotation = f.tileRotation;
            f.host = fillCube.GetComponentInChildren<Renderer>();
            f.scale = 1f; f.tileSize = 1f; f.height = 0f; f.instance = null; f.tile = null; f.fader = null; f.light = null;
            cornerProps.Add(f);
            return;
        }
        if (!spawnCornerProps) return;

        CornerPropSet set = CornerPropSetFor(zone);
        if (set == null) return;
        GameObject cornerCube = incoming.cubes[incoming.cubes.Count - 1];
        if (cornerCube == null) return;

        // Every specialEveryMin..Max sideways corners, the set's special prop (the tree) stands here instead.
        bool special = false;
        GameObject prefab;
        if (set.HasSpecials)
        {
            if (cornerSpecialGap < 0) cornerSpecialGap = set.NextSpecialGap();
            if (--cornerSpecialGap <= 0)
            {
                special = true;
                cornerSpecialGap = set.NextSpecialGap();
            }
        }
        prefab = special ? set.SpecialAt(cornerSpecialCount++) : set.PropForLeg(leg);
        if (prefab == null) return;
        CornerPropSystemReady();

        Vector3 cell = cornerPropCell == CornerPropCell.Outside ? from : nextDir;

        Bounds b = CornerPropBoundsOf(prefab);
        float h = Mathf.Max(0.0001f, b.size.y);
        float w = Mathf.Max(0.0001f, Mathf.Max(b.size.x, b.size.z));

        CornerProp p = spareCornerProps.Count > 0 ? spareCornerProps.Pop() : new CornerProp();
        p.segment = incoming;
        p.cube = cornerCube;
        p.prefab = prefab;
        p.position = incoming.endPosition + cell * cubeSize;   // middle of the empty cell; the top is found per frame
        float tileMul = 1f;   // the tile is always exactly one cube; cornerPropSizeMul only stretches the prop upward
        // Square to the track like the tile, so a square base sits inside the tile instead of poking its corners out.
        p.tileRotation = Quaternion.LookRotation(from, Vector3.up);
        p.rotation = p.tileRotation * Quaternion.Euler(0f, cornerPropYaw, 0f);
        p.host = cornerCube.GetComponentInChildren<Renderer>();
        // Special props are a bit bigger than the regular ones (the user: "slightly bigger"), still in cube terms.
        p.scale = special ? Mathf.Min(set.specialHeight / h, set.specialMaxWidth / w) * cubeSize : CornerPropFit(h, w);
        p.height = h * p.scale * cornerPropSizeMul;

        // The tile is as wide as what stands ON it (the base, not a tree's crown) plus a margin. A tile wider than one
        // cube grows away from the track: its inner corner stays at the corner cell's inner corner and it spreads
        // along the two free directions, so it never lies over the legs.
        // The tile is exactly one path cube, level with the track top, so it reads as another cube lying flat in the
        // corner - never stretched, never over the legs. The prop's FOOT must fit on it, whatever its crown does.
        p.tileSize = tileMul;
        float baseWidth = Mathf.Max(0.0001f, CornerPropBaseWidth(prefab));
        p.scale = Mathf.Min(p.scale, cornerPropFootCubes * cubeSize / baseWidth);
        p.height = h * p.scale * cornerPropSizeMul;
        p.instance = null;
        p.tile = null;
        cornerProps.Add(p);
    }

    /// <summary>
    /// One uniform scale for a model h tall and w wide (its own units), in path-cube terms:
    /// aim at cornerPropHeight -> no wider than the tile -> no thinner than cornerPropMinFootprint -> no taller than
    /// cornerPropMaxHeight (the last word, so nothing towers over the legs).
    /// For the 7 pillars this gives 0.9 - 1.8 cubes tall and 0.6 - 1.0 cubes wide.
    /// </summary>
    float CornerPropFit(float h, float w)
    {
        float s = cornerPropHeight / h;   // the foot cap in PlaceCornerProp has the last word on width
        s = Mathf.Min(s, cornerPropFootprint / w);
        s = Mathf.Min(s, cornerPropMaxHeight / h);
        return s * cubeSize;
    }

    private readonly Dictionary<GameObject, float> cornerPropBaseWidths = new Dictionary<GameObject, float>();

    /// <summary>Width of the bottom 12 % of the prop (its foot), in the prefab's own units - what the tile must hold.</summary>
    float CornerPropBaseWidth(GameObject prefab)
    {
        if (cornerPropBaseWidths.TryGetValue(prefab, out float bw)) return bw;
        Bounds all = CornerPropBoundsOf(prefab);
        float cut = all.min.y + all.size.y * 0.12f;
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
        foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh m = mf.sharedMesh;
            if (m == null || !m.isReadable) continue;
            Matrix4x4 mx = toRoot * mf.transform.localToWorldMatrix;
            foreach (Vector3 v in m.vertices)
            {
                Vector3 q = mx.MultiplyPoint3x4(v);
                if (q.y > cut) continue;
                minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x); minZ = Mathf.Min(minZ, q.z); maxZ = Mathf.Max(maxZ, q.z);
            }
        }
        bw = minX <= maxX ? Mathf.Max(maxX - minX, maxZ - minZ) : Mathf.Max(all.size.x, all.size.z);
        cornerPropBaseWidths[prefab] = bw;
        return bw;
    }

    CornerPropSet CornerPropSetFor(int zone)
    {
        if (cornerPropSets == null || cornerPropSets.Length == 0) return null;
        return cornerPropSets[Mathf.Clamp(zone - 1, 0, cornerPropSets.Length - 1)];
    }

    Bounds CornerPropBoundsOf(GameObject prefab)
    {
        if (cornerPropBounds.TryGetValue(prefab, out Bounds b)) return b;

        // In the prefab's own space: every mesh under it, so a prop made of several parts measures as a whole.
        bool any = false;
        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
        foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 pt = m.MultiplyPoint3x4(c);
                if (!any) { b = new Bounds(pt, Vector3.zero); any = true; }
                else b.Encapsulate(pt);
            }
        }
        if (!any) b = new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
        cornerPropBounds[prefab] = b;
        return b;
    }

    // ------------------------------------------------------------------
    // Lifetime
    // ------------------------------------------------------------------

    void ResetCornerProps()
    {
        for (int i = cornerProps.Count - 1; i >= 0; i--) RemoveCornerPropAt(i);
        cornerPropLeg = 0;
        cornerPropZone = -1;
        cornerSpecialGap = -1;
        cornerSpecialCount = 0;
    }

    void ReleaseCornerPropsOn(Segment seg)
    {
        for (int i = cornerProps.Count - 1; i >= 0; i--)
            if (cornerProps[i].segment == seg) RemoveCornerPropAt(i);
    }

    void RemoveCornerPropAt(int i)
    {
        CornerProp p = cornerProps[i];
        HideCornerProp(p);
        p.segment = null;
        p.cube = null;
        p.prefab = null;
        p.host = null;
        spareCornerProps.Push(p);

        int last = cornerProps.Count - 1;
        cornerProps[i] = cornerProps[last];
        cornerProps.RemoveAt(last);
    }

    void HideCornerProp(CornerProp p)
    {
        if (p.instance != null)
        {
            p.instance.SetActive(false);
            if (p.prefab != null && cornerPropPools.TryGetValue(p.prefab, out var pool)) pool.Push(p.instance);
            else Destroy(p.instance);
            p.instance = null;
            p.fader = null;
        }
        if (p.light != null) { p.light.enabled = false; cornerPropLightPool.Push(p.light); p.light = null; }
        if (p.tile != null) { p.tile.enabled = false; cornerTilePool.Push(p.tile); p.tile = null; }
    }

    void DestroyCornerPropRuntime()
    {
        if (cornerTileMesh != null) Destroy(cornerTileMesh);
        if (cornerFillMesh != null) Destroy(cornerFillMesh);
        cornerTileMesh = null;
        cornerFillMesh = null;
    }

    /// <summary>Builds the tile mesh, the root and the pools on first use, and warms a couple of every prop in the sets.</summary>
    void CornerPropSystemReady()
    {
        if (cornerTileMesh != null) return;

        cornerTileMesh = BuildCornerTile();
        cornerFillMesh = BuildFillCube();
        cornerTileBlock = new MaterialPropertyBlock();
        cornerPropRoot = new GameObject("CornerProps").transform;
        cornerPropRoot.SetParent(transform, false);

        for (int i = 0; i < CornerTilePoolWarm; i++) cornerTilePool.Push(NewCornerTile());

        if (cornerPropSets == null) return;
        foreach (CornerPropSet set in cornerPropSets)
        {
            if (set == null) continue;
            var all = new List<GameObject>();
            if (set.props != null) all.AddRange(set.props);
            if (set.specialProps != null) all.AddRange(set.specialProps);
            foreach (GameObject prefab in all)
            {
                if (prefab == null || cornerPropPools.ContainsKey(prefab)) continue;
                var pool = new Stack<GameObject>();
                cornerPropPools[prefab] = pool;
                for (int i = 0; i < CornerPropPoolWarm; i++) pool.Push(NewCornerPropInstance(prefab));
            }
        }
    }

    GameObject NewCornerPropInstance(GameObject prefab)
    {
        GameObject go = Instantiate(prefab, cornerPropRoot);
        go.name = prefab.name;
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = cornerPropShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        // Every prop fades with the legs; a prefab made by hand without a fader gets one (runtime transparent twins).
        if (go.GetComponent<CornerPropFader>() == null) go.AddComponent<CornerPropFader>();
        go.SetActive(false);
        return go;
    }


    Light NewCornerPropLight()
    {
        var go = new GameObject("CornerPropLight");
        go.transform.SetParent(cornerPropRoot, false);
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.ForcePixel;
        l.enabled = false;
        return l;
    }
    MeshRenderer NewCornerTile()
    {
        var go = new GameObject("CornerTile");
        go.transform.SetParent(cornerPropRoot, false);
        go.AddComponent<MeshFilter>().sharedMesh = cornerTileMesh;
        var r = go.AddComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.enabled = false;
        return r;
    }

    /// <summary>A unit cube centred on its pivot, flat-shaded.</summary>
    static Mesh BuildFillCube()
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
        for (int a = 0; a < 3; a++)
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Vector3 nn = axes[a] * sgn, u = axes[(a + 1) % 3], w = axes[(a + 2) % 3];
                int b = v.Count;
                v.Add((nn - u - w) * 0.5f); v.Add((nn + u - w) * 0.5f); v.Add((nn + u + w) * 0.5f); v.Add((nn - u + w) * 0.5f);
                for (int i = 0; i < 4; i++) n.Add(nn);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                if (Vector3.Dot(Vector3.Cross(u, w), nn) < 0f) t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                else t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
        var m = new Mesh { name = "CornerFillCube" };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateBounds(); m.RecalculateTangents();
        return m;
    }

    /// <summary>A 1 x 1 quad lying flat, facing up, centred on its pivot.</summary>
    static Mesh BuildCornerTile()
    {
        var mesh = new Mesh { name = "CornerPropTile" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
            new Vector3( 0.5f, 0f,  0.5f), new Vector3( 0.5f, 0f, -0.5f),
        };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    // ------------------------------------------------------------------
    // Per frame
    // ------------------------------------------------------------------


    static readonly int CornerBaseColorId = Shader.PropertyToID("_BaseColor");

    /// <summary>
    /// The renderer PathDepthShading actually draws the corner cube with: it hands every path cube's renderer to a
    /// visual-only child (and disables the cube's own), so the child wins once it exists.
    /// </summary>
    static Renderer CornerHostOf(GameObject cube)
    {
        Transform t = cube.transform;
        for (int i = 0; i < t.childCount; i++)
        {
            Renderer r = t.GetChild(i).GetComponent<Renderer>();
            if (r != null) return r;
        }
        return cube.GetComponent<Renderer>();
    }

    void UpdateCornerProps()
    {
        if (cornerProps.Count == 0) return;

        if (shadingRef == null) shadingRef = GetComponent<PathDepthShading>();
        bool shaded = shadingRef != null && shadingRef.isActiveAndEnabled;
        float parentScale = cornerPropRoot != null ? Mathf.Max(0.0001f, cornerPropRoot.lossyScale.x) : 1f;

        for (int i = cornerProps.Count - 1; i >= 0; i--)
        {
            CornerProp p = cornerProps[i];
            if (p.cube == null) { RemoveCornerPropAt(i); continue; }

            // Drawn exactly as much as its corner cube: shown with it, hidden with it.
            float shown = shaded ? shadingRef.ShownAmount(p.cube) : (p.cube.activeInHierarchy ? 1f : 0f);
            if (shown <= 0.01f) { HideCornerProp(p); continue; }

            if (p.host == null || (p.host.transform == p.cube.transform && p.cube.transform.childCount > 0))
                p.host = CornerHostOf(p.cube);
            Renderer host = p.host;
            if (host == null) continue;

            // The leg's own fade, read off the corner cube: PathDepthShading puts the cube's alpha (faint far ahead,
            // fading at the edges of the drawn window, see-through once the runner has left the leg) in _BaseColor
            // and shrinks its cross-section (the visual child's x scale). The prop and its tile take both, so they
            // appear, thin out, go see-through and disappear exactly with the legs.
            host.GetPropertyBlock(cornerTileBlock);
            float alpha = shaded && !cornerTileBlock.isEmpty ? cornerTileBlock.GetColor(CornerBaseColorId).a : shown;
            float thick = host.transform != p.cube.transform ? Mathf.Max(0.0001f, host.transform.localScale.x) : 1f;
            if (p.prefab == null)
            {
                // Plain filler cube in the empty cell: the corner cube's material / block, so it fades and looks like it.
                if (p.tile == null) { p.tile = cornerTilePool.Count > 0 ? cornerTilePool.Pop() : NewCornerTile(); p.tile.enabled = true; }
                MeshFilter fmf = p.tile.GetComponent<MeshFilter>();
                if (fmf.sharedMesh != cornerFillMesh) fmf.sharedMesh = cornerFillMesh;
                if (p.tile.sharedMaterial != host.sharedMaterial) p.tile.sharedMaterial = host.sharedMaterial;
                p.tile.SetPropertyBlock(cornerTileBlock);
                float fs = cubeSize * thick / parentScale;
                p.tile.transform.SetPositionAndRotation(p.position, p.tileRotation);
                p.tile.transform.localScale = new Vector3(fs, fs, fs);
                continue;
            }
            Vector3 top = p.position + Vector3.up * (cubeSize * 0.5f * thick);

            if (p.instance == null)
            {
                Stack<GameObject> pool;
                if (!cornerPropPools.TryGetValue(p.prefab, out pool)) { pool = new Stack<GameObject>(); cornerPropPools[p.prefab] = pool; }
                p.instance = pool.Count > 0 ? pool.Pop() : NewCornerPropInstance(p.prefab);
                p.instance.SetActive(true);
                p.fader = p.instance.GetComponent<CornerPropFader>();
            }
            Transform t = p.instance.transform;
            t.SetPositionAndRotation(top, p.rotation);
            { float us = p.scale * thick / parentScale; t.localScale = new Vector3(us, us * cornerPropSizeMul, us); }
            if (p.fader != null) { p.fader.glow = cornerPropGlow; p.fader.SetAlpha(alpha); }

            // The glow: a warm point light floating in front of the prop on the camera's side, half way up it, so
            // the face the player sees is lit. Its strength follows the leg's alpha, so it fades with the prop.
            if (cornerPropLight && cornerPropLightIntensity > 0f)
            {
                if (p.light == null) { p.light = cornerPropLightPool.Count > 0 ? cornerPropLightPool.Pop() : NewCornerPropLight(); p.light.enabled = true; }
                Camera cam = Camera.main;
                Vector3 toCam = cam != null ? -cam.transform.forward : Vector3.back;
                toCam.y = 0f;
                toCam = toCam.sqrMagnitude > 0.0001f ? toCam.normalized : Vector3.back;
                float hNow = p.height * thick;
                p.light.transform.position = top + Vector3.up * (hNow * 0.5f) + toCam * (cornerPropLightForward * cubeSize);
                p.light.color = cornerPropLightColor;
                p.light.intensity = cornerPropLightIntensity * alpha;
                p.light.range = hNow * cornerPropLightRange + 2f * cubeSize;
            }
            else if (p.light != null) { p.light.enabled = false; cornerPropLightPool.Push(p.light); p.light = null; }

            if (!cornerPropTile)
            {
                if (p.tile != null) { p.tile.enabled = false; cornerTilePool.Push(p.tile); p.tile = null; }
                continue;
            }

            // The tile wears the corner cube's own material and property block, so its colour, fade and any look
            // wipe are the track's. Lifted a hair over the track top so it never fights the cubes beside it.
            if (p.tile == null) { p.tile = cornerTilePool.Count > 0 ? cornerTilePool.Pop() : NewCornerTile(); p.tile.enabled = true; }
            { MeshFilter tmf = p.tile.GetComponent<MeshFilter>(); if (tmf.sharedMesh != cornerTileMesh) tmf.sharedMesh = cornerTileMesh; }
            if (p.tile.sharedMaterial != host.sharedMaterial) p.tile.sharedMaterial = host.sharedMaterial;
            p.tile.SetPropertyBlock(cornerTileBlock);
            float size = cubeSize * p.tileSize * thick / parentScale;
            Transform q = p.tile.transform;
            q.SetPositionAndRotation(top + Vector3.up * (cubeSize * 0.002f), p.tileRotation);
            q.localScale = new Vector3(size, 1f, size);
        }
    }
}
