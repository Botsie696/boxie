using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Surrounds the path with a backdrop of huge low-poly rock walls and floating rocks, so the runner reads as crossing a deep chasm.
/// Real geometry, so the orthographic camera's motion gives parallax. Everything is spawned on a world grid around the player and
/// recycled as the player moves.
///
/// The backdrop never interrupts the route: a piece is never placed where it would touch a path cube, and every few frames each piece
/// is tested from the camera's point of view - if it would draw over any cube of the path, it is shrunk away until it no longer does.
/// The foot of every wall fades into the camera's background colour (the pack's height fog), so the rocks never look planted on a floor.
/// Tick <see cref="enableBackground"/> off to remove the whole effect.
/// </summary>
[DisallowMultipleComponent]
public class PathBackgroundGenerator : MonoBehaviour
{
    [Header("Master switch")]
    [Tooltip("Turn the rock backdrop on or off.")]
    public bool enableBackground = true;

    [Header("References (found automatically when empty)")]
    public DynamicPathGenerator generator;
    public CubeRailRunner player;
    public Camera cam;
    [Tooltip("The houses on cliffs. Background rock keeps clear of them and never draws over them.")]
    public NeonWindowDecorator houses;

    [Header("Prefabs")]
    [Tooltip("Cliff pieces stacked into the huge walls.")]
    public GameObject[] wallPrefabs;
    [Tooltip("Rocks that float in the void.")]
    public GameObject[] floatingRocks;
    [Tooltip("Tiny gravel, scaled up into drifting debris.")]
    public GameObject[] debris;

    [Header("Layout")]
    public float cellSize = 46f;
    [Tooltip("How far around the player the backdrop is kept (horizontal).")]
    public float spawnRadius = 45f;
    [Tooltip("Rocks are also kept around the route this far, so the backdrop is in place before the camera reaches it. Nothing is built or removed while the camera can see it.")]
    public float pathReach = 50f;
    [Range(0f, 1f)] public float wallChance = 0.75f;
    [Range(1, 3)] public int wallsPerCell = 1;
    [Range(0f, 1f)] public float floatingChance = 0.5f;
    [Range(0, 4)] public int floatingPerCell = 1;
    [Range(0, 6)] public int debrisPerCell = 1;
    public int seed = 7;

    [Header("Walls")]
    public Vector2 wallWidth = new Vector2(28f, 70f);
    [Tooltip("Total height of a wall column; it is fogged out long before the bottom.")]
    public float wallDepth = 260f;
    [Range(1, 8)] public int wallMaxStack = 8;
    [Tooltip("Wall tops sit between anchor+x and anchor+y, in world units above/below the player's height.")]
    public Vector2 wallTopRange = new Vector2(35f, 100f);

    [Header("Floating rocks")]
    public Vector2 floatingWidth = new Vector2(12f, 34f);
    public Vector2 floatingHeightRange = new Vector2(-90f, 20f);
    public Vector2 debrisWidth = new Vector2(2f, 6f);

    [Header("Fallback fog (only when the scene has no ChasmFog)")]
    [Tooltip("Rock is untouched this far below a wall's top...")]
    public float fogClearDrop = 0f;
    [Tooltip("...and fully the background colour this far below it.")]
    public float fogFullDrop = 70f;
    public Color rockTint = new Color(0.32f, 0.36f, 0.5f, 1f);

    [Header("Path protection")]
    [Tooltip("Walls stay at least this far (horizontally) from every path cube, so the space around the route is an empty chasm.")]
    public float wallClearance = 32f;
    public float floatingClearance = 20f;
    [Tooltip("Background rock stays at least this far (horizontally) from every house and the cliff it stands on.")]
    public float houseClearance = 12f;
    [Tooltip("Extra room kept around every path cube.")]
    public float pathMargin = 3f;
    [Tooltip("Extra vertical room: rock tops stay this far below/away from any path cube they are next to.")]
    public float pathMarginVertical = 10f;
    public float fadeSeconds = 0.45f;
    public float refreshInterval = 0.25f;

    [Header("Below the sight-lines (nothing ever shrinks in view)")]
    [Tooltip("Rock may only stand below every sight-line from the route to the camera, whichever way the camera swings, so it can never cover the route and never has to shrink away in view. Rock tops are lowered to fit; rock that stops fitting because new track appeared is removed while it is off screen.")]
    public bool belowViewRays = false;
    [Tooltip("Lowest camera pitch (degrees) the rule guards against. The rig holds 35.26.")]
    [Range(10f, 60f)] public float guardPitch = 30f;
    [Tooltip("Extra height kept under the sight-lines.")]
    public float rayMargin = 4f;
    [Tooltip("Track that has not been laid yet may appear this far (horizontally) from the end of the route, so rock this close to the end is kept low enough for that too.")]
    public float futureReach = 100f;
    [Tooltip("...and as much as this far below the end's height.")]
    public float futureDrop = 30f;
    [Tooltip("Mobile / toon: stop stacking a column once its next piece would start inside the solid fog (it could never be seen).")]
    public bool stopStackAtSolidFog = false;

    class Piece
    {
        public Transform root;        // unscaled holder; its localScale is the fade
        public Vector3 pos;
        public Quaternion rot;
        public Bounds local;          // in holder space, at full size
        public float vis = 1f;
        public bool blocked;
        public bool applied;          // the fade state was written to the holder and it is at its target: nothing to do until it changes
        public float clearance;       // horizontal gap kept from every path cube, at any height
    }

    class Cell
    {
        public readonly List<Piece> pieces = new List<Piece>();
        public Transform root;
    }

    /// <summary>
    /// Optional: called with the holder of every rock top just placed (the first piece of a wall column, or a floating
    /// rock), so a look can dress it (StorybookCheckpointController puts trees / houses / towers on it). Children added to
    /// the holder fade and retire with the rock. Null = nothing happens (every other scene).
    /// </summary>
    public System.Action<Transform, bool> RockTopBuilt;

    readonly Dictionary<Vector2Int, Cell> cells = new Dictionary<Vector2Int, Cell>();
    readonly List<Vector3> cubePoints = new List<Vector3>();
    readonly List<Vector4> houseSpots = new List<Vector4>();     // x, z, radius, top y
    readonly List<Vector3> protectedPoints = new List<Vector3>(); // path cubes plus points on each house: no rock may draw over these
    Vector3 frontier;      // the last cube of the route: more track may be laid beyond it
    bool hasFrontier;
    Transform container;
    float nextRefresh;
    float anchorY;
    bool anchorSet;
    bool filled;
    readonly Dictionary<Vector2Int, float> retryAt = new Dictionary<Vector2Int, float>();
    Vector3 lastCamDir;

    void OnDisable() { Clear(); }

    void Clear()
    {
        cells.Clear();
        if (container != null)
        {
            if (Application.isPlaying) Destroy(container.gameObject); else DestroyImmediate(container.gameObject);
            container = null;
        }
    }

    void Reset() { AutoFind(); }

    void AutoFind()
    {
        if (generator == null) generator = FindFirstObjectByType<DynamicPathGenerator>();
        if (player == null) player = FindFirstObjectByType<CubeRailRunner>();
        if (cam == null) cam = Camera.main;
        if (houses == null) houses = FindFirstObjectByType<NeonWindowDecorator>();
    }

    void Start() { AutoFind(); }

    void LateUpdate()
    {
        if (!enableBackground) { if (container != null) Clear(); return; }
        if (generator == null || player == null || cam == null) { AutoFind(); if (generator == null || player == null || cam == null) return; }
        if (wallPrefabs == null || wallPrefabs.Length == 0) return;

        if (container == null)
        {
            container = new GameObject("PathBackground").transform;
            container.SetParent(transform, false);
            nextRefresh = 0f;
        }

        Vector3 dir = cam.transform.forward;
        bool camMoved = (dir - lastCamDir).sqrMagnitude > 1e-6f;
        if (Time.time >= nextRefresh || camMoved)
        {
            if (Time.time >= nextRefresh) { nextRefresh = Time.time + refreshInterval; Refresh(); }
            lastCamDir = dir;
            UpdateBlocking();
        }
        Fade();
    }

    // ---------- placement ----------

    void Refresh()
    {
        Vector3 p = player.transform.position;
        anchorY = anchorSet ? Mathf.Lerp(anchorY, p.y, 0.2f) : p.y;
        anchorSet = true;

        CollectCubes();
        Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(cam);
        bool first = !filled;
        filled = true;
        if (belowViewRays) RetireUnfit(frustum);

        // Wanted: around the player and around the route ahead, so rocks are already there before the camera looks that way.
        float reach = Mathf.Max(spawnRadius, pathReach);
        int r = Mathf.CeilToInt(reach / cellSize) + 1;
        int cx = Mathf.FloorToInt(p.x / cellSize), cz = Mathf.FloorToInt(p.z / cellSize);
        var wanted = new HashSet<Vector2Int>();
        for (int x = cx - r - 4; x <= cx + r + 4; x++)
            for (int z = cz - r - 4; z <= cz + r + 4; z++)
            {
                Vector2 c = new Vector2((x + 0.5f) * cellSize, (z + 0.5f) * cellSize);
                if (!Wanted(c, p)) continue;
                var key = new Vector2Int(x, z);
                wanted.Add(key);
                if (cells.ContainsKey(key)) continue;
                // Nothing is ever built (or removed) in the camera's sight, except the very first fill.
                if (!first && retryAt.TryGetValue(key, out float t) && Time.time < t) continue;
                Cell built = BuildCell(x, z);
                if (!first && InSight(built, frustum))
                {
                    Destroy(built.root.gameObject);   // it would pop into view: try again once the camera looks elsewhere
                    retryAt[key] = Time.time + 1f;
                    continue;
                }
                cells[key] = built;
            }

        var drop = new List<Vector2Int>();
        foreach (var kv in cells)
            if (!wanted.Contains(kv.Key) && !InSight(kv.Value, frustum)) drop.Add(kv.Key);
        foreach (var k in drop)
        {
            if (cells[k].root != null) Destroy(cells[k].root.gameObject);
            cells.Remove(k);
        }
    }

    /// <summary>
    /// Throws away every cell the camera cannot see and builds the wanted ones again at once, with the current settings
    /// (a checkpoint that changes the layout calls this as its portal is crossed). Cells in view are kept, so nothing pops.
    /// Not called by anything else.
    /// </summary>
    public void RebuildOutOfSight()
    {
        if (container == null || cam == null || generator == null || player == null) return;
        Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(cam);
        var drop = new List<Vector2Int>();
        foreach (var kv in cells)
            if (!InSight(kv.Value, frustum)) drop.Add(kv.Key);
        foreach (var k in drop)
        {
            if (cells[k].root != null) Destroy(cells[k].root.gameObject);
            cells.Remove(k);
        }
        retryAt.Clear();
        Refresh();
        nextRefresh = Time.time + refreshInterval;
    }

    bool Wanted(Vector2 c, Vector3 player)
    {
        if (Vector2.Distance(c, new Vector2(player.x, player.z)) <= spawnRadius) return true;
        float sq = pathReach * pathReach;
        foreach (Vector3 q in cubePoints)
        {
            float dx = q.x - c.x, dz = q.z - c.y;
            if (dx * dx + dz * dz <= sq) return true;
        }
        return false;
    }

    /// <summary>True if any part of the cell that is not already lost in the fog is inside the camera's view.</summary>
    bool InSight(Cell cell, Plane[] frustum)
    {
        foreach (Piece pc in cell.pieces)
            if (PieceInSight(pc, frustum)) return true;
        return false;
    }

    bool PieceInSight(Piece pc, Plane[] frustum)
    {
        if (pc.root == null) return false;
        ChasmFog chasm = ChasmFog.Active;
        float visibleFloor = chasm != null && chasm.Running ? chasm.SolidY : anchorY - fogFullDrop;   // below this, rock is lost in the fog
        foreach (Renderer r in pc.root.GetComponentsInChildren<Renderer>())
        {
            Bounds b = r.bounds;
            if (b.max.y < visibleFloor) continue;
            if (GeometryUtility.TestPlanesAABB(frustum, b)) return true;
        }
        return false;
    }

    /// <summary>Removes rock that no longer fits under the sight-lines (new track was laid next to it), but only while the camera cannot see it.</summary>
    void RetireUnfit(Plane[] frustum)
    {
        foreach (var cell in cells.Values)
            for (int i = cell.pieces.Count - 1; i >= 0; i--)
            {
                Piece pc = cell.pieces[i];
                if (pc.root == null) { cell.pieces.RemoveAt(i); continue; }
                if (!ViolatesViewRays(pc) || PieceInSight(pc, frustum)) continue;
                Destroy(pc.root.gameObject);
                cell.pieces.RemoveAt(i);
            }
    }

    // ---------- the sight-line rule ----------

    /// <summary>Highest a rock centred at "at" and reaching "halfSize" sideways may stand: below the sight-line up from every path cube and house, at any yaw.</summary>
    float TopLimit(Vector3 at, float halfSize)
    {
        float tan = Mathf.Tan(guardPitch * Mathf.Deg2Rad);
        float limit = float.PositiveInfinity;
        foreach (Vector3 c in cubePoints)
        {
            float dx = c.x - at.x, dz = c.z - at.z;
            float d = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - halfSize);
            limit = Mathf.Min(limit, c.y + d * tan - rayMargin);
        }
        if (hasFrontier)
        {
            float dx = frontier.x - at.x, dz = frontier.z - at.z;
            float d = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - halfSize - futureReach);
            limit = Mathf.Min(limit, frontier.y - futureDrop + d * tan - rayMargin);
        }
        foreach (Vector4 h in houseSpots)
        {
            float dx = h.x - at.x, dz = h.y - at.z;
            float d = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - halfSize - h.z);
            limit = Mathf.Min(limit, h.w - 12f + d * tan - rayMargin);
        }
        return limit;
    }

    static float WorldTop(Piece pc)
    {
        Vector3 c = pc.rot * pc.local.center, e = pc.local.extents;
        Vector3 r = pc.rot * Vector3.right, u = pc.rot * Vector3.up, f = pc.rot * Vector3.forward;
        return pc.pos.y + c.y + Mathf.Abs(r.y) * e.x + Mathf.Abs(u.y) * e.y + Mathf.Abs(f.y) * e.z;
    }

    /// <summary>True if any sight-line from a path cube or house, at any yaw, could pass through the piece.</summary>
    bool ViolatesViewRays(Piece pc)
    {
        float tan = Mathf.Tan(guardPitch * Mathf.Deg2Rad);
        float top = WorldTop(pc), margin = rayMargin * 0.5f;
        Quaternion inv = Quaternion.Inverse(pc.rot);
        Vector3 ext = pc.local.extents, ctr = pc.local.center;
        foreach (Vector3 c in cubePoints)
        {
            Vector3 l = inv * (c - pc.pos) - ctr;
            float dx = Mathf.Max(Mathf.Abs(l.x) - ext.x, 0f), dz = Mathf.Max(Mathf.Abs(l.z) - ext.z, 0f);
            if (top > c.y + Mathf.Sqrt(dx * dx + dz * dz) * tan - margin) return true;
        }
        if (hasFrontier)
        {
            Vector3 l = inv * (new Vector3(frontier.x, pc.pos.y, frontier.z) - pc.pos) - ctr;
            float dx = Mathf.Max(Mathf.Abs(l.x) - ext.x, 0f), dz = Mathf.Max(Mathf.Abs(l.z) - ext.z, 0f);
            float d = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - futureReach);
            if (top > frontier.y - futureDrop + d * tan - margin) return true;
        }
        foreach (Vector4 h in houseSpots)
        {
            Vector3 l = inv * (new Vector3(h.x, pc.pos.y, h.y) - pc.pos) - ctr;
            float dx = Mathf.Max(Mathf.Abs(l.x) - ext.x, 0f), dz = Mathf.Max(Mathf.Abs(l.z) - ext.z, 0f);
            float d = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - h.z);
            if (top > h.w - 12f + d * tan - margin) return true;
        }
        return false;
    }

    void CollectCubes()
    {
        cubePoints.Clear();
        foreach (var zone in generator.Zones)
            foreach (var seg in zone.segments)
                foreach (var cube in seg.cubes)
                    if (cube != null) cubePoints.Add(cube.transform.position);

        hasFrontier = false;
        var zs = generator.Zones;
        if (zs.Count > 0)
        {
            var segs = zs[zs.Count - 1].segments;
            for (int i = segs.Count - 1; i >= 0 && !hasFrontier; i--)
                for (int j = segs[i].cubes.Count - 1; j >= 0; j--)
                    if (segs[i].cubes[j] != null) { frontier = segs[i].cubes[j].transform.position; hasFrontier = true; break; }
        }

        if (houses != null) houses.GetHouseFootprints(houseSpots); else houseSpots.Clear();
        protectedPoints.Clear();
        protectedPoints.AddRange(cubePoints);
        foreach (Vector4 h in houseSpots)
        {
            protectedPoints.Add(new Vector3(h.x, h.w, h.y));          // roof
            protectedPoints.Add(new Vector3(h.x, h.w - 12f, h.y));    // the house's base and the top of its cliff
        }
    }

    /// <summary>Horizontal keep-out: nothing sits under, over or beside the route closer than its clearance, so the chasm around the path stays empty.</summary>
    bool TouchesPath(Piece pc)
    {
        Quaternion inv = Quaternion.Inverse(pc.rot);
        Vector3 ext = pc.local.extents, ctr = pc.local.center;
        float sq = pc.clearance * pc.clearance;
        foreach (Vector3 c in cubePoints)
        {
            Vector3 l = inv * (c - pc.pos) - ctr;
            float dx = Mathf.Max(Mathf.Abs(l.x) - ext.x, 0f), dz = Mathf.Max(Mathf.Abs(l.z) - ext.z, 0f);
            if (dx * dx + dz * dz < sq) return true;
        }
        foreach (Vector4 h in houseSpots)
        {
            Vector3 l = inv * (new Vector3(h.x, pc.pos.y, h.y) - pc.pos) - ctr;
            float dx = Mathf.Max(Mathf.Abs(l.x) - ext.x, 0f), dz = Mathf.Max(Mathf.Abs(l.z) - ext.z, 0f);
            float r = h.z + houseClearance;
            if (dx * dx + dz * dz < r * r) return true;
        }
        return belowViewRays && ViolatesViewRays(pc);
    }

    Cell BuildCell(int x, int z)
    {
        var cell = new Cell();
        cell.root = new GameObject("Cell " + x + "," + z).transform;
        cell.root.SetParent(container, false);
        var rng = new System.Random(seed * 73856093 ^ x * 19349663 ^ z * 83492791);
        float F(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        Vector3 Jitter() => new Vector3((x + 0.5f + F(-0.4f, 0.4f)) * cellSize, 0f, (z + 0.5f + F(-0.4f, 0.4f)) * cellSize);

        for (int w = 0; w < wallsPerCell; w++)
            if (rng.NextDouble() < wallChance)
            {
                Vector3 at = Jitter();
                at.y = anchorY + F(wallTopRange.x, wallTopRange.y);
                float wWidth = F(wallWidth.x, wallWidth.y);
                if (belowViewRays)
                {
                    at.y = Mathf.Min(at.y, TopLimit(at, wWidth * 0.72f));
                    ChasmFog fogNow = ChasmFog.Active;
                    float lostBelow = fogNow != null && fogNow.Running ? fogNow.SolidY : anchorY - fogFullDrop * 1.5f;
                    if (at.y < lostBelow) continue;   // the whole column would sit in the fog
                }
                BuildWall(cell, at, wWidth, rng);
            }
        int nf = floatingRocks != null && floatingRocks.Length > 0 ? (rng.NextDouble() < floatingChance ? rng.Next(1, floatingPerCell + 1) : 0) : 0;
        for (int i = 0; i < nf; i++)
        {
            Vector3 at = Jitter();
            at.y = anchorY + F(floatingHeightRange.x, floatingHeightRange.y);
            var fpc = AddPiece(cell, floatingRocks[rng.Next(floatingRocks.Length)], at, F(floatingWidth.x, floatingWidth.y), 1f,
                     Quaternion.Euler(F(-15f, 15f), F(0f, 360f), F(-15f, 15f)), at.y, at.y - fogFullDrop * 1.2f, at.y + 40f);
            if (fpc != null && RockTopBuilt != null) RockTopBuilt(fpc.root, false);
        }
        int nd = debris != null && debris.Length > 0 ? rng.Next(0, debrisPerCell + 1) : 0;
        for (int i = 0; i < nd; i++)
        {
            Vector3 at = Jitter();
            at.y = anchorY + F(floatingHeightRange.x, floatingHeightRange.y + 15f);
            AddPiece(cell, debris[rng.Next(debris.Length)], at, F(debrisWidth.x, debrisWidth.y), 1f,
                     Quaternion.Euler(F(0f, 360f), F(0f, 360f), F(0f, 360f)), at.y, at.y - fogFullDrop * 1.2f, at.y + 40f);
        }
        return cell;
    }

    void BuildWall(Cell cell, Vector3 top, float width, System.Random rng)
    {
        float y = top.y;
        float bottom = top.y - wallDepth;
        bool topDressed = false;
        ChasmFog solidFog = stopStackAtSolidFog ? ChasmFog.Active : null;
        float solidY = solidFog != null && solidFog.Running ? solidFog.SolidY - 2f : float.NegativeInfinity;
        for (int n = 0; n < wallMaxStack && y > bottom && y > solidY; n++)
        {
            GameObject prefab = wallPrefabs[rng.Next(wallPrefabs.Length)];
            if (prefab == null) continue;
            float w = width * (float)(0.8 + rng.NextDouble() * 0.2);
            var pc = AddPiece(cell, prefab, new Vector3(top.x, y, top.z), w, 1f,
                              Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), top.y - fogClearDrop, top.y - fogFullDrop, top.y);
            if (pc == null) continue;
            if (!topDressed && RockTopBuilt != null) RockTopBuilt(pc.root, true);
            topDressed = true;
            y -= pc.local.size.y * 0.8f;   // pieces overlap so the joins sit inside the rock
        }
    }

    /// <summary>Places one prefab with its pivot (base centre) at "basePos" minus its own height, i.e. hanging down from basePos.y for walls.</summary>
    Piece AddPiece(Cell cell, GameObject prefab, Vector3 top, float targetWidth, float stretch, Quaternion rot,
                   float fogClearY, float fogFullY, float fogTop)
    {
        if (prefab == null) return null;
        Bounds pb;
        if (!PrefabBounds(prefab, out pb)) return null;
        float footprint = Mathf.Max(pb.size.x, pb.size.z);
        if (footprint < 1e-4f) return null;
        float s = targetWidth / footprint;
        float sy = s * stretch;
        float height = pb.size.y * sy;

        // "top" is where the piece's top sits for walls (hangs down), and its centre for floating pieces.
        bool wall = System.Array.IndexOf(wallPrefabs, prefab) >= 0;
        if (belowViewRays && !wall)
            top.y = Mathf.Min(top.y, TopLimit(top, targetWidth * 0.72f) - height * 0.5f - targetWidth * 0.15f);
        Vector3 pivot = wall ? new Vector3(top.x, top.y - height, top.z) : new Vector3(top.x, top.y - pb.center.y * sy, top.z);

        var holder = new GameObject(prefab.name).transform;
        holder.SetParent(cell.root, false);
        holder.SetPositionAndRotation(pivot, rot);
        var visual = Instantiate(prefab, holder, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = new Vector3(s, sy, s);
        foreach (Collider c in visual.GetComponentsInChildren<Collider>(true)) Destroy(c);

        Color fog = cam.clearFlags == CameraClearFlags.SolidColor ? cam.backgroundColor : RenderSettings.fogColor;
        var block = new MaterialPropertyBlock();
        foreach (Renderer r in visual.GetComponentsInChildren<Renderer>())
        {
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", rockTint);
            block.SetColor("_Color", rockTint);
            block.SetColor("_HeightFogColor", fog);
            block.SetFloat("_HeightFogEnd", fogClearY);
            block.SetFloat("_HeightFogStart", fogFullY);
            r.SetPropertyBlock(block);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        ChasmFog.Apply(visual, receiveShadows: false);   // with a ChasmFog in the scene, its fog replaces the per-piece fade above

        var piece = new Piece
        {
            root = holder,
            pos = pivot,
            rot = rot,
            local = new Bounds(Vector3.Scale(pb.center, new Vector3(s, sy, s)), Vector3.Scale(pb.size, new Vector3(s, sy, s)))
        };
        piece.clearance = wall ? wallClearance : floatingClearance;
        if (TouchesPath(piece)) { Destroy(holder.gameObject); return null; }
        
        piece.vis = 1f;
        piece.blocked = false;
        cell.pieces.Add(piece);
        return piece;
    }

    static readonly Dictionary<GameObject, Bounds> boundsCache = new Dictionary<GameObject, Bounds>();

    static bool PrefabBounds(GameObject prefab, out Bounds b)
    {
        if (boundsCache.TryGetValue(prefab, out b) && b.size.sqrMagnitude > 0f) return true;
        b = default;
        var rs = prefab.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return false;
        // Prefabs are unrotated at unit scale, so renderer bounds are already in the prefab's own space.
        b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        if (b.size.sqrMagnitude <= 0f)
        {
            // A runtime template (e.g. a look's procedural rock kept under an inactive parent): its renderers report no
            // bounds, so measure its meshes in its own space.
            bool any = false;
            foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 m = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Bounds mb = mf.sharedMesh.bounds;
                Vector3 c = m.MultiplyPoint3x4(mb.center);
                Vector3 e = mb.extents;
                Vector3 ex = new Vector3(
                    Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
                    Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
                    Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);
                var wb = new Bounds(c, ex * 2f);
                if (!any) { b = wb; any = true; } else b.Encapsulate(wb);
            }
            if (!any) return false;
        }
        boundsCache[prefab] = b;
        return true;
    }

    // ---------- keeping the route clear ----------

    void UpdateBlocking()
    {
        Vector3 toCam = -cam.transform.forward;   // orthographic: every ray to the camera is parallel
        Vector3 camPos = cam.transform.position;
        foreach (var cell in cells.Values)
            foreach (var pc in cell.pieces)
            {
                Bounds b = pc.local;
                b.Expand(new Vector3(pathMargin * 2f, pathMarginVertical * 2f, pathMargin * 2f));
                Quaternion inv = Quaternion.Inverse(pc.rot);
                bool block = b.Contains(inv * (camPos - pc.pos));   // never swallow the camera
                if (!block)
                {
                    Vector3 ld = inv * toCam;
                    foreach (Vector3 c in protectedPoints)
                    {
                        var ray = new Ray(inv * (c - pc.pos), ld);
                        if (b.IntersectRay(ray)) { block = true; break; }
                    }
                }
                pc.blocked = block;
            }
    }

    void Fade()
    {
        float step = fadeSeconds > 0.01f ? Time.deltaTime / fadeSeconds : 1f;
        foreach (var cell in cells.Values)
            foreach (var pc in cell.pieces)
            {
                if (pc.root == null) continue;
                float target = pc.blocked ? 0f : 1f;
                // Settled pieces (the vast majority, every frame) are left alone: writing localScale / SetActive on hundreds
                // of rocks per frame was the largest script cost of the whole game.
                if (pc.applied && pc.vis == target) continue;
                pc.vis = Mathf.MoveTowards(pc.vis, target, step);
                float e = pc.vis * pc.vis * (3f - 2f * pc.vis);
                pc.root.localScale = Vector3.one * Mathf.Max(e, 0.0001f);
                pc.root.gameObject.SetActive(pc.vis > 0.001f || !pc.blocked);
                pc.applied = pc.vis == target;
            }
    }
}
