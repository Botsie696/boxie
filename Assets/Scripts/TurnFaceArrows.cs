using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Thin glowing yellow chevrons painted on the face the runner has to be on to take the next turn.
/// That face is the one pointing toward the outgoing leg - the same rule the coins use (see
/// DynamicPathGeneratorCoins). The set sits at the corner end of the leg and lights up in a chase toward
/// the corner. By default only the turn at the end of the runner's current leg is marked, and the set
/// stays up the whole time the runner is on that leg. Drawn over the trail so riding the face never
/// hides it. Purely visual: reads DynamicPathGenerator.GetUpcomingCorners and never changes the path.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DynamicPathGenerator))]
public class TurnFaceArrows : MonoBehaviour
{
    [Tooltip("Material using Runner/ChevronGlow (additive, baked bloom). Empty = one is made at runtime.")]
    public Material arrowMaterial;
    [ColorUsage(false, true)] public Color arrowColor = new Color(1f, 0.72f, 0.12f, 1f);
    [Tooltip("Overall glow strength.")]
    [Range(0f, 4f)] public float glowIntensity = 2.4f;
    [Tooltip("How many upcoming turns get chevrons (each only once its track is drawn).")]
    [Range(1, 8)] public int cornersAhead = 5;
    [Range(1, 8)] public int chevronCount = 4;
    [Tooltip("Distance between chevrons, in cubes.")]
    [Range(0.1f, 1.5f)] public float chevronSpacing = 0.55f;
    [Tooltip("Size of each chevron's glow quad as a share of the face width (the stroke itself is about 60% of it). Keep at 1 or below so the glow stays on the one face.")]
    [Range(0.2f, 1f)] public float chevronWidth = 1f;
    [Tooltip("How far the nearest chevron sits back from the corner end of the leg, in cubes.")]
    [Range(0f, 2f)] public float cornerGap = 0.2f;
    [Tooltip("Chase cycles per second.")]
    [Range(0f, 4f)] public float chaseSpeed = 1.4f;
    [Range(0f, 1f)] public float minBrightness = 0.45f;
    public float lift = 0.03f;

    class Hint
    {
        public GameObject go;
        public Mesh mesh;
        public Material mat;
        public Color[] colors;
        public float shown;
        public float builtSize = -1f;
        public bool next;   // the set on the first cube of the leg AFTER the corner, continuing the L
        public bool onLeg;  // this corner ends the leg the runner is on
        public bool hold;   // the runner is already on this corner's face: show the steady "hold" cue
        // TUTORIAL ADDITION: how enhanced this set is (0 = normal) and where its face is, for the lane.
        public float enh;
        public Vector3 faceEnd, faceNormal, faceTravel;
        public float faceWidth;
        public bool placed;
    }

    // ---- TUTORIAL ADDITION: tutorial-only chevron look. Inert (every value below unused) unless TutorialManager
    // sets tutorialMode; with it off, every scene draws exactly as before. ----
    [System.NonSerialized] public bool tutorialMode;
    /// <summary>0 = exactly the normal look, 1 = the full tutorial look. Lowered lesson by lesson.</summary>
    [System.NonSerialized] public float tutorialBoost;
    /// <summary>1 = the full lane, 0 = the lane shrunk back to just the chevrons.</summary>
    [System.NonSerialized] public float tutorialLaneScale = 1f;
    /// <summary>Extra lane glow (the L2 miss flashes it twice).</summary>
    [System.NonSerialized] public float tutorialLaneFlash;
    [System.NonSerialized] public TutorialChevronBoost tutorialSettings;
    /// <summary>How enhanced the next corner is right now (0..1).</summary>
    public float TutorialEnhance { get; private set; }
    static readonly TutorialChevronBoost DefaultBoost = new TutorialChevronBoost();
    TutorialChevronBoost Boost => tutorialSettings ?? DefaultBoost;
    int QuadCount => tutorialMode ? Mathf.Max(chevronCount, Boost.chevronCount) : chevronCount;
    GameObject laneCur, laneNext;
    Material laneCurMat, laneNextMat;
    Mesh laneMesh;
    Texture2D laneTex;
    // ---- end TUTORIAL ADDITION ----

    [Tooltip("When the runner is already on the face the coming turn needs (a 0-tap leg, or after the roll), the chevrons stop chasing and glow steady, so an extra habitual tap reads as wrong. Switched on at run time by a DifficultyProfile with feedbackFX.")]
    public bool holdCue = false;
    float burst;
    /// <summary>A brief flare of every chevron on show (close call / clean corner).</summary>
    public void Burst(float strength) { burst = Mathf.Max(burst, strength); }

    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int ZTestId = Shader.PropertyToID("_ZTest");

    private DynamicPathGenerator path;
    private PathDepthShading shading;
    private Material runtimeMat;
    private Texture2D chevronTex;
    private readonly List<Hint> hints = new List<Hint>();
    private readonly Collider[] overlap = new Collider[4];
    private readonly List<Vector3> verts = new List<Vector3>();
    private int builtCount = -1;

    void Awake()
    {
        path = GetComponent<DynamicPathGenerator>();
        shading = GetComponent<PathDepthShading>();
        if (shading == null) shading = FindFirstObjectByType<PathDepthShading>();
    }

    void OnDisable()
    {
        foreach (var h in hints) if (h.go != null) h.go.SetActive(false);
        if (laneCur != null) laneCur.SetActive(false);    // TUTORIAL ADDITION
        if (laneNext != null) laneNext.SetActive(false);  // TUTORIAL ADDITION
    }

    void OnDestroy()
    {
        foreach (var h in hints) { if (h.go != null) Destroy(h.go); if (h.mat != null) Destroy(h.mat); }
        if (runtimeMat != null) Destroy(runtimeMat);
        if (chevronTex != null) Destroy(chevronTex);
        // TUTORIAL ADDITION
        if (laneCur != null) Destroy(laneCur);
        if (laneNext != null) Destroy(laneNext);
        if (laneCurMat != null) Destroy(laneCurMat);
        if (laneNextMat != null) Destroy(laneNextMat);
        if (laneMesh != null) Destroy(laneMesh);
        if (laneTex != null) Destroy(laneTex);
    }

    void LateUpdate()
    {
        if (path == null || path.player == null) return;
        EnsureHints();

        var corners = path.player.IsGameOver ? null : path.GetUpcomingCorners(cornersAhead);
        float cs = path.cubeSize;
        burst = Mathf.MoveTowards(burst, 0f, Time.deltaTime * 3f);
        float enhNext = tutorialMode ? TutorialEnhanceFor(corners) : 0f;   // TUTORIAL ADDITION
        TutorialEnhance = enhNext;
        for (int i = 0; i < hints.Count; i++)
        {
            Hint h = hints[i];
            int ci = h.next ? i - cornersAhead : i;
            h.enh = ci == 0 ? enhNext : 0f;   // TUTORIAL ADDITION: only the next corner (0 outside the tutorial)
            h.placed = false;
            if (corners == null || ci >= corners.Count || !Place(h, corners[ci], cs)) { h.go.SetActive(false); continue; }
            h.placed = true;
            h.hold = (holdCue || tutorialMode) && h.onLeg && !h.next && !path.player.IsRevolving &&   // TUTORIAL ADDITION (|| tutorialMode)
                     Vector3.Dot(path.player.SideNormal(path.player.currentSideIndex), corners[ci].outgoingDirection.normalized) > 0.9f;
            h.go.SetActive(true);
            h.mat.color = arrowColor;
            float boost = h.enh > 0f ? Mathf.Lerp(1f, Boost.intensityMultiplier, h.enh) : 1f;   // TUTORIAL ADDITION (1 outside the tutorial)
            h.mat.SetFloat(IntensityId, glowIntensity * (1f + 1.6f * burst) * (h.hold ? 1.15f : 1f) * boost);
            h.mat.SetFloat(ZTestId, (float)CompareFunction.LessEqual);
            Animate(h);
        }
        UpdateLanes(enhNext, cs);   // TUTORIAL ADDITION (hides itself outside the tutorial)
    }

    // ---- TUTORIAL ADDITION ----

    // The next corner's enhancement: tutorialBoost, shown from Boost.showSeconds before the corner (time, not cubes).
    float TutorialEnhanceFor(List<DynamicPathGenerator.PathCorner> corners)
    {
        if (corners == null || corners.Count == 0 || tutorialBoost <= 0f) return 0f;
        DynamicPathGenerator.PathCorner c = corners[0];
        if (c.segmentIndex - 1 != path.PlayerSegmentIndex) return 0f;
        float speed = path.player.forwardSpeed;
        if (speed <= 0.01f) return 0f;
        float dist = Vector3.Dot(c.position - path.player.transform.position, c.incomingDirection.normalized);
        float seconds = Mathf.Max(0f, dist) / speed;
        float show = Mathf.Max(0.05f, Boost.showSeconds);
        float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(show - 0.25f, show, seconds));
        return Mathf.Clamp01(tutorialBoost) * k;
    }

    // Chase phase locked to the tutorial's master clock: a whole number of chases per loop.
    float TutorialChaseT()
    {
        float perLoop = Mathf.Max(1f, Mathf.Round(chaseSpeed * Boost.chaseMultiplier * TutorialClock.Period));
        return TutorialClock.Cycles * perLoop;
    }

    // A soft glowing strip under the chevrons on the turn face, from laneBackCubes before the corner to it, and on
    // round onto the first laneNextCubes of the next leg: it shows that THIS face leads into the next leg.
    void UpdateLanes(float enh, float cs)
    {
        bool want = tutorialMode && enh > 0.001f && hints.Count > cornersAhead;
        Hint cur = want ? hints[0] : null;
        Hint nxt = want ? hints[cornersAhead] : null;
        bool showCur = want && cur.placed && cur.onLeg;
        bool showNext = want && nxt.placed && showCur;
        if (!showCur && laneCur == null) return;

        EnsureLanes();
        laneCur.SetActive(showCur);
        laneNext.SetActive(showNext);
        if (!showCur) return;

        TutorialChevronBoost b = Boost;
        float pulse = 0.85f + 0.15f * Mathf.Cos(TutorialClock.Phase * Mathf.PI * 2f);
        float intensity = glowIntensity * b.laneIntensity * enh * pulse * (1f + Mathf.Max(0f, tutorialLaneFlash));

        float chevronSpan = (chevronCount * chevronSpacing + 0.5f) * cs;
        float back = Mathf.Lerp(chevronSpan, Mathf.Max(chevronSpan, b.laneBackCubes * cs), Mathf.Clamp01(tutorialLaneScale));
        PlaceLane(laneCur, laneCurMat, cur.faceEnd - cur.faceTravel * back, cur.faceNormal, cur.faceTravel, cur.faceWidth * b.laneWidth, back, intensity);
        if (showNext)
        {
            float fwd = Mathf.Lerp(chevronSpan * 0.5f, b.laneNextCubes * cs, Mathf.Clamp01(tutorialLaneScale));
            PlaceLane(laneNext, laneNextMat, nxt.faceEnd, nxt.faceNormal, nxt.faceTravel, nxt.faceWidth * b.laneWidth, fwd, intensity);
        }
    }

    void PlaceLane(GameObject go, Material mat, Vector3 start, Vector3 normal, Vector3 travel, float width, float length, float intensity)
    {
        go.transform.SetPositionAndRotation(start, Quaternion.LookRotation(normal, travel));
        go.transform.localScale = new Vector3(Mathf.Max(0.001f, width), Mathf.Max(0.001f, length), 1f);
        mat.color = arrowColor;
        mat.SetFloat(IntensityId, intensity);
        mat.SetFloat(ZTestId, (float)CompareFunction.LessEqual);
    }

    void EnsureLanes()
    {
        if (laneCur != null) return;
        laneTex = MakeLaneTexture(64);
        laneMesh = new Mesh { name = "TurnFaceLane" };
        // Unit quad: x across the face (-0.5..0.5), y along the lane (0..1), z out of the face.
        laneMesh.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0f), new Vector3(0.5f, 0f), new Vector3(0.5f, 1f), new Vector3(-0.5f, 1f) });
        laneMesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
        laneMesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        laneMesh.RecalculateBounds();
        laneCur = MakeLane("TurnFaceLane", out laneCurMat);
        laneNext = MakeLane("TurnFaceLane next", out laneNextMat);
    }

    GameObject MakeLane(string name, out Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, true);
        go.AddComponent<MeshFilter>().sharedMesh = laneMesh;
        var r = go.AddComponent<MeshRenderer>();
        mat = new Material(runtimeMat) { mainTexture = laneTex };
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        go.SetActive(false);
        return go;
    }

    // Soft across the lane (A), hot along its centre line (G), fading out at both ends.
    static Texture2D MakeLaneTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Lane", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
            float d = Mathf.Abs(u - 0.5f) * 2f;
            float ends = Mathf.SmoothStep(0f, 1f, v / 0.15f) * Mathf.SmoothStep(0f, 1f, (1f - v) / 0.1f);
            float a = (Mathf.Exp(-d * d * 3f) * 0.8f + Mathf.Exp(-d * d * 18f) * 0.4f) * Mathf.Clamp01((1f - d) * 4f) * ends;
            float core = Mathf.Clamp01(1f - d / 0.25f);
            px[y * size + x] = new Color32(255, (byte)(core * core * 255f), 255, (byte)(Mathf.Clamp01(a) * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(true, true);
        return tex;
    }
    // ---- end TUTORIAL ADDITION ----

    // Puts the set on the out-facing face of the incoming leg's last cube. False if that cube isn't drawn.
    bool Place(Hint h, DynamicPathGenerator.PathCorner c, float cs)
    {
        Vector3 inDir = c.incomingDirection.normalized;
        Vector3 outDir = c.outgoingDirection.normalized;
        // AppendSegment: the outgoing leg starts at lastEnd + inDir + outDir (the diagonal riser).
        Vector3 lastCube = c.position - (inDir + outDir) * cs;
        // The next set stands on the first cube of the outgoing leg, on the side that touches the incoming set.
        Vector3 probe = h.next ? c.position : lastCube;

        int n = Physics.OverlapSphereNonAlloc(probe, cs * 0.2f, overlap, path.player.groundLayer, QueryTriggerInteraction.Ignore);
        GameObject cube = null;
        for (int i = 0; i < n && cube == null; i++)
            if (overlap[i].GetComponentInParent<CubeRailRunner>() == null) cube = overlap[i].gameObject;
        if (cube == null) return false;

        // PathDepthShading hides the cube's own renderer and draws a thinner child copy that fades in.
        Renderer r = null;
        foreach (var candidate in cube.GetComponentsInChildren<Renderer>())
            if (candidate.enabled) { r = candidate; break; }
        // Marked as soon as its cube is drawn at all, whichever leg the runner is on; the leg the
        // runner is on is always fully lit.
        bool onLeg = c.segmentIndex - 1 == path.PlayerSegmentIndex;
        h.onLeg = onLeg;
        if (!onLeg) h.enh = 0f;   // TUTORIAL ADDITION: only the corner at the end of the runner's leg is enhanced
        h.shown = shading != null ? shading.ShownAmount(cube) : 1f;
        if (h.shown <= 0.01f && !onLeg) return false;
        h.shown = onLeg ? 1f : Mathf.Clamp01(h.shown * 2f);

        Bounds b = r != null ? r.bounds : new Bounds(cube.transform.position, Vector3.one * cs);
        Vector3 ext = b.extents;
        // Face normal and travel direction of the set. The first set rides the outDir face of the last cube
        // heading inDir; the next one rides the -inDir face of the first cube heading outDir, so the two
        // faces meet along an edge and the chevrons turn the corner as an L.
        Vector3 normal = h.next ? -inDir : outDir;
        Vector3 travel = h.next ? outDir : inDir;
        float surface = Vector3.Dot(ext, Abs(normal));
        float faceWidth = 2f * Vector3.Dot(ext, Abs(Vector3.Cross(normal, travel)));
        float halfLen = Vector3.Dot(ext, Abs(travel));

        // Local frame: x across, y along travel, z out of the face. Origin at the corner end of the face
        // (the next set: at the start edge of the leg, chevrons stacked forward from it).
        Vector3 origin = b.center + normal * (surface + lift)
            + (h.next ? -travel * (halfLen - cornerGap * cs) : travel * (halfLen - cornerGap * cs));
        float size = faceWidth * chevronWidth;

        // Only on a face the camera can see - never drawn through the cube.
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 toCam = cam.orthographic ? -cam.transform.forward : (cam.transform.position - origin).normalized;
            if (Vector3.Dot(normal, toCam) < 0.02f) return false;
        }
        h.go.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(normal, travel));
        // TUTORIAL ADDITION: the enhanced corner is drawn a little larger (k is exactly 1 outside the tutorial).
        float k = h.enh > 0f ? Mathf.Lerp(1f, Boost.scaleMultiplier, h.enh) : 1f;
        h.go.transform.localScale = new Vector3(size * k, size * k, 1f);
        if (!Mathf.Approximately(size, h.builtSize)) Rebuild(h, cs / Mathf.Max(0.01f, size));
        h.builtSize = size;
        // TUTORIAL ADDITION: where this face is, for the lane (the first set: its corner end; the next set: its start edge).
        h.faceEnd = b.center + normal * (surface + lift) + (h.next ? -travel * halfLen : travel * halfLen);
        h.faceNormal = normal;
        h.faceTravel = travel;
        h.faceWidth = faceWidth;
        return true;
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    void Animate(Hint h)
    {
        // TUTORIAL ADDITION: the enhanced corner shows up to Boost.chevronCount chevrons and chases on the tutorial's
        // master clock. Outside the tutorial: n == quads == chevronCount and t is the original Time.time * chaseSpeed.
        int quads = QuadCount;
        int n = chevronCount;
        if (h.enh > 0f && !h.next) n = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(chevronCount, Boost.chevronCount, h.enh)), 1, quads);
        float t = h.enh > 0.01f ? TutorialChaseT() : Time.time * chaseSpeed;
        if (h.hold)
        {
            // Steady glow, gently breathing: "you are on it - hold".
            float breathe = 0.88f + 0.12f * Mathf.Sin(Time.time * 5f);
            var steady = new Color(1f, 1f, 1f, breathe * h.shown);
            for (int i = 0; i < quads * 4; i++) h.colors[i] = i < n * 4 ? steady : Color.clear;
            h.mesh.colors = h.colors;
            return;
        }
        for (int i = 0; i < quads; i++)
        {
            if (i >= n) { for (int j = 0; j < 4; j++) h.colors[i * 4 + j] = Color.clear; continue; }
            // i = 0 is the chevron nearest the corner; the light runs from the far one toward it.
            // The next set continues the run: the light leaves the corner one step after it arrives there.
            float phase = h.next
                ? Mathf.Repeat(t - (i + 1) / (float)n, 1f)
                : Mathf.Repeat(t + i / (float)n, 1f);
            float k = Mathf.Pow(1f - phase, 3f);
            var col = new Color(1f, 1f, 1f, Mathf.Lerp(minBrightness, 1f, k) * h.shown);
            for (int j = 0; j < 4; j++) h.colors[i * 4 + j] = col;
        }
        h.mesh.colors = h.colors;
    }

    void EnsureHints()
    {
        if (runtimeMat == null)
        {
            chevronTex = MakeChevronTexture(128);
            runtimeMat = arrowMaterial != null ? new Material(arrowMaterial) : new Material(Shader.Find("Runner/ChevronGlow"));
            runtimeMat.mainTexture = chevronTex;
        }
        if (builtCount == QuadCount && hints.Count == cornersAhead * 2) return;   // TUTORIAL ADDITION: QuadCount == chevronCount outside the tutorial

        foreach (var h in hints) { if (h.go != null) Destroy(h.go); if (h.mat != null) Destroy(h.mat); }
        hints.Clear();
        for (int i = 0; i < cornersAhead * 2; i++)
        {
            bool next = i >= cornersAhead;
            var go = new GameObject("TurnFaceArrows " + (next ? "next " : "") + i);
            go.transform.SetParent(transform, true);
            var mesh = new Mesh { name = "TurnFaceArrows" };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            var mat = new Material(runtimeMat);   // own copy: the depth test differs per hint
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            go.SetActive(false);
            hints.Add(new Hint { go = go, mesh = mesh, mat = mat, colors = new Color[QuadCount * 4], next = next });
        }
        builtCount = QuadCount;
    }

    // One unit quad per chevron (the set is scaled to the chevron width), stepped back along -y.
    // cubeInUnits converts cube distances into the scaled space.
    void Rebuild(Hint h, float cubeInUnits)
    {
        verts.Clear();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        for (int i = 0; i < QuadCount; i++)   // TUTORIAL ADDITION: QuadCount == chevronCount outside the tutorial
        {
            // the first set stacks back from the corner (-y), the next one forward from the leg start (+y)
            float top = h.next ? 1f + i * chevronSpacing * cubeInUnits : -i * chevronSpacing * cubeInUnits;
            int b = verts.Count;
            verts.Add(new Vector3(-0.5f, top - 1f)); uvs.Add(new Vector2(0f, 0f));
            verts.Add(new Vector3(0.5f, top - 1f));  uvs.Add(new Vector2(1f, 0f));
            verts.Add(new Vector3(0.5f, top));       uvs.Add(new Vector2(1f, 1f));
            verts.Add(new Vector3(-0.5f, top));      uvs.Add(new Vector2(0f, 1f));
            tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }
        h.mesh.Clear();
        h.mesh.SetVertices(verts);
        h.mesh.SetUVs(0, uvs);
        h.mesh.SetTriangles(tris, 0);
        h.mesh.colors = h.colors;
        h.mesh.RecalculateBounds();
    }

    // A thin chevron pointing up (+v) with a baked bloom around it (Runner/ChevronGlow).
    // A = coverage: the stroke plus a tight and a wide falloff. G = the hot centre of the stroke.
    static Texture2D MakeChevronTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Chevron", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        Vector2 apex = new Vector2(0.5f, 0.66f), left = new Vector2(0.2f, 0.36f), right = new Vector2(0.8f, 0.36f);
        const float stroke = 0.028f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
            float d = Mathf.Min(SegmentDistance(p, left, apex), SegmentDistance(p, right, apex));
            float line = 1f - Mathf.Clamp01((d - stroke) / 0.01f);
            float tight = Mathf.Exp(-(d * d) / (0.06f * 0.06f)) * 0.55f;
            float wide = Mathf.Exp(-(d * d) / (0.16f * 0.16f)) * 0.22f;
            // fade to nothing at the quad's edge so neighbouring quads never show a seam
            float edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(p.x, 1f - p.x), Mathf.Min(p.y, 1f - p.y)) / 0.12f);
            float a = Mathf.Clamp01(line + tight + wide) * edge;
            float core = 1f - Mathf.Clamp01(d / (stroke * 0.9f));
            px[y * size + x] = new Color32(255, (byte)(core * 255f), 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(true, true);
        return tex;
    }

    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }
}
