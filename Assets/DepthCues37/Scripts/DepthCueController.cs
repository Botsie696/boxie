using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// MainScene prop camera 3.7 only (on the "DepthCues" object): helps the player always know which face of the rail
/// they are on and where it leads, without changing how the game plays. It drives the depth-cue globals of
/// PropLooks/LookTransition/FlatFaceBlockDepthCue (3.7's LookTransitionDirector.blockWipeShader):
///   face ladder   top / lit side / shade side / bottom step down in brightness, hue kept
///   top lip       a thin pale line along the top edges of the beams
///   contact       a soft square of light under the runner + the runner's own bottom edges in the same colour
///   rail          a thin line on the ridden face to the end of the leg, crossbar where the turn is; on a hidden face it
///                 runs along the edges that face shares with the visible ones
/// Each checkpoint has its own colours (looks[0] = checkpoint 1 ...); checkpoints past the list get no cues and the
/// scene's own ActiveFaceHighlight lane comes back. As the run speeds up the contact and the rail get stronger while the
/// decoration calms down (trail light / motes / wisps via RunnerTrailFX.decorScale, bloom via a runtime volume).
/// Purely visual: it reads the runner and the generator and never touches gameplay, the camera or any asset.
/// </summary>
[DefaultExecutionOrder(950)]   // after LookTransitionDirector and PropLookController
public class DepthCueController : MonoBehaviour
{
    [System.Serializable]
    public class CueLook
    {
        public string name = "Checkpoint";
        [Header("Face ladder (linear luminance share of the top face; faces are only ever darkened)")]
        [Range(0f, 1f)] public float ladder = 1f;
        [Range(0f, 1f)] public float litShare = 0.46f;     // ~70 % of the top on screen
        [Range(0f, 1f)] public float shadeShare = 0.22f;   // ~50 %
        [Range(0f, 1f)] public float bottomShare = 0.1f;   // ~35 %
        [Range(0f, 1f)] public float runnerLadder = 1f;
        [Tooltip("The camera-right side of the path is the lit one in this look (else the left). The runner block and its cape are lit the same way.")]
        public bool litRight = true;
        [Header("Top lip")]
        [Range(0f, 1f)] public float lip = 0.3f;
        [Range(0.01f, 0.15f)] public float lipWidth = 0.045f;
        [Header("Contact")]
        public Color contactColor = new Color(0.55f, 0.95f, 1f);
        [Range(0f, 1.5f)] public float contact = 0.75f;
        [Tooltip("Half size of the contact square, world units (the runner covers about 0.55 of it).")]
        [Range(0.5f, 1.5f)] public float contactSize = 0.9f;
        [Range(0f, 1f)] public float runnerRim = 0.7f;
        [Header("Rail")]
        public Color railColor = new Color(1f, 0.92f, 0.85f);
        [Range(0f, 1.5f)] public float rail = 0.55f;
        [Range(0.01f, 0.15f)] public float railWidth = 0.04f;
        [Header("Trail (FaceHuggingTrail)")]
        public Color trailTint = Color.white;
        [Range(0f, 1f)] public float trailTintAmount = 0.2f;
        [Range(0.3f, 1.5f)] public float trailIntensity = 0.85f;
        [Header("Calm (before speed)")]
        [Tooltip("Trail decoration kept at low speed (point light on the path, motes, wisps). 1 = as the look has it.")]
        [Range(0f, 1f)] public float decor = 1f;
        [Tooltip("Share of the bloom taken away at low speed (bright looks bloom the runner into a blob).")]
        [Range(0f, 1f)] public float bloomCut = 0f;

        public CueLook Copy() => (CueLook)MemberwiseClone();

        public static CueLook Lerp(CueLook a, CueLook b, float t, CueLook into)
        {
            into.ladder = Mathf.Lerp(a.ladder, b.ladder, t);
            into.litRight = t < 0.5f ? a.litRight : b.litRight;
            into.litShare = Mathf.Lerp(a.litShare, b.litShare, t);
            into.shadeShare = Mathf.Lerp(a.shadeShare, b.shadeShare, t);
            into.bottomShare = Mathf.Lerp(a.bottomShare, b.bottomShare, t);
            into.runnerLadder = Mathf.Lerp(a.runnerLadder, b.runnerLadder, t);
            into.lip = Mathf.Lerp(a.lip, b.lip, t);
            into.lipWidth = Mathf.Lerp(a.lipWidth, b.lipWidth, t);
            into.contactColor = Color.Lerp(a.contactColor, b.contactColor, t);
            into.contact = Mathf.Lerp(a.contact, b.contact, t);
            into.contactSize = Mathf.Lerp(a.contactSize, b.contactSize, t);
            into.runnerRim = Mathf.Lerp(a.runnerRim, b.runnerRim, t);
            into.railColor = Color.Lerp(a.railColor, b.railColor, t);
            into.rail = Mathf.Lerp(a.rail, b.rail, t);
            into.railWidth = Mathf.Lerp(a.railWidth, b.railWidth, t);
            into.trailTint = Color.Lerp(a.trailTint, b.trailTint, t);
            into.trailTintAmount = Mathf.Lerp(a.trailTintAmount, b.trailTintAmount, t);
            into.trailIntensity = Mathf.Lerp(a.trailIntensity, b.trailIntensity, t);
            into.decor = Mathf.Lerp(a.decor, b.decor, t);
            into.bloomCut = Mathf.Lerp(a.bloomCut, b.bloomCut, t);
            return into;
        }
    }

    public DynamicPathGenerator pathGen;
    [Tooltip("Checkpoint 1, 2, 3 ... Checkpoints past the end of the list get no cues.")]
    public CueLook[] looks = new CueLook[0];
    [Tooltip("Seconds the cues take to blend into the next checkpoint's colours after its portal.")]
    [Range(0.1f, 4f)] public float blendSeconds = 1.6f;

    [Header("Rail")]
    [Tooltip("Units of rail kept behind the runner.")]
    [Range(0f, 2f)] public float railBehind = 0.35f;
    [Tooltip("Units before the leg's end where the rail comes back to full strength (so the turn reads).")]
    [Range(0.5f, 8f)] public float railEndBoost = 3f;
    [Tooltip("Seconds the rail / contact flash lasts after a roll onto another face.")]
    [Range(0.1f, 1f)] public float switchFlashSeconds = 0.4f;
    [Tooltip("Hide the scene's ActiveFaceHighlight lane while cues are on (the rail replaces it).")]
    public bool replaceFaceLane = true;

    [Header("Calm with speed")]
    [Tooltip("Runner speed where the calming starts and where it is complete.")]
    public Vector2 calmSpeed = new Vector2(14f, 30f);
    [Range(0f, 1f)] public float contactGainAtSpeed = 0.5f;
    [Range(0f, 1f)] public float railGainAtSpeed = 0.4f;
    [Tooltip("How much of the trail's decoration (point light, motes, wisps) is gone at full speed.")]
    [Range(0f, 1f)] public float decorCutAtSpeed = 0.65f;
    [Tooltip("Fade the trail decoration (wisps, motes, point light) while the ridden face is turned away from the camera.")]
    public bool hiddenFadesDecor = true;
    [Tooltip("How much of the bloom is gone at full speed.")]
    [Range(0f, 1f)] public float bloomCutAtSpeed = 0.6f;

    static readonly int FaceId = Shader.PropertyToID("_DCFace");
    static readonly int Face2Id = Shader.PropertyToID("_DCFace2");
    static readonly int RunnerPId = Shader.PropertyToID("_DCRunnerP");
    static readonly int RunnerNId = Shader.PropertyToID("_DCRunnerN");
    static readonly int RunnerFId = Shader.PropertyToID("_DCRunnerF");
    static readonly int ContactId = Shader.PropertyToID("_DCContact");
    static readonly int RailId = Shader.PropertyToID("_DCRail");
    static readonly int Rail2Id = Shader.PropertyToID("_DCRail2");
    static readonly int PlayerId = Shader.PropertyToID("_DCPlayer");

    static readonly CueLook Off = new CueLook { ladder = 0f, runnerLadder = 0f, lip = 0f, contact = 0f, runnerRim = 0f, rail = 0f, trailTintAmount = 0f, trailIntensity = 1f };

    CubeRailRunner runner;
    ActiveFaceHighlight lane;
    RunnerTrailFX trailFx;
    FaceHuggingTrail faceTrail;
    Volume calmVolume;
    VolumeProfile calmProfile;

    readonly CueLook current = new CueLook();
    CueLook from, to;
    float blendT = 1f;
    int checkpoint = -1;

    float presence;           // 0..1: the runner is on the rail and running
    float flash;
    float calm;               // 0..1 with speed
    float decorVis = 1f;      // 0..1: the ridden face is visible (smoothed)
    int lastSide = -1;
    Vector3 point, normal = Vector3.up, forward = Vector3.forward;
    float toEnd;

    /// <summary>Checkpoint the cues are showing (1 = the start).</summary>
    public int Checkpoint => checkpoint;
    public float Calm => calm;

    void Start()
    {
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        runner = pathGen != null ? pathGen.player : FindFirstObjectByType<CubeRailRunner>();
        if (runner != null)
        {
            lane = runner.GetComponent<ActiveFaceHighlight>();
            trailFx = runner.GetComponent<RunnerTrailFX>();
            faceTrail = runner.GetComponent<FaceHuggingTrail>();
        }
        calmProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        calmProfile.name = "DepthCueCalm (runtime)";
        Bloom bloom = calmProfile.Add<Bloom>(true);
        bloom.intensity.Override(0f);
        calmVolume = gameObject.AddComponent<Volume>();
        calmVolume.isGlobal = true;
        calmVolume.priority = 1000f;
        calmVolume.sharedProfile = calmProfile;
        calmVolume.weight = 0f;
        Snap(CurrentCheckpoint());
    }

    void OnDisable()
    {
        Shader.SetGlobalVector(FaceId, Vector4.zero);
        Shader.SetGlobalVector(Face2Id, Vector4.zero);
        Shader.SetGlobalVector(RunnerPId, Vector4.zero);
        Shader.SetGlobalVector(RailId, Vector4.zero);
        if (lane != null) lane.enabled = true;
        if (trailFx != null) trailFx.decorScale = 1f;
        if (faceTrail != null) { faceTrail.tintAmount = 0f; faceTrail.intensityScale = 1f; }
        if (calmVolume != null) calmVolume.weight = 0f;
    }

    void OnDestroy()
    {
        if (calmProfile != null) Destroy(calmProfile);
    }

    int CurrentCheckpoint() => pathGen != null ? pathGen.PortalsTaken + 1 : 1;

    CueLook LookFor(int cp) => cp >= 1 && cp <= looks.Length && looks[cp - 1] != null ? looks[cp - 1] : Off;

    void Snap(int cp)
    {
        checkpoint = cp;
        from = to = LookFor(cp);
        CueLook.Lerp(to, to, 1f, current);
        blendT = 1f;
    }

    void LateUpdate()
    {
        if (runner == null) return;
        float dt = Time.deltaTime;

        // Checkpoint colours: blend after a portal, snap on a restart / checkpoint start (more than one step).
        int cp = CurrentCheckpoint();
        if (cp != checkpoint)
        {
            if (cp == checkpoint + 1)
            {
                from = current.Copy();
                to = LookFor(cp);
                blendT = 0f;
                checkpoint = cp;
            }
            else Snap(cp);
        }
        if (blendT < 1f)
        {
            blendT = Mathf.MoveTowards(blendT, 1f, dt / blendSeconds);
            CueLook.Lerp(from, to, Mathf.SmoothStep(0f, 1f, blendT), current);
        }
        bool cuesOn = current.contact + current.rail + current.ladder > 0.01f;
        if (replaceFaceLane && lane != null && lane.enabled == cuesOn) lane.enabled = !cuesOn;

        // Speed: clarity up, decoration down.
        float speed01 = Mathf.InverseLerp(calmSpeed.x, calmSpeed.y, runner.forwardSpeed);
        calm = Mathf.MoveTowards(calm, Mathf.SmoothStep(0f, 1f, speed01), dt * 0.5f);
        if (calmVolume != null) calmVolume.weight = cuesOn ? 1f - (1f - current.bloomCut) * (1f - bloomCutAtSpeed * calm) : 0f;
        if (faceTrail != null)
        {
            faceTrail.tint = current.trailTint;
            faceTrail.tintAmount = current.trailTintAmount;
            faceTrail.intensityScale = current.trailIntensity;
        }

        UpdateRunnerFace(dt);

        float contact = current.contact * presence * (1f + contactGainAtSpeed * calm) * (1f + 0.35f * flash);
        float rail = current.rail * presence * (1f + railGainAtSpeed * calm);
        Vector4 cc = current.contactColor.linear;
        Vector4 rc = current.railColor.linear;
        float vis = FaceVisibility();
        // Behind / under the leg: the trail decoration (wisps, motes, point light) fades too, so nothing glows past the
        // leg as if it were a second path (user, 3.7). FaceHuggingTrail.finHeight is 0 in 3.7 for the same reason.
        decorVis = Mathf.MoveTowards(decorVis, hiddenFadesDecor ? vis : 1f, dt / 0.12f);
        if (trailFx != null) trailFx.decorScale = cuesOn ? current.decor * (1f - decorCutAtSpeed * calm) * decorVis : 1f;

        Shader.SetGlobalVector(FaceId, new Vector4(current.ladder, current.litShare, current.shadeShare, current.bottomShare));
        Shader.SetGlobalVector(Face2Id, new Vector4(current.lip, current.lipWidth, current.runnerLadder, current.runnerRim));
        Shader.SetGlobalVector(RunnerPId, new Vector4(point.x, point.y, point.z, contact));
        Shader.SetGlobalVector(RunnerNId, new Vector4(normal.x, normal.y, normal.z, vis));
        Shader.SetGlobalVector(RunnerFId, new Vector4(forward.x, forward.y, forward.z, toEnd));
        Shader.SetGlobalVector(ContactId, new Vector4(cc.x, cc.y, cc.z, current.contactSize));
        Shader.SetGlobalVector(RailId, new Vector4(rc.x, rc.y, rc.z, rail));
        Shader.SetGlobalVector(Rail2Id, new Vector4(current.railWidth, railBehind, flash, railEndBoost));
        Shader.SetGlobalVector(PlayerId, new Vector4(current.litRight ? 1f : -1f, calm, 0f, 0f));
    }

    // The face under the runner (normal + the point right beneath it), the way along the leg and how far to its end.
    void UpdateRunnerFace(float dt)
    {
        bool live = runner.currentCube != null && runner.IsRunning && !runner.isFlying && !runner.IsGameOver;
        presence = Mathf.MoveTowards(presence, live ? 1f : 0f, dt / (live ? 0.2f : 0.12f));
        flash = Mathf.MoveTowards(flash, 0f, dt / switchFlashSeconds);
        if (!live) { lastSide = -1; return; }

        int side = ((runner.currentSideIndex % 4) + 4) % 4;
        if (lastSide >= 0 && side != lastSide) flash = 1f;
        lastSide = side;

        normal = runner.SideNormal(side);
        Vector3 from = runner.transform.position;
        if (Physics.Raycast(from, -normal, out RaycastHit hit, 1.6f, ~0, QueryTriggerInteraction.Ignore) && Vector3.Dot(hit.normal, normal) > 0.9f)
            point = hit.point;
        else
            point = from - normal * 0.55f;   // mid-roll: assume it rests on the face

        float cs = pathGen != null ? pathGen.cubeSize : 1f;
        var seg = pathGen != null ? pathGen.PlayerSegment : null;
        Vector3 dir = seg != null && seg.direction.sqrMagnitude > 0.01f ? seg.direction.normalized : runner.currentCube.forward;
        dir = Vector3.ProjectOnPlane(dir, normal);
        if (dir.sqrMagnitude < 1e-4f) dir = Vector3.ProjectOnPlane(runner.currentCube.forward, normal);
        forward = dir.normalized;
        float inset = lane != null ? lane.endInsetCubes * cs : 0f;
        toEnd = seg != null ? Mathf.Max(0f, Vector3.Dot(seg.endPosition - point, forward) + 0.5f * cs - inset) : 4f * cs;
    }

    // 1 when the ridden face is turned to the camera, 0 when the leg hides it.
    float FaceVisibility()
    {
        Camera cam = Camera.main;
        if (cam == null) return 1f;
        Vector3 toCam = cam.orthographic ? -cam.transform.forward : (cam.transform.position - point).normalized;
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.3f, Vector3.Dot(normal, toCam)));
    }
}
