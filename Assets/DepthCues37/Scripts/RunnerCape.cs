using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// MainScene prop camera 3.7 only: a short cape on the runner block (child of PlayerTotem, so it rides the visual roll).
/// A small Verlet cloth - columns x rows particles - pinned along the block's top-back edge (its "shoulders"), simulated
/// in world space with fixed sub-steps:
///   - air drag against the particles' own world velocity, so at running speed it streams out behind like a cape
///   - "gravity" toward the rail the runner stands on (the block's down), so it always drapes the same way relative to
///     the block - another hint of which way is up for the player
///   - a little flutter that grows with speed
///   - kept out of the block and above the rail face; snaps back to its rest shape on a teleport (restart / checkpoint)
/// Drawn two-sided with DepthCues37/RunnerCape (outside / lining). Purely visual: no colliders, nothing else reads it.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(970)]   // after RunnerVisualRig has posed the block this frame
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RunnerCape : MonoBehaviour
{
    [Header("Shape (block units)")]
    [Range(3, 9)] public int columns = 5;
    [Range(3, 10)] public int rows = 6;
    [Tooltip("Width at the shoulders.")]
    [Range(0.3f, 1.2f)] public float width = 0.74f;
    [Tooltip("Length from shoulders to hem. Kept short on purpose.")]
    [Range(0.3f, 1.5f)] public float length = 0.8f;
    [Tooltip("The hem is this much wider than the shoulders (a cape, not a scarf).")]
    [Range(1f, 1.6f)] public float flare = 1.18f;
    [Tooltip("Where the shoulders sit in the block's local space: y = head height, z = behind the back face.")]
    public Vector2 shoulderYZ = new Vector2(0.44f, -0.53f);

    [Header("Motion")]
    [Tooltip("Pull toward the rail the block stands on (units/s²).")]
    public float gravity = 9f;
    [Tooltip("Air drag on each particle's world velocity. Higher = streams out flatter behind at speed.")]
    [Range(0f, 8f)] public float drag = 2.4f;
    [Tooltip("Velocity kept per sub-step (1 = no damping).")]
    [Range(0.9f, 1f)] public float damping = 0.985f;
    [Tooltip("Flutter strength at full speed.")]
    [Range(0f, 4f)] public float flutter = 1.4f;
    [Range(0.5f, 12f)] public float flutterFrequency = 6f;
    [Tooltip("Runner speed of 'full speed' for the flutter.")]
    public float fastSpeed = 28f;
    [Range(1, 12)] public int iterations = 6;
    [Tooltip("Sub-steps per second.")]
    [Range(30f, 240f)] public float stepRate = 120f;
    [Tooltip("Stiffness of the across / diagonal / bend links (the lengthwise ones are always stiff).")]
    [Range(0f, 1f)] public float shear = 0.8f;
    [Range(0f, 1f)] public float bend = 0.25f;

    [Header("Collision")]
    [Tooltip("Kept this far outside the block.")]
    [Range(0f, 0.1f)] public float margin = 0.035f;
    [Tooltip("Kept this far above the rail face the runner stands on.")]
    [Range(0f, 0.1f)] public float railMargin = 0.03f;
    [Tooltip("An anchor jump bigger than this in one frame (restart, checkpoint start) resets the cape.")]
    public float teleportDistance = 2.5f;

    [Tooltip("Renderer whose visibility the cape follows (the block). Empty = always visible.")]
    public Renderer followVisibility;

    struct Link { public int a, b; public float rest, stiff; }

    Transform block;            // the block the cape hangs from (this object's parent)
    CubeRailRunner runner;
    Mesh mesh;
    MeshRenderer mr;
    Vector3[] pos, prev, verts;
    Vector3[] anchorNow, anchorLast;
    Link[] links;
    float accumulator, time;
    bool ready;

    void Awake()
    {
        block = transform.parent;
        runner = GetComponentInParent<CubeRailRunner>();
        mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        Build();
    }

    void OnEnable() { ready = false; }

    void OnDestroy() { if (mesh != null) Destroy(mesh); }

    int Index(int c, int r) => r * columns + c;

    // The shoulder points (row 0) in the block's local space.
    Vector3 ShoulderLocal(int c)
    {
        float u = columns > 1 ? c / (float)(columns - 1) - 0.5f : 0f;
        return new Vector3(u * width, shoulderYZ.x, shoulderYZ.y);
    }

    void Build()
    {
        int n = columns * rows;
        pos = new Vector3[n];
        prev = new Vector3[n];
        verts = new Vector3[n];
        anchorNow = new Vector3[columns];
        anchorLast = new Vector3[columns];

        var uv = new Vector2[n];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                uv[Index(c, r)] = new Vector2(columns > 1 ? c / (float)(columns - 1) : 0f, rows > 1 ? r / (float)(rows - 1) : 0f);
        var tris = new int[(columns - 1) * (rows - 1) * 6];
        int t = 0;
        for (int r = 0; r < rows - 1; r++)
            for (int c = 0; c < columns - 1; c++)
            {
                int a = Index(c, r), b = Index(c + 1, r), d = Index(c, r + 1), e = Index(c + 1, r + 1);
                tris[t++] = a; tris[t++] = b; tris[t++] = d;
                tris[t++] = b; tris[t++] = e; tris[t++] = d;
            }
        mesh = new Mesh { name = "RunnerCape" };
        mesh.MarkDynamic();
        mesh.vertices = verts;
        mesh.uv = uv;
        mesh.triangles = tris;
        GetComponent<MeshFilter>().sharedMesh = mesh;

        // Rest lengths come from the rest shape in block space (scale 1), flared toward the hem.
        Vector3[] rest = new Vector3[n];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                rest[Index(c, r)] = RestLocal(c, r);
        var list = new System.Collections.Generic.List<Link>();
        void Add(int a, int b, float k) { list.Add(new Link { a = a, b = b, rest = Vector3.Distance(rest[a], rest[b]), stiff = k }); }
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                if (r + 1 < rows) Add(Index(c, r), Index(c, r + 1), 1f);                       // lengthwise
                if (c + 1 < columns) Add(Index(c, r), Index(c + 1, r), shear);                 // across
                if (r + 1 < rows && c + 1 < columns)
                {
                    Add(Index(c, r), Index(c + 1, r + 1), shear * 0.7f);                        // shear
                    Add(Index(c + 1, r), Index(c, r + 1), shear * 0.7f);
                }
                if (r + 2 < rows) Add(Index(c, r), Index(c, r + 2), bend);                     // bend
                if (c + 2 < columns) Add(Index(c, r), Index(c + 2, r), bend * 0.6f);
            }
        links = list.ToArray();
    }

    // Rest shape: from the shoulders back and down over the block's back, flaring out.
    Vector3 RestLocal(int c, int r)
    {
        float v = rows > 1 ? r / (float)(rows - 1) : 0f;
        Vector3 s = ShoulderLocal(c);
        s.x *= Mathf.Lerp(1f, flare, v);
        float l = v * length;
        return s + new Vector3(0f, -l * 0.55f, -l * 0.84f);
    }

    void ResetCloth()
    {
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                Vector3 w = block.TransformPoint(RestLocal(c, r));
                pos[Index(c, r)] = prev[Index(c, r)] = w;
            }
        for (int c = 0; c < columns; c++) anchorLast[c] = anchorNow[c] = block.TransformPoint(ShoulderLocal(c));
        accumulator = 0f;
        ready = true;
    }

    void LateUpdate()
    {
        if (block == null) return;
        bool visible = followVisibility == null || (followVisibility.enabled && !followVisibility.forceRenderingOff && followVisibility.gameObject.activeInHierarchy);
        if (mr.enabled != visible) mr.enabled = visible;

        for (int c = 0; c < columns; c++) anchorNow[c] = block.TransformPoint(ShoulderLocal(c));
        if (!ready || (anchorNow[columns / 2] - anchorLast[columns / 2]).sqrMagnitude > teleportDistance * teleportDistance)
            ResetCloth();

        float dt = Mathf.Min(Time.deltaTime, 1f / 20f);
        if (dt > 0f)
        {
            float h = 1f / stepRate;
            accumulator += dt;
            int steps = Mathf.Min(8, Mathf.FloorToInt(accumulator / h));
            accumulator -= steps * h;
            if (accumulator > h) accumulator = 0f;
            for (int s = 0; s < steps; s++)
            {
                // Anchors move smoothly across the frame's sub-steps.
                float f = (s + 1f) / steps;
                Step(h, f);
            }
            for (int c = 0; c < columns; c++) anchorLast[c] = anchorNow[c];
        }
        // The block moves every frame even when no sub-step ran: keep the cloth behind it and above the rail now.
        for (int c = 0; c < columns; c++) pos[c] = prev[c] = anchorNow[c];
        Collide(runner != null ? runner.transform : block);

        // Mesh in this object's space.
        Transform me = transform;
        for (int i = 0; i < pos.Length; i++) verts[i] = me.InverseTransformPoint(pos[i]);
        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    void Step(float h, float f)
    {
        time += h;
        Transform root = runner != null ? runner.transform : block;
        Vector3 down = -root.up;
        float speed01 = runner != null ? Mathf.Clamp01(runner.forwardSpeed / Mathf.Max(1f, fastSpeed)) : 0f;
        Vector3 across = block.right;

        // Integrate (row 0 is pinned).
        for (int r = 1; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                int i = Index(c, r);
                Vector3 vel = (pos[i] - prev[i]) / h;
                float v = r / (float)(rows - 1);
                Vector3 acc = down * gravity - vel * drag;
                // Flutter: a travelling wave across the cape, stronger toward the hem and with speed.
                float wave = Mathf.Sin(time * flutterFrequency * 6.283f - r * 1.3f + c * 0.7f);
                acc += (root.up * 0.7f + across * 0.3f) * (wave * flutter * speed01 * v * 9f);
                Vector3 p = pos[i];
                pos[i] = p + (p - prev[i]) * damping + acc * (h * h);
                prev[i] = p;
            }
        for (int c = 0; c < columns; c++)
        {
            Vector3 a = Vector3.Lerp(anchorLast[c], anchorNow[c], f);
            pos[Index(c, 0)] = prev[Index(c, 0)] = a;
        }

        // Links.
        for (int it = 0; it < iterations; it++)
        {
            for (int k = 0; k < links.Length; k++)
            {
                Link l = links[k];
                Vector3 d = pos[l.b] - pos[l.a];
                float len = d.magnitude;
                if (len < 1e-6f) continue;
                Vector3 corr = d * ((len - l.rest) / len * l.stiff);
                bool pinA = l.a < columns, pinB = l.b < columns;
                if (pinA && pinB) continue;
                if (pinA) pos[l.b] -= corr;
                else if (pinB) pos[l.a] += corr;
                else { pos[l.a] += corr * 0.5f; pos[l.b] -= corr * 0.5f; }
            }
            Collide(root);
        }
    }

    // Outside the block's box, above the rail face.
    void Collide(Transform root)
    {
        float half = 0.5f + margin;
        Vector3 railPoint = root.position - root.up * (0.5f * Mathf.Max(0.01f, root.lossyScale.y) - railMargin);
        Vector3 up = root.up;
        for (int i = columns; i < pos.Length; i++)
        {
            Vector3 lp = block.InverseTransformPoint(pos[i]);
            // A cape hangs behind the shoulders: never over the head onto the top face or round the front (the eye).
            if (lp.z > shoulderYZ.y + 0.02f) { lp.z = shoulderYZ.y + 0.02f; pos[i] = block.TransformPoint(lp); }
            if (Mathf.Abs(lp.x) < half && Mathf.Abs(lp.y) < half && Mathf.Abs(lp.z) < half)
            {
                // Out through the nearest face (never through the front, the eye).
                float dx = half - Mathf.Abs(lp.x), dy = half - Mathf.Abs(lp.y), dz = half - Mathf.Abs(lp.z);
                if (dz <= dx && dz <= dy && lp.z < 0f) lp.z = -half;
                else if (dy <= dx) lp.y = Mathf.Sign(lp.y) * half;
                else lp.x = Mathf.Sign(lp.x) * half;
                pos[i] = block.TransformPoint(lp);
            }
            float above = Vector3.Dot(pos[i] - railPoint, up);
            if (above < 0f) pos[i] -= up * above;
        }
    }
}
