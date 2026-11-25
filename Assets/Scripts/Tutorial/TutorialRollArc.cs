using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The tutorial's world-space roll arc: a glowing ribbon that wraps round the beam's edge from the runner's face to
/// the face a tap rolls it onto, with an arrowhead at the end. One per screen half, so the left arc and the left half
/// of the screen pulse together (TutorialClock).
///
///   Roll90    one edge: radius arcRadius round the shared cube edge, swept 90 degrees.
///   Flip180   two edges, across the face in between: the double-tap flip.
///   TwoRolls  the same shape, lit one half after the other (two taps on the same side).
///
/// The meshes are built once in a canonical frame (current face = +Y, the face rolled onto = +X, beam = +Z, one cube
/// = 1) and only the transform changes: it rides along the beam with the runner, mirrored when the roll goes the
/// other way. Drawn twice with Graphics.DrawMesh (no GameObjects): depth-tested, then ZTest Greater at
/// arcBehindAlpha, so the part that goes behind the beam stays faintly visible. On the real tap the arc sweeps in
/// (Progress 0 -> 1 over arcSweepSeconds, the roll's own time) and then fades.
/// </summary>
public class TutorialRollArc : MonoBehaviour
{
    public enum Kind { Roll90, Flip180, TwoRolls }

    class Arc
    {
        public int side;
        public bool on;
        public Kind kind;
        public int fromFace = -1, midFace = -1;
        public float fade;
        public float sweepT = -1f;   // >= 0 while sweeping in after a real tap
        public float holdT = -1f;    // >= 0 while showing the finished sweep before fading
    }

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    static readonly int PulseId = Shader.PropertyToID("_Pulse");
    static readonly int ProgressId = Shader.PropertyToID("_Progress");
    static readonly int ModeId = Shader.PropertyToID("_Mode");
    static readonly int ZTestId = Shader.PropertyToID("_ZTest");

    CubeRailRunner runner;
    Mesh mesh90, mesh180;
    Material front, behind;
    MaterialPropertyBlock mpb;
    readonly Arc left = new Arc { side = -1 }, right = new Arc { side = 1 };
    int builtSegments = -1;
    float builtRadius, builtWidth, builtArrowW, builtArrowL;

    public void Init(CubeRailRunner r)
    {
        runner = r;
        TutorialStyle st = TutorialStyle.Active;
        Shader glow = st.WorldGlow;
        front = new Material(glow) { name = "TutorialArcFront" };
        front.SetFloat(ModeId, 0f);
        front.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.LessEqual);
        behind = new Material(glow) { name = "TutorialArcBehind" };
        behind.SetFloat(ModeId, 0f);
        behind.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.Greater);
        mpb = new MaterialPropertyBlock();
        mesh90 = new Mesh { name = "TutorialArc90" };
        mesh180 = new Mesh { name = "TutorialArc180" };
    }

    Arc For(int side) => side < 0 ? left : right;

    /// <summary>Shows (or hides) one side's arc: from face `fromFace` (the runner's) via `midFace` (the face that side's tap rolls onto).</summary>
    public void Show(int side, Kind kind, int fromFace, int midFace, bool on = true)
    {
        Arc a = For(side);
        if (on && (a.kind != kind || a.fromFace != fromFace || a.midFace != midFace)) { a.sweepT = -1f; a.holdT = -1f; }
        a.on = on && midFace >= 0;
        a.kind = kind;
        if (on) { a.fromFace = fromFace; a.midFace = midFace; }
    }

    public void Hide(int side) { For(side).on = false; }

    public void HideAll() { left.on = false; right.on = false; }

    /// <summary>The real tap: the arc sweeps in over the roll's time, then fades.</summary>
    public void Sweep(int side)
    {
        Arc a = For(side);
        if (a.midFace < 0) return;
        a.sweepT = 0f;
        a.holdT = -1f;
        a.fade = Mathf.Max(a.fade, 1f);
    }

    void OnDestroy()
    {
        if (front != null) Destroy(front);
        if (behind != null) Destroy(behind);
        if (mesh90 != null) Destroy(mesh90);
        if (mesh180 != null) Destroy(mesh180);
    }

    void LateUpdate()
    {
        if (runner == null || front == null) return;
        TutorialStyle st = TutorialStyle.Active;
        EnsureMeshes(st);
        float dt = Time.unscaledDeltaTime;
        Tick(left, st, dt);
        Tick(right, st, dt);
    }

    void Tick(Arc a, TutorialStyle st, float dt)
    {
        bool sweeping = a.sweepT >= 0f || a.holdT >= 0f;
        float progress = 1f;
        float pulse = TutorialUIController.Pulse(a.kind == Kind.Roll90 ? TutorialUIController.PromptKind.Tap :
                                                 a.kind == Kind.Flip180 ? TutorialUIController.PromptKind.DoubleTap : TutorialUIController.PromptKind.TwoTaps);
        if (a.sweepT >= 0f)
        {
            a.sweepT += dt / Mathf.Max(0.02f, st.arcSweepSeconds);
            progress = TutorialEase.OutCubic(a.sweepT);
            pulse = 1.3f;
            if (a.sweepT >= 1f) { a.sweepT = -1f; a.holdT = 0f; }
        }
        else if (a.holdT >= 0f)
        {
            a.holdT += dt / Mathf.Max(0.02f, st.arcFadeSeconds);
            pulse = 1.3f;
            if (a.holdT >= 1f) { a.holdT = -1f; a.on = false; a.fade = 0f; }
        }
        else if (a.kind == Kind.TwoRolls)
        {
            // Two taps, one after the other: the first half lights at the first press, the second half at the second.
            float s = TutorialClock.LoopSeconds;
            float c2 = TutorialUIController.ContactAt(TutorialUIController.PromptKind.TwoTaps, st);
            float c1 = c2 - st.doublePressGap;
            progress = s < c1 ? 0.04f : s < c2 ? 0.5f : 1f;
        }

        float vis;
        if (sweeping) vis = a.holdT >= 0f ? 1f - TutorialEase.InCubic(a.holdT) : 1f;
        else vis = TutorialEase.Fade(ref a.fade, a.on, st.appearSeconds, st.disappearSeconds, dt);
        if (vis <= 0.002f || a.midFace < 0 || a.fromFace < 0) return;

        Transform cube = runner.currentCube;
        if (cube == null) return;

        Vector3 f = cube.forward;
        Vector3 n0 = runner.SideNormal(a.fromFace);
        Vector3 n1 = runner.SideNormal(a.midFace);
        Quaternion rot = Quaternion.LookRotation(f, n0);
        float mirror = Vector3.Dot(n1, rot * Vector3.right) >= 0f ? 1f : -1f;
        float s3 = cube.lossyScale.x;
        Vector3 pos = cube.position + f * Vector3.Dot(runner.transform.position - cube.position, f);
        Matrix4x4 m = Matrix4x4.TRS(pos, rot, new Vector3(mirror * s3, s3, s3));
        Mesh mesh = a.kind == Kind.Roll90 ? mesh90 : mesh180;

        Color g = st.Guidance;
        mpb.Clear();
        mpb.SetColor(ColorId, g);
        mpb.SetFloat(IntensityId, st.arcIntensity);
        mpb.SetFloat(PulseId, pulse);
        mpb.SetFloat(ProgressId, progress * 1.06f);   // a touch past 1 so the arrowhead's tip is fully lit
        mpb.SetFloat(AlphaId, vis);
        Graphics.DrawMesh(mesh, m, front, 0, null, 0, mpb, false, false);
        mpb.SetFloat(AlphaId, vis * st.arcBehindAlpha);
        Graphics.DrawMesh(mesh, m, behind, 0, null, 0, mpb, false, false);
    }

    // ------------------------------------------------------------------ meshes (built once; rebuilt only if the style's geometry changes)

    void EnsureMeshes(TutorialStyle st)
    {
        if (builtSegments == st.arcSegments && builtRadius == st.arcRadius && builtWidth == st.arcWidth &&
            builtArrowW == st.arrowWidth && builtArrowL == st.arrowLength) return;
        builtSegments = st.arcSegments; builtRadius = st.arcRadius; builtWidth = st.arcWidth;
        builtArrowW = st.arrowWidth; builtArrowL = st.arrowLength;

        float R = st.arcRadius;
        int n = Mathf.Max(8, st.arcSegments);

        // 90: round the edge between +Y and +X.
        var pts = new List<Vector3>();
        var tans = new List<Vector3>();
        AddEdgeArc(pts, tans, new Vector3(0.5f, 0.5f, 0f), R, true, n);
        Build(mesh90, pts, tans, st);

        // 180: round the +Y/+X edge, across the +X face, round the +X/-Y edge.
        pts.Clear(); tans.Clear();
        int half = Mathf.Max(6, (n - 4) / 2);
        AddEdgeArc(pts, tans, new Vector3(0.5f, 0.5f, 0f), R, true, half);
        Vector3 a = new Vector3(0.5f + R, 0.5f, 0f), b = new Vector3(0.5f + R, -0.5f, 0f);
        for (int i = 1; i < 4; i++) { pts.Add(Vector3.Lerp(a, b, i / 4f)); tans.Add(Vector3.down); }
        AddEdgeArc(pts, tans, new Vector3(0.5f, -0.5f, 0f), R, false, half);
        Build(mesh180, pts, tans, st);
    }

    // first: from +Y round to +X about `edge`; otherwise from +X round to -Y.
    static void AddEdgeArc(List<Vector3> pts, List<Vector3> tans, Vector3 edge, float r, bool first, int segments)
    {
        for (int i = 0; i <= segments; i++)
        {
            if (!first && i == 0) continue;   // shares the straight run's end
            float t = i / (float)segments * Mathf.PI * 0.5f;
            if (first)
            {
                pts.Add(edge + new Vector3(Mathf.Sin(t), Mathf.Cos(t), 0f) * r);
                tans.Add(new Vector3(Mathf.Cos(t), -Mathf.Sin(t), 0f));
            }
            else
            {
                pts.Add(edge + new Vector3(Mathf.Cos(t), -Mathf.Sin(t), 0f) * r);
                tans.Add(new Vector3(-Mathf.Sin(t), -Mathf.Cos(t), 0f));
            }
        }
    }

    // A ribbon (width across the beam, +Z) along the points, UV.x = 0..1 by length, plus a flat arrowhead at the end.
    static void Build(Mesh mesh, List<Vector3> pts, List<Vector3> tans, TutorialStyle st)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        float total = 0f;
        var along = new float[pts.Count];
        for (int i = 1; i < pts.Count; i++) { total += Vector3.Distance(pts[i - 1], pts[i]); along[i] = total; }
        float hw = st.arcWidth * 0.5f;
        // The ribbon stops where the arrowhead begins, so the head carries the end of the sweep.
        for (int i = 0; i < pts.Count; i++)
        {
            float u = total > 0f ? along[i] / total : 0f;
            verts.Add(pts[i] + Vector3.back * hw); uvs.Add(new Vector2(u * 0.97f, 0f));
            verts.Add(pts[i] + Vector3.forward * hw); uvs.Add(new Vector2(u * 0.97f, 1f));
            if (i > 0)
            {
                int b = (i - 1) * 2;
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }
        }

        // Arrowhead: a flat triangle in the ribbon's plane, pointing along the last tangent. Its glow is drawn from the
        // UVs: a centre line (v = 0.5) to the tip, soft at the two base corners.
        Vector3 end = pts[pts.Count - 1];
        Vector3 tan = tans[tans.Count - 1].normalized;
        int h0 = verts.Count;
        verts.Add(end + Vector3.back * (st.arrowWidth * 0.5f)); uvs.Add(new Vector2(0.985f, 0.02f));
        verts.Add(end + Vector3.forward * (st.arrowWidth * 0.5f)); uvs.Add(new Vector2(0.985f, 0.98f));
        verts.Add(end + tan * st.arrowLength); uvs.Add(new Vector2(1f, 0.5f));
        verts.Add(end); uvs.Add(new Vector2(0.985f, 0.5f));
        tris.Add(h0); tris.Add(h0 + 3); tris.Add(h0 + 2);
        tris.Add(h0 + 3); tris.Add(h0 + 1); tris.Add(h0 + 2);

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }
}
