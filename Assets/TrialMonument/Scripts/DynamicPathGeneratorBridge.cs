using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BRIDGE LEGS (the "prop" scene's level-2 hazard). Past bridgeAfterLeg, an ordinary leg has a
/// bridgeChance of becoming a bridge: it is laid bridgeLengthMultiplier times as long as usual, and
/// the middle bridgeNarrowFraction of it is not the ordinary four-sided beam but a plank attached to
/// only ONE of its four sides (top/right/bottom/left, chosen once per plank). The other three sides
/// are open air over that stretch.
///
/// bridgeMultiChance of eligible bridge legs instead get bridgeMultiPlankRange (1-3) separate planks
/// strung along one extra-long leg, each independently sided, with at least bridgeGapCubes ordinary
/// full cubes between consecutive planks so the runner has room to cross back over before the next one.
///
/// The lead-in and lead-out cubes around the planks are ordinary, full beam - the runner can see each
/// plank's side coming and has a stretch afterwards to get back to whichever side the path needs next.
///
/// Collision stays simple: every cube, narrow or not, keeps its ordinary box collider, so landing and
/// forward-probing behave exactly as on any other leg. Only the VISIBLE mesh of a narrow cube is
/// swapped for a thin one-sided slab (so PathDepthShading, which just proxies whatever mesh sits on
/// the cube, fades/shrinks it exactly like the rest of the track). Being caught on the wrong face is
/// enforced separately, by BridgePlankGuard, which is what actually ends the run - the plank's
/// "missing" sides are a look, not a hitbox.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Bridge Legs (one-sided planks)")]
    [Tooltip("Only the prop scene wants these. Off everywhere else by default.")]
    public bool bridgeSections = false;
    [Tooltip("Bridges only start appearing after this many points of score.")]
    [Range(0, 200)] public int bridgeAfterLeg = 25;
    [Tooltip("Chance (0-1) an eligible leg becomes a bridge. 0.3 = 30%.")]
    [Range(0f, 1f)] public float bridgeChance = 0.3f;
    [Tooltip("A bridge leg is this many times as long as an ordinary leg would be here.")]
    [Range(1f, 4f)] public float bridgeLengthMultiplier = 2f;
    [Tooltip("Share of the bridge's total length that narrows to a one-sided plank (each plank on a multi-plank leg gets this same share).")]
    [Range(0.05f, 0.6f)] public float bridgeNarrowFraction = 0.2f;
    [Header("Multi-Plank Bridge Legs")]
    [Tooltip("Of eligible bridge legs, the chance the leg gets multiple planks instead of just one.")]
    [Range(0f, 1f)] public float bridgeMultiChance = 0.3f;
    [Tooltip("How many separate planks a multi-plank leg gets, picked at random in this range (inclusive).")]
    public Vector2Int bridgeMultiPlankRange = new Vector2Int(1, 3);
    [Tooltip("A multi-plank leg is stretched this much further past the ordinary bridgeLengthMultiplier - it needs the room for extra planks and gaps.")]
    [Range(1f, 2f)] public float bridgeMultiExtraLengthMultiplier = 1.4f;
    [Tooltip("Minimum ordinary full cubes between one plank and the next on a multi-plank leg.")]
    [Range(1, 10)] public int bridgeGapCubes = 4;
    [Tooltip("How thick the plank is, as a fraction of the full beam's depth. Kept fairly chunky so which side it is on reads at a glance.")]
    [Range(0.15f, 0.45f)] public float bridgePlankThickness = 0.3f;
    [Tooltip("Cross-slats cut into the plank's outer face (the reference bridge's boards), each a real groove so lighting shadows every seam. Same material and colour as the rest of the path - only the shape marks it as a bridge.")]
    [Range(1, 6)] public int bridgePlankSlats = 3;
    [Tooltip("Share of each slat's length left as an open groove between it and the next.")]
    [Range(0.05f, 0.4f)] public float bridgePlankGrooveShare = 0.16f;
    [Tooltip("How deep a groove cuts into the plank, as a fraction of its thickness.")]
    [Range(0.1f, 0.8f)] public float bridgePlankGrooveDepth = 0.4f;
    [Tooltip("Colour the plank is drawn in (PathDepthShading blends its route colour toward it), so the bridge side stands out.")]
    public Color bridgePlankColor = new Color(0.9f, 0.12f, 0.1f, 1f);
    [Tooltip("How much of bridgePlankColor replaces the route colour. 0 = same colour as the rest of the path.")]
    [Range(0f, 1f)] public float bridgePlankColorStrength = 0.9f;
    [Tooltip("Draw a dark outline along the plank's edges and across every slat seam.")]
    public bool bridgePlankBorders = true;
    [Tooltip("Colour of the outline.")]
    public Color bridgePlankBorderColor = Color.black;
    [Tooltip("Width of the outline along the plank's edges, in cubes. The slat seams are drawn a little thinner.")]
    [Range(0.01f, 0.2f)] public float bridgePlankBorderWidth = 0.06f;

    private System.Random bridgeRng;
    private readonly Mesh[] plankMeshes = new Mesh[4];
    private readonly Mesh[] plankBorderMeshes = new Mesh[4];
    private Material plankBorderMaterial;
    private readonly List<(int first, int last, int side)> bridgePlanks = new List<(int, int, int)>();

    private bool bridgeTeaching;
    private int plankSerial;

    bool TryPlanBridge(float arrivalSpeed, ref int segLen, Vector3 prevDir, Vector3 dir)
    {
        bridgePlanks.Clear();
        bridgeTeaching = false;

        // DifficultyProfile: planks spaced in TIME, scheduled by the waves (DynamicPathGenerator.Waves.cs).
        if (difficultyProfile != null && (TB || Waves)) return TryPlanBridgeTB(arrivalSpeed, ref segLen, prevDir, dir);

        if (!bridgeSections || segmentSerial <= bridgeAfterLeg) return false;

        bridgeRng ??= new System.Random();
        if (bridgeRng.NextDouble() >= bridgeChance) return false;

        bool multi = bridgeRng.NextDouble() < bridgeMultiChance;
        int planks = multi
            ? bridgeRng.Next(Mathf.Min(bridgeMultiPlankRange.x, bridgeMultiPlankRange.y), Mathf.Max(bridgeMultiPlankRange.x, bridgeMultiPlankRange.y) + 1)
            : 1;

        float lengthMul = Mathf.Max(1f, bridgeLengthMultiplier) * (multi ? Mathf.Max(1f, bridgeMultiExtraLengthMultiplier) : 1f);
        int baseTotal = Mathf.Max(4, Mathf.RoundToInt(segLen * lengthMul));
        int narrow = Mathf.Clamp(Mathf.RoundToInt(baseTotal * bridgeNarrowFraction), 1, baseTotal - 2);
        int lead = Mathf.Max(1, (baseTotal - narrow) / 2);
        int gap = Mathf.Max(1, bridgeGapCubes);

        int cursor = lead;
        for (int p = 0; p < planks; p++)
        {
            int first = cursor;
            int last = first + narrow - 1;
            bridgePlanks.Add((first, last, bridgeRng.Next(0, 4)));
            cursor = last + 1;
            if (p < planks - 1) cursor += gap;
        }

        segLen = cursor + lead;
        return true;
    }

    /// <summary>
    /// The profile's bridge: how many planks comes from the waves (or the old chance), then each plank is
    /// placed so every face change has the time it needs at this leg's speed:
    ///   landing face to the first plank ..... minLegTime(rolls)        (T_human from the leg start)
    ///   same face as the plank before ....... 2 cubes
    ///   adjacent face (1 roll) .............. overhead(1) - deadline + plankGapAdjacent
    ///   opposite face (2 rolls) ............. overhead(2) - deadline + plankGapOpposite
    ///   last plank to the corner ............ the opposite-face gap, so whatever the turn needs fits.
    /// The leg grows to fit; past bridgeMaxTierMultiple x the longest ordinary leg a plank is dropped.
    /// </summary>
    bool TryPlanBridgeTB(float v, ref int segLen, Vector3 prevDir, Vector3 dir)
    {
        if (!bridgeSections) return false;
        var P = difficultyProfile;
        bridgeRng ??= new System.Random();

        int planks;
        bool teaching = false;
        if (Waves) planks = WaveBridgePlanks(out teaching);
        else
        {
            if (segmentSerial <= bridgeAfterLeg || bridgeRng.NextDouble() >= bridgeChance) return false;
            planks = bridgeRng.NextDouble() < bridgeMultiChance
                ? bridgeRng.Next(Mathf.Min(bridgeMultiPlankRange.x, bridgeMultiPlankRange.y), Mathf.Max(bridgeMultiPlankRange.x, bridgeMultiPlankRange.y) + 1)
                : 1;
        }
        if (planks <= 0) return false;

        int land = SideOnLeg(dir, -prevDir);
        float cs = Mathf.Max(0.01f, cubeSize);
        v = Mathf.Max(0.01f, v);
        int tierMax = GetSegmentLengthRange(segmentSerial).y;
        Vector3 origin = lastPosition + prevDir * cubeSize;

        // Faces first, so the spacing can be worked out for them.
        var faces = new int[planks];
        int prevFace = land;
        for (int p = 0; p < planks; p++)
        {
            int f;
            if (teaching || PlankWeave) f = (prevFace + (bridgeRng.Next(0, 2) == 0 ? 1 : 3)) % 4;   // adjacent: one roll
            else f = bridgeRng.Next(0, 4);
            faces[p] = f;
            prevFace = f;
        }

        for (; planks > 0; planks--)
        {
            float lengthMul = Mathf.Max(1f, bridgeLengthMultiplier) * (planks > 1 ? Mathf.Max(1f, bridgeMultiExtraLengthMultiplier) : 1f);
            int baseTotal = Mathf.Max(4, Mathf.RoundToInt(segLen * lengthMul));
            int narrow = Mathf.Clamp(Mathf.RoundToInt(baseTotal * bridgeNarrowFraction), 1, Mathf.Max(1, baseTotal - 2));
            int lead = Mathf.Max(2, (baseTotal - narrow) / 2);

            bridgePlanks.Clear();
            int r0 = Rolls(land, faces[0]);
            float leadSeconds = r0 > 0 ? P.MinLegSeconds(r0, segmentSerial, false) * (teaching ? 1.5f : 1f) : 0f;
            int cursor = Mathf.Max(lead, Mathf.CeilToInt(leadSeconds * v / cs + 0.55f));
            for (int p = 0; p < planks; p++)
            {
                if (p > 0)
                {
                    int r = Rolls(faces[p - 1], faces[p]);
                    int gap = r == 0 ? 2 : Mathf.Max(2, Mathf.CeilToInt(P.PlankGapSeconds(r) * v / cs));
                    cursor += gap;
                }
                int first = cursor, last = first + narrow - 1;
                bridgePlanks.Add((first, last, faces[p]));
                cursor = last + 1;
            }
            int lastPlank = cursor - 1;
            int outCubes = Mathf.Max(lead, Mathf.CeilToInt(P.PlankGapSeconds(2) * v / cs + 1.55f));
            int total = lastPlank + outCubes;

            if (planks > 1 && total > P.bridgeMaxTierMultiple * tierMax) continue;   // too long: drop a plank
            if (!LegIsClear(origin, dir, total)) { bridgePlanks.Clear(); return false; }  // the longer leg must still keep clear
            segLen = total;
            bridgeTeaching = teaching;
            return true;
        }
        bridgePlanks.Clear();
        return false;
    }

    // Which plank (if any) covers cube index i along the leg, so the caller can swap that cube's mesh.
    bool BridgePlankSideAt(int i, out int side)
    {
        if (i == 0) plankSerial += 8;   // a new bridge leg: plank ids (one rescue per plank) start afresh
        for (int p = 0; p < bridgePlanks.Count; p++)
        {
            var plank = bridgePlanks[p];
            if (i >= plank.first && i <= plank.last) { side = plank.side; lastPlankAt = p; return true; }
        }
        side = -1;
        return false;
    }

    private int lastPlankAt;
    // Every cube of one plank shares an id, so the runner can be rescued once per plank, not once per cube.
    int PlankIdFor(GameObject cube) => plankSerial + lastPlankAt;

    // Swaps the cube's own mesh for a one-sided plank slab and adds the guard that ends the run if
    // the runner is not on that side while crossing it.
    void MakeBridgePlank(GameObject cube, int side)
    {
        MeshFilter mf = cube.GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = PlankMeshFor(side);

        var guard = cube.AddComponent<BridgePlankGuard>();
        guard.allowedSide = side;
        guard.plankId = PlankIdFor(cube);
        guard.player = player;
        guard.shading = GetComponent<PathDepthShading>();

        if (bridgePlankBorders)
        {
            Material m = PlankBorderMaterial(cube.GetComponent<Renderer>());
            if (m != null)
            {
                var b = new GameObject("BridgeBorder") { layer = cube.layer };
                b.transform.SetParent(cube.transform, false);
                b.AddComponent<MeshFilter>().sharedMesh = PlankBorderMeshFor(side);
                var r = b.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.enabled = false;   // the guard shows it once PathDepthShading draws the cube
                guard.border = r;
            }
        }
    }

    // A copy of the cube's own material, turned flat dark: builds keep it (no Shader.Find), and it
    // sorts and lights like the track it outlines.
    Material PlankBorderMaterial(Renderer source)
    {
        if (plankBorderMaterial != null) return plankBorderMaterial;
        if (source == null || source.sharedMaterial == null) return null;

        plankBorderMaterial = new Material(source.sharedMaterial) { name = "BridgePlankBorder (runtime)" };
        plankBorderMaterial.SetColor("_BaseColor", bridgePlankBorderColor);
        plankBorderMaterial.SetColor("_Color", bridgePlankBorderColor);
        plankBorderMaterial.SetColor("_EmissionColor", Color.black);
        plankBorderMaterial.DisableKeyword("_EMISSION");
        plankBorderMaterial.SetFloat("_Smoothness", 0f);
        return plankBorderMaterial;
    }

    // Thin bars on the 12 edges of the plank's slab, plus one across the outer face at each side of
    // every groove - the boards of the reference bridge, and a clear outline of which face it is on.
    Mesh PlankBorderMeshFor(int side)
    {
        side = ((side % 4) + 4) % 4;
        if (plankBorderMeshes[side] != null) return plankBorderMeshes[side];

        float t = Mathf.Clamp(bridgePlankThickness, 0.05f, 0.5f);
        bool thinIsY = side == 0 || side == 2;
        float sign = (side == 0 || side == 1) ? 1f : -1f;
        float outer = sign * 0.5f;
        float inner = outer - sign * t;
        float w = Mathf.Max(0.005f, bridgePlankBorderWidth);
        float seamW = w * 0.6f;

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uv = new List<Vector2>();
        var tris = new List<int>();

        // (cross, thin, z) -> cube-local position
        Vector3 P(float cross, float thin, float z) => thinIsY ? new Vector3(cross, thin, z) : new Vector3(thin, cross, z);

        void Bar(Vector3 a, Vector3 b, float width)
        {
            Vector3 h = Vector3.one * (width * 0.5f);
            AppendBoxFaces(verts, norms, uv, tris, Vector3.Min(a, b) - h, Vector3.Max(a, b) + h);
        }

        float[] crosses = { -0.5f, 0.5f };
        float[] thins = { inner, outer };
        float[] zs = { -0.5f, 0.5f };

        foreach (float c in crosses)
            foreach (float th in thins)
                Bar(P(c, th, -0.5f), P(c, th, 0.5f), w);          // along the leg
        foreach (float z in zs)
            foreach (float th in thins)
                Bar(P(-0.5f, th, z), P(0.5f, th, z), w);          // across the leg
        foreach (float c in crosses)
            foreach (float z in zs)
                Bar(P(c, inner, z), P(c, outer, z), w);           // through the thickness

        int n = Mathf.Max(1, bridgePlankSlats);
        float period = 1f / n;
        float grooveLen = period * Mathf.Clamp01(bridgePlankGrooveShare);
        for (int i = 0; i < n; i++)
        {
            float zMid = -0.5f + i * period + (period - grooveLen);
            float z1 = -0.5f + (i + 1) * period;
            if (grooveLen > 0.0001f && zMid > -0.5f && zMid < 0.5f) Bar(P(-0.5f, outer, zMid), P(0.5f, outer, zMid), seamW);
            if (z1 < 0.4999f) Bar(P(-0.5f, outer, z1), P(0.5f, outer, z1), seamW);
        }

        var mesh = new Mesh { name = $"BridgePlankBorder_{side}" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        plankBorderMeshes[side] = mesh;
        return mesh;
    }

    // Same material, same route colour as the rest of the path (PathDepthShading treats it like any
    // other cube) - only the SHAPE says "bridge": a run of raised slats along the direction of
    // travel, each separated by a real groove cut partway into the plank's thickness, after the
    // reference bridge's boards. The grooves are actual geometry, so ordinary scene lighting throws
    // a real shadow into every seam instead of a painted-on line.
    Mesh PlankMeshFor(int side)
    {
        side = ((side % 4) + 4) % 4;
        if (plankMeshes[side] != null) return plankMeshes[side];

        float t = Mathf.Clamp(bridgePlankThickness, 0.05f, 0.5f);
        bool thinIsY = side == 0 || side == 2;
        float sign = (side == 0 || side == 1) ? 1f : -1f;   // outer face at +bound (top/right) or -bound (bottom/left)
        float outerFull = sign * 0.5f;
        float inner = sign * 0.5f - sign * t;
        float outerGroove = outerFull - sign * t * Mathf.Clamp01(bridgePlankGrooveDepth);

        int n = Mathf.Max(1, bridgePlankSlats);
        float period = 1f / n;
        float grooveLen = period * Mathf.Clamp01(bridgePlankGrooveShare);
        float raisedLen = period - grooveLen;

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uv = new List<Vector2>();
        var tris = new List<int>();

        void AddBox(float thinA, float thinB, float z0, float z1)
        {
            float x0 = thinIsY ? -0.5f : Mathf.Min(thinA, thinB);
            float x1 = thinIsY ? 0.5f : Mathf.Max(thinA, thinB);
            float y0 = thinIsY ? Mathf.Min(thinA, thinB) : -0.5f;
            float y1 = thinIsY ? Mathf.Max(thinA, thinB) : 0.5f;
            AppendBoxFaces(verts, norms, uv, tris,
                new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));
        }

        for (int i = 0; i < n; i++)
        {
            float z0 = Mathf.Clamp(-0.5f + i * period, -0.5f, 0.5f);
            float zMid = Mathf.Clamp(z0 + raisedLen, -0.5f, 0.5f);
            float z1 = Mathf.Clamp(z0 + period, -0.5f, 0.5f);

            if (zMid > z0) AddBox(inner, outerFull, z0, zMid);
            if (grooveLen > 0.0001f && z1 > zMid) AddBox(inner, outerGroove, zMid, z1);
        }

        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh.name = $"BridgePlank_{side}";
        plankMeshes[side] = mesh;
        return mesh;
    }

    static void AppendBoxFaces(List<Vector3> verts, List<Vector3> normals, List<Vector2> uv, List<int> tris,
                               Vector3 min, Vector3 max)
    {
        void Face(Vector3 n, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int v = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
            tris.Add(v); tris.Add(v + 2); tris.Add(v + 1);
            tris.Add(v); tris.Add(v + 3); tris.Add(v + 2);
        }

        Vector3 p000 = new Vector3(min.x, min.y, min.z), p001 = new Vector3(min.x, min.y, max.z);
        Vector3 p010 = new Vector3(min.x, max.y, min.z), p011 = new Vector3(min.x, max.y, max.z);
        Vector3 p100 = new Vector3(max.x, min.y, min.z), p101 = new Vector3(max.x, min.y, max.z);
        Vector3 p110 = new Vector3(max.x, max.y, min.z), p111 = new Vector3(max.x, max.y, max.z);

        Face(Vector3.up, p010, p011, p111, p110);
        Face(Vector3.down, p000, p100, p101, p001);
        Face(Vector3.right, p100, p110, p111, p101);
        Face(Vector3.left, p000, p001, p011, p010);
        Face(Vector3.forward, p001, p101, p111, p011);
        Face(Vector3.back, p000, p010, p110, p100);
    }

    void DestroyBridgeRuntime()
    {
        for (int i = 0; i < plankMeshes.Length; i++)
        {
            if (plankMeshes[i] != null) Destroy(plankMeshes[i]);
            plankMeshes[i] = null;
            if (plankBorderMeshes[i] != null) Destroy(plankBorderMeshes[i]);
            plankBorderMeshes[i] = null;
        }
        if (plankBorderMaterial != null) Destroy(plankBorderMaterial);
        plankBorderMaterial = null;
    }
}
