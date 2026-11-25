using UnityEngine;

/// <summary>
/// MainScene prop camera 3.6 (on the "CheckpointRun" object): dresses the runner in the equipped CubeSkin. Classic changes
/// nothing (the checkpoint look keeps driving the runner exactly as before skins). Other skins swap the runner cube's mesh /
/// material, add their following particles, squash a little on each landing, burst on a
/// death, and colour the trail when they override it. Purely cosmetic: speed, collider and controls are never touched.
/// Nothing is allocated per frame (the burst is one pooled instance).
/// </summary>
[DefaultExecutionOrder(2000)]   // after LookTransitionDirector swapped its material copies in
public class SkinApplier : MonoBehaviour
{
    public SkinCatalog catalog;

    CubeRailRunner runner;
    CubeSkin skin;
    MeshRenderer[] cubes;
    Vector3[] baseScales;
    RunnerTrailFX trailFx;
    ParticleSystem burst;
    bool wasOver;
    float squash;

    public CubeSkin Skin => skin;

    void Start()
    {
        if (catalog == null) catalog = SkinCatalog.Load();
        runner = FindFirstObjectByType<CubeRailRunner>();
        if (catalog == null || runner == null) { enabled = false; return; }
        skin = catalog.Equipped;
        if (skin == null) { enabled = false; return; }

        var list = new System.Collections.Generic.List<MeshRenderer>();
        if (skin.characterPrefab == null || skin.sceneCharacter)
            foreach (MeshRenderer r in runner.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name == "Cube") list.Add(r);
            }
        else SpawnCharacter();
        cubes = list.ToArray();
        baseScales = new Vector3[cubes.Length];
        for (int i = 0; i < cubes.Length; i++)
        {
            baseScales[i] = cubes[i].transform.localScale;
            if (skin.mesh != null) cubes[i].GetComponent<MeshFilter>().sharedMesh = skin.mesh;
            if (skin.material != null && !skin.keepSceneLook) cubes[i].sharedMaterial = skin.material;
        }
        trailFx = runner.GetComponent<RunnerTrailFX>();
        if (skin.attachedParticles != null) Instantiate(skin.attachedParticles, runner.transform, false);
        if (skin.deathBurstPrefab != null)
        {
            burst = Instantiate(skin.deathBurstPrefab).GetComponent<ParticleSystem>();
            if (burst != null) burst.gameObject.SetActive(false);
        }
        if (skin.landingSquash > 0f) runner.Landed += OnLanded;
    }

    // A soul character rides on the runner's CubeMesh (so it rolls, hops and squashes with it); it hides the plain cube itself.
    void SpawnCharacter()
    {
        Transform cubeMesh = FindDeep(runner.transform, "CubeMesh");
        Transform parent = cubeMesh != null ? cubeMesh : runner.transform;
        GameObject ch = Instantiate(skin.characterPrefab, parent, false);
        ch.name = skin.characterPrefab.name;
        foreach (Transform t in ch.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = parent.gameObject.layer;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { Transform f = FindDeep(c, name); if (f != null) return f; }
        return null;
    }

    void OnDestroy()
    {
        if (runner == null) return;
        runner.Landed -= OnLanded;
    }

    void OnLanded() => squash = 1f;

    void Update()
    {
        bool over = runner.IsGameOver;
        if (over && !wasOver && burst != null)
        {
            burst.transform.position = runner.transform.position;
            burst.gameObject.SetActive(true);
            burst.Clear(true);
            burst.Play(true);
        }
        wasOver = over;

        if (squash > 0f)
        {
            squash = Mathf.Max(0f, squash - Time.deltaTime / 0.18f);
            float s = skin.landingSquash * Mathf.Sin(squash * Mathf.PI) * squash;
            for (int i = 0; i < cubes.Length; i++)
                if (cubes[i] != null) cubes[i].transform.localScale = Vector3.Scale(baseScales[i], new Vector3(1f + s, 1f - s, 1f + s));
        }
    }

    void LateUpdate()
    {
        if (!skin.overrideTrail || trailFx == null) return;
        // After the look director's accents: the skin's trail wins.
        trailFx.headColor = skin.trailHead;
        trailFx.midColor = skin.trailMid;
        trailFx.tailColor = skin.trailTail;
        trailFx.fastHeadColor = skin.trailHead;
        trailFx.fastMidColor = skin.trailMid;
        trailFx.fastTailColor = skin.trailTail;
    }
}
