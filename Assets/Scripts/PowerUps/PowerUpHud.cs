using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The power-up row under the coin counter: a fixed row of slots (shield, focus, magnet, autopilot - in
/// that order, at most four). Icons slide in and out (0.2 s ease) and never jump. Also owns the few
/// screen overlays the power-ups share: a coloured screen-edge tint, popping text ("SAVED!"), a first-time
/// hint label and a sprite that flies from the pickup to its slot.
///
/// One instance, on its own canvas, built in code and pooled (nothing is allocated per frame). Runs on
/// UNSCALED time, so it keeps its pace during hit-stop, the shield's dip and focus.
/// </summary>
public class PowerUpHud : MonoBehaviour
{
    static PowerUpHud instance;
    public static PowerUpHud Get()
    {
        if (instance != null) return instance;
        instance = new GameObject("PowerUpHud").AddComponent<PowerUpHud>();
        return instance;
    }

    const int SlotCount = 4;
    const float SlotSize = 100f, SlotGap = 12f;
    // Where the row sits (top-right of the screen); 3.6 moves it under the coin and diamond pills (SetRowPosition).
    float RowTop = 250f, RightMargin = 60f;

    /// <summary>3.6 HUD: puts the power-up row (and its hint line) at this top / right margin, in reference pixels.</summary>
    public void SetRowPosition(float top, float right)
    {
        RowTop = top; RightMargin = right;
        if (row != null) row.anchoredPosition = new Vector2(-RightMargin, -RowTop);
        if (hintText != null) hintText.rectTransform.anchoredPosition = new Vector2(-RightMargin, -RowTop - SlotSize - 18f);
    }

    class Slot
    {
        public RectTransform rt;
        public Image icon, glow;
        public float x, targetX, scale, targetScale, pop = 1f, fall = -1f, glowPulse;
        public bool shown;
        public bool cracked;
    }

    class Flyer { public RectTransform rt; public Image img; public Vector2 from; public float t; public int slot; public bool live; public float duration; }
    class TextPop { public Text text; public Outline outline; public float t, life; public Vector3 world; public bool live; }

    readonly Slot[] slots = new Slot[SlotCount];
    readonly Flyer[] flyers = new Flyer[6];
    readonly TextPop[] pops = new TextPop[4];
    readonly System.Collections.Generic.List<Image> edgeBands = new System.Collections.Generic.List<Image>();
    readonly System.Collections.Generic.List<float> edgeAlphas = new System.Collections.Generic.List<float>();
    CanvasGroup[] edgeGroups = new CanvasGroup[4];
    RectTransform root, row;
    Text hintText;
    float hintT = 1f;
    Color edgeColor = Color.white;
    float edgeT = 1f, edgeLife = 0.25f, edgeStrength = 0.5f;
    Sprite[] icons = new Sprite[SlotCount];
    Sprite iconCracked, spDot;
    Font font;
    Canvas canvas;

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        var go = new GameObject("PowerUpHudCanvas");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 55;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        root = NewRect("Root", go.transform);
        Stretch(root);

        icons[(int)PowerUpType.Shield] = MakeIcon(ShieldAlpha(false));
        iconCracked = MakeIcon(ShieldAlpha(true));
        icons[(int)PowerUpType.Focus] = MakeIcon(FocusAlpha());
        icons[(int)PowerUpType.Magnet] = MakeIcon(MagnetAlpha());
        icons[(int)PowerUpType.Autopilot] = MakeIcon(AutopilotAlpha());
        spDot = MakeSprite(32, 32, (x, y) => Mathf.Clamp01(1.4f - Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 15.5f * 1.4f));

        row = NewRect("Row", root);
        row.anchorMin = row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(1f, 1f);
        row.anchoredPosition = new Vector2(-RightMargin, -RowTop);
        row.sizeDelta = new Vector2(10, SlotSize);

        for (int i = 0; i < SlotCount; i++)
        {
            var s = new Slot();
            s.rt = NewRect("Slot" + i, row);
            s.rt.anchorMin = s.rt.anchorMax = new Vector2(1f, 1f);
            s.rt.pivot = new Vector2(0.5f, 0.5f);
            s.rt.sizeDelta = new Vector2(SlotSize, SlotSize);
            s.glow = NewImage("Glow", s.rt, spDot);
            s.glow.rectTransform.sizeDelta = new Vector2(SlotSize * 1.6f, SlotSize * 1.6f);
            s.glow.color = Color.clear;
            s.icon = NewImage("Icon", s.rt, icons[i]);
            s.icon.rectTransform.sizeDelta = new Vector2(SlotSize, SlotSize);
            s.x = s.targetX = 0f;
            s.scale = 0f;
            s.rt.gameObject.SetActive(false);
            slots[i] = s;
        }

        for (int i = 0; i < flyers.Length; i++)
        {
            var f = new Flyer();
            f.rt = NewRect("Flyer", root);
            f.img = f.rt.gameObject.AddComponent<Image>();
            f.img.raycastTarget = false;
            f.rt.sizeDelta = new Vector2(SlotSize, SlotSize);
            f.rt.gameObject.SetActive(false);
            flyers[i] = f;
        }

        for (int i = 0; i < pops.Length; i++)
        {
            var p = new TextPop();
            var tr = NewRect("Pop", root);
            p.text = tr.gameObject.AddComponent<Text>();
            p.text.font = font; p.text.fontSize = 96; p.text.fontStyle = FontStyle.Bold; p.text.alignment = TextAnchor.MiddleCenter;
            p.text.horizontalOverflow = HorizontalWrapMode.Overflow; p.text.verticalOverflow = VerticalWrapMode.Overflow;
            p.text.raycastTarget = false;
            p.outline = tr.gameObject.AddComponent<Outline>();
            p.outline.effectColor = Color.white; p.outline.effectDistance = new Vector2(4, -4);
            tr.sizeDelta = new Vector2(800, 160);
            tr.gameObject.SetActive(false);
            pops[i] = p;
        }

        var hr = NewRect("Hint", root);
        hr.anchorMin = hr.anchorMax = new Vector2(1f, 1f);
        hr.pivot = new Vector2(1f, 1f);
        hr.anchoredPosition = new Vector2(-RightMargin, -RowTop - SlotSize - 18f);
        hr.sizeDelta = new Vector2(700, 50);
        hintText = hr.gameObject.AddComponent<Text>();
        hintText.font = font; hintText.fontSize = 34; hintText.fontStyle = FontStyle.Bold; hintText.alignment = TextAnchor.UpperRight;
        hintText.horizontalOverflow = HorizontalWrapMode.Overflow; hintText.raycastTarget = false;
        hintText.color = Color.clear;

        BuildEdges();
    }

    void OnDestroy() { if (instance == this) instance = null; }

    // ------------------------------------------------------------------ public API

    public bool IsShown(PowerUpType t) => slots[(int)t].shown;

    /// <summary>Slides the icon in (pop 0.4x to 1x in 0.2 s). It appears after "delay" seconds, e.g. once a flying sprite lands.</summary>
    public void Show(PowerUpType t)
    {
        Slot s = slots[(int)t];
        s.icon.enabled = true;
        if (!s.shown) { s.shown = true; s.cracked = false; s.fall = -1f; s.icon.sprite = icons[(int)t]; s.rt.gameObject.SetActive(true); s.scale = 0.4f; s.x = s.targetX; }
        s.pop = 0f;
        Relayout();
    }

    /// <summary>Slides the icon out. crack = a two-frame cracked sprite first, then it falls out of its slot.</summary>
    public void Hide(PowerUpType t, bool crack)
    {
        Slot s = slots[(int)t];
        if (!s.shown) return;
        s.shown = false;
        s.fall = 0f;
        s.cracked = crack;
        if (crack && t == PowerUpType.Shield) s.icon.sprite = iconCracked;
        Relayout();
    }

    /// <summary>A soft pulse of the icon's glow (held shield, ready focus).</summary>
    public void Pulse(PowerUpType t, Color c)
    {
        Slot s = slots[(int)t];
        s.glow.color = new Color(c.r, c.g, c.b, 0.6f);
        s.glowPulse = 1f;
    }

    public void SetIconTint(PowerUpType t, Color c) { slots[(int)t].icon.color = c; }

    /// <summary>A sprite of the pickup flies from a world point to its slot, then the slot pops in.</summary>
    public void FlyToSlot(PowerUpType t, Vector3 world, Color tint, float seconds)
    {
        Camera cam = Camera.main;
        Flyer f = null;
        foreach (Flyer c in flyers) if (!c.live) { f = c; break; }
        if (f == null || cam == null) { Show(t); return; }
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, cam.WorldToScreenPoint(world), null, out f.from);
        f.live = true; f.t = 0f; f.slot = (int)t; f.duration = Mathf.Max(0.1f, seconds);
        f.img.sprite = icons[(int)t]; f.img.color = tint;
        f.rt.gameObject.SetActive(true);
        // The slot gets its place now (so the row has made room) but only pops when the sprite lands.
        Slot s = slots[(int)t];
        if (!s.shown) { s.shown = true; s.cracked = false; s.fall = -1f; s.icon.sprite = icons[(int)t]; s.rt.gameObject.SetActive(true); s.scale = 0f; s.targetScale = 0f; s.x = s.targetX; Relayout(); s.icon.enabled = false; }
    }

    /// <summary>A short label under the row, e.g. the first-time hint. Fades in/out over "seconds".</summary>
    public void Hint(string text, Color c, float seconds)
    {
        hintText.text = text;
        hintText.color = new Color(c.r, c.g, c.b, 0f);
        hintT = 0f;
        hintLife = Mathf.Max(0.3f, seconds);
        hintColor = c;
    }
    float hintLife = 1.6f; Color hintColor = Color.white;

    /// <summary>Pops text at a world point: 0.6x to 1.1x to 1x, fading out near the end.</summary>
    public void PopText(string text, Vector3 world, Color c, Color outline, float seconds)
    {
        TextPop p = null;
        foreach (TextPop q in pops) if (!q.live) { p = q; break; }
        if (p == null) p = pops[0];
        p.live = true; p.t = 0f; p.life = Mathf.Max(0.2f, seconds); p.world = world;
        p.text.text = text; p.text.color = c; p.outline.effectColor = outline;
        p.text.rectTransform.gameObject.SetActive(true);
    }

    /// <summary>A coloured screen-edge tint that fades out over "seconds".</summary>
    public void EdgeTint(Color c, float seconds, float strength = 0.5f)
    {
        edgeColor = c; edgeLife = Mathf.Max(0.05f, seconds); edgeT = 0f; edgeStrength = strength;
        for (int i = 0; i < edgeBands.Count; i++) edgeBands[i].color = new Color(c.r, c.g, c.b, edgeAlphas[i]);
    }

    // ------------------------------------------------------------------ per frame

    void Relayout()
    {
        int idx = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            Slot s = slots[i];
            if (!s.shown) continue;
            s.targetX = -(SlotSize * 0.5f + idx * (SlotSize + SlotGap));
            s.targetScale = 1f;
            idx++;
        }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float follow = 1f - Mathf.Exp(-dt * 18f);   // ~0.2 s ease, frame-rate independent

        for (int i = 0; i < SlotCount; i++)
        {
            Slot s = slots[i];
            if (!s.rt.gameObject.activeSelf) continue;

            if (s.fall >= 0f)
            {
                s.fall += dt / 0.45f;
                float k = Mathf.Clamp01(s.fall);
                s.rt.anchoredPosition = new Vector2(s.x, -SlotSize * 0.5f - k * k * 260f);
                s.rt.localRotation = Quaternion.Euler(0, 0, k * 40f);
                s.icon.color = new Color(1, 1, 1, 1f - k);
                s.glow.color = Color.clear;
                if (k >= 1f) { s.rt.gameObject.SetActive(false); s.rt.localRotation = Quaternion.identity; s.icon.color = Color.white; s.fall = -1f; }
                continue;
            }

            s.x = Mathf.Lerp(s.x, s.targetX, follow);
            if (s.pop < 1f && s.icon.enabled) s.pop = Mathf.Min(1f, s.pop + dt / 0.2f);
            float popScale = s.pop < 1f ? Mathf.Lerp(0.4f, 1f, 1f - (1f - s.pop) * (1f - s.pop)) : 1f;
            s.scale = Mathf.Lerp(s.scale, s.targetScale * popScale, follow * 1.5f);
            if (s.pop < 1f && s.icon.enabled) s.scale = popScale * s.targetScale;
            s.rt.anchoredPosition = new Vector2(s.x, -SlotSize * 0.5f);
            s.rt.localScale = Vector3.one * Mathf.Max(0f, s.scale);

            if (s.glowPulse > 0f)
            {
                s.glowPulse = Mathf.Max(0f, s.glowPulse - dt / 0.7f);
                Color g = s.glow.color; g.a = 0.6f * s.glowPulse; s.glow.color = g;
            }
        }

        for (int i = 0; i < flyers.Length; i++)
        {
            Flyer f = flyers[i];
            if (!f.live) continue;
            f.t += dt / f.duration;
            float k = Mathf.Clamp01(f.t);
            Slot s = slots[f.slot];
            Vector2 to = new Vector2(root.rect.width * 0.5f - RightMargin + s.targetX, root.rect.height * 0.5f - RowTop - SlotSize * 0.5f);
            Vector2 mid = (f.from + to) * 0.5f + new Vector2(-140f, 120f);
            float e = k * k;
            f.rt.anchoredPosition = (1 - e) * (1 - e) * f.from + 2 * (1 - e) * e * mid + e * e * to;
            f.rt.localScale = Vector3.one * Mathf.Lerp(1.1f, 0.8f, k);
            if (k >= 1f)
            {
                f.live = false; f.rt.gameObject.SetActive(false);
                s.icon.enabled = true; s.pop = 0f; s.scale = 0.4f;
            }
        }

        if (hintT < 1f)
        {
            hintT += dt / hintLife;
            float a = Mathf.Clamp01(hintT * 8f) * (1f - Mathf.Clamp01((hintT - 0.8f) / 0.2f));
            hintText.color = new Color(hintColor.r, hintColor.g, hintColor.b, a);
        }

        Camera cam = Camera.main;
        for (int i = 0; i < pops.Length; i++)
        {
            TextPop p = pops[i];
            if (!p.live) continue;
            p.t += dt / p.life;
            float k = p.t;
            if (k >= 1f) { p.live = false; p.text.rectTransform.gameObject.SetActive(false); continue; }
            float s = k < 0.15f ? Mathf.Lerp(0.6f, 1.1f, k / 0.15f) : Mathf.Lerp(1.1f, 1f, Mathf.Clamp01((k - 0.15f) / 0.15f));
            Vector2 local = Vector2.zero;
            if (cam != null) RectTransformUtility.ScreenPointToLocalPointInRectangle(root, cam.WorldToScreenPoint(p.world), null, out local);
            RectTransform rt = p.text.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = local + new Vector2(0f, 140f + k * 60f);
            rt.localScale = Vector3.one * s;
            Color c = p.text.color; c.a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.65f) / 0.35f)); p.text.color = c;
            Color oc = p.outline.effectColor; oc.a = c.a; p.outline.effectColor = oc;
        }

        if (edgeT < 1f)
        {
            edgeT += dt / edgeLife;
            float a = Mathf.Clamp01(1f - edgeT) * Mathf.Clamp01(edgeStrength * 1.4f);
            for (int i = 0; i < 4; i++) if (edgeGroups[i] != null) edgeGroups[i].alpha = a;
        }
    }

    // ------------------------------------------------------------------ building

    void BuildEdges()
    {
        for (int i = 0; i < 4; i++)
        {
            RectTransform holder = NewRect("Edge" + i, root);
            Stretch(holder);
            edgeGroups[i] = holder.gameObject.AddComponent<CanvasGroup>();
            edgeGroups[i].blocksRaycasts = false; edgeGroups[i].alpha = 0f;
            const int steps = 14;
            for (int s = 0; s < steps; s++)
            {
                Image img = NewImage("s" + s, holder, null);
                float band = 0.5f * Mathf.Pow(1f - s / (float)steps, 2.2f);
                img.color = new Color(1f, 1f, 1f, band);
                edgeBands.Add(img); edgeAlphas.Add(band);
                RectTransform rt = img.rectTransform;
                const float w = 9f;
                switch (i)
                {
                    case 0: rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 0.5f); rt.anchoredPosition = new Vector2(s * w, 0); rt.sizeDelta = new Vector2(w, 0); break;
                    case 1: rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 0.5f); rt.anchoredPosition = new Vector2(-s * w, 0); rt.sizeDelta = new Vector2(w, 0); break;
                    case 2: rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1); rt.anchoredPosition = new Vector2(0, -s * w); rt.sizeDelta = new Vector2(0, w); break;
                    default: rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0); rt.anchoredPosition = new Vector2(0, s * w); rt.sizeDelta = new Vector2(0, w); break;
                }
            }
        }
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static Image NewImage(string name, Transform parent, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite; img.raycastTarget = false;
        return img;
    }

    static Sprite MakeSprite(int w, int h, System.Func<int, int, float> alpha)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x, y)) * 255f));
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    // Icons are drawn as RGBA from a signed shape: emerald body, white rim and emblem.
    const int IconPx = 96;

    Sprite MakeIcon(System.Func<float, float, Color> shape)
    {
        var tex = new Texture2D(IconPx, IconPx, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[IconPx * IconPx];
        for (int y = 0; y < IconPx; y++)
            for (int x = 0; x < IconPx; x++)
                px[y * IconPx + x] = shape((x + 0.5f) / IconPx * 2f - 1f, (y + 0.5f) / IconPx * 2f - 1f);
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, IconPx, IconPx), new Vector2(0.5f, 0.5f), 100f);
    }

    static float ShieldHalfWidth(float y)
    {
        if (y > 0.82f || y < -0.92f) return -1f;
        if (y >= 0.05f) return 0.74f;
        float t = (0.05f - y) / 0.97f;
        return 0.74f * (1f - Mathf.Pow(t, 1.7f));
    }

    static System.Func<float, float, Color> ShieldAlpha(bool cracked)
    {
        Color body = new Color(0.184f, 0.827f, 0.541f, 1f);
        return (x, y) =>
        {
            float hw = ShieldHalfWidth(y);
            float ax = Mathf.Abs(x);
            if (hw < 0f || ax > hw) return Color.clear;
            bool rim = hw - ax < 0.12f || y > 0.7f || (y < 0.05f && hw - ax < 0.16f);
            Color c = rim ? Color.white : body;
            // the raised emblem cube
            if (Mathf.Abs(x) < 0.16f && Mathf.Abs(y - 0.18f) < 0.16f) c = Color.white;
            if (cracked)
            {
                float zig = 0.04f * Mathf.Sin(y * 22f);
                if (Mathf.Abs(x - zig) < 0.045f && y < 0.7f && y > -0.7f) return Color.clear;
            }
            return c;
        };
    }

    static System.Func<float, float, Color> FocusAlpha()
    {
        Color pearl = new Color(0.96f, 0.96f, 1f, 1f), indigo = new Color(0.30f, 0.26f, 0.80f, 1f);
        return (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r > 0.85f) return Color.clear;
            if (r > 0.74f) return indigo;
            if (r < 0.22f) return new Color(0.08f, 0.05f, 0.2f, 1f);
            if (r < 0.5f) return indigo;
            return pearl;
        };
    }

    static System.Func<float, float, Color> MagnetAlpha()
    {
        Color violet = new Color(0.69f, 0.30f, 1f, 1f);
        return (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + (y - 0.1f) * (y - 0.1f));
            bool arc = y > 0.1f && r > 0.38f && r < 0.78f;
            bool leg = Mathf.Abs(Mathf.Abs(x) - 0.58f) < 0.2f && y <= 0.1f && y > -0.75f;
            if (!arc && !leg) return Color.clear;
            return y < -0.5f && leg ? Color.white : violet;
        };
    }

    static System.Func<float, float, Color> AutopilotAlpha()
    {
        Color cyan = new Color(0.55f, 0.95f, 1f, 1f);
        return (x, y) => Mathf.Abs(x) + Mathf.Abs(y) < 0.8f && Mathf.Abs(x) + Mathf.Abs(y) > 0.45f ? cyan : Color.clear;
    }
}
