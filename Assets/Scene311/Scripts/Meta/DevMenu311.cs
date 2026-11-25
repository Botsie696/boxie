#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;

/// <summary>
/// DEV ONLY (editor + development builds; compiled out of release). Scene 3.11 meta / skill test menu.
/// Open: F3, or tap the top-RIGHT corner 3 times quickly. Actions:
///   clock +1 hour / +1 day / +1 week, clock back 1 day (anti-cheat test), clear the offset
///   complete a mission, show the daily card, show / trigger the soft prompt, open the Hub
///   reset the meta save, reset the skill profile, force a tier (-2..+2) and a mercy step (0..2)
///   fire a test notification (10 s; leave the app), log the notification plan
/// </summary>
public class DevMenu311 : MonoBehaviour
{
    bool open;
    int taps;
    float tapAt;
    Rect win = new Rect(20, 120, 560, 980);
    float forceTier;
    int forceMercy;
    string status = "";

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F3)) open = !open;
        if (Input.GetMouseButtonDown(0) && Input.mousePosition.x > Screen.width * 0.88f && Input.mousePosition.y > Screen.height * 0.88f)
        {
            taps = Time.unscaledTime - tapAt < 0.5f ? taps + 1 : 1;
            tapAt = Time.unscaledTime;
            if (taps >= 3) { open = !open; taps = 0; }
        }
    }

    void OnGUI()
    {
        if (!open) return;
        float s = Mathf.Max(1f, Screen.height / 1100f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        win = GUI.Window(311, win, Draw, "3.11 DEV");
    }

    void Draw(int id)
    {
        var meta = MetaSystems311.Instance;
        var skill = SkillProfile311.Instance;
        var notes = Notifications311.Instance;
        var ui = FindFirstObjectByType<MetaUI311>();
        GUILayout.Label("now " + MetaClock311.LocalNow.ToString("yyyy-MM-dd HH:mm") + "  offset " + MetaClock311.DevOffset.TotalHours.ToString("0") + "h" +
                        (meta != null && meta.ClockRolledBack ? "  CLOCK BACK" : ""));
        if (meta != null)
        {
            GUILayout.Label("streak " + meta.Data.streak + "  last claim " + meta.Data.lastClaimDay + "  daily " + (meta.DailyAvailable ? "READY" : "taken") +
                            "  claimable " + meta.ClaimableCount + "  coins " + SaveSystem.Coins);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+1 hour")) meta.DevShiftClock(TimeSpan.FromHours(1));
            if (GUILayout.Button("+1 day")) meta.DevShiftClock(TimeSpan.FromDays(1));
            if (GUILayout.Button("+1 week")) meta.DevShiftClock(TimeSpan.FromDays(7));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("clock back 1 day")) meta.DevShiftClock(TimeSpan.FromDays(-1));
            if (GUILayout.Button("clear offset")) meta.DevShiftClock(-MetaClock311.DevOffset);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("complete a mission")) meta.DevCompleteMission();
            if (GUILayout.Button("reset meta save")) meta.DevReset();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (ui != null && GUILayout.Button("daily card")) ui.DevShowDaily();
            if (ui != null && GUILayout.Button("hub")) ui.OpenHub(0);
            if (ui != null && GUILayout.Button("soft prompt")) ui.DevShowPrompt();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("trigger prompt condition (day-2 / L3)")) meta.DevTriggerPrompt();
        }
        if (skill != null)
        {
            GUILayout.Space(8);
            GUILayout.Label("skill: rating " + skill.Model.S.rating.ToString("0") + "  tier " + skill.Model.Tier.ToString("0.00") +
                            "  applied CP" + skill.AppliedCheckpoint + " " + skill.AppliedTier.ToString("0.00") + " mercy " + skill.AppliedMercy);
            GUILayout.Label("force tier " + forceTier.ToString("+0.0;-0.0") + "  mercy " + forceMercy);
            forceTier = Mathf.Round(GUILayout.HorizontalSlider(forceTier, -2f, 2f) * 2f) / 2f;
            GUILayout.BeginHorizontal();
            for (int m = 0; m <= 2; m++) if (GUILayout.Button("mercy " + m)) forceMercy = m;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("apply tier/mercy")) { skill.ForceTier(forceTier, forceMercy); status = "applied: the next run / level uses it"; }
            if (GUILayout.Button("reset skill profile")) { skill.ResetProfile(); status = "skill reset"; }
            GUILayout.EndHorizontal();
        }
        if (notes != null)
        {
            GUILayout.Space(8);
            GUILayout.Label("notifications: " + (notes.IsOn ? "ON" : "off") + "  permission " + (meta != null ? meta.Data.permission : -1) +
                            "  usual hour " + notes.UsualHour() + (notes.Supported ? "" : "  (no OS here)"));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("fire test (10 s)")) notes.FireTest();
            if (GUILayout.Button("log plan"))
            {
                int n = notes.BuildPlan(out var p);
                var sb = new System.Text.StringBuilder("[Notifications311] plan:\n");
                for (int i = 0; i < n; i++) sb.Append(p[i].at.ToString("ddd yyyy-MM-dd HH:mm")).Append("  ").Append(p[i].title).Append(" / ").Append(p[i].text).Append('\n');
                Debug.Log(sb.ToString());
                status = n + " planned (see log)";
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.Label(status);
        if (GUILayout.Button("close")) open = false;
        GUI.DragWindow();
    }
}
#endif
