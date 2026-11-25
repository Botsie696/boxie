using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Voxel checkpoint: builds the blocky meshes (VoxelCheckpoint/Block shader). A VoxelMesh is a voxel grid; only faces
/// between a filled and an empty cell are emitted. Vertex colour = base pigment (linear), alpha = wind sway; uv0 = surface
/// coordinate in voxels; uv1 = (kind, texture pixels per voxel). Grass voxels use the grass-top / grass-side / dirt kinds
/// on their faces; logs show rings on their ends. Boxes (pipes, "?" blocks) can be added with a uv of 0..1 per face.
/// </summary>
public class VoxelMesh
{
    public const int Plain = 0, GrassTop = 1, GrassSide = 2, Dirt = 3, Brick = 4, Stone = 5, Leaves = 6, Log = 7, Plank = 8,
        QBlock = 9, Pipe = 10, Window = 11, LitWindow = 12, Water = 13, Cloud = 14, Roof = 15, Sand = 16, Spots = 17,
        Petal = 18, Lamp = 19, Eye = 20;
    /// <summary>Voxel-only: a grass block (grass top, turf-and-dirt sides, dirt bottom; colour = the dirt).</summary>
    public const int Grass = 100;

    readonly int sx, sy, sz, ox, oy, oz;
    readonly byte[] kind;          // kind + 1, 0 = empty
    readonly Color[] color;
    readonly float[] sway;

    readonly List<Vector3> v = new List<Vector3>();
    readonly List<Vector3> nr = new List<Vector3>();
    readonly List<Color> c = new List<Color>();
    readonly List<Vector2> uv = new List<Vector2>();
    readonly List<Vector2> uv1 = new List<Vector2>();
    readonly List<int> t = new List<int>();

    /// <summary>Grid of x in [-ox, sx-ox), y in [-oy, sy-oy), z in [-oz, sz-oz).</summary>
    public VoxelMesh(int sizeX, int sizeY, int sizeZ, int offX = -1, int offY = 0, int offZ = -1)
    {
        sx = sizeX; sy = sizeY; sz = sizeZ;
        ox = offX < 0 ? sizeX / 2 : offX; oy = offY; oz = offZ < 0 ? sizeZ / 2 : offZ;
        kind = new byte[sx * sy * sz];
        color = new Color[sx * sy * sz];
        sway = new float[sx * sy * sz];
    }

    int Index(int x, int y, int z)
    {
        x += ox; y += oy; z += oz;
        if (x < 0 || y < 0 || z < 0 || x >= sx || y >= sy || z >= sz) return -1;
        return (y * sz + z) * sx + x;
    }

    public void Set(int x, int y, int z, Color col, int k, float swayW = 0f)
    {
        int i = Index(x, y, z);
        if (i < 0) return;
        kind[i] = (byte)(k == Grass ? 255 : k + 1);
        color[i] = col.linear;
        sway[i] = swayW;
    }

    public void Clear(int x, int y, int z) { int i = Index(x, y, z); if (i >= 0) kind[i] = 0; }

    public bool Filled(int x, int y, int z) { int i = Index(x, y, z); return i >= 0 && kind[i] != 0; }

    public void Box(int x0, int y0, int z0, int x1, int y1, int z1, Color col, int k, float swayW = 0f)
    {
        for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    Set(x, y, z, col, k, swayW);
    }

    /// <summary>A rounded blob of voxels (crowns, bushes, clouds). Cells with hash under "holes" at the rim are left out.</summary>
    public void Blob(Vector3 ctr, Vector3 radius, Color col, int k, System.Random rng, float holes = 0.25f, float swayW = 0f)
    {
        int x0 = Mathf.FloorToInt(ctr.x - radius.x), x1 = Mathf.CeilToInt(ctr.x + radius.x);
        int y0 = Mathf.FloorToInt(ctr.y - radius.y), y1 = Mathf.CeilToInt(ctr.y + radius.y);
        int z0 = Mathf.FloorToInt(ctr.z - radius.z), z1 = Mathf.CeilToInt(ctr.z + radius.z);
        for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector3 d = new Vector3((x + 0.5f - ctr.x) / radius.x, (y + 0.5f - ctr.y) / radius.y, (z + 0.5f - ctr.z) / radius.z);
                    float r = d.magnitude;
                    if (r > 1f) continue;
                    if (r > 0.75f && rng.NextDouble() < holes) continue;
                    Set(x, y, z, col, k, swayW * Mathf.Clamp01(y - y0) / Mathf.Max(1, y1 - y0));
                }
    }

    static readonly Vector3Int[] Dirs =
    {
        new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, 1, 0),
        new Vector3Int(0, -1, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
    };

    void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Vector3 n, Vector2 ua, Vector2 ub, Vector2 ud, Vector2 ue, Color col, int k, float px, float sw)
    {
        int s = v.Count;
        v.Add(a); v.Add(b); v.Add(d); v.Add(e);
        for (int i = 0; i < 4; i++) { nr.Add(n); col.a = sw; c.Add(col); uv1.Add(new Vector2(k, px)); }
        uv.Add(ua); uv.Add(ub); uv.Add(ud); uv.Add(ue);
        t.Add(s); t.Add(s + 1); t.Add(s + 2);
        t.Add(s); t.Add(s + 2); t.Add(s + 3);
    }

    // One unit face of the voxel at (x, y, z) facing dir, scaled by size, uv in voxel units (y up on the sides).
    void Face(int x, int y, int z, int dir, float size, Color col, int k, float px, float sw)
    {
        Vector3 p = new Vector3(x, y, z);
        Vector3Int dn = Dirs[dir];
        Vector3 n = dn;
        Vector3 a, b, d, e;
        Vector2 ua, ub, ud, ue;
        switch (dir)
        {
            case 0: a = p + new Vector3(1, 0, 0); b = p + new Vector3(1, 1, 0); d = p + new Vector3(1, 1, 1); e = p + new Vector3(1, 0, 1);
                ua = new Vector2(z, y); ub = new Vector2(z, y + 1); ud = new Vector2(z + 1, y + 1); ue = new Vector2(z + 1, y); break;
            case 1: a = p + new Vector3(0, 0, 1); b = p + new Vector3(0, 1, 1); d = p + new Vector3(0, 1, 0); e = p;
                ua = new Vector2(-z - 1, y); ub = new Vector2(-z - 1, y + 1); ud = new Vector2(-z, y + 1); ue = new Vector2(-z, y); break;
            case 2: a = p + new Vector3(0, 1, 0); b = p + new Vector3(0, 1, 1); d = p + new Vector3(1, 1, 1); e = p + new Vector3(1, 1, 0);
                ua = new Vector2(x, z); ub = new Vector2(x, z + 1); ud = new Vector2(x + 1, z + 1); ue = new Vector2(x + 1, z); break;
            case 3: a = p; b = p + new Vector3(1, 0, 0); d = p + new Vector3(1, 0, 1); e = p + new Vector3(0, 0, 1);
                ua = new Vector2(x, z); ub = new Vector2(x + 1, z); ud = new Vector2(x + 1, z + 1); ue = new Vector2(x, z + 1); break;
            case 4: a = p + new Vector3(1, 0, 1); b = p + new Vector3(1, 1, 1); d = p + new Vector3(0, 1, 1); e = p + new Vector3(0, 0, 1);
                ua = new Vector2(-x - 1, y); ub = new Vector2(-x - 1, y + 1); ud = new Vector2(-x, y + 1); ue = new Vector2(-x, y); break;
            default: a = p; b = p + new Vector3(0, 1, 0); d = p + new Vector3(1, 1, 0); e = p + new Vector3(1, 0, 0);
                ua = new Vector2(x, y); ub = new Vector2(x, y + 1); ud = new Vector2(x + 1, y + 1); ue = new Vector2(x + 1, y); break;
        }
        Quad(a * size, b * size, d * size, e * size, n, ua, ub, ud, ue, col, k, px, sw);
    }

    /// <summary>An axis box (world units of the mesh) with uv 0..1 across each face (pipes, "?" blocks, brick blocks).</summary>
    public void SolidBox(Vector3 min, Vector3 max, Color col, int k, float px = 8f)
    {
        col = col.linear;
        Vector3 s = max - min;
        Vector3 P(float x, float y, float z) => min + Vector3.Scale(s, new Vector3(x, y, z));
        Vector2 U(float a, float b) => new Vector2(a, b);
        Quad(P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1), Vector3.right, U(0, 0), U(0, 1), U(1, 1), U(1, 0), col, k, px, 0f);
        Quad(P(0, 0, 1), P(0, 1, 1), P(0, 1, 0), P(0, 0, 0), Vector3.left, U(0, 0), U(0, 1), U(1, 1), U(1, 0), col, k, px, 0f);
        Quad(P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0), Vector3.up, U(0, 0), U(0, 1), U(1, 1), U(1, 0), col, k, px, 0f);
        Quad(P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1), Vector3.down, U(0, 0), U(1, 0), U(1, 1), U(0, 1), col, k, px, 0f);
        Quad(P(1, 0, 1), P(1, 1, 1), P(0, 1, 1), P(0, 0, 1), Vector3.forward, U(0, 0), U(0, 1), U(1, 1), U(1, 0), col, k, px, 0f);
        Quad(P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0), Vector3.back, U(0, 0), U(0, 1), U(1, 1), U(1, 0), col, k, px, 0f);
    }

    /// <summary>Emits every exposed face of the grid, merging neighbouring faces of the same kind / colour / sway into one
    /// quad (greedy meshing). The uv stays in voxel units, so a merged quad shows exactly the per-voxel texture of the faces it
    /// replaces. size = world size of one voxel in the mesh; px = texture pixels per voxel.</summary>
    public void EmitVoxels(float size, float px, Color grass)
    {
        Color grassLin = grass.linear;
        int[] dims = { sx, sy, sz };
        int[] offs = { ox, oy, oz };
        for (int dir = 0; dir < 6; dir++)
        {
            int d = dir / 2;
            int ua = (d + 1) % 3, va = (d + 2) % 3;
            int nu = dims[ua], nv = dims[va];
            Vector3Int dn = Dirs[dir];
            var mk = new int[nu * nv];
            var mc = new Color[nu * nv];
            var ms = new float[nu * nv];
            var pos = new int[3];
            for (int sl = 0; sl < dims[d]; sl++)
            {
                for (int v0 = 0; v0 < nv; v0++)
                    for (int u0 = 0; u0 < nu; u0++)
                    {
                        pos[d] = sl - offs[d]; pos[ua] = u0 - offs[ua]; pos[va] = v0 - offs[va];
                        int i = Index(pos[0], pos[1], pos[2]);
                        int m = u0 + v0 * nu;
                        mk[m] = -1;
                        if (kind[i] == 0 || Filled(pos[0] + dn.x, pos[1] + dn.y, pos[2] + dn.z)) continue;
                        bool isGrass = kind[i] == 255;
                        int fk = isGrass ? (dir == 2 ? GrassTop : dir == 3 ? Dirt : GrassSide) : kind[i] - 1;
                        mk[m] = fk;
                        mc[m] = isGrass && dir == 2 ? grassLin : color[i];
                        ms[m] = sway[i];
                    }
                for (int v0 = 0; v0 < nv; v0++)
                    for (int u0 = 0; u0 < nu; u0++)
                    {
                        int m = u0 + v0 * nu;
                        int k = mk[m];
                        if (k < 0) continue;
                        Color col = mc[m]; float sw = ms[m];
                        bool Same(int j) => mk[j] == k && mc[j] == col && ms[j] == sw;
                        int w = 1;
                        while (u0 + w < nu && Same(m + w)) w++;
                        int h = 1;
                        for (; v0 + h < nv; h++)
                        {
                            bool row = true;
                            for (int x = 0; x < w; x++) if (!Same(m + x + h * nu)) { row = false; break; }
                            if (!row) break;
                        }
                        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) mk[m + x + y * nu] = -1;
                        float plane = sl - offs[d] + (dn[d] > 0 ? 1 : 0);
                        float uA = u0 - offs[ua], uB = uA + w, vA = v0 - offs[va], vB = vA + h;
                        Vector3 C(float uu, float vv) { var p = new Vector3(); p[d] = plane; p[ua] = uu; p[va] = vv; return p; }
                        Vector3 c0 = C(uA, vA), c1 = C(uB, vA), c2 = C(uB, vB), c3 = C(uA, vB);
                        Vector3 n = dn;
                        if (Vector3.Dot(Vector3.Cross(c1 - c0, c2 - c0), n) < 0f) { Vector3 tmp = c1; c1 = c3; c3 = tmp; }
                        Quad(c0 * size, c1 * size, c2 * size, c3 * size, n, UV(c0, dir), UV(c1, dir), UV(c2, dir), UV(c3, dir), col, k, px, sw);
                    }
            }
        }
    }

    // The same surface coordinate Face() uses (voxel units, y up on the sides).
    static Vector2 UV(Vector3 p, int dir)
    {
        switch (dir)
        {
            case 0: return new Vector2(p.z, p.y);
            case 1: return new Vector2(-p.z, p.y);
            case 2: case 3: return new Vector2(p.x, p.z);
            case 4: return new Vector2(-p.x, p.y);
            default: return new Vector2(p.x, p.y);
        }
    }

    public Mesh Build(string name)
    {
        var m = new Mesh { name = name };
        if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(v); m.SetNormals(nr); m.SetColors(c); m.SetUVs(0, uv); m.SetUVs(1, uv1); m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }
}
