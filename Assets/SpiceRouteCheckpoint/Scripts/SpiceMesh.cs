using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Flat-shaded mesh builder for the Spice Route checkpoint's cliffs and props (SpiceRouteCheckpoint/Prop shader): every
/// face has its own vertices (hard creases), the painted colour (sRGB) goes in the vertex colour, the surface kind in uv.x
/// and a per-piece seed in uv.y. Lathes make most of the architecture (shikharas, domes, onion domes, minarets, pots).
/// </summary>
public class SpiceMesh
{
    public const int Paint = 0, Gold = 1, Foliage = 2, Glow = 3, Carved = 4, Rock = 5, Tiles = 6, Marble = 7, Fabric = 8, Feather = 9;

    readonly List<Vector3> v = new List<Vector3>();
    readonly List<Vector3> n = new List<Vector3>();
    readonly List<Color> c = new List<Color>();
    readonly List<Vector2> uv = new List<Vector2>();
    readonly List<int> t = new List<int>();

    public float seed;
    public int VertexCount => v.Count;

    public Bounds Bounds
    {
        get
        {
            if (v.Count == 0) return new Bounds();
            var b = new Bounds(v[0], Vector3.zero);
            for (int i = 1; i < v.Count; i++) b.Encapsulate(v[i]);
            return b;
        }
    }

    public void Tri(Vector3 a, Vector3 b, Vector3 d, Color col, int kind)
    {
        Vector3 nn = Vector3.Cross(b - a, d - a);
        if (nn.sqrMagnitude < 1e-14f) return;
        nn.Normalize();
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(d);
        for (int k = 0; k < 3; k++) { n.Add(nn); c.Add(col); uv.Add(new Vector2(kind, seed)); }
        t.Add(i); t.Add(i + 1); t.Add(i + 2);
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col, int kind)
    {
        Tri(a, b, cc, col, kind);
        Tri(a, cc, d, col, kind);
    }

    /// <summary>Box from its centre, half size and a yaw (degrees). The top may have its own colour / kind.</summary>
    public void Box(Vector3 centre, Vector3 half, float yaw, Color col, int kind, Color? topCol = null, int topKind = -1)
    {
        Quaternion r = Quaternion.Euler(0f, yaw, 0f);
        Vector3 P(float x, float y, float z) => centre + r * new Vector3(x * half.x, y * half.y, z * half.z);
        Vector3 a = P(-1, -1, -1), b = P(1, -1, -1), cc = P(1, -1, 1), d = P(-1, -1, 1);
        Vector3 e = P(-1, 1, -1), f = P(1, 1, -1), g = P(1, 1, 1), h = P(-1, 1, 1);
        Quad(e, h, g, f, topCol ?? col, topKind < 0 ? kind : topKind);
        Quad(a, b, cc, d, col, kind);
        Quad(a, e, f, b, col, kind);
        Quad(cc, g, h, d, col, kind);
        Quad(d, h, e, a, col, kind);
        Quad(b, f, g, cc, col, kind);
    }

    /// <summary>A surface of revolution: rings of `sides` points at heights y[i] with radii r[i] (r = 0 = a point).
    /// ribs (optional) multiplies the radius per side (vertical ribs / petals); squash scales z.</summary>
    public void Lathe(Vector3 centre, float[] r, float[] y, int sides, float yaw, Color col, int kind, bool capBottom = true,
                      float[] ribs = null, float squash = 1f, Color? capCol = null, int capKind = -1)
    {
        int rings = r.Length;
        var p = new Vector3[rings, sides];
        for (int i = 0; i < rings; i++)
            for (int s = 0; s < sides; s++)
            {
                float a = (yaw + 360f * s / sides) * Mathf.Deg2Rad;
                float m = ribs != null ? ribs[s % ribs.Length] : 1f;
                p[i, s] = centre + new Vector3(Mathf.Cos(a) * r[i] * m, y[i], Mathf.Sin(a) * r[i] * m * squash);
            }
        for (int i = 0; i < rings - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int k = (s + 1) % sides;
                if (r[i + 1] < 1e-5f) Tri(p[i, s], p[i + 1, s], p[i, k], col, kind);
                else if (r[i] < 1e-5f) Tri(p[i, s], p[i + 1, s], p[i + 1, k], col, kind);
                else Quad(p[i, s], p[i + 1, s], p[i + 1, k], p[i, k], col, kind);
            }
        Color cc = capCol ?? col;
        int ck = capKind < 0 ? kind : capKind;
        if (r[rings - 1] > 1e-5f)
        {
            Vector3 top = centre + Vector3.up * y[rings - 1];
            for (int s = 0; s < sides; s++) Tri(p[rings - 1, s], top, p[rings - 1, (s + 1) % sides], cc, ck);
        }
        if (capBottom && r[0] > 1e-5f)
        {
            Vector3 bot = centre + Vector3.up * y[0];
            for (int s = 0; s < sides; s++) Tri(p[0, (s + 1) % sides], bot, p[0, s], col, kind);
        }
    }

    /// <summary>A prism / frustum: radius r0 at y0 to r1 at y1.</summary>
    public void Prism(Vector3 centre, int sides, float r0, float r1, float y0, float y1, float yaw, Color col, int kind, float[] ribs = null, Color? capCol = null, int capKind = -1)
        => Lathe(centre, new[] { r0, r1 }, new[] { y0, y1 }, sides, yaw, col, kind, true, ribs, 1f, capCol, capKind);

    /// <summary>A dome on a base ring of radius rad at y0: hemisphere (onion 0) or an onion swelling out then to a point.</summary>
    public void Dome(Vector3 centre, float rad, float y0, float height, int sides, Color col, int kind, float onion = 0f, int rings = 6)
    {
        var r = new float[rings + 1];
        var y = new float[rings + 1];
        for (int i = 0; i <= rings; i++)
        {
            float f = i / (float)rings;
            float hemi = Mathf.Sqrt(Mathf.Max(0f, 1f - f * f));
            float bulge = onion > 0f ? (1f + onion * Mathf.Sin(Mathf.Clamp01(f * 1.6f) * Mathf.PI)) * Mathf.Pow(1f - f, 0.9f) : hemi;
            r[i] = rad * (i == rings ? 0f : bulge);
            y[i] = y0 + height * f;
        }
        Lathe(centre, r, y, sides, 0f, col, kind, false);
    }

    /// <summary>A tube through points with per-point radii (trunks, arms, minarets that lean, tusks).</summary>
    public void Tube(Vector3[] pts, float[] radii, int sides, Color col, int kind, bool capEnd = true)
    {
        int m = pts.Length;
        var rings = new Vector3[m, sides];
        Vector3 prevUp = Vector3.up;
        for (int i = 0; i < m; i++)
        {
            Vector3 dir = i < m - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1];
            dir.Normalize();
            Vector3 side = Vector3.Cross(dir, Mathf.Abs(Vector3.Dot(dir, prevUp)) > 0.95f ? Vector3.right : prevUp).normalized;
            Vector3 up = Vector3.Cross(side, dir).normalized;
            prevUp = up;
            for (int s = 0; s < sides; s++)
            {
                float a = 2f * Mathf.PI * s / sides;
                rings[i, s] = pts[i] + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * radii[i];
            }
        }
        for (int i = 0; i < m - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int k = (s + 1) % sides;
                Quad(rings[i, s], rings[i + 1, s], rings[i + 1, k], rings[i, k], col, kind);
            }
        if (capEnd)
            for (int s = 0; s < sides; s++) Tri(rings[m - 1, s], pts[m - 1], rings[m - 1, (s + 1) % sides], col, kind);
    }

    /// <summary>A low-poly blob (octahedron subdivided once, jittered): bellies, heads, crowns of trees, bushes.</summary>
    public void Blob(Vector3 centre, Vector3 radius, System.Random rng, float rough, Color col, int kind, Quaternion? rot = null)
    {
        Quaternion q = rot ?? Quaternion.identity;
        Vector3[] o = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        int[,] f = { { 0, 2, 4 }, { 4, 2, 1 }, { 1, 2, 5 }, { 5, 2, 0 }, { 4, 3, 0 }, { 1, 3, 4 }, { 5, 3, 1 }, { 0, 3, 5 } };
        var cache = new Dictionary<long, Vector3>();
        Vector3 J(Vector3 d)
        {
            d.Normalize();
            long key = Mathf.RoundToInt(d.x * 1000f) * 4000000L + Mathf.RoundToInt(d.y * 1000f) * 2000L + Mathf.RoundToInt(d.z * 1000f);
            if (!cache.TryGetValue(key, out Vector3 p))
            {
                float s = 1f + ((float)(rng?.NextDouble() ?? 0.5) - 0.5f) * 2f * rough;
                p = centre + q * Vector3.Scale(d * s, radius);
                cache[key] = p;
            }
            return p;
        }
        for (int i = 0; i < 8; i++)
        {
            Vector3 a = o[f[i, 0]], b = o[f[i, 1]], d = o[f[i, 2]];
            Vector3 ab = (a + b) * 0.5f, bd = (b + d) * 0.5f, da = (d + a) * 0.5f;
            Tri(J(a), J(ab), J(da), col, kind);
            Tri(J(ab), J(b), J(bd), col, kind);
            Tri(J(da), J(bd), J(d), col, kind);
            Tri(J(ab), J(bd), J(da), col, kind);
        }
    }

    /// <summary>Copies another kit's triangles in, moved by a matrix (normals turned with it).</summary>
    public void Add(SpiceMesh other, Matrix4x4 m)
    {
        int i0 = v.Count;
        for (int i = 0; i < other.v.Count; i++)
        {
            v.Add(m.MultiplyPoint3x4(other.v[i]));
            n.Add(m.MultiplyVector(other.n[i]).normalized);
            c.Add(other.c[i]);
            uv.Add(other.uv[i]);
        }
        foreach (int k in other.t) t.Add(i0 + k);
    }

    public Mesh Build(string name, float reserveTop = 0f)
    {
        var m = new Mesh { name = name };
        if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(v);
        m.SetNormals(n);
        m.SetColors(c);
        m.SetUVs(0, uv);
        m.SetTriangles(t, 0);
        m.RecalculateBounds();
        if (reserveTop > 0f)
        {
            // Room for what will stand on the top, so the background generator's sight-line guard keeps it clear too.
            Bounds b = m.bounds;
            b.Encapsulate(new Vector3(b.center.x, b.max.y + reserveTop, b.center.z));
            m.bounds = b;
        }
        return m;
    }

    public static Color H(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color col); return col; }
}
