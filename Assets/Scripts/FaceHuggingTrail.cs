using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws the runner's trail painted flat on the face of the path it rides, instead of the TrailRenderer's
/// camera-facing ribbon through the runner's middle (which crosses the leg and looks the same on every face).
///  - the ribbon lies on the face, so its shape on screen says which face it is (wide band on top, thin on a side)
///  - on a roll it wraps round the edge the runner went over, and that fold glows, so where you turned stays visible
///  - on a face turned away from the camera (where the leg hides the flat stripe) a fin of light stands straight out
///    of the face, as tall as the runner, so it shows past the leg's outline the way the runner does
///  - everything is depth tested, so nothing is drawn through a cube
/// The TrailRenderer keeps running underneath (PathDepthShading still sets its length, RunnerTrailFX still sets its
/// colours); this only stops it drawing and copies its length, width and colours. Turning this off puts it back.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]   // after RunnerTrailFX has set this frame's colours on the trail
[RequireComponent(typeof(CubeRailRunner))]
public class FaceHuggingTrail : MonoBehaviour
{
    [Tooltip("Runner/TrailFX Face Ribbon.")]
    public Material material;
    [Tooltip("Ribbon width as a share of the face (1 = the whole face). Under 1 keeps the face's edges visible.")]
    [Range(0.2f, 1f)] public float faceWidth = 0.75f;
    [Tooltip("How far off the face the ribbon floats (world units).")]
    [Range(0.002f, 0.1f)] public float lift = 0.02f;
    [Tooltip("How round the fold over an edge is. Higher = tighter to the edge.")]
    [Range(4f, 24f)] public float edgeSharpness = 12f;
    [Tooltip("Extra brightness where the ribbon folds over an edge (where the runner rolled).")]
    [Range(0f, 4f)] public float edgeGlow = 1.6f;
    [Tooltip("How far the fin stands out of the face, in cubes. 0 = no fin. The runner itself stands about 1.")]
    [Range(0f, 1.5f)] public float finHeight = 1f;
    [Tooltip("Fin brightness relative to the flat stripe.")]
    [Range(0f, 1.5f)] public float finStrength = 0.9f;
    [Tooltip("The fin only grows on faces turned away from the camera: none while the face's normal points at least this much toward the camera (dot with the view direction)...")]
    [Range(-1f, 1f)] public float finHiddenFrom = -0.15f;
    [Tooltip("...and full height once it points this much away.")]
    [Range(-1f, 1f)] public float finHiddenFull = 0.35f;
    [Tooltip("New point every this many units travelled.")]
    [Range(0.02f, 0.5f)] public float pointSpacing = 0.08f;

    [Header("Corners (off by default = as before; on in MainScene prop camera 3.7)")]
    [Tooltip("Turns the ribbon round a corner on a smooth bend half a ribbon wide, instead of twisting it at the turn point.")]
    public bool smoothCorners = false;
    [Tooltip("Extra brightness where the trail turned a corner on the same face (so the turn you made stays readable). 0 = none.")]
    [Range(0f, 2f)] public float cornerGlow = 0f;

    /// <summary>MainScene prop camera 3.7 DepthCueController: softens the ribbon toward a lighter tint. 0 / 1 = untouched.</summary>
    [System.NonSerialized] public Color tint = Color.white;
    [System.NonSerialized] public float tintAmount;
    [System.NonSerialized] public float intensityScale = 1f;

    struct Sample
    {
        public Vector3 pos, normal;
        public float time, dist, edge;
        public bool start;   // first point of a new stretch: not joined to the point before it
    }

    CubeRailRunner runner;
    TrailRenderer trail;
    RunnerTrailFX fx;
    MaterialPropertyBlock block;
    GameObject go;
    Mesh mesh;
    MeshRenderer mr;

    readonly List<Sample> samples = new List<Sample>();
    readonly List<Vector3> verts = new List<Vector3>();
    readonly List<Vector2> uvs = new List<Vector2>();
    readonly List<Vector2> uv2s = new List<Vector2>();
    readonly List<Color> colors = new List<Color>();
    readonly List<int> tris = new List<int>();
    float travelled;
    Vector3 lastRunnerPos;
    bool hasLast;
    int lastTrailCount;

    // Play-test counters (read by test scripts).
    [System.NonSerialized] public int statNoSurface, statBreaks, statClears, statSkippedOdd;
    [System.NonSerialized] public float statMaxHeadGap;
    public string DebugStats() => $"samples {samples.Count} noSurface {statNoSurface} breaks {statBreaks} clears {statClears} oddSkipped {statSkippedOdd} maxHeadGap {statMaxHeadGap:0.00}";

    void OnEnable()
    {
        runner = GetComponent<CubeRailRunner>();
        trail = GetComponent<TrailRenderer>();
        fx = GetComponent<RunnerTrailFX>();
        if (block == null) block = new MaterialPropertyBlock();
        if (material == null) { Debug.LogWarning("FaceHuggingTrail: assign the Runner/TrailFX Face Ribbon material.", this); enabled = false; return; }

        if (go == null)
        {
            go = new GameObject("FaceHuggingTrail");
            mesh = new Mesh { name = "FaceHuggingTrail" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        go.SetActive(true);
        if (trail != null) trail.forceRenderingOff = true;
        Clear();
    }

    void OnDisable()
    {
        if (trail != null) trail.forceRenderingOff = false;
        if (go != null) go.SetActive(false);
    }

    void OnDestroy()
    {
        if (go != null) Destroy(go);
        if (mesh != null) Destroy(mesh);
    }

    void Clear()
    {
        samples.Clear();
        hasLast = false;
        statClears++;
        if (mesh != null) mesh.Clear();
    }

    void LateUpdate()
    {
        if (trail == null) return;
        if (!trail.forceRenderingOff) trail.forceRenderingOff = true;

        // The runner cleared its trail (a restart): clear this one too.
        int trailCount = trail.positionCount;
        if (trailCount == 0 && lastTrailCount > 0) Clear();
        lastTrailCount = trailCount;

        // A jump much further than a frame of running (a teleport, a rescue snap): break the ribbon there.
        Vector3 runnerPos = transform.position;
        float maxStep = Mathf.Max(1.2f, runner.forwardSpeed * Time.deltaTime * 3f);
        bool jumped = hasLast && (runnerPos - lastRunnerPos).magnitude > maxStep;
        lastRunnerPos = runnerPos;
        hasLast = true;

        float now = Time.time;
        float life = Mathf.Max(0.05f, trail.time);
        while (samples.Count > 0 && now - samples[0].time > life) samples.RemoveAt(0);

        if (!runner.IsGameOver && trail.emitting && TrySurface(out Vector3 p, out Vector3 n, out float edge))
        {
            int last = samples.Count - 1;
            bool start = last < 0 || jumped || Vector3.Distance(samples[last].pos, p) > maxStep;
            if (start && last >= 0) statBreaks++;
            if (!start) travelled += Vector3.Distance(samples[last].pos, p);
            var s = new Sample { pos = p, normal = n, time = now, dist = travelled, edge = edge, start = start };
            // The newest point follows the runner; a new one is left behind every pointSpacing.
            if (!start && last >= 1 && !samples[last].start && Vector3.Distance(samples[last - 1].pos, p) < pointSpacing)
            {
                s.start = false;
                samples[last] = s;
            }
            else samples.Add(s);
            statMaxHeadGap = Mathf.Max(statMaxHeadGap, Vector3.Distance(p, runnerPos));
        }
        else if (!runner.IsGameOver) statNoSurface++;

        BuildMesh(now, life);

        trail.GetPropertyBlock(block);
        if (tintAmount > 0f || intensityScale != 1f) Soften();
        mr.SetPropertyBlock(block);
    }

    static readonly int[] TintIds = { Shader.PropertyToID("_HeadColor"), Shader.PropertyToID("_MidColor"), Shader.PropertyToID("_TailColor") };
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    // The colours RunnerTrailFX wrote, pulled toward the tint (linear, like the block), and the brightness scaled.
    void Soften()
    {
        Vector4 t = tint.linear;
        foreach (int id in TintIds)
        {
            Vector4 c = block.GetVector(id);
            if (c == Vector4.zero) continue;
            block.SetVector(id, Vector4.Lerp(c, new Vector4(t.x, t.y, t.z, c.w), tintAmount));
        }
        float i = block.GetFloat(IntensityId);
        if (i > 0f) block.SetFloat(IntensityId, i * intensityScale);
    }

    // The point on the path's surface under the runner (wrapping round the edge mid-roll), its normal, and
    // how much it is on an edge (0 flat face, 1 right on the edge).
    bool TrySurface(out Vector3 pos, out Vector3 normal, out float edge)
    {
        pos = normal = Vector3.zero;
        edge = 0f;
        Transform cube = runner.currentCube;
        if (cube == null || runner.isFlying) return false;

        Vector3 local = cube.InverseTransformPoint(transform.position);
        Vector2 d = new Vector2(local.x, local.y);
        if (d.sqrMagnitude < 1e-6f) return false;
        // The runner rides about 1.05 cubes out from the leg's axis (nearer mid-flip, when it cuts across).
        // Much further means it is not round this leg yet (the frame it changes leg at a corner): skip rather
        // than drop a point on a wrong face.
        float far = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y));
        if (far < 0.05f || far > 1.6f) { statSkippedOdd++; return false; }

        // A rounded square (superellipse |x|^k + |y|^k = h^k) so the ribbon bends smoothly over an edge.
        float k = edgeSharpness;
        float scale = Mathf.Max(1e-4f, cube.lossyScale.x);
        float h = 0.5f + lift / scale;
        float r = Mathf.Pow(Mathf.Pow(Mathf.Abs(d.x), k) + Mathf.Pow(Mathf.Abs(d.y), k), 1f / k);
        Vector2 q = d * (h / r);
        Vector2 g = new Vector2(Mathf.Sign(q.x) * Mathf.Pow(Mathf.Abs(q.x / h), k - 1f),
                                Mathf.Sign(q.y) * Mathf.Pow(Mathf.Abs(q.y / h), k - 1f)).normalized;

        pos = cube.TransformPoint(new Vector3(q.x, q.y, local.z));
        normal = cube.TransformDirection(new Vector3(g.x, g.y, 0f)).normalized;
        edge = 1f - Mathf.Abs(Mathf.Abs(g.x) - Mathf.Abs(g.y));
        edge = edge * edge;
        return true;
    }

    // Smooth corners: the ribbon's direction at sample i is taken across a window of +-half a ribbon width along
    // the trail (not just from the previous point), so at a 90 degree turn the band swings round a bend instead of
    // twisting at one point. corner = how sharply the trail turns here on the same face (0 straight .. 1 square turn).
    void WindowTangent(int i, float window, ref Vector3 tangent, out float corner)
    {
        corner = 0f;
        Sample s = samples[i];
        int back = i, ahead = i;
        while (back > 0 && !samples[back].start && s.dist - samples[back - 1].dist <= window) back--;
        while (ahead < samples.Count - 1 && !samples[ahead + 1].start && samples[ahead + 1].dist - s.dist <= window) ahead++;
        if (back == i && ahead == i) return;
        Vector3 inV = s.pos - samples[back].pos, outV = samples[ahead].pos - s.pos;
        if (smoothCorners)
        {
            Vector3 t = (back < i ? inV.normalized : Vector3.zero) + (ahead > i ? outV.normalized : Vector3.zero);
            if (t.sqrMagnitude > 1e-6f) tangent = t;
        }
        if (back < i && ahead > i && Vector3.Dot(samples[back].normal, s.normal) > 0.95f && Vector3.Dot(samples[ahead].normal, s.normal) > 0.95f)
        {
            Vector3 a = Vector3.ProjectOnPlane(inV, s.normal), b = Vector3.ProjectOnPlane(outV, s.normal);
            if (a.sqrMagnitude > 1e-6f && b.sqrMagnitude > 1e-6f)
                corner = Mathf.Clamp01(1f - Vector3.Dot(a.normalized, b.normalized));
        }
    }

    void BuildMesh(float now, float life)
    {
        mesh.Clear();
        int count = samples.Count;
        if (count < 2) return;

        verts.Clear(); uvs.Clear(); uv2s.Clear(); colors.Clear(); tris.Clear();

        float baseWidth = fx != null && fx.ribbonWidth > 0f ? fx.ribbonWidth : Mathf.Max(0.01f, trail.widthMultiplier);
        float pulse = trail.widthMultiplier / baseWidth;   // RunnerTrailFX's speed widening and switch pulses
        AnimationCurve curve = trail.widthCurve;

        // Newest first, so uv.x runs 0 at the runner to 1 at the tail like a TrailRenderer.
        for (int i = count - 1; i >= 0; i--)
        {
            Sample s = samples[i];
            bool hasPrev = i > 0 && !s.start;
            bool hasNext = i < count - 1 && !samples[i + 1].start;
            Vector3 tangent = hasPrev ? s.pos - samples[i - 1].pos : hasNext ? samples[i + 1].pos - s.pos : Vector3.zero;
            float corner = 0f;
            if (smoothCorners || cornerGlow > 0f) WindowTangent(i, faceWidth * 0.5f, ref tangent, out corner);
            Vector3 side = Vector3.Cross(s.normal, tangent);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(s.normal, runner.currentCube != null ? runner.currentCube.forward : transform.forward);
            side.Normalize();

            float t = Mathf.Clamp01((now - s.time) / life);
            float w = Mathf.Min(0.98f, faceWidth * pulse) * Mathf.Max(0f, curve.Evaluate(t)) * 0.5f;

            verts.Add(s.pos - side * w);
            verts.Add(s.pos + side * w);
            uvs.Add(new Vector2(t, 0f));
            uvs.Add(new Vector2(t, 1f));
            uv2s.Add(new Vector2(s.dist, 0f));
            uv2s.Add(new Vector2(s.dist, 0f));
            float b = 1f + edgeGlow * s.edge + cornerGlow * corner;
            var c = new Color(b, b, b, 1f);
            colors.Add(c);
            colors.Add(c);
        }
        // The fin: straight out of the face along its normal, on faces turned away from the camera only.
        bool fin = finHeight > 0f && finStrength > 0f;
        if (fin)
        {
            float scale = runner.currentCube != null ? Mathf.Max(0.01f, runner.currentCube.lossyScale.y) : 1f;
            Camera cam = Camera.main;
            Vector3 view = cam != null ? cam.transform.forward : Vector3.forward;
            for (int i = count - 1; i >= 0; i--)
            {
                Sample s = samples[i];
                float t = Mathf.Clamp01((now - s.time) / life);
                float hidden = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(finHiddenFrom, finHiddenFull, Vector3.Dot(s.normal, view)));
                float h = finHeight * scale * hidden * Mathf.Clamp01(pulse) * Mathf.Max(0f, curve.Evaluate(t));
                verts.Add(s.pos);
                verts.Add(s.pos + s.normal * h);
                // Brightest at the outer edge: that is the part that shows past the leg.
                uvs.Add(new Vector2(t, 0.92f));
                uvs.Add(new Vector2(t, 0.5f));
                uv2s.Add(new Vector2(s.dist, 0f));
                uv2s.Add(new Vector2(s.dist, 0f));
                float b = 1f + edgeGlow * s.edge;
                var c = new Color(b, b, b, finStrength * hidden);
                colors.Add(c);
                colors.Add(c);
            }
        }

        // Vertices are newest first: quad i joins sample (count-1-i) to the older one before it.
        for (int strip = 0; strip < (fin ? 2 : 1); strip++)
        {
            int offset = strip * count * 2;
            for (int i = 0; i < count - 1; i++)
            {
                if (samples[count - 1 - i].start) continue;
                int a = offset + i * 2;
                tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
            }
        }

        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uv2s);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }
}
