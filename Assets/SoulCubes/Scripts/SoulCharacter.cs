using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Soul cubes: the personality of a character cube from the shop (CubeSkin.characterPrefab). Works in two places:
///   - in a run (spawned by SkinApplier on the runner's CubeMesh, so it rolls with it): hides the plain cube (and 3.7's
///     PlayerTotem), mirrors the cube's visibility (the death shatter / restart hide it), and reacts to the game -
///     a squash on landing, an effort squint on a tap, surprise then a grin on a close call, a happy squint on a coin,
///     a wince on a crash, a determined look at speed; the pupils look down the leg and swing to the next one near a corner
///   - in the shop (no runner): looks where it is going, glances at the camera, breathes, yawns, blinks
/// The face (eyes, brows, mouth, cheeks...) sits on whichever of the front / left / right faces is turned most toward the
/// camera - camera D looks from behind, so a front-only face would rarely be seen - and only moves there inside a blink.
/// Rigid accessories (ears, crown, hat) sway on springs; SoulCloth does the cloth. Purely visual.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(975)]   // after RunnerVisualRig (0) / PlayerTotem (960), before SoulCloth (980)
public class SoulCharacter : MonoBehaviour
{
    [Header("Parts")]
    [Tooltip("Squash / breathing scale is played on this (the body, face, hat and cloth all ride on it).")]
    public Transform rig;
    public Renderer bodyRenderer;
    [Tooltip("Turned to the face the eyes are on: +Z = out of that face, +Y = the head.")]
    public Transform faceRoot;
    public Transform[] eyeWhites = new Transform[0];
    public Transform[] pupils = new Transform[0];
    [Tooltip("Small highlights on the pupils (move with them, never squash below a dot).")]
    public Transform[] shines = new Transform[0];
    [Tooltip("Left then right.")]
    public Transform[] brows = new Transform[0];
    public Transform[] cheeks = new Transform[0];
    [Tooltip("Drawn by code: a line that smiles, frowns and opens.")]
    public MeshFilter mouth;
    [Tooltip("Rigid accessories that sway on a spring when the body moves (pivot at their base).")]
    public Transform[] wobblers = new Transform[0];
    [Tooltip("Accessories that twitch now and then (cat ears).")]
    public Transform[] twitchers = new Transform[0];
    public SoulCloth[] cloths = new SoulCloth[0];

    [Header("Personality")]
    [Tooltip("How open the eyes are at rest (sleepy < 1).")]
    [Range(0.2f, 1.2f)] public float eyeOpen = 1f;
    [Tooltip("Brow angle at rest: + = determined (inner ends down), - = worried.")]
    [Range(-25f, 25f)] public float browTilt = 0f;
    [Range(-1f, 1f)] public float smile = 0.6f;
    [Range(0.04f, 0.3f)] public float mouthWidth = 0.14f;
    public Vector2 blinkEvery = new Vector2(2.2f, 5.5f);
    [Range(0.06f, 0.6f)] public float blinkSeconds = 0.13f;
    [Tooltip("Chance a blink is a slow, contented one (cats).")]
    [Range(0f, 1f)] public float slowBlinkChance = 0.1f;
    [Tooltip("Seconds between yawns while idle or slow (0 = never).")]
    public float yawnEvery = 0f;
    [Tooltip("Idle breathing bounce.")]
    [Range(0f, 0.06f)] public float breathe = 0.018f;
    [Tooltip("Squash on landing a roll.")]
    [Range(0f, 0.3f)] public float landSquash = 0.12f;
    [Range(0f, 1f)] public float cheekGlow = 0.6f;

    [Header("Eyes")]
    [Range(0f, 0.15f)] public float maxPupilOffset = 0.045f;
    [Range(1f, 20f)] public float lookAhead = 7f;
    [Range(0f, 0.6f)] public float switchMargin = 0.18f;
    [Tooltip("Face the camera from the front / left / right face (off = always the front).")]
    public bool faceFollowsCamera = true;
    [Tooltip("Camera the face turns to (empty = Camera.main). The shop sets its stage camera here.")]
    public Camera viewCamera;

    enum Emotion { None, Happy, Effort, Surprised, Hurt, Yawn }

    static readonly Vector3[] Faces = { Vector3.forward, Vector3.right, Vector3.left };

    CubeRailRunner runner;
    DynamicPathGenerator gen;
    CoinCollector coins;
    MeshRenderer cubeRenderer;
    readonly List<Renderer> hidden = new List<Renderer>();
    readonly List<Renderer> mine = new List<Renderer>();
    bool shownState = true, hookedUp;

    Vector3[] eyeScale, pupilScale, pupilBase, shineBase, browBase, cheekScale;
    Quaternion[] wobbleBase, twitchBase;
    Vector3[] wobbleAngle, wobbleVel;
    float[] twitchT, twitchNext;
    Vector3 rigScale, rigPos, lastPos, lastVel, accSmooth;

    Vector2 gaze, gazeVel;
    float nextBlink, blinkT = -1f, blinkLen, squash, nextYawn, nextGlance, glanceT;
    int face, pendingFace = -1;
    Emotion emotion;
    float emotionT, emotionLen;
    Emotion queued; float queuedLen;

    // the face's current mood (smoothed)
    float mOpen = 1f, mBrowY, mBrowTilt, mSmile, mMouthOpen, mMouthW, mCheek, mPupil = 1f;
    Mesh mouthMesh;
    Vector3[] mouthVerts;
    const int MouthSegs = 10;

    void Awake() => Init();

    bool initialised;

    /// <summary>Set up (Awake does this; editor previews call it on an instance that never woke).</summary>
    public void Init()
    {
        if (initialised) return;
        initialised = true;
        if (rig == null) rig = transform;
        rigScale = rig.localScale;
        rigPos = rig.localPosition;
        Cache(eyeWhites, out eyeScale);
        Cache(pupils, out pupilScale);
        pupilBase = Positions(pupils);
        shineBase = Positions(shines);
        browBase = Positions(brows);
        Cache(cheeks, out cheekScale);
        wobbleBase = Rotations(wobblers);
        wobbleAngle = new Vector3[wobblers.Length];
        wobbleVel = new Vector3[wobblers.Length];
        twitchBase = Rotations(twitchers);
        twitchT = new float[twitchers.Length];
        twitchNext = new float[twitchers.Length];
        for (int i = 0; i < twitchNext.Length; i++) { twitchT[i] = -1f; twitchNext[i] = Random.Range(2f, 6f); }
        BuildMouth();
        mOpen = eyeOpen; mBrowTilt = browTilt; mSmile = smile; mMouthW = mouthWidth; mCheek = cheekGlow;
        nextBlink = Random.Range(blinkEvery.x, blinkEvery.y);
        nextYawn = yawnEvery > 0f ? yawnEvery * Random.Range(0.6f, 1.2f) : -1f;
        nextGlance = Random.Range(2f, 5f);
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) mine.Add(r);
        ApplyFace(0);
        ApplyMood();
    }

    static void Cache(Transform[] t, out Vector3[] scales)
    {
        scales = new Vector3[t.Length];
        for (int i = 0; i < t.Length; i++) scales[i] = t[i] != null ? t[i].localScale : Vector3.one;
    }
    static Vector3[] Positions(Transform[] t)
    {
        var p = new Vector3[t.Length];
        for (int i = 0; i < t.Length; i++) p[i] = t[i] != null ? t[i].localPosition : Vector3.zero;
        return p;
    }
    static Quaternion[] Rotations(Transform[] t)
    {
        var q = new Quaternion[t.Length];
        for (int i = 0; i < t.Length; i++) q[i] = t[i] != null ? t[i].localRotation : Quaternion.identity;
        return q;
    }

    void Start()
    {
        runner = GetComponentInParent<CubeRailRunner>();
        lastPos = transform.position;
        if (runner == null) return;
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        coins = FindFirstObjectByType<CoinCollector>();
        cubeRenderer = transform.parent != null ? transform.parent.GetComponent<MeshRenderer>() : null;
        // 3.7's eye block steps aside for a character.
        foreach (PlayerTotem t in runner.GetComponentsInChildren<PlayerTotem>(true)) t.gameObject.SetActive(false);
        // Everything else drawn on the runner's cube (the cube itself, a hat, overlays) - not the trail or effects.
        if (transform.parent != null)
            foreach (MeshRenderer r in transform.parent.GetComponentsInChildren<MeshRenderer>(true))
                if (!r.transform.IsChildOf(transform)) hidden.Add(r);
        runner.Landed += OnLanded;
        runner.TapTaken += OnTap;
        if (gen != null) gen.CloseCallAwarded += OnCloseCall;
        if (coins != null) coins.CoinCollected += OnCoin;
        hookedUp = true;
    }

    void OnDestroy()
    {
        if (mouthMesh != null) Destroy(mouthMesh);
        foreach (Renderer r in hidden) if (r != null) r.forceRenderingOff = false;
        if (!hookedUp) return;
        if (runner != null) { runner.Landed -= OnLanded; runner.TapTaken -= OnTap; }
        if (gen != null) gen.CloseCallAwarded -= OnCloseCall;
        if (coins != null) coins.CoinCollected -= OnCoin;
    }

    void OnLanded() { squash = 1f; }
    void OnTap(int _) { Feel(Emotion.Effort, 0.22f); }
    void OnCloseCall(int a, int b, int c) { Feel(Emotion.Surprised, 0.35f); queued = Emotion.Happy; queuedLen = 0.8f; }
    void OnCoin(int n, Vector3 at) { if (emotion != Emotion.Surprised && emotion != Emotion.Hurt) Feel(Emotion.Happy, 0.45f); }

    void Feel(Emotion e, float seconds)
    {
        emotion = e; emotionT = 0f; emotionLen = seconds;
        queued = Emotion.None;
    }

    /// <summary>Pose the character for a still (shop icon): face front, rest mood, cloth in its rest shape.</summary>
    public void PoseStill()
    {
        Init();
        foreach (SoulCloth c in cloths) if (c != null) c.Init();
        ApplyFace(0);
        ApplyMood();
        foreach (SoulCloth c in cloths) if (c != null) c.Snap();
    }

    // ------------------------------------------------------------------ per frame

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (runner != null) Mirror();

        Vector3 p = transform.position;
        Vector3 vel = dt > 0f ? (p - lastPos) / dt : Vector3.zero;
        if (vel.sqrMagnitude > 2500f) vel = lastVel;   // a teleport, not motion
        Vector3 acc = dt > 0f ? (vel - lastVel) / dt : Vector3.zero;
        lastPos = p; lastVel = vel;
        accSmooth = Vector3.Lerp(accSmooth, Vector3.ClampMagnitude(acc, 400f), 1f - Mathf.Exp(-dt * 12f));

        ChooseFace();
        UpdateEmotion(dt);
        UpdateBlink(dt);
        UpdateMood(dt);
        UpdateGaze(dt, vel);
        UpdateBody(dt);
        UpdateWobble(dt, accSmooth);
        ApplyMood();
    }

    // In a run: the plain cube never draws; the character draws whenever the game would draw the cube.
    void Mirror()
    {
        foreach (Renderer r in hidden) if (r != null && !r.forceRenderingOff) r.forceRenderingOff = true;
        bool show = cubeRenderer == null || cubeRenderer.enabled;
        if (show == shownState) return;
        shownState = show;
        foreach (Renderer r in mine) if (r != null) r.enabled = show;
    }

    Camera ViewCam => viewCamera != null ? viewCamera : Camera.main;

    void ChooseFace()
    {
        if (!faceFollowsCamera || faceRoot == null) return;
        Camera cam = ViewCam;
        if (cam == null) return;
        if (runner != null && Vector3.Dot(transform.up, runner.transform.up) < 0.98f) return;   // mid-roll
        Transform frame = rig != null ? rig : transform;
        Vector3 toCam = cam.orthographic ? -cam.transform.forward : (cam.transform.position - frame.position).normalized;
        int best = face;
        float bestScore = Vector3.Dot(frame.TransformDirection(Faces[face]), toCam) + switchMargin;
        for (int i = 0; i < Faces.Length; i++)
        {
            float s = Vector3.Dot(frame.TransformDirection(Faces[i]), toCam);
            if (s > bestScore) { bestScore = s; best = i; }
        }
        if (best != face && pendingFace < 0)
        {
            pendingFace = best;
            if (blinkT < 0f) StartBlink(blinkSeconds);
        }
    }

    void ApplyFace(int i)
    {
        face = i;
        if (faceRoot != null) faceRoot.localRotation = Quaternion.LookRotation(Faces[i], Vector3.up);
    }

    void StartBlink(float len) { blinkT = 0f; blinkLen = len; }

    void UpdateBlink(float dt)
    {
        if (blinkT >= 0f)
        {
            blinkT += dt;
            float u = Mathf.Clamp01(blinkT / blinkLen);
            if (u >= 0.5f && pendingFace >= 0) { ApplyFace(pendingFace); pendingFace = -1; gaze = gazeVel = Vector2.zero; }
            if (u >= 1f)
            {
                blinkT = -1f;
                // now and then a quick double blink
                nextBlink = Random.value < 0.15f ? 0.12f : Random.Range(blinkEvery.x, blinkEvery.y);
            }
        }
        else if ((nextBlink -= dt) <= 0f)
            StartBlink(Random.value < slowBlinkChance ? blinkSeconds * 3.5f : blinkSeconds);
    }

    float BlinkClose => blinkT < 0f ? 0f : Mathf.Sin(Mathf.Clamp01(blinkT / blinkLen) * Mathf.PI);

    bool Idle => runner == null || runner.IsWaitingToStart;
    float Speed01 => runner != null && runner.IsRunning ? Mathf.InverseLerp(14f, 30f, runner.CurrentMoveSpeed) : 0f;

    void UpdateEmotion(float dt)
    {
        if (runner != null && runner.IsGameOver)
        {
            if (emotion != Emotion.Hurt) Feel(Emotion.Hurt, 999f);
            return;
        }
        if (emotion == Emotion.Hurt && runner != null && !runner.IsGameOver) emotion = Emotion.None;

        if (emotion != Emotion.None)
        {
            emotionT += dt;
            if (emotionT >= emotionLen)
            {
                if (queued != Emotion.None) { Emotion q = queued; queued = Emotion.None; emotion = q; emotionT = 0f; emotionLen = queuedLen; }
                else emotion = Emotion.None;
            }
        }
        if (yawnEvery > 0f && emotion == Emotion.None && (Idle || Speed01 < 0.2f))
        {
            if ((nextYawn -= dt) <= 0f)
            {
                Feel(Emotion.Yawn, 1.4f);
                nextYawn = yawnEvery * Random.Range(0.8f, 1.4f);
            }
        }
    }

    void UpdateMood(float dt)
    {
        float speed = Speed01;
        // rest: the personality, a little more determined (squinting, brows in) as the run gets fast
        float open = Mathf.Lerp(eyeOpen, Mathf.Max(eyeOpen, 0.9f) * 0.86f, speed);
        float tilt = browTilt + 10f * speed, browY = 0f, sm = smile, mo = 0f, mw = mouthWidth, ch = cheekGlow, pupil = 1f;
        float k = emotionLen > 0f ? Mathf.Clamp01(emotionT / emotionLen) : 0f;
        float env = Mathf.Sin(Mathf.Min(1f, k * 3f) * Mathf.PI * 0.5f);   // quick in, held
        switch (emotion)
        {
            case Emotion.Happy:
                open = Mathf.Lerp(open, 0.32f, env); sm = 1f; mo = 0.45f * env; mw = mouthWidth * 1.15f; ch = 1f; browY = 0.02f * env; tilt -= 6f * env;
                break;
            case Emotion.Effort:
                open = Mathf.Lerp(open, 0.55f, env); tilt += 12f * env; sm = Mathf.Lerp(sm, -0.1f, env); mw = mouthWidth * 0.8f;
                break;
            case Emotion.Surprised:
                open = 1.3f; pupil = 0.7f; browY = 0.05f; tilt = -10f; sm = 0f; mo = 1f; mw = mouthWidth * 0.5f; ch = cheekGlow * 0.5f;
                break;
            case Emotion.Hurt:
                open = 0.08f; tilt = -18f; browY = 0.02f; sm = -0.8f; mo = 0.35f; mw = mouthWidth * 0.9f; ch = 0.2f;
                break;
            case Emotion.Yawn:
                {
                    float y = Mathf.Sin(k * Mathf.PI);
                    open = Mathf.Lerp(open, 0.05f, Mathf.Clamp01(y * 1.6f)); mo = 1f * y; mw = mouthWidth * Mathf.Lerp(1f, 0.7f, y); sm = Mathf.Lerp(sm, 0f, y);
                    tilt -= 12f * y; browY = 0.025f * y;
                }
                break;
        }
        // glance at the player now and then while idle: a little brighter
        if (glanceT > 0f && emotion == Emotion.None) { ch = Mathf.Max(ch, cheekGlow * 1.2f); sm = Mathf.Max(sm, smile + 0.2f); }

        float rate = 1f - Mathf.Exp(-dt * (emotion == Emotion.Surprised ? 30f : 14f));
        mOpen = Mathf.Lerp(mOpen, open, rate);
        mBrowTilt = Mathf.Lerp(mBrowTilt, tilt, rate);
        mBrowY = Mathf.Lerp(mBrowY, browY, rate);
        mSmile = Mathf.Lerp(mSmile, sm, rate);
        mMouthOpen = Mathf.Lerp(mMouthOpen, mo, rate);
        mMouthW = Mathf.Lerp(mMouthW, mw, rate);
        mCheek = Mathf.Lerp(mCheek, ch, rate);
        mPupil = Mathf.Lerp(mPupil, pupil, rate);
    }

    void UpdateGaze(float dt, Vector3 vel)
    {
        Vector3 lookWorld = Vector3.zero;
        float amount = 0.6f;
        Camera cam = ViewCam;
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
            amount = Mathf.Lerp(0.6f, 1f, w);
        }
        else
        {
            // idle / shop: look where it is going, and every few seconds look at the player
            if ((nextGlance -= dt) <= 0f) { glanceT = Random.Range(0.9f, 1.6f); nextGlance = Random.Range(3f, 7f); }
            if (glanceT > 0f) glanceT -= dt;
            if (glanceT > 0f && cam != null) { lookWorld = cam.transform.position - transform.position; amount = 1f; }
            else if (vel.sqrMagnitude > 0.25f) lookWorld = vel;
            else lookWorld = transform.forward + transform.right * (Mathf.PerlinNoise(Time.time * 0.3f, 4f) - 0.5f) * 2f;
        }

        Vector2 target = Vector2.zero;
        if (faceRoot != null && lookWorld.sqrMagnitude > 1e-6f)
        {
            Vector3 l = faceRoot.InverseTransformDirection(lookWorld.normalized);
            Vector2 p = new Vector2(l.x, l.y);
            if (p.sqrMagnitude > 0.0025f) target = p.normalized * (maxPupilOffset * amount * Mathf.Clamp01(p.magnitude * 1.5f));
        }
        float t = Time.time;
        target += new Vector2(Mathf.PerlinNoise(t * 0.4f, 1.7f) - 0.5f, Mathf.PerlinNoise(3.1f, t * 0.4f) - 0.5f) * (0.25f * maxPupilOffset);
        gaze = Vector2.SmoothDamp(gaze, Vector2.ClampMagnitude(target, maxPupilOffset), ref gazeVel, 0.07f, Mathf.Infinity, dt);
    }

    void UpdateBody(float dt)
    {
        // landing squash (bottom stays on the rail) + idle breathing
        float s = 0f;
        if (squash > 0f)
        {
            squash = Mathf.Max(0f, squash - dt / 0.22f);
            s = landSquash * Mathf.Sin(squash * Mathf.PI) * squash;
        }
        float b = (Idle || (runner != null && !runner.IsRunning)) ? breathe * Mathf.Sin(Time.time * 2.4f) : breathe * 0.3f * Mathf.Sin(Time.time * 5f);
        if (emotion == Emotion.Yawn) b += 0.04f * Mathf.Sin(Mathf.Clamp01(emotionT / emotionLen) * Mathf.PI);
        if (emotion == Emotion.Surprised) b += 0.06f * Mathf.Sin(Mathf.Clamp01(emotionT / emotionLen) * Mathf.PI);
        float sy = 1f - s + b, sxz = 1f + s - b * 0.5f;
        rig.localScale = Vector3.Scale(rigScale, new Vector3(sxz, sy, sxz));
        rig.localPosition = rigPos + Vector3.up * ((sy - 1f) * 0.5f * rigScale.y);
    }

    void UpdateWobble(float dt, Vector3 worldAcc)
    {
        if (dt <= 0f) return;
        Vector3 a = transform.InverseTransformDirection(Vector3.ClampMagnitude(worldAcc, 400f));
        for (int i = 0; i < wobblers.Length; i++)
        {
            if (wobblers[i] == null) continue;
            // lag behind the body's acceleration: tip back when it speeds up, sideways in a turn
            Vector3 target = new Vector3(-a.z * 0.06f, 0f, a.x * 0.06f);
            target += new Vector3(Mathf.Sin(Time.time * 1.7f + i) * 1.5f, 0f, Mathf.Sin(Time.time * 1.3f + i * 2f) * 2f);
            target = Vector3.ClampMagnitude(target, 22f);
            Vector3 f = (target - wobbleAngle[i]) * 160f - wobbleVel[i] * 11f;
            wobbleVel[i] += f * dt;
            wobbleAngle[i] += wobbleVel[i] * dt;
            wobblers[i].localRotation = wobbleBase[i] * Quaternion.Euler(wobbleAngle[i]);
        }
        for (int i = 0; i < twitchers.Length; i++)
        {
            if (twitchers[i] == null) continue;
            if (twitchT[i] < 0f && (twitchNext[i] -= dt) <= 0f) { twitchT[i] = 0f; twitchNext[i] = Random.Range(2.5f, 7f); }
            float ang = 0f;
            if (twitchT[i] >= 0f)
            {
                twitchT[i] += dt;
                float u = twitchT[i] / 0.28f;
                ang = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI * 2f) * 16f * (1f - Mathf.Clamp01(u));
                if (u >= 1f) twitchT[i] = -1f;
            }
            twitchers[i].localRotation = twitchBase[i] * Quaternion.Euler(0f, 0f, ang * (i % 2 == 0 ? 1f : -1f));
        }
    }

    // ------------------------------------------------------------------ drawing the face

    void ApplyMood()
    {
        float open = Mathf.Max(0.04f, mOpen * (1f - 0.96f * BlinkClose));
        for (int i = 0; i < eyeWhites.Length; i++)
            if (eyeWhites[i] != null) eyeWhites[i].localScale = new Vector3(eyeScale[i].x, eyeScale[i].y, eyeScale[i].z * open);
        bool pupilsOn = open > 0.16f;
        for (int i = 0; i < pupils.Length; i++)
        {
            if (pupils[i] == null) continue;
            if (pupils[i].gameObject.activeSelf != pupilsOn) pupils[i].gameObject.SetActive(pupilsOn);
            // the pupil never pokes out of a half-closed eye
            float py = Mathf.Clamp(gaze.y, -PupilRoom(i, open), PupilRoom(i, open));
            pupils[i].localPosition = pupilBase[i] + new Vector3(gaze.x, py * 0.9f, 0f);
            pupils[i].localScale = new Vector3(pupilScale[i].x * mPupil, pupilScale[i].y, pupilScale[i].z * mPupil * Mathf.Min(1f, open * 1.3f));
        }
        for (int i = 0; i < shines.Length; i++)
        {
            if (shines[i] == null) continue;
            if (shines[i].gameObject.activeSelf != pupilsOn) shines[i].gameObject.SetActive(pupilsOn);
            int e = Mathf.Min(i, pupils.Length - 1);
            float room = e >= 0 ? PupilRoom(e, open) : 0f;
            shines[i].localPosition = shineBase[i] + new Vector3(gaze.x, Mathf.Clamp(gaze.y, -room, room) * 0.9f, 0f);
        }
        for (int i = 0; i < brows.Length; i++)
        {
            if (brows[i] == null) continue;
            float side = i == 0 ? 1f : -1f;   // left brow: + tilt turns its inner (right) end down
            brows[i].localPosition = browBase[i] + new Vector3(0f, mBrowY - 0.03f * (1f - Mathf.Min(1f, mOpen)), 0f);
            brows[i].localRotation = Quaternion.Euler(0f, 0f, -mBrowTilt * side);
        }
        for (int i = 0; i < cheeks.Length; i++)
            if (cheeks[i] != null) cheeks[i].localScale = cheekScale[i] * Mathf.Lerp(0.55f, 1.1f, Mathf.Clamp01(mCheek));
        DrawMouth();
    }

    // How far the pupil can slide up / down and stay inside eye i at this openness.
    float PupilRoom(int i, float open) =>
        eyeScale.Length == 0 ? 0f : Mathf.Max(0f, 0.5f * eyeScale[Mathf.Min(i, eyeScale.Length - 1)].z * open - 0.5f * pupilScale[i].z * mPupil);

    void BuildMouth()
    {
        if (mouth == null) return;
        mouthMesh = new Mesh { name = "SoulMouth" };
        mouthMesh.MarkDynamic();
        mouthVerts = new Vector3[(MouthSegs + 1) * 2];
        var tris = new int[MouthSegs * 6];
        for (int s = 0, t = 0; s < MouthSegs; s++)
        {
            int a = s * 2, b = a + 1, c = a + 2, d = a + 3;   // a/c upper, b/d lower
            tris[t++] = a; tris[t++] = c; tris[t++] = b;
            tris[t++] = c; tris[t++] = d; tris[t++] = b;
        }
        var normals = new Vector3[mouthVerts.Length];
        for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.forward;
        mouthMesh.vertices = mouthVerts;
        mouthMesh.normals = normals;
        mouthMesh.triangles = tris;
        mouthMesh.bounds = new Bounds(Vector3.zero, new Vector3(0.6f, 0.4f, 0.05f));
        mouth.sharedMesh = mouthMesh;
    }

    // A thick line whose ends lift with the smile; opening drops the lower lip into a "D".
    void DrawMouth()
    {
        if (mouthMesh == null) return;
        float w = mMouthW, thick = 0.02f;
        for (int s = 0; s <= MouthSegs; s++)
        {
            float t = s / (float)MouthSegs * 2f - 1f;
            float x = t * 0.5f * w;
            float yu = mSmile * 0.035f * (t * t) - 0.5f * mSmile * 0.035f;
            float drop = mMouthOpen * 0.085f * (1f - t * t) * Mathf.Lerp(1f, 1.25f, Mathf.Clamp01(-mSmile + 0.5f));
            float yl = yu - thick * (0.6f + 0.4f * (1f - t * t)) - drop;
            mouthVerts[s * 2] = new Vector3(x, yu, 0f);
            mouthVerts[s * 2 + 1] = new Vector3(x, yl, 0f);
        }
        mouthMesh.SetVertices(mouthVerts);
    }
}
