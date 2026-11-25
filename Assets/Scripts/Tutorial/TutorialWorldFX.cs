using UnityEngine;

/// <summary>
/// The tutorial's small world effects, all additive glow (Tutorial/WorldGlow), pooled, no post-processing:
///   Sparks      10-14 success sparks (one ParticleSystem, capped below 80 alive, unscaled time).
///   RingBurst   a ring that opens from the runner at the hand-over.
///   TrackWave   a quick wave of light that runs ahead along the runner's face at the hand-over.
///   Strip       a glowing strip on a face (the obstacle lesson's open face, the first-encounter hints), drawn
///               for the frame it is asked for.
/// Everything is drawn with Graphics.DrawMesh except the sparks.
/// </summary>
public class TutorialWorldFX : MonoBehaviour
{
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    static readonly int PulseId = Shader.PropertyToID("_Pulse");
    static readonly int ProgressId = Shader.PropertyToID("_Progress");
    static readonly int ModeId = Shader.PropertyToID("_Mode");
    static readonly int ZTestId = Shader.PropertyToID("_ZTest");

    ParticleSystem sparks;
    Material sparkMat, glowMat;
    Mesh strip, quad;
    MaterialPropertyBlock mpb;

    // ring burst
    float ringT = -1f;
    Vector3 ringPos;
    float ringSize;
    // track wave
    float waveT = -1f;
    Vector3 waveStart, waveNormal, waveTravel;
    float waveWidth, waveCube;

    public void Init()
    {
        TutorialStyle st = TutorialStyle.Active;
        Shader glow = st.WorldGlow;
        glowMat = new Material(glow) { name = "TutorialGlow" };
        glowMat.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.LessEqual);
        mpb = new MaterialPropertyBlock();

        strip = new Mesh { name = "TutorialStrip" };
        strip.SetVertices(new[] { new Vector3(-0.5f, 0f), new Vector3(0.5f, 0f), new Vector3(0.5f, 1f), new Vector3(-0.5f, 1f) });
        strip.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
        strip.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        strip.RecalculateBounds();

        quad = new Mesh { name = "TutorialQuad" };
        quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) });
        quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
        quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        quad.RecalculateBounds();

        BuildSparks(st, glow);
    }

    void BuildSparks(TutorialStyle st, Shader glow)
    {
        var go = new GameObject("TutorialSparks");
        go.transform.SetParent(transform, false);
        sparks = go.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = sparks.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 1f;
        main.maxParticles = Mathf.Clamp(st.maxParticles, 10, 79);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(st.sparkSeconds * 0.8f, st.sparkSeconds * 1.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
        main.gravityModifier = 0f;

        var emission = sparks.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        var shape = sparks.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;

        var col = sparks.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var size = sparks.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.25f));

        var limit = sparks.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.25f;
        limit.limit = 0.5f;

        sparkMat = new Material(glow) { name = "TutorialSpark" };
        sparkMat.SetFloat(ModeId, 1f);
        sparkMat.SetFloat(IntensityId, 2.6f);
        sparkMat.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.LessEqual);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = sparkMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    /// <summary>10-14 sparks in the success colour at `pos` (scaled to the cube size).</summary>
    public void Sparks(Vector3 pos, float cubeScale = 1f)
    {
        if (sparks == null) return;
        TutorialStyle st = TutorialStyle.Active;
        sparkMat.SetColor(ColorId, st.Success);
        var main = sparks.main;
        main.maxParticles = Mathf.Clamp(st.maxParticles, 10, 79);
        int n = Random.Range(st.sparksMin, st.sparksMax + 1);
        var ep = new ParticleSystem.EmitParams
        {
            position = pos,
            applyShapeToPosition = true,
            startColor = Color.white,
            startSize = 0.11f * cubeScale
        };
        if (!sparks.isPlaying) sparks.Play();
        sparks.Emit(ep, n);
    }

    /// <summary>A ring opening from `pos` (the hand-over).</summary>
    public void RingBurst(Vector3 pos, float cubeScale)
    {
        ringPos = pos;
        ringSize = cubeScale;
        ringT = 0f;
    }

    /// <summary>A wave of light running ahead along a face: from `start`, along `travel`, on the face whose normal is `normal`.</summary>
    public void TrackWave(Vector3 start, Vector3 normal, Vector3 travel, float faceWidth, float cubeScale)
    {
        waveStart = start;
        waveNormal = normal;
        waveTravel = travel;
        waveWidth = faceWidth;
        waveCube = cubeScale;
        waveT = 0f;
    }

    /// <summary>Draws a glowing strip on a face this frame: from `start`, `length` along `travel`, `width` across.</summary>
    public void Strip(Vector3 start, Vector3 normal, Vector3 travel, float width, float length, float intensity, Color color)
    {
        if (glowMat == null || length <= 0.001f || intensity <= 0.001f) return;
        Matrix4x4 m = Matrix4x4.TRS(start, Quaternion.LookRotation(normal, travel), new Vector3(width, length, 1f));
        mpb.Clear();
        mpb.SetColor(ColorId, color);
        mpb.SetFloat(IntensityId, intensity);
        mpb.SetFloat(ModeId, 3f);
        mpb.SetFloat(PulseId, 1f);
        mpb.SetFloat(AlphaId, 1f);
        mpb.SetFloat(ProgressId, 2f);
        Graphics.DrawMesh(strip, m, glowMat, 0, null, 0, mpb, false, false);
    }

    void LateUpdate()
    {
        TutorialStyle st = TutorialStyle.Active;
        float dt = Time.unscaledDeltaTime;
        Color g = st.Guidance;

        if (ringT >= 0f)
        {
            ringT += dt / Mathf.Max(0.05f, st.ringBurstSeconds);
            if (ringT >= 1f) ringT = -1f;
            else
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    float d = Mathf.Lerp(0.4f, st.ringBurstCubes * 2f, TutorialEase.OutCubic(ringT)) * ringSize;
                    Matrix4x4 m = Matrix4x4.TRS(ringPos, Quaternion.LookRotation(cam.transform.forward, cam.transform.up), new Vector3(d, d, 1f));
                    mpb.Clear();
                    mpb.SetColor(ColorId, st.Success);
                    mpb.SetFloat(IntensityId, 2.4f);
                    mpb.SetFloat(ModeId, 2f);
                    mpb.SetFloat(PulseId, 1f);
                    mpb.SetFloat(AlphaId, 1f - TutorialEase.InCubic(ringT));
                    mpb.SetFloat(ProgressId, 2f);
                    Graphics.DrawMesh(quad, m, glowMat, 0, null, 0, mpb, false, false);
                }
            }
        }

        if (waveT >= 0f)
        {
            waveT += dt / Mathf.Max(0.05f, st.handoverWaveSeconds);
            if (waveT >= 1f) waveT = -1f;
            else
            {
                float e = TutorialEase.OutCubic(waveT);
                float head = st.handoverWaveCubes * waveCube * e;
                float len = Mathf.Min(head, 3f * waveCube);
                Vector3 from = waveStart + waveTravel * (head - len);
                float alpha = Mathf.Sin(waveT * Mathf.PI);
                Strip(from, waveNormal, waveTravel, waveWidth * 0.7f, len, 2.2f * alpha, g);
            }
        }
    }

    void OnDestroy()
    {
        if (sparkMat != null) Destroy(sparkMat);
        if (glowMat != null) Destroy(glowMat);
        if (strip != null) Destroy(strip);
        if (quad != null) Destroy(quad);
    }
}
