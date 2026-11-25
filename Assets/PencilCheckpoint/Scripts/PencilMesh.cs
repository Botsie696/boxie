using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tiny flat-shaded mesh builder for the pencil checkpoint's props and cliffs (PencilCheckpoint/Prop shader): every face
/// has its own vertices (hard creases, so the pencil line pass outlines it), the surface kind goes in uv.x and the
/// material's darkness in vertex colour r.
/// </summary>
public class PencilMesh
{
    public const int Plaster = 0, Roof = 1, Foliage = 2, Wood = 3, Window = 4, Rock = 5, Stone = 6;

    readonly List<Vector3> v = new List<Vector3>();
    readonly List<Vector3> n = new List<Vector3>();
    readonly List<Color> c = new List<Color>();
    readonly List<Vector2> uv = new List<Vector2>();
    readonly List<int> t = new List<int>();

    public int VertexCount => v.Count;

    public void Tri(Vector3 a, Vector3 b, Vector3 d, int kind, float dark)
    {
        Vector3 nn = Vector3.Cross(b - a, d - a);
        if (nn.sqrMagnitude < 1e-12f) return;
        nn.Normalize();
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(d);
        var col = new Color(dark, 0f, 0f, 1f);
        for (int k = 0; k < 3; k++) { n.Add(nn); c.Add(col); uv.Add(new Vector2(kind, 0f)); }
        t.Add(i); t.Add(i + 1); t.Add(i + 2);
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, int kind, float dark)
    {
        Tri(a, b, cc, kind, dark);
        Tri(a, cc, d, kind, dark);
    }

    /// <summary>Box from its centre, half size and a yaw (degrees).</summary>
    public void Box(Vector3 centre, Vector3 half, float yaw, int kind, float dark, int topKind = -1, float topDark = -1f)
    {
        Quaternion r = Quaternion.Euler(0f, yaw, 0f);
        Vector3 P(float x, float y, float z) => centre + r * new Vector3(x * half.x, y * half.y, z * half.z);
        Vector3 a = P(-1, -1, -1), b = P(1, -1, -1), cc = P(1, -1, 1), d = P(-1, -1, 1);
        Vector3 e = P(-1, 1, -1), f = P(1, 1, -1), g = P(1, 1, 1), h = P(-1, 1, 1);
        int tk = topKind < 0 ? kind : topKind;
        float td = topDark < 0f ? dark : topDark;
        Quad(e, h, g, f, tk, td);          // top
        Quad(a, b, cc, d, kind, dark);     // bottom
        Quad(a, e, f, b, kind, dark);      // -z
        Quad(cc, g, h, d, kind, dark);     // +z
        Quad(d, h, e, a, kind, dark);      // -x
        Quad(b, f, g, cc, kind, dark);     // +x
    }

    /// <summary>A gable roof over a box footprint (half x along the ridge, half z across), eaves at y0, ridge at y1.</summary>
    public void Gable(Vector3 centre, float hx, float hz, float y0, float y1, float yaw, int kind, float dark, int gableKind, float gableDark)
    {
        Quaternion r = Quaternion.Euler(0f, yaw, 0f);
        Vector3 P(float x, float y, float z) => centre + r * new Vector3(x, y, z);
        Vector3 a = P(-hx, y0, -hz), b = P(hx, y0, -hz), cc = P(hx, y0, hz), d = P(-hx, y0, hz);
        Vector3 ra = P(-hx, y1, 0f), rb = P(hx, y1, 0f);
        Quad(a, ra, rb, b, kind, dark);
        Quad(cc, rb, ra, d, kind, dark);
        Tri(d, ra, a, gableKind, gableDark);
        Tri(b, rb, cc, gableKind, gableDark);
    }

    /// <summary>A prism / cone ring: sides between two rings of `sides` points (radius r0 at y0, r1 at y1), capped.</summary>
    public void Prism(Vector3 centre, int sides, float r0, float r1, float y0, float y1, float yaw, int kind, float dark, int capKind = -1, float capDark = -1f, float[] jitter = null)
    {
        var lo = new Vector3[sides];
        var hi = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = (yaw + 360f * i / sides) * Mathf.Deg2Rad;
            float j = jitter != null ? jitter[i % jitter.Length] : 1f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * j;
            lo[i] = centre + dir * r0 + Vector3.up * y0;
            hi[i] = centre + dir * r1 + Vector3.up * y1;
        }
        int ck = capKind < 0 ? kind : capKind;
        float cd = capDark < 0f ? dark : capDark;
        Vector3 top = centre + Vector3.up * y1;
        Vector3 bot = centre + Vector3.up * y0;
        for (int i = 0; i < sides; i++)
        {
            int k = (i + 1) % sides;
            if (r1 > 1e-4f) Quad(lo[i], hi[i], hi[k], lo[k], kind, dark);
            else Tri(lo[i], top, lo[k], kind, dark);
            if (r1 > 1e-4f) Tri(hi[i], top, hi[k], ck, cd);
            Tri(lo[k], bot, lo[i], kind, dark);
        }
    }

    /// <summary>A low-poly blob (octahedron subdivided once, jittered) - tree crowns and bushes.</summary>
    public void Blob(Vector3 centre, Vector3 radius, System.Random rng, float rough, int kind, float dark)
    {
        Vector3[] o = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        int[,] f = { { 0, 2, 4 }, { 4, 2, 1 }, { 1, 2, 5 }, { 5, 2, 0 }, { 4, 3, 0 }, { 1, 3, 4 }, { 5, 3, 1 }, { 0, 3, 5 } };
        var cache = new Dictionary<long, Vector3>();
        Vector3 J(Vector3 d)
        {
            d.Normalize();
            long key = Mathf.RoundToInt(d.x * 1000f) * 4000000L + Mathf.RoundToInt(d.y * 1000f) * 2000L + Mathf.RoundToInt(d.z * 1000f);
            if (!cache.TryGetValue(key, out Vector3 p))
            {
                float s = 1f + ((float)rng.NextDouble() - 0.5f) * 2f * rough;
                p = centre + Vector3.Scale(d * s, radius);
                cache[key] = p;
            }
            return p;
        }
        for (int i = 0; i < 8; i++)
        {
            Vector3 a = o[f[i, 0]], b = o[f[i, 1]], d = o[f[i, 2]];
            Vector3 ab = (a + b) * 0.5f, bd = (b + d) * 0.5f, da = (d + a) * 0.5f;
            Tri(J(a), J(ab), J(da), kind, dark);
            Tri(J(ab), J(b), J(bd), kind, dark);
            Tri(J(da), J(bd), J(d), kind, dark);
            Tri(J(ab), J(bd), J(da), kind, dark);
        }
    }

    public Mesh Build(string name)
    {
        var m = new Mesh { name = name };
        if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(v);
        m.SetNormals(n);
        m.SetColors(c);
        m.SetUVs(0, uv);
        m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }
}
