using UnityEngine;

/// <summary>
/// Scene "Test Obstacles" (a copy of MainScene prop camera 3.11): a quick look at every obstacle-leg design. Sets the
/// generator up before it builds the path - the level-1 (Start) theme forever (no door: endlessLevel 1), no boosts, no
/// slow motion, and TEST MODE: every other leg is a ~10 s obstacle leg showing ONE design, the next one each time
/// (DynamicPathGenerator.TestSequence), looping. A bar under the HUD names the design you are on (or the next one) and
/// how to get through it.
///
/// Awake runs after CheckpointRunController's (1000) so its endless setting is the one that stands, and before any
/// Start, so the generator (Start, -100) builds the path with it.
/// </summary>
[DefaultExecutionOrder(1100)]
public class ObstacleTestScene : MonoBehaviour
{
    [Tooltip("Seconds each obstacle leg lasts.")]
    [Range(4f, 30f)] public float legSeconds = 10f;
    [Tooltip("First design of the sequence (0 = A-frame).")]
    public int startIndex = 0;
    [Tooltip("Start with a slow-motion section (level 3 numbers from the DifficultyProfile), and repeat it after every round of designs.")]
    public bool slowMotionFirst = true;
    [Tooltip("Show the bar naming the design.")]
    public bool showBar = true;

    DynamicPathGenerator gen;
    GUIStyle title, hint, box;

    void Awake()
    {
        gen = FindFirstObjectByType<DynamicPathGenerator>();
        if (gen == null) return;
        gen.endlessLevel = 1;               // the Start theme for good: no door is ever laid
        gen.checkpointStartPortal = 0;
        gen.obstacleLegSections = true;
        gen.obstacleLegDesigns = true;
        gen.obstacleTestMode = true;
        gen.obstacleTestLegSeconds = legSeconds;
        gen.obstacleTestStartIndex = startIndex;
        gen.boostSections = false;
        gen.slowMoSections = slowMotionFirst;   // a slow-motion section right at the start, again after each round
        gen.noSpecialsInFirstLevel = false;
    }

    static bool Special(DynamicPathGenerator.Segment s) => s.portalRole == 3 || s.slowMo;

    // The obstacle leg / slow-motion section the runner is on, else the next one laid ahead.
    DynamicPathGenerator.Segment Current(out bool now)
    {
        now = false;
        DynamicPathGenerator.Segment s = gen.PlayerSegment;
        if (s != null && Special(s)) { now = true; return s; }
        int serial = s != null ? s.serial : -1;
        for (int i = 0; ; i++)
        {
            DynamicPathGenerator.Segment x = gen.SegmentAt(i);
            if (x == null) return null;
            if (Special(x) && x.serial > serial) return x;
        }
    }

    void OnGUI()
    {
        if (!showBar || gen == null) return;
        DynamicPathGenerator.Segment s = Current(out bool now);
        if (s == null) return;
        if (s.slowMo)
        {
            int lvl = gen.Profile != null ? gen.Profile.slowMoFromLevel : 3;
            s.designLabel = "SLOW MOTION  -  tiny legs, from L" + lvl + "  (time x" + s.slowMoScale.ToString("0.00") + ", runner x" + s.slowMoSpeed.ToString("0.00") + ")";
            s.designHint = s.slowMoExit ? "exit leg: easing back to full speed" : "a half turn on every tiny leg - double tap early";
        }
        if (string.IsNullOrEmpty(s.designLabel)) return;

        float h = Screen.height, w = Screen.width;
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
            hint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            box = new GUIStyle(GUI.skin.box);
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.62f));
            tex.Apply();
            box.normal.background = tex;
        }
        title.fontSize = Mathf.RoundToInt(h * 0.022f);
        hint.fontSize = Mathf.RoundToInt(h * 0.016f);
        title.normal.textColor = now ? new Color(1f, 0.85f, 0.3f) : Color.white;
        hint.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

        // Along the bottom: the run ahead (top half) stays clear.
        Rect r = new Rect(w * 0.03f, h * 0.895f, w * 0.94f, h * 0.085f);
        GUI.Box(r, GUIContent.none, box);
        GUI.Label(new Rect(r.x, r.y + r.height * 0.04f, r.width, r.height * 0.5f), (now ? "NOW  " : "NEXT  ") + s.designLabel, title);
        GUI.Label(new Rect(r.x + r.width * 0.03f, r.y + r.height * 0.5f, r.width * 0.94f, r.height * 0.48f), s.designHint, hint);
    }
}
