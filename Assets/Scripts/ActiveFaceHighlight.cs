using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shows which face of the path the runner is on: a soft lit lane on that face, from just behind the
/// runner to a few cubes ahead (never past the end of the leg). On a side switch the lane snaps to the
/// new face, flashes, and a bright band sweeps down it, so the move reads at a glance.
/// Purely visual: it only reads CubeRailRunner / DynamicPathGenerator and draws its own strip.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CubeRailRunner))]
public class ActiveFaceHighlight : MonoBehaviour
{
    [Tooltip("Material using Runner/FaceLane. Empty = one is made at runtime.")]
    public Material overlayMaterial;
    [ColorUsage(true, true)] public Color faceColor = new Color(0.25f, 0.8f, 1f, 0.5f);
    [ColorUsage(true, true)] public Color switchColor = new Color(0.8f, 1f, 1f, 0.95f);
    [Tooltip("Lane width as a share of the face width.")]
    [Range(0.3f, 1f)] public float laneWidth = 0.9f;
    [Tooltip("Cubes of lane ahead of the runner.")]
    [Range(1f, 12f)] public float laneAhead = 5f;
    [Tooltip("Cubes of lane behind the runner.")]
    [Range(0f, 3f)] public float laneBehind = 0.6f;
    [Tooltip("How far above the face the lane floats (world units).")]
    public float lift = 0.012f;
    [Tooltip("Seconds the switch flash lasts.")]
    [Range(0.1f, 1.5f)] public float switchSeconds = 0.55f;
    [Tooltip("Cubes per second the switch band runs down the lane.")]
    [Range(2f, 40f)] public float sweepSpeed = 16f;
    [Range(0f, 1f)] public float pulseAmount = 0.2f;
    [Tooltip("Cubes the lane stops short of the leg's far edge. 0 = the edge (default). A DifficultyProfile sets it at run time so the lane ends where the corner is actually decided.")]
    public float endInsetCubes = 0f;

    float extraFlash;      // CloseCallFX: a flash brighter than the switch flash
    /// <summary>Flashes the lane (1 = full white-hot); fades over about 0.4 s.</summary>
    public void Flash(float strength) { extraFlash = Mathf.Max(extraFlash, strength); switchT = 0f; }

    static readonly int ZTestId = Shader.PropertyToID("_ZTest");
    static readonly int OpacityId = Shader.PropertyToID("_Opacity");

    // kept so older scene data still loads; the turn hint now lives in TurnFaceArrows
    [HideInInspector] public bool turnHint;

    const int Rows = 16;
    static readonly float[] Columns = { -0.5f, -0.36f, 0.36f, 0.5f };
    static readonly float[] ColumnAlpha = { 0f, 1f, 1f, 0f };

    private CubeRailRunner runner;
    private DynamicPathGenerator path;
    private Transform lane;
    private Mesh mesh;
    private Material mat;
    private Color[] colors;
    private Vector3[] verts;
    private Transform lastCube;
    private int lastSide = -1;
    private float fade;
    private float switchT = 1f;   // 0 at a switch, 1 when the flash is over

    void Awake()
    {
        runner = GetComponent<CubeRailRunner>();
        path = FindFirstObjectByType<DynamicPathGenerator>();
    }

    void OnDisable() { if (lane != null) lane.gameObject.SetActive(false); }

    void OnDestroy()
    {
        if (lane != null) Destroy(lane.gameObject);
        if (mat != null) Destroy(mat);
    }

    void LateUpdate()
    {
        if (lane == null) Build();
        Transform cube = runner.currentCube;
        bool show = cube != null && !runner.isFlying && !runner.IsGameOver;
        fade = Mathf.MoveTowards(fade, show ? 1f : 0f, Time.deltaTime * 6f);
        lane.gameObject.SetActive(fade > 0.01f && cube != null);
        if (cube == null || fade <= 0.01f) return;

        int side = runner.currentSideIndex;
        if (cube == lastCube && side != lastSide && lastSide >= 0) switchT = 0f;
        lastCube = cube;
        lastSide = side;
        switchT = Mathf.MoveTowards(switchT, 1f, Time.deltaTime / switchSeconds);
        extraFlash = Mathf.MoveTowards(extraFlash, 0f, Time.deltaTime * 2.5f);

        PlaceLane(cube, side, out float ahead, out float behind);
        Paint(ahead, behind);
    }

    void PlaceLane(Transform cube, int side, out float ahead, out float behind)
    {
        float cs = path != null ? path.cubeSize : 1f;
        Vector3 localN = side switch { 0 => Vector3.up, 1 => Vector3.right, 2 => Vector3.down, _ => Vector3.left };
        Vector3 n = cube.TransformDirection(localN);
        Vector3 fwd = cube.forward;

        // PathDepthShading draws a thinner child copy; measure the face from whatever renderer is on.
        Bounds b = new Bounds(cube.position, Vector3.one * cs);
        foreach (var r in cube.GetComponentsInChildren<Renderer>())
            if (r.enabled) { b = r.bounds; break; }
        Vector3 across = Vector3.Cross(n, fwd);
        float surface = Vector3.Dot(b.extents, Abs(n));
        float width = 2f * Vector3.Dot(b.extents, Abs(across)) * laneWidth;

        // Along the leg: start at the runner, stop at the end of the leg.
        Vector3 runnerOnAxis = b.center + fwd * Vector3.Dot(transform.position - b.center, fwd);
        ahead = laneAhead * cs;
        var seg = path != null ? path.PlayerSegment : null;
        if (seg != null)
        {
            float toEnd = Vector3.Dot(seg.endPosition - runnerOnAxis, fwd) + Vector3.Dot(b.extents, Abs(fwd)) - endInsetCubes * cs;
            ahead = Mathf.Clamp(toEnd, 0.05f, ahead);
        }
        behind = laneBehind * cs;

        lane.SetPositionAndRotation(runnerOnAxis + n * (surface + lift), Quaternion.LookRotation(n, fwd));
        lane.localScale = new Vector3(width, 1f, 1f);

        // A face pointing away from the camera stays unlit - never drawn through the cube.
        Camera cam = Camera.main;
        Vector3 toCam = cam == null ? n : cam.orthographic ? -cam.transform.forward : (cam.transform.position - lane.position).normalized;
        bool hidden = Vector3.Dot(n, toCam) < 0.02f;
        mat.SetFloat(ZTestId, (float)CompareFunction.LessEqual);
        mat.SetFloat(OpacityId, hidden ? 0f : 1f);
    }

    void Paint(float ahead, float behind)
    {
        float total = ahead + behind;
        float pulse = 1f - pulseAmount + pulseAmount * (0.5f + 0.5f * Mathf.Sin(Time.time * 4f));
        float flash = 1f - switchT;                         // 1 right at the switch, fading out
        float sweepAt = switchT * switchSeconds * sweepSpeed; // cubes the band has run from the runner
        float cs = path != null ? path.cubeSize : 1f;

        for (int row = 0; row < Rows; row++)
        {
            float v = row / (float)(Rows - 1);
            float y = -behind + v * total;
            // soft behind the runner, brightest at it, fading toward the far end
            float along = y < 0f ? Mathf.Clamp01(1f + y / Mathf.Max(0.01f, behind)) : Mathf.Pow(1f - y / Mathf.Max(0.01f, ahead), 1.3f);
            float band = Mathf.Exp(-Mathf.Pow((y / cs - sweepAt) / 0.8f, 2f));
            Color c = faceColor;
            c.a *= along * pulse;
            if (flash > 0f)
            {
                float k = flash * (0.45f * along + band);
                c = Color.Lerp(c, switchColor, Mathf.Clamp01(k));
                c.a = Mathf.Max(c.a, switchColor.a * Mathf.Clamp01(k));
            }
            if (extraFlash > 0f)
            {
                c = Color.Lerp(c, Color.white, Mathf.Clamp01(extraFlash * (0.6f + 0.4f * along)));
                c.a = Mathf.Max(c.a, Mathf.Clamp01(extraFlash));
            }
            c.a *= fade;
            for (int col = 0; col < Columns.Length; col++)
            {
                int i = row * Columns.Length + col;
                verts[i] = new Vector3(Columns[col], y, 0f);
                colors[i] = new Color(c.r, c.g, c.b, c.a * ColumnAlpha[col]);
            }
        }
        mesh.vertices = verts;
        mesh.colors = colors;
        mesh.RecalculateBounds();
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    void Build()
    {
        mat = overlayMaterial != null ? new Material(overlayMaterial) : new Material(Shader.Find("Runner/FaceLane"));
        int cols = Columns.Length;
        verts = new Vector3[Rows * cols];
        colors = new Color[Rows * cols];
        var tris = new int[(Rows - 1) * (cols - 1) * 6];
        int t = 0;
        for (int row = 0; row < Rows - 1; row++)
            for (int col = 0; col < cols - 1; col++)
            {
                int a = row * cols + col, b = a + 1, c = a + cols, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
        mesh = new Mesh { name = "ActiveFaceLane" };
        mesh.MarkDynamic();
        mesh.vertices = verts;
        mesh.colors = colors;
        mesh.triangles = tris;

        var go = new GameObject("ActiveFaceLane");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        lane = go.transform;
    }
}
