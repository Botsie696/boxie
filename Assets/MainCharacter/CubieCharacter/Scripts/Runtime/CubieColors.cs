using UnityEngine;

namespace Cubie
{
    /// <summary>
    /// Recolour Cubie per instance at runtime (skins, team colours, power-ups) without
    /// creating material copies. Fur, brows, skin and bow textures are grayscale, so any
    /// colour works. Eyes and glasses keep their own colours.
    /// Uses MaterialPropertyBlocks; untick "Apply" to just use the material colours.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class CubieColors : MonoBehaviour
    {
        public bool apply = false;
        public Color furColor = new Color(1.00f, 0.56f, 0.50f);
        public Color browColor = new Color(0.05f, 0.60f, 0.62f);
        [Tooltip("Lids / inner body. Usually a slightly darker version of the fur colour.")]
        public Color skinColor = new Color(0.95f, 0.48f, 0.42f);
        public Color bowColor = new Color(0.55f, 0.95f, 0.80f);

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
                Color? c = n.Contains("FurMain") ? furColor
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
