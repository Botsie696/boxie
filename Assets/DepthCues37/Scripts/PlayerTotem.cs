using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MainScene prop camera 3.7 only: the runner is the yellow eye block (Left_5 (15)'s design, DepthCues37/TotemPlayer).
/// This object is a child of MainPlayer, so RunnerVisualRig moves it onto CubeMesh and it rolls with the cube; its +Z is
/// the way the runner runs (the root's forward), +Y the face normal (away from the rail = the block's head).
///   - the eye faces the player: it sits on whichever of the block's front / left / right / top faces is turned most
///     toward the camera (never the back - the cape - or the bottom - the rail). Camera D looks from behind, so the
///     running-front face alone would almost never be seen. It changes face during a blink, so it never jumps.
///   - the eye's "up" is the block's head (on the top face: the running direction), so its tilt shows which side the
///     block is on; the pupil looks the way the runner is going, and as a corner comes up it swings to the next leg
///   - the old cube and its hat stay in the hierarchy (the game still drives them) but are never drawn
/// Purely visual.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(960)]   // after RunnerVisualRig (0) and before RunnerCape (970)
public class PlayerTotem : MonoBehaviour
{
    public Transform eyeWhite;
    public Transform pupil;
    public Renderer body;

    [Header("Eye face")]
    [Tooltip("A face must be turned this much more toward the camera than the current one before the eye moves to it.")]
    [Range(0f, 0.6f)] public float switchMargin = 0.18f;

    [Header("Gaze")]
    [Tooltip("Units before the corner where the eye starts to turn toward the next leg.")]
    [Range(1f, 20f)] public float lookAhead = 7f;
    [Tooltip("How far the pupil can slide across the eye (block units).")]
    [Range(0f, 0.2f)] public float maxPupilOffset = 0.11f;
    [Tooltip("Share of the full slide used to look along the running direction on a straight (the corner uses all of it).")]
    [Range(0f, 1f)] public float straightGaze = 0.55f;
    [Tooltip("Seconds the pupil takes to follow.")]
    [Range(0.01f, 0.5f)] public float pupilSmooth = 0.07f;
    [Tooltip("Small idle drift of the pupil (block units).")]
    [Range(0f, 0.05f)] public float idleWander = 0.012f;

    [Header("Blink")]
    public Vector2 blinkEvery = new Vector2(2.5f, 6f);
    [Range(0.05f, 0.4f)] public float blinkSeconds = 0.14f;

    [Tooltip("Hide the runner's own cube (and whatever rides on it, e.g. the hat) while this block is shown.")]
    public bool hideOriginalCube = true;
    [Tooltip("Show / hide this block, its eye and the cape together with the runner's own cube, so every hide / show the game does to the cube (game over, a checkpoint start, the door) applies to them too. Off = as before (3.7); MainScene prop camera 3.9 / Pixel turn it on - a checkpoint start hides every runner mesh but the door only shows the cube again.")]
    public bool mirrorCubeVisibility = false;

    static readonly Vector3[] Faces = { Vector3.forward, Vector3.right, Vector3.left, Vector3.up };
    static readonly int EyeFaceId = Shader.PropertyToID("_EyeFace");

    CubeRailRunner runner;
    DynamicPathGenerator gen;
    Transform eyeRoot;
    MaterialPropertyBlock block;
    readonly List<Renderer> hidden = new List<Renderer>();
    readonly List<Renderer> own = new List<Renderer>();
    MeshRenderer cubeRenderer;
    Vector3 pupilBase, eyeScale, pupilScale;
    Vector2 gaze, gazeVel;
    float nextBlink, blinkT = -1f;
    int face = 0, pendingFace = -1;
    bool collected;

    /// <summary>Where the pupil is looking, -1..1 per axis of the eye (x = eye right, y = eye up). For tests.</summary>
    public Vector2 Gaze => maxPupilOffset > 0f ? gaze / maxPupilOffset : Vector2.zero;
    /// <summary>The face the eye is on: 0 front, 1 right, 2 left, 3 top. For tests.</summary>
    public int EyeFace => face;

    void Awake()
    {
        runner = GetComponentInParent<CubeRailRunner>();
        if (eyeWhite != null) { eyeRoot = eyeWhite.parent; eyeScale = eyeWhite.localScale; }
        if (pupil != null) { pupilBase = pupil.localPosition; pupilScale = pupil.localScale; }
        block = new MaterialPropertyBlock();
        nextBlink = Random.Range(blinkEvery.x, blinkEvery.y);
        ApplyFace(0);
    }

    void Start()
    {
        gen = FindFirstObjectByType<DynamicPathGenerator>();
    }

    // The runner's cube moved onto CubeMesh in RunnerVisualRig.Awake; everything drawn there except this block is old.
    void CollectHidden()
    {
        collected = true;
        if (runner == null || !hideOriginalCube) return;
        Transform cubeMesh = FindDeep(runner.transform, "CubeMesh");
        Transform root = cubeMesh != null ? cubeMesh : runner.transform;
        if (cubeMesh != null) cubeRenderer = cubeMesh.GetComponent<MeshRenderer>();
        GetComponentsInChildren(true, own);
        foreach (RunnerCape cape in runner.GetComponentsInChildren<RunnerCape>(true))
            foreach (Renderer r in cape.GetComponentsInChildren<Renderer>(true)) if (!own.Contains(r)) own.Add(r);
        foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            if (!r.transform.IsChildOf(transform)) hidden.Add(r);
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { Transform f = FindDeep(c, name); if (f != null) return f; }
        return null;
    }

    void OnDisable()
    {
        foreach (Renderer r in hidden) if (r != null) r.forceRenderingOff = false;
    }

    void LateUpdate()
    {
        if (!collected) CollectHidden();
        foreach (Renderer r in hidden) if (r != null && !r.forceRenderingOff) r.forceRenderingOff = true;
        if (mirrorCubeVisibility && cubeRenderer != null)
        {
            bool show = cubeRenderer.enabled;
            foreach (Renderer r in own) if (r != null && r.enabled != show) r.enabled = show;
        }
        ChooseFace();
        UpdateBlink();
        UpdateGaze();
    }

    // The eye's frame on face i, in this block's space: forward = out of the face, up = the block's head
    // (on the top face: the running direction).
    static Quaternion FaceRotation(int i) => Quaternion.LookRotation(Faces[i], i == 3 ? Vector3.forward : Vector3.up);

    void ApplyFace(int i)
    {
        face = i;
        if (eyeRoot != null && eyeRoot != transform) eyeRoot.localRotation = FaceRotation(i);
        if (body != null)
        {
            body.GetPropertyBlock(block);
            Vector3 n = Faces[i];
            block.SetVector(EyeFaceId, new Vector4(n.x, n.y, n.z, 0f));
            body.SetPropertyBlock(block);
        }
    }

    void ChooseFace()
    {
        Camera cam = Camera.main;
        if (cam == null || runner == null) return;
        // Not while the block is mid-roll (its head is not on the root's up yet): the choice would flicker.
        if (Vector3.Dot(transform.up, runner.transform.up) < 0.98f) return;
        Vector3 toCam = cam.orthographic ? -cam.transform.forward : (cam.transform.position - transform.position).normalized;
        int best = face;
        float bestScore = Vector3.Dot(transform.TransformDirection(Faces[face]), toCam) + switchMargin;
        for (int i = 0; i < Faces.Length; i++)
        {
            float s = Vector3.Dot(transform.TransformDirection(Faces[i]), toCam);
            if (s > bestScore) { bestScore = s; best = i; }
        }
        if (best != face && pendingFace < 0)
        {
            pendingFace = best;
            if (blinkT < 0f) blinkT = 0f;   // close the eye, move it while closed
        }
    }

    void UpdateGaze()
    {
        Vector3 lookWorld = Vector3.zero;
        float amount = 0f;
        if (runner != null && gen != null && runner.IsRunning && !runner.IsGameOver)
        {
            var seg = gen.PlayerSegment;
            var next = gen.SegmentAt(gen.PlayerSegmentIndex + 1);
            Vector3 fwd = seg != null && seg.direction.sqrMagnitude > 0.01f ? seg.direction.normalized : runner.transform.forward;
            float w = 0f;
            if (seg != null && next != null)
            {
                float toCorner = Vector3.Dot(seg.endPosition - runner.transform.position, fwd) + 0.5f * gen.cubeSize;
                w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, lookAhead, toCorner));
                lookWorld = Vector3.Slerp(fwd, next.direction.normalized, w);
            }
            else lookWorld = fwd;
            amount = Mathf.Lerp(straightGaze, 1f, w);
        }
        Vector2 target = Vector2.zero;
        if (eyeRoot != null && lookWorld != Vector3.zero)
        {
            Vector3 l = eyeRoot.InverseTransformDirection(lookWorld);
            Vector2 p = new Vector2(l.x, l.y);
            // Looking straight out of the face (running toward the camera): the pupil stays centred.
            if (p.sqrMagnitude > 0.04f) target = p.normalized * (maxPupilOffset * amount * Mathf.Clamp01(p.magnitude * 1.5f));
        }
        float t = Time.time;
        target += new Vector2(Mathf.PerlinNoise(t * 0.35f, 1.7f) - 0.5f, Mathf.PerlinNoise(3.1f, t * 0.35f) - 0.5f) * (2f * idleWander);
        gaze = Vector2.SmoothDamp(gaze, Vector2.ClampMagnitude(target, maxPupilOffset), ref gazeVel, pupilSmooth, Mathf.Infinity, Time.deltaTime);
        if (pupil != null) pupil.localPosition = pupilBase + new Vector3(gaze.x, gaze.y, 0f);
    }

    void UpdateBlink()
    {
        if (eyeWhite == null) return;
        float squeeze = 1f;
        if (blinkT >= 0f)
        {
            blinkT += Time.deltaTime;
            float u = Mathf.Clamp01(blinkT / blinkSeconds);
            squeeze = Mathf.Lerp(1f, 0.05f, Mathf.Sin(u * Mathf.PI));
            if (u >= 0.5f && pendingFace >= 0) { ApplyFace(pendingFace); pendingFace = -1; gaze = gazeVel = Vector2.zero; }
            if (u >= 1f) { blinkT = -1f; nextBlink = Random.Range(blinkEvery.x, blinkEvery.y); }
        }
        else if ((nextBlink -= Time.deltaTime) <= 0f) blinkT = 0f;

        // The discs lie with their axis out of the face: their local Z is the eye's up / down.
        eyeWhite.localScale = new Vector3(eyeScale.x, eyeScale.y, eyeScale.z * squeeze);
        if (pupil != null) pupil.localScale = new Vector3(pupilScale.x, pupilScale.y, pupilScale.z * squeeze);
    }
}
