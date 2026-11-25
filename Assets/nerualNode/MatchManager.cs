using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class MatchManager : MonoBehaviour
{
    public static MatchManager Instance;
    public float mergeRadius = 1.5f;
    public int score;
    public NeuralGlobeGenerator generator;

    private int totalPairs;
    private int mergedPairs;

    void Awake() => Instance = this;

    void Start() => StartCoroutine(InitNextFrame());

    IEnumerator InitNextFrame()
    {
        yield return null;
        if (ColorPhaseManager.Instance == null)
        {
            gameObject.AddComponent<ColorPhaseManager>().Initialize(generator);
        }
        else
        {
            ColorPhaseManager.Instance.Initialize(generator);
        }
        CountPairs();
    }

    void CountPairs()
    {
        var counts = new Dictionary<Color, int>();
        foreach (var n in generator.GetNodes())
        {
            if (n == null || n.isNeutral) continue;
            if (!counts.ContainsKey(n.nodeColor)) counts[n.nodeColor] = 0;
            counts[n.nodeColor]++;
        }
        totalPairs  = counts.Values.Sum(c => c / 2);
        mergedPairs = 0;
        NeuralUIManager.Instance?.Init(totalPairs);
        Debug.Log($"[NeuralScene] Ready — {totalPairs} color pairs to merge.");
    }

    public void TryMerge(NodeView selected)
    {
        NodeView target = FindMatch(selected);
        if (target != null)
            StartCoroutine(DoMerge(selected, target));
        else
        {
            selected.Shake();
            selected.transform.localPosition = selected.originalLocalPos;
        }
    }

    NodeView FindMatch(NodeView sel)
    {
        NodeView best = null;
        float minD = mergeRadius;
        foreach (var n in generator.GetNodes())
        {
            if (n == sel || n == null || n.isNeutral) continue;
            if (n.nodeColor != sel.nodeColor) continue;
            float d = Vector3.Distance(sel.transform.position, n.transform.position);
            if (d < minD) { minD = d; best = n; }
        }
        return best;
    }

    IEnumerator DoMerge(NodeView source, NodeView target)
    {
        Color mergedColor = source.nodeColor;
        score += 100;
        mergedPairs++;
        int remaining = totalPairs - mergedPairs;
        Debug.Log($"[NeuralScene] Merged! Score: {score} | Remaining pairs: {remaining}");
        NeuralUIManager.Instance?.OnMerge(remaining, totalPairs);

        SpawnBurst(source.transform.position, source.nodeColor);
        SpawnBurst(target.transform.position, target.nodeColor);

        var sourceNeighbors = new List<NodeView>(source.neighbors.Where(n => n != target));
        var targetNeighbors = new List<NodeView>(target.neighbors.Where(n => n != source));

        var allSourceLines = new List<LineConnection>(source.connections);
        foreach (var line in allSourceLines)
        {
            if (line == null) continue;
            NodeView other = (line.nodeA == source) ? line.nodeB : line.nodeA;
            source.RemoveConnection(other, line);
            other.RemoveConnection(source, line);
            Destroy(line.gameObject);
        }

        var allTargetLines = new List<LineConnection>(target.connections);
        foreach (var line in allTargetLines)
        {
            if (line == null) continue;
            NodeView other = (line.nodeA == target) ? line.nodeB : line.nodeA;
            target.RemoveConnection(other, line);
            other.RemoveConnection(target, line);
            Destroy(line.gameObject);
        }

        foreach (var sn in sourceNeighbors)
        {
            if (sn == null) continue;
            foreach (var tn in targetNeighbors)
            {
                if (tn == null || tn == sn) continue;
                generator.CreateConnection(sn, tn);
                break;
            }
        }

        generator.GetNodes().Remove(source);
        generator.GetNodes().Remove(target);

        StartCoroutine(target.FadeOut(0.45f));
        yield return StartCoroutine(source.FadeOut(0.45f));

        if (generator.level2 && ColorPhaseManager.Instance != null)
        {
            ColorPhaseManager.Instance.CheckColorCompletion(mergedColor);
        }

        if (mergedPairs >= totalPairs && !generator.level2)
        {
            Debug.Log("[NeuralScene] All pairs merged — YOU WIN!");
            NeuralUIManager.Instance?.OnWin();
            StartCoroutine(WinPulseEffect());
        }
    }

    IEnumerator WinPulseEffect()
    {
        float duration = 2.5f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float s = 1.0f + 0.08f * Mathf.Sin(elapsed * 10f);
            generator.transform.localScale = Vector3.one * s;
            yield return null;
        }
        generator.transform.localScale = Vector3.one;
    }

    public void SpawnBurst(Vector3 pos, Color color)
    {
        var go = new GameObject("MergeBurst");
        go.transform.position = pos;
        var ps   = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startColor    = new ParticleSystem.MinMaxGradient(color);
        main.startSize     = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
        main.startSpeed    = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.maxParticles  = 30;
        main.duration      = 0.25f;
        main.loop          = false;
        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = 0.08f;
        var psr = go.GetComponent<ParticleSystemRenderer>();
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = color;
        psr.material = mat;
        Destroy(go, 2f);
    }
}