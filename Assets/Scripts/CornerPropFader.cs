using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lets a corner prop (DynamicPathGeneratorCornerProps.cs) fade exactly like the path cube it stands beside.
///
/// PathDepthShading draws every path cube with an alpha (far ahead = faint, the edge of the drawn window = fading in
/// or out, a leg the runner has left = see-through) and swaps the cube to a transparent material only while that
/// alpha is under 1. This does the same for every renderer of the prop: the generator hands it the corner cube's
/// alpha each frame; at 1 the prop wears its own opaque materials with no property block (SRP batching, depth,
/// shadows as authored); under 1 each material slot swaps to its transparent twin and gets its own colour with that
/// alpha through a per-slot property block.
///
/// Twins: the builders save one next to each material (fadeTwins, parallel to opaqueMaterials) so the transparent
/// shader variant is in the build. A material with no twin gets a runtime copy - fine in the editor, but a build may
/// strip the variant. Materials that are already transparent (particles) are their own twin.
/// </summary>
[DisallowMultipleComponent]
public class CornerPropFader : MonoBehaviour
{
    [Tooltip("Opaque materials of this prop, each paired with the transparent twin at the same index in fadeTwins.")]
    public Material[] opaqueMaterials;
    public Material[] fadeTwins;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    /// <summary>Multiplier on the materials' own emission (1 = as authored). Set by the generator each frame.</summary>
    [System.NonSerialized] public float glow = 1f;
    float shownGlow = 1f;

    struct Slot
    {
        public Renderer renderer;
        public Material[] opaque;
        public Material[] fade;
        public Color[] colors;
        public Color[] emis;
    }

    readonly List<Slot> slots = new List<Slot>();
    static readonly Dictionary<Material, Material> runtimeTwins = new Dictionary<Material, Material>();
    MaterialPropertyBlock block;
    bool built;
    bool fading;
    float shownAlpha = -1f;

    void Build()
    {
        built = true;
        block = new MaterialPropertyBlock();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            Material[] opaque = r.sharedMaterials;
            var s = new Slot { renderer = r, opaque = opaque, fade = new Material[opaque.Length], colors = new Color[opaque.Length], emis = new Color[opaque.Length] };
            for (int i = 0; i < opaque.Length; i++)
            {
                Material m = opaque[i];
                s.fade[i] = TwinOf(m);
                s.emis[i] = m != null && m.HasProperty(EmissionId) ? m.GetColor(EmissionId) : Color.black;
                s.colors[i] = m == null ? Color.white : m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : m.HasProperty(ColorId) ? m.GetColor(ColorId) : Color.white;
            }
            slots.Add(s);
        }
    }

    Material TwinOf(Material m)
    {
        if (m == null) return null;
        if (m.renderQueue >= 2900) return m;   // already transparent
        if (opaqueMaterials != null && fadeTwins != null)
            for (int i = 0; i < opaqueMaterials.Length && i < fadeTwins.Length; i++)
                if (opaqueMaterials[i] == m && fadeTwins[i] != null) return fadeTwins[i];
        if (!runtimeTwins.TryGetValue(m, out Material t) || t == null)
        {
            t = new Material(m) { name = m.name + "_FadeRuntime" };
            MakeTransparent(t);
            runtimeTwins[m] = t;
        }
        return t;
    }

    /// <summary>URP Lit (or Simple Lit) switched to alpha-blended transparent that still writes depth, like the path's fade material.</summary>
    public static void MakeTransparent(Material t)
    {
        t.SetFloat("_Surface", 1f);
        t.SetFloat("_Blend", 0f);
        t.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        t.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        t.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        t.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        t.SetFloat("_ZWrite", 1f);   // own back faces stay hidden while see-through, as the path cubes do
        t.SetOverrideTag("RenderType", "Transparent");
        t.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        t.DisableKeyword("_ALPHATEST_ON");
        t.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    /// <summary>alpha = the corner cube's drawn alpha this frame (0..1).</summary>
    public void SetAlpha(float alpha)
    {
        if (!built) Build();
        alpha = Mathf.Clamp01(alpha);
        bool wantFade = alpha < 0.999f;
        if (wantFade == fading && Mathf.Abs(alpha - shownAlpha) < 0.002f && Mathf.Abs(glow - shownGlow) < 0.001f) return;
        shownGlow = glow;
        bool boosted = glow > 1.001f;

        if (wantFade != fading || shownAlpha < 0f)
            foreach (Slot s in slots) if (s.renderer != null) s.renderer.sharedMaterials = wantFade ? s.fade : s.opaque;
        fading = wantFade;
        shownAlpha = alpha;

        foreach (Slot s in slots)
        {
            if (s.renderer == null) continue;
            if (!wantFade && !boosted)
            {
                for (int i = 0; i < s.colors.Length; i++) s.renderer.SetPropertyBlock(null, i);
                continue;
            }
            for (int i = 0; i < s.colors.Length; i++)
            {
                Color c = s.colors[i];
                if (wantFade) c.a *= alpha;
                block.Clear();
                if (wantFade) { block.SetColor(BaseColorId, c); block.SetColor(ColorId, c); }
                if (boosted) block.SetColor(EmissionId, s.emis[i] * glow);
                s.renderer.SetPropertyBlock(block, i);
            }
        }
    }
}
