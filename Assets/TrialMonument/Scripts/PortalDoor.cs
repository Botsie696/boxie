using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A portal door. Two looks share this class:
///
/// SLAB (every scene but MainScene prop): a plain red card, taller than wide, with a small light-coloured
/// triangular fold at its bottom-right corner - no frame, no rays, no glow. Built with real thickness
/// and spanning the full width of the path, planted on one cube of the leg, facing across the direction
/// of travel. Built from a universal unlit sprite shader, so it is always exactly a flat coloured card.
///
/// TOWER (MainScene prop, PortalZoneController.monumentDoors): the crenellated tower of the Monument Valley 2
/// reference, built round ONE path cube - the last cube of the dead-end entry runway, or the first cube of
/// the exit island. One solid square body with a round-arched doorway cut clean through it (the beam runs
/// through the tunnel), a slightly flared crown, a ring of battlements with notches, an onion dome with a
/// finial, two thin groove lines under the crown and a short flight of steps against one flank (the flank
/// the camera can see). Colour is the zone accent through TrialMonument/PortalBlock; the shape is fixed.
/// Between the arch's walls, recessed a little inside the front face, is an opaque quad drawn with
/// TrialMonument/PortalVoid. It writes depth, so the runner is hidden by it - and by the stone around it -
/// once it is past the quad's plane: it goes INTO the tower instead of popping.
///   Entry: the quad is at the front of the tower and dark (a deep hole).
///   Exit:  the quad is at the front (exit side) of the tower and glows warm gold; the runner starts
///          hidden behind it and drives out through it, through a fan of light shafts (rays) that spills
///          onto the new track, over a soft haze, with a few glowing motes floating in it.
///
/// Local space of a tower: +Z is the direction of travel, +Y is out of the face the door stands on
/// (world up for side 0), X is across the path, and the door's origin is the centre of its cube's face.
///
/// Reveal fades a door in with the track beside it and SetCrossed fades it out in step with
/// PathDepthShading's own crossed-leg amount: a dither on the stone, the void's own iris (_Open) on the
/// hole, real alpha on the light - no separate effect and no pop.
/// </summary>
public class PortalDoor : MonoBehaviour
{
    /// <summary>0=Top, 1=Right, 2=Bottom, 3=Left - the face of the cube this door stands against.</summary>
    public int Side { get; private set; }
    public Vector3 Forward { get; private set; }
    /// <summary>Where the runner crosses the door, in world space.</summary>
    public Vector3 ThresholdCentre { get; private set; }
    /// <summary>True when built as the tower rather than the slab.</summary>
    public bool Monument { get; private set; }

    // What the camera framing needs to know about the tower (local units = cubes).
    /// <summary>Half the width the tower and its steps take up across the path.</summary>
    public const float FootprintHalfWidth = 2.4f;
    /// <summary>Half the depth of the tower along the path.</summary>
    public const float FootprintHalfDepth = 1.1f;
    /// <summary>Tip of the finial above the top face of the cube it stands on.</summary>
    public const float TowerTopY = 5.9f;

    const float CardWidth = 1.05f;  // spans the path edge to edge - a gate you run into, not a sign beside it
    const float CardHeight = 1.4f;
    const float CardDepth = 0.16f;  // real thickness, so it reads as a solid slab rather than a flat plane
    const float FoldSize = 0.32f;
    static readonly Color FoldTint = new Color(0.98f, 0.82f, 0.68f); // warm, light peach - the paper-fold accent

    Renderer cardRenderer, foldRenderer;
    Material cardMaterial, foldMaterial;
    Color baseColor = Color.red;

    float reveal = 1f;   // 0 = not drawn, 1 = fully there (follows how much of the path beside it is drawn)
    float crossed;       // 0 = fresh, 1 = as left-behind as PathDepthShading's own crossed track

    /// <summary>
    /// origin is where the door stands (a cube's position, on the beam's axis); forward is the
    /// direction of travel through it (flat); sideUp is the world-space outward direction of the face
    /// the door stands against (cube.up/right/-up/-right for side 0/1/2/3).
    /// For the tower, isExit picks the exit variant (glowing, with light rays) and voidTemplate is
    /// M_PortalVoid (a copy is made per door).
    /// </summary>
    public static PortalDoor Create(Transform parent, Vector3 origin, Vector3 forward, Vector3 sideUp, int side, float unit,
                                    bool monument = false, bool isExit = false, Material voidTemplate = null)
    {
        var go = new GameObject("PortalDoor");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(origin + sideUp.normalized * (0.5f * unit), Quaternion.LookRotation(forward, sideUp));
        go.transform.localScale = Vector3.one * unit;

        var door = go.AddComponent<PortalDoor>();
        door.Forward = forward;
        door.Side = side;
        door.Monument = monument;
        door.isExit = isExit;
        door.voidTemplate = voidTemplate;
        if (monument) door.BuildArchway();
        else door.Build();
        // The tower's threshold is the recessed void quad, not the centre of its cube.
        door.ThresholdCentre = monument ? origin + forward.normalized * (VoidZ(isExit) * unit) : origin;
        // Born hidden: the new component's own Update only runs next frame, so without this the freshly
        // built door (reveal defaulting to 1, renderers on) would be drawn at full strength for one frame
        // before the controller's reveal(0) took effect - the brief flash when a portal spawns.
        door.reveal = 0f;
        door.Apply();
        return door;
    }

    // ------------------------------------------------------------------ slab

    void Build()
    {
        // A real box, not a paper-thin quad: it has to read as a solid slab you run into, not a
        // sticker floating beside the path.
        Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

        var card = new GameObject("Card");
        card.transform.SetParent(transform, false);
        card.transform.localPosition = new Vector3(0f, CardHeight * 0.5f, 0f);
        card.transform.localScale = new Vector3(CardWidth, CardHeight, CardDepth);
        card.AddComponent<MeshFilter>().sharedMesh = cube;
        cardRenderer = card.AddComponent<MeshRenderer>();
        cardRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cardRenderer.receiveShadows = false;
        cardMaterial = MakeFlatMaterial(baseColor);
        cardRenderer.sharedMaterial = cardMaterial;

        // Flush on the face that greets the approaching runner (local -Z: forward is the direction
        // of travel, so the runner meets this side first).
        var fold = new GameObject("Fold");
        fold.transform.SetParent(transform, false);
        fold.transform.localPosition = new Vector3(0f, 0f, -(CardDepth * 0.5f + 0.01f));
        fold.AddComponent<MeshFilter>().sharedMesh = FoldMesh();
        foldRenderer = fold.AddComponent<MeshRenderer>();
        foldRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        foldRenderer.receiveShadows = false;
        foldMaterial = MakeFlatMaterial(FoldTint);
        foldRenderer.sharedMaterial = foldMaterial;

        Apply();
    }

    static Mesh foldMeshCache;
    // A small right triangle folding in from the card's bottom-right corner, built both-sided so it
    // reads from either direction of travel.
    static Mesh FoldMesh()
    {
        if (foldMeshCache != null) return foldMeshCache;
        float hw = CardWidth * 0.5f;
        var v0 = new Vector3(hw, 0f, 0f);
        var v1 = new Vector3(hw, FoldSize, 0f);
        var v2 = new Vector3(hw - FoldSize, 0f, 0f);
        var mesh = new Mesh
        {
            name = "PortalDoorFold",
            vertices = new[] { v0, v1, v2 },
            triangles = new[] { 0, 1, 2, 0, 2, 1 },
            normals = new[] { Vector3.back, Vector3.back, Vector3.back }
        };
        mesh.RecalculateBounds();
        foldMeshCache = mesh;
        return mesh;
    }

    static Material MakeFlatMaterial(Color c)
    {
        Shader shader = Shader.Find("Sprites/Default");
        var m = new Material(shader) { name = "PortalDoorFlat (runtime)" };
        m.color = c;
        return m;
    }

    // ------------------------------------------------------------------ tower

    // The cube the tower is built round spans Z -0.5..0.5 and Y -1..0 (its top face is Y = 0).
    const float TowerHalf = 1.0f;       // the shaft is a square, 2 x 2, centred on the cube
    const float CorbelHalf = 1.1f;      // the crown flares out a little above the shaft
    const float BaseY = -1f;            // the plinth stands on the bottom of the beam
    const float FloorY = -0.02f;        // just under the beam's top face, so the tunnel floor never z-fights it
    const float ShaftTop = 3.3f;        // slim and tall, like the reference: the shaft is over twice as high as it is wide
    const float CorbelTop = 3.7f;
    const float MerlonHeight = 0.4f;
    const float MerlonThickness = 0.3f;
    const float OpenHalf = 0.68f;       // half the width of the doorway: room for the runner cube
    const float SpringY = 1.7f;         // the arch's straight sides stand this high, then it turns round
    const float Recess = 0.3f;          // how far inside the tower, from its front face, the void sits
    const float RayLength = 5.5f;
    const float RayFarHalfWidth = 2.4f;
    static float ArchTop => SpringY + OpenHalf;

    static float VoidZ(bool exit) => exit ? TowerHalf - Recess : -TowerHalf + Recess;

    bool isExit;
    Material voidTemplate;
    Color glowColor = new Color(1f, 0.82f, 0.45f, 1f);

    readonly List<Mesh> ownedMeshes = new List<Mesh>();
    Material bodyMat, domeMat, spireMat, voidMat, rayMat, shaftMat, poolMat, moteMat;
    Renderer voidRenderer;
    readonly List<Renderer> stoneRenderers = new List<Renderer>();
    readonly List<Renderer> stairPosRenderers = new List<Renderer>();
    readonly List<Renderer> stairNegRenderers = new List<Renderer>();
    readonly List<Renderer> lightRenderers = new List<Renderer>();
    ParticleSystem motes;
    ParticleSystemRenderer moteRenderer;
    bool viewKnown;
    bool voidOn = true;
    bool stairsOnPos;   // which flank carries the steps: the one the camera can see

    static readonly int FadeId = Shader.PropertyToID("_Fade");
    static readonly int OpenId = Shader.PropertyToID("_Open");
    static readonly int ColorPropId = Shader.PropertyToID("_Color");
    static readonly int RimColorId = Shader.PropertyToID("_RimColor");
    static readonly int AccentColorId = Shader.PropertyToID("_AccentColor");
    static readonly int VoidColorId = Shader.PropertyToID("_VoidColor");

    Material MakeBlockMaterial(string name)
    {
        Shader s = Shader.Find("TrialMonument/PortalBlock");
        // Without the shader (a build that stripped it) fall back to the flat sprite look.
        Material m = s != null ? new Material(s) : MakeFlatMaterial(Color.white);
        m.name = name;
        return m;
    }

    Renderer AddPart(string name, Mesh mesh, Material mat, int order, List<Renderer> into)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sharedMaterial = mat;
        r.sortingOrder = order;
        into.Add(r);
        return r;
    }

    void BuildArchway()
    {
        // The bright, camera-facing Z face: an entry is seen from behind the runner (its -Z front), an
        // exit from the way out (its +Z front).
        float bz = isExit ? 1f : -1f;

        bodyMat = MakeBlockMaterial("PortalTowerStone (runtime)");
        domeMat = MakeBlockMaterial("PortalTowerDome (runtime)");
        spireMat = MakeBlockMaterial("PortalTowerFinial (runtime)");

        AddPart("Tower", Keep(TowerMesh(bz)), bodyMat, 0, stoneRenderers);
        AddPart("Dome", Keep(DomeMesh(bz)), domeMat, 0, stoneRenderers);
        AddPart("Finial", Keep(FinialMesh(bz)), spireMat, 0, stoneRenderers);
        AddPart("StairsPos", Keep(StairsMesh(1f, bz)), bodyMat, 0, stairPosRenderers);
        AddPart("StairsNeg", Keep(StairsMesh(-1f, bz)), bodyMat, 0, stairNegRenderers);

        // ---- the hole: an opaque quad recessed inside the tower, drawn by TrialMonument/PortalVoid
        voidMat = MakeVoidMaterial();
        var vgo = new GameObject("Void");
        vgo.transform.SetParent(transform, false);
        vgo.transform.localPosition = new Vector3(0f, 0f, VoidZ(isExit));
        vgo.AddComponent<MeshFilter>().sharedMesh = VoidQuadMesh(OpenHalf * 2f, ArchTop);
        voidRenderer = vgo.AddComponent<MeshRenderer>();
        voidRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        voidRenderer.receiveShadows = false;
        voidRenderer.sharedMaterial = voidMat;

        if (isExit) BuildLight();

        Apply();
    }

    Mesh Keep(Mesh m) { ownedMeshes.Add(m); return m; }

    Material MakeVoidMaterial()
    {
        Material m;
        if (voidTemplate != null) m = new Material(voidTemplate);
        else
        {
            Shader s = Shader.Find("TrialMonument/PortalVoid");
            m = s != null ? new Material(s) : MakeFlatMaterial(new Color(0.01f, 0.01f, 0.03f, 1f));
        }
        m.name = "PortalDoorVoid (runtime)";
        if (m.HasProperty("_HalfWidth"))
        {
            // The shader maps the quad's UVs onto these local units, so the hole is exactly the doorway:
            // straight sides up to _SpringY, then a half circle (the arch).
            m.SetFloat("_HalfWidth", OpenHalf);
            m.SetFloat("_BottomY", 0f);
            m.SetFloat("_SpringY", SpringY);
            m.SetFloat("_Rect", 0f);
            m.SetFloat("_Tear", 0.02f);        // a clean architectural edge, not a rip
            m.SetFloat("_RimWidth", isExit ? 0.16f : 0.1f);
            m.SetFloat("_Intensity", isExit ? 1.1f : 0.85f);
        }
        return m;
    }

    // ---- geometry helpers: flat-shaded meshes whose vertex colours carry the shading, so the material
    // colour (the zone accent) can change after the tower is built.

    sealed class MB
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<int> T = new List<int>();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            for (int k = 0; k < 4; k++) C.Add(col);
            T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float grey) => Quad(a, b, c, d, new Color(grey, grey, grey, 1f));

        // The same per-face greys the old archway used: +X 0.78, -X 0.62, the lit Z face 1, the other 0.7,
        // top 0.92, underside 0.5.
        public void Box(Vector3 min, Vector3 max, float brightZ, float topGrey = 0.92f, bool bottom = true)
        {
            Quad(new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, min.y, max.z), 0.78f);
            Quad(new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z), new Vector3(min.x, min.y, min.z), 0.62f);
            Quad(new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, min.y, max.z), brightZ > 0f ? 1f : 0.7f);
            Quad(new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(min.x, min.y, min.z), brightZ < 0f ? 1f : 0.7f);
            Quad(new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z), topGrey);
            if (bottom)
                Quad(new Vector3(min.x, min.y, max.z), new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), 0.5f);
        }

        // A surface of revolution about the Y axis, one flat facet per segment, lit by a fixed direction.
        // prof is (radius, height) going UP; baseY lifts it; tint runs from tint0 (bottom) to tint1 (top).
        public void Lathe(Vector2[] prof, float baseY, int seg, Vector3 light, float lo, float hi, Color tint0, Color tint1)
        {
            float top = prof[prof.Length - 1].y;
            light.Normalize();
            for (int i = 0; i < prof.Length - 1; i++)
            {
                Vector2 p0 = prof[i], p1 = prof[i + 1];
                Vector2 t = p1 - p0;
                Vector2 n2 = new Vector2(t.y, -t.x).normalized;   // outward: the profile always climbs
                float yMid = 0.5f * (p0.y + p1.y);
                Color tint = Color.Lerp(tint0, tint1, top > 0f ? Mathf.Clamp01(yMid / top) : 0f);
                for (int j = 0; j < seg; j++)
                {
                    float a0 = 2f * Mathf.PI * j / seg, a1 = 2f * Mathf.PI * (j + 1) / seg;
                    float am = 0.5f * (a0 + a1);
                    Vector3 n3 = new Vector3(n2.x * Mathf.Cos(am), n2.y, n2.x * Mathf.Sin(am));
                    float g = Mathf.Lerp(lo, hi, Mathf.Clamp01(Vector3.Dot(n3, light)));
                    Vector3 A(Vector2 p, float a) => new Vector3(p.x * Mathf.Cos(a), baseY + p.y, p.x * Mathf.Sin(a));
                    Quad(A(p0, a0), A(p0, a1), A(p1, a1), A(p1, a0), new Color(g * tint.r, g * tint.g, g * tint.b, 1f));
                }
            }
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

    // The face light: from the camera's side (+X a little, and the lit Z face's side), and above.
    static Vector3 LightDir(float brightZ) => new Vector3(0.45f, 0.65f, 0.6f * brightZ);

    // The body: plinth, the square shaft with its round-arched doorway cut through, the flared crown, the
    // battlements and two groove lines under the crown.
    Mesh TowerMesh(float bz)
    {
        var mb = new MB();
        float h = TowerHalf;
        float y0 = FloorY;

        // Plinth: a shade wider than the shaft, standing on the beam's underside.
        float ph = h + 0.08f;
        mb.Box(new Vector3(-ph, BaseY, -ph), new Vector3(ph, y0, ph), bz);

        // Outer flanks.
        mb.Quad(new Vector3(h, y0, -h), new Vector3(h, ShaftTop, -h), new Vector3(h, ShaftTop, h), new Vector3(h, y0, h), 0.78f);
        mb.Quad(new Vector3(-h, y0, h), new Vector3(-h, ShaftTop, h), new Vector3(-h, ShaftTop, -h), new Vector3(-h, y0, -h), 0.62f);

        // Front and back: a wall with the arch cut out of it.
        ArchWall(mb, -h, bz < 0f ? 1f : 0.7f);
        ArchWall(mb, h, bz > 0f ? 1f : 0.7f);

        // The tunnel: the doorway's two straight sides and the curved ceiling, extruded through the tower.
        var arch = ArchPoints();
        mb.Quad(new Vector3(-OpenHalf, y0, -h), new Vector3(-OpenHalf, SpringY, -h), new Vector3(-OpenHalf, SpringY, h), new Vector3(-OpenHalf, y0, h), 0.55f);
        mb.Quad(new Vector3(OpenHalf, y0, h), new Vector3(OpenHalf, SpringY, h), new Vector3(OpenHalf, SpringY, -h), new Vector3(OpenHalf, y0, -h), 0.68f);
        for (int i = 1; i < arch.Count - 2; i++)   // the curve only: the straight sides are the two walls above
        {
            Vector2 a = arch[i], b = arch[i + 1];
            mb.Quad(new Vector3(a.x, a.y, -h), new Vector3(b.x, b.y, -h), new Vector3(b.x, b.y, h), new Vector3(a.x, a.y, h), 0.5f);
        }

        // Two thin groove lines round the shaft under the crown, just proud of the wall.
        foreach (float gy in new[] { ShaftTop - 0.26f, ShaftTop - 0.46f })
        {
            const float gh = 0.035f, e = 0.004f;
            mb.Quad(new Vector3(h + e, gy, -h), new Vector3(h + e, gy + gh, -h), new Vector3(h + e, gy + gh, h), new Vector3(h + e, gy, h), 0.55f);
            mb.Quad(new Vector3(-h - e, gy, h), new Vector3(-h - e, gy + gh, h), new Vector3(-h - e, gy + gh, -h), new Vector3(-h - e, gy, -h), 0.42f);
            mb.Quad(new Vector3(-h, gy, h + e), new Vector3(-h, gy + gh, h + e), new Vector3(h, gy + gh, h + e), new Vector3(h, gy, h + e), bz > 0f ? 0.78f : 0.5f);
            mb.Quad(new Vector3(h, gy, -h - e), new Vector3(h, gy + gh, -h - e), new Vector3(-h, gy + gh, -h - e), new Vector3(-h, gy, -h - e), bz < 0f ? 0.78f : 0.5f);
        }

        // The crown: a slab that flares a little over the shaft.
        mb.Box(new Vector3(-CorbelHalf, ShaftTop, -CorbelHalf), new Vector3(CorbelHalf, CorbelTop, CorbelHalf), bz);

        // Battlements: a ring of merlons with notches between them, a taller L at every corner.
        float c = CorbelHalf, t = MerlonThickness;
        float cornerLen = 0.53f, midHalf = 0.25f;
        float[][] spans = { new[] { -c, -c + cornerLen }, new[] { -midHalf, midHalf }, new[] { c - cornerLen, c } };
        float my = CorbelTop, mt = CorbelTop + MerlonHeight;
        foreach (float[] s in spans)
        {
            mb.Box(new Vector3(s[0], my, -c), new Vector3(s[1], mt, -c + t), bz, 0.98f, false);   // -Z side
            mb.Box(new Vector3(s[0], my, c - t), new Vector3(s[1], mt, c), bz, 0.98f, false);     // +Z side
            mb.Box(new Vector3(-c, my, s[0]), new Vector3(-c + t, mt, s[1]), bz, 0.98f, false);   // -X side
            mb.Box(new Vector3(c - t, my, s[0]), new Vector3(c, mt, s[1]), bz, 0.98f, false);     // +X side
        }

        return mb.ToMesh("PortalTower");
    }

    // The arch outline, left foot to right foot: straight up the left side, over the top, down the right.
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

    // A tower wall at depth z, full width, with the round-arched doorway left open.
    static void ArchWall(MB mb, float z, float grey)
    {
        float h = TowerHalf, y0 = FloorY;
        // Either side of the doorway: solid, floor to shaft top.
        mb.Quad(new Vector3(-h, y0, z), new Vector3(-h, ShaftTop, z), new Vector3(-OpenHalf, ShaftTop, z), new Vector3(-OpenHalf, y0, z), grey);
        mb.Quad(new Vector3(OpenHalf, y0, z), new Vector3(OpenHalf, ShaftTop, z), new Vector3(h, ShaftTop, z), new Vector3(h, y0, z), grey);
        // Above the doorway: strips from the curve of the arch up to the shaft top.
        var arch = ArchPoints();
        for (int i = 1; i < arch.Count - 2; i++)
        {
            Vector2 a = arch[i], b = arch[i + 1];
            mb.Quad(new Vector3(a.x, a.y, z), new Vector3(a.x, ShaftTop, z), new Vector3(b.x, ShaftTop, z), new Vector3(b.x, b.y, z), grey);
        }
    }

    // The onion dome sitting inside the battlements: neck, a belly wider than the neck, then an ogee
    // curve in to a point. Faceted, so it reads like the petals of the reference.
    Mesh DomeMesh(float bz)
    {
        var mb = new MB();
        var prof = new[]
        {
            new Vector2(0.66f, -0.05f), new Vector2(0.66f, 0.00f), new Vector2(0.80f, 0.14f), new Vector2(0.86f, 0.30f),
            new Vector2(0.84f, 0.46f), new Vector2(0.74f, 0.64f), new Vector2(0.56f, 0.84f), new Vector2(0.36f, 1.04f),
            new Vector2(0.20f, 1.22f), new Vector2(0.10f, 1.36f), new Vector2(0.05f, 1.45f)
        };
        mb.Lathe(prof, CorbelTop, 14, LightDir(bz), 0.66f, 1f, Color.white, new Color(1f, 0.95f, 0.82f));
        return mb.ToMesh("PortalTowerDome");
    }

    // The finial: a thin stem, a ball and a small point on top of the dome.
    Mesh FinialMesh(float bz)
    {
        var mb = new MB();
        var prof = new[]
        {
            new Vector2(0.05f, 1.40f), new Vector2(0.035f, 1.50f), new Vector2(0.035f, 1.68f),
            new Vector2(0.075f, 1.72f), new Vector2(0.115f, 1.77f), new Vector2(0.125f, 1.83f), new Vector2(0.105f, 1.90f),
            new Vector2(0.055f, 1.95f), new Vector2(0.025f, 1.96f), new Vector2(0.02f, 2.03f), new Vector2(0.004f, 2.08f)
        };
        mb.Lathe(prof, CorbelTop, 10, LightDir(bz), 0.72f, 1f, Color.white, Color.white);
        return mb.ToMesh("PortalTowerFinial");
    }

    // A short flight of steps against one flank, tallest next to the tower. sign picks the flank
    // (+1 = +X): only the one the camera can see is shown (see SetView).
    Mesh StairsMesh(float sign, float bz)
    {
        var mb = new MB();
        const int steps = 4;
        const float run = 0.3f, rise = 0.35f, halfDepth = 0.62f;
        for (int k = 0; k < steps; k++)
        {
            float xa = sign * (TowerHalf + run * k), xb = sign * (TowerHalf + run * (k + 1));
            float top = FloorY + rise * (steps - k);
            mb.Box(new Vector3(Mathf.Min(xa, xb), BaseY, -halfDepth), new Vector3(Mathf.Max(xa, xb), top, halfDepth), bz, 0.98f, false);
        }
        return mb.ToMesh(sign > 0f ? "PortalTowerStairsPos" : "PortalTowerStairsNeg");
    }

    // ---- the exit's light: a fan of shafts out of the doorway over a soft haze, a pool of light on the
    // path and a few floating motes

    void BuildLight()
    {
        float z0 = VoidZ(true) + 0.02f;

        // Soft haze in the shape of the cone the shafts fan through.
        rayMat = MakeFlatMaterial(Color.white);
        AddPart("Haze", RayConeMesh(z0), rayMat, 6, lightRenderers);

        // The shafts themselves. Without the shader (a stripped build) the haze alone carries the light.
        Shader shaftShader = Shader.Find("TrialMonument/PortalShaft");
        if (shaftShader != null)
        {
            shaftMat = new Material(shaftShader) { name = "PortalDoorShafts (runtime)" };
            AddPart("Rays", RayShaftsMesh(z0), shaftMat, 7, lightRenderers);
        }

        poolMat = MakeFlatMaterial(Color.white);
        var pool = AddPart("LightPool", LightPoolMesh(z0), poolMat, 5, lightRenderers);
        pool.transform.localPosition = new Vector3(0f, 0.02f, 0f);

        // Glowing dust motes drifting slowly inside the cone.
        moteMat = MakeFlatMaterial(Color.white);
        var go = new GameObject("Motes");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, ArchTop * 0.45f, z0 + RayLength * 0.5f);
        motes = go.AddComponent<ParticleSystem>();
        motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = motes.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.04f, 0.16f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
        main.startColor = new Color(1f, 0.92f, 0.62f, 1f);
        main.maxParticles = 45;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.gravityModifier = 0f;

        var emission = motes.emission;
        emission.rateOverTime = 8f;

        var shape = motes.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(2.4f, ArchTop * 0.9f, RayLength * 0.85f);

        var life = motes.colorOverLifetime;
        life.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.25f), new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0f, 1f) });
        life.color = new ParticleSystem.MinMaxGradient(g);

        var noise = motes.noise;
        noise.enabled = true;
        noise.strength = 0.18f;
        noise.frequency = 0.35f;

        moteRenderer = go.GetComponent<ParticleSystemRenderer>();
        moteRenderer.sharedMaterial = moteMat;
        moteRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        moteRenderer.sortingOrder = 8;
        moteRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        moteRenderer.receiveShadows = false;

        motes.Play();
    }

    static float Frac(float x) => x - Mathf.Floor(x);

    // Eleven light shafts fanned out of the doorway: each starts as a thin sliver inside the arch and
    // widens as it runs out. Every shaft is two crossed ribbons (one wide sideways, one wide up and down),
    // so it reads from the camera's high angle as well as from level. uv.x is across a ribbon, uv.y along.
    Mesh RayShaftsMesh(float z0)
    {
        const int N = 11;
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var t = new List<int>();

        void Ribbon(Vector3 p0, Vector3 p1, Vector3 side, float w0, float w1)
        {
            int i = v.Count;
            v.Add(p0 - side * (w0 * 0.5f)); v.Add(p0 + side * (w0 * 0.5f));
            v.Add(p1 + side * (w1 * 0.5f)); v.Add(p1 - side * (w1 * 0.5f));
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        for (int i = 0; i < N; i++)
        {
            float u = i / (float)(N - 1) * 2f - 1f;                       // -1 .. 1 across the doorway
            float j1 = Frac(i * 0.6180339f), j2 = Frac(i * 0.3819660f + 0.25f), j3 = Frac(i * 0.7548777f), j4 = Frac(i * 0.5698403f + 0.1f);
            float yaw = (u * 34f + (j1 - 0.5f) * 8f) * Mathf.Deg2Rad;
            float pitch = Mathf.Lerp(-3f, 20f, j2) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
            Vector3 p0 = new Vector3(u * 0.42f, Mathf.Lerp(0.45f, 1.7f, j1), z0);
            Vector3 p1 = p0 + dir * (RayLength * Mathf.Lerp(0.8f, 1.2f, j3));
            float w1 = Mathf.Lerp(0.3f, 0.6f, j4);
            Vector3 s1 = Vector3.Cross(dir, Vector3.up).normalized;
            Vector3 s2 = Vector3.Cross(dir, s1).normalized;
            Ribbon(p0, p1, s1, 0.06f, w1);
            Ribbon(p0, p1, s2, 0.06f, w1);
        }

        var m = new Mesh { name = "PortalDoorShafts" };
        m.SetVertices(v);
        m.SetUVs(0, uv);
        m.SetTriangles(t, 0);
        m.RecalculateBounds();
        ownedMeshes.Add(m);
        return m;
    }

    // A frustum from the doorway's opening out to a much wider end, alpha 1 at the door falling to 0
    // at the wide end (vertex colour), seen from inside and out.
    Mesh RayConeMesh(float z0)
    {
        float z1 = z0 + RayLength;
        var v = new[]
        {
            new Vector3(-OpenHalf, 0f, z0), new Vector3(OpenHalf, 0f, z0),
            new Vector3(OpenHalf, ArchTop, z0), new Vector3(-OpenHalf, ArchTop, z0),
            new Vector3(-RayFarHalfWidth, -0.6f, z1), new Vector3(RayFarHalfWidth, -0.6f, z1),
            new Vector3(RayFarHalfWidth, ArchTop + 0.9f, z1), new Vector3(-RayFarHalfWidth, ArchTop + 0.9f, z1)
        };
        var c = new Color[8];
        for (int i = 0; i < 4; i++) c[i] = new Color(1f, 1f, 1f, 1f);
        for (int i = 4; i < 8; i++) c[i] = new Color(1f, 1f, 1f, 0f);
        var t = new[]
        {
            0, 4, 5, 0, 5, 1,       // floor of the cone
            3, 2, 6, 3, 6, 7,       // roof
            0, 3, 7, 0, 7, 4,       // left
            1, 5, 6, 1, 6, 2        // right
        };
        var m = new Mesh { name = "PortalDoorHaze", vertices = v, colors = c, triangles = t };
        m.RecalculateBounds();
        ownedMeshes.Add(m);
        return m;
    }

    // Light spilled on the new track: a trapezoid on its top face, bright at the door and gone at the far end.
    Mesh LightPoolMesh(float z0)
    {
        float z1 = z0 + RayLength * 1.05f;
        var v = new[]
        {
            new Vector3(-OpenHalf, 0f, z0), new Vector3(OpenHalf, 0f, z0),
            new Vector3(RayFarHalfWidth * 0.9f, 0f, z1), new Vector3(-RayFarHalfWidth * 0.9f, 0f, z1)
        };
        var c = new[] { new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0f) };
        var m = new Mesh { name = "PortalDoorLightPool", vertices = v, colors = c, triangles = new[] { 0, 2, 1, 0, 3, 2 } };
        m.RecalculateBounds();
        ownedMeshes.Add(m);
        return m;
    }

    Mesh VoidQuadMesh(float w, float h)
    {
        var m = new Mesh { name = "PortalDoorVoidQuad" };
        m.vertices = new[]
        {
            new Vector3(-w * 0.5f, 0f, 0f), new Vector3(w * 0.5f, 0f, 0f),
            new Vector3(w * 0.5f, h, 0f), new Vector3(-w * 0.5f, h, 0f)
        };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateBounds();
        ownedMeshes.Add(m);
        return m;
    }

    /// <summary>
    /// The camera's flat look direction. The void hides what is behind it, which is only right with the
    /// camera on the door's front: looking ALONG an entry door's travel, or AGAINST an exit door's.
    /// From the other side it would hide the runner while it approaches, so it is then left out and only
    /// the stone shows. (The generator forces the camera's yaw for the two door zones so this holds.)
    /// It also picks which flank carries the steps: the one the camera can see.
    /// </summary>
    public void SetView(Vector3 cameraForwardFlat)
    {
        viewKnown = true;
        float facing = Vector3.Dot(cameraForwardFlat, Forward);
        voidOn = isExit ? facing < -0.05f : facing > 0.05f;

        if (Monument)
        {
            // A camera looking toward local -X sees the +X flank, and the other way round.
            Vector3 local = transform.InverseTransformDirection(cameraForwardFlat);
            stairsOnPos = local.x < 0f;
        }
    }

    // ------------------------------------------------------------------ look

    /// <summary>The card's base colour; the tower's stone and the entry hole's rim take it too.</summary>
    public void SetColor(Color c) { baseColor = c; }

    /// <summary>The exit's light: the glowing doorway, the shafts, the haze, the pool of light and the motes.</summary>
    public void SetGlowColor(Color c) { glowColor = c; }

    public void SetReveal(float value) { reveal = Mathf.Clamp01(value); }

    /// <summary>Follows PathDepthShading's own crossed-amount for the leg this door stands on, so a
    /// door left behind fades out in step with its track instead of needing its own effect.</summary>
    public void SetCrossed(float amount) { crossed = Mathf.Clamp01(amount); }

    void Update() => Apply();

    void SetBlock(Material m, Color c, float fade)
    {
        if (m == null) return;
        m.color = c;
        if (m.HasProperty(FadeId)) m.SetFloat(FadeId, fade);
    }

    void ApplyArchway()
    {
        float alpha = reveal * (1f - crossed);
        bool visible = alpha > 0.01f;

        foreach (Renderer r in stoneRenderers) if (r != null) r.enabled = visible;
        foreach (Renderer r in stairPosRenderers) if (r != null) r.enabled = visible && stairsOnPos;
        foreach (Renderer r in stairNegRenderers) if (r != null) r.enabled = visible && !stairsOnPos;
        foreach (Renderer r in lightRenderers) if (r != null) r.enabled = visible;
        if (moteRenderer != null) moteRenderer.enabled = visible;
        if (motes != null)
        {
            var em = motes.emission;
            em.enabled = alpha > 0.5f;
        }
        bool showVoid = visible && (voidOn || !viewKnown);
        if (voidRenderer != null) voidRenderer.enabled = showVoid;
        if (!visible) return;

        Color stone = Color.Lerp(baseColor, Color.white, 0.12f);
        SetBlock(bodyMat, stone, alpha);
        SetBlock(domeMat, Color.Lerp(baseColor, Color.white, 0.4f), alpha);
        SetBlock(spireMat, Color.Lerp(baseColor, Color.white, 0.65f), alpha);

        if (voidMat != null && showVoid)
        {
            // The hole irises open as the door is revealed and closed as it is left behind.
            if (voidMat.HasProperty(OpenId)) voidMat.SetFloat(OpenId, Mathf.Max(0.001f, alpha));
            if (isExit)
            {
                // A warm, bright doorway instead of a dark one.
                Color warm = glowColor;
                if (voidMat.HasProperty(VoidColorId)) voidMat.SetColor(VoidColorId, new Color(warm.r, warm.g * 0.95f, warm.b * 0.8f, 1f));
                if (voidMat.HasProperty(AccentColorId)) voidMat.SetColor(AccentColorId, new Color(1f, 0.7f, 0.42f, 1f));
                if (voidMat.HasProperty(RimColorId)) voidMat.SetColor(RimColorId, new Color(1.5f, 1.3f, 0.95f, 1f));
            }
            else
            {
                if (voidMat.HasProperty(VoidColorId)) voidMat.SetColor(VoidColorId, new Color(0.008f, 0.006f, 0.022f, 1f));
                if (voidMat.HasProperty(AccentColorId)) voidMat.SetColor(AccentColorId, baseColor * 0.8f);
                if (voidMat.HasProperty(RimColorId)) voidMat.SetColor(RimColorId, baseColor * 1.5f);
            }
        }

        if (isExit)
        {
            if (rayMat != null) rayMat.color = new Color(glowColor.r, glowColor.g, glowColor.b, alpha * 0.2f);
            if (poolMat != null) poolMat.color = new Color(glowColor.r, glowColor.g, glowColor.b, alpha * 0.45f);
            if (moteMat != null) moteMat.color = new Color(glowColor.r, glowColor.g, glowColor.b, alpha);
            if (shaftMat != null)
            {
                // HDR warm gold: the shader multiplies this by the shaft's own falloff.
                shaftMat.SetColor(ColorPropId, new Color(glowColor.r * 1.25f, glowColor.g * 0.9f, glowColor.b * 0.6f, 1f));
                shaftMat.SetFloat(FadeId, alpha * 1.3f);
            }
        }
    }

    void Apply()
    {
        if (Monument) { ApplyArchway(); return; }

        float alpha = reveal * (1f - crossed);
        bool visible = alpha > 0.01f;
        if (cardRenderer != null) cardRenderer.enabled = visible;
        if (foldRenderer != null) foldRenderer.enabled = visible;
        if (!visible) return;

        if (cardMaterial != null) cardMaterial.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        if (foldMaterial != null) foldMaterial.color = new Color(FoldTint.r, FoldTint.g, FoldTint.b, alpha);
    }

    void OnDestroy()
    {
        if (cardMaterial != null) Destroy(cardMaterial);
        if (foldMaterial != null) Destroy(foldMaterial);
        foreach (Material m in new[] { bodyMat, domeMat, spireMat, voidMat, rayMat, shaftMat, poolMat, moteMat })
            if (m != null) Destroy(m);
        foreach (Mesh m in ownedMeshes) if (m != null) Destroy(m);
    }
}
