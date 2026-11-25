using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The fail moment, shared by every scene. Lives on the runner from Awake, and everything expensive (the
/// chime, the shard pool) is built in Start, so a fall allocates almost nothing:
///
///  t = 0     the runner leaves the path; CubeRailRunner hands it to physics (gravity on, velocity kept)
///            so it flies off the edge in an arc. The next few cubes of the missed route glow as a ghost.
///  t = 0.2   the camera, which has been following the arc, locks in place.
///  t = 0.3   the runner shatters mid-air: its renderers and collider go off, the pooled shards are moved
///            to it and released with its momentum, the chime plays.
///
/// RunnerHUD shows the score once <see cref="OverlayReady"/>. CubeRailRunner's soft restart calls
/// <see cref="Release"/> (runner + shards + ghosts back to normal) and <see cref="UnfreezeCamera"/>.
/// </summary>
public class GameOverSequence : MonoBehaviour
{
    public const float OverlayDelay = 1.0f;
    const float CameraDriftSeconds = 0.2f;
    const float ShatterDelay = 0.3f;
    const int ShardCount = 12;
    const int GhostCount = 5;
    static readonly Color PaleCyan = new Color(0.7f, 0.95f, 1f);

    bool active;
    float t;
    float scale = 1f;
    bool cameraFrozen, shattered;
    float shardAge;
    Vector3? impactMomentum;

    Rigidbody body;
    Collider runnerCollider;
    MeshRenderer[] runnerMeshes;
    Material shardMaterial;
    readonly List<Transform> shards = new List<Transform>();
    readonly List<Rigidbody> shardBodies = new List<Rigidbody>();
    readonly List<Behaviour> frozen = new List<Behaviour>();
    readonly List<Transform> ghosts = new List<Transform>();
    readonly List<Material> ghostMats = new List<Material>();

    public bool OverlayReady => active && t >= OverlayDelay;
    public bool IsActive => active;

    void Start()
    {
        body = GetComponent<Rigidbody>();
        runnerCollider = GetComponent<Collider>();
        runnerMeshes = GetComponentsInChildren<MeshRenderer>();
        scale = Mathf.Max(0.01f, transform.lossyScale.x);
        BuildShardPool();
    }

    // Shards are made once, inactive, and re-used on every fall.
    void BuildShardPool()
    {
        Material src = null;
        foreach (var mr in runnerMeshes) if (mr.sharedMaterial != null) { src = mr.sharedMaterial; break; }
        if (src != null)
        {
            shardMaterial = new Material(src);
            if (shardMaterial.HasProperty("_BaseColor")) shardMaterial.SetColor("_BaseColor", PaleCyan);
            if (shardMaterial.HasProperty("_Color")) shardMaterial.SetColor("_Color", PaleCyan);
        }

        var root = new GameObject("RunnerShardPool");
        for (int i = 0; i < ShardCount; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(g.GetComponent<Collider>());
            g.name = "RunnerShard";
            g.transform.SetParent(root.transform, false);
            if (shardMaterial != null) g.GetComponent<MeshRenderer>().sharedMaterial = shardMaterial;
            var rb = g.AddComponent<Rigidbody>();
            rb.useGravity = true;
            g.SetActive(false);
            shards.Add(g.transform);
            shardBodies.Add(rb);
        }
    }

    /// <summary>The runner has left the path and been handed to physics. <paramref name="wallMomentum"/>,
    /// when given (a wall/obstacle hit), overrides the shard burst direction with the runner's speed
    /// at the moment of impact, so the shards keep flying the way it was going even though the runner
    /// itself is stopped dead against the wall rather than sailing through it.</summary>
    public void Begin(Vector3? wallMomentum = null)
    {
        if (active) return;
        active = true;
        t = 0f;
        shattered = false;
        cameraFrozen = false;
        impactMomentum = wallMomentum;
        // Instant: the camera holds exactly where it was and the burst happens on the hit frame (no drift, no stop-then-pop).
        FreezeCamera();
        Shatter();

        var gen = FindFirstObjectByType<DynamicPathGenerator>();
        if (gen == null) return;
        foreach (var g in gen.GenerateGhostLeg(GhostCount))
        {
            ghosts.Add(g.transform);
            ghostMats.Add(g.GetComponent<MeshRenderer>().material);
        }
    }

    void Update()
    {
        if (!active) return;
        t += Time.deltaTime;

        if (!cameraFrozen && t >= CameraDriftSeconds) FreezeCamera();
        if (!shattered && t >= ShatterDelay) Shatter();
        UpdateShards();
        UpdateGhosts();
    }

    // The camera has followed the arc for a moment; now it stays put and watches.
    void FreezeCamera()
    {
        cameraFrozen = true;
        Camera cam = Camera.main;
        foreach (var rig in FindObjectsByType<CameraParentRotator>(FindObjectsSortMode.None))
        {
            if (rig.enabled) frozen.Add(rig);
            rig.enabled = false;
        }
        for (Transform tr = cam != null ? cam.transform : null; tr != null; tr = tr.parent)
        {
            string n = tr.name.ToLowerInvariant();
            bool rigLike = n.Contains("camera") || n.Contains("pivot") || n.Contains("rig") || n.Contains("follow");
            foreach (var mb in tr.GetComponents<MonoBehaviour>())
                if (mb != null && mb != this && rigLike && mb.enabled && mb.GetType().Namespace == null)
                {
                    frozen.Add(mb);
                    mb.enabled = false;
                }
        }
    }

    public void UnfreezeCamera()
    {
        foreach (var mb in frozen) if (mb != null) mb.enabled = true;
        frozen.Clear();
        cameraFrozen = false;
    }

    // No allocation: the pooled shards are moved to the runner and released with its momentum.
    void Shatter()
    {
        shattered = true;
        Vector3 momentum = impactMomentum ?? (body != null ? body.linearVelocity : Vector3.zero);

        foreach (var mr in runnerMeshes) if (mr != null) mr.enabled = false;
        if (runnerCollider != null) runnerCollider.enabled = false;
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true; // hidden now; stop it falling forever
        }

        shardAge = 0f;
        for (int i = 0; i < shards.Count; i++)
        {
            var tr = shards[i];
            tr.position = transform.position + Random.insideUnitSphere * 0.2f * scale;
            tr.rotation = Random.rotation;
            tr.localScale = Vector3.one * Random.Range(0.18f, 0.32f) * scale;
            tr.gameObject.SetActive(true);
            var rb = shardBodies[i];
            rb.linearVelocity = (Random.onUnitSphere + Vector3.up * 0.4f) * Random.Range(2f, 5f) * scale + momentum;
            rb.angularVelocity = Random.onUnitSphere * 8f;
        }

#if UNITY_ANDROID || UNITY_IOS
        Handheld.Vibrate();
#endif
    }

    void UpdateShards()
    {
        if (!shattered) return;
        shardAge += Time.deltaTime;
        float k = Mathf.Clamp01(shardAge / 1.4f);
        for (int i = 0; i < shards.Count; i++)
        {
            if (!shards[i].gameObject.activeSelf) continue;
            if (k >= 1f) { shards[i].gameObject.SetActive(false); continue; }
            float s = Mathf.Lerp(0.25f, 0f, k * k) * scale;
            shards[i].localScale = Vector3.one * s;
        }
    }

    // Ghost cubes light up one after another, then breathe.
    void UpdateGhosts()
    {
        for (int i = 0; i < ghostMats.Count; i++)
        {
            float a = Mathf.Clamp01((t - 0.15f - i * 0.12f) / 0.4f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f - i * 0.6f);
            Color c = ghostMats[i].color;
            c.a = a * Mathf.Lerp(0.12f, 0.3f, pulse);
            ghostMats[i].color = c;
        }
    }

    /// <summary>Runner visible and solid again, shards and ghosts gone. The camera stays frozen until <see cref="UnfreezeCamera"/>.</summary>
    public void Release()
    {
        active = false;
        shattered = false;
        foreach (var mr in runnerMeshes) if (mr != null) mr.enabled = true;
        if (runnerCollider != null) runnerCollider.enabled = true;
        foreach (var s in shards) if (s != null) s.gameObject.SetActive(false);
        foreach (var g in ghosts) if (g != null) Destroy(g.gameObject);
        ghosts.Clear();
        ghostMats.Clear();
    }

    /// <summary>Hides the runner again (the restart keeps it out of sight while the camera rewinds).</summary>
    public void HideRunner()
    {
        foreach (var mr in runnerMeshes) if (mr != null) mr.enabled = false;
    }

    void OnDestroy()
    {
        UnfreezeCamera();
        if (shards.Count > 0 && shards[0] != null) Destroy(shards[0].parent.gameObject);
        foreach (var g in ghosts) if (g != null) Destroy(g.gameObject);
    }
}
