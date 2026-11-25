using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SQUARE TILES on the portal runways (MainScene prop). Every cube of the runway into a door and of the
/// runway out of one gets a thin raised square inset on each of its four long faces, so the way to the
/// tower reads as a run of paving squares rather than a plain beam, after the Monument Valley reference.
///
/// Only the SHAPE is added: the cube keeps the route colour PathDepthShading gives it, and the lines
/// are a darker shade of it (RunwayTileOverlay follows it every frame). Off unless portalSquarePaths is
/// ticked, so every other scene generates exactly as before. The two hooks are in
/// DynamicPathGeneratorPortal.cs (OnPortalEntryLaid / OnPortalExitLaid) and DestroyPortalTilesRuntime in
/// DynamicPathGenerator.OnDestroy.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Portal Runway Tiles (MainScene prop)")]
    [Tooltip("Cover every cube of the runway into a door and the runway out of one with a square tile pattern: a thin raised square inset on each long face.")]
    public bool portalSquarePaths = false;
    [Tooltip("Width of the tile lines, in cubes.")]
    [Range(0.02f, 0.2f)] public float portalTileSeamWidth = 0.07f;
    [Tooltip("How far in from the cube's edge the tile square sits, in cubes.")]
    [Range(0.04f, 0.3f)] public float portalTileInset = 0.13f;
    [Tooltip("The lines are the cube's own colour times this: lower = darker lines.")]
    [Range(0.2f, 1f)] public float portalTileSeamShade = 0.6f;

    private Mesh portalTileMesh;
    private Material portalTileMaterial;

    void DecoratePortalRunway(Segment seg)
    {
        if (!portalSquarePaths || seg == null) return;

        Mesh mesh = PortalTileMeshFor();
        Material mat = PortalTileMaterial();
        if (mesh == null || mat == null) return;

        foreach (GameObject cube in seg.cubes)
        {
            if (cube == null || cube.transform == firstCube) continue;
            if (cube.GetComponent<RunwayTileOverlay>() != null) continue;

            var go = new GameObject("RunwayTiles") { layer = cube.layer };
            go.transform.SetParent(cube.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;   // the overlay shows it once PathDepthShading draws the cube

            var overlay = cube.AddComponent<RunwayTileOverlay>();
            overlay.tiles = r;
            overlay.seamShade = portalTileSeamShade;
        }
    }

    // Four thin bars forming a square ring on each of the cube's four long faces (top, right, bottom,
    // left), in the cube's own +-0.5 frame - the same frame the bridge planks' outlines use.
    Mesh PortalTileMeshFor()
    {
        if (portalTileMesh != null) return portalTileMesh;

        float w = Mathf.Max(0.005f, portalTileSeamWidth);
        float e = 0.5f - Mathf.Clamp(portalTileInset, 0.02f, 0.4f);   // centre line of the ring
        const float lift = 0.016f, sink = 0.004f;

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uv = new List<Vector2>();
        var tris = new List<int>();

        for (int face = 0; face < 4; face++)
        {
            Quaternion q = Quaternion.Euler(0f, 0f, -90f * face);   // top -> right -> bottom -> left
            void Bar(Vector3 a, Vector3 b)
            {
                Vector3 p = q * a, r = q * b;
                AppendBoxFaces(verts, norms, uv, tris, Vector3.Min(p, r), Vector3.Max(p, r));
            }

            float y0 = 0.5f - sink, y1 = 0.5f + lift, hw = w * 0.5f;
            foreach (float s in new[] { -e, e })
            {
                Bar(new Vector3(s - hw, y0, -e - hw), new Vector3(s + hw, y1, e + hw));   // along the leg
                Bar(new Vector3(-e - hw, y0, s - hw), new Vector3(e + hw, y1, s + hw));   // across it
            }
        }

        var cols = new List<Color>(verts.Count);
        for (int i = 0; i < verts.Count; i++) cols.Add(Color.white);

        portalTileMesh = new Mesh { name = "PortalRunwayTiles" };
        portalTileMesh.SetVertices(verts);
        portalTileMesh.SetNormals(norms);
        portalTileMesh.SetUVs(0, uv);
        portalTileMesh.SetColors(cols);
        portalTileMesh.SetTriangles(tris, 0);
        portalTileMesh.RecalculateBounds();
        return portalTileMesh;
    }

    // The zone door's opaque, unlit block shader: colour x vertex colour, dithered by _Fade. Both are set
    // per cube through a property block (RunwayTileOverlay).
    Material PortalTileMaterial()
    {
        if (portalTileMaterial != null) return portalTileMaterial;
        Shader s = Shader.Find("TrialMonument/PortalBlock");
        if (s == null) s = Shader.Find("Sprites/Default");
        if (s == null) return null;
        portalTileMaterial = new Material(s) { name = "PortalRunwayTiles (runtime)" };
        return portalTileMaterial;
    }

    void DestroyPortalTilesRuntime()
    {
        if (portalTileMesh != null) Destroy(portalTileMesh);
        portalTileMesh = null;
        if (portalTileMaterial != null) Destroy(portalTileMaterial);
        portalTileMaterial = null;
    }
}
