using UnityEngine;

/// <summary>
/// The props one checkpoint stands in the corner gaps of its sideways turns (DynamicPathGeneratorCornerProps.cs).
///
/// They are used IN ORDER: the first prop for the first legsPerProp legs of the checkpoint, the next one for the
/// next legsPerProp legs, and so on, starting over from the first after the last. Every prop is a prefab whose pivot
/// sits in the middle of its base (it stands on the corner tile) - Tools > Corner Props > Build Pillars makes them so.
/// </summary>
[CreateAssetMenu(menuName = "Runner/Corner Prop Set", fileName = "CornerPropSet")]
public class CornerPropSet : ScriptableObject
{
    [Tooltip("Used in this order, one after the other, then from the top again.")]
    public GameObject[] props;
    [Tooltip("How many legs of the path one prop is used for before the next one takes over. Legs that climb or drop count too - they just get no prop.")]
    [Min(1)] public int legsPerProp = 5;

    [Header("Special props (the Monument Valley tree)")]
    [Tooltip("Used in order instead of the regular prop at one sideways corner every specialEveryMin..specialEveryMax sideways corners.")]
    public GameObject[] specialProps;
    [Min(1)] public int specialEveryMin = 18;
    [Min(1)] public int specialEveryMax = 22;
    [Tooltip("Height a special prop is aimed at, in cubes - a bit bigger than the regular props.")]
    [Range(0.5f, 16f)] public float specialHeight = 9f;
    [Tooltip("Widest a special prop may be, in cubes (its crown may overhang the tile; its base still sits on it).")]
    [Range(0.5f, 8f)] public float specialMaxWidth = 6f;

    public bool HasSpecials => specialProps != null && specialProps.Length > 0;

    /// <summary>Sideways corners until the next special one, counted from the one just used.</summary>
    public int NextSpecialGap()
    {
        int lo = Mathf.Max(1, specialEveryMin), hi = Mathf.Max(lo, specialEveryMax);
        return Random.Range(lo, hi + 1);
    }

    public GameObject SpecialAt(int n)
    {
        if (!HasSpecials) return null;
        return specialProps[Mathf.Max(0, n) % specialProps.Length];
    }

    public GameObject PropForLeg(int leg)
    {
        if (props == null || props.Length == 0) return null;
        int i = (Mathf.Max(0, leg) / Mathf.Max(1, legsPerProp)) % props.Length;
        return props[i];
    }
}
