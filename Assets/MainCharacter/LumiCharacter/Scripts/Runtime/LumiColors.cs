using UnityEngine;

namespace Lumi
{
    /// <summary>
    /// Recolour Lumi per instance at runtime (skins, team colours, power-ups) without
    /// creating material copies. Fur, brows, skin and bow textures are grayscale, so any
    /// colour works. Eyes and glasses keep their own colours.
    /// Uses MaterialPropertyBlocks; untick "Apply" to just use the material colours.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class LumiColors : MonoBehaviour
    {
        public bool apply = false;
        public Color furColor = new Color(1.00f, 0.66f, 0.08f);
        public Color browColor = new Color(0.10f, 0.92f, 0.82f);
        [Tooltip("Brow glow brightness (HDR multiplier; drives Bloom).")] public float browGlow = 2.6f;
        [Tooltip("Lids / inner body. Usually a slightly darker version of the fur colour.")]
        public Color skinColor = new Color(0.80f, 0.40f, 0.04f);
        [Tooltip("Smile line colour.")] public Color lipColor = new Color(0.70f, 0.17f, 0.27f);
        [Tooltip("Hair tie colour.")] public Color bowColor = new Color(0.10f, 0.78f, 0.72f);
        [Tooltip("Hair colour (cap, bangs, ponytail). Black by default - any colour works.")] public Color hairColor = new Color(0.045f, 0.045f, 0.06f);
        [Tooltip("Glowing edge/corner fur colour.")] public Color glowColor = new Color(1.00f, 0.86f, 0.32f);
        [Tooltip("Glow brightness (HDR multiplier; drives Bloom).")] public float glowIntensity = 3f;
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int LegacyColor = Shader.PropertyToID("_Color");
        MaterialPropertyBlock mpb;

        void OnEnable() { Apply(); }
        void OnValidate() { Apply(); }

        /// <summary>Convenience: set fur + matching skin in one call.</summary>
        public void SetFur(Color c)
        {
            furColor = c;
            skinColor = c * 0.85f; skinColor.a = 1f;
            apply = true;
            Apply();
        }

        public void Apply()
        {
            var r = GetComponentInChildren<SkinnedMeshRenderer>();
            if (!r) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!apply || mats[i] == null) { r.SetPropertyBlock(null, i); continue; }
                string n = mats[i].name;
                if (n.Contains("FurAccent"))
                {
                    mpb.Clear(); mpb.SetColor(BaseColor, browColor); mpb.SetColor(LegacyColor, browColor);
                    mpb.SetColor(EmissionColor, browColor * browGlow); r.SetPropertyBlock(mpb, i); continue;
                }
                if (n.Contains("FurGlow"))
                {
                    mpb.Clear(); mpb.SetColor(BaseColor, glowColor); mpb.SetColor(LegacyColor, glowColor);
                    mpb.SetColor(EmissionColor, glowColor * glowIntensity); r.SetPropertyBlock(mpb, i); continue;
                }
                Color? c = n.Contains("Lip") ? lipColor : n.Contains("Hair") ? hairColor : n.Contains("FurMain") ? furColor
                         : n.Contains("FurAccent") ? browColor
                         : n.Contains("Skin") ? skinColor
                         : n.Contains("Bow") ? bowColor
                         : (Color?)null;
                if (c == null) { r.SetPropertyBlock(null, i); continue; }
                mpb.Clear();
                mpb.SetColor(BaseColor, c.Value);
                mpb.SetColor(LegacyColor, c.Value);
                r.SetPropertyBlock(mpb, i);
            }
        }
    }
}
