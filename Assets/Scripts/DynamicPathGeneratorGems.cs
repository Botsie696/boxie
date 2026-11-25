using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The little diamond that marks a corner of the path.
///
/// Every leg is laid with a DIAGONAL RISER (see AppendSegment): the last cube of the leg arriving at
/// a turn and the first cube of the leg leaving it only touch along one edge, so the corner has a
/// notch in it with nothing in it. A hollow diamond hangs in the air over that notch, drifting up
/// and down, with a soft halo behind it - the marker in the reference shot. It is an OUTLINE drawn
/// on a camera-facing quad by Custom/PathGem, so it reads as the same diamond from every yaw the
/// camera takes. Its glow is CONSTANT: nothing about it brightens or dims once it is up.
///
/// SIDEWAYS TURNS ONLY. A leg that climbs or drops gets nothing: the notch of a vertical turn is
/// read as a step the runner has to take, and a glow sitting in it only muddles it.
///
/// Mobile notes, the same rules the coins follow:
///   * Two triangles and one material per marker, another two for its halo, all of them batching
///     together. No lights, no shadows, no depth write, no post-processing and no textures: both
///     the outline and the halo fall out of the quad's own UVs (Custom/PathGem, Custom/SoftGlow).
///   * A gem is only data until the cube it belongs to is drawn (PathDepthShading), and only then
///     does it borrow a pooled renderer, which it hands straight back when the corner is hidden or
///     culled. Nothing is instantiated after the opening seconds.
///   * No colliders anywhere - the runner's forward probe raycasts everything on the Default layer
///     and would take one for a path cube.
///   * The drift is one number per frame, applied to the transforms of the markers that are
///     actually on screen.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Corner Gems")]
    [Tooltip("Float a glowing diamond over the notch at every sideways turn of the path.")]
    public bool spawnCornerGems = true;
    [Tooltip("Colour of the outline and its halo. HDR - push it past white for a brighter line.")]
    [ColorUsage(true, true)] public Color cornerGemColor = new Color(1f, 0.83f, 0.42f, 1f);
    [Tooltip("Width of the diamond, in cubes.")]
    [Range(0.1f, 4f)] public float cornerGemSize = 1.6f;
    [Tooltip("How tall the diamond is compared with its width.")]
    [Range(0.6f, 2.5f)] public float cornerGemStretch = 1.1f;
    [Tooltip("How high over the corner the diamond hangs, in cubes, measured from the notch. 0 puts it in the gap between the two cubes; the default leaves clear air between the track and the marker. Only new corners take a change.")]
    [Range(-0.5f, 6f)] public float cornerGemLift = 2.6f;
    [Tooltip("Thickness of the outline. Small values stay a hairline at any distance - the line is never drawn thinner than a pixel.")]
    [Range(0.005f, 0.4f)] public float cornerGemThickness = 0.05f;
    [Tooltip("How much the line bleeds into the air around it.")]
    [Range(0f, 2f)] public float cornerGemBleed = 0.3f;
    [Tooltip("How much the inside of the diamond is filled in. 0 = hollow, as in the reference.")]
    [Range(0f, 1f)] public float cornerGemFill = 0f;
    [Tooltip("Degrees per second the diamond turns in the plane of the screen. 0 = it hangs still, point up.")]
    [Range(0f, 360f)] public float cornerGemSpin = 0f;
    [Tooltip("How far the diamond rises and falls, in cubes.")]
    [Range(0f, 0.5f)] public float cornerGemBob = 0.1f;
    [Tooltip("Rises and falls this many times a second.")]
    [Range(0f, 3f)] public float cornerGemBobSpeed = 0.5f;

    [Header("Corner Gem Glow")]
    [Tooltip("Draw the soft halo behind the diamond. Off leaves just the outline.")]
    public bool cornerGemGlow = true;
    [Tooltip("Width of the halo, as a multiple of the diamond's width.")]
    [Range(0.5f, 8f)] public float cornerGemGlowSize = 1.6f;
    [Tooltip("Brightness of the halo.")]
    [Range(0f, 3f)] public float cornerGemGlowStrength = 0.4f;

    class PathGem
    {
        public Segment segment;
        public GameObject cube;      // the corner cube: the gem is drawn exactly as much as this is
        public Vector3 position;
        public float phase;          // so neighbouring markers do not drift in step
        public MeshRenderer core;
        public MeshRenderer glow;
    }

    const int GemPoolWarm = 8;
    static readonly int GemColorId = Shader.PropertyToID("_Color");
    static readonly int GemThicknessId = Shader.PropertyToID("_Thickness");
    static readonly int GemGlowId = Shader.PropertyToID("_Glow");
    static readonly int GemFillId = Shader.PropertyToID("_Fill");

    private readonly List<PathGem> gems = new List<PathGem>();
    private readonly Stack<PathGem> spareGems = new Stack<PathGem>();
    private readonly Stack<MeshRenderer> gemCorePool = new Stack<MeshRenderer>();
    private readonly Stack<MeshRenderer> gemGlowPool = new Stack<MeshRenderer>();
    private Transform gemRoot;
    private Mesh gemMesh;
    private Mesh gemQuad;
    private Material gemMaterial;
    private Material gemGlowMaterial;
    private Color appliedGemColor;
    private Vector4 appliedGemShape = new Vector4(-1f, -1f, -1f, -1f);
    private float gemSpinAngle;
    private float gemClock;
    private int gemSerial;

    // ------------------------------------------------------------------
    // Placement
    // ------------------------------------------------------------------

    /// <summary>
    /// incoming is the leg that ends at the corner, nextDir the heading of the leg just laid after
    /// it. The marker hangs over the empty corner cell between the two - half a cube along each
    /// heading from the end of the incoming leg, which is the middle of the diagonal step - lifted
    /// cornerGemLift cubes into the air above it.
    /// </summary>
    void PlaceCornerGem(Segment incoming, Vector3 nextDir)
    {
        if (!spawnCornerGems || incoming == null || incoming.cubes.Count == 0) return;

        Vector3 from = incoming.direction;
        // Sideways turns only: nothing on a climb, a drop, or the leg that leaves one.
        if (Mathf.Abs(from.y) > 0.5f || Mathf.Abs(nextDir.y) > 0.5f) return;
        if (Vector3.Dot(from, nextDir) > 0.9f) return;   // straight on: no corner, no notch
        if (!GemSystemReady()) return;

        GameObject cornerCube = incoming.cubes[incoming.cubes.Count - 1];
        if (cornerCube == null) return;

        PathGem g = spareGems.Count > 0 ? spareGems.Pop() : new PathGem();
        g.segment = incoming;
        g.cube = cornerCube;
        g.position = incoming.endPosition
                     + (from + nextDir) * (cubeSize * 0.5f)
                     + Vector3.up * (cubeSize * cornerGemLift);
        g.phase = gemSerial++ * 0.618f;   // golden-ratio steps: no two neighbours in step
        g.core = null;
        g.glow = null;
        gems.Add(g);
    }

    // ------------------------------------------------------------------
    // Lifetime
    // ------------------------------------------------------------------

    /// <summary>Drops every gem, for a fresh path.</summary>
    void ResetGems()
    {
        for (int i = gems.Count - 1; i >= 0; i--) RemoveGemAt(i);
        gemSerial = 0;
    }

    /// <summary>A gem goes with the leg whose corner it marks.</summary>
    void ReleaseGemsOn(Segment seg)
    {
        for (int i = gems.Count - 1; i >= 0; i--)
            if (gems[i].segment == seg) RemoveGemAt(i);
    }

    void RemoveGemAt(int i)
    {
        PathGem g = gems[i];
        HideGem(g);
        g.segment = null;
        g.cube = null;
        spareGems.Push(g);

        int last = gems.Count - 1;
        gems[i] = gems[last];
        gems.RemoveAt(last);
    }

    void HideGem(PathGem g)
    {
        if (g.core != null) { g.core.enabled = false; gemCorePool.Push(g.core); g.core = null; }
        if (g.glow != null) { g.glow.enabled = false; gemGlowPool.Push(g.glow); g.glow = null; }
    }

    void DestroyGemRuntime()
    {
        if (gemMaterial != null) Destroy(gemMaterial);
        if (gemGlowMaterial != null) Destroy(gemGlowMaterial);
        if (gemQuad != null) Destroy(gemQuad);
        gemMaterial = null;
        gemGlowMaterial = null;
        gemMesh = null;
        gemQuad = null;
    }

    /// <summary>Builds the shared mesh, quad, materials and pools on first use.</summary>
    bool GemSystemReady()
    {
        if (gemQuad != null) return true;

        Shader gemShader = Shader.Find("Custom/PathGem");
        Shader glowShader = Shader.Find("Custom/SoftGlow");
        if (gemShader == null) return false;

        gemQuad = BuildQuad();
        gemMesh = gemQuad;   // the outline is drawn on the same camera-facing quad as the halo

        gemMaterial = new Material(gemShader) { name = "PathGem" };
        if (glowShader != null) gemGlowMaterial = new Material(glowShader) { name = "PathGemGlow" };
        appliedGemColor = Color.clear;
        appliedGemShape = new Vector4(-1f, -1f, -1f, -1f);
        ApplyGemLook();

        gemRoot = new GameObject("CornerGems").transform;
        gemRoot.SetParent(transform, false);

        for (int i = 0; i < GemPoolWarm; i++)
        {
            gemCorePool.Push(NewGemRenderer("Gem", gemMesh, gemMaterial));
            if (gemGlowMaterial != null) gemGlowPool.Push(NewGemRenderer("GemGlow", gemQuad, gemGlowMaterial));
        }
        return true;
    }

    MeshRenderer NewGemRenderer(string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(gemRoot, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.enabled = false;
        return r;
    }

    /// <summary>Pushes the sliders to the two shared materials, and only when one of them has moved.</summary>
    void ApplyGemLook()
    {
        Vector4 shape = new Vector4(cornerGemThickness, cornerGemBleed, cornerGemFill, cornerGemGlowStrength);
        if (appliedGemColor == cornerGemColor && appliedGemShape == shape) return;
        appliedGemColor = cornerGemColor;
        appliedGemShape = shape;

        if (gemMaterial != null)
        {
            gemMaterial.SetColor(GemColorId, cornerGemColor);
            gemMaterial.SetFloat(GemThicknessId, cornerGemThickness);
            gemMaterial.SetFloat(GemGlowId, cornerGemBleed);
            gemMaterial.SetFloat(GemFillId, cornerGemFill);
        }
        if (gemGlowMaterial != null)
        {
            Color glow = cornerGemColor;
            glow.a = cornerGemGlowStrength;
            gemGlowMaterial.SetColor(GemColorId, glow);
        }
    }

    static Mesh BuildQuad()
    {
        var mesh = new Mesh { name = "PathGemQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f), new Vector3( 0.5f, -0.5f, 0f),
        };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();
        return mesh;
    }

    // ------------------------------------------------------------------
    // Per frame
    // ------------------------------------------------------------------

    void UpdateGems()
    {
        if (gems.Count == 0) return;

        ApplyGemLook();

        float dt = Time.deltaTime;
        gemSpinAngle = Mathf.Repeat(gemSpinAngle + cornerGemSpin * dt, 360f);
        gemClock += dt;

        if (shadingRef == null) shadingRef = GetComponent<PathDepthShading>();
        bool shaded = shadingRef != null && shadingRef.isActiveAndEnabled;

        Camera cam = Camera.main;
        Quaternion faceCamera = cam != null ? cam.transform.rotation : Quaternion.identity;
        float parentScale = gemRoot != null ? Mathf.Max(0.0001f, gemRoot.lossyScale.x) : 1f;
        float width = cubeSize * cornerGemSize / parentScale;
        bool wantGlow = cornerGemGlow && gemGlowMaterial != null;

        for (int i = gems.Count - 1; i >= 0; i--)
        {
            PathGem g = gems[i];
            if (g.cube == null) { RemoveGemAt(i); continue; }

            // Drawn exactly as much as its corner cube: it grows in with the track ahead of the
            // runner and goes with it behind, so nothing is ever seen appearing in view.
            float shown = shaded ? shadingRef.ShownAmount(g.cube) : (g.cube.activeInHierarchy ? 1f : 0f);
            if (shown <= 0.01f) { HideGem(g); continue; }

            float grow = Mathf.SmoothStep(0f, 1f, shown);
            float bob = Mathf.Sin((gemClock * cornerGemBobSpeed + g.phase) * Mathf.PI * 2f);
            Vector3 pos = g.position + Vector3.up * (cubeSize * cornerGemBob * bob);

            // Facing the camera, point up: the marker reads as the same diamond from every yaw.
            Quaternion facing = faceCamera * Quaternion.AngleAxis(gemSpinAngle, Vector3.forward);

            if (g.core == null) { g.core = gemCorePool.Count > 0 ? gemCorePool.Pop() : NewGemRenderer("Gem", gemMesh, gemMaterial); g.core.enabled = true; }
            Transform t = g.core.transform;
            t.SetPositionAndRotation(pos, facing);
            t.localScale = new Vector3(width * grow, width * cornerGemStretch * grow, width * grow);

            if (!wantGlow)
            {
                if (g.glow != null) { g.glow.enabled = false; gemGlowPool.Push(g.glow); g.glow = null; }
                continue;
            }

            // Steady: the halo only ever grows in with its corner. Its size and brightness never move
            // afterwards, so the marker does not pulse - the user asked for a constant glow.
            if (g.glow == null) { g.glow = gemGlowPool.Count > 0 ? gemGlowPool.Pop() : NewGemRenderer("GemGlow", gemQuad, gemGlowMaterial); g.glow.enabled = true; }
            float halo = width * cornerGemGlowSize * grow;
            Transform q = g.glow.transform;
            q.SetPositionAndRotation(pos, faceCamera);
            q.localScale = new Vector3(halo, halo * cornerGemStretch, halo);
        }
    }
}
