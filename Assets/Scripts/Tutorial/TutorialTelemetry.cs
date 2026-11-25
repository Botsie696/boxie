using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Tutorial telemetry, development builds and the editor only (every call is compiled out of release builds):
/// time per lesson, retries, wrong-half taps per lesson, the L2 outcome, whether L4 passed first time, completion,
/// deaths in the first 30 s after the hand-over, and the first-encounter hint outcomes. Logged as one summary at
/// the hand-over and again when the 30 s watch ends.
/// </summary>
public static class TutorialTelemetry
{
    class Lesson { public string name; public float start = -1f, end = -1f; public int retries, wrongTaps; }

    static readonly List<Lesson> lessons = new List<Lesson>();
    static readonly List<string> hintOutcomes = new List<string>();
    static string l2Outcome = "-";
    static string l4FirstTry = "-";
    static bool completed;
    static int deathsAfterHandover = -1;
    static float tutorialStart = -1f;

    static Lesson Get(string name)
    {
        foreach (Lesson l in lessons) if (l.name == name) return l;
        var n = new Lesson { name = name };
        lessons.Add(n);
        return n;
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Reset()
    {
        lessons.Clear();
        hintOutcomes.Clear();
        l2Outcome = "-";
        l4FirstTry = "-";
        completed = false;
        deathsAfterHandover = -1;
        tutorialStart = Time.unscaledTime;
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LessonStart(string name) { Lesson l = Get(name); if (l.start < 0f) l.start = Time.unscaledTime; }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LessonEnd(string name) { Lesson l = Get(name); if (l.start >= 0f) l.end = Time.unscaledTime; }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Retry(string name) { Get(name).retries++; }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void WrongTap(string name) { Get(name).wrongTaps++; }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void L2(bool success) { if (l2Outcome == "-") l2Outcome = success ? "made it" : "missed (rewound)"; }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void L4(bool firstTry) { if (l4FirstTry == "-") l4FirstTry = firstTry ? "yes" : "no"; }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Completed() { completed = true; deathsAfterHandover = 0; Report("hand-over"); }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void DeathAfterHandover() { if (deathsAfterHandover >= 0) deathsAfterHandover++; }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void HintOutcome(string type, bool correct) { hintOutcomes.Add(type + ": " + (correct ? "correct" : "missed")); }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Report(string when)
    {
        var sb = new StringBuilder();
        sb.Append("[TutorialTelemetry] (").Append(when).Append(")");
        if (tutorialStart >= 0f) sb.Append(" total ").Append((Time.unscaledTime - tutorialStart).ToString("0.0")).Append(" s");
        sb.Append(", completed ").Append(completed ? "yes" : "no");
        foreach (Lesson l in lessons)
        {
            float secs = l.start >= 0f ? ((l.end >= 0f ? l.end : Time.unscaledTime) - l.start) : 0f;
            sb.Append("\n  ").Append(l.name).Append(": ").Append(secs.ToString("0.0")).Append(" s, retries ").Append(l.retries)
              .Append(", wrong-half taps ").Append(l.wrongTaps);
        }
        sb.Append("\n  L2 outcome: ").Append(l2Outcome).Append("   L4 first-try pass: ").Append(l4FirstTry);
        if (deathsAfterHandover >= 0) sb.Append("\n  deaths in the first 30 s after the hand-over: ").Append(deathsAfterHandover);
        foreach (string h in hintOutcomes) sb.Append("\n  first-encounter ").Append(h);
        Debug.Log(sb.ToString());
    }
}
