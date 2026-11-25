using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Coins along the track - the coin part of the old game's UnifiedMazeGenerator3D (PathManager.cs),
/// carried over to this generator's single path.
///
/// The placement rule is the old one. When a leg is laid in direction d, the straight behind the
/// turn gets a short run of coins on the face that points toward d: skip the cubes nearest the
/// corner and at the far end, then a coin on every coinSpacing-th cube going back from the corner.
/// Here that face is also the one the runner has to be on to take the turn (see the DIAGONAL RISER
/// note), so the coins double as a hint. Every leg turns a real 90 degrees, so the old "coin inside
/// the next cube" case on a straight continuation cannot happen.
///
/// What is different, for mobile:
///   * A coin is only data until its cube is drawn (PathDepthShading shows a few legs ahead). Only
///     then does it borrow a pooled renderer, and it hands it back when the cube is hidden or culled.
///     So the live coin objects are the ones on screen, nothing is instantiated after the opening
///     seconds, and missed coins go away with their cube instead of piling up.
///   * No colliders. Pickup is a swept distance test against the runner. A trigger on the Default
///     layer would be hit by the runner's own forward probe (raycasts hit triggers) and taken for
///     the next path cube.
///   * One spin angle for every coin, applied here - no Update or SlowRotate per coin.
///   * Coins draw from their own random stream, so turning them on or off does not change the path.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Coins")]
    [Tooltip("Lay runs of coins along the track.")]
    public bool spawnCoins = true;
    [Tooltip("Coin model. Only its first mesh is used (Coins_1.fbx).")]
    public GameObject coinPrefab;
    [Tooltip("No coins on the legs before this one, counted from 0 (the leg the runner starts on). 2 = coins can start on the 3rd leg.")]
    [Range(0, 10)] public int firstCoinLeg = 2;
    [Tooltip("Materials for the coin, one per sub-mesh. Empty = the model's own. Give it materials saved with emission on, or a build can strip the glow.")]
    public Material[] coinMaterials;
    [Tooltip("Chance that a leg gets a run of coins.")]
    [Range(0f, 1f)] public float coinSpawnChance = 0.5f;
    [Tooltip("A coin on every Nth cube.")]
    [Range(1, 6)] public int coinSpacing = 2;
    [Tooltip("Cubes before the corner cube that stay clear of coins.")]
    [Range(0, 12)] public int coinTurnMargin = 4;
    [Tooltip("Cubes at the start of the leg (just after the previous turn) that stay clear, so there is time to switch face.")]
    [Range(0, 12)] public int coinLegStartMargin = 4;
    [Tooltip("Fewest coins in a run, when the leg has room for that many.")]
    [Range(1, 30)] public int minCoinsPerRun = 8;
    [Tooltip("Most coins in a run.")]
    [Range(1, 30)] public int maxCoinsPerRun = 15;

    [Header("Wrong-Side Coins")]
    [Tooltip("Chance that a run of coins is laid on a face the runner does not need for the turn, tempting them off the safe face. The rest stay on the turn-facing face.")]
    [Range(0f, 1f)] public float wrongSideRunChance = 0.7f;
    [Tooltip("Wrong-side coins only start after this fraction of the leg's cubes, counted from the leg's start.")]
    [Range(0f, 0.9f)] public float wrongSideStartFraction = 0.2f;
    [Tooltip("Size of a wrong-side coin relative to a normal one.")]
    [Range(0.2f, 1f)] public float wrongSideCoinScale = 0.6f;

    [Header("Coin Look")]
    [Tooltip("Width of a coin in world units (a path cube is 1).")]
    [Range(0.2f, 1.6f)] public float coinSize = 1.1f;
    [Tooltip("How far out from the cube's centre a coin sits, in cubes. The runner rides at 1.05.")]
    [Range(0.8f, 2f)] public float coinOutwardOffset = 1.12f;
    [Tooltip("Small offset in cubes: along the cube's up on a side face, along the leg on the top or bottom face.")]
    [Range(0f, 0.5f)] public float coinFloat = 0.10f;
    [Tooltip("Degrees per second a coin spins about the face it stands on. 0 = it stands still, facing down the leg.")]
    [Range(0f, 720f)] public float coinSpinSpeed = 100f;
    [Tooltip("Degrees of spin between neighbouring coins in a run, so a run turns as a wave. 0 = in step.")]
    [Range(0f, 90f)] public float coinSpinWave = 24f;
    [Tooltip("How much the coins glow in their own colour on top of the scene light.")]
    [Range(0f, 2f)] public float coinGlow = 0.35f;
    [Tooltip("Coins fade and shrink with the track they sit on, so coins far ahead are as see-through and as small as their legs (PathDepthShading). Off = always full (every scene but 3.11).")]
    public bool coinsMatchTrackFade = false;
    [Tooltip("Coins cast shadows: the sun's shadow plus a soft dark blob on the face under each coin (the sun can't shade a face turned away from it). Off is cheaper on mobile.")]
    public bool coinsCastShadows = true;
    [Tooltip("Darkness of the blob under each coin.")]
    [Range(0f, 1f)] public float coinBlobStrength = 0.7f;
    [Tooltip("Blob width as a share of the coin width.")]
    [Range(0.5f, 2.5f)] public float coinBlobSize = 1.6f;
    [Tooltip("Distance from the runner's centre inside which a coin is collected. A coin on the runner's face passes within about 0.2; the next face over is about 1.5 away.")]
    [Range(0.2f, 1.2f)] public float coinPickupRadius = 0.7f;

    class PathCoin
    {
        public Segment segment;
        public GameObject cube;
        public Vector3 cubeCentre;
        public Vector3 outward;       // normal of the face the coin stands on
        public Vector3 floatAxis;
        public Quaternion stand;      // upright on that face, looking down the leg
        public int indexInRun;
        public float sizeScale = 1f;  // below 1 for the wrong-side bait coins
        public Quaternion blobRot;    // flat on the face, normal = outward
        public MeshRenderer blob;     // pooled contact shadow under the coin
        public MeshRenderer view;    // pooled renderer while the cube is drawn
        public float viewScale;
    }

    class CoinBurst
    {
        public MeshRenderer view;
        public Vector3 from;
        public Vector3 up;
        public Quaternion rotation;
        public float scale;
        public float t;
    }

    const float CoinBurstSeconds = 0.3f;
    const int CoinPoolWarm = 32;
    static readonly int CoinBaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int CoinEmissionId = Shader.PropertyToID("_EmissionColor");

    private readonly List<PathCoin> coins = new List<PathCoin>();
    private readonly Stack<PathCoin> spareCoins = new Stack<PathCoin>();
    private readonly List<CoinBurst> coinBursts = new List<CoinBurst>();
    private readonly Stack<CoinBurst> spareBursts = new Stack<CoinBurst>();
    private readonly Stack<MeshRenderer> coinViewPool = new Stack<MeshRenderer>();
    private readonly List<MeshRenderer> allCoinViews = new List<MeshRenderer>();
    private Transform coinRoot;
    private Mesh blobMesh;
    private Material blobMaterial;
    private readonly Stack<MeshRenderer> blobPool = new Stack<MeshRenderer>();
    private Mesh coinMesh;
    private Material[] coinRuntimeMaterials;
    private Quaternion coinMeshTurn = Quaternion.identity;   // turns the model's coin axis onto local forward
    private Vector3 coinMeshCentre;
    private float coinMeshWidth = 1f;
    private float appliedCoinGlow = -1f;
    private bool appliedCoinShadows;
    private System.Random coinRng;
    private CoinCollector coinCollector;
    private Vector3 coinLastRunnerPos;
    private bool coinHaveRunnerPos;
    private float coinSpin;

    /// <summary>Coins laid on the track and not collected yet, drawn or not.</summary>
    public int CoinsOnTrack => coins.Count;

    /// <summary>The runner's coin counter, once coins are in play.</summary>
    public CoinCollector Coins => coinCollector;

    /// <summary>The coin's runtime material copies (shared by every coin), null until coins are set up. A checkpoint look
    /// may tint them (and must put the colours back).</summary>
    public Material[] CoinRuntimeMaterials => coinRuntimeMaterials;

    // ------------------------------------------------------------------
    // Placement
    // ------------------------------------------------------------------

    /// <summary>
    /// The old junction rule. incoming is the leg that ends at the corner, nextDir the heading of
    /// the leg just laid after it. backLine[0] is the cube before the corner cube, then back along
    /// the leg; coins start coinTurnMargin cubes in and stop coinLegStartMargin short of the far end.
    /// </summary>
    void SpawnCoinsBehindTurn(Segment incoming, Vector3 nextDir)
    {
        if (!spawnCoins || incoming == null || !CoinSystemReady()) return;
        if (incoming.portalRole >= 2) return;   // the runway out of a zone door has its own unbroken line of coins; an obstacle leg (3) carries none
        if (TutorialCoinsBehindTurn(incoming, nextDir)) return;   // TUTORIAL ADDITION: hand-placed coin trails

        List<GameObject> cubes = incoming.cubes;

        // A boost leg gets one coin, on the apex of its turn: with constant speed and even cubes
        // the pickups fall on the beat of the double taps.
        if (incoming.boost && boostApexCoins)
        {
            if (cubes.Count > 0 && cubes[cubes.Count - 1] != null)
                PlaceSingleCoin(incoming, cubes[cubes.Count - 1].transform, nextDir, 0);
            return;
        }

        int backCount = cubes.Count - 1;                 // every cube behind the corner cube
        int usableStart = coinTurnMargin;
        int usableEnd = backCount - 1 - coinLegStartMargin;
        int spacing = Mathf.Max(1, coinSpacing);

        // Bridge legs (DynamicPathGeneratorBridge.cs): no coins on a plank, and none in the gap
        // between two planks either - only before the first one or after the last one.
        bool hasBridgeSpan = GetBridgeSpan(cubes, out int bridgeFirst, out int bridgeLast);

        float coinChance = coinSpawnChance;
        if (difficultyProfile != null) coinChance = Mathf.Min(1f, coinChance * difficultyProfile.CoinChanceScale(incoming.cycle));
        if (backCount <= 0 || usableEnd < usableStart || coinRng.NextDouble() >= coinChance) return;

        int maxCoins = (usableEnd - usableStart) / spacing + 1;
        int targetMax = Mathf.Min(Mathf.Max(1, maxCoinsPerRun), maxCoins);
        int targetMin = Mathf.Min(Mathf.Clamp(minCoinsPerRun, 1, Mathf.Max(1, maxCoinsPerRun)), targetMax);
        if (targetMax <= 0) return;

        // Most runs are a temptation: the coins sit on a face the runner does NOT need for the turn,
        // so grabbing them means leaving the safe face and swiping back before the corner.
        int wrongPick = -1;
        float sizeScale = 1f;
        if (LureAllowedOn(incoming) && coinRng.NextDouble() < wrongSideRunChance) // DifficultyProfile: no lures on hard legs
        {
            // No such coin in the first wrongSideStartFraction of the leg - the runner gets a
            // stretch of clear track before the first bait.
            int startSkip = Mathf.Max(coinLegStartMargin, Mathf.CeilToInt(cubes.Count * wrongSideStartFraction));
            int wrongEnd = Mathf.Min(usableEnd, backCount - 1 - startSkip);
            if (wrongEnd >= usableStart)
            {
                usableEnd = wrongEnd;
                maxCoins = (usableEnd - usableStart) / spacing + 1;
                targetMax = Mathf.Min(Mathf.Max(1, maxCoinsPerRun), maxCoins);
                targetMin = Mathf.Min(targetMin, targetMax);
                wrongPick = coinRng.Next(0, 3);
                sizeScale = wrongSideCoinScale;
            }
        }

        int wanted = coinRng.Next(targetMin, targetMax + 1);
        for (int c = 0; c < wanted; c++)
        {
            int idxInBackLine = usableStart + c * spacing;
            if (idxInBackLine > usableEnd) break;

            int cubeIndex = backCount - 1 - idxInBackLine;
            if (hasBridgeSpan && cubeIndex >= bridgeFirst && cubeIndex <= bridgeLast) continue;

            GameObject cube = cubes[cubeIndex];
            if (cube != null) PlaceSingleCoin(incoming, cube.transform, nextDir, c, wrongPick, sizeScale);
        }
    }

    // The span (inclusive, by index in cubes) from the first plank cube to the last on this leg -
    // covers every plank AND the ordinary cubes between planks on a multi-plank bridge leg. Empty
    // (false) on a leg with no bridge at all.
    static bool GetBridgeSpan(List<GameObject> cubes, out int first, out int last)
    {
        first = -1; last = -1;
        for (int i = 0; i < cubes.Count; i++)
        {
            if (cubes[i] == null || cubes[i].GetComponent<BridgePlankGuard>() == null) continue;
            if (first < 0) first = i;
            last = i;
        }
        return first >= 0;
    }

    /// <summary>
    /// Puts a coin on the face of the cube that points toward nextDir, using the cube's own axes
    /// (PlaceBlock-style: its forward is always the direction of travel).
    /// </summary>
    void PlaceSingleCoin(Segment seg, Transform cubeT, Vector3 nextDir, int indexInRun, int wrongPick = -1, float sizeScale = 1f)
    {
        if (tutorialActive) return;   // no coins at all until the tutorial is over
        // Every coin goes through here (runs, boost apex, door runways), so this one check keeps the
        // opening legs clear in every scene. The tutorial's scripted legs are exempt: they teach coins.
        if (seg != null && seg.serial < firstCoinLeg && TutorialLegOf(seg) < 0) return;

        Vector3 nextW = nextDir.normalized;
        if (nextW.sqrMagnitude < 0.0001f) nextW = cubeT.forward;

        float dR = Vector3.Dot(cubeT.right, nextW);
        float dU = Vector3.Dot(cubeT.up, nextW);
        float dF = Vector3.Dot(cubeT.forward, nextW);
        float aR = Mathf.Abs(dR);
        float aU = Mathf.Abs(dU);
        float aF = Mathf.Abs(dF);

        Vector3 outward;
        if (aR >= aU && aR >= aF) outward = dR >= 0f ? cubeT.right : -cubeT.right;
        else if (aU >= aF) outward = dU >= 0f ? cubeT.up : -cubeT.up;
        else outward = dF >= 0f ? cubeT.forward : -cubeT.forward;

        // The front and back faces are never ridden, and a coin pushed out of them lands inside the
        // neighbouring cube. Only a leg that carried straight on could ask for one.
        if (Mathf.Abs(Vector3.Dot(outward, cubeT.forward)) > 0.85f) return;

        // Wrong-side run: move to one of the three ridable faces that is not the turn face.
        // wrongPick (0-2) picks among them, the same way on every cube of the run.
        if (wrongPick >= 0)
        {
            Vector3[] faces = { cubeT.right, -cubeT.right, cubeT.up, -cubeT.up };
            int picked = 0;
            Vector3 wrong = outward;
            foreach (Vector3 f in faces)
            {
                if (Vector3.Dot(f, outward) > 0.85f) continue;
                if (picked++ == wrongPick) { wrong = f; break; }
            }
            outward = wrong;
        }

        // Float along the cube's up, or along the leg when the coin is on the top or bottom face.
        Vector3 floatAxis = cubeT.up;
        if (Mathf.Abs(Vector3.Dot(outward, cubeT.up)) > 0.85f) floatAxis = cubeT.forward;

        PathCoin coin = spareCoins.Count > 0 ? spareCoins.Pop() : new PathCoin();
        coin.segment = seg;
        coin.cube = cubeT.gameObject;
        coin.cubeCentre = cubeT.position;
        coin.outward = outward;
        coin.floatAxis = floatAxis;
        coin.stand = Quaternion.LookRotation(cubeT.forward, outward);
        coin.blobRot = Quaternion.LookRotation(outward, floatAxis);
        coin.blob = null;
        coin.indexInRun = indexInRun;
        coin.sizeScale = sizeScale;
        coin.view = null;
        coin.viewScale = -1f;
        coins.Add(coin);
    }

    Vector3 CoinPosition(PathCoin c) =>
        c.cubeCentre + c.outward * (cubeSize * coinOutwardOffset) + c.floatAxis * (cubeSize * coinFloat);

    /// <summary>
    /// Lays the coins again on every leg that is not drawn yet, with the current settings, so the
    /// Tune panel's coin sliders can be judged without waiting for new track. Legs already on screen
    /// keep theirs.
    /// </summary>
    public void RerollCoinsAhead()
    {
        if (!CoinSystemReady()) return;
        if (shadingRef == null) shadingRef = GetComponent<PathDepthShading>();

        for (int s = Mathf.Max(0, currentPlayerSegmentIdx + 1); s + 1 < allSegments.Count; s++)
        {
            Segment seg = allSegments[s];
            if (SegmentDrawn(seg)) continue;

            ReleaseCoinsOn(seg);
            SpawnCoinsBehindTurn(seg, allSegments[s + 1].direction);
            ClearCoinsNearObstacles(seg);
        }
    }

    bool SegmentDrawn(Segment seg)
    {
        bool shaded = shadingRef != null && shadingRef.isActiveAndEnabled;
        if (!shaded) return allSegments.IndexOf(seg) <= currentPlayerSegmentIdx + 2;

        foreach (GameObject cube in seg.cubes)
            if (cube != null && shadingRef.ShownAmount(cube) > 0f) return true;
        return false;
    }

    // ------------------------------------------------------------------
    // Lifetime
    // ------------------------------------------------------------------

    /// <summary>Drops every coin, for a fresh path.</summary>
    void ResetCoins()
    {
        for (int i = coins.Count - 1; i >= 0; i--) RemoveCoinAt(i);
        for (int i = coinBursts.Count - 1; i >= 0; i--) ReturnCoinView(coinBursts[i].view);
        coinBursts.Clear();
        coinHaveRunnerPos = false;

        if (spawnCoins) CoinSystemReady();
    }

    /// <summary>Coins go with the leg they sit on, collected or not.</summary>
    void ReleaseCoinsOn(Segment seg)
    {
        for (int i = coins.Count - 1; i >= 0; i--)
            if (coins[i].segment == seg) RemoveCoinAt(i);
    }

    void RemoveCoinAt(int i)
    {
        PathCoin c = coins[i];
        HideCoin(c);
        c.segment = null;
        c.cube = null;
        spareCoins.Push(c);

        int last = coins.Count - 1;
        coins[i] = coins[last];
        coins.RemoveAt(last);
    }

    void DestroyCoinRuntime()
    {
        if (coinRuntimeMaterials == null) return;
        foreach (Material m in coinRuntimeMaterials)
            if (m != null) Destroy(m);
        coinRuntimeMaterials = null;
    }

    /// <summary>Sets up the mesh, materials, pool and the runner's counter on first use.</summary>
    bool CoinSystemReady()
    {
        coinRng ??= new System.Random();

        if (player != null && coinCollector == null)
        {
            coinCollector = player.GetComponent<CoinCollector>();
            if (coinCollector == null) coinCollector = player.gameObject.AddComponent<CoinCollector>();
        }

        if (coinMesh != null) return true;
        if (coinPrefab == null) return false;

        MeshFilter mf = coinPrefab.GetComponentInChildren<MeshFilter>(true);
        Renderer source = mf != null ? mf.GetComponent<Renderer>() : null;
        if (mf == null || mf.sharedMesh == null || source == null) return false;

        // A coin is a flat disc, so its thinnest axis is the one it faces along. That axis is turned
        // to point down the leg; the model's own root rotation is only the FBX axis conversion.
        coinMesh = mf.sharedMesh;
        Bounds b = coinMesh.bounds;
        Vector3 s = b.size;
        Vector3 axis = (s.x <= s.y && s.x <= s.z) ? Vector3.right : (s.y <= s.z ? Vector3.up : Vector3.forward);
        coinMeshTurn = Quaternion.FromToRotation(axis, Vector3.forward);
        coinMeshWidth = Mathf.Max(0.0001f, Mathf.Max(s.x, Mathf.Max(s.y, s.z)));
        coinMeshCentre = b.center;

        // Copies, so the glow slider never edits an asset. Shared by every coin: one SRP batch.
        Material[] src = (coinMaterials != null && coinMaterials.Length > 0) ? coinMaterials : source.sharedMaterials;
        coinRuntimeMaterials = new Material[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            if (src[i] == null) continue;
            var m = new Material(src[i]) { name = src[i].name + " (Coin)" };
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            coinRuntimeMaterials[i] = m;
        }
        appliedCoinGlow = -1f;
        appliedCoinShadows = coinsCastShadows;
        ApplyCoinLook();

        coinRoot = new GameObject("Coins").transform;
        coinRoot.SetParent(transform, false);

        Shader blobShader = Shader.Find("Custom/CoinBlob");
        if (blobShader != null)
        {
            blobMaterial = new Material(blobShader) { name = "CoinBlob (runtime)" };
            blobMesh = new Mesh { name = "CoinBlobQuad" };
            blobMesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) });
            blobMesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            blobMesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            blobMesh.RecalculateBounds();
        }

        for (int i = 0; i < CoinPoolWarm; i++) coinViewPool.Push(NewCoinView());
        return true;
    }

    MeshRenderer NewCoinView()
    {
        var go = new GameObject("Coin") { layer = coinPrefab.layer };
        go.transform.SetParent(coinRoot, false);
        go.AddComponent<MeshFilter>().sharedMesh = coinMesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = coinRuntimeMaterials;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.enabled = false;
        allCoinViews.Add(r);
        return r;
    }

    MeshRenderer TakeCoinView()
    {
        MeshRenderer r = coinViewPool.Count > 0 ? coinViewPool.Pop() : NewCoinView();
        r.enabled = true;
        return r;
    }

    void ReturnCoinView(MeshRenderer r)
    {
        if (r == null) return;
        r.enabled = false;
        coinViewPool.Push(r);
    }

    MeshRenderer TakeBlob()
    {
        if (blobPool.Count > 0) { var p = blobPool.Pop(); p.enabled = true; return p; }
        var go = new GameObject("CoinBlob") { layer = coinPrefab.layer };
        go.transform.SetParent(coinRoot, false);
        go.AddComponent<MeshFilter>().sharedMesh = blobMesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = blobMaterial;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return r;
    }

    void ReturnBlob(PathCoin c)
    {
        if (c.blob == null) return;
        c.blob.enabled = false;
        blobPool.Push(c.blob);
        c.blob = null;
    }

    void HideCoin(PathCoin c)
    {
        ReturnBlob(c);
        if (c.view == null) return;
        ReturnCoinView(c.view);
        c.view = null;
        c.viewScale = -1f;
    }

    // coinsMatchTrackFade: a coin as see-through as the track under it - the fade copies of the coin materials (made on
    // first use, like PathDepthShading's) with the alpha and glow in a property block; back to the shared opaque ones
    // (one SRP batch) once it is near.
    private Material[] coinFadeMaterials;
    private MaterialPropertyBlock coinFadeBlock;

    void ApplyCoinFade(MeshRenderer r, float alpha)
    {
        if (r == null || coinRuntimeMaterials == null) return;
        bool fading = alpha < 0.995f;
        if (!fading)
        {
            if (r.sharedMaterials.Length > 0 && r.sharedMaterials[0] != coinRuntimeMaterials[0])
            {
                r.sharedMaterials = coinRuntimeMaterials;
                r.SetPropertyBlock(null);
                for (int i = 0; i < coinRuntimeMaterials.Length; i++) r.SetPropertyBlock(null, i);
            }
            return;
        }
        if (coinFadeMaterials == null)
        {
            coinFadeMaterials = new Material[coinRuntimeMaterials.Length];
            for (int i = 0; i < coinRuntimeMaterials.Length; i++)
            {
                if (coinRuntimeMaterials[i] == null) continue;
                coinFadeMaterials[i] = new Material(coinRuntimeMaterials[i]) { name = coinRuntimeMaterials[i].name + " (Fade)" };
                PathDepthShading.ConfigureAsFade(coinFadeMaterials[i]);
            }
            coinFadeBlock = new MaterialPropertyBlock();
        }
        if (r.sharedMaterials.Length > 0 && r.sharedMaterials[0] != coinFadeMaterials[0]) r.sharedMaterials = coinFadeMaterials;
        for (int i = 0; i < coinFadeMaterials.Length; i++)
        {
            Material m = coinFadeMaterials[i];
            if (m == null) continue;
            coinFadeBlock.Clear();
            Color b = m.HasProperty(CoinBaseColorId) ? m.GetColor(CoinBaseColorId) : Color.white;
            coinFadeBlock.SetColor(CoinBaseColorId, new Color(b.r, b.g, b.b, b.a * alpha));
            if (m.HasProperty(CoinEmissionId)) coinFadeBlock.SetColor(CoinEmissionId, new Color(b.r, b.g, b.b, 1f) * (coinGlow * alpha));
            r.SetPropertyBlock(coinFadeBlock, i);
        }
    }

    void ApplyCoinLook()
    {
        if (coinRuntimeMaterials != null && !Mathf.Approximately(appliedCoinGlow, coinGlow))
        {
            appliedCoinGlow = coinGlow;
            foreach (Material m in coinRuntimeMaterials)
            {
                if (m == null || !m.HasProperty(CoinEmissionId)) continue;
                Color c = m.HasProperty(CoinBaseColorId) ? m.GetColor(CoinBaseColorId) : Color.white;
                m.SetColor(CoinEmissionId, new Color(c.r, c.g, c.b, 1f) * coinGlow);
            }
        }

        if (appliedCoinShadows != coinsCastShadows)
        {
            appliedCoinShadows = coinsCastShadows;
            var mode = ShadowCastingMode.Off;
            foreach (MeshRenderer r in allCoinViews)
                if (r != null) r.shadowCastingMode = mode;
        }
    }

    // ------------------------------------------------------------------
    // Per frame: pickup, reveal, spin
    // ------------------------------------------------------------------

    void UpdateCoins()
    {
        if (coins.Count == 0 && coinBursts.Count == 0)
        {
            coinHaveRunnerPos = false;
            return;
        }

        ApplyCoinLook();

        float dt = Time.deltaTime;
        coinSpin = Mathf.Repeat(coinSpin + coinSpinSpeed * dt, 360f);

        // Swept from last frame's position, so a fast runner on a slow frame cannot skip a coin.
        bool canCollect = player != null && player.isActiveAndEnabled && !player.IsGameOver;
        Vector3 runnerPos = player != null ? player.transform.position : Vector3.zero;
        Vector3 sweepFrom = coinHaveRunnerPos && (runnerPos - coinLastRunnerPos).sqrMagnitude < 9f
            ? coinLastRunnerPos
            : runnerPos;
        float pickup2 = coinPickupRadius * coinPickupRadius;

        if (shadingRef == null) shadingRef = GetComponent<PathDepthShading>();
        bool shaded = shadingRef != null && shadingRef.isActiveAndEnabled;
        float parentScale = coinRoot != null ? Mathf.Max(0.0001f, coinRoot.lossyScale.x) : 1f;
        float fullScale = coinSize / coinMeshWidth / parentScale;

        for (int i = coins.Count - 1; i >= 0; i--)
        {
            PathCoin c = coins[i];
            if (c.cube == null) { RemoveCoinAt(i); continue; }

            Vector3 pos = CoinPosition(c);

            if (canCollect && SqrDistanceToSegment(pos, sweepFrom, runnerPos) <= pickup2)
            {
                CollectCoinAt(i, pos);
                continue;
            }

            // Drawn exactly as much as the cube it sits on: it grows in with the track ahead and
            // goes with it behind. The shading's value is from the frame before - it runs later.
            float shown = shaded ? shadingRef.ShownAmount(c.cube) : (c.cube.activeInHierarchy ? 1f : 0f);
            // A missed coin fades out at the rate its leg is left behind by the runner.
            if (shaded) shown *= 1f - Mathf.SmoothStep(0f, 1f, shadingRef.CrossedAmount(c.cube));
            if (shown <= 0.01f)
            {
                HideCoin(c);
                continue;
            }

            if (c.view == null) c.view = TakeCoinView();

            if (coinsCastShadows && blobMesh != null)
            {
                if (c.blob == null) c.blob = TakeBlob();
                blobMaterial.color = new Color(0f, 0f, 0f, coinBlobStrength);
                float bw = coinSize * c.sizeScale * coinBlobSize * Mathf.SmoothStep(0f, 1f, shown) * coinFade;
                Transform bt = c.blob.transform;
                bt.SetPositionAndRotation(c.cubeCentre + c.outward * (cubeSize * 0.5f + 0.012f), c.blobRot);
                bt.localScale = Vector3.one * (bw / parentScale);
            }
            else if (c.blob != null) ReturnBlob(c);

            float scale = fullScale * c.sizeScale * Mathf.SmoothStep(0f, 1f, shown) * coinFade;
            if (coinsMatchTrackFade && shaded)
            {
                scale *= shadingRef.TrackSize(c.cube);
                ApplyCoinFade(c.view, shadingRef.TrackOpacity(c.cube));
            }
            Quaternion rot = c.stand * Quaternion.AngleAxis(coinSpin + c.indexInRun * coinSpinWave, Vector3.up) * coinMeshTurn;
            Transform t = c.view.transform;
            t.SetPositionAndRotation(pos - rot * (coinMeshCentre * (scale * parentScale)), rot);
            if (!Mathf.Approximately(c.viewScale, scale))
            {
                c.viewScale = scale;
                t.localScale = new Vector3(scale, scale, scale);
            }
        }

        UpdateCoinBursts(dt);

        coinLastRunnerPos = runnerPos;
        coinHaveRunnerPos = true;
    }

    void CollectCoinAt(int i, Vector3 pos)
    {
        PathCoin c = coins[i];

        // The coin's own renderer plays the pickup, so it is not snatched off screen.
        if (c.view != null)
        {
            CoinBurst b = spareBursts.Count > 0 ? spareBursts.Pop() : new CoinBurst();
            b.view = c.view;
            b.from = pos;
            b.up = c.outward;
            b.rotation = c.view.transform.rotation;
            b.scale = c.viewScale;
            b.t = 0f;
            coinBursts.Add(b);
            c.view = null;
        }

        RemoveCoinAt(i);
        if (coinCollector != null) coinCollector.Collect(pos);
    }

    // A quick swell and hop off the face while spinning fast, then gone.
    void UpdateCoinBursts(float dt)
    {
        for (int i = coinBursts.Count - 1; i >= 0; i--)
        {
            CoinBurst b = coinBursts[i];
            b.t += dt / CoinBurstSeconds;

            if (b.t >= 1f || b.view == null)
            {
                ReturnCoinView(b.view);
                b.view = null;
                spareBursts.Push(b);
                int last = coinBursts.Count - 1;
                coinBursts[i] = coinBursts[last];
                coinBursts.RemoveAt(last);
                continue;
            }

            float k = b.t;
            float grow = k < 0.3f
                ? Mathf.Lerp(1f, 1.5f, Mathf.SmoothStep(0f, 1f, k / 0.3f))
                : Mathf.Lerp(1.5f, 0f, Mathf.SmoothStep(0f, 1f, (k - 0.3f) / 0.7f));
            float hop = 1f - (1f - k) * (1f - k);

            Transform t = b.view.transform;
            t.SetPositionAndRotation(
                b.from + b.up * (cubeSize * 0.9f * hop),
                Quaternion.AngleAxis(900f * k, b.up) * b.rotation);
            float s = b.scale * grow;
            t.localScale = new Vector3(s, s, s);
        }
    }

    static float SqrDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
        return (a + ab * t - p).sqrMagnitude;
    }
}
