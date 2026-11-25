using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fall + restart support for DynamicPathGenerator: the ghost of the leg the runner missed, and the
/// scene-free reset (the old path sinks into the void, a fresh one rises out of it). Everything the
/// generator lays lives under its own transform (cubes, coins, corner gems), so moving that one
/// transform moves the whole path.
/// </summary>
public partial class DynamicPathGenerator
{
    [Header("Restart")]
    [Tooltip("How far below its place the path sits while it is gone: the old one sinks this far, the new one rises from it.")]
    public float voidDrop = 5f;
    [Tooltip("Seconds the old path takes to sink away after a fall.")]
    public float sinkSeconds = 0.3f;
    [Tooltip("Seconds the new path takes to rise into place.")]
    public float riseSeconds = 0.8f;

    /// <summary>1 = coins full size; the restart shrinks it to 0 (see SinkPath) and back to 1 with the new path.</summary>
    float coinFade = 1f;
    Vector3 pathHome;
    bool pathHomeKnown;
    Coroutine riseRoutine;

    public bool IsRising => riseRoutine != null;
    public float RiseSeconds => riseSeconds;

    void CaptureHome()
    {
        if (pathHomeKnown) return;
        pathHome = transform.position;
        pathHomeKnown = true;
    }

    /// <summary>
    /// Brand-new see-through cubes for the next few steps of the route the runner failed to reach: laid
    /// from the point they fell off, along the way the path actually goes. They are not part of the path
    /// (no segment, no cell, no collider) and start invisible; the caller fades them in and destroys them.
    /// </summary>
    public List<GameObject> GenerateGhostLeg(int count)
    {
        var made = new List<GameObject>();
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null || count <= 0) return made;

        var route = new List<Vector3>();
        GetPathAhead(count, 1, count, route);
        Vector3 size = cubePrefab != null ? cubePrefab.transform.lossyScale : Vector3.one * cubeSize;

        foreach (Vector3 p in route)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = g.GetComponent<Collider>();
            if (col != null) Destroy(col);
            g.name = "GhostPathCube";
            g.transform.position = p;
            g.transform.localScale = size * 1.04f;
            g.GetComponent<MeshRenderer>().material = new Material(shader) { color = new Color(0.7f, 0.95f, 1f, 0f) };
            made.Add(g);
        }
        return made;
    }

    /// <summary>The old path drops away into the void.</summary>
    public IEnumerator SinkPath()
    {
        CaptureHome();
        if (riseRoutine != null) { StopCoroutine(riseRoutine); riseRoutine = null; }

        Vector3 from = transform.position;
        Vector3 to = pathHome + Vector3.down * voidDrop;
        for (float t = 0f; t < sinkSeconds; t += Time.deltaTime)
        {
            float k = t / sinkSeconds;
            transform.position = Vector3.LerpUnclamped(from, to, k * k);
            coinFade = 1f - Mathf.Clamp01(t / (sinkSeconds * 0.5f)); // coins vanish twice as fast as the path
            yield return null;
        }
        transform.position = to;
        coinFade = 0f;
    }

    /// <summary>
    /// Clears the old path, coins and gems, lays a new path, puts the runner back on the first cube, and
    /// lifts the new path up out of the void. No scene reload. The caller resets the runner and camera.
    /// </summary>
    public void SoftReset()
    {
        CaptureHome();
        if (riseRoutine != null) { StopCoroutine(riseRoutine); riseRoutine = null; }

        // Cubes are laid at world positions, so lay them with the path in its place, then drop it.
        transform.position = pathHome;
        coinFade = 1f;
        GenerateNewPath();
        riseRoutine = StartCoroutine(RiseFromVoid());
    }

    IEnumerator RiseFromVoid()
    {
        Vector3 from = pathHome + Vector3.down * voidDrop;
        transform.position = from;
        for (float t = 0f; t < riseSeconds; t += Time.deltaTime)
        {
            float k = 1f - t / riseSeconds;
            transform.position = Vector3.LerpUnclamped(pathHome, from, k * k * k); // ease out
            yield return null;
        }
        transform.position = pathHome;
        riseRoutine = null;
    }
}
