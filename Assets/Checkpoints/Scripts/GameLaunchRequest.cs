using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// What the menu asks the game scene to do on its next load: which checkpoint to start at. Set by MainUi, consumed once
/// by CheckpointRunController in the game scene (a direct Play of 3.6 in the editor = a normal start).
/// </summary>
public static class GameLaunchRequest
{
    public const string GameScene = "MainScene prop camera 3.11";
    public const string MenuScene = "MainUi";

    static int checkpointIndex;
    static bool pending, endlessPending;

    /// <summary>True when the request Consume just handed out was an ENDLESS one (that checkpoint's theme, forever).</summary>
    public static bool ConsumedEndless { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { checkpointIndex = 0; pending = false; endlessPending = false; ConsumedEndless = false; }

    public static bool Pending => pending;

    /// <summary>Asks for a start at this checkpoint (1 = the run start).</summary>
    public static void Set(int checkpoint, bool endless = false)
    {
        checkpointIndex = Mathf.Max(1, checkpoint);
        endlessPending = endless;
        pending = true;
    }

    /// <summary>The requested checkpoint (1 when nothing was asked), and whether it came from the menu. Clears the request.</summary>
    public static int Consume(out bool fromMenu)
    {
        fromMenu = pending;
        ConsumedEndless = pending && endlessPending;
        endlessPending = false;
        int c = pending ? checkpointIndex : 1;
        pending = false;
        checkpointIndex = 0;
        return Mathf.Max(1, c);
    }

    /// <summary>Sets the request and starts loading the game scene (async; the caller may hold activation for a transition).</summary>
    public static AsyncOperation LoadGame(int checkpoint, bool allowActivation = true, bool endless = false)
    {
        Set(checkpoint, endless);
        AsyncOperation op = SceneManager.LoadSceneAsync(GameScene);
        if (op != null) op.allowSceneActivation = allowActivation;
        return op;
    }
}
