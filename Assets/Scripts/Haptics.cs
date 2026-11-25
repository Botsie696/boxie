using System.Runtime.InteropServices;

/// <summary>Soft iOS haptics (UISelectionFeedbackGenerator / light and medium UIImpactFeedbackGenerator). No-op elsewhere.</summary>
public static class Haptics
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void _HapticSelection();
    [DllImport("__Internal")] static extern void _HapticLightImpact();
    [DllImport("__Internal")] static extern void _HapticMediumImpact();

    public static void Selection() { _HapticSelection(); }
    public static void LightImpact() { _HapticLightImpact(); }
    public static void MediumImpact() { _HapticMediumImpact(); }
#else
    public static void Selection() { }
    public static void LightImpact() { }
    public static void MediumImpact() { }
#endif
}
