using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural low-poly heater shield, built once and cached. The crest is flat on top with sides that
/// curve to a point at the bottom, extruded 0.12 thick; its rim is a bevelled band round the front
/// (submesh 1) and a small raised cube sits in the middle as an emblem (submesh 2, ties it to the game's
/// cubes). Every triangle has its own vertices, so it shades flat like the totem blocks.
///
/// The mesh is one unit tall-ish and faces +Z with its up on +Y; the pickup stands it on a face with +Y
/// along the face normal and spins it about that axis.
/// Submeshes: 0 body, 1 rim, 2 emblem.
/// </summary>
public static class ShieldMeshBuilder
{
    static Mesh cached;
    public static Mesh Mesh => cached != null ? cached : (cached = Build());

    // Optional: assign an imported mesh to replace the procedural one.
    public static Mesh Override;

    class Builder
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<int>[] tris = { new List<int>(), new List<int>(), new List<int>() };

        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 nn = Vector3.Cross(b - a, c - a).normalized;
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            n.Add(nn); n.Add(nn); n.Add(nn);
            tris[sub].Add(i); tris[sub].Add(i + 1); tris[sub].Add(i + 2);
        }

        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Tri(sub, a, b, c);
            Tri(sub, a, c, d);
        }
    }

    static Mesh Build()
    {
        if (Override != null) return Override;

        // Outline, clockwise seen from +Z: flat top, straight sides, then a curve to the point.
        var o = new List<Vector2>();
        o.Add(new Vector2(-0.5f, 0.55f));
        o.Add(new Vector2(0.5f, 0.55f));
        o.Add(new Vector2(0.5f, 0.05f));
        const int curve = 6;
        for (int i = 1; i <= curve; i++)
        {
            float t = i / (float)curve;
            o.Add(new Vector2(0.5f * (1f - Mathf.Pow(t, 1.7f)), 0.05f - 0.7f * t));
        }
        for (int i = curve - 1; i >= 1; i--)
        {
            float t = i / (float)curve;
            o.Add(new Vector2(-0.5f * (1f - Mathf.Pow(t, 1.7f)), 0.05f - 0.7f * t));
        }
        o.Add(new Vector2(-0.5f, 0.05f));
        // o now runs top-left -> top-right -> down the right -> point -> up the left (clockwise from the front).

        int cnt = o.Count;
        Vector2 centre = new Vector2(0f, 0.1f);
        const float half = 0.06f;      // half the thickness
        const float inset = 0.8f;      // body face scale inside the rim
        var b = new Builder();

        Vector3 P(Vector2 p, float z) => new Vector3(p.x, p.y, z);
        Vector2 In(Vector2 p) => centre + (p - centre) * inset;

        for (int i = 0; i < cnt; i++)
        {
            Vector2 a = o[i], c = o[(i + 1) % cnt];
            Vector2 ai = In(a), ci = In(c);

            // Body: the raised front face inside the rim, and the back face.
            b.Tri(0, P(centre, half + 0.012f), P(ci, half + 0.012f), P(ai, half + 0.012f));
            b.Tri(0, P(centre, -half), P(a, -half), P(c, -half));

            // Rim: a bevel from the edge up to the inset line (front), plus the side wall.
            b.Quad(1, P(ai, half + 0.012f), P(ci, half + 0.012f), P(c, half), P(a, half));
            b.Quad(1, P(a, half), P(c, half), P(c, -half), P(a, -half));
        }

        // Emblem: a small cube standing on the front face.
        float e = 0.09f, z0 = half + 0.012f, z1 = z0 + 0.07f;
        Vector3 ec = new Vector3(centre.x, centre.y, 0f);
        Vector3 f0 = ec + new Vector3(-e, -e, z0), f1 = ec + new Vector3(e, -e, z0), f2 = ec + new Vector3(e, e, z0), f3 = ec + new Vector3(-e, e, z0);
        Vector3 g0 = ec + new Vector3(-e, -e, z1), g1 = ec + new Vector3(e, -e, z1), g2 = ec + new Vector3(e, e, z1), g3 = ec + new Vector3(-e, e, z1);
        b.Quad(2, g0, g1, g2, g3);
        b.Quad(2, g1, g0, f0, f1);
        b.Quad(2, g2, g1, f1, f2);
        b.Quad(2, g3, g2, f2, f3);
        b.Quad(2, g0, g3, f3, f0);

        var mesh = new Mesh { name = "ShieldCrest (procedural)", subMeshCount = 3 };
        mesh.SetVertices(b.v);
        mesh.SetNormals(b.n);
        for (int s = 0; s < 3; s++) mesh.SetTriangles(b.tris[s], s);
        mesh.RecalculateBounds();
        return mesh;
    }
}
