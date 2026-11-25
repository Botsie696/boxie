using UnityEngine;

/// <summary>
/// One source of truth for how the tutorial looks and moves. Every tutorial visual (screen prompts, ghost
/// thumb, world roll arc, ghost runner, chevron boost, sparks, rewind look) reads its colours, timings and
/// sizes from here, live, so changing a value changes it everywhere at once.
///
/// The asset lives at Assets/Tutorial/Resources/TutorialStyle.asset (Resources, because TutorialManager is
/// spawned at run time and has no scene object to hold a reference). If it is missing, a default instance is
/// made with the values below.
///
/// Colours: guidance is NOT stored here - it is the scene's TurnFaceArrows arrowColor at run time (so the
/// tutorial teaches the real cue colour), with guidanceFallback when the scene has no arrows. No other hue
/// is used by the tutorial besides neutralUI (white), dimmer (navy), success (guidance brightened) and
/// danger (the obstacle red).
/// </summary>
[CreateAssetMenu(menuName = "Runner/Tutorial Style", fileName = "TutorialStyle")]
public class TutorialStyle : ScriptableObject
{
    [Header("Colours")]
    [Tooltip("Used only when the scene has no TurnFaceArrows to take the real cue colour from.")]
    public Color guidanceFallback = new Color(1f, 0.788f, 0.290f, 1f);   // #FFC94A
    public Color neutralUI = Color.white;
    public Color dimmer = new Color(0.039f, 0.067f, 0.157f, 1f);        // #0A1128
    [Range(0f, 0.4f)] public float dimmerMaxAlpha = 0.4f;
    [Range(0f, 0.5f)] public float edgeGlowMaxAlpha = 0.5f;
    [Tooltip("Success colour = guidance x this.")]
    [Range(1f, 1.5f)] public float successBrighten = 1.2f;
    [Tooltip("The obstacle red (unchanged from the old tutorial).")]
    public Color danger = new Color(0.95f, 0.3f, 0.3f, 1f);

    [Header("Motion")]
    [Tooltip("Appear: ease-out cubic.")] public float appearSeconds = 0.25f;
    [Tooltip("Disappear: ease-in cubic.")] public float disappearSeconds = 0.20f;
    [Tooltip("The ONE shared loop: every pulse, glow, thumb press, arc pulse and chevron chase boost is phase-locked to it.")]
    public float loopPeriod = 1.6f;
    [Tooltip("Success pops: ease-out-back.")] public float popSeconds = 0.30f;
    public float popOvershoot = 1.1f;

    [Header("Layout (Canvas Scaler: Scale With Screen Size)")]
    public Vector2 referenceResolution = new Vector2(1080f, 1920f);
    [Range(0f, 1f)] public float matchWidthOrHeight = 0.5f;
    [Tooltip("Thumb rest points, as a share of the safe area (x from the left, y from the bottom).")]
    public Vector2 leftThumbRest = new Vector2(0.25f, 0.28f);
    public Vector2 rightThumbRest = new Vector2(0.75f, 0.28f);

    [Header("Ghost thumb (reference pixels)")]
    public float thumbTipRadius = 34f;
    public float thumbBaseRadius = 28f;
    public float thumbLength = 150f;
    [Tooltip("Smooth-min blend between the tip pad and the tapered body.")]
    public float thumbSmoothK = 8f;
    public float thumbEdgeSoftness = 1.5f;
    [Range(0f, 1f)] public float thumbAlpha = 0.55f;
    [Range(0f, 1f)] public float thumbRestAlpha = 0.35f;
    [Range(0f, 0.5f)] public float nailLighten = 0.2f;
    public float shadowBlur = 12f;
    [Range(0f, 1f)] public float shadowAlpha = 0.25f;
    public Vector2 shadowOffset = new Vector2(0f, -6f);
    [Tooltip("Tilt of the thumbs, degrees: the left one leans in from the bottom-left, the right one from the bottom-right.")]
    public float thumbTilt = 22f;
    public float pressDepth = 8f;
    public float pressScale = 0.92f;
    public float pressDownSeconds = 0.12f;
    public float pressReleaseSeconds = 0.18f;
    [Range(0f, 0.6f)] public float shadowPressScale = 0.7f;
    [Tooltip("Two presses this far apart (inside the game's double-tap window).")]
    public float doublePressGap = 0.18f;
    public float contactRippleRadius = 90f;
    public float contactRippleWidth = 2.5f;
    public float contactRippleSeconds = 0.5f;
    public float handsOffRise = 40f;
    [Range(0f, 1f)] public float handsOffAlpha = 0.25f;

    [Header("Screen halves")]
    public float seamWidth = 4f;
    [Range(0f, 1f)] public float seamAlpha = 0.3f;

    [Header("Feedback")]
    public float tapRippleRadius = 70f;
    public float tapRippleSeconds = 0.35f;
    [Range(0f, 1f)] public float tapRippleAlpha = 0.6f;
    [Range(0f, 0.3f)] public float wrongFlashExtra = 0.15f;
    public float wrongFlashSeconds = 0.15f;
    public float wobbleDegrees = 6f;
    public float wobbleSeconds = 0.25f;
    public int sparksMin = 10;
    public int sparksMax = 14;
    public float sparkSeconds = 0.3f;
    [Range(0f, 0.5f)] public float rewindTint = 0.12f;
    [Range(0f, 1f)] public float rewindVignette = 0.35f;

    [Header("World roll arc (cubes)")]
    public float arcRadius = 0.78f;
    public float arcWidth = 0.12f;
    [Range(8, 64)] public int arcSegments = 32;
    public float arrowWidth = 0.26f;
    public float arrowLength = 0.22f;
    [Range(0f, 1f)] public float arcBehindAlpha = 0.3f;
    public float arcSweepSeconds = 0.1f;
    public float arcFadeSeconds = 0.2f;
    [Range(0f, 4f)] public float arcIntensity = 2.2f;

    [Header("Ghost runner")]
    [Range(0f, 1f)] public float ghostBaseAlpha = 0.2f;
    [Range(0f, 1f)] public float ghostRimAlpha = 0.7f;

    [Header("Budget")]
    [Range(10, 80)] public int maxParticles = 79;

    [Header("Chevron boost (TurnFaceArrows tutorial mode)")]
    public TutorialChevronBoost chevrons = new TutorialChevronBoost();

    [Header("Lessons (seconds are game seconds before the corner unless noted)")]
    [Range(0.02f, 0.5f)] public float slowMoScale = 0.1f;
    [Tooltip("Real seconds to ease into slow motion.")] public float slowMoEaseSeconds = 0.25f;
    public float l1Lead = 1.0f;
    public float l2Lead = 0.8f;
    public float l3AssistLead = 1.2f;
    public float l3ChevronLead = 1.0f;
    public float l5Lead = 1.0f;
    public float l6Lead = 1.2f;
    public float l7Lead = 0.9f;
    [Range(0f, 1f)] public float l3AssistBoost = 0.7f;
    [Range(0f, 1f)] public float l3ChevronBoost = 0.4f;
    [Tooltip("Real seconds a correct tap may come BEFORE its prompt and still count.")]
    public float promptGraceSeconds = 0.1f;
    [Tooltip("Real seconds for L0's speed ease from standing to tutorial speed.")]
    public float l0SpeedEaseSeconds = 1.0f;
    [Tooltip("Pause (real seconds) between L0's prompts.")]
    public float l0StepPause = 0.35f;
    public float failTimeScale = 0.3f;
    [Tooltip("Real seconds the miss plays before the rewind.")] public float failSeconds = 0.4f;
    [Tooltip("Game seconds of runner and camera history kept for the rewind.")] public float recordSeconds = 1.5f;
    [Tooltip("Real seconds the rewind plays.")] public float rewindSeconds = 0.6f;
    [Tooltip("Rewind lands this many game seconds before the corner.")] public float rewindToBeforeCorner = 1.2f;
    [Tooltip("Real seconds the camera blends from the rewound pose back to the rig.")] public float rewindCameraBlend = 0.35f;

    [Header("Hand-over")]
    public float handoverWaveSeconds = 0.6f;
    public float handoverWaveCubes = 14f;
    public float ringBurstSeconds = 0.5f;
    public float ringBurstCubes = 3f;

    [Header("First-encounter hints (real game)")]
    [Range(0.2f, 1f)] public float hintTimeScale = 0.5f;
    public float hintEaseInSeconds = 0.2f;
    public float hintEaseOutSeconds = 0.25f;
    public float hintLead = 1.0f;
    public float hintMinGapSeconds = 5f;

    [Header("Shaders (referenced here so builds include them)")]
    public Shader sdfUIShader;
    public Shader worldGlowShader;
    public Shader ghostShader;

    // ------------------------------------------------------------------ runtime

    /// <summary>The guidance colour the scene teaches (TurnFaceArrows.arrowColor), set by TutorialManager.</summary>
    public static Color? GuidanceOverride;

    public Color Guidance
    {
        get
        {
            Color c = GuidanceOverride ?? guidanceFallback;
            c.a = 1f;
            return c;
        }
    }

    public Color Success
    {
        get
        {
            Color g = Guidance;
            return new Color(Mathf.Min(1f, g.r * successBrighten), Mathf.Min(1f, g.g * successBrighten), Mathf.Min(1f, g.b * successBrighten), 1f);
        }
    }

    public Shader SdfUI => sdfUIShader != null ? sdfUIShader : Shader.Find("Tutorial/SdfUI");
    public Shader WorldGlow => worldGlowShader != null ? worldGlowShader : Shader.Find("Tutorial/WorldGlow");
    public Shader Ghost => ghostShader != null ? ghostShader : Shader.Find("Tutorial/GhostFresnel");

    static TutorialStyle active;

    /// <summary>The style asset (Resources/TutorialStyle), or a default instance if it is missing.</summary>
    public static TutorialStyle Active
    {
        get
        {
            if (active != null) return active;
            active = Resources.Load<TutorialStyle>("TutorialStyle");
            if (active == null)
            {
                active = CreateInstance<TutorialStyle>();
                active.name = "TutorialStyle (default)";
            }
            return active;
        }
    }
}

/// <summary>The tutorial-only chevron look (TurnFaceArrows.tutorialMode). tutorialBoost blends from the normal look (0) to this (1).</summary>
[System.Serializable]
public class TutorialChevronBoost
{
    [Range(1, 8)] public int chevronCount = 6;
    public float intensityMultiplier = 1.6f;
    public float scaleMultiplier = 1.15f;
    public float chaseMultiplier = 1.3f;
    [Tooltip("The lane under the chevrons, as a share of the chevrons' glow intensity.")]
    [Range(0f, 1f)] public float laneIntensity = 0.4f;
    [Tooltip("Lane length back from the corner on the current leg, cubes.")]
    public float laneBackCubes = 5f;
    [Tooltip("Lane length onto the next leg, cubes.")]
    public float laneNextCubes = 3f;
    [Tooltip("Lane width as a share of the face.")]
    [Range(0.1f, 1f)] public float laneWidth = 0.55f;
    [Tooltip("The enhanced look shows from this many seconds before the corner.")]
    public float showSeconds = 2.0f;
}

/// <summary>The master clock every looping tutorial animation is phase-locked to (unscaled time, so it runs the same in slow motion).</summary>
public static class TutorialClock
{
    public static float Period => Mathf.Max(0.1f, TutorialStyle.Active.loopPeriod);
    /// <summary>Whole loops since start (fractional).</summary>
    public static float Cycles => Time.unscaledTime / Period;
    /// <summary>Position in the current loop, 0..1.</summary>
    public static float Phase => Mathf.Repeat(Time.unscaledTime / Period, 1f);
    /// <summary>Seconds into the current loop.</summary>
    public static float LoopSeconds => Phase * Period;
}

/// <summary>Easing used by the tutorial. Nothing visible moves linearly.</summary>
public static class TutorialEase
{
    public static float OutCubic(float x) { x = Mathf.Clamp01(x); float k = 1f - x; return 1f - k * k * k; }
    public static float InCubic(float x) { x = Mathf.Clamp01(x); return x * x * x; }
    public static float InOutCubic(float x)
    {
        x = Mathf.Clamp01(x);
        return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) * 0.5f;
    }
    /// <summary>Ease-out-back that peaks at `overshoot` (1.1 = 10% past the end).</summary>
    public static float OutBack(float x, float overshoot)
    {
        x = Mathf.Clamp01(x);
        // c chosen so the curve's peak is roughly `overshoot`: c = 1.70158 gives about 1.1.
        float c1 = 1.70158f * Mathf.Max(0f, overshoot - 1f) / 0.1f;
        float c3 = c1 + 1f;
        float k = x - 1f;
        return 1f + c3 * k * k * k + c1 * k * k;
    }

    /// <summary>
    /// A fade that appears over `appear` seconds (ease-out cubic) and disappears over `disappear` seconds
    /// (ease-in cubic). Keep `t` (linear 0..1) between calls; the return value is what to show.
    /// </summary>
    public static float Fade(ref float t, bool on, float appear, float disappear, float dt)
    {
        t = Mathf.MoveTowards(t, on ? 1f : 0f, dt / Mathf.Max(0.01f, on ? appear : disappear));
        // Appear: OutCubic(t). Disappear: 1 - InCubic(1 - t) == OutCubic(t). One curve, two speeds.
        return OutCubic(t);
    }

    /// <summary>Smooth 0..1..0 bump: rises over [start, start+rise] (ease-in), falls over the next `fall` seconds (ease-out).</summary>
    public static float Press(float s, float start, float rise, float fall)
    {
        if (s < start) return 0f;
        if (s < start + rise) return InCubic((s - start) / Mathf.Max(0.001f, rise));
        if (s < start + rise + fall) return 1f - OutCubic((s - start - rise) / Mathf.Max(0.001f, fall));
        return 0f;
    }
}
