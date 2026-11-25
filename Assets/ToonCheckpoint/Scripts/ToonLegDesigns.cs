using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Toon checkpoint: tells every path cube which leg it belongs to, so the toon shader can paint line art that runs the
/// whole length of a leg (ToonLegDesigns.hlsl). Per cube renderer it sets the property-block vector _ToonLeg =
/// (cube centre along its leg, leg length in cubes, leg serial, 1). Legs are numbered in the order they are first seen,
/// so neighbouring legs always get different designs. PathDepthShading reads the renderer's block before writing its
/// tint, so the two never overwrite each other. Shaders without _ToonLeg ignore it; outside the toon look it draws nothing.
/// No gameplay is touched. Pooled lists, no per-frame allocation once the path is laid.
/// </summary>
[DefaultExecutionOrder(970)]
public class ToonLegDesigns : MonoBehaviour
{
    public DynamicPathGenerator pathGen;
    [Tooltip("Seconds between passes over the laid track (new legs and the visual cubes PathDepthShading makes later).")]
    [Range(0.05f, 1f)] public float scanInterval = 0.15f;

    static readonly int LegId = Shader.PropertyToID("_ToonLeg");
    static readonly int RoleId = Shader.PropertyToID("_LTRole");

    readonly Dictionary<DynamicPathGenerator.Segment, int> serials = new Dictionary<DynamicPathGenerator.Segment, int>();
    // Mobile: a leg is painted while young (its visual cubes appear within a moment) or when its cube count changes, then left alone.
    readonly Dictionary<DynamicPathGenerator.Segment, Vector2> settled = new Dictionary<DynamicPathGenerator.Segment, Vector2>();
    readonly Dictionary<Renderer, Vector4> applied = new Dictionary<Renderer, Vector4>();
    readonly List<Renderer> renderers = new List<Renderer>();
    readonly HashSet<DynamicPathGenerator.Segment> live = new HashSet<DynamicPathGenerator.Segment>();
    readonly List<DynamicPathGenerator.Segment> dead = new List<DynamicPathGenerator.Segment>();
    readonly List<Renderer> deadRenderers = new List<Renderer>();
    MaterialPropertyBlock block;
    int nextSerial;
    float nextScan, nextPrune;

    /// <summary>Legs numbered so far (for tooling).</summary>
    public int LegsSeen => nextSerial;

    void Awake()
    {
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        block = new MaterialPropertyBlock();
    }

    void LateUpdate()
    {
        if (pathGen == null || Time.time < nextScan) return;
        nextScan = Time.time + scanInterval;
        Scan();
        if (Time.time >= nextPrune) { nextPrune = Time.time + 3f; Prune(); }
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
                if (seg == null) continue;
                live.Add(seg);
                if (!serials.TryGetValue(seg, out int serial)) { serial = nextSerial++; serials[seg] = serial; }
                int n = seg.cubes.Count;
                if (!settled.TryGetValue(seg, out Vector2 st)) { st = new Vector2(Time.time, n); settled[seg] = st; }
                else if ((int)st.y != n) { st = new Vector2(Time.time, n); settled[seg] = st; }
                else if (Time.time - st.x > 5f) continue;
                for (int i = 0; i < n; i++)
                {
                    GameObject cube = seg.cubes[i];
                    if (cube == null) continue;
                    var v = new Vector4(i + 0.5f, n, serial, 1f);
                    cube.GetComponentsInChildren(true, renderers);
                    for (int r = 0; r < renderers.Count; r++)
                    {
                        Renderer rd = renderers[r];
                        if (applied.TryGetValue(rd, out Vector4 old) && old == v) continue;
                        // Path cube materials only (the look-transition block family): coins and props keep their batching.
                        Material m = rd.sharedMaterial;
                        if (m == null || !m.HasProperty(RoleId)) continue;
                        rd.GetPropertyBlock(block);
                        block.SetVector(LegId, v);
                        rd.SetPropertyBlock(block);
                        applied[rd] = v;
                    }
                }
            }
        }
    }

    // Forget legs and renderers the generator has thrown away.
    void Prune()
    {
        dead.Clear();
        foreach (var kv in serials) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
        for (int i = 0; i < dead.Count; i++) { serials.Remove(dead[i]); settled.Remove(dead[i]); }
        deadRenderers.Clear();
        foreach (var kv in applied) if (kv.Key == null) deadRenderers.Add(kv.Key);
        for (int i = 0; i < deadRenderers.Count; i++) applied.Remove(deadRenderers[i]);
    }
}
