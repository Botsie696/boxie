using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A see-through copy of the runner, sat on the face it should roll onto, keeping pace with it along the leg: "be
/// here". Only the runner's meshes are copied (no scripts, colliders or rigidbody), and they are drawn with
/// Graphics.DrawMesh in the unlit fresnel material - nothing is instantiated. Seated with the runner's own maths
/// (face normal * 1.05 cube units, same progress along the cube). Fades out with a small flash once the runner gets
/// there.
/// </summary>
public class TutorialGhostRunner : MonoBehaviour
{
    struct Part { public Mesh mesh; public int subMeshes; public Matrix4x4 rel; }

    static readonly int BaseId = Shader.PropertyToID("_BaseColor");
    static readonly int RimId = Shader.PropertyToID("_RimColor");
    static readonly int PulseId = Shader.PropertyToID("_Pulse");
    static readonly int FadeId = Shader.PropertyToID("_Fade");

    CubeRailRunner runner;
    Material mat;
    MaterialPropertyBlock mpb;
    readonly List<Part> parts = new List<Part>();
    bool captured, on;
    int face = -1;
    float fade, flashT = -1f;

    public void Init(CubeRailRunner r)
    {
        runner = r;
        mat = new Material(TutorialStyle.Active.Ghost) { name = "TutorialGhost" };
        mpb = new MaterialPropertyBlock();
    }

    /// <summary>Shows the ghost on `targetFace` (0 top, 1 right, 2 bottom, 3 left of the runner's cube).</summary>
    public void Show(int targetFace)
    {
        if (targetFace < 0) { Hide(); return; }
        face = targetFace;
        on = true;
        if (!captured && !runner.IsRevolving) Capture();
    }

    public void Hide() { on = false; }

    /// <summary>The runner reached the face: a small flash, then gone.</summary>
    public void Flash()
    {
        if (fade <= 0.01f) return;
        flashT = 0f;
        on = false;
    }

    // The runner's visible meshes, relative to its root, taken once while it sits still on a face.
    void Capture()
    {
        parts.Clear();
        Matrix4x4 toRoot = runner.transform.worldToLocalMatrix;
        // The runner's body: the biggest visible mesh (FX shells and trails hang off the runner too).
        MeshFilter body = null;
        float bodySize = 0f;
        foreach (MeshFilter mf in runner.GetComponentsInChildren<MeshFilter>())
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || !mr.enabled || mf.sharedMesh == null) continue;
            Vector3 e = mr.bounds.size;
            float size = e.x * e.y * e.z;
            if (body == null || size > bodySize) { body = mf; bodySize = size; }
        }
        if (body != null)
            parts.Add(new Part { mesh = body.sharedMesh, subMeshes = Mathf.Max(1, body.sharedMesh.subMeshCount), rel = toRoot * body.transform.localToWorldMatrix });
        captured = parts.Count > 0;
    }

    void OnDestroy() { if (mat != null) Destroy(mat); }

    void LateUpdate()
    {
        if (runner == null || mat == null) return;
        TutorialStyle st = TutorialStyle.Active;
        float dt = Time.unscaledDeltaTime;
        float vis = TutorialEase.Fade(ref fade, on, st.appearSeconds, st.disappearSeconds, dt);
        float flash = 0f;
        float grow = 1f;
        if (flashT >= 0f)
        {
            flashT += dt / Mathf.Max(0.05f, st.popSeconds);
            if (flashT >= 1f) { flashT = -1f; fade = 0f; return; }
            flash = Mathf.Sin(flashT * Mathf.PI);
            grow = Mathf.LerpUnclamped(1f, st.popOvershoot, TutorialEase.OutBack(flashT, st.popOvershoot) * (1f - flashT));
            vis = 1f - TutorialEase.InCubic(flashT);
        }
        if (vis <= 0.002f || face < 0) return;
        if (!captured) { if (!runner.IsRevolving) Capture(); if (!captured) return; }

        Transform cube = runner.currentCube;
        if (cube == null) return;

        // The runner's own seat for that face (MoveAndSnap): 0.5 + 0.5 + 0.05 cube units out, same progress along the cube.
        Vector3 up = face == 1 ? Vector3.right : face == 2 ? Vector3.down : face == 3 ? Vector3.left : Vector3.up;
        Vector3 lp = up * 1.05f;
        lp.z = cube.InverseTransformPoint(runner.transform.position).z;
        Vector3 pos = cube.TransformPoint(lp);
        Quaternion rot = Quaternion.LookRotation(cube.forward, cube.TransformDirection(up));
        Matrix4x4 seat = Matrix4x4.TRS(pos, rot, runner.transform.lossyScale * grow);

        Color baseC = st.neutralUI; baseC.a = st.ghostBaseAlpha;
        Color rimC = st.Guidance; rimC.a = st.ghostRimAlpha;
        if (flash > 0f) rimC = Color.Lerp(rimC, st.Success, flash);
        mpb.Clear();
        mpb.SetColor(BaseId, baseC);
        mpb.SetColor(RimId, rimC);
        mpb.SetFloat(PulseId, Mathf.Lerp(0.75f, 1.1f, TutorialUIController.Pulse(TutorialUIController.PromptKind.Tap)) * (1f + flash * 0.8f));
        mpb.SetFloat(FadeId, vis);

        foreach (Part p in parts)
        {
            Matrix4x4 m = seat * p.rel;
            for (int s = 0; s < p.subMeshes; s++)
                Graphics.DrawMesh(p.mesh, m, mat, 0, null, s, mpb, false, false);
        }
    }
}
