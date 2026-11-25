using UnityEngine;

/// <summary>
/// Debug aid: runs the game at a fraction of normal speed while switched on. Add it to any GameObject in
/// any scene; tick or untick "slowDown" (live, even in play mode) or change timeScale. Turning it off,
/// disabling the component or leaving the scene puts time back to normal.
/// </summary>
public class DebugTimeScale : MonoBehaviour
{
    [Tooltip("On = the game runs at timeScale. Off = normal speed.")]
    public bool slowDown = true;
    [Range(0.01f, 1f)] public float timeScale = 0.3f;

    private float baseFixedDelta;
    private bool applied;

    void OnEnable() { baseFixedDelta = Time.fixedDeltaTime; }

    void Update()
    {
        if (slowDown)
        {
            TimeScaleMixer.Set(TimeScaleMixer.Source.Debug, timeScale);   // stacks with hit-stop, shield and focus
            applied = true;
        }
        else if (applied) Restore();
    }

    void OnDisable() { if (applied) Restore(); }

    void Restore()
    {
        TimeScaleMixer.Set(TimeScaleMixer.Source.Debug, 1f);
        applied = false;
    }
}
