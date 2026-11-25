using System.Text;
using UnityEngine;

/// <summary>
/// An in-game panel with a slider for every look setting on the path: the guide trail, the route
/// colour, brightness, cube size, and how far ahead cubes stay full size and opaque. Tap "Tune"
/// (top left) to open it. Everything changes live, so you can dial it in while the runner moves.
///
/// Changes are made to the real components (DynamicPathGenerator and PathDepthShading) and are
/// lost when Play stops, like any Play-mode edit - press "Log values" to print them to the
/// Console so they can be copied back into the Inspector or into the defaults.
///
/// Added automatically by DynamicPathGenerator while its showTuningPanel is on.
/// </summary>
public class PathVisualTuner : MonoBehaviour
{
    public DynamicPathGenerator pathGen;
    public PathDepthShading shading;
    CameraParentRotator rig;

    [Tooltip("Panel text/slider scale on top of the automatic screen-size scaling.")]
    [Range(0.5f, 3f)] public float uiScale = 1f;

    bool open;
    Vector2 scroll;
    GUIStyle header;
    GUIStyle label;

    void Start()
    {
        if (pathGen == null) pathGen = FindFirstObjectByType<DynamicPathGenerator>();
        if (shading == null && pathGen != null) shading = pathGen.GetComponent<PathDepthShading>();
    }

    void OnGUI()
    {
        if (pathGen == null) return;
        if (shading == null) shading = pathGen.GetComponent<PathDepthShading>();

        float s = Mathf.Clamp(Screen.height / 900f, 0.8f, 2.5f) * uiScale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = Screen.width / s;
        float h = Screen.height / s;

        header ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13 };
        label ??= new GUIStyle(GUI.skin.label) { fontSize = 12 };

        if (GUI.Button(new Rect(6, 6, 60, 26), open ? "Close" : "Tune")) open = !open;
        if (!open) return;

        float panelW = Mathf.Min(300f, w - 12f);
        var panel = new Rect(6, 36, panelW, h - 44f);
        GUI.Box(panel, GUIContent.none);

        GUILayout.BeginArea(new Rect(panel.x + 6, panel.y + 4, panel.width - 12, panel.height - 8));
        scroll = GUILayout.BeginScrollView(scroll);

        // ---- Camera ----
        if (rig == null) rig = FindFirstObjectByType<CameraParentRotator>();
        if (rig != null)
        {
            GUILayout.Label("Camera", header);
            rig.zoomOutMultiplier = Slider("Zoom out x", rig.zoomOutMultiplier, 0.6f, 2.5f);
            Gap();
        }

        // ---- Guide trail ----
        GUILayout.Label("Guide trail", header);
        pathGen.showGuideTrail = GUILayout.Toggle(pathGen.showGuideTrail, " Show trail");
        pathGen.guideTrailLengthCubes = Slider("Length ahead (cubes)", pathGen.guideTrailLengthCubes, 2f, 60f);
        pathGen.guideTrailWidth = Slider("Thickness", pathGen.guideTrailWidth, 0.02f, 0.6f);
        pathGen.guideTrailStartAhead = Slider("Starts ahead (cubes)", pathGen.guideTrailStartAhead, 0f, 3f);
        pathGen.guideTrailLift = Slider("Lift toward camera", pathGen.guideTrailLift, 0.9f, 3f);
        Color tc = pathGen.guideTrailColor;
        tc.a = Slider("Strength", tc.a, 0.05f, 1f);
        pathGen.guideTrailColor = tc;

        if (shading == null)
        {
            GUILayout.Label("Route shading is off (PathManager > Route Shading).", label);
        }
        else
        {
            // ---- Visible window ----
            Gap();
            GUILayout.Label("Visible track", header);
            shading.limitVisibleLegs = GUILayout.Toggle(shading.limitVisibleLegs, " Limit to nearby legs");
            shading.visibleLegsAhead = Mathf.RoundToInt(Slider("Legs ahead", shading.visibleLegsAhead, 1f, 8f));
            shading.visibleLegsBehind = Mathf.RoundToInt(Slider("Legs behind", shading.visibleLegsBehind, 0f, 6f));
            shading.edgeFadeCubes = Slider("Far edge fade (cubes)", shading.edgeFadeCubes, 1f, 20f);
            shading.revealCubesPerSecond = Slider("Draw-in speed", shading.revealCubesPerSecond, 5f, 150f);
            shading.behindFadeOutCubes = Slider("Rear edge fade (cubes)", shading.behindFadeOutCubes, 1f, 30f);
            shading.hideCubesPerSecond = Slider("Rear go-away speed", shading.hideCubesPerSecond, 5f, 200f);
            shading.matchRunnerTrailToVisibleTrack = GUILayout.Toggle(shading.matchRunnerTrailToVisibleTrack, " Runner trail ends at last cube");

            // ---- Cube size ----
            Gap();
            GUILayout.Label("Cube size", header);
            shading.cubeVisualScale = Slider("Cube thickness", shading.cubeVisualScale, 0.3f, 1.5f);
            shading.farScale = Slider("Far cube size", shading.farScale, 0.05f, 1f);
            shading.fullSizeAheadCubes = Slider("Full size ahead (cubes)", shading.fullSizeAheadCubes, 0f, 80f);
            shading.shrinkSpanCubes = Slider("Shrink over (cubes)", shading.shrinkSpanCubes, 1f, 80f);

            // ---- Transparency ----
            Gap();
            GUILayout.Label("Transparency", header);
            shading.opaqueAheadCubes = Slider("Opaque ahead (cubes)", shading.opaqueAheadCubes, 0f, 80f);
            shading.fadeSpanCubes = Slider("Fade over (cubes)", shading.fadeSpanCubes, 1f, 80f);
            shading.farAlpha = Slider("Far opacity", shading.farAlpha, 0f, 1f);

            // ---- Brightness & colour ----
            Gap();
            GUILayout.Label("Brightness & colour", header);
            shading.nearBrightness = Slider("Next-cube brightness", shading.nearBrightness, 1f, 2.5f);
            shading.brightSpanCubes = Slider("Brightness fades over", shading.brightSpanCubes, 2f, 40f);
            shading.farBrightness = Slider("Far brightness", shading.farBrightness, 0.5f, 2f);
            shading.colourSpanCubes = Slider("Colour shift over (cubes)", shading.colourSpanCubes, 4f, 80f);
            shading.behindFadeCubes = Slider("Covered track fades over", shading.behindFadeCubes, 1f, 30f);
            shading.colorResponse = Slider("Colour follow speed", shading.colorResponse, 0.5f, 20f);

            // ---- Depth cues ----
            Gap();
            GUILayout.Label("Depth cues", header);
            shading.depthDarkening = Slider("Far-from-camera dark", shading.depthDarkening, 0f, 0.7f);
            shading.depthRange = Slider("  range", shading.depthRange, 2f, 80f);
            shading.lowerLayerDarkening = Slider("Lower-layer dark", shading.lowerLayerDarkening, 0f, 0.7f);
            shading.lowerLayerRange = Slider("  range", shading.lowerLayerRange, 2f, 60f);
            shading.cuesOnImmediateRoute = Slider("Cues on next cubes", shading.cuesOnImmediateRoute, 0f, 1f);
        }

        // ---- Coins ----
        // Placement sliders shape legs as they are generated; "Re-roll" lays the legs not yet on
        // screen again so a change shows at once. Look sliders apply live.
        Gap();
        GUILayout.Label("Coins", header);
        pathGen.spawnCoins = GUILayout.Toggle(pathGen.spawnCoins, " Spawn coins");
        pathGen.coinSpawnChance = Slider("Chance per leg", pathGen.coinSpawnChance, 0f, 1f);
        pathGen.minCoinsPerRun = Mathf.RoundToInt(Slider("Min coins per run", pathGen.minCoinsPerRun, 1f, 30f));
        pathGen.maxCoinsPerRun = Mathf.RoundToInt(Slider("Max coins per run", pathGen.maxCoinsPerRun, 1f, 30f));
        pathGen.coinSpacing = Mathf.RoundToInt(Slider("Every Nth cube", pathGen.coinSpacing, 1f, 6f));
        pathGen.coinTurnMargin = Mathf.RoundToInt(Slider("Clear before turn", pathGen.coinTurnMargin, 0f, 12f));
        pathGen.coinLegStartMargin = Mathf.RoundToInt(Slider("Clear at leg start", pathGen.coinLegStartMargin, 0f, 12f));
        if (GUILayout.Button("Re-roll coins ahead")) pathGen.RerollCoinsAhead();
        pathGen.coinSize = Slider("Coin size", pathGen.coinSize, 0.2f, 1.6f);
        pathGen.coinOutwardOffset = Slider("Height off face", pathGen.coinOutwardOffset, 0.8f, 2f);
        pathGen.coinSpinSpeed = Slider("Spin speed", pathGen.coinSpinSpeed, 0f, 720f);
        pathGen.coinSpinWave = Slider("Spin wave", pathGen.coinSpinWave, 0f, 90f);
        pathGen.coinGlow = Slider("Glow", pathGen.coinGlow, 0f, 2f);
        pathGen.coinPickupRadius = Slider("Pickup radius", pathGen.coinPickupRadius, 0.2f, 1.2f);
        pathGen.coinsCastShadows = GUILayout.Toggle(pathGen.coinsCastShadows, " Coins cast shadows");
        GUILayout.Label($"Collected {(pathGen.Coins != null ? pathGen.Coins.currentScore : 0)}   on track {pathGen.CoinsOnTrack}", label);

        // ---- Corner gems ----
        // The diamond in the notch of every sideways turn. Look sliders apply live; the lift is
        // read when a corner is laid, so only corners generated after a change take it.
        Gap();
        GUILayout.Label("Corner gems", header);
        pathGen.spawnCornerGems = GUILayout.Toggle(pathGen.spawnCornerGems, " Gems at sideways turns");
        pathGen.cornerGemSize = Slider("Diamond size", pathGen.cornerGemSize, 0.1f, 4f);
        pathGen.cornerGemStretch = Slider("Diamond height", pathGen.cornerGemStretch, 0.6f, 2.5f);
        pathGen.cornerGemLift = Slider("Height over corner*", pathGen.cornerGemLift, -0.5f, 6f);
        pathGen.cornerGemThickness = Slider("Outline thickness", pathGen.cornerGemThickness, 0.005f, 0.4f);
        pathGen.cornerGemBleed = Slider("Line bleed", pathGen.cornerGemBleed, 0f, 2f);
        pathGen.cornerGemFill = Slider("Inside fill", pathGen.cornerGemFill, 0f, 1f);
        pathGen.cornerGemSpin = Slider("Spin speed", pathGen.cornerGemSpin, 0f, 360f);
        pathGen.cornerGemBob = Slider("Bob height", pathGen.cornerGemBob, 0f, 0.5f);
        pathGen.cornerGemBobSpeed = Slider("Bob speed", pathGen.cornerGemBobSpeed, 0f, 3f);
        pathGen.cornerGemGlow = GUILayout.Toggle(pathGen.cornerGemGlow, " Halo");
        pathGen.cornerGemGlowSize = Slider("Halo size", pathGen.cornerGemGlowSize, 0.5f, 8f);
        pathGen.cornerGemGlowStrength = Slider("Halo brightness", pathGen.cornerGemGlowStrength, 0f, 3f);

        Gap();
        if (GUILayout.Button("Log values to Console")) Log();
        GUILayout.Space(8);

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    static void Gap() => GUILayout.Space(8);

    float Slider(string text, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(text, label, GUILayout.Width(150));
        GUILayout.Label(value.ToString("0.##"), label, GUILayout.Width(40));
        GUILayout.EndHorizontal();
        return GUILayout.HorizontalSlider(value, min, max);
    }

    void Log()
    {
        var sb = new StringBuilder("Path visual settings:\n");
        sb.AppendLine($"  Guide trail: show={pathGen.showGuideTrail} length={pathGen.guideTrailLengthCubes:0.##} " +
                      $"width={pathGen.guideTrailWidth:0.###} startAhead={pathGen.guideTrailStartAhead:0.##} " +
                      $"lift={pathGen.guideTrailLift:0.##} strength={pathGen.guideTrailColor.a:0.##}");
        if (shading != null)
        {
            sb.AppendLine($"  Cube size: thickness={shading.cubeVisualScale:0.##} farScale={shading.farScale:0.##} " +
                          $"fullSizeAhead={shading.fullSizeAheadCubes:0.#} shrinkSpan={shading.shrinkSpanCubes:0.#}");
            sb.AppendLine($"  Transparency: opaqueAhead={shading.opaqueAheadCubes:0.#} fadeSpan={shading.fadeSpanCubes:0.#} " +
                          $"farAlpha={shading.farAlpha:0.##}");
            sb.AppendLine($"  Brightness: near={shading.nearBrightness:0.##} span={shading.brightSpanCubes:0.#} " +
                          $"far={shading.farBrightness:0.##} colourSpan={shading.colourSpanCubes:0.#} " +
                          $"behindFade={shading.behindFadeCubes:0.#} response={shading.colorResponse:0.#}");
            sb.AppendLine($"  Depth cues: depth={shading.depthDarkening:0.##}/{shading.depthRange:0.#} " +
                          $"layer={shading.lowerLayerDarkening:0.##}/{shading.lowerLayerRange:0.#} " +
                          $"onNextCubes={shading.cuesOnImmediateRoute:0.##}");
        }
        sb.AppendLine($"  Coins: spawn={pathGen.spawnCoins} chance={pathGen.coinSpawnChance:0.##} " +
                      $"perRun={pathGen.minCoinsPerRun}-{pathGen.maxCoinsPerRun} spacing={pathGen.coinSpacing} " +
                      $"turnMargin={pathGen.coinTurnMargin} legStartMargin={pathGen.coinLegStartMargin} " +
                      $"size={pathGen.coinSize:0.##} offset={pathGen.coinOutwardOffset:0.##} spin={pathGen.coinSpinSpeed:0} " +
                      $"wave={pathGen.coinSpinWave:0} glow={pathGen.coinGlow:0.##} pickup={pathGen.coinPickupRadius:0.##} " +
                      $"shadows={pathGen.coinsCastShadows}");
        sb.AppendLine($"  Corner gems: spawn={pathGen.spawnCornerGems} size={pathGen.cornerGemSize:0.##} " +
                      $"stretch={pathGen.cornerGemStretch:0.##} lift={pathGen.cornerGemLift:0.##} " +
                      $"spin={pathGen.cornerGemSpin:0} bob={pathGen.cornerGemBob:0.##}/{pathGen.cornerGemBobSpeed:0.##} " +
                      $"thickness={pathGen.cornerGemThickness:0.###} bleed={pathGen.cornerGemBleed:0.##} fill={pathGen.cornerGemFill:0.##} " +
                      $"halo={pathGen.cornerGemGlow} haloSize={pathGen.cornerGemGlowSize:0.##} haloStrength={pathGen.cornerGemGlowStrength:0.##}");
        Debug.Log(sb.ToString());
    }
}
