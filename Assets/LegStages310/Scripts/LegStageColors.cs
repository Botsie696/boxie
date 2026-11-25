using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WITHIN-LEVEL LEG DESIGN (scene 3.10 only; skill within_level_legs_Design). Every leg is coloured by where it sits in
/// its level: a level runs from portal k's runway out to portal k + 1, and its thirds (LegStagePalette.stageAt) are
/// stage 0 (the look as authored), 1 and 2. A leg takes its stage when it is LAID - far ahead, out of sight - and keeps
/// it, so the change is never seen popping: the road ahead simply arrives in the new colour and the runner crosses into
/// it at a corner, with a pale threshold line round the first cube of the new stage.
///
/// Per renderer it writes the property-block vectors _LegStageFace / _LegStageSel / _LegStageSel2 that
/// FlatFaceBlockPixel reads (zero = nothing, which is every other scene). PathDepthShading and ToonLegDesigns read the
/// block before writing theirs, so nobody overwrites anybody. Obstacle-leg blockers (pillars / triangles) get the
/// stage's obstacle colour through _BaseColor. Background, rocks, lighting and the runner are never touched.
/// </summary>
[DefaultExecutionOrder(975)]
public class LegStageColors : MonoBehaviour
{
    public DynamicPathGenerator pathGen;
    public LegStagePalette palette;
    [Range(0.05f, 1f)] public float scanInterval = 0.12f;
    [Tooltip("Print each leg's level / stage when it is first seen.")]
    public bool log;

    static readonly int FaceId = Shader.PropertyToID("_LegStageFace");
    static readonly int SelId = Shader.PropertyToID("_LegStageSel");
    static readonly int Sel2Id = Shader.PropertyToID("_LegStageSel2");
    static readonly int RoleId = Shader.PropertyToID("_LTRole");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    struct Look { public Vector4 face, sel, sel2; public Color obstacle, trunk, leaves; public int treeShape; public bool any; }

    readonly Dictionary<DynamicPathGenerator.Segment, Look> legs = new Dictionary<DynamicPathGenerator.Segment, Look>();
    readonly Dictionary<Renderer, int> applied = new Dictionary<Renderer, int>();
    // Mobile: a leg is painted while young (visual cubes appear within a moment) or when its cube count changes, then left alone.
    readonly Dictionary<DynamicPathGenerator.Segment, Vector2> settled = new Dictionary<DynamicPathGenerator.Segment, Vector2>();
    readonly List<Renderer> renderers = new List<Renderer>();
    readonly HashSet<DynamicPathGenerator.Segment> live = new HashSet<DynamicPathGenerator.Segment>();
    readonly List<DynamicPathGenerator.Segment> dead = new List<DynamicPathGenerator.Segment>();
    readonly List<Renderer> deadRenderers = new List<Renderer>();
    MaterialPropertyBlock block;
    float nextScan, nextPrune;

    /// <summary>Level (portals crossed) and stage (0..2) of a leg serial; level -1 = before the path starts.</summary>
    public void StageOf(int serial, out int level, out int stage, out bool firstOfStage)
    {
        level = 0; stage = 0; firstOfStage = false;
        if (pathGen == null || palette == null) return;
        int start = 0, end = pathGen.CheckpointLeg(1);
        for (int k = 1; k < 64 && serial >= end; k++) { level = k; start = end; end = pathGen.CheckpointLeg(k + 1); }
        int len = Mathf.Max(3, pathGen.NominalCheckpointLeg(level + 1) - start);   // the level's real length (an endless level's door is out of reach)
        int b1 = start + Mathf.RoundToInt(len * palette.stageAt.x), b2 = start + Mathf.RoundToInt(len * palette.stageAt.y);
        stage = serial >= b2 ? 2 : serial >= b1 ? 1 : 0;
        firstOfStage = stage > 0 && serial == (stage == 2 ? b2 : b1);
        if (level <= 1) { stage = 0; firstOfStage = false; }   // Start + Crimson keep the look as authored (no colour thirds)
    }

    void Awake()
    {
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        block = new MaterialPropertyBlock();
        // 3.10's own switches, set here too so a stale / re-saved scene can never show pillars or leave corner holes.
        if (pathGen != null) { pathGen.obstacleLegTrees = false; pathGen.obstacleLegTriangleChance = 0f; pathGen.cornerFillOtherTurns = true; }   // totem pillars (4 cubes) like 3.9
    }

    void LateUpdate()
    {
        if (pathGen == null || palette == null || palette.levels == null || Time.time < nextScan) return;
        nextScan = Time.time + scanInterval;
        Scan();
        if (Time.time >= nextPrune) { nextPrune = Time.time + 3f; Prune(); }
    }

    Look Resolve(DynamicPathGenerator.Segment seg, out bool firstOfStage)
    {
        StageOf(seg.serial, out int level, out int stage, out firstOfStage);
        var look = new Look();
        if (level < 0 || level >= palette.levels.Length || palette.levels[level] == null) return look;
        LegStagePalette.Level L = palette.levels[level];
        LegStagePalette.Stage st = L.stages != null && stage < L.stages.Length ? L.stages[stage] : null;
        if (st == null) return look;
        bool runway = seg.portalRole == 3;
        // Obstacle legs take the colour their stage is heading to (stage 0 = the look as authored); only the
        // diagonal stripes of the level's runway design stay, so the danger section still reads.
        LegStagePalette.Stage paint = st;
        float stripes = runway && L.obstacleRunway != null ? L.obstacleRunway.stripes : st.stripes;
        if (runway) firstOfStage = false;

        if (paint.face.a > 0.001f) { Color f = paint.face.linear; look.face = new Vector4(f.r, f.g, f.b, 1f); }
        float satMul = 1f, valMul = 1f;
        if (paint.pickFrom.a > 0.001f && paint.pickTo.a > 0.001f)
        {
            Color.RGBToHSV(paint.pickFrom.linear, out float h1, out float s1, out float v1);
            Color.RGBToHSV(paint.pickTo.linear, out float h2, out float s2, out float v2);
            look.sel = new Vector4(h1, paint.pickWidth, Mathf.Repeat(h2 - h1, 1f), 1f);
            satMul = s2 / Mathf.Max(s1, 1e-3f);
            valMul = v2 / Mathf.Max(v1, 1e-3f);
        }
        look.sel2 = new Vector4(satMul, valMul, 0f, stripes);
        look.obstacle = st.obstacle;
        LegStagePalette.TreeStyle(level, stage, out look.treeShape, out look.trunk, out look.leaves);
        look.any = true;
        if (log) Debug.Log($"[LegStage] leg {seg.serial}: level {level} stage {stage}{(runway ? " (obstacle runway)" : "")}{(firstOfStage ? " FIRST" : "")}");
        return look;
    }

    void Scan()
    {
        live.Clear();
        var zones = pathGen.Zones;
        for (int z = 0; z < zones.Count; z++)
        {
            var segs = zones[z].segments;
            for (int s = 0; s < segs.Count; s++)
            {
                DynamicPathGenerator.Segment seg = segs[s];
                if (seg == null || seg.serial < 0) continue;
                live.Add(seg);
                bool first = false;
                if (!legs.TryGetValue(seg, out Look look)) { look = Resolve(seg, out first); legs[seg] = look; }
                else StageOf(seg.serial, out _, out _, out first);
                if (!look.any) continue;
                int cc = seg.cubes.Count + (seg.decoyCubes != null ? seg.decoyCubes.Count : 0);
                if (!settled.TryGetValue(seg, out Vector2 st) || (int)st.y != cc) { st = new Vector2(Time.time, cc); settled[seg] = st; }
                else if (Time.time - st.x > 5f) continue;
                for (int i = 0; i < seg.cubes.Count; i++) Paint(seg.cubes[i], look, first && i == 0, seg.serial);
                if (seg.decoyCubes != null) for (int i = 0; i < seg.decoyCubes.Count; i++) Paint(seg.decoyCubes[i], look, false, seg.serial);
            }
        }
    }

    void Paint(GameObject cube, Look look, bool threshold, int serial)
    {
        if (cube == null) return;
        int key = serial * 2 + (threshold ? 1 : 0);
        cube.GetComponentsInChildren(true, renderers);
        for (int r = 0; r < renderers.Count; r++)
        {
            Renderer rd = renderers[r];
            if (applied.TryGetValue(rd, out int old) && old == key) continue;
            Material m = rd.sharedMaterial;
            if (m == null) continue;
            rd.GetPropertyBlock(block);
            if (IsBlocker(rd.transform))
            {
                // Obstacle leg pillar / triangle: the stage's obstacle colour (look-family material: its faces re-hued,
                // anything else: _BaseColor).
                // Trees: trunk and bright crown (crown shape per level).
                bool trunk = rd.name == "TreeTrunk", crown = rd.name == "TreeCrown";
                Color paintCol = trunk ? look.trunk : crown ? look.leaves : look.obstacle;
                if (crown && rd.TryGetComponent(out MeshFilter mf))
                {
                    Mesh cm = mf.sharedMesh;
                    if (cm != null && cm.name != "TreeCrown" + look.treeShape)
                    {
                        Vector2 ts = pathGen.obstacleLegTreeSize;
                        float cs = pathGen.cubeSize;
                        mf.sharedMesh = ObstacleTreeMesh.Crown(look.treeShape, ts.x * cs, ts.y * cs * 0.55f, ts.y * cs * 0.45f);
                    }
                }
                if (m.HasProperty(FaceId)) { Color o = paintCol.linear; block.SetVector(FaceId, new Vector4(o.r, o.g, o.b, 1f)); }
                else if (m.HasProperty(BaseColorId)) block.SetColor(BaseColorId, paintCol);
                else { applied[rd] = key; continue; }
            }
            else if (m.HasProperty(FaceId) && m.HasProperty(RoleId))
            {
                Vector4 sel2 = look.sel2;
                sel2.z = threshold ? palette.thresholdLine : 0f;
                block.SetVector(FaceId, look.face);
                block.SetVector(SelId, look.sel);
                block.SetVector(Sel2Id, sel2);
            }
            else { applied[rd] = key; continue; }   // coins, props: leave their batching alone
            rd.SetPropertyBlock(block);
            applied[rd] = key;
        }
    }

    // Under an obstacle leg's pole / pillar / triangle (DynamicPathGeneratorObstacleLeg.PlacePole).
    static bool IsBlocker(Transform t)
    {
        for (int d = 0; t != null && d < 4; d++, t = t.parent)
            if (t.name == "ObstaclePole" || t.name == "ObstacleTriangle") return true;
        return false;
    }

    void Prune()
    {
        dead.Clear();
        foreach (var kv in legs) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
        for (int i = 0; i < dead.Count; i++) { legs.Remove(dead[i]); settled.Remove(dead[i]); }
        deadRenderers.Clear();
        foreach (var kv in applied) if (kv.Key == null) deadRenderers.Add(kv.Key);
        for (int i = 0; i < deadRenderers.Count; i++) applied.Remove(deadRenderers[i]);
    }
}
