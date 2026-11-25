using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Soul cubes: a small CPU Verlet cloth for capes, scarf tails, shirt hems and floppy hat tops.
///   Sheet - a strip of columns x rows pinned along its first row (cape, scarf tail, shirt tail)
///   Tube  - a ring of columns x rows pinned round its first ring, tapering to a tip (beanie top, nightcap); an optional
///           tip transform (the pompom) rides on the end
/// Forces: gravity toward the rail the character stands on, air drag against (own velocity - wind), a gusting wind so it
/// keeps moving even when the character stands still, a flutter wave that grows with speed, and a little "shape memory"
/// pulling back toward the authored rest shape (so a hat tip keeps its flop instead of hanging like string).
/// Kept outside the body box and above the rail. Snaps to rest on a teleport (restart / checkpoint). Purely visual.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(980)]   // after RunnerVisualRig (0) and SoulCharacter (975) have posed the body
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SoulCloth : MonoBehaviour
{
    public enum Shape { Sheet, Tube }

    [Header("Where it hangs")]
    public Shape shape = Shape.Sheet;
    [Tooltip("Anchors and the rest shape are in this transform's space (the body, or a hat that wobbles). Empty = parent.")]
    public Transform frame;
    [Tooltip("The unit body box the cloth is kept out of (and whose bottom is the rail). Empty = the frame.")]
    public Transform body;
    [Tooltip("Centre of the pinned row / ring, in frame space.")]
    public Vector3 anchor = new Vector3(0f, 0.44f, -0.53f);
    [Tooltip("Which way the cloth runs from the anchor at rest (frame space).")]
    public Vector3 restDirection = new Vector3(0f, -0.55f, -0.84f);
    [Tooltip("Sheet: the pinned row runs along this. Tube: one axis of the ring.")]
    public Vector3 across = Vector3.right;
    [Tooltip("Sheet: width of the pinned row. Tube: diameter of the pinned ring.")]
    [Range(0.02f, 1.4f)] public float width = 0.74f;
    [Range(0.1f, 2f)] public float length = 0.8f;
    [Tooltip("Width at the far end relative to the pinned end: > 1 flares (cape), < 1 tapers (hat tip, scarf).")]
    [Range(0.05f, 1.8f)] public float hemScale = 1.18f;
    [Tooltip("The rest shape bends by this many degrees from the anchor to the tip (a hat's flop).")]
    [Range(-180f, 180f)] public float curl = 0f;
    [Tooltip("Axis of the curl (frame space).")]
    public Vector3 curlAxis = Vector3.right;
    [Range(2, 10)] public int columns = 5;
    [Range(3, 12)] public int rows = 6;

    [Header("Motion")]
    [Tooltip("Pull toward the rail (units/s²).")]
    public float gravity = 9f;
    [Tooltip("Air drag on each particle's velocity relative to the wind.")]
    [Range(0f, 8f)] public float drag = 2.4f;
    [Range(0.9f, 1f)] public float damping = 0.985f;
    [Tooltip("Wind speed (units/s) in world space; gusts vary it.")]
    [Range(0f, 8f)] public float wind = 2.2f;
    [Range(0f, 1f)] public float gustiness = 0.6f;
    public Vector3 windDirection = new Vector3(1f, 0.25f, 0.35f);
    [Tooltip("A breeze in the frame's own space that lifts the cloth away from the body in gusts (so it billows instead of " +
             "lying flat when the world wind pushes it into the body).")]
    public Vector3 breeze = new Vector3(0.3f, 0.5f, -1f);
    [Range(0f, 10f)] public float breezeStrength = 3.5f;
    [Tooltip("A slow side-to-side sway (units/s) in the frame's right direction.")]
    [Range(0f, 5f)] public float sway = 1.4f;
    [Tooltip("Flutter strength at full speed (plus a little always).")]
    [Range(0f, 4f)] public float flutter = 1.2f;
    [Range(0.5f, 12f)] public float flutterFrequency = 5f;
    public float fastSpeed = 28f;
    [Tooltip("How firmly the cloth springs back toward its rest shape (rad/s). 0 = free, ~4 = a soft knit hat.")]
    [Range(0f, 30f)] public float shapeMemory = 0f;
    [Range(1, 12)] public int iterations = 6;
    [Range(30f, 240f)] public float stepRate = 120f;
    [Range(0f, 1f)] public float shear = 0.8f;
    [Range(0f, 1f)] public float bend = 0.25f;

    [Header("Collision")]
    [Range(0f, 0.1f)] public float margin = 0.035f;
    [Range(0f, 0.1f)] public float railMargin = 0.03f;
    [Tooltip("Keep the cloth behind the body's back (capes): body-space z never above behindZ.")]
    public bool keepBehind = false;
    public float behindZ = -0.51f;
    public float teleportDistance = 2.5f;

    [Header("Extras")]
    [Tooltip("Rides on the far end (the pompom).")]
    public Transform tip;
    [Tooltip("The tip sits this far beyond the last ring along the cloth.")]
    public float tipOffset = 0.04f;

    struct Link { public int a, b; public float rest, stiff; }

    CubeRailRunner runner;
    Mesh mesh;
    Vector3[] pos, prev, verts, restLocal;
    Vector3[] anchorNow, anchorLast;
    Link[] links;
    float accumulator, time, speed, seed;
    Vector3 lastCentre;
    bool ready, built;
    // Tube: a floppy spine chain is simulated; the rings are wrapped round it each frame (a fat cone made of springs
    // would behave like a rigid truss and never flop)
    Vector3[] sp, spPrev, spRest;
    float[] ringRadius;
    Vector3 ringA0, spRootLast, spRootNow;

    int RingCount => columns;
    int Index(int c, int r) => r * columns + c;

    void Awake() => Init();

    bool initialised;

    /// <summary>Set up (Awake does this; editor previews call it on an instance that never woke).</summary>
    public void Init()
    {
        if (initialised) return;
        initialised = true;
        if (frame == null) frame = transform.parent;
        if (body == null) body = frame;
        runner = GetComponentInParent<CubeRailRunner>();
        var mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        seed = Random.value * 50f;
        Build();
    }

    void OnEnable() { ready = false; }
    void OnDestroy() { if (mesh != null) Destroy(mesh); }

    // ------------------------------------------------------------------ build

    void Build()
    {
        if (built) return;
        built = true;
        int n = columns * rows;
        pos = new Vector3[n];
        prev = new Vector3[n];
        verts = new Vector3[n];
        restLocal = new Vector3[n];
        anchorNow = new Vector3[columns];
        anchorLast = new Vector3[columns];

        // Rest shape in frame space: a spine from the anchor, bending by curl, with a ring / row across it.
        Vector3 dir0 = restDirection.sqrMagnitude > 1e-6f ? restDirection.normalized : Vector3.down;
        Vector3 axis = curlAxis.sqrMagnitude > 1e-6f ? curlAxis.normalized : Vector3.right;
        Vector3 spine = anchor;
        float seg = length / Mathf.Max(1, rows - 1);
        for (int r = 0; r < rows; r++)
        {
            float v = rows > 1 ? r / (float)(rows - 1) : 0f;
            Vector3 dir = Quaternion.AngleAxis(curl * v, axis) * dir0;
            if (r > 0) spine += dir * seg;
            Vector3 a = Vector3.ProjectOnPlane(across, dir);
            if (a.sqrMagnitude < 1e-6f) a = Vector3.ProjectOnPlane(Vector3.forward, dir);
            a.Normalize();
            Vector3 b = Vector3.Cross(dir, a);
            float w = width * Mathf.Lerp(1f, hemScale, v);
            if (shape == Shape.Tube)
            {
                if (sp == null) { sp = new Vector3[rows]; spPrev = new Vector3[rows]; spRest = new Vector3[rows]; ringRadius = new float[rows]; }
                spRest[r] = spine;
                ringRadius[r] = 0.5f * w;
                if (r == 0) ringA0 = a;
            }
            for (int c = 0; c < columns; c++)
            {
                Vector3 p;
                if (shape == Shape.Tube)
                {
                    float ang = c / (float)columns * Mathf.PI * 2f;
                    p = spine + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * (0.5f * w);
                }
                else
                {
                    float u = columns > 1 ? c / (float)(columns - 1) - 0.5f : 0f;
                    p = spine + a * (u * w);
                }
                restLocal[Index(c, r)] = p;
            }
        }

        var uv = new Vector2[n];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                uv[Index(c, r)] = new Vector2(shape == Shape.Tube ? c / (float)columns : (columns > 1 ? c / (float)(columns - 1) : 0f),
                                              rows > 1 ? r / (float)(rows - 1) : 0f);

        int quadCols = shape == Shape.Tube ? columns : columns - 1;
        var tris = new int[quadCols * (rows - 1) * 6];
        int t = 0;
        for (int r = 0; r < rows - 1; r++)
            for (int c = 0; c < quadCols; c++)
            {
                int c1 = (c + 1) % columns;
                int a = Index(c, r), b = Index(c1, r), d = Index(c, r + 1), e = Index(c1, r + 1);
                tris[t++] = a; tris[t++] = b; tris[t++] = d;
                tris[t++] = b; tris[t++] = e; tris[t++] = d;
            }
        mesh = new Mesh { name = "SoulCloth" };
        mesh.MarkDynamic();
        mesh.vertices = verts;
        mesh.uv = uv;
        mesh.triangles = tris;
        GetComponent<MeshFilter>().sharedMesh = mesh;

        var list = new System.Collections.Generic.List<Link>();
        if (shape == Shape.Tube)
        {
            // spine links: stretch + a little bend stiffness
            for (int r = 0; r < rows; r++)
            {
                if (r + 1 < rows) list.Add(new Link { a = r, b = r + 1, rest = Vector3.Distance(spRest[r], spRest[r + 1]), stiff = 1f });
                if (r + 2 < rows) list.Add(new Link { a = r, b = r + 2, rest = Vector3.Distance(spRest[r], spRest[r + 2]), stiff = bend });
            }
            links = list.ToArray();
            return;
        }
        void Add(int a, int b, float k) { list.Add(new Link { a = a, b = b, rest = Vector3.Distance(restLocal[a], restLocal[b]), stiff = k }); }
        bool tube = false;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                bool hasRight = tube || c + 1 < columns;
                int cr = (c + 1) % columns;
                if (r + 1 < rows) Add(Index(c, r), Index(c, r + 1), 1f);                       // lengthwise
                if (hasRight) Add(Index(c, r), Index(cr, r), tube ? 1f : shear);              // across (a tube keeps its ring)
                if (r + 1 < rows && hasRight)
                {
                    Add(Index(c, r), Index(cr, r + 1), shear * 0.7f);
                    Add(Index(cr, r), Index(c, r + 1), shear * 0.7f);
                }
                if (r + 2 < rows) Add(Index(c, r), Index(c, r + 2), bend);
                if (tube ? columns > 3 : c + 2 < columns) Add(Index(c, r), Index((c + 2) % columns, r), tube ? 0.9f : bend * 0.6f);
            }
        links = list.ToArray();
    }

    // ------------------------------------------------------------------ simulate

    /// <summary>Put the cloth in its rest shape around the current pose and write the mesh (icons, stills, teleports).</summary>
    public void Snap()
    {
        if (!built) Build();
        if (frame == null) return;
        for (int i = 0; i < pos.Length; i++) pos[i] = prev[i] = frame.TransformPoint(restLocal[i]);
        for (int c = 0; c < columns; c++) anchorLast[c] = anchorNow[c] = pos[c];
        if (shape == Shape.Tube)
        {
            for (int r = 0; r < rows; r++) sp[r] = spPrev[r] = frame.TransformPoint(spRest[r]);
            spRootLast = spRootNow = sp[0];
        }
        lastCentre = anchorNow[0];
        accumulator = 0f;
        speed = 0f;
        ready = true;
        WriteMesh();
    }

    void LateUpdate() => Tick(Time.deltaTime);

    /// <summary>Advance the cloth by dt seconds (LateUpdate does this every frame; editor previews call it directly).</summary>
    public void Tick(float deltaTime)
    {
        if (frame == null) return;
        for (int c = 0; c < columns; c++) anchorNow[c] = frame.TransformPoint(restLocal[Index(c, 0)]);
        if (!ready || (anchorNow[0] - anchorLast[0]).sqrMagnitude > teleportDistance * teleportDistance)
            Snap();

        bool tube = shape == Shape.Tube;
        if (tube) spRootNow = frame.TransformPoint(spRest[0]);
        float dt = Mathf.Min(deltaTime, 1f / 20f);
        if (dt > 0f)
        {
            float v = (anchorNow[0] - lastCentre).magnitude / dt;
            speed = Mathf.Lerp(speed, v, 1f - Mathf.Exp(-dt * 6f));
            lastCentre = anchorNow[0];

            float h = 1f / stepRate;
            accumulator += dt;
            int steps = Mathf.Min(8, Mathf.FloorToInt(accumulator / h));
            accumulator -= steps * h;
            if (accumulator > h) accumulator = 0f;
            for (int s = 0; s < steps; s++) Step(h, (s + 1f) / steps);
            for (int c = 0; c < columns; c++) anchorLast[c] = anchorNow[c];
            spRootLast = spRootNow;
        }
        for (int c = 0; c < columns; c++) pos[c] = prev[c] = anchorNow[c];
        if (tube)
        {
            sp[0] = spPrev[0] = spRootNow;
            CollideSpine();
            WrapRings();
        }
        else Collide();
        WriteMesh();
    }

    Vector3 Up => runner != null ? runner.transform.up : (body != null ? body.up : Vector3.up);

    void Step(float h, float f)
    {
        time += h;
        Vector3 up = Up;
        Vector3 down = -up;
        float speed01 = Mathf.Clamp01(speed / Mathf.Max(1f, fastSpeed));
        Vector3 across = frame.TransformDirection(this.across).normalized;

        // Gusting wind: strength breathes, direction wanders a little.
        // two octaves: slow swells plus quicker puffs, so it never settles into a still pose
        float g = Mathf.Clamp01(0.7f * Mathf.PerlinNoise(time * 0.45f, seed) + 0.5f * Mathf.PerlinNoise(time * 1.6f, seed + 7f) - 0.1f);
        float g2 = Mathf.PerlinNoise(seed, time * 0.3f) - 0.5f;
        Vector3 wd = windDirection.sqrMagnitude > 1e-6f ? windDirection.normalized : Vector3.right;
        wd = Quaternion.AngleAxis(g2 * 50f, Vector3.up) * wd;
        Vector3 windVel = wd * (wind * Mathf.Lerp(1f, 0.25f + 1.5f * g, gustiness));
        if (breeze.sqrMagnitude > 1e-6f) windVel += frame.TransformDirection(breeze.normalized) * (breezeStrength * (0.2f + 1.6f * g));
        windVel += frame.TransformDirection(Vector3.right) * (Mathf.Sin(time * 1.3f + seed) * sway + Mathf.Sin(time * 2.9f + seed * 2f) * sway * 0.35f);
        float spring = shapeMemory * shapeMemory;   // a soft spring back to the rest shape (natural frequency shapeMemory)
        float ripple = flutter * (0.35f + 0.5f * g * gustiness + speed01) * 9f;

        if (shape == Shape.Tube)
        {
            for (int r = 1; r < rows; r++)
            {
                float v = r / (float)(rows - 1);
                Vector3 vel = (sp[r] - spPrev[r]) / h;
                Vector3 acc = down * gravity - (vel - windVel) * drag;
                if (spring > 0f) acc += (frame.TransformPoint(spRest[r]) - sp[r]) * (spring * (1f - 0.5f * v));
                float wave = Mathf.Sin(time * flutterFrequency * 6.283f - r * 1.3f + seed);
                acc += (up * 0.6f + across * 0.4f) * (wave * ripple * v);
                Vector3 p = sp[r];
                sp[r] = p + (p - spPrev[r]) * damping + acc * (h * h);
                spPrev[r] = p;
            }
            sp[0] = spPrev[0] = Vector3.Lerp(spRootLast, spRootNow, f);
            for (int it = 0; it < iterations; it++)
                for (int k = 0; k < links.Length; k++)
                {
                    Link l = links[k];
                    Vector3 d = sp[l.b] - sp[l.a];
                    float len = d.magnitude;
                    if (len < 1e-6f) continue;
                    Vector3 corr = d * ((len - l.rest * frame.lossyScale.y) / len * l.stiff);
                    if (l.a == 0) sp[l.b] -= corr;
                    else { sp[l.a] += corr * 0.5f; sp[l.b] -= corr * 0.5f; }
                }
            CollideSpine();
            return;
        }

        for (int r = 1; r < rows; r++)
        {
            float v = r / (float)(rows - 1);
            for (int c = 0; c < columns; c++)
            {
                int i = Index(c, r);
                Vector3 vel = (pos[i] - prev[i]) / h;
                Vector3 acc = down * gravity - (vel - windVel) * drag;
                if (spring > 0f) acc += (frame.TransformPoint(restLocal[i]) - pos[i]) * (spring * (1f - 0.5f * v));
                float wave = Mathf.Sin(time * flutterFrequency * 6.283f - r * 1.3f + c * 0.7f + seed);
                // the wind's ripple runs down the cloth even standing still (stronger in a gust), more with speed
                acc += (up * 0.6f + across * 0.4f) * (wave * flutter * (0.35f + 0.5f * g * gustiness + speed01) * v * 9f);
                Vector3 p = pos[i];
                pos[i] = p + (p - prev[i]) * damping + acc * (h * h);
                prev[i] = p;
            }
        }
        for (int c = 0; c < columns; c++)
        {
            Vector3 a = Vector3.Lerp(anchorLast[c], anchorNow[c], f);
            pos[c] = prev[c] = a;
        }

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
        }
        Collide();
    }

    // Outside the body box, behind the back when asked, above the rail.
    void Collide()
    {
        if (body == null) return;
        float half = 0.5f + margin;
        Vector3 up = Up;
        Vector3 railPoint = body.position - up * (0.5f * Mathf.Max(0.01f, body.lossyScale.y) - railMargin);
        for (int i = columns; i < pos.Length; i++)
        {
            Vector3 lp = body.InverseTransformPoint(pos[i]);
            bool moved = false;
            if (keepBehind && lp.z > behindZ) { lp.z = behindZ; moved = true; }
            if (Mathf.Abs(lp.x) < half && Mathf.Abs(lp.y) < half && Mathf.Abs(lp.z) < half)
            {
                float dx = half - Mathf.Abs(lp.x), dy = half - Mathf.Abs(lp.y), dz = half - Mathf.Abs(lp.z);
                if (keepBehind && dz <= dx && dz <= dy) lp.z = -half;
                else if (dz <= dx && dz <= dy) lp.z = Mathf.Sign(lp.z) * half;
                else if (dy <= dx) lp.y = Mathf.Sign(lp.y) * half;
                else lp.x = Mathf.Sign(lp.x) * half;
                moved = true;
            }
            if (moved) pos[i] = body.TransformPoint(lp);
            float above = Vector3.Dot(pos[i] - railPoint, up);
            if (above < 0f) pos[i] -= up * above;
        }
    }

    // The tube's spine stays outside the body box (by its ring radius) and above the rail.
    void CollideSpine()
    {
        if (body == null) return;
        Vector3 up = Up;
        Vector3 railPoint = body.position - up * (0.5f * Mathf.Max(0.01f, body.lossyScale.y) - railMargin);
        for (int r = 1; r < rows; r++)
        {
            float rad = ringRadius[r] * 0.7f;
            float half = 0.5f + margin + rad;
            Vector3 lp = body.InverseTransformPoint(sp[r]);
            if (Mathf.Abs(lp.x) < half && Mathf.Abs(lp.y) < half && Mathf.Abs(lp.z) < half)
            {
                float dx = half - Mathf.Abs(lp.x), dy = half - Mathf.Abs(lp.y), dz = half - Mathf.Abs(lp.z);
                if (dz <= dx && dz <= dy) lp.z = Mathf.Sign(lp.z) * half;
                else if (dy <= dx) lp.y = Mathf.Sign(lp.y) * half;
                else lp.x = Mathf.Sign(lp.x) * half;
                sp[r] = body.TransformPoint(lp);
            }
            float above = Vector3.Dot(sp[r] - railPoint, up) - rad;
            if (above < 0f) sp[r] -= up * above;
        }
    }

    // Rings round the spine, their "across" axis carried down it so the stripes don't twist.
    void WrapRings()
    {
        Vector3 a = frame.TransformDirection(ringA0).normalized;
        float scale = frame.lossyScale.x;
        for (int r = 0; r < rows; r++)
        {
            Vector3 t = r < rows - 1 ? sp[r + 1] - sp[r] : sp[r] - sp[r - 1];
            if (t.sqrMagnitude < 1e-10f) t = frame.up;
            t.Normalize();
            Vector3 pa = Vector3.ProjectOnPlane(a, t);
            if (pa.sqrMagnitude > 1e-8f) a = pa.normalized;
            if (r == 0) continue;   // the pinned ring stays where the hat holds it
            Vector3 b = Vector3.Cross(t, a);
            for (int c = 0; c < columns; c++)
            {
                float ang = c / (float)columns * Mathf.PI * 2f;
                pos[Index(c, r)] = sp[r] + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * (ringRadius[r] * scale);
            }
        }
    }

    void WriteMesh()
    {
        Transform me = transform;
        for (int i = 0; i < pos.Length; i++) verts[i] = me.InverseTransformPoint(pos[i]);
        mesh.SetVertices(verts);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (tip != null)
        {
            Vector3 end = Vector3.zero, before = Vector3.zero;
            int last = rows - 1;
            for (int c = 0; c < columns; c++) { end += pos[Index(c, last)]; before += pos[Index(c, last - 1)]; }
            end /= columns; before /= columns;
            Vector3 d = end - before;
            tip.position = end + (d.sqrMagnitude > 1e-8f ? d.normalized * tipOffset : Vector3.zero);
        }
    }
}
