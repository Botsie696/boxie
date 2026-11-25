using UnityEngine;

/// <summary>
/// "MainScene prop camera 5" only. The run starts with the runner coming OUT of a portal: an exit-style
/// tower door (the same PortalDoor the zone doors use, skinned by PropPortalTowerSkin) is built round the
/// first cube of the first leg. While the run waits to start, and while a restarted path rises, the
/// runner is held a little behind the door's opaque void plane, so it is hidden inside the tower; the
/// moment the run begins it drives out through the glowing doorway into the first leg, and the door's
/// light shafts flare for a moment.
///
/// Nothing in the generator, the runner or the zone controller is changed: the door is built with the
/// public PortalDoor.Create, fades in and out with the track through PathDepthShading (the same calls
/// PortalZoneController makes for its own doors, so it never pops), and is rebuilt for each new path
/// after a restart. The tower and the zone doors are separate objects; this one is never crossed.
/// </summary>
[DefaultExecutionOrder(650)]   // after PortalDoor.Update and PropPortalTowerSkin
public class PropStartPortal : MonoBehaviour
{
    public DynamicPathGenerator pathGen;
    public CubeRailRunner runner;
    [Tooltip("The opaque hole material (M_PortalVoid); empty = taken from the scene's PortalZoneController.")]
    public Material voidMaterial;
    [Tooltip("Where the runner waits along the first cube's leg, in cubes from its centre: inside the tunnel, between the two void planes (+-0.7).")]
    [Range(-0.2f, 0.2f)] public float seatAlong = 0f;
    [Tooltip("Seconds the light shafts flare for when the run begins.")]
    [Range(0.2f, 3f)] public float flareSeconds = 1.2f;
    [Tooltip("Peak brightness multiplier of the shafts during the flare.")]
    [Range(1f, 4f)] public float flareGain = 2.2f;

    PortalDoor door;
    DynamicPathGenerator.Segment builtFor, spentFor;
    PathDepthShading shading;
    PortalZoneController zones;
    Renderer raysRenderer, towerRenderer, frontVoid, backVoid;
    Material backMat;
    bool wasRunning;
    float flare;

    static readonly int FadeId = Shader.PropertyToID("_Fade");

    void LateUpdate()
    {
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (runner == null) runner = FindFirstObjectByType<CubeRailRunner>();
        if (pathGen == null || runner == null) return;
        if (shading == null) shading = pathGen.GetComponent<PathDepthShading>();
        if (zones == null) zones = pathGen.GetComponent<PortalZoneController>();

        DynamicPathGenerator.Segment seg = FirstSegment();
        if (seg == null) return;

        // A new path (first run, or after a restart) gets a new door; a door that has been left behind
        // (fully faded) is not built again for the same path.
        if (door == null && seg != builtFor && seg != spentFor) Build(seg);
        if (door == null) return;
        if (seg != builtFor) { Clear(); return; }

        // Fade with the track: in with the drawn track beside it, out with the crossed leg.
        int fade = shading != null ? Mathf.CeilToInt(shading.edgeFadeCubes) : 0;
        GameObject probe = seg.cubes[Mathf.Min(fade, seg.cubes.Count - 1)];
        door.SetReveal(shading != null && probe != null ? shading.ShownAmount(probe) : 1f);
        float crossed = shading != null && seg.cubes[0] != null ? shading.CrossedAmount(seg.cubes[0]) : 0f;
        door.SetCrossed(crossed);
        if (crossed >= 0.999f)
        {
            Clear();
            spentFor = seg;
            return;
        }
        UpdateBackVoid();

        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 f = cam.transform.forward; f.y = 0f;
            if (f.sqrMagnitude > 1e-4f) door.SetView(f.normalized);
        }

        HoldRunner(seg);
        Flare();
    }

    DynamicPathGenerator.Segment FirstSegment()
    {
        var zs = pathGen.Zones;
        if (zs == null || zs.Count == 0) return null;
        var segs = zs[0].segments;
        if (segs == null || segs.Count == 0) return null;
        var s = segs[0];
        return s != null && s.cubes.Count > 0 && s.cubes[0] != null ? s : null;
    }

    void Build(DynamicPathGenerator.Segment seg)
    {
        Transform cube0 = seg.cubes[0].transform;
        Vector3 forward = seg.direction; forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = cube0.forward;
        forward.Normalize();
        Material voidMat = voidMaterial != null ? voidMaterial : (zones != null ? zones.voidMaterial : null);
        door = PortalDoor.Create(cube0, cube0.position, forward, cube0.up, 0, pathGen.cubeSize, true, true, voidMat);
        door.SetColor(Color.white);
        door.SetGlowColor(zones != null ? zones.doorGlowColor : new Color(1f, 0.84f, 0.91f));
        door.SetReveal(0f);
        builtFor = seg;
        raysRenderer = null;
        BuildBackVoid();
    }

    void Clear()
    {
        if (door != null) Destroy(door.gameObject);
        if (backMat != null) Destroy(backMat);
        door = null; raysRenderer = null; towerRenderer = null; frontVoid = null; backVoid = null; backMat = null;
    }

    // The camera of this scene looks along the first leg, so it sees the tower's BACK doorway. A second
    // copy of the glowing void (same arch, same colours) closes it, so the runner waiting inside is hidden
    // from either side and drives out of the far doorway. It irises open with the door's fade-in.
    void BuildBackVoid()
    {
        Transform t = door.transform.Find("Void");
        Transform tw = door.transform.Find("Tower");
        if (t == null || tw == null) return;
        frontVoid = t.GetComponent<Renderer>();
        towerRenderer = tw.GetComponent<Renderer>();
        var mf = t.GetComponent<MeshFilter>();
        if (frontVoid == null || mf == null) return;

        var go = new GameObject("BackVoid");
        go.transform.SetParent(door.transform, false);
        go.transform.localPosition = new Vector3(t.localPosition.x, t.localPosition.y, -t.localPosition.z);
        go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        backVoid = go.AddComponent<MeshRenderer>();
        backVoid.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        backVoid.receiveShadows = false;
        backMat = new Material(frontVoid.sharedMaterial) { name = "StartPortalBackVoid (runtime)" };
        backVoid.sharedMaterial = backMat;
        backVoid.enabled = false;
    }

    void UpdateBackVoid()
    {
        if (backVoid == null || towerRenderer == null || frontVoid == null) return;
        bool visible = towerRenderer.enabled;
        if (backVoid.enabled != visible) backVoid.enabled = visible;
        if (!visible) return;
        float fade = towerRenderer.sharedMaterial != null && towerRenderer.sharedMaterial.HasProperty(FadeId) ? towerRenderer.sharedMaterial.GetFloat(FadeId) : 1f;
        backMat.CopyPropertiesFromMaterial(frontVoid.sharedMaterial);
        if (backMat.HasProperty("_Open")) backMat.SetFloat("_Open", Mathf.Max(0.001f, fade));
    }

    // While the run waits (or a restarted path is still rising) the runner sits just behind the void
    // plane: inside the tower, hidden by the opaque doorway.
    void HoldRunner(DynamicPathGenerator.Segment seg)
    {
        if (runner.IsGameOver || !(runner.IsWaitingToStart || runner.IsRebuilding)) return;
        Transform cube0 = seg.cubes[0].transform;
        Vector3 fwd = door.Forward;
        float u = pathGen.cubeSize;
        Vector3 rel = runner.transform.position - cube0.position;
        float along = Vector3.Dot(rel, fwd);
        if (rel.magnitude > 1.6f * u) return;   // not seated on the first cube
        float want = seatAlong * u;
        if (Mathf.Abs(along - want) <= 0.01f * u) return;
        Vector3 pos = runner.transform.position + fwd * (want - along);
        runner.transform.position = pos;
        var rb = runner.GetComponent<Rigidbody>();
        if (rb != null) rb.position = pos;
        // The jump must not leave a streak behind it.
        foreach (TrailRenderer tr in runner.GetComponentsInChildren<TrailRenderer>()) tr.Clear();
    }

    // The shafts flare when the run begins, then settle.
    void Flare()
    {
        bool running = runner.IsRunning;
        if (running && !wasRunning) flare = 1f;
        wasRunning = running;
        if (flare <= 0f) return;
        flare = Mathf.Max(0f, flare - Time.deltaTime / Mathf.Max(0.05f, flareSeconds));
        if (raysRenderer == null)
        {
            Transform rays = door.transform.Find("Rays");
            if (rays != null) raysRenderer = rays.GetComponent<Renderer>();
        }
        if (raysRenderer != null && raysRenderer.sharedMaterial != null && raysRenderer.sharedMaterial.HasProperty(FadeId))
        {
            float cur = raysRenderer.sharedMaterial.GetFloat(FadeId);
            float k = 1f + (flareGain - 1f) * Mathf.SmoothStep(0f, 1f, flare);
            raysRenderer.sharedMaterial.SetFloat(FadeId, cur * k);
        }
    }

    void OnDisable()
    {
        Clear();
        builtFor = null; spentFor = null;
    }
}
