using UnityEditor;
using UnityEngine;

/// <summary>Editor shortcuts for testing the tutorial (TutorialManager saves PlayerPrefs "TutorialCompleted").</summary>
public static class TutorialMenu
{
    [MenuItem("Tools/Tutorial/Reset TutorialCompleted (play it again)")]
    static void Reset()
    {
        PlayerPrefs.DeleteKey(TutorialManager.PrefKey);
        PlayerPrefs.Save();
        Debug.Log("[Tutorial] TutorialCompleted cleared - the tutorial will run next time.");
    }

    [MenuItem("Tools/Tutorial/Reset first-encounter hints (show them again)")]
    static void ResetHints()
    {
        foreach (FirstEncounterHints.HintType t in System.Enum.GetValues(typeof(FirstEncounterHints.HintType)))
            PlayerPrefs.DeleteKey("FirstHint_" + t);
        PlayerPrefs.Save();
        Debug.Log("[Tutorial] first-encounter hints cleared - each shows once more.");
    }

    [MenuItem("Tools/Tutorial/Mark TutorialCompleted (skip it)")]
    static void Skip()
    {
        PlayerPrefs.SetInt(TutorialManager.PrefKey, 1);
        PlayerPrefs.Save();
        Debug.Log("[Tutorial] TutorialCompleted set - the tutorial is skipped.");
    }
}
