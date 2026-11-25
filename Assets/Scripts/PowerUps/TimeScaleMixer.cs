using UnityEngine;

/// <summary>
/// One owner for Time.timeScale and Time.fixedDeltaTime. Every system that slows or freezes time
/// (hit-stop, the shield's slow-mo dip, focus, DebugTimeScale) sets its OWN factor here, and the real
/// timeScale is their product, so they stack instead of overwriting each other. fixedDeltaTime is
/// scaled by the same product from the value the project started with and restored exactly at 1.
/// Static, allocation free.
/// </summary>
public static class TimeScaleMixer
{
    public enum Source { HitStop, PowerDip, Shield, Focus, Debug, Hotkey, Pause, Tutorial, SlowMo, Meta }

    static readonly float[] factors = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };

    /// <summary>Dev tests only (default off): keep the physics step at its own value whatever the time scale, so a
    /// sped-up bot run steps the physics exactly as a real one (more steps per frame, never longer ones).</summary>
    public static bool KeepPhysicsStep;

    /// <summary>The product of every factor except one (what time will be when that one is back at 1).</summary>
    public static float ProductWithout(Source s)
    {
        float p = 1f;
        for (int i = 0; i < factors.Length; i++) if (i != (int)s) p *= factors[i];
        return p;
    }

    /// <summary>fixedDeltaTime at time scale 1 (the project own value).</summary>
    public static float BaseFixedDelta => baseFixedDelta > 0f ? baseFixedDelta : Time.fixedDeltaTime;
    static float baseFixedDelta = -1f;

    public static float Get(Source s) => factors[(int)s];

    public static void Set(Source s, float factor)
    {
        if (baseFixedDelta < 0f) baseFixedDelta = Time.fixedDeltaTime;
        factors[(int)s] = Mathf.Max(0f, factor);
        Apply();
    }

    /// <summary>Puts every factor back to 1 and restores timeScale and fixedDeltaTime exactly.</summary>
    public static void ResetAll()
    {
        for (int i = 0; i < factors.Length; i++) factors[i] = 1f;
        Apply();
    }

    static void Apply()
    {
        if (baseFixedDelta < 0f) baseFixedDelta = Time.fixedDeltaTime;
        float p = 1f;
        for (int i = 0; i < factors.Length; i++) p *= factors[i];
        Time.timeScale = p;
        // A frozen game (0) keeps the physics step as it was; anything else scales it with the time.
        Time.fixedDeltaTime = p > 0.0001f && !Mathf.Approximately(p, 1f) && !KeepPhysicsStep ? baseFixedDelta * p : baseFixedDelta;
    }
}
