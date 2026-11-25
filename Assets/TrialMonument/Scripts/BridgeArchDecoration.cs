using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Purely decorative sag-curve under each bridge plank span (DynamicPathGeneratorBridge.cs) - the
/// "hanging arch" look from the reference Monument Valley bridge: a beam that starts at full size
/// where the plank begins, thins down toward the middle of the span, then thickens back to full
/// size where the plank ends. No pillars down to the ground - it only ever spans between the two
/// ends of one plank, floating the way everything else in this scene does.
///
/// Built once, right when the plank's cubes are laid (BuildBridgeArches, called from AppendSegment
/// straight after MakeBridgePlank), so - like the plank itself - it is always generated well ahead
/// of the runner, off screen. It is destroyed only when the whole leg is culled behind the camera
/// (DestroyBridgeArches, called from the same three spots DestroyDecoy is). To avoid it popping
/// into view before the deck above it has faded in, BridgeArchVisual hides its renderer until
/// PathDepthShading reports the anchor plank cube as substantially shown - the same threshold
/// BridgePlankGuard already uses for the plank's border.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Bridge Arch Decoration (sag curve under each plank)")]
    [Tooltip("Draw the decorative sag curve under every bridge plank span.")]
    public bool bridgeArchEnabled = true;
    [Tooltip("How far below the plank's underside the curve's ends sit, as a share of a cube.")]
    [Range(0.1f, 1.5f)] public float bridgeArchDrop = 0.6f;
    [Tooltip("Extra dip at the middle of the span, on top of bridgeArchDrop, as a share of the span's own length.")]
    [Range(0f, 0.6f)] public float bridgeArchSagFraction = 0.22f;
    [Tooltip("Cap on the extra middle dip, in cubes, so a very long plank doesn't sag absurdly far.")]
    [Range(1f, 15f)] public float bridgeArchMaxSag = 4f;
    [Tooltip("Thickness of the curve at its two ends, as a share of a cube - where it reads as a normal support beam.")]
    [Range(0.05f, 0.5f)] public float bridgeArchEndThickness = 0.22f;
    [Tooltip("Thickness of the curve at the middle of the span, as a share of a cube - thin, like a cable pulled taut.")]
    [Range(0.01f, 0.3f)] public float bridgeArchMidThickness = 0.06f;
    [Tooltip("How many straight segments the curve is built from. Higher = smoother.")]
    [Range(6, 40)] public int bridgeArchSegments = 20;
    [Tooltip("Colour the arch is drawn in.")]
    public Color bridgeArchColor = new Color(0.8f, 0.83f, 0.9f, 1f);
    [Tooltip("How much bridgeArchColor replaces the beam's own material colour. 0 = same colour as the rest of the path.")]
    [Range(0f, 1f)] public float bridgeArchColorStrength = 0.5f;

    private Material bridgeArchMaterial;
    private static readonly Vector3[] LocalSideNormal = { Vector3.up, Vector3.right, Vector3.down, Vector3.left };

    // One decorative curve per plank on this leg, spanning exactly that plank's cubes.
    void BuildBridgeArches(Segment seg)
    {
        if (!bridgeSections || !bridgeArchEnabled || bridgePlanks.Count == 0) return;

        foreach (var plank in bridgePlanks)
        {
            if (plank.first < 0 || plank.last >= seg.cubes.Count || plank.last < plank.first) continue;

            GameObject firstCubeGO = seg.cubes[plank.first];
            GameObject lastCubeGO = seg.cubes[plank.last];
            if (firstCubeGO == null || lastCubeGO == null) continue;

            GameObject arch = BuildOneArch(firstCubeGO, lastCubeGO, plank.side);
            if (arch == null) continue;

            seg.archObjects ??= new List<GameObject>();
            seg.archObjects.Add(arch);
        }
    }

    GameObject BuildOneArch(GameObject firstCubeGO, GameObject lastCubeGO, int side)
    {
        side = ((side % 4) + 4) % 4;
        Transform firstT = firstCubeGO.transform;
        Transform lastT = lastCubeGO.transform;

        // The plank's underside: the opposite direction from whichever face carries the walkway,
        // in world space, from the actual cube rotation - so this works whichever way the leg runs.
        Vector3 underDir = firstT.TransformDirection(-LocalSideNormal[side]).normalized;
        if (underDir.sqrMagnitude < 0.001f) underDir = Vector3.down;

        Vector3 chordStart = firstT.position;
        Vector3 chordEnd = lastT.position;
        Vector3 chord = chordEnd - chordStart;
        float span = chord.magnitude;
        if (span < 0.001f) return null;

        // The curve's plane is spanned by the direction of travel and the hang direction - the
        // width axis (the deck's other side-to-side axis) is perpendicular to both, so the beam's
        // cross-section stays flush with the plank above it at every point along the sag.
        Vector3 widthAxis = Vector3.Cross(chord.normalized, underDir).normalized;
        if (widthAxis.sqrMagnitude < 0.001f) widthAxis = Vector3.Cross(chord.normalized, Vector3.up).normalized;
        if (widthAxis.sqrMagnitude < 0.001f) widthAxis = Vector3.right;

        float drop = bridgeArchDrop * cubeSize;
        Vector3 ctrl0 = chordStart + underDir * drop;
        Vector3 ctrl2 = chordEnd + underDir * drop;
        float sag = Mathf.Min(bridgeArchMaxSag * cubeSize, span * bridgeArchSagFraction);
        Vector3 ctrl1 = (ctrl0 + ctrl2) * 0.5f + underDir * sag;

        Mesh mesh = BuildTaperedCurveMesh(ctrl0, ctrl1, ctrl2, widthAxis,
            bridgeArchEndThickness * cubeSize * 0.5f, bridgeArchMidThickness * cubeSize * 0.5f,
            Mathf.Max(2, bridgeArchSegments), out Vector3 anchor);

        var go = new GameObject("BridgeArch") { layer = firstCubeGO.layer };
        go.transform.SetParent(transform, false);
        go.transform.position = anchor;
        go.transform.rotation = Quaternion.identity;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = BridgeArchMaterial(firstCubeGO.GetComponent<Renderer>());
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mr.receiveShadows = true;
        mr.enabled = false; // BridgeArchVisual shows it once the deck above has faded in

        var visual = go.AddComponent<BridgeArchVisual>();
        visual.shading = GetComponent<PathDepthShading>();
        visual.anchorCube = firstCubeGO;

        return go;
    }

    // A copy of the cube's own material, tinted - built once and reused for every arch, the same
    // way PlankBorderMaterial is: no Shader.Find, no per-instance allocation, sorts and lights like
    // the track it hangs beneath.
    Material BridgeArchMaterial(Renderer source)
    {
        if (bridgeArchMaterial != null) return bridgeArchMaterial;
        if (source == null || source.sharedMaterial == null) return null;

        bridgeArchMaterial = new Material(source.sharedMaterial) { name = "BridgeArch (runtime)" };
        Color baseColor = bridgeArchMaterial.HasProperty("_BaseColor") ? bridgeArchMaterial.GetColor("_BaseColor") : Color.white;
        Color tinted = Color.Lerp(baseColor, bridgeArchColor, bridgeArchColorStrength);
        bridgeArchMaterial.SetColor("_BaseColor", tinted);
        bridgeArchMaterial.SetColor("_Color", tinted);
        return bridgeArchMaterial;
    }

    // A tapered beam of rectangular cross-section, following a quadratic Bezier through ctrl0/1/2:
    // half-thickness is `full` at t=0 and t=1, `thin` at t=0.5, eased by |cos(pi*t)| between them.
    // Vertices come back relative to ctrl0 (the returned anchor), so the mesh can sit on a GameObject
    // placed exactly there with an identity rotation.
    static Mesh BuildTaperedCurveMesh(Vector3 ctrl0, Vector3 ctrl1, Vector3 ctrl2, Vector3 widthAxis,
        float full, float thin, int segments, out Vector3 anchor)
    {
        anchor = ctrl0;

        Vector3 Bezier(float t)
        {
            float u = 1f - t;
            return u * u * ctrl0 + 2f * u * t * ctrl1 + t * t * ctrl2;
        }
        Vector3 Tangent(float t)
        {
            Vector3 d = 2f * (1f - t) * (ctrl1 - ctrl0) + 2f * t * (ctrl2 - ctrl1);
            return d.sqrMagnitude > 0.0001f ? d.normalized : (ctrl2 - ctrl0).normalized;
        }
        float HalfSize(float t) => thin + (full - thin) * Mathf.Abs(Mathf.Cos(Mathf.PI * t));

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uv = new List<Vector2>();
        var tris = new List<int>();

        // One ring of 4 corners per sample point along the curve.
        var rings = new Vector3[segments + 1][];
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            Vector3 c = Bezier(t) - anchor;
            Vector3 tangent = Tangent(t);
            Vector3 thicknessAxis = Vector3.Cross(tangent, widthAxis).normalized;
            float h = HalfSize(t);
            float w = HalfSize(t);

            rings[i] = new[]
            {
                c + widthAxis * w + thicknessAxis * h,   // 0: +width +thickness
                c - widthAxis * w + thicknessAxis * h,   // 1: -width +thickness
                c - widthAxis * w - thicknessAxis * h,   // 2: -width -thickness
                c + widthAxis * w - thicknessAxis * h,   // 3: +width -thickness
            };
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            int v = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
            tris.Add(v); tris.Add(v + 2); tris.Add(v + 1);
            tris.Add(v); tris.Add(v + 3); tris.Add(v + 2);
        }

        for (int i = 0; i < segments; i++)
        {
            Vector3[] a = rings[i];
            Vector3[] b = rings[i + 1];
            for (int s = 0; s < 4; s++)
            {
                int ns = (s + 1) % 4;
                Quad(a[s], b[s], b[ns], a[ns]);
            }
        }

        // End caps, so the beam doesn't look hollow from the side.
        Quad(rings[0][3], rings[0][2], rings[0][1], rings[0][0]);
        int last = segments;
        Quad(rings[last][0], rings[last][1], rings[last][2], rings[last][3]);

        var mesh = new Mesh { name = "BridgeArchCurve" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    void DestroyBridgeArches(Segment seg)
    {
        if (seg == null || seg.archObjects == null) return;
        foreach (GameObject go in seg.archObjects)
            if (go != null) Destroy(go);
        seg.archObjects = null;
    }

    void DestroyBridgeArchRuntime()
    {
        if (bridgeArchMaterial != null) Destroy(bridgeArchMaterial);
        bridgeArchMaterial = null;
    }
}

/// <summary>
/// Keeps one arch hidden until the plank cube it hangs from has faded substantially into view
/// (PathDepthShading.ShownAmount), the same threshold BridgePlankGuard uses for the plank's own
/// border - so the arch never pops into view ahead of the deck it belongs to.
/// </summary>
[DefaultExecutionOrder(60)]
public class BridgeArchVisual : MonoBehaviour
{
    public PathDepthShading shading;
    public GameObject anchorCube;

    Renderer rend;

    void Awake() => rend = GetComponent<Renderer>();

    void LateUpdate()
    {
        if (rend == null) return;
        bool show = shading == null || anchorCube == null || shading.ShownAmount(anchorCube) > 0.35f;
        if (rend.enabled != show) rend.enabled = show;
    }
}
