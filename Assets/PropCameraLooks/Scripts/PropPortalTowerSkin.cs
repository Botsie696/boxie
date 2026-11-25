using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "MainScene prop camera 5" only (the red Monument Valley tower reference). Re-skins every zone-door
/// tower PortalZoneController builds, WITHOUT touching PortalDoor.cs: the door is built as usual
/// (void quad, light rays, fade/reveal, runner logic) and this component then swaps the mesh of its
/// "Tower" part for the reference's tower and drops the crenellated dome, finial and steps.
///
///   - a pink slab with square tile outlines under it (the portal's ground),
///   - three stacked blocks split by thin violet grooves: a blue-and-cyan bottom block with the
///     coral-framed archway on the travel face and two rings on the flanks, a middle block with a
///     white ring / green disc and a coral arch window, and a top block with a mint lid and coral arcs,
///   - the arch tunnel walls in violet.
///
/// Everything is one vertex-coloured mesh on the existing Tower renderer, so the door's own dither
/// fade-in / fade-out and its renderer toggling keep working unchanged: the new tower grows in with
/// the track exactly as the old one did and nothing pops. The tower's material colour must be white
/// (PortalZoneController.doorStoneColor = white) so the vertex colours are the final colours.
///
/// The doorway geometry mirrors PortalDoor's private constants (TowerHalf, OpenHalf, SpringY, FloorY).
/// An optional shadows-only copy of the mesh lets the tower cast a sun shadow onto the floor; it
/// scales in with the door's fade.
/// </summary>
[DefaultExecutionOrder(600)]   // after PortalDoor.Update
public class PropPortalTowerSkin : MonoBehaviour
{
    [Header("Stone (sRGB)")]
    public Color blue = Hex("4FA7D3");
    public Color cyan = Hex("6DE6E1");
    public Color mint = Hex("D6F0D6");
    public Color coral = Hex("F29A8A");
    public Color groove = Hex("6A6AA8");
    public Color ring = Hex("67D4E5");
    public Color ringCentre = Hex("587496");
    public Color ringWhite = Hex("FFFFFF");
    public Color ringGreen = Hex("206749");
    public Color tunnelWall = Hex("4B4470");
    public Color tunnelCeiling = Hex("3C3763");

    [Header("Ground slab")]
    public bool slab = true;
    public Color slabTop = Hex("FBCBCE");
    public Color slabFaceX = Hex("E88588");
    public Color slabFaceZ = Hex("F77884");
    public Color slabLine = Hex("EBB1B5");
    public float slabHalf = 2.2f;
    public float slabThickness = 0.5f;

    [Header("Doorway (void shader)")]
    public Color entryVoid = Hex("3E3868");
    public Color entrySwirl = Hex("8D7BD8");
    public Color exitVoid = Hex("7468B8");
    public Color exitSwirl = Hex("D9C6FF");
    public Color rim = Hex("F5A090");
    public Color rayColor = new Color(1.0f, 0.88f, 1.05f, 1f);
    public Color moteColor = Hex("E9B4F6");

    [Header("Shadow")]
    [Tooltip("The tower casts the sun's shadow (onto the glow floor). Off = no tower shadow.")]
    public bool castShadow = true;

    // ---- mirrors of PortalDoor's private constants
    const float H = 1.0f;          // TowerHalf
    const float FloorY = -0.02f;
    const float OpenHalf = 0.68f;
    const float SpringY = 1.7f;

    // ---- block layout (local units = cubes)
    const float BotTop = 2.85f;
    const float Gap = 0.1f;
    const float MidBot = BotTop + Gap, MidTop = MidBot + 1.6f;
    const float TopBot = MidTop + Gap, TopTop = TopBot + 1.2f;

    sealed class Skinned
    {
        public PortalDoor door;
        public Mesh mesh, rayMesh;
        public Renderer voidRenderer, tower;
        public Renderer shadowRenderer;
        public Material shadowMat;
        public bool exit;
        public Material shaftMat, moteMat;
        public ParticleSystem motes;
    }

    readonly List<Skinned> skinned = new List<Skinned>();
    readonly HashSet<int> seen = new HashSet<int>();
    float nextPoll;

    static readonly int VoidId = Shader.PropertyToID("_VoidColor");
    static readonly int AccentId = Shader.PropertyToID("_AccentColor");
    static readonly int RimId = Shader.PropertyToID("_RimColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int FadeId = Shader.PropertyToID("_Fade");

    static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out Color c); return c; }

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextPoll)
        {
            nextPoll = Time.unscaledTime + 0.05f;
            foreach (PortalDoor d in FindObjectsByType<PortalDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (d != null && d.Monument && seen.Add(d.GetInstanceID())) Skin(d);
        }

        for (int i = skinned.Count - 1; i >= 0; i--)
        {
            Skinned s = skinned[i];
            if (s.door == null)
            {
                if (s.mesh != null) Destroy(s.mesh);
                if (s.rayMesh != null) Destroy(s.rayMesh);
                if (s.shadowMat != null) Destroy(s.shadowMat);
                skinned.RemoveAt(i);
                continue;
            }
            Restyle(s);
        }
    }

    void OnDestroy()
    {
        foreach (Skinned s in skinned)
        {
            if (s.mesh != null) Destroy(s.mesh);
            if (s.rayMesh != null) Destroy(s.rayMesh);
            if (s.shadowMat != null) Destroy(s.shadowMat);
        }
    }

    void Skin(PortalDoor d)
    {
        Transform tower = d.transform.Find("Tower");
        if (tower == null) return;
        var mf = tower.GetComponent<MeshFilter>();
        if (mf == null) return;

        bool exit = d.transform.Find("Haze") != null;
        var s = new Skinned { door = d, exit = exit, tower = tower.GetComponent<Renderer>() };
        s.mesh = BuildTower(exit ? 1f : -1f);
        mf.sharedMesh = s.mesh;

        // The crenellated extras are not part of this tower: empty their meshes (the door keeps toggling
        // the renderers, which then draw nothing).
        foreach (string n in new[] { "Dome", "Finial", "StairsPos", "StairsNeg" })
        {
            Transform t = d.transform.Find(n);
            if (t == null) continue;
            var f = t.GetComponent<MeshFilter>();
            if (f != null) f.sharedMesh = null;
        }

        Transform v = d.transform.Find("Void");
        if (v != null) s.voidRenderer = v.GetComponent<Renderer>();

        // The exit's light: the reference has one thin beam, not a spray of eleven shafts over a haze box.
        Transform haze = d.transform.Find("Haze");
        if (haze != null) { var hf = haze.GetComponent<MeshFilter>(); if (hf != null) hf.sharedMesh = null; }
        Transform rays = d.transform.Find("Rays");
        if (rays != null)
        {
            s.shaftMat = rays.GetComponent<Renderer>().sharedMaterial;
            var rf = rays.GetComponent<MeshFilter>();
            if (rf != null) { s.rayMesh = BuildBeam(); rf.sharedMesh = s.rayMesh; }
        }
        Transform mt = d.transform.Find("Motes");
        if (mt != null)
        {
            s.motes = mt.GetComponent<ParticleSystem>();
            if (s.motes != null)
            {
                var main = s.motes.main;
                main.startColor = moteColor;
            }
        }

        if (castShadow)
        {
            var go = new GameObject("TowerShadow");
            go.transform.SetParent(d.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = s.mesh;
            var r = go.AddComponent<MeshRenderer>();
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            s.shadowMat = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = "TowerShadow (runtime)" };
            r.sharedMaterial = s.shadowMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            r.receiveShadows = false;
            r.enabled = false;
            s.shadowRenderer = r;
        }
        skinned.Add(s);
    }

    // Per frame, after PortalDoor.Update: the doorway's colours and the shadow caster's scale.
    void Restyle(Skinned s)
    {
        if (s.voidRenderer != null && s.voidRenderer.sharedMaterial != null)
        {
            Material m = s.voidRenderer.sharedMaterial;
            if (m.HasProperty(VoidId)) m.SetColor(VoidId, s.exit ? exitVoid : entryVoid);
            if (m.HasProperty(AccentId)) m.SetColor(AccentId, s.exit ? exitSwirl : entrySwirl);
            if (m.HasProperty(RimId)) m.SetColor(RimId, rim);
        }
        if (s.shaftMat != null && s.shaftMat.HasProperty(ColorId)) s.shaftMat.SetColor(ColorId, rayColor);

        if (s.shadowRenderer != null)
        {
            float fade = 0f;
            if (s.tower != null && s.tower.enabled && s.tower.sharedMaterial != null && s.tower.sharedMaterial.HasProperty(FadeId))
                fade = Mathf.Clamp01(s.tower.sharedMaterial.GetFloat(FadeId));
            bool on = fade > 0.02f;
            if (s.shadowRenderer.enabled != on) s.shadowRenderer.enabled = on;
            // Grows from the door's base with its fade-in, so the shadow never pops onto the floor.
            s.shadowRenderer.transform.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, fade);
        }
    }

    // ------------------------------------------------------------------ light beam

    // A few thin shafts out of the doorway (uv.x across a ribbon, uv.y along it, as PortalShaft expects):
    // one long bright beam with two shorter, fainter ones fanned a little either side. Two crossed
    // ribbons per shaft so it reads from the camera's high angle.
    static Mesh BuildBeam()
    {
        const float z0 = H - 0.3f + 0.02f;   // PortalDoor.VoidZ(exit) + 0.02
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        void Ribbon(Vector3 p0, Vector3 p1, Vector3 side, float w0, float w1)
        {
            int i = v.Count;
            v.Add(p0 - side * (w0 * 0.5f)); v.Add(p0 + side * (w0 * 0.5f));
            v.Add(p1 + side * (w1 * 0.5f)); v.Add(p1 - side * (w1 * 0.5f));
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }
        float[] yaws = { 0f, -13f, 12f };
        float[] pitches = { 4f, 9f, 1f };
        float[] lens = { 6.2f, 4.2f, 4.6f };
        float[] widths = { 0.34f, 0.2f, 0.2f };
        for (int i = 0; i < 3; i++)
        {
            float yaw = yaws[i] * Mathf.Deg2Rad, pitch = pitches[i] * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
            Vector3 p0 = new Vector3(0f, 0.75f, z0), p1 = p0 + dir * lens[i];
            Vector3 s1 = Vector3.Cross(dir, Vector3.up).normalized, s2 = Vector3.Cross(dir, s1).normalized;
            Ribbon(p0, p1, s1, 0.05f, widths[i]);
            Ribbon(p0, p1, s2, 0.05f, widths[i]);
        }
        var m = new Mesh { name = "PortalBeamRed" };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }

    // ------------------------------------------------------------------ mesh

    sealed class MB
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<int> T = new List<int>();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color srgb)
        {
            Color col = srgb.linear;   // vertex colours are used as-is by the unlit shader
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            for (int k = 0; k < 4; k++) C.Add(col);
            T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color srgb)
        {
            Color col = srgb.linear;
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            for (int k = 0; k < 3; k++) C.Add(col);
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(V);
            m.SetColors(C);
            m.SetTriangles(T, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    // A flat face the decals are drawn on: origin o (face centre), u to the viewer's right, v up, n outward.
    struct Face
    {
        public Vector3 o, u, v, n;
        public Vector3 P(float x, float y, float lift) => o + u * x + v * y + n * lift;
    }

    static Face ZFace(float z, float yCentre) { float s = Mathf.Sign(z); return new Face { o = new Vector3(0f, yCentre, z), u = Vector3.right * s, v = Vector3.up, n = new Vector3(0f, 0f, s) }; }
    static Face XFace(float x, float yCentre) { float s = Mathf.Sign(x); return new Face { o = new Vector3(x, yCentre, 0f), u = new Vector3(0f, 0f, -s), v = Vector3.up, n = new Vector3(s, 0f, 0f) }; }

    static void Rect(MB mb, Face f, float u0, float v0, float u1, float v1, Color c, float lift)
        => mb.Quad(f.P(u0, v0, lift), f.P(u0, v1, lift), f.P(u1, v1, lift), f.P(u1, v0, lift), c);

    static void Disc(MB mb, Face f, float cu, float cv, float r, Color c, float lift, int seg = 28)
    {
        Vector3 o = f.P(cu, cv, lift);
        for (int i = 0; i < seg; i++)
        {
            float a0 = Mathf.PI * 2f * i / seg, a1 = Mathf.PI * 2f * (i + 1) / seg;
            mb.Tri(o, f.P(cu + Mathf.Cos(a0) * r, cv + Mathf.Sin(a0) * r, lift), f.P(cu + Mathf.Cos(a1) * r, cv + Mathf.Sin(a1) * r, lift), c);
        }
    }

    // A band of an annulus between radii r0..r1, angles a0..a1 (degrees, 0 = +u, counter-clockwise).
    static void Band(MB mb, Face f, float cu, float cv, float r0, float r1, float a0, float a1, Color c, float lift, int seg = 24)
    {
        for (int i = 0; i < seg; i++)
        {
            float t0 = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, i / (float)seg), t1 = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
            mb.Quad(f.P(cu + Mathf.Cos(t0) * r0, cv + Mathf.Sin(t0) * r0, lift), f.P(cu + Mathf.Cos(t0) * r1, cv + Mathf.Sin(t0) * r1, lift),
                    f.P(cu + Mathf.Cos(t1) * r1, cv + Mathf.Sin(t1) * r1, lift), f.P(cu + Mathf.Cos(t1) * r0, cv + Mathf.Sin(t1) * r0, lift), c);
        }
    }

    // A line of constant width along a polyline.
    static void Strip(MB mb, Face f, List<Vector2> p, float hw, Color c, float lift)
    {
        for (int i = 0; i < p.Count - 1; i++)
        {
            Vector2 a = p[i], b = p[i + 1];
            Vector2 d = (b - a).normalized;
            Vector2 n = new Vector2(-d.y, d.x) * hw;
            a -= d * hw * 0.5f; b += d * hw * 0.5f;   // overlap the joints
            mb.Quad(f.P(a.x - n.x, a.y - n.y, lift), f.P(a.x + n.x, a.y + n.y, lift), f.P(b.x + n.x, b.y + n.y, lift), f.P(b.x - n.x, b.y - n.y, lift), c);
        }
    }

    // ∩ outline (open at the bottom) when up, ∪ (open at the top) when not.
    static List<Vector2> ArchLine(float cu, float footV, float springV, float r, bool up, int n = 16)
    {
        float s = up ? 1f : -1f;
        var p = new List<Vector2> { new Vector2(cu - r, footV), new Vector2(cu - r, springV) };
        for (int k = 1; k < n; k++)
        {
            float a = Mathf.PI * k / n;
            p.Add(new Vector2(cu - r * Mathf.Cos(a), springV + s * r * Mathf.Sin(a)));
        }
        p.Add(new Vector2(cu + r, springV));
        p.Add(new Vector2(cu + r, footV));
        return p;
    }

    static void Box(MB mb, Vector3 min, Vector3 max, Color px, Color nx, Color pz, Color nz, Color top, Color bottom)
    {
        mb.Quad(new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, min.y, max.z), px);
        mb.Quad(new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z), new Vector3(min.x, min.y, min.z), nx);
        mb.Quad(new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, min.y, max.z), pz);
        mb.Quad(new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(min.x, min.y, min.z), nz);
        mb.Quad(new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z), top);
        mb.Quad(new Vector3(min.x, min.y, max.z), new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), bottom);
    }

    static List<Vector2> ArchPoints()
    {
        const int N = 16;
        var pts = new List<Vector2> { new Vector2(-OpenHalf, FloorY), new Vector2(-OpenHalf, SpringY) };
        for (int k = 1; k < N; k++)
        {
            float a = Mathf.PI * k / N;
            pts.Add(new Vector2(-OpenHalf * Mathf.Cos(a), SpringY + OpenHalf * Mathf.Sin(a)));
        }
        pts.Add(new Vector2(OpenHalf, SpringY));
        pts.Add(new Vector2(OpenHalf, FloorY));
        return pts;
    }

    // A wall at depth z with the arched doorway left open, up to height top.
    static void ArchWall(MB mb, float z, float top, Color c)
    {
        mb.Quad(new Vector3(-H, FloorY, z), new Vector3(-H, top, z), new Vector3(-OpenHalf, top, z), new Vector3(-OpenHalf, FloorY, z), c);
        mb.Quad(new Vector3(OpenHalf, FloorY, z), new Vector3(OpenHalf, top, z), new Vector3(H, top, z), new Vector3(H, FloorY, z), c);
        var arch = ArchPoints();
        for (int i = 1; i < arch.Count - 2; i++)
        {
            Vector2 a = arch[i], b = arch[i + 1];
            mb.Quad(new Vector3(a.x, a.y, z), new Vector3(a.x, top, z), new Vector3(b.x, top, z), new Vector3(b.x, b.y, z), c);
        }
    }

    // bz = the Z side the camera sees (the travel face): -1 entry, +1 exit.
    Mesh BuildTower(float bz)
    {
        var mb = new MB();
        float front = bz * H, back = -bz * H;

        // ---------------- ground slab with square tile outlines
        if (slab)
        {
            float top = -0.04f, bot = top - slabThickness, sh = slabHalf;
            Box(mb, new Vector3(-sh, bot, -sh), new Vector3(sh, top, sh), slabFaceX, slabFaceX, slabFaceZ, slabFaceZ, slabTop, slabFaceX);
            var tf = new Face { o = new Vector3(0f, top, 0f), u = Vector3.right, v = Vector3.forward, n = Vector3.up };
            const int cells = 4;
            float cell = sh * 2f / cells, inset = 0.14f, lw = 0.04f;
            for (int i = 0; i < cells; i++)
                for (int j = 0; j < cells; j++)
                {
                    float u0 = -sh + i * cell + inset, u1 = -sh + (i + 1) * cell - inset;
                    float v0 = -sh + j * cell + inset, v1 = -sh + (j + 1) * cell - inset;
                    Rect(mb, tf, u0, v0, u1, v0 + lw, slabLine, 0.006f);
                    Rect(mb, tf, u0, v1 - lw, u1, v1, slabLine, 0.006f);
                    Rect(mb, tf, u0, v0, u0 + lw, v1, slabLine, 0.006f);
                    Rect(mb, tf, u1 - lw, v0, u1, v1, slabLine, 0.006f);
                }
        }

        // ---------------- the three blocks
        // Bottom: arch walls on both Z faces, flanks solid.
        mb.Quad(new Vector3(H, FloorY, -H), new Vector3(H, BotTop, -H), new Vector3(H, BotTop, H), new Vector3(H, FloorY, H), blue);
        mb.Quad(new Vector3(-H, FloorY, H), new Vector3(-H, BotTop, H), new Vector3(-H, BotTop, -H), new Vector3(-H, FloorY, -H), blue);
        ArchWall(mb, front, BotTop, cyan);
        ArchWall(mb, back, BotTop, cyan);
        mb.Quad(new Vector3(-H, BotTop, -H), new Vector3(-H, BotTop, H), new Vector3(H, BotTop, H), new Vector3(H, BotTop, -H), groove);

        // The tunnel: violet walls and ceiling through the shaft.
        mb.Quad(new Vector3(-OpenHalf, FloorY, -H), new Vector3(-OpenHalf, SpringY, -H), new Vector3(-OpenHalf, SpringY, H), new Vector3(-OpenHalf, FloorY, H), tunnelWall);
        mb.Quad(new Vector3(OpenHalf, FloorY, H), new Vector3(OpenHalf, SpringY, H), new Vector3(OpenHalf, SpringY, -H), new Vector3(OpenHalf, FloorY, -H), tunnelWall);
        var arch = ArchPoints();
        for (int i = 1; i < arch.Count - 2; i++)
        {
            Vector2 a = arch[i], b = arch[i + 1];
            mb.Quad(new Vector3(a.x, a.y, -H), new Vector3(b.x, b.y, -H), new Vector3(b.x, b.y, H), new Vector3(a.x, a.y, H), tunnelCeiling);
        }

        // Grooves: slightly narrower bands, so each block reads as its own cube.
        float gi = 0.05f;
        foreach (float gy in new[] { BotTop, MidTop })
            Box(mb, new Vector3(-H + gi, gy, -H + gi), new Vector3(H - gi, gy + Gap, H - gi), groove, groove, groove, groove, groove, groove);

        // Middle and top blocks: solid boxes.
        Box(mb, new Vector3(-H, MidBot, -H), new Vector3(H, MidTop, H), blue, blue, cyan, cyan, cyan, cyan);
        Box(mb, new Vector3(-H, TopBot, -H), new Vector3(H, TopTop, H), blue, blue, cyan, cyan, mint, cyan);

        // ---------------- decals
        const float L1 = 0.010f, L2 = 0.016f;

        // Bottom block, flanks: two rings.
        float botC = 0.5f * (FloorY + BotTop);
        foreach (float sx in new[] { H, -H })
        {
            Face f = XFace(sx, botC);
            foreach (float cv in new[] { 0.78f - botC, 2.0f - botC })
            {
                Disc(mb, f, -0.1f, cv, 0.44f, ring, L1);
                Disc(mb, f, -0.1f, cv, 0.21f, ringCentre, L2);
            }
        }

        // Bottom block, travel face: the coral frame round the doorway.
        {
            Face f = ZFace(front, botC);
            float fw = 0.2f;
            Rect(mb, f, -OpenHalf - fw, FloorY - botC, -OpenHalf, SpringY - botC, coral, L1);
            Rect(mb, f, OpenHalf, FloorY - botC, OpenHalf + fw, SpringY - botC, coral, L1);
            Band(mb, f, 0f, SpringY - botC, OpenHalf, OpenHalf + fw, 0f, 180f, coral, L1);
        }

        // Middle block: white ring + green disc on the flanks, coral arch window on the travel face.
        float midC = 0.5f * (MidBot + MidTop);
        foreach (float sx in new[] { H, -H })
        {
            Face f = XFace(sx, midC);
            Disc(mb, f, -0.1f, 0f, 0.52f, ringWhite, L1);
            Disc(mb, f, -0.1f, 0f, 0.33f, ringGreen, L2);
        }
        {
            Face f = ZFace(front, midC);
            Strip(mb, f, ArchLine(0f, -0.58f, 0.12f, 0.46f, true), 0.075f, coral, L1);
            Disc(mb, f, 0f, -0.2f, 0.13f, coral, L1);
        }

        // Top block: coral arcs round the upper corner on the flanks, nested U on the travel face, U on the lid.
        float topC = 0.5f * (TopBot + TopTop);
        foreach (float sx in new[] { H, -H })
        {
            Face f = XFace(sx, topC);
            foreach (float r in new[] { 0.5f, 0.9f, 1.3f })
            {
                float amax = Mathf.Min(90f, Mathf.Rad2Deg * Mathf.Asin(Mathf.Min(1f, 1.12f / r)));
                Band(mb, f, -H, 0.6f, r - 0.07f, r + 0.07f, -amax, 0f, coral, L1);
            }
        }
        {
            Face f = ZFace(front, topC);
            Strip(mb, f, ArchLine(0f, -0.45f, 0.1f, 0.62f, true), 0.08f, coral, L1);
            Strip(mb, f, ArchLine(0f, -0.45f, 0.1f, 0.3f, true), 0.07f, coral, L1);
        }
        {
            // Lid: two nested U shapes (feet toward the back, bowl toward the viewer), like the reference.
            Face f = new Face { o = new Vector3(0f, TopTop, 0f), u = Vector3.right, v = Vector3.forward, n = Vector3.up };
            Strip(mb, f, ArchLine(0f, 0.62f, 0.0f, 0.62f, false), 0.12f, coral, L1);
            Strip(mb, f, ArchLine(0f, 0.62f, 0.12f, 0.3f, false), 0.1f, coral, L1);
        }

        return mb.ToMesh("PortalTowerRed");
    }
}
