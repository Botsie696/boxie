using UnityEngine;

/// <summary>
/// Endless practice on one level (MainUi's infinity button): while a run is endless, <see cref="Level"/> is that checkpoint
/// (1..N), the generator never lays another door (DynamicPathGenerator.endlessLevel), the score counts from 0 and the best
/// score lives under its own per-level key. Set by CheckpointRunController at scene start, cleared when the scene unloads.
/// </summary>
public static class EndlessRun
{
    /// <summary>0 = a normal run.</summary>
    public static int Level;
    public static bool Active => Level > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Level = 0; }

    /// <summary>The saved best of one level's endless runs.</summary>
    public static string BestKey(string scene, int level) => "RunnerBest_" + scene + "_Endless" + level;

    /// <summary>The key the running game saves its best score under: the level's endless key in an endless run, else the scene's.</summary>
    public static string CurrentBestKey(string scene) => Active ? BestKey(scene, Level) : "RunnerBest_" + scene;
}
