using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using Unity.Notifications.Android;
#endif
#if UNITY_IOS
using Unity.Notifications.iOS;
#endif

/// <summary>
/// SCENE 3.11 LOCAL NOTIFICATIONS (com.unity.mobile.notifications; nothing leaves the device).
///
///   Permission   never asked at launch. After the first daily-reward claim on day 2, or the first L3 clear (whichever
///                comes first), MetaUI311 shows an in-game soft prompt; only "Yes" opens the OS dialog (Android 13+
///                POST_NOTIFICATIONS, iOS authorization). "Not now" asks again at most once, 7 days later. A toggle in
///                the 3.11 pause screen turns it on / off.
///   Schedule     cancelled and rebuilt on every app pause / quit, at most one a day:
///                  day +1  the daily reward is ready, at the player's usual play hour (learned from session starts,
///                          clamped 10:00-21:00) - or, with a streak of 3+, "streak at risk" at 19:00
///                  day +3, +7  re-engagement, then nothing
///                Nothing when the toggle is off or permission is denied. Android channel "daily". The text names the
///                reward ("Day 4 reward: 150 coins is waiting").
/// </summary>
public class Notifications311 : MonoBehaviour
{
    public static Notifications311 Instance { get; private set; }

    MetaSystems311 meta;
    MetaTuning311 T => meta != null ? meta.tuning : null;
    bool requesting;

    public bool Supported =>
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        true;
#else
        false;
#endif

    /// <summary>The toggle shows ON only with permission granted and the player's choice on.</summary>
    public bool IsOn => meta != null && meta.Data.notifyEnabled && meta.Data.permission == 1;

    void Awake() { Instance = this; }

    void Start()
    {
        meta = MetaSystems311.Instance;
#if UNITY_ANDROID
        if (Supported && T != null)
            AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel(T.androidChannelId, T.androidChannelName, "Daily reward reminders", Importance.Default));
#endif
        RefreshPermission();
        CancelAll();   // the player is here: nothing pending should fire now
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void OnApplicationPause(bool paused)
    {
        if (paused) Reschedule();
        else { RefreshPermission(); CancelAll(); }
    }

    void OnApplicationQuit() => Reschedule();

    // ---------------------------------------------------------------------- soft prompt

    /// <summary>Should the in-game "Get a reminder?" card be offered now?</summary>
    public bool SoftPromptDue
    {
        get
        {
            if (meta == null || !meta.Data.promptTriggered || meta.Data.permission == 1 && meta.Data.notifyEnabled) return false;
            if (meta.Data.softPromptState == 0) return true;
            if (meta.Data.softPromptState == 1)
                return MetaClock311.UtcNow.Ticks >= meta.Data.softPromptAtTicks + TimeSpan.FromDays(T.softPromptRepeatDays).Ticks;
            return false;
        }
    }

    public void SoftPromptYes()
    {
        meta.Data.softPromptState = 2;
        RequestPermission(granted => { meta.Data.notifyEnabled = granted; });
    }

    public void SoftPromptNotNow()
    {
        meta.Data.softPromptState = meta.Data.softPromptState == 0 ? 1 : 2;
        meta.Data.softPromptAtTicks = MetaClock311.UtcNow.Ticks;
    }

    /// <summary>The pause-screen toggle.</summary>
    public void SetEnabled(bool on)
    {
        if (meta == null) return;
        if (!on) { meta.Data.notifyEnabled = false; CancelAll(); return; }
        if (meta.Data.permission == 1) { meta.Data.notifyEnabled = true; return; }
        RequestPermission(granted => { meta.Data.notifyEnabled = granted; });
    }

    void RequestPermission(Action<bool> done)
    {
        if (requesting) return;
#if UNITY_EDITOR
        // The editor has no OS dialog: treat it as granted so the flow can be tried.
        meta.Data.permission = 1;
        done?.Invoke(true);
#elif UNITY_ANDROID || UNITY_IOS
        StartCoroutine(Request(done));
#else
        done?.Invoke(false);
#endif
    }

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
    IEnumerator Request(Action<bool> done)
    {
        requesting = true;
        bool granted = false;
#if UNITY_ANDROID
        var req = new PermissionRequest();
        while (req.Status == PermissionStatus.RequestPending) yield return null;
        granted = req.Status == PermissionStatus.Allowed;
#elif UNITY_IOS
        var req = new AuthorizationRequest(AuthorizationOption.Alert | AuthorizationOption.Badge | AuthorizationOption.Sound, false);
        while (!req.IsFinished) yield return null;
        granted = req.Granted;
        req.Dispose();
#endif
        meta.Data.permission = granted ? 1 : 2;
        requesting = false;
        done?.Invoke(granted);
    }
#endif

    void RefreshPermission()
    {
        if (meta == null || !Supported) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        PermissionStatus s = AndroidNotificationCenter.UserPermissionToPost;
        if (s == PermissionStatus.Allowed) meta.Data.permission = 1;
        else if (s == PermissionStatus.Denied || s == PermissionStatus.DeniedDontAskAgain) meta.Data.permission = 2;
#elif UNITY_IOS && !UNITY_EDITOR
        var st = iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus;
        if (st == AuthorizationStatus.Authorized || st == AuthorizationStatus.Provisional) meta.Data.permission = 1;
        else if (st == AuthorizationStatus.Denied) meta.Data.permission = 2;
#endif
    }

    // ---------------------------------------------------------------------- schedule

    public struct Planned { public DateTime at; public string title, text; public int id; }
    readonly Planned[] plan = new Planned[3];
    int planCount;

    /// <summary>What would be sent if the app were left now (also used by the dev menu and tests).</summary>
    public int BuildPlan(out Planned[] result)
    {
        planCount = 0;
        result = plan;
        if (meta == null || T == null) return 0;
        MetaSave311 d = meta.Data;
        DateTime now = MetaClock311.LocalNow;
        int hour = UsualHour();
        int nextStreak = d.streak + 1;
        int coins = meta.DailyCoinsFor(nextStreak);
        int dayNo = meta.DayIndexFor(nextStreak) + 1;

        // day +1: the daily reward (or the streak at risk)
        DateTime tomorrow = now.Date.AddDays(1);
        if (d.streak >= T.streakRiskMinStreak)
            Add(tomorrow.AddHours(T.streakRiskHour), string.Format(T.titleStreak, d.streak), string.Format(T.textStreak, d.streak, dayNo, coins), 1);
        else
            Add(tomorrow.AddHours(hour), T.titleDaily, string.Format(T.textDaily, dayNo, coins), 1);
        // re-engagement: day 3, day 7 (day 1 is the one above), then stop
        foreach (int days in T.reengageDays)
        {
            if (days <= 1) continue;
            Add(now.Date.AddDays(days).AddHours(hour), T.titleReengage, string.Format(T.textReengage, 1, meta.DailyCoinsFor(1)), 10 + days);
        }
        return planCount;
    }

    void Add(DateTime at, string title, string text, int id)
    {
        if (planCount >= plan.Length) return;
        plan[planCount++] = new Planned { at = at, title = title, text = text, id = id };
    }

    /// <summary>The hour most sessions start at, clamped to the notification window.</summary>
    public int UsualHour()
    {
        int best = -1, bestCount = 0;
        int[] h = meta.Data.playHours;
        for (int i = 0; i < 24; i++) if (h[i] > bestCount) { bestCount = h[i]; best = i; }
        if (best < 0) best = T.defaultPlayHour;
        return Mathf.Clamp(best, T.notifyHourWindow.x, T.notifyHourWindow.y);
    }

    public void Reschedule()
    {
        CancelAll();
        if (meta == null || !IsOn || !Supported) return;
        int n = BuildPlan(out Planned[] p);
        for (int i = 0; i < n; i++) Send(p[i]);
    }

    void Send(Planned p)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        var note = new AndroidNotification { Title = p.title, Text = p.text, FireTime = p.at };
        AndroidNotificationCenter.SendNotificationWithExplicitID(note, T.androidChannelId, p.id);
#elif UNITY_IOS && !UNITY_EDITOR
        var trigger = new iOSNotificationCalendarTrigger { Year = p.at.Year, Month = p.at.Month, Day = p.at.Day, Hour = p.at.Hour, Minute = p.at.Minute, Repeats = false };
        iOSNotificationCenter.ScheduleNotification(new iOSNotification
        {
            Identifier = "r311_" + p.id, Title = p.title, Body = p.text, ShowInForeground = false,
            ForegroundPresentationOption = PresentationOption.Alert, CategoryIdentifier = "daily", ThreadIdentifier = "daily", Trigger = trigger,
        });
#endif
    }

    public void CancelAll()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidNotificationCenter.CancelAllScheduledNotifications();
        AndroidNotificationCenter.CancelAllDisplayedNotifications();
#elif UNITY_IOS && !UNITY_EDITOR
        iOSNotificationCenter.RemoveAllScheduledNotifications();
        iOSNotificationCenter.RemoveAllDeliveredNotifications();
#endif
    }

    /// <summary>Dev menu: one notification in 10 seconds (leave the app to see it).</summary>
    public void FireTest()
    {
        if (T == null) return;
        Send(new Planned { at = DateTime.Now.AddSeconds(10), title = T.titleDaily, text = string.Format(T.textDaily, 4, 150), id = 99 });
        Debug.Log("[Notifications311] test notification in 10 s" + (Supported ? "" : " (not on this platform: nothing is sent)"));
    }
}
