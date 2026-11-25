using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The tutorial's on-screen prompts. No words: the screen halves and a ghost thumb say where and when to tap.
///
///   Dimmer      the half NOT to tap gets a navy shadow, darkest at the phone's outer edge (max 40%).
///   Edge glow   the half to tap glows in the guidance colour from its outer edge (max 50%).
///   Ghost thumb a thumb seen from above (SDF: tapered capsule + tip pad + faint nail, soft drop shadow) resting at
///               25% / 75% across and 28% up the safe area. It presses (down 8 px, 0.92 scale, shadow shrinks)
///               and lets one ripple ring go at contact. Double press: two presses 0.18 s apart.
///   Seam        a soft vertical line down the middle (L0 only).
///   Feedback    a ripple at every real touch, a flash + thumb wobble for a tap on the wrong half, a pop on success.
///   Rewind      a white tint and a vignette while time runs backwards.
///
/// Every loop (thumb press, glow, ripple) runs on TutorialClock, so the screen and the world arc pulse together.
/// Everything runs on unscaled time (it animates the same in slow motion), every colour, size and timing comes
/// from TutorialStyle, and nothing here is a raycast target: touches all go to the game.
/// Screen sides: -1 = left half, +1 = right half (the halves CubeRailRunner reads taps from).
/// </summary>
public class TutorialUIController : MonoBehaviour
{
    public enum PromptKind { Tap, DoubleTap, TwoTaps }

    // ------------------------------------------------------------------ timing shared with the world arc

    const float PressStart = 0.25f;   // seconds into each loop the (first) press starts

    /// <summary>How far the thumb is pressed at `s` seconds into the loop (0..1).</summary>
    public static float PressAt(float s, PromptKind kind, TutorialStyle st)
    {
        if (kind == PromptKind.Tap) return TutorialEase.Press(s, PressStart, st.pressDownSeconds, st.pressReleaseSeconds);
        // Two quicker presses whose contacts are doublePressGap apart.
        float down = Mathf.Min(st.pressDownSeconds, st.doublePressGap * 0.4f);
        float up = Mathf.Min(st.pressReleaseSeconds, st.doublePressGap * 0.5f);
        return Mathf.Max(TutorialEase.Press(s, PressStart, down, up),
                         TutorialEase.Press(s, PressStart + st.doublePressGap, down, up));
    }

    /// <summary>Seconds into the loop of the (last) contact.</summary>
    public static float ContactAt(PromptKind kind, TutorialStyle st)
    {
        if (kind == PromptKind.Tap) return PressStart + st.pressDownSeconds;
        float down = Mathf.Min(st.pressDownSeconds, st.doublePressGap * 0.4f);
        return PressStart + st.doublePressGap + down;
    }

    /// <summary>The shared prompt pulse (0..1), peaking at contact: glows, the world arc and the chevron boost use it.</summary>
    public static float Pulse(PromptKind kind)
    {
        TutorialStyle st = TutorialStyle.Active;
        float s = TutorialClock.LoopSeconds;
        float d = s - ContactAt(kind, st);
        float p = TutorialClock.Period;
        if (d > p * 0.5f) d -= p;
        if (d < -p * 0.5f) d += p;
        return Mathf.Lerp(0.35f, 1f, Mathf.Exp(-(d * d) / (0.22f * 0.22f)));
    }

    // ------------------------------------------------------------------ state

    class Thumb
    {
        public RectTransform root, body, shadow, ripple;
        public Image bodyImg, shadowImg, rippleImg;
        public Material bodyMat, shadowMat, rippleMat;
        public float vis, level, rise, wobbleT = -1f, popT = -1f, rippleT = -1f, lastPress;
        public int side;
    }

    class Half
    {
        public Image dim, glow;
        public float dimFade, glowFade, flashT = -1f;
    }

    class TapRipple
    {
        public RectTransform rt;
        public Image img;
        public Material mat;
        public float t = -1f;
    }

    TutorialStyle style;
    Canvas canvas;
    CanvasScaler scaler;
    RectTransform canvasRect, safeRect;
    Thumb left, right;
    Half halfL, halfR;
    Image seam, tint, vignette;
    Material seamMat, vignetteMat;
    readonly TapRipple[] ripples = new TapRipple[6];
    Rect appliedSafe;
    Vector2 appliedScreen;

    int promptSide;
    PromptKind promptKind;
    bool promptDim, promptGlow, promptThumb;
    bool seamOn, restBoth, handsOff;
    float seamFade, rewindFade, rewindTarget;

    public int PromptSide => promptSide;

    // ------------------------------------------------------------------ API used by TutorialManager

    /// <summary>Prompts one screen half. dim: shade the other half. glow: light this half's edge. thumb: show the ghost thumb pressing.</summary>
    public void ShowPrompt(int side, PromptKind kind, bool dim, bool glow, bool thumb)
    {
        promptSide = side >= 0 ? 1 : -1;
        promptKind = kind;
        promptDim = dim;
        promptGlow = glow;
        promptThumb = thumb;
        handsOff = false;
    }

    /// <summary>No prompt (the halves, thumbs and seam fade out; L0's resting thumbs stay if SetRest is on).</summary>
    public void Hide()
    {
        promptSide = 0;
        promptDim = promptGlow = promptThumb = false;
        handsOff = false;
    }

    /// <summary>L0: both thumbs resting on screen (the unprompted one dimmer), and the centre seam.</summary>
    public void SetRest(bool bothThumbs, bool seamVisible)
    {
        restBoth = bothThumbs;
        seamOn = seamVisible;
    }

    /// <summary>"No tap needed": both thumbs rise and fade to a quarter, and hold there.</summary>
    public void HandsOff(bool on)
    {
        handsOff = on;
        if (on) { promptSide = 0; promptDim = promptGlow = promptThumb = false; }
    }

    /// <summary>A tap on the wrong half: that half's dimmer flashes, the prompted thumb wobbles twice. No penalty.</summary>
    public void WrongHalf(int tappedSide)
    {
        Half h = tappedSide < 0 ? halfL : halfR;
        if (h != null) h.flashT = 0f;
        Thumb t = promptSide < 0 ? left : promptSide > 0 ? right : null;
        if (t != null) t.wobbleT = 0f;
        else { if (left != null) left.wobbleT = 0f; if (right != null) right.wobbleT = 0f; }
        Haptics.LightImpact();
    }

    /// <summary>The prompted thumb pops (ease-out-back) and fades: the lesson step was done.</summary>
    public void SuccessPop()
    {
        Thumb t = promptSide < 0 ? left : promptSide > 0 ? right : null;
        if (t != null) t.popT = 0f;
    }

    /// <summary>A ripple where a finger actually touched (screen pixels).</summary>
    public void TouchRipple(Vector2 screenPos)
    {
        if (canvasRect == null) return;
        // A free ripple, else the one that has run longest.
        TapRipple best = null;
        foreach (TapRipple r in ripples)
        {
            if (r == null) continue;
            if (r.t < 0f) { best = r; break; }
            if (best == null || r.t > best.t) best = r;
        }
        if (best == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, null, out Vector2 local)) return;
        best.rt.anchoredPosition = local;
        best.t = 0f;
        best.img.enabled = true;
    }

    /// <summary>The rewind look on (1) or off (0).</summary>
    public void SetRewind(bool on) { rewindTarget = on ? 1f : 0f; }

    // ------------------------------------------------------------------ per frame

    void Update()
    {
        if (style == null) style = TutorialStyle.Active;
        float dt = Time.unscaledDeltaTime;
        FitSafeArea();
        ApplyScaler();

        Color guidance = style.Guidance;
        float pulse = promptSide != 0 ? Pulse(promptKind) : 0f;

        UpdateHalf(halfL, -1, guidance, pulse, dt);
        UpdateHalf(halfR, 1, guidance, pulse, dt);
        UpdateThumb(left, dt);
        UpdateThumb(right, dt);

        // Seam
        float sf = TutorialEase.Fade(ref seamFade, seamOn, style.appearSeconds, style.disappearSeconds, dt);
        seam.color = WithAlpha(style.neutralUI, style.seamAlpha * sf);
        seam.enabled = sf > 0.001f;
        seamMat.SetFloat(StrokeId, style.seamWidth);
        seamMat.SetFloat(SoftId, 2f);
        SetSize(seamMat, seam.rectTransform);

        // Real-tap ripples
        foreach (TapRipple r in ripples)
        {
            if (r == null || r.t < 0f) continue;
            r.t += dt / Mathf.Max(0.05f, style.tapRippleSeconds);
            if (r.t >= 1f) { r.t = -1f; r.img.enabled = false; continue; }
            float e = TutorialEase.OutCubic(r.t);
            float d = Mathf.Lerp(8f, style.tapRippleRadius * 2f, e) + 8f;
            r.rt.sizeDelta = new Vector2(d, d);
            r.img.color = WithAlpha(style.neutralUI, style.tapRippleAlpha * (1f - TutorialEase.InCubic(r.t)));
            r.mat.SetFloat(StrokeId, style.contactRippleWidth);
            r.mat.SetFloat(SoftId, 1.2f);
            SetSize(r.mat, r.rt);
        }

        // Rewind look
        float rf = TutorialEase.Fade(ref rewindFade, rewindTarget > 0.5f, style.appearSeconds, style.disappearSeconds, dt);
        tint.color = WithAlpha(Color.white, style.rewindTint * rf);
        tint.enabled = rf > 0.001f;
        vignette.color = WithAlpha(style.dimmer, style.rewindVignette * rf);
        vignette.enabled = rf > 0.001f;
    }

    void UpdateHalf(Half h, int side, Color guidance, float pulse, float dt)
    {
        bool prompted = promptSide == side;
        bool dimmed = promptDim && promptSide == -side;
        float df = TutorialEase.Fade(ref h.dimFade, dimmed, style.appearSeconds, style.disappearSeconds, dt);
        float flash = 0f;
        if (h.flashT >= 0f)
        {
            h.flashT += dt / Mathf.Max(0.02f, style.wrongFlashSeconds);
            flash = h.flashT >= 1f ? 0f : Mathf.Sin(h.flashT * Mathf.PI);
            if (h.flashT >= 1f) h.flashT = -1f;
        }
        h.dim.color = WithAlpha(style.dimmer, Mathf.Clamp01(style.dimmerMaxAlpha * df + style.wrongFlashExtra * flash));
        h.dim.enabled = h.dim.color.a > 0.001f;

        float gf = TutorialEase.Fade(ref h.glowFade, prompted && promptGlow, style.appearSeconds, style.disappearSeconds, dt);
        // Light seeps in from the outer edge with the pulse: the lit strip widens toward the middle as it brightens.
        float w = Mathf.Lerp(0.45f, 1f, pulse);
        RectTransform rt = h.glow.rectTransform;
        if (side < 0) { rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(0.5f * w, 1f); }
        else { rt.anchorMin = new Vector2(1f - 0.5f * w, 0f); rt.anchorMax = new Vector2(1f, 1f); }
        h.glow.color = WithAlpha(guidance, style.edgeGlowMaxAlpha * gf * pulse);
        h.glow.enabled = h.glow.color.a > 0.001f;
    }

    void UpdateThumb(Thumb t, float dt)
    {
        bool pressing = promptThumb && promptSide == t.side;
        bool visible = pressing || handsOff || restBoth;
        float targetLevel = pressing ? style.thumbAlpha : handsOff ? style.handsOffAlpha : style.thumbRestAlpha;
        float vis = TutorialEase.Fade(ref t.vis, visible || t.popT >= 0f, style.appearSeconds, style.disappearSeconds, dt);
        t.level = Mathf.MoveTowards(t.level <= 0f ? targetLevel : t.level, targetLevel, dt * 2.5f);
        t.rise = Mathf.MoveTowards(t.rise, handsOff ? 1f : 0f, dt / Mathf.Max(0.05f, style.appearSeconds));

        float loopS = TutorialClock.LoopSeconds;
        float press = pressing ? PressAt(loopS, promptKind, style) : 0f;

        // Contact ripple: starts on the beat each press reaches the bottom (both presses of a double).
        if (pressing)
        {
            float last = ContactAt(promptKind, style);
            float first = promptKind == PromptKind.Tap ? last : last - style.doublePressGap;
            if (Crossed(t.lastPress, loopS, first) || Crossed(t.lastPress, loopS, last)) t.rippleT = 0f;
        }
        t.lastPress = loopS;

        // Pop on success: ease-out-back up, then away.
        float pop = 1f, popAlpha = 1f;
        if (t.popT >= 0f)
        {
            t.popT += dt / Mathf.Max(0.05f, style.popSeconds);
            if (t.popT >= 1f) { t.popT = -1f; }
            else
            {
                pop = Mathf.LerpUnclamped(1f, style.popOvershoot, TutorialEase.OutBack(t.popT, style.popOvershoot) * (1f - t.popT));
                popAlpha = 1f - TutorialEase.InCubic(t.popT);
            }
        }

        // Wobble: two quick swings of wobbleDegrees, dying away.
        float wobble = 0f;
        if (t.wobbleT >= 0f)
        {
            t.wobbleT += dt / Mathf.Max(0.05f, style.wobbleSeconds);
            if (t.wobbleT >= 1f) t.wobbleT = -1f;
            else wobble = style.wobbleDegrees * Mathf.Sin(t.wobbleT * Mathf.PI * 4f) * (1f - t.wobbleT);
        }

        // Pose: rotated about the tip; enters along its own axis from the bottom corner; presses straight down.
        float tilt = t.side < 0 ? -style.thumbTilt : style.thumbTilt;
        Quaternion rot = Quaternion.Euler(0f, 0f, tilt + wobble);
        Vector2 axisDown = rot * Vector2.down;
        Vector2 rest = Vector2.zero;
        Vector2 enter = axisDown * (1f - vis) * 140f;
        Vector2 offset = rest + enter + Vector2.up * (style.handsOffRise * TutorialEase.OutCubic(t.rise)) + Vector2.down * (style.pressDepth * press);
        float scale = Mathf.Lerp(1f, style.pressScale, press) * pop;

        t.body.anchoredPosition = offset;
        t.body.localRotation = rot;
        t.body.localScale = new Vector3(scale, scale, 1f);
        t.shadow.anchoredPosition = offset + style.shadowOffset * Mathf.Lerp(1f, 0.5f, press);
        t.shadow.localRotation = rot;
        float ss = scale * Mathf.Lerp(1f, style.shadowPressScale, press);
        t.shadow.localScale = new Vector3(ss, ss, 1f);

        float a = vis * t.level * popAlpha;
        t.bodyImg.color = WithAlpha(style.neutralUI, a);
        t.shadowImg.color = WithAlpha(Color.black, style.shadowAlpha * a / Mathf.Max(0.01f, style.thumbAlpha));
        t.bodyImg.enabled = t.shadowImg.enabled = a > 0.002f;
        ApplyThumbShape(t);

        // Contact ripple from the tip.
        if (t.rippleT >= 0f)
        {
            t.rippleT += dt / Mathf.Max(0.05f, style.contactRippleSeconds);
            if (t.rippleT >= 1f) { t.rippleT = -1f; t.rippleImg.enabled = false; }
            else
            {
                float e = TutorialEase.OutCubic(t.rippleT);
                float d = Mathf.Lerp(12f, style.contactRippleRadius * 2f, e) + 8f;
                t.ripple.anchoredPosition = offset;
                t.ripple.sizeDelta = new Vector2(d, d);
                t.rippleImg.color = WithAlpha(style.neutralUI, style.thumbAlpha * (1f - TutorialEase.InCubic(t.rippleT)) * vis);
                t.rippleImg.enabled = true;
                t.rippleMat.SetFloat(StrokeId, style.contactRippleWidth);
                t.rippleMat.SetFloat(SoftId, 1.2f);
                SetSize(t.rippleMat, t.ripple);
            }
        }
    }

    // True if the loop clock passed `at` between two frames (handles the wrap at the end of the loop).
    static bool Crossed(float prev, float now, float at)
    {
        if (now >= prev) return prev < at && at <= now;
        return at > prev || at <= now;
    }

    void ApplyThumbShape(Thumb t)
    {
        Vector2 size = ThumbRectSize();
        t.body.sizeDelta = size;
        t.shadow.sizeDelta = size;
        Vector2 pivot = new Vector2(0.5f, (size.y * 0.5f + style.thumbLength * 0.5f) / size.y);   // the tip
        t.body.pivot = pivot;
        t.shadow.pivot = pivot;
        var shape = new Vector4(style.thumbTipRadius, style.thumbBaseRadius, style.thumbLength, style.thumbSmoothK);
        t.bodyMat.SetVector(ThumbId, shape);
        t.bodyMat.SetFloat(SoftId, style.thumbEdgeSoftness);
        t.bodyMat.SetFloat(NailId, style.nailLighten);
        t.bodyMat.SetVector(SizeId, size);
        t.shadowMat.SetVector(ThumbId, shape);
        t.shadowMat.SetFloat(SoftId, style.shadowBlur);
        t.shadowMat.SetFloat(NailId, 0f);
        t.shadowMat.SetVector(SizeId, size);
    }

    Vector2 ThumbRectSize()
    {
        float pad = style.shadowBlur + 10f;
        return new Vector2(style.thumbTipRadius * 2f + pad * 2f, style.thumbLength + pad * 2f);
    }

    void FitSafeArea()
    {
        Rect sa = Screen.safeArea;
        var screen = new Vector2(Screen.width, Screen.height);
        if (sa == appliedSafe && screen == appliedScreen) return;
        appliedSafe = sa;
        appliedScreen = screen;
        if (screen.x <= 0f || screen.y <= 0f) return;
        safeRect.anchorMin = new Vector2(sa.xMin / screen.x, sa.yMin / screen.y);
        safeRect.anchorMax = new Vector2(sa.xMax / screen.x, sa.yMax / screen.y);
        safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;
    }

    void ApplyScaler()
    {
        scaler.referenceResolution = style.referenceResolution;
        scaler.matchWidthOrHeight = style.matchWidthOrHeight;
        PlaceRest(left, style.leftThumbRest);
        PlaceRest(right, style.rightThumbRest);
    }

    static void PlaceRest(Thumb t, Vector2 rest)
    {
        t.root.anchorMin = t.root.anchorMax = rest;
        t.root.anchoredPosition = Vector2.zero;
    }

    static Color WithAlpha(Color c, float a) { c.a = Mathf.Clamp01(a); return c; }

    static readonly int SizeId = Shader.PropertyToID("_Size");
    static readonly int SoftId = Shader.PropertyToID("_Softness");
    static readonly int StrokeId = Shader.PropertyToID("_Stroke");
    static readonly int ThumbId = Shader.PropertyToID("_Thumb");
    static readonly int NailId = Shader.PropertyToID("_Nail");
    static readonly int ShapeId = Shader.PropertyToID("_Shape");

    static void SetSize(Material m, RectTransform rt)
    {
        Vector2 s = rt.rect.size;
        if (s.x <= 0f || s.y <= 0f) s = rt.sizeDelta;
        m.SetVector(SizeId, new Vector4(s.x, s.y, 0f, 0f));
    }

    void OnDestroy()
    {
        foreach (Material m in new[] { seamMat, vignetteMat,
                     left?.bodyMat, left?.shadowMat, left?.rippleMat, right?.bodyMat, right?.shadowMat, right?.rippleMat })
            if (m != null) Destroy(m);
        foreach (TapRipple r in ripples) if (r != null && r.mat != null) Destroy(r.mat);
    }

    // ------------------------------------------------------------------ builder

    /// <summary>Builds the prompt canvas and returns its controller, fully wired. Nothing is a raycast target.</summary>
    public static TutorialUIController Build()
    {
        TutorialStyle st = TutorialStyle.Active;
        var canvasGo = new GameObject("TutorialCanvas", typeof(RectTransform));
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = st.referenceResolution;
        scaler.matchWidthOrHeight = st.matchWidthOrHeight;
        // No GraphicRaycaster: nothing here is tappable, so touches all go to the game.

        var ctrl = canvasGo.AddComponent<TutorialUIController>();
        ctrl.style = st;
        ctrl.canvas = canvas;
        ctrl.scaler = scaler;
        ctrl.canvasRect = (RectTransform)canvasGo.transform;

        Shader sdf = st.SdfUI;

        // Full-screen layers (the halves reach the bezel, under the notch too).
        Color navy = st.dimmer; navy.a = 0f;
        Color guide = st.Guidance; guide.a = 0f;
        ctrl.halfL = new Half
        {
            dim = NewImage("LeftDimmer", ctrl.canvasRect, new Vector2(0f, 0f), new Vector2(0.5f, 1f), navy, EdgeSprite(true, 1f), null),
            glow = NewImage("LeftGlow", ctrl.canvasRect, new Vector2(0f, 0f), new Vector2(0.5f, 1f), guide, EdgeSprite(true, 2f), null)
        };
        ctrl.halfR = new Half
        {
            dim = NewImage("RightDimmer", ctrl.canvasRect, new Vector2(0.5f, 0f), new Vector2(1f, 1f), navy, EdgeSprite(false, 1f), null),
            glow = NewImage("RightGlow", ctrl.canvasRect, new Vector2(0.5f, 0f), new Vector2(1f, 1f), guide, EdgeSprite(false, 2f), null)
        };

        // Rewind look.
        ctrl.tint = NewImage("RewindTint", ctrl.canvasRect, Vector2.zero, Vector2.one, new Color(1f, 1f, 1f, 0f), null, null);
        ctrl.vignetteMat = NewSdfMaterial(sdf, 3f);
        ctrl.vignette = NewImage("RewindVignette", ctrl.canvasRect, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0f), null, ctrl.vignetteMat);

        // Safe-area container: seam, thumbs.
        ctrl.safeRect = NewRect("SafeArea", ctrl.canvasRect, Vector2.zero, Vector2.one);

        ctrl.seamMat = NewSdfMaterial(sdf, 2f);
        ctrl.seam = NewImage("Seam", ctrl.safeRect, new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.92f), new Color(1f, 1f, 1f, 0f), null, ctrl.seamMat);
        ctrl.seam.rectTransform.sizeDelta = new Vector2(24f, 0f);

        ctrl.left = NewThumb("ThumbLeft", ctrl.safeRect, -1, sdf, st);
        ctrl.right = NewThumb("ThumbRight", ctrl.safeRect, 1, sdf, st);

        // Real-tap ripples, pooled, over everything (screen coordinates).
        for (int i = 0; i < ctrl.ripples.Length; i++)
        {
            var mat = NewSdfMaterial(sdf, 1f);
            var img = NewImage("TapRipple", ctrl.canvasRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Color(1f, 1f, 1f, 0f), null, mat);
            img.enabled = false;
            ctrl.ripples[i] = new TapRipple { rt = img.rectTransform, img = img, mat = mat };
        }

        ctrl.style = st;
        return ctrl;
    }

    static Thumb NewThumb(string name, RectTransform parent, int side, Shader sdf, TutorialStyle st)
    {
        var root = NewRect(name, parent, Vector2.zero, Vector2.zero);
        root.sizeDelta = Vector2.zero;
        var t = new Thumb { root = root, side = side };
        t.rippleMat = NewSdfMaterial(sdf, 1f);
        t.rippleImg = NewImage("Ripple", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Color(1f, 1f, 1f, 0f), null, t.rippleMat);
        t.ripple = t.rippleImg.rectTransform;
        t.rippleImg.enabled = false;
        t.shadowMat = NewSdfMaterial(sdf, 0f);
        t.shadowImg = NewImage("Shadow", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Color(0f, 0f, 0f, 0f), null, t.shadowMat);
        t.shadow = t.shadowImg.rectTransform;
        t.bodyMat = NewSdfMaterial(sdf, 0f);
        t.bodyImg = NewImage("Thumb", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Color(1f, 1f, 1f, 0f), null, t.bodyMat);
        t.body = t.bodyImg.rectTransform;
        t.bodyImg.enabled = t.shadowImg.enabled = false;
        return t;
    }

    static Material NewSdfMaterial(Shader sdf, float shape)
    {
        var m = new Material(sdf != null ? sdf : Shader.Find("UI/Default")) { name = "TutorialSdf" };
        m.SetFloat(ShapeId, shape);
        return m;
    }

    static RectTransform NewRect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    static Image NewImage(string name, Transform parent, Vector2 min, Vector2 max, Color color, Sprite sprite, Material mat)
    {
        var rt = NewRect(name, parent, min, max);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        if (mat != null) img.material = mat;
        return img;
    }

    // Horizontal gradient: strongest at the phone's outer edge (left edge if fromLeft), clear toward the middle.
    static Sprite EdgeSprite(bool fromLeft, float power)
    {
        const int n = 128;
        var tex = new Texture2D(n, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int x = 0; x < n; x++)
        {
            float u = x / (float)(n - 1);
            float a = Mathf.Pow(fromLeft ? 1f - u : u, power);
            for (int y = 0; y < 2; y++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, 2), new Vector2(0.5f, 0.5f));
    }
}
