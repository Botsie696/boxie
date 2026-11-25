using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// THE POWER DIAMOND'S SOLID LOOK (powerSolidLook). The original diamond is a flat outline that
/// always faces the camera, floating off its face inside a wide halo - in the orthographic view
/// nothing ties it to a face, so the side it is on is guesswork. This look borrows what makes the
/// coins readable, plus the usual platformer cues for "which surface is this on":
///
///   * A solid faceted gem (crown + pavilion, flat shaded like the shield pickup and the totem
///     blocks) standing on the face exactly like a coin: its long axis is the face normal, it spins
///     about it, and its point nearly touches the face. Two-tone - pale crown, deep pavilion - so
///     which way it points, and so which way the face points, reads at a glance.
///   * A hard-edged contact shadow on the face right under it, the gem's own octagon outline turning
///     with it, cast along the face normal (not the sun) - as Mario Galaxy does on its round planets -
///     so it shows on every face, sunlit or not. It shrinks and darkens as the bob brings the gem closer.
///   * A crisp square frame lights up the face it is on, in the diamond's colour.
///   * Sized like a coin (a coin is a 1.1 disc, its top ~1.17 above the face): the same width, a
///     little taller - clearly special, never a different-scale object.
///   * No halo, no round glows: everything has straight edges and corners, like the track.
///
/// Drawing only: placement, pickup (powerGem.pos) and the guide line are unchanged. Off = the old look.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Power Diamond - Solid Look")]
    [Tooltip("Draw the diamond as a solid gem standing on its face, with a contact shadow and a frame on that face, instead of the flat camera-facing outline. Off = the original look.")]
    public bool powerSolidLook = false;
    [Tooltip("Gem width at its widest, in cubes (a coin is 1.1).")]
    [Range(0.3f, 1.6f)] public float powerGemWidth = 1f;
    [Tooltip("Gem height along the face normal, tip to table, in cubes.")]
    [Range(0.4f, 2f)] public float powerGemHeight = 1.3f;
    [Tooltip("Gap between the gem's point and the face, in cubes (before the bob).")]
    [Range(0f, 0.5f)] public float powerGemLift = 0.06f;
    [Tooltip("Seconds per full turn about the face normal.")]
    [Range(0.5f, 8f)] public float powerGemSpinSeconds = 2.6f;
    [Tooltip("Darkness of the contact shadow on the face.")]
    [Range(0f, 1f)] public float powerShadowStrength = 0.5f;
    [Tooltip("Contact shadow width as a share of the gem width.")]
    [Range(0.3f, 2f)] public float powerShadowSize = 0.65f;
    [Tooltip("Opacity of the square frame lit on the face the gem is on. 0 = none.")]
    [Range(0f, 1f)] public float powerFaceFrame = 0.85f;

    private MeshRenderer powerGemBody, powerShadow, powerFaceFrameR;
    private Material[] powerGemMats;
    private Material powerShadowMat, powerFrameMat;
    private Mesh powerGemMesh, powerShadowMesh;
    private static readonly int PowerFrameId = Shader.PropertyToID("_Frame");

    bool PowerLookReady()
    {
        if (powerGemBody != null) return true;
        if (powerRoot == null || powerQuad == null) return false;

        Material flat = Resources.Load<Material>("PowerUps/PowerUpFlat");
        Shader flatShader = flat != null ? flat.shader : Shader.Find("Custom/PowerUpFlat");
        Shader decal = Shader.Find("Custom/PowerGemDecal");
        if (flatShader == null || decal == null) return false;

        powerGemMesh = BuildPowerGemMesh();
        powerShadowMesh = BuildPowerShadowMesh();
        powerGemMats = new Material[2];
        for (int i = 0; i < 2; i++)
            powerGemMats[i] = flat != null ? new Material(flat) { name = "PowerGem" + i } : new Material(flatShader) { name = "PowerGem" + i };
        powerShadowMat = new Material(decal) { name = "PowerGemShadow" };
        powerShadowMat.SetFloat(PowerFrameId, 0f);
        powerFrameMat = new Material(decal) { name = "PowerGemFaceFrame" };
        powerFrameMat.SetFloat(PowerFrameId, 0.1f);

        powerGemBody = NewPowerRenderer("GemBody", powerGemMats[0]);
        powerGemBody.GetComponent<MeshFilter>().sharedMesh = powerGemMesh;
        powerGemBody.sharedMaterials = powerGemMats;
        powerShadow = NewPowerRenderer("GemShadow", powerShadowMat);
        powerShadow.GetComponent<MeshFilter>().sharedMesh = powerShadowMesh;
        powerFaceFrameR = NewPowerRenderer("GemFaceFrame", powerFrameMat);   // the shared quad, UV 0..1
        return true;
    }

    void HidePowerLook()
    {
        if (powerGemBody != null) powerGemBody.enabled = false;
        if (powerShadow != null) powerShadow.enabled = false;
        if (powerFaceFrameR != null) powerFaceFrameR.enabled = false;
    }

    void DestroyPowerLook()
    {
        if (powerGemMats != null) foreach (Material m in powerGemMats) if (m != null) Destroy(m);
        if (powerShadowMat != null) Destroy(powerShadowMat);
        if (powerFrameMat != null) Destroy(powerFrameMat);
        if (powerGemMesh != null) Destroy(powerGemMesh);
        if (powerShadowMesh != null) Destroy(powerShadowMesh);
        powerGemMats = null;
        powerShadowMat = powerFrameMat = null;
        powerGemMesh = powerShadowMesh = null;
        powerGemBody = powerShadow = powerFaceFrameR = null;
    }

    /// <summary>Draws the solid gem on its face. grow 0..1 follows the cube's reveal; bob is in world units along outward.</summary>
    void DrawPowerLook(PowerGem g, float grow, float bob, float beat, float parentScale)
    {
        if (!PowerLookReady()) return;

        Transform cubeT = g.cube.transform;
        Vector3 n = g.outward;
        Vector3 along = cubeT.forward;   // the leg's direction on this cube, always perpendicular to the face normal
        Vector3 faceCentre = cubeT.position + n * (cubeSize * 0.5f);
        float w = powerGemWidth * cubeSize;
        float h = powerGemHeight * cubeSize;

        // The shared bob (+-0.08 cubes) is gentled so the point never dips into the face.
        bob *= 0.6f;

        // The mesh's girdle is its origin, its point 0.7 of the height below: the point sits powerGemLift off the face.
        Vector3 girdle = faceCentre + n * (powerGemLift * cubeSize + 0.7f * h + bob);
        Quaternion spin = Quaternion.AngleAxis(powerClock * 360f / Mathf.Max(0.1f, powerGemSpinSeconds), n) * Quaternion.LookRotation(along, n);
        powerGemBody.enabled = true;
        powerGemBody.transform.SetPositionAndRotation(girdle, spin);
        powerGemBody.transform.localScale = new Vector3(w, h, w) * (grow / parentScale);

        // Two-tone, like a cut gem lit from above: a pale crown over a deep, saturated pavilion. The
        // light/dark split is what makes it read as a solid pointing one way rather than a flat blob.
        Color c = powerColor;
        powerGemMats[0].SetColor(GemColorId, Color.Lerp(c, Color.white, 0.3f) * 0.9f);   // kept under 1 so bloom doesn't flatten the facets
        powerGemMats[0].SetColor("_Rim", c * 0.6f);
        powerGemMats[1].SetColor(GemColorId, new Color(c.r * 0.25f, c.g * 0.6f, c.b * 0.8f, 1f));
        powerGemMats[1].SetColor("_Rim", c * 0.8f);

        // Contact shadow: the gem's own octagon, turning with it, flat on the face along its normal.
        // Closer (bob down) = smaller and darker.
        float near = Mathf.InverseLerp(0.05f * cubeSize, -0.05f * cubeSize, bob);
        float sw = w * powerShadowSize * Mathf.Lerp(1.06f, 0.94f, near) * grow / parentScale;
        powerShadow.enabled = powerShadowStrength > 0f;
        powerShadow.transform.SetPositionAndRotation(faceCentre + n * 0.012f, spin);
        powerShadow.transform.localScale = new Vector3(sw, 1f, sw);
        powerShadowMat.SetColor(GemColorId, new Color(0f, 0f, 0f, powerShadowStrength * Mathf.Lerp(0.8f, 1f, near) * grow));

        // A square frame round the face it is on, just inside the cube's edges.
        powerFaceFrameR.enabled = powerFaceFrame > 0f;
        powerFaceFrameR.transform.SetPositionAndRotation(faceCentre + n * 0.008f, Quaternion.LookRotation(n, along));
        powerFaceFrameR.transform.localScale = Vector3.one * (cubeSize * 0.9f * grow / parentScale);
        Color fc = c * 1.3f; fc.a = powerFaceFrame * Mathf.Lerp(0.7f, 1f, beat) * grow;
        powerFrameMat.SetColor(GemColorId, fc);
    }

    const int PowerGemSides = 8;

    // An eight-sided brilliant: flat table, crown down to the girdle (y = 0), pavilion to a point.
    // One unit wide, one unit tall (table at +0.3, point at -0.7); +Y is the face normal.
    // Every triangle has its own vertices so it shades flat. Submeshes: 0 crown + table, 1 pavilion.
    static Mesh BuildPowerGemMesh()
    {
        const float girdleR = 0.5f, tableR = 0.27f, tableY = 0.3f, pointY = -0.7f;
        var v = new List<Vector3>();
        var nrm = new List<Vector3>();
        var tris = new[] { new List<int>(), new List<int>() };
        Vector3 inside = new Vector3(0f, -0.1f, 0f);

        void Tri(int sub, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 nn = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(nn, (a + b + c) / 3f - inside) < 0f) { (b, c) = (c, b); nn = -nn; }
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            nrm.Add(nn); nrm.Add(nn); nrm.Add(nn);
            tris[sub].Add(i); tris[sub].Add(i + 1); tris[sub].Add(i + 2);
        }

        Vector3 table = new Vector3(0f, tableY, 0f), point = new Vector3(0f, pointY, 0f);
        for (int i = 0; i < PowerGemSides; i++)
        {
            Vector3 g0 = GemRing(girdleR, 0f, i), g1 = GemRing(girdleR, 0f, i + 1);
            Vector3 t0 = GemRing(tableR, tableY, i), t1 = GemRing(tableR, tableY, i + 1);
            Tri(0, table, t0, t1);       // table
            Tri(0, g0, g1, t1);          // crown facet (a quad as two triangles)
            Tri(0, g0, t1, t0);
            Tri(1, g0, g1, point);       // pavilion
        }

        var mesh = new Mesh { name = "PowerGem (procedural)", subMeshCount = 2 };
        mesh.SetVertices(v);
        mesh.SetNormals(nrm);
        mesh.SetTriangles(tris[0], 0);
        mesh.SetTriangles(tris[1], 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    // The gem's girdle outline as a flat filled octagon in the XZ plane (normal +Y), one unit across,
    // corners lined up with the gem's so the two turn together.
    static Mesh BuildPowerShadowMesh()
    {
        var v = new List<Vector3> { Vector3.zero };
        var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) };
        var t = new List<int>();
        for (int i = 0; i < PowerGemSides; i++)
        {
            Vector3 p = GemRing(0.5f, 0f, i);
            v.Add(p);
            uv.Add(new Vector2(p.x + 0.5f, p.z + 0.5f));
            t.Add(0); t.Add(1 + (i + 1) % PowerGemSides); t.Add(1 + i);
        }
        var mesh = new Mesh { name = "PowerGemShadow (procedural)" };
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Vector3 GemRing(float r, float y, int i)
    {
        float a = i * Mathf.PI * 2f / PowerGemSides;
        return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
    }
}
