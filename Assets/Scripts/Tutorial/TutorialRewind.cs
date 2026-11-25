using System;
using UnityEngine;

/// <summary>
/// The tutorial's rewind. Every frame it records the runner's pose (and cube and face) and the camera rig's pose and
/// zoom into a fixed ring buffer (no allocations). After a miss, Begin plays that history backwards over a set number
/// of real seconds (the world is frozen meanwhile: TutorialManager sets timeScale 0), lands on the frame it was asked
/// for, puts the runner back on its cube and face in a clean "just placed" state, and eases the camera from the
/// rewound pose back to the rig.
///
/// Runs after the camera rig (DefaultExecutionOrder), so while it plays it has the last word on the camera.
/// </summary>
[DefaultExecutionOrder(10000)]
public class TutorialRewind : MonoBehaviour
{
    struct Frame
    {
        public float time;
        public Vector3 pos;
        public Quaternion rot;
        public Transform cube;
        public int side;
        public Vector3 camPos;
        public Quaternion camRot;
        public float ortho;
    }

    const int Capacity = 4096;
    readonly Frame[] frames = new Frame[Capacity];
    int head = -1, count;

    CubeRailRunner runner;
    Rigidbody rb;
    Transform rigT;
    Camera cam;

    public bool Recording = true;
    public bool Playing { get; private set; }

    float playT, playSeconds, fromTime, toTime;
    int toIndexBack;   // how many frames back from head the target frame is
    Action onDone;

    float blendT = -1f;
    Vector3 blendPos;
    Quaternion blendRot;
    float blendOrtho;

    public void Init(CubeRailRunner r, CameraParentRotator rig)
    {
        runner = r;
        rb = r.GetComponent<Rigidbody>();
        rigT = rig != null ? rig.transform : null;
        cam = rig != null && rig.boomCamera != null ? rig.boomCamera : Camera.main;
    }

    public void Clear() { head = -1; count = 0; }

    /// <summary>Drops a rewind in progress (the run is being rebuilt under it); the history is cleared.</summary>
    public void Cancel()
    {
        Playing = false;
        onDone = null;
        blendT = -1f;
        Clear();
    }

    Frame At(int back) => frames[((head - back) % Capacity + Capacity) % Capacity];

    void LateUpdate()
    {
        if (runner == null) return;
        if (TimeScaleMixer.Get(TimeScaleMixer.Source.Pause) <= 0.0001f) return;   // the pause screen holds the rewind too
        if (Playing) { StepPlayback(); return; }
        if (blendT >= 0f) StepBlend();
        if (Recording) Record();
    }

    void Record()
    {
        if (runner.currentCube == null || runner.IsGameOver) return;
        head = (head + 1) % Capacity;
        count = Mathf.Min(count + 1, Capacity);
        frames[head] = new Frame
        {
            time = Time.time,
            pos = runner.transform.position,
            rot = runner.transform.rotation,
            cube = runner.currentCube,
            side = runner.currentSideIndex,
            camPos = rigT != null ? rigT.position : Vector3.zero,
            camRot = rigT != null ? rigT.rotation : Quaternion.identity,
            ortho = cam != null ? cam.orthographicSize : 0f
        };
    }

    /// <summary>
    /// Plays the history back to the newest frame at or before `gameTime` (or the oldest kept, if the history is
    /// shorter) over `seconds` real seconds, then calls onDone. False if there is no history.
    /// </summary>
    public bool Begin(float gameTime, float seconds, Action done, Transform stayOn = null)
    {
        if (count == 0 || Playing) return false;
        gameTime = Mathf.Max(gameTime, At(0).time - TutorialStyle.Active.recordSeconds);   // only the last recordSeconds are kept for this
        int back = 0;
        while (back < count - 1 && (At(back).time > gameTime || At(back).cube == null))
        {
            // Never back past the landing on the leg it missed on (a short leg would otherwise send it back round
            // the corner before, which the player already made).
            if (stayOn != null && At(back).cube == stayOn && At(back + 1).cube != stayOn) break;
            back++;
        }
        if (At(back).cube == null) return false;

        toIndexBack = back;
        fromTime = At(0).time;
        toTime = At(back).time;
        playSeconds = Mathf.Max(0.05f, seconds);
        playT = 0f;
        onDone = done;
        Playing = true;
        blendT = -1f;
        if (rb != null) rb.isKinematic = true;
        return true;
    }

    void StepPlayback()
    {
        playT += Time.unscaledDeltaTime / playSeconds;
        float u = TutorialEase.InOutCubic(playT);
        float t = Mathf.Lerp(fromTime, toTime, u);

        // Frames (newest first) around time t.
        int b = 0;
        while (b < toIndexBack && At(b + 1).time >= t) b++;
        Frame a = At(b);
        Frame c = At(Mathf.Min(b + 1, toIndexBack));
        float k = a.time > c.time ? Mathf.InverseLerp(a.time, c.time, t) : 0f;
        Vector3 pos = Vector3.Lerp(a.pos, c.pos, k);
        Quaternion rot = Quaternion.Slerp(a.rot, c.rot, k);
        runner.transform.SetPositionAndRotation(pos, rot);
        if (rb != null) { rb.position = pos; rb.rotation = rot; }
        if (rigT != null) rigT.SetPositionAndRotation(Vector3.Lerp(a.camPos, c.camPos, k), Quaternion.Slerp(a.camRot, c.camRot, k));
        if (cam != null && a.ortho > 0f) cam.orthographicSize = Mathf.Lerp(a.ortho, c.ortho, k);

        if (playT >= 1f) Finish();
    }

    void Finish()
    {
        Frame f = At(toIndexBack);
        Playing = false;

        // Back on the cube and face it was on, still, as a fresh placement.
        runner.currentCube = f.cube;
        runner.currentSideIndex = f.side;
        runner.isFlying = false;
        runner.transform.SetPositionAndRotation(f.pos, f.rot);
        if (rb != null)
        {
            rb.position = f.pos;
            rb.rotation = f.rot;
        }
        runner.ResetForTutorial();
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        Physics.SyncTransforms();
        foreach (TrailRenderer tr in runner.GetComponentsInChildren<TrailRenderer>()) tr.Clear();

        // The history after the landing frame is the future that did not happen.
        head = ((head - toIndexBack) % Capacity + Capacity) % Capacity;
        count = Mathf.Max(1, count - toIndexBack);

        // Ease the camera from where the rewind left it back to the rig's own pose.
        if (rigT != null)
        {
            blendPos = f.camPos;
            blendRot = f.camRot;
            blendOrtho = f.ortho;
            blendT = 0f;
            rigT.SetPositionAndRotation(blendPos, blendRot);
            if (cam != null && blendOrtho > 0f) cam.orthographicSize = blendOrtho;
        }

        Action done = onDone;
        onDone = null;
        done?.Invoke();
    }

    void StepBlend()
    {
        float secs = Mathf.Max(0.05f, TutorialStyle.Active.rewindCameraBlend);
        blendT += Time.unscaledDeltaTime / secs;
        if (blendT >= 1f || rigT == null) { blendT = -1f; return; }
        float e = TutorialEase.OutCubic(blendT);
        // The rig has already written its pose this frame (it runs first): lean from the rewound pose toward it.
        rigT.SetPositionAndRotation(Vector3.Lerp(blendPos, rigT.position, e), Quaternion.Slerp(blendRot, rigT.rotation, e));
        if (cam != null && blendOrtho > 0f) cam.orthographicSize = Mathf.Lerp(blendOrtho, cam.orthographicSize, e);
    }
}
