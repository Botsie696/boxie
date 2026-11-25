using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// OBSTACLES. Past obstacleAfterLeg, an eligible leg has obstacleChance of getting ONE small red
/// pyramid stuck to a face of one cube near the middle of the leg (obstacleMiddleSpread either side
/// of centre, and never within obstacleEdgeMargin cubes of either end, so there is always a clear
/// stretch to react in). The apex points outward.
///
/// It always sits on the face the runner needs for the NEXT turn (the same outward face
/// SpawnCoinsBehindTurn computes in DynamicPathGeneratorCoins.cs), so dodging it means leaving the
/// turn face and swiping back before the corner - same shape of choice as coins' wrong-side runs,
/// just lethal instead of a bonus.
///
/// Touching it (PathObstacleGuard) is exactly like running off the track: it stops the runner and
/// hands to GameOverSequence, so obstacles get the same burst as any other death for free.
///
/// The look is the totem blocks' TotemBlock material (obstaclePyramidMaterial, plain part) in red.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Obstacles")]
    [Tooltip("Stick a small red pyramid onto the turn face of some legs.")]
    public bool obstacleSections = false;
    [Tooltip("Obstacles only start appearing after this many points of score.")]
    [Range(0, 200)] public int obstacleAfterLeg = 10;
    [Tooltip("No obstacles while the legs being laid belong to checkpoint zones 1..this value (2 = zones 1 and 2 stay clear). 0 = off, obstacles in every zone.")]
    [Range(0, 10)] public int obstacleFreeZones = 0;
    [Tooltip("Chance (0-1) an eligible leg gets an obstacle. 0.45 = 45%.")]
    [Range(0f, 1f)] public float obstacleChance = 0.45f;
    [Tooltip("A leg shorter than this many cubes never gets an obstacle.")]
    [Range(1, 40)] public int obstacleMinLegCubes = 20;
    [Tooltip("Optional: a prefab to stand on the turn face instead of the generated block (e.g. Assets/Prefabs/Obstacles/Obstacle_Left_5_15). Its eyes are turned to look along the leg. Empty = the generated block.")]
    public GameObject obstaclePrefab;
    [Tooltip("Only lay obstacles on legs that run sideways (never on a leg that climbs or drops).")]
    public bool obstacleHorizontalLegsOnly = false;
    [Tooltip("Material of the pyramid: the totem blocks' TotemBlock material in red.")]
    public Material obstaclePyramidMaterial;
    [Tooltip("Cubes kept clear at each end of the leg, so a pyramid is never at the very start or end.")]
    [Range(1, 12)] public int obstacleEdgeMargin = 4;
    [Tooltip("How far either side of the leg's centre the pyramid may sit, as a fraction of the leg's length. 0 = dead centre.")]
    [Range(0f, 0.5f)] public float obstacleMiddleSpread = 0.12f;
    [Tooltip("No coins within this many cubes of an obstacle, along the leg either side of it (2 = the two cubes before, the two after, and the obstacle's own cube). 0 = off, coins ignore obstacles.")]
    [Range(0, 6)] public int coinClearFromObstacle = 0;
    [Tooltip("Height of the pyramid, in cubes.")]
    [Range(0.1f, 3f)] public float obstaclePyramidHeight = 2.2f;
    [Tooltip("Base width of the pyramid, as a fraction of the cube size.")]
    [Range(0.2f, 1.5f)] public float obstaclePyramidBaseScale = 0.8f;
    [Tooltip("Layer the live obstacles are put on. Must NOT be the ground-probe layer, or the runner's forward probe would mistake one for the next path cube - add this layer in Project Settings > Tags and Layers if it does not exist yet.")]
    public string obstacleLayerName = "Obstacle";
    [Tooltip("Colour of the small glow every obstacle gets.")]
    public Color obstacleGlowColor = new Color(1f, 0.85f, 0.2f);
    [Tooltip("Brightness of that glow.")]
    [Range(0f, 3f)] public float obstacleGlowIntensity = 1.2f;
    [Tooltip("Range of the glow light, in cubes.")]
    [Range(0.5f, 4f)] public float obstacleGlowRange = 1.6f;

    private System.Random obstacleRng;
    private int obstacleLayer = -1;
    private PathDepthShading obstacleShading;
    private Mesh obstacleMesh;
    private float obstacleMeshHalf, obstacleMeshHeight;

    void TryPlaceObstacleBehindTurn(Segment incoming, Vector3 nextDir)
    {
        if (!obstacleSections || incoming == null) return;
        if (TutorialLegOf(incoming) >= 0) return;   // TUTORIAL ADDITION: the scripted legs carry only the tutorial's own obstacle
        if (incoming.boost || incoming.portalRole != 0) return;   // boosts and zone runways stay clear
        if (obstacleHorizontalLegsOnly && Mathf.Abs(incoming.direction.y) > 0.5f) return;
        if (PowerBlocksObstacle(incoming)) return;                // the power-diamond legs stay plain
        if (incoming.gate) return;                                // a triangle gate leg (3.6 only) carries nothing else
        if (segmentSerial <= obstacleAfterLeg) return;
        if (obstacleFreeZones > 0 && GateZoneIndex <= obstacleFreeZones) return;
        if (incoming.cubes.Count < obstacleMinLegCubes) return;   // short legs stay clear: no room to read it

        obstacleRng ??= new System.Random();
        if (Waves)
        {
            // DifficultyProfile: BUILD phase only, within budget, never with a bridge (and so never with a multi-plank).
            if (incoming.plankCount > 0 || !CanSpend(incoming, difficultyProfile.costObstacle)) return;
            if (obstacleRng.NextDouble() >= obstacleChance * HazardScale(incoming, Hazard.Obstacle)) return;
        }
        else if (obstacleRng.NextDouble() >= obstacleChance) return;

        List<GameObject> cubes = incoming.cubes;
        // Obstacles and bridge planks never share a leg - if this leg has a bridge anywhere on it
        // (decided when the leg itself was laid, in TryPlanBridge), skip it entirely.
        if (GetBridgeSpan(cubes, out _, out _)) return;

        int margin = Mathf.Max(1, obstacleEdgeMargin);
        int lo = margin;
        int hi = cubes.Count - 1 - margin;
        // DifficultyProfile: the dodge is one roll off the turn face before the pyramid and one roll back after it,
        // so each side of it gets a full 1-roll floor (T_human included) at this leg's speed.
        if (TB)
        {
            int side = CubesForSeconds(difficultyProfile.MinLegSeconds(1, incoming.serial, false), Mathf.Max(0.01f, incoming.speed));
            lo = Mathf.Max(lo, side);
            hi = Mathf.Min(hi, cubes.Count - 1 - side);
        }
        if (hi < lo) return;

        // Middle-ish: the leg's centre, nudged by up to obstacleMiddleSpread of its length.
        float centre = (cubes.Count - 1) * 0.5f;
        float nudge = (float)(obstacleRng.NextDouble() * 2.0 - 1.0) * obstacleMiddleSpread * cubes.Count;
        int index = Mathf.Clamp(Mathf.RoundToInt(centre + nudge), lo, hi);
        if (cubes[index] == null) return;
        PlaceObstaclePyramid(cubes[index].transform, nextDir);
        ClearCoinsNearObstacles(incoming);
        if (difficultyProfile != null && !incoming.obstacle)
        {
            incoming.obstacle = true;
            incoming.hazardCost += difficultyProfile.costObstacle;
        }
    }

    // Takes every coin within coinClearFromObstacle cubes of an obstacle off the leg. Coins are laid before the
    // obstacle, and again by the Tune panel's reroll, so this runs after both.
    void ClearCoinsNearObstacles(Segment seg)
    {
        if (coinClearFromObstacle <= 0 || seg == null) return;

        List<GameObject> cubes = seg.cubes;
        for (int o = 0; o < cubes.Count; o++)
        {
            if (cubes[o] == null || cubes[o].GetComponentInChildren<PathObstacleGuard>(true) == null) continue;

            for (int i = coins.Count - 1; i >= 0; i--)
            {
                if (coins[i].segment != seg) continue;
                int at = cubes.IndexOf(coins[i].cube);
                if (at >= 0 && Mathf.Abs(at - o) <= coinClearFromObstacle) RemoveCoinAt(i);
            }
        }
    }

    // The pyramid stands on the cube's face, base flat against it, apex pointing outward.
    void PlaceObstaclePyramid(Transform cubeT, Vector3 nextDir)
    {
        if (obstacleLayer < 0) obstacleLayer = ResolveObstacleLayer();

        Vector3 outward = ObstacleFace(cubeT, nextDir);
        if (obstaclePrefab != null) { PlaceObstaclePrefab(cubeT, outward); return; }

        var go = new GameObject("Obstacle") { layer = obstacleLayer };
        go.transform.SetParent(cubeT, true);
        go.transform.position = cubeT.position + outward * (cubeSize * 0.5f);
        go.transform.rotation = Quaternion.LookRotation(cubeT.forward, outward);

        go.AddComponent<MeshFilter>().sharedMesh = GetObstacleMesh();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = obstaclePyramidMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var col = go.AddComponent<MeshCollider>();
        col.sharedMesh = obstacleMesh;
        col.convex = true;
        col.isTrigger = true;

        Light glow = null;
        if (obstacleGlowIntensity > 0f)
        {
            var lightGO = new GameObject("ObstacleGlow") { layer = obstacleLayer };
            lightGO.transform.SetParent(go.transform, false);
            lightGO.transform.localPosition = Vector3.up * (cubeSize * obstaclePyramidHeight * 0.4f);
            glow = lightGO.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = obstacleGlowColor;
            glow.intensity = obstacleGlowIntensity;
            glow.range = obstacleGlowRange * cubeSize;
            glow.shadows = LightShadows.None;
        }

        if (obstacleShading == null) obstacleShading = GetComponent<PathDepthShading>();

        var guard = go.AddComponent<PathObstacleGuard>();
        guard.player = player;
        guard.blockedSide = SideIndexFromOutward(cubeT, outward);
        guard.Init(cubeT.gameObject, obstacleShading, go.transform.localScale, new Collider[] { col }, glow);
    }

    // A prefab block (one unit) sitting on the turn face, eyes turned along the leg, with a trigger box for the guard.
    void PlaceObstaclePrefab(Transform cubeT, Vector3 outward)
    {
        GameObject go = Instantiate(obstaclePrefab, cubeT.position + outward * cubeSize, Quaternion.FromToRotation(Vector3.up, cubeT.forward), cubeT);
        go.name = "Obstacle";
        go.transform.localScale = Vector3.one * cubeSize;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = obstacleLayer;
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        var col = go.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = Vector3.one * 0.9f;
        if (obstacleShading == null) obstacleShading = GetComponent<PathDepthShading>();
        var guard = go.AddComponent<PathObstacleGuard>();
        guard.player = player;
        guard.blockedSide = SideIndexFromOutward(cubeT, outward);
        guard.Init(cubeT.gameObject, obstacleShading, go.transform.localScale, new Collider[] { col }, null);
    }

    // One shared cube mesh (rebuilt only if the size slider changes), base on y = 0, top at +y.
    Mesh GetObstacleMesh()
    {
        float half = cubeSize * obstaclePyramidBaseScale * 0.5f;
        float height = half * 2f; // a cube: as tall as it is wide
        if (obstacleMesh != null && Mathf.Approximately(half, obstacleMeshHalf) && Mathf.Approximately(height, obstacleMeshHeight))
            return obstacleMesh;

        // A plain cube: side = base scale, standing on the face. (The pyramid height slider no longer applies.)
        Vector3 c0 = new Vector3(-half, 0f, -half), c1 = new Vector3(half, 0f, -half);
        Vector3 c2 = new Vector3(half, 0f, half), c3 = new Vector3(-half, 0f, half);
        Vector3 t0 = c0 + Vector3.up * height, t1 = c1 + Vector3.up * height;
        Vector3 t2 = c2 + Vector3.up * height, t3 = c3 + Vector3.up * height;

        var mesh = new Mesh { name = "ObstacleCube" };
        Vector3[] verts =
        {
            c0, c2, c1,  c0, c3, c2,     // bottom
            t0, t1, t2,  t0, t2, t3,     // top
            c0, t0, t3,  c0, t3, c3,     // -x
            c1, c2, t2,  c1, t2, t1,     // +x
            c0, c1, t1,  c0, t1, t0,     // -z
            c3, t3, t2,  c3, t2, c2,     // +z
        };
        int[] tris = new int[verts.Length];
        for (int i = 0; i < tris.Length; i++) tris[i] = i;
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        obstacleMesh = mesh;
        obstacleMeshHalf = half;
        obstacleMeshHeight = height;
        return mesh;
    }

    // Same face math as coins (PlaceSingleCoin): the face pointing toward nextDir is the one the
    // runner needs for the turn, and that is always where the pyramid goes.
    Vector3 ObstacleFace(Transform cubeT, Vector3 nextDir)
    {
        Vector3[] faces = { cubeT.up, cubeT.right, -cubeT.up, -cubeT.right };
        Vector3 nextW = nextDir.sqrMagnitude > 0.0001f ? nextDir.normalized : cubeT.forward;
        float best = -2f;
        Vector3 bestFace = faces[0];
        foreach (Vector3 f in faces)
        {
            float d = Vector3.Dot(f, nextW);
            if (d > best) { best = d; bestFace = f; }
        }
        return bestFace;
    }

    int ResolveObstacleLayer()
    {
        int l = LayerMask.NameToLayer(obstacleLayerName);
        if (l < 0)
        {
            Debug.LogWarning($"[Obstacles] layer '{obstacleLayerName}' does not exist - add it in Project Settings > Tags and Layers. Falling back to Default, which risks confusing the ground probe.");
            return 0;
        }
        return l;
    }

    // 0 up, 1 right, 2 down, 3 left - CubeRailRunner.currentSideIndex's convention, so AutoPlayer
    // (and anything else) can read a guard's blockedSide directly against the player's own side.
    static int SideIndexFromOutward(Transform cubeT, Vector3 outward)
    {
        if (Vector3.Dot(outward, cubeT.up) > 0.9f) return 0;
        if (Vector3.Dot(outward, cubeT.right) > 0.9f) return 1;
        if (Vector3.Dot(outward, -cubeT.up) > 0.9f) return 2;
        return 3;
    }
}
