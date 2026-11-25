using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SCENE 3.11 META UI, built in code on its own canvas above the pause screen (sorting 320; the pause canvas is 300):
///   - toasts: small, top of the screen, no raycasts (never blocks a touch during play)
///   - pause screen: REWARDS and REMINDERS ON/OFF buttons beside RESTART / MENU (thumb zone)
///   - game-over panel: REWARDS and CLAIM ALL buttons (the panel's tap-to-restart is blocked over them)
///   - the Hub: Daily | Missions | Weekly | Awards, tabs and CLOSE at the bottom for one hand
///   - the daily reward card, once per day at the first scene start (game time held while it is up)
///   - the soft notification prompt (Yes / Not now) at a calm moment (game over, paused, between runs)
/// Everything is built once and pooled; text is rebuilt only when the meta data changes.
/// </summary>
[DefaultExecutionOrder(500)]
public class MetaUI311 : MonoBehaviour
{
    const int RowPool = 34;

    [Tooltip("Off for now: no REWARDS / CLAIM buttons, no Hub, no daily card (the meta data still runs and saves). The pause-screen REMINDERS toggle and the soft prompt stay.")]
    public bool showRewardsUI = false;

    MetaSystems311 meta;
    Notifications311 notes;
    CubeRailRunner runner;
    RunnerHUD hud;
    PauseController pause;
    TMP_FontAsset big, label;
    Canvas canvas;

    // toasts
    RectTransform toastRect;
    CanvasGroup toastGroup;
    TextMeshProUGUI toastText;
    readonly Queue<string> toasts = new Queue<string>(8);
    float toastT = -1f;

    // pause + game over extras
    RectTransform pauseExtras, overExtras;
    TextMeshProUGUI pauseRewardsLabel, remindersLabel, overRewardsLabel, claimAllLabel;
    GameObject claimAllButton, pauseRewardsButton;

    // hub
    GameObject hub;
    TextMeshProUGUI hubTitle, hubCoins;
    RectTransform content;
    ScrollRect scroll;
    readonly List<Row> rows = new List<Row>(RowPool);
    readonly Image[] tabImages = new Image[4];
    int tab;

    // daily card + soft prompt
    GameObject dailyCard, promptCard;
    TextMeshProUGUI dailyTitle, dailySub;
    readonly Image[] dayCells = new Image[7];
    readonly TextMeshProUGUI[] dayTexts = new TextMeshProUGUI[7];
    bool holdingTime, promptPending, dirtyUI = true;

    static readonly Color Panel = new Color(0.08f, 0.09f, 0.13f, 0.96f);
    static readonly Color RowBg = new Color(1f, 1f, 1f, 0.07f);
    static readonly Color Gold = new Color(1f, 0.79f, 0.27f, 1f);
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.45f);

    class Row
    {
        public GameObject go;
        public TextMeshProUGUI title, sub, buttonLabel;
        public Button button;
        public Image buttonImage;
        public System.Action action;
    }

    void Start()
    {
        meta = MetaSystems311.Instance;
        notes = Notifications311.Instance;
        runner = FindFirstObjectByType<CubeRailRunner>();
        hud = runner != null ? runner.GetComponent<RunnerHUD>() : null;
        pause = FindFirstObjectByType<PauseController>();
        if (pause != null) { big = pause.bigFont; label = pause.labelFont; }
        if (meta == null) { enabled = false; return; }
        Build();
        meta.Changed += OnChanged;
        meta.Toast += OnToast;
        meta.PromptDue += OnPromptDue;
        if (showRewardsUI && meta.DailyAvailable && meta.Data.dailyShownDay != MetaClock311.Today) ShowDaily();
    }

    void OnDestroy()
    {
        if (meta != null) { meta.Changed -= OnChanged; meta.Toast -= OnToast; meta.PromptDue -= OnPromptDue; }
        ReleaseTime();
        if (canvas != null) Destroy(canvas.gameObject);
    }

    void OnChanged() => dirtyUI = true;
    void OnToast(string s) { if (toasts.Count < 8) toasts.Enqueue(s); }
    void OnPromptDue() => promptPending = true;

    // ---------------------------------------------------------------------- frame

    bool tapBlockerHooked;

    void Update()
    {
        if (!tapBlockerHooked && hud != null)
        {
            // CheckpointRunController sets the panel's tap blocker in its Start; ours goes on top of it.
            var prev = hud.tapBlocker;
            hud.tapBlocker = pos => (prev != null && prev(pos)) || BlocksGameOverTap(pos);
            tapBlockerHooked = true;
        }

        bool paused = pause != null && pause.Current == PauseController.State.Paused;
        bool over = runner != null && runner.IsGameOver && hud != null && hud.GameOverPanelRect != null && hud.GameOverPanelRect.gameObject.activeInHierarchy;
        bool hubOpen = hub.activeSelf;
        bool cardUp = dailyCard.activeSelf || promptCard.activeSelf;
        SetActive(pauseExtras.gameObject, showRewardsUI && paused && !hubOpen && !cardUp);   // REWARDS + REMINDERS buttons: off with the rewards UI
        SetActive(overExtras.gameObject, showRewardsUI && over && !hubOpen && !cardUp);
        if (pauseRewardsButton != null) SetActive(pauseRewardsButton, showRewardsUI);
        if (!paused && !over && hubOpen && runner != null && runner.IsRunning && !runner.IsGameOver) CloseHub();   // never left over play

        // the soft prompt: only at a calm moment
        if (promptPending && !cardUp && !hubOpen && (over || paused || (runner != null && !runner.IsRunning)) && notes != null && notes.SoftPromptDue)
        {
            promptPending = false;
            promptCard.SetActive(true);
        }
        else if (promptPending && notes != null && !notes.SoftPromptDue) promptPending = false;

        if (dirtyUI) RefreshTexts();
        UpdateToast();
    }

    static void SetActive(GameObject g, bool on) { if (g.activeSelf != on) g.SetActive(on); }

    bool BlocksGameOverTap(Vector2 screenPos)
    {
        if (hub.activeSelf || promptCard.activeSelf || dailyCard.activeSelf) return true;
        if (!overExtras.gameObject.activeInHierarchy) return false;
        foreach (RectTransform r in overExtras)
            if (r.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(r, screenPos, null)) return true;
        return false;
    }

    void UpdateToast()
    {
        if (toastT < 0f)
        {
            if (toasts.Count == 0) return;
            toastText.text = toasts.Dequeue();
            toastT = 0f;
            toastRect.gameObject.SetActive(true);
        }
        toastT += Time.unscaledDeltaTime;
        float len = meta.tuning.toastSeconds;
        float a = toastT < 0.2f ? toastT / 0.2f : toastT > len - 0.3f ? Mathf.Clamp01((len - toastT) / 0.3f) : 1f;
        toastGroup.alpha = a;
        if (toastT >= len) { toastT = -1f; toastRect.gameObject.SetActive(false); }
    }

    // ---------------------------------------------------------------------- texts

    void RefreshTexts()
    {
        dirtyUI = false;
        int n = meta.ClaimableCount;
        string rewards = n > 0 ? "REWARDS  " + n : "REWARDS";
        pauseRewardsLabel.text = rewards;
        overRewardsLabel.text = rewards;
        int ready = n - (meta.DailyAvailable ? 1 : 0);
        claimAllButton.SetActive(ready > 0);
        if (ready > 0) claimAllLabel.text = "CLAIM " + ready;
        remindersLabel.text = notes != null && notes.IsOn ? "REMINDERS ON" : "REMINDERS OFF";
        if (hub.activeSelf) FillTab();
        if (dailyCard.activeSelf) FillDaily();
    }

    // ---------------------------------------------------------------------- hub

    public void OpenHub(int which = 0)
    {
        showRewardsUI = true;   // opened on purpose (dev menu)
        meta.Refresh();
        hub.SetActive(true);
        tab = which;
        FillTab();
        MenuFeedback.Light();
    }

    void CloseHub() { hub.SetActive(false); }

    void SelectTab(int t) { tab = t; FillTab(); MenuFeedback.Light(); }

    void FillTab()
    {
        for (int i = 0; i < tabImages.Length; i++) tabImages[i].color = i == tab ? new Color(Gold.r, Gold.g, Gold.b, 0.9f) : new Color(1f, 1f, 1f, 0.12f);
        hubCoins.text = SaveSystem.Coins + " coins";
        int used = 0;
        if (meta.ClockRolledBack) SetRow(used++, "Clock changed", "Rewards wait until the time catches up", null, null);
        switch (tab)
        {
            case 0: hubTitle.text = "DAILY"; used = FillDailyRows(used); break;
            case 1: hubTitle.text = "MISSIONS"; used = FillMissions(meta.Data.daily, used, true); break;
            case 2: hubTitle.text = "WEEKLY"; used = FillMissions(meta.Data.weekly, used, false); break;
            default: hubTitle.text = "AWARDS"; used = FillAwards(used); break;
        }
        for (int i = used; i < rows.Count; i++) if (rows[i].go.activeSelf) rows[i].go.SetActive(false);
    }

    int FillDailyRows(int used)
    {
        int streak = meta.NextStreak(out bool grace);
        bool can = meta.DailyAvailable;
        int shownStreak = can ? streak : meta.Data.streak;
        SetRow(used++, "Streak: " + meta.Data.streak + (meta.Data.streak == 1 ? " day" : " days"),
               can ? (grace ? "Today uses this week's grace day" : (streak == 1 && meta.Data.streak > 1 ? "The streak starts again today" : "Claim today to keep it")) : "Come back tomorrow",
               null, null);
        int today = meta.DayIndexFor(shownStreak);
        for (int d = 0; d < 7 && used < rows.Count; d++)
        {
            int coins = meta.tuning.dailyCoins[d % meta.tuning.dailyCoins.Length];
            bool isToday = d == today;
            string sub = isToday ? (can ? "Today" : "Claimed today") : d < today ? "Claimed" : "";
            SetRow(used++, "Day " + (d + 1) + "   " + coins + " coins", sub, isToday && can ? "CLAIM" : null, isToday && can ? (System.Action)ClaimDaily : null);
        }
        return used;
    }

    int FillMissions(List<Mission311> list, int used, bool daily)
    {
        bool reroll = daily && meta.RerollAvailable;
        for (int i = 0; i < list.Count && used < rows.Count; i++)
        {
            Mission311 m = list[i];
            string diff = m.difficulty == 0 ? "Easy" : m.difficulty == 1 ? "Medium" : "Hard";
            string sub = diff + "   " + m.progress + " / " + m.target + "   +" + m.coins;
            string btn = null; System.Action act = null;
            if (m.claimed) sub = diff + "   Claimed";
            else if (m.Done) { btn = "CLAIM"; int idx = i; act = () => { int c = meta.ClaimMission(list[idx]); if (c > 0) OnToast("+" + c + " coins"); }; }
            else if (reroll) { btn = "NEW"; int idx = i; act = () => meta.Reroll(idx); }
            SetRow(used++, m.text, sub, btn, act);
            if (btn == "CLAIM" && !meta.CanClaim) rows[used - 1].button.interactable = false;
        }
        if (daily && used < rows.Count) SetRow(used++, "", reroll ? "One free NEW mission a day" : "New missions tomorrow", null, null);
        return used;
    }

    int FillAwards(int used)
    {
        // Unlocked first (claimable), then the rest in order.
        foreach (AwardDef311 a in meta.tuning.awards)
        {
            if (!meta.Data.awardsUnlocked.Contains(a.id) || used >= rows.Count) continue;
            string id = a.id;
            SetRow(used++, a.title, a.text + "   +" + a.coins, "CLAIM", () => { int c = meta.ClaimAward(id); if (c > 0) OnToast("+" + c + " coins"); });
            if (!meta.CanClaim) rows[used - 1].button.interactable = false;
        }
        foreach (AwardDef311 a in meta.tuning.awards)
        {
            if (meta.Data.awardsUnlocked.Contains(a.id) || used >= rows.Count) continue;
            bool done = meta.Data.awardsClaimed.Contains(a.id);
            SetRow(used++, a.title, done ? "Done" : a.text + "   +" + a.coins, null, null);
            rows[used - 1].title.color = done ? Color.white : Dim;
        }
        return used;
    }

    void SetRow(int i, string title, string sub, string button, System.Action action)
    {
        Row r = rows[i];
        if (!r.go.activeSelf) r.go.SetActive(true);
        r.title.text = title;
        r.title.color = Color.white;
        r.sub.text = sub;
        r.action = action;
        bool b = button != null;
        if (r.button.gameObject.activeSelf != b) r.button.gameObject.SetActive(b);
        r.button.interactable = true;
        if (b) r.buttonLabel.text = button;
    }

    void ClaimDaily()
    {
        int c = meta.ClaimDaily();
        if (c > 0) OnToast("+" + c + " coins");
        dirtyUI = true;
    }

    // ---------------------------------------------------------------------- daily card

    void ShowDaily()
    {
        meta.Data.dailyShownDay = MetaClock311.Today;
        dailyCard.SetActive(true);
        FillDaily();
        HoldTime();
    }

    void FillDaily()
    {
        int streak = meta.NextStreak(out bool grace);
        int today = meta.DayIndexFor(streak);
        dailyTitle.text = "DAY " + (today + 1) + " REWARD";
        dailySub.text = (streak > 1 ? streak + "-day streak" : "A new streak starts") + (grace ? " (grace day)" : "");
        for (int d = 0; d < 7; d++)
        {
            int coins = meta.tuning.dailyCoins[d % meta.tuning.dailyCoins.Length];
            dayTexts[d].text = "DAY " + (d + 1) + "\n" + coins;
            dayCells[d].color = d == today ? Gold : d < today ? new Color(1f, 1f, 1f, 0.3f) : new Color(1f, 1f, 1f, 0.1f);
            dayTexts[d].color = d == today ? Color.black : Color.white;
        }
    }

    void ClaimDailyCard()
    {
        ClaimDaily();
        dailyCard.SetActive(false);
        ReleaseTime();
    }

    void CloseDailyCard() { dailyCard.SetActive(false); ReleaseTime(); }

    // The daily card is up before the run begins: game time is held (and given back exactly) while it shows.
    void HoldTime()
    {
        if (holdingTime) return;
        holdingTime = true;
        TimeScaleMixer.Set(TimeScaleMixer.Source.Meta, 0f);
        if (runner != null) runner.pauseInput = true;
    }

    void ReleaseTime()
    {
        if (!holdingTime) return;
        holdingTime = false;
        TimeScaleMixer.Set(TimeScaleMixer.Source.Meta, 1f);
        if (runner != null && (pause == null || pause.Current != PauseController.State.Paused)) runner.pauseInput = false;
    }

    // ---------------------------------------------------------------------- building

    void Build()
    {
        var go = new GameObject("MetaCanvas311", typeof(RectTransform));
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 320;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        RectTransform safe = MainMenuController.NewRect(go.transform, "SafeArea");
        MainMenuController.Stretch(safe);
        safe.gameObject.AddComponent<SafeArea>();

        // toast (no raycasts)
        Image t = MainMenuController.NewImage(safe, "Toast", MenuIcons.Pill, new Color(0f, 0f, 0f, 0.72f));
        t.type = Image.Type.Sliced;
        t.raycastTarget = false;
        toastRect = t.rectTransform;
        MainMenuController.Place(toastRect, new Vector2(0.5f, 1f), new Vector2(0f, -250f), new Vector2(820f, 140f), new Vector2(0.5f, 1f));
        toastGroup = t.gameObject.AddComponent<CanvasGroup>();
        toastGroup.blocksRaycasts = false;
        toastGroup.interactable = false;
        toastText = Text(toastRect, "Text", label, 34f, Color.white, true);
        MainMenuController.Stretch(toastText.rectTransform);
        toastRect.gameObject.SetActive(false);

        // pause extras (above RESTART / MENU, thumb zone)
        pauseExtras = MainMenuController.NewRect(safe, "PauseExtras");
        MainMenuController.Stretch(pauseExtras);
        pauseRewardsLabel = Button(pauseExtras, "Rewards", "REWARDS", new Vector2(0.5f, 0f), new Vector2(-170f, 470f), new Vector2(300f, 130f), () => OpenHub(0), out pauseRewardsButton);
        remindersLabel = Button(pauseExtras, "Reminders", "REMINDERS OFF", new Vector2(0.5f, 0f), new Vector2(170f, 470f), new Vector2(300f, 130f), ToggleReminders, out _);
        remindersLabel.fontSize = 30f;
        pauseExtras.gameObject.SetActive(false);

        // game-over extras (top of the screen; the panel's own buttons stay where they are)
        overExtras = MainMenuController.NewRect(safe, "GameOverExtras");
        MainMenuController.Stretch(overExtras);
        overRewardsLabel = Button(overExtras, "Rewards", "REWARDS", new Vector2(0.5f, 1f), new Vector2(-170f, -430f), new Vector2(300f, 120f), () => OpenHub(1), out _);
        claimAllLabel = Button(overExtras, "ClaimAll", "CLAIM", new Vector2(0.5f, 1f), new Vector2(170f, -430f), new Vector2(300f, 120f), ClaimAll, out claimAllButton);
        overExtras.gameObject.SetActive(false);

        BuildHub(safe);
        BuildDaily(safe);
        BuildPrompt(safe);
    }

    void ClaimAll()
    {
        int c = meta.ClaimAllReady();
        if (c > 0) OnToast("+" + c + " coins");
    }

    void ToggleReminders()
    {
        if (notes == null) return;
        notes.SetEnabled(!notes.IsOn);
        dirtyUI = true;
    }

    void BuildHub(RectTransform safe)
    {
        Image dim = MainMenuController.NewImage(safe, "Hub", null, new Color(0f, 0f, 0f, 0.7f));
        MainMenuController.Stretch(dim.rectTransform);
        hub = dim.gameObject;
        Image card = MainMenuController.NewImage(dim.rectTransform, "Card", MenuIcons.Pill, Panel);
        card.type = Image.Type.Sliced;
        RectTransform c = card.rectTransform;
        MainMenuController.Place(c, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(980f, 1560f), new Vector2(0.5f, 0.5f));

        hubTitle = Text(c, "Title", big, 60f, Color.white, false);
        MainMenuController.Place(hubTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(900f, 90f), new Vector2(0.5f, 0.5f));
        hubCoins = Text(c, "Coins", label, 30f, Gold, false);
        MainMenuController.Place(hubCoins.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(900f, 50f), new Vector2(0.5f, 0.5f));

        // scrolling rows
        RectTransform view = MainMenuController.NewRect(c, "View");
        MainMenuController.Place(view, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(920f, 1110f), new Vector2(0.5f, 1f));
        view.gameObject.AddComponent<RectMask2D>();
        var viewImg = view.gameObject.AddComponent<Image>();
        viewImg.color = new Color(0f, 0f, 0f, 0.001f);
        content = MainMenuController.NewRect(view, "Content");
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0f, 0f);
        var vl = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 12f; vl.padding = new RectOffset(0, 0, 4, 4);
        vl.childControlHeight = false; vl.childControlWidth = true; vl.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.content = content; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
        for (int i = 0; i < RowPool; i++) rows.Add(NewRow(content, i));

        // CLOSE and the tabs at the bottom (one hand)
        Button(c, "Close", "CLOSE", new Vector2(0.5f, 0f), new Vector2(0f, 230f), new Vector2(360f, 110f), CloseHub, out _);
        string[] names = { "DAILY", "MISSIONS", "WEEKLY", "AWARDS" };
        for (int i = 0; i < 4; i++)
        {
            int k = i;
            Button(c, "Tab" + i, names[i], new Vector2(0.5f, 0f), new Vector2(-345f + 230f * i, 90f), new Vector2(220f, 110f), () => SelectTab(k), out GameObject tg);
            tabImages[i] = tg.GetComponent<Image>();
        }
        hub.SetActive(false);
    }

    Row NewRow(RectTransform parent, int i)
    {
        Image bg = MainMenuController.NewImage(parent, "Row" + i, MenuIcons.Pill, RowBg);
        bg.type = Image.Type.Sliced;
        bg.rectTransform.sizeDelta = new Vector2(0f, 150f);
        var r = new Row { go = bg.gameObject };
        r.title = Text(bg.rectTransform, "Title", label, 32f, Color.white, true);
        r.title.alignment = TextAlignmentOptions.Left;
        MainMenuController.Place(r.title.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -14f), new Vector2(620f, 80f), new Vector2(0f, 1f));
        r.sub = Text(bg.rectTransform, "Sub", label, 26f, Dim, false);
        r.sub.alignment = TextAlignmentOptions.Left;
        MainMenuController.Place(r.sub.rectTransform, new Vector2(0f, 0f), new Vector2(30f, 14f), new Vector2(620f, 44f), new Vector2(0f, 0f));
        Button(bg.rectTransform, "Act", "CLAIM", new Vector2(1f, 0.5f), new Vector2(-130f, 0f), new Vector2(220f, 100f), () => r.action?.Invoke(), out GameObject bgo);
        r.button = bgo.GetComponent<Button>();
        r.buttonImage = bgo.GetComponent<Image>();
        r.buttonImage.color = new Color(Gold.r, Gold.g, Gold.b, 0.85f);
        r.buttonLabel = bgo.GetComponentInChildren<TextMeshProUGUI>();
        r.buttonLabel.color = Color.black;
        bg.gameObject.SetActive(false);
        return r;
    }

    void BuildDaily(RectTransform safe)
    {
        Image dim = MainMenuController.NewImage(safe, "Daily", null, new Color(0f, 0f, 0f, 0.7f));
        MainMenuController.Stretch(dim.rectTransform);
        dailyCard = dim.gameObject;
        Image card = MainMenuController.NewImage(dim.rectTransform, "Card", MenuIcons.Pill, Panel);
        card.type = Image.Type.Sliced;
        RectTransform c = card.rectTransform;
        MainMenuController.Place(c, new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(980f, 860f), new Vector2(0.5f, 0.5f));
        dailyTitle = Text(c, "Title", big, 64f, Gold, false);
        MainMenuController.Place(dailyTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(900f, 100f), new Vector2(0.5f, 0.5f));
        dailySub = Text(c, "Sub", label, 32f, Color.white, false);
        MainMenuController.Place(dailySub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(900f, 60f), new Vector2(0.5f, 0.5f));
        for (int d = 0; d < 7; d++)
        {
            Image cell = MainMenuController.NewImage(c, "Day" + d, MenuIcons.Pill, RowBg);
            cell.type = Image.Type.Sliced;
            MainMenuController.Place(cell.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-396f + 132f * d, 40f), new Vector2(120f, 170f), new Vector2(0.5f, 0.5f));
            dayCells[d] = cell;
            dayTexts[d] = Text(cell.rectTransform, "T", label, 26f, Color.white, true);
            MainMenuController.Stretch(dayTexts[d].rectTransform);
        }
        Button(c, "Claim", "CLAIM", new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(460f, 140f), ClaimDailyCard, out GameObject cg);
        cg.GetComponent<Image>().color = new Color(Gold.r, Gold.g, Gold.b, 0.95f);
        cg.GetComponentInChildren<TextMeshProUGUI>().color = Color.black;
        Button(c, "Later", "LATER", new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(260f, 90f), CloseDailyCard, out _);
        dailyCard.SetActive(false);
    }

    void BuildPrompt(RectTransform safe)
    {
        Image dim = MainMenuController.NewImage(safe, "Prompt", null, new Color(0f, 0f, 0f, 0.7f));
        MainMenuController.Stretch(dim.rectTransform);
        promptCard = dim.gameObject;
        Image card = MainMenuController.NewImage(dim.rectTransform, "Card", MenuIcons.Pill, Panel);
        card.type = Image.Type.Sliced;
        RectTransform c = card.rectTransform;
        MainMenuController.Place(c, new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(940f, 600f), new Vector2(0.5f, 0.5f));
        var txt = Text(c, "Text", big, 48f, Color.white, true);
        MainMenuController.Place(txt.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(820f, 240f), new Vector2(0.5f, 0.5f));
        txt.text = meta.tuning.softPromptText;
        Button(c, "Yes", "YES", new Vector2(0.5f, 0f), new Vector2(-180f, 110f), new Vector2(320f, 130f), () =>
        {
            promptCard.SetActive(false);
            if (notes != null) notes.SoftPromptYes();
            dirtyUI = true;
        }, out GameObject yes);
        yes.GetComponent<Image>().color = new Color(Gold.r, Gold.g, Gold.b, 0.95f);
        yes.GetComponentInChildren<TextMeshProUGUI>().color = Color.black;
        Button(c, "NotNow", "NOT NOW", new Vector2(0.5f, 0f), new Vector2(180f, 110f), new Vector2(320f, 130f), () =>
        {
            promptCard.SetActive(false);
            if (notes != null) notes.SoftPromptNotNow();
        }, out _);
        promptCard.SetActive(false);
    }

    TextMeshProUGUI Button(RectTransform parent, string name, string text, Vector2 anchor, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction action, out GameObject go)
    {
        Image img = MainMenuController.NewImage(parent, name, MenuIcons.Pill, new Color(1f, 1f, 1f, 0.16f));
        img.type = Image.Type.Sliced;
        MainMenuController.Place(img.rectTransform, anchor, pos, size, new Vector2(0.5f, 0.5f));
        var b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(action);
        img.gameObject.AddComponent<PressScale>();
        var t = Text(img.rectTransform, "Label", big, 38f, Color.white, false);
        MainMenuController.Stretch(t.rectTransform);
        t.text = text;
        go = img.gameObject;
        return t;
    }

    static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, float size, Color c, bool wrap)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = c;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    // ---------------------------------------------------------------------- dev

    public void DevShowDaily() { showRewardsUI = true; if (meta.DailyAvailable) ShowDaily(); }
    public void DevShowPrompt() { promptCard.SetActive(true); }
}
