using UnityEngine;

namespace Poppy
{
    /// <summary>
    /// Recolour Poppy per instance at runtime (skins, team colours, power-ups) without
    /// creating material copies. Fur, brows, skin and bow textures are grayscale, so any
    /// colour works. Eyes and glasses keep their own colours.
    /// Uses MaterialPropertyBlocks; untick "Apply" to just use the material colours.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class PoppyColors : MonoBehaviour
    {
        public bool apply = false;
        public Color furColor = new Color(1.00f, 0.38f, 0.72f);
        public Color browColor = new Color(0.72f, 0.46f, 1.00f);
        [Tooltip("Brow glow brightness (HDR multiplier; drives Bloom).")] public float browGlow = 2.6f;
        [Tooltip("Lids / inner body. Usually a slightly darker version of the fur colour.")]
        public Color skinColor = new Color(1.00f, 0.30f, 0.62f);
        [Tooltip("Soft ambient lift for fur + skin (keeps shadows from going muddy). Far below the Bloom threshold. 0 = off.")] [Range(0f, 0.5f)] public float ambientLift = 0.14f;
        [Tooltip("Glowing hair streak colour.")] public Color streakColor = new Color(0.80f, 0.40f, 1.00f);
        [Tooltip("Hair streak glow brightness (HDR multiplier; drives Bloom).")] public float streakGlow = 3f;
        [Tooltip("Cheek blush colour.")] public Color blushColor = new Color(1.00f, 0.10f, 0.40f);
        [Tooltip("Smile line colour.")] public Color lipColor = new Color(0.80f, 0.12f, 0.30f);
        [Tooltip("Gold hair-tie colour.")] public Color bowColor = new Color(1.00f, 0.78f, 0.22f);
        [Tooltip("Hair colour (cap, buns, ringlets). Plum by default - any colour works.")] public Color hairColor = new Color(0.34f, 0.09f, 0.50f);
        [Tooltip("Glowing edge/corner fur colour.")] public Color glowColor = new Color(1.00f, 0.70f, 0.90f);
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
                if (n.Contains("HairGlow"))
                {
                    mpb.Clear(); mpb.SetColor(BaseColor, streakColor); mpb.SetColor(LegacyColor, streakColor);
                    mpb.SetColor(EmissionColor, streakColor * streakGlow); r.SetPropertyBlock(mpb, i); continue;
                }
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
                Color? c = n.Contains("Blush") ? blushColor : n.Contains("Lip") ? lipColor : n.Contains("Hair") ? hairColor : n.Contains("FurMain") ? furColor
                         : n.Contains("FurAccent") ? browColor
                         : n.Contains("Skin") ? skinColor
                         : n.Contains("Bow") ? bowColor
                         : (Color?)null;
                if (c == null) { r.SetPropertyBlock(null, i); continue; }
                mpb.Clear();
                mpb.SetColor(BaseColor, c.Value);
                mpb.SetColor(LegacyColor, c.Value);
                if (n.Contains("FurMain") || n.Contains("Skin")) mpb.SetColor(EmissionColor, c.Value * ambientLift);
                r.SetPropertyBlock(mpb, i);
            }
        }
    }
}
