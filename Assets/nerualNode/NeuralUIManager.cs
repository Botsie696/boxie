using UnityEngine;
using UnityEngine.UI;
using System.Linq;

public class NeuralUIManager : MonoBehaviour
{
    public static NeuralUIManager Instance;

    private Text mainLabel;
    private Image progressFill;
    private RectTransform indicatorsRoot;

    void Awake()
    {
        Instance = this;
        BuildUI();
    }

    void BuildUI()
    {
        var canvasGO = new GameObject("NeuralUICanvas");
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel");
        panel.transform.SetParent(canvasGO.transform, false);
        var pRT = panel.AddComponent<RectTransform>();
        pRT.anchorMin = new Vector2(0.5f, 0);
        pRT.anchorMax = new Vector2(0.5f, 0);
        pRT.pivot = new Vector2(0.5f, 0);
        pRT.anchoredPosition = new Vector2(0, 45);
        pRT.sizeDelta = new Vector2(420, 110);
        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.01f, 0.02f, 0.05f, 0.7f);

        var lblGO = new GameObject("Label");
        lblGO.transform.SetParent(panel.transform, false);
        var lRT = lblGO.AddComponent<RectTransform>();
        lRT.anchorMin = new Vector2(0, 1);
        lRT.anchorMax = new Vector2(1, 1);
        lRT.pivot = new Vector2(0.5f, 1);
        lRT.anchoredPosition = new Vector2(0, -18);
        lRT.sizeDelta = new Vector2(0, 30);
        mainLabel = lblGO.AddComponent<Text>();
        mainLabel.font = GetFont();
        mainLabel.fontSize = 24;
        mainLabel.color = new Color(0.85f, 0.92f, 1f, 1f);
        mainLabel.text = "REMAINING PAIRS: 0 / 0";
        mainLabel.alignment = TextAnchor.MiddleCenter;

        var indGO = new GameObject("Indicators");
        indGO.transform.SetParent(panel.transform, false);
        indicatorsRoot = indGO.AddComponent<RectTransform>();
        indicatorsRoot.anchorMin = new Vector2(0, 0);
        indicatorsRoot.anchorMax = new Vector2(1, 0);
        indicatorsRoot.anchoredPosition = new Vector2(0, 42);
        indicatorsRoot.sizeDelta = new Vector2(0, 20);

        var barBgGO = new GameObject("BarBg");
        barBgGO.transform.SetParent(panel.transform, false);
        var bRT = barBgGO.AddComponent<RectTransform>();
        bRT.anchorMin = new Vector2(0.15f, 0);
        bRT.anchorMax = new Vector2(0.85f, 0);
        bRT.pivot = new Vector2(0.5f, 0);
        bRT.anchoredPosition = new Vector2(0, 18);
        bRT.sizeDelta = new Vector2(0, 5);
        barBgGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);

        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(barBgGO.transform, false);
        var fRT = fillGO.AddComponent<RectTransform>();
        fRT.anchorMin = Vector2.zero;
        fRT.anchorMax = Vector2.one;
        fRT.offsetMin = Vector2.zero;
        fRT.offsetMax = Vector2.zero;
        progressFill = fillGO.AddComponent<Image>();
        progressFill.color = new Color(0.35f, 0.75f, 1f, 0.95f);
        progressFill.type = Image.Type.Filled;
        progressFill.fillMethod = Image.FillMethod.Horizontal;
        progressFill.fillAmount = 1f;
    }

    Font GetFont()
    {
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return f;
    }

    public void Init(int total) => Refresh(total, total);
    public void OnMerge(int remaining, int total) => Refresh(remaining, total);

    public void OnWin()
    {
        if (mainLabel != null) mainLabel.text = "MISSION COMPLETE";
        if (progressFill != null) progressFill.color = new Color(0.4f, 1f, 0.6f, 0.95f);
    }

    void Refresh(int remaining, int total)
    {
        var generator = Object.FindAnyObjectByType<NeuralGlobeGenerator>();
        bool isLevel2 = generator != null && generator.level2;
        
        if (isLevel2 && generator != null)
        {
            int totalActive = generator.GetNodes().Count(n => !n.isNeutral);
            if (mainLabel != null) mainLabel.text = $"ACTIVE NODES: {totalActive}";
            if (progressFill != null) progressFill.fillAmount = 1f;
        }
        else
        {
            if (mainLabel != null) mainLabel.text = $"REMAINING PAIRS: {remaining} / {total}";
            if (progressFill != null && total > 0)
                progressFill.fillAmount = (float)remaining / total;
        }

        UpdateIndicators(remaining, total);
    }

    void UpdateIndicators(int remaining, int total)
    {
        if (indicatorsRoot == null) return;
        foreach (Transform child in indicatorsRoot) Destroy(child.gameObject);

        float spacing = 28f;
        float startX = -(total - 1) * spacing * 0.5f;

        for (int i = 0; i < total; i++)
        {
            var dot = new GameObject("Dot");
            dot.transform.SetParent(indicatorsRoot, false);
            var dRT = dot.AddComponent<RectTransform>();
            dRT.anchoredPosition = new Vector2(startX + i * spacing, 0);
            dRT.sizeDelta = new Vector2(14, 14);
            var img = dot.AddComponent<Image>();
            bool isMerged = i < (total - remaining);
            img.color = isMerged ? new Color(1, 1, 1, 0.25f) : new Color(0.35f, 0.85f, 1f, 1f);
        }
    }
}