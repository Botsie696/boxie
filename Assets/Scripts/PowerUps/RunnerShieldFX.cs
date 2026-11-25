using UnityEngine;

/// <summary>
/// The runner's side of the shield: collecting it (bubble wraps the runner, HUD icon), holding it (a
/// thin hex bubble), and THE SAVE - when the runner is about to die the death is cancelled
/// (CubeRailRunner.SaveHook, called at the top of GameOver, before GameOverSequence can start), the
/// runner is put back on the route (CubeRailRunner.ShieldRecover), and the moment is played: a slow-mo
/// dip, the bubble cracking into shards, the correct route flashed ahead, "SAVED!", a flicker while
/// invulnerable, and the HUD icon cracking and falling out.
///
/// All time effects go through TimeScaleMixer so they stack with hit-stop, focus and DebugTimeScale and
/// restore exactly. Pooled and built once; nothing is allocated per frame. Nothing moves the camera.
/// Added to the runner by DynamicPathGenerator when enableShield is on.
/// </summary>
public class RunnerShieldFX : MonoBehaviour
{
    public DynamicPathGenerator gen;
    public bool Held { get; private set; }
    public bool Invulnerable => Time.unscaledTime < invulnUntil;

    CubeRailRunner player;
    RunnerBoostFX boostFx;
    RunnerTrailFX trailFx;
    PowerUpHud hud;
    ShieldSettings S;

    Transform bubbleT;
    MeshRenderer bubbleR;
    Material bubbleMat;
    float wrapT = 1f, crackT = -1f, shimmerClock, pulseClock;
    Vector3 wrapFrom;
    float wrapFromDiameter;

    float hitStopEnd;
    int dipPhase;
    float dipT;
    float invulnUntil;
    Renderer[] flicker;
    bool summaryLogged;

    ParticleSystem shards;
    Material shardMat;

    Transform[] teach;
    Material[] teachMats;
    float teachT = 1f;
    readonly System.Collections.Generic.List<GameObject> cubeBuf = new System.Collections.Generic.List<GameObject>();

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int FadeId = Shader.PropertyToID("_Fade");
    static readonly int WaveId = Shader.PropertyToID("_Wave");
    static readonly int FlashId = Shader.PropertyToID("_Flash");
    static readonly int AlphaId = Shader.PropertyToID("_Alpha");

    void Start()
    {
        player = GetComponent<CubeRailRunner>();
        boostFx = GetComponent<RunnerBoostFX>();
        trailFx = GetComponent<RunnerTrailFX>();
        hud = PowerUpHud.Get();
        S = gen != null ? gen.ShieldConfig : ScriptableObject.CreateInstance<ShieldSettings>();
        if (player != null) player.SaveHook = OnDeath;
        Build();
    }

    void OnDisable()
    {
        TimeScaleMixer.Set(TimeScaleMixer.Source.Shield, 1f);
        if (hitStopEnd > 0f) TimeScaleMixer.Set(TimeScaleMixer.Source.HitStop, 1f);
        hitStopEnd = 0f;
        RestoreFlicker();
    }

    void OnDestroy()
    {
        if (player != null && player.SaveHook == (System.Func<bool, bool>)OnDeath) player.SaveHook = null;
        if (bubbleMat != null) Destroy(bubbleMat);
        if (shardMat != null) Destroy(shardMat);
        if (bubbleT != null) Destroy(bubbleT.gameObject);
        if (teachMats != null) foreach (Material m in teachMats) if (m != null) Destroy(m);
        if (teach != null) foreach (Transform t in teach) if (t != null) Destroy(t.gameObject);
    }

    void OnApplicationPause(bool paused)
    {
        // Never leave the game slowed or frozen because of the shield.
        if (!paused) return;
        dipPhase = 0;
        TimeScaleMixer.Set(TimeScaleMixer.Source.Shield, 1f);
        TimeScaleMixer.Set(TimeScaleMixer.Source.HitStop, 1f);
        hitStopEnd = 0f;
    }

    // ------------------------------------------------------------------ build

    void Build()
    {
        Material b = Resources.Load<Material>("PowerUps/PowerUpBubble");
        bubbleMat = b != null ? new Material(b) : new Material(Shader.Find("Custom/PowerUpBubble"));
        bubbleMat.SetColor(ColorId, S.bodyColor);
        var go = new GameObject("ShieldBubble");
        go.transform.SetParent(transform.parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        bubbleR = go.AddComponent<MeshRenderer>();
        bubbleR.sharedMaterial = bubbleMat;
        bubbleR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bubbleR.receiveShadows = false;
        bubbleR.enabled = false;
        bubbleT = go.transform;

        var sg = new GameObject("ShieldShards");
        sg.transform.SetParent(transform, false);
        shards = sg.AddComponent<ParticleSystem>();
        shards.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = shards.main;
        main.loop = false; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 50;
        main.startSpeed = 0f;
        var em = shards.emission; em.enabled = false;
        var sh = shards.shape; sh.enabled = false;
        var colLife = shards.colorOverLifetime; colLife.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.4f), new GradientAlphaKey(0f, 1f) });
        colLife.color = g;
        var rot = shards.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        var r = sg.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        Material src = Resources.Load<Material>("BoostFX/BoostRing");
        Shader sh2 = src != null ? src.shader : Shader.Find("Runner/TrailFX Spark");
        if (sh2 == null) sh2 = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        shardMat = src != null ? new Material(src) : new Material(sh2);
        if (shardMat.HasProperty("_Shape")) shardMat.SetFloat("_Shape", 2f);   // polygon-ish where the shader has one
        if (shardMat.HasProperty("_Intensity")) shardMat.SetFloat("_Intensity", 2.2f);
        r.sharedMaterial = shardMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        Shader glow = Shader.Find("Custom/SoftGlow");
        Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        int n = Mathf.Max(2, S.teachCubes);
        teach = new Transform[n];
        teachMats = new Material[n];
        for (int i = 0; i < n; i++)
        {
            teachMats[i] = new Material(glow) { name = "ShieldTeach" + i };
            teachMats[i].SetFloat("_Falloff", 1.6f); teachMats[i].SetFloat("_Core", 0.25f);
            var q = new GameObject("ShieldTeach" + i);
            q.transform.SetParent(transform.parent, false);
            q.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = q.AddComponent<MeshRenderer>();
            mr.sharedMaterial = teachMats[i];
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            q.SetActive(false);
            teach[i] = q.transform;
        }
    }

    static Color Lin(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

    void Shards(Vector3 at, int n, float speed, float life, Color c, float size)
    {
        for (int i = 0; i < n; i++)
        {
            Vector3 d = Random.onUnitSphere;
            shards.Emit(new ParticleSystem.EmitParams
            {
                position = at, velocity = d * speed * (0.5f + Random.value),
                startLifetime = life * (0.7f + Random.value * 0.6f),
                startSize = size * (0.6f + Random.value * 0.8f) * Mathf.Max(0.01f, gen.cubeSize),
                startColor = Lin(Color.Lerp(c, Color.white, Random.value * 0.4f))
            }, 1);
        }
    }

    // ------------------------------------------------------------------ collect

    public void Collect(Vector3 at, Vector3 outward, Vector3 faceCentre)
    {
        if (S == null) return;
        TimeScaleMixer.Set(TimeScaleMixer.Source.HitStop, 0f);
        hitStopEnd = Time.unscaledTime + S.hitStopSeconds;
        Haptics.MediumImpact();
        hud.EdgeTint(S.bodyColor, 0.2f);
        if (trailFx != null) trailFx.SmallBurst(S.bodyColor, 6f);
        else if (boostFx != null) boostFx.PowerRing(6f, 0.4f, 0.9f);
        Shards(at, 8, 3f * gen.cubeSize, 0.4f, S.bodyColor, 0.15f);

        if (Held)
        {
            // Already holding one: it becomes coins, flying to the counter as usual.
            CoinCollector cc = gen != null ? gen.Coins : null;
            if (cc != null) for (int i = 0; i < S.coinsIfHeld; i++) cc.Collect(at);
            return;
        }

        Held = true;
        wrapT = 0f; wrapFrom = at;
        wrapFromDiameter = S.bubbleRadius * 2f * gen.cubeSize;
        bubbleR.enabled = true;
        bubbleMat.SetFloat(FlashId, 0f);
        hud.FlyToSlot(PowerUpType.Shield, at, Color.white, 0.45f);
        pulseClock = 0f;
        if (PlayerPrefs.GetInt("PowerUpHint_Shield", 0) == 0)
        {
            hud.Hint(S.firstTimeHint, S.bodyColor, 1.6f);
            PlayerPrefs.SetInt("PowerUpHint_Shield", 1);
        }
    }

    // ------------------------------------------------------------------ the save

    bool OnDeath(bool obstacle)
    {
        if (gen != null && gen.tutorialActive) return false;
        float now = Time.unscaledTime;
        bool invuln = now < invulnUntil;
        if (!invuln && !Held) return false;

        string cause = obstacle ? "obstacle"
            : (player.PlankFallSide >= 0 && Time.time - player.PlankFallTime < 0.6f) ? "plank"
            : (gen.PlayerSegment != null && player.currentCube != null && !gen.PlayerSegment.cubes.Contains(player.currentCube.gameObject)) ? "decoy"
            : "corner";
        PathObstacleGuard hit = obstacle ? PathObstacleGuard.LastHit : null;

        if (!player.ShieldRecover(obstacle))
        {
            Debug.LogWarning($"[Shield] could not recover from {cause} (leg {gen.PlayerSerial}, side {player.currentSideIndex}); the run ends.");
            return false;
        }
        if (gen.logPower) Debug.Log($"[Shield] saved from {cause} on leg {gen.PlayerSerial}");
        if (invuln) return true;                       // a death inside the window is ignored, with no effects

        Held = false;
        invulnUntil = now + S.invulnerableSeconds;
        PowerUpTelemetry.Used(PowerUpType.Shield, cause);
        Vector3 p = transform.position;

        // Slow-mo dip.
        dipPhase = 1; dipT = 0f;

        // Bubble: flash white, then break into shards.
        bubbleR.enabled = true;
        crackT = S.crackFlashSeconds;
        bubbleMat.SetFloat(FlashId, 1f);

        // Obstacle: the pyramid shatters (red shards) and is gone.
        if (hit != null)
        {
            Shards(hit.transform.position, 18, 5f * gen.cubeSize, 0.55f, S.dangerShardColor, 0.2f);
            Destroy(hit.gameObject);
            PathObstacleGuard.LastHit = null;
        }

        // The correct route ahead, as glowing ghost cubes.
        if (S.teachSeconds > 0f)
        {
            gen.CubesAhead(cubeBuf, teach.Length);
            Vector3 want = player.SideNormal(player.currentSideIndex);
            for (int i = 0; i < teach.Length; i++)
            {
                bool on = i < cubeBuf.Count;
                teach[i].gameObject.SetActive(on);
                if (!on) continue;
                Transform ct = cubeBuf[i].transform;
                Vector3 n = ct.up; float best = Vector3.Dot(n, want);
                for (int k = 0; k < 3; k++)
                {
                    Vector3 a = k == 0 ? -ct.up : (k == 1 ? ct.right : -ct.right);
                    float d = Vector3.Dot(a, want);
                    if (d > best) { best = d; n = a; }
                }
                teach[i].position = ct.position + n * (gen.cubeSize * 0.56f);
                teach[i].rotation = Quaternion.LookRotation(n, ct.forward);
                teach[i].localScale = Vector3.one * (gen.cubeSize * 1.5f);
                teachMats[i].SetColor(ColorId, S.bodyColor);
            }
            teachT = 0f;
        }

        hud.PopText("SAVED!", p, S.bodyColor, Color.white, S.savedTextSeconds);
        hud.Hide(PowerUpType.Shield, true);
        hud.EdgeTint(S.bodyColor, 0.3f, 0.35f);
        Haptics.MediumImpact(); Haptics.LightImpact();
        if (trailFx != null) trailFx.SmallBurst(S.bodyColor, 5f);

        // Flicker while invulnerable: the runner's own meshes only.
        GatherFlicker();
        return true;
    }

    void GatherFlicker()
    {
        var list = new System.Collections.Generic.List<Renderer>();
        foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.GetComponent<ParticleSystem>() != null) continue;
            string n = r.gameObject.name;
            if (n.Contains("FX") || n.Contains("Glow") || n.Contains("Burst") || n.Contains("Shield") || n.Contains("Power") || n.Contains("Trail")) continue;
            if (r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name.StartsWith("Custom/Soft")) continue;
            list.Add(r);
        }
        flicker = list.ToArray();
    }

    void RestoreFlicker()
    {
        if (flicker == null) return;
        foreach (Renderer r in flicker) if (r != null) r.enabled = true;
        flicker = null;
    }

    // ------------------------------------------------------------------ per frame

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float now = Time.unscaledTime;

        if (hitStopEnd > 0f && now >= hitStopEnd)
        {
            TimeScaleMixer.Set(TimeScaleMixer.Source.HitStop, 1f);
            hitStopEnd = 0f;
        }

        if (player != null && player.IsGameOver)
        {
            // A run that ended: nothing may stay slowed.
            if (dipPhase != 0) { dipPhase = 0; TimeScaleMixer.Set(TimeScaleMixer.Source.Shield, 1f); }
            if (!summaryLogged && gen != null && gen.schedulerSettings != null && gen.schedulerSettings.logTelemetry)
            { summaryLogged = true; Debug.Log(PowerUpTelemetry.Summary()); }
            RestoreFlicker();
        }
        else summaryLogged = false;

        UpdateDip(dt);
        UpdateBubble(dt);
        UpdateTeach(dt);

        if (flicker != null)
        {
            bool visible = now >= invulnUntil || Mathf.Repeat(now * 10f, 1f) < 0.55f;
            foreach (Renderer r in flicker) if (r != null) r.enabled = visible;
            if (now >= invulnUntil) RestoreFlicker();
        }
    }

    void UpdateDip(float dt)
    {
        if (dipPhase == 0) return;
        dipT += dt;
        float f = 1f;
        if (dipPhase == 1)
        {
            float k = Mathf.Clamp01(dipT / Mathf.Max(0.01f, S.dipIn));
            f = Mathf.Lerp(1f, S.dipScale, k * k * (3f - 2f * k));
            if (k >= 1f) { dipPhase = 2; dipT = 0f; }
        }
        else if (dipPhase == 2)
        {
            f = S.dipScale;
            if (dipT >= S.dipHold) { dipPhase = 3; dipT = 0f; }
        }
        else
        {
            float k = Mathf.Clamp01(dipT / Mathf.Max(0.05f, S.dipOut));
            f = Mathf.Lerp(S.dipScale, 1f, k * k * (3f - 2f * k));
            if (k >= 1f) { dipPhase = 0; f = 1f; }
        }
        TimeScaleMixer.Set(TimeScaleMixer.Source.Shield, f);
    }

    void UpdateBubble(float dt)
    {
        if (bubbleT == null) return;
        float runnerD = Mathf.Max(0.01f, transform.lossyScale.x) * S.heldBubbleScale;

        if (crackT >= 0f)
        {
            // Flash white, then break into shards.
            bubbleT.position = transform.position;
            bubbleT.localScale = Vector3.one * runnerD;
            crackT -= dt;
            if (crackT < 0f)
            {
                crackT = -1f;
                bubbleR.enabled = false;
                bubbleMat.SetFloat(FlashId, 0f);
                Shards(transform.position, S.shardCount, 6f * gen.cubeSize, 0.6f, S.bodyColor, 0.22f);
            }
            return;
        }
        if (!Held) { if (bubbleR.enabled) bubbleR.enabled = false; return; }

        shimmerClock += dt;
        pulseClock += dt;
        if (pulseClock >= S.heldShimmerPeriod)
        {
            pulseClock = 0f;
            hud.Pulse(PowerUpType.Shield, S.bodyColor);
        }
        bubbleMat.SetFloat(WaveId, Mathf.Repeat(shimmerClock / Mathf.Max(0.5f, S.heldShimmerPeriod), 1f) * 1.8f - 0.4f);

        if (wrapT < 1f)
        {
            wrapT += dt / Mathf.Max(0.05f, S.wrapSeconds);
            float k = Mathf.Clamp01(wrapT);
            Vector3 center = Vector3.Lerp(wrapFrom, transform.position, k * k * (3f - 2f * k));
            float d = k < 0.7f ? Mathf.Lerp(wrapFromDiameter, runnerD * 1.15f, k / 0.7f) : Mathf.Lerp(runnerD * 1.15f, runnerD, (k - 0.7f) / 0.3f);
            bubbleT.SetPositionAndRotation(center, Quaternion.identity);
            bubbleT.localScale = Vector3.one * d;
            bubbleMat.SetFloat(AlphaId, Mathf.Lerp(0.25f, S.heldAlpha, k));
        }
        else
        {
            bubbleT.SetPositionAndRotation(transform.position, Quaternion.identity);
            bubbleT.localScale = Vector3.one * runnerD;
            bubbleMat.SetFloat(AlphaId, S.heldAlpha);
        }
    }

    void UpdateTeach(float dt)
    {
        if (teachT >= 1f) return;
        teachT += dt / Mathf.Max(0.05f, S.teachSeconds);
        float a = 1f - Mathf.Clamp01(teachT);
        Camera cam = Camera.main;
        for (int i = 0; i < teach.Length; i++)
        {
            if (!teach[i].gameObject.activeSelf) continue;
            if (teachT >= 1f) { teach[i].gameObject.SetActive(false); continue; }
            teachMats[i].SetFloat(FadeId, a * 0.9f * (1f - i / (float)teach.Length * 0.5f));
        }
    }
}
// 
