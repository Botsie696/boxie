using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Applies one of the three reference looks to the OPEN scene ("MainScene prop camera 1/2/3").
/// Every colour is measured from the reference images (see the Look table below); the materials,
/// volume profiles and renderer it makes live in Assets/PropCameraLooks. Re-running it simply
/// re-applies the values, so tune here and run again.
///
/// It only changes looks: materials, colours, the camera's renderer/post-processing switch, a
/// global Volume, lights and the PropLookController. The path generator's logic, the runner's
/// physics and the camera rig's framing are left alone.
/// </summary>
public static class PropLooksSceneBuilder
{
    const string Root = "Assets/PropCameraLooks";

    static Color H(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color c); return c; }

    public class Look
    {
        public string key;
        // Blocks (camera-relative faces)
        public Color top, left, right, bottom, shadow;
        public float shadowStrength = 0.55f;
        public Color seam = Color.white; public float seamStrength, seamWidth = 0.016f;
        public float rimStrength; public Color rim = Color.white;
        public float grain, grainScale = 14f;
        public Color fogTop, fogBottom; public float fogStart, fogEnd, fogMax, screenFogHeight, screenFogStrength;
        public float pointLight = 1f; public Color glowTint; public float glowGain = 0.6f;
        // Route tint (PathDepthShading)
        public Color n0 = Color.white, n1, n2, n3, travelled;
        public float nearBrightness = 1.04f, farBrightness = 1f;
        // Sky
        public Color skyTop, skyBottom, skyMid, skyHaze, vignetteCol;
        public Vector2 skyCentre = new Vector2(0.5f, 0.55f);
        public float skyRadius = 0.8f, skyStrength = 1f, skyFalloff = 1.4f, hazeHeight, hazeStrength, skyVignette = 0.3f, rings, ringCount = 9f, skyGrain = 0.012f;
        // Runner, obstacle, props
        public Color pTop, pLeft, pRight; public Color pEmissive = Color.black;
        public Color oTop, oLeft, oRight; public Color oEmissive = Color.black;
        public Color obstacleGlow; public float obstacleGlowIntensity = 1.2f, obstacleGlowRange = 1.6f;
        public Color accent, doorStone, doorGlow, plank, faceHighlight, faceSwitch, arrow;
        // Post
        public float bloomThreshold, bloomIntensity, bloomScatter; public Color bloomTint;
        public float exposure, contrast, saturation; public Color colorFilter = Color.white;
        public Color vignette; public float vignetteIntensity, vignetteSmooth = 0.45f;
        public FilmGrainLookup grainType = FilmGrainLookup.Thin1; public float filmGrain, filmGrainResponse = 0.8f;
        public float ssaoIntensity = 1f;
        // Carved square panels (FlatFaceBlock), 0 = off
        public float tileTop, tileSide, tileInset = 0.13f, tileGroove = 0.035f;
        public Color tileGroove_ = new Color(0.62f, 0.45f, 0.35f), tilePanel = new Color(0.93f, 0.9f, 0.88f), tileLip = new Color(1.15f, 1.08f, 1f);
        // Rock backdrop + ChasmFog (AddRocks). Defaults = what scene 3 was built with.
        public bool rocks;
        public Color sunColor = new Color(1f, 0.83f, 0.6f); public float sunIntensity = 0.4f;
        public Color ambient = new Color(0.09f, 0.12f, 0.12f);
        public float rockVeil = 0.35f, rockHazeStart = 6f, rockHazeMax = 0.85f, fogGlowStrength = 0.9f, fogGlowRadius = 0.85f;
        public float screenFogSolid = -1f, screenFogTop = -1f;   // -1 = keep TrialScene3's
        public int rockFloatingPerCell = -1, rockDebrisPerCell = -1;  // -1 = keep TrialScene3's
        public float rockWallClearance = -1f, rockFloatingClearance = -1f;  // -1 = keep TrialScene3's
        public float rockFutureReach = -1f, rockFutureDrop = -1f;           // -1 = keep TrialScene3's
        public Vector2 rockFloatingHeight = new Vector2(float.NaN, float.NaN); // NaN = keep TrialScene3's
        // Flat-look rocks (PropRockRestyle). restyleRocks false = the pack's lit material + ChasmFog.
        public bool restyleRocks;
        public Color rockTop, rockLeft, rockRight, rockBottom;
        public float rockSideSoftness = 0.25f, rockTopSharpness = 3f, rockFogStart = 4f, rockFogEnd = 40f, rockFogMax = 0.85f;
        public float pathFogFromChasm;            // 1 = the path's fog ends in the ChasmFog backdrop colour
        public float chasmScreenFogStrength = -1f; // -1 = keep TrialScene3's
        // Glowing floor under the track (MonumentShadowFloor + GlowFloor), off = the floor is switched off
        public bool glowFloor;
        public Color floorCenter, floorEdge, floorHaze, floorShadow;
        public float floorGap = 10f, floorInner = 18f, floorFade = 70f, floorOpacity = 0.92f, floorShadowStrength = 0.45f;
        // Rising sparks under the runner, 0 = none
        public float motesRate; public Color moteColor = Color.white;
        // Scene 5 only (all default off): the stacked-cube tower skin on the zone doors, no per-cube portal
        // tile lines, the tile line shade, a crisp floor shadow, and a camera-relative sun (0 = leave the sun).
        public bool towerSkin, disablePortalTiles, startPortal;
        public float portalTileShade = -1f, floorBlur = -1f, sunPitch, sunYawOffset = -135f;
        // Render queue of the glow floor (-1 = the shader's own, which draws over everything below it).
        // 1001 = right after the backdrop, so rocks and track always draw over the floor and its shadows.
        public int floorQueue = -1;
        // Optional rock-placement / height-fog overrides (-1 = keep what the scene has), scene 5.
        public float rockRayMargin = -1f, rockGuardPitch = -1f, rockPathMarginVertical = -1f, chasmFogStart = -1f, chasmFogSolid = -1f;
    }

    // ------------------------------------------------------------------ the three looks

    // 1: Penrose triangle - crimson on flat indigo, printed grain, faces fade to the backdrop low down.
    static Look Penrose() => new Look
    {
        key = "PC1",
        top = H("E9164D"), right = H("C81A4E"), left = H("8A244F"), bottom = H("50284A"),
        shadow = new Color(0.62f, 0.52f, 0.78f), shadowStrength = 0.5f,
        grain = 0.09f, grainScale = 16f,
        fogTop = H("342E52"), fogBottom = H("322A4F"), fogStart = 3f, fogEnd = 26f, fogMax = 0.8f,
        pointLight = 0.8f, glowTint = H("FF6F9A"), glowGain = 0.5f,
        n0 = new Color(0.97f, 0.97f, 0.97f), n1 = new Color(0.95f, 0.94f, 0.96f), n2 = new Color(0.88f, 0.86f, 0.92f), n3 = new Color(0.80f, 0.78f, 0.88f),
        travelled = new Color(0.66f, 0.60f, 0.78f),
        skyTop = H("231C3F"), skyBottom = H("3B2752"), skyMid = H("3A3259"), vignetteCol = H("241E3D"),
        skyCentre = new Vector2(0.6f, 0.22f), skyRadius = 0.9f, skyStrength = 0.8f, skyFalloff = 1.2f, skyVignette = 0.35f, skyGrain = 0.02f,
        pTop = H("FFFFFF"), pLeft = H("E4DFF0"), pRight = H("CCC5DD"),
        oTop = H("3B3263"), oLeft = H("1E1936"), oRight = H("2A2449"),
        obstacleGlow = H("FF8AB0"), obstacleGlowIntensity = 1.0f, obstacleGlowRange = 1.8f,
        accent = H("FFD6E4"), doorStone = H("E9164D"), doorGlow = H("FFE3EC"), plank = new Color(0.42f, 0.36f, 0.78f),
        faceHighlight = new Color(1f, 0.86f, 0.92f, 0.45f), faceSwitch = new Color(1f, 0.95f, 0.98f, 0.95f), arrow = H("FFFFFF"),
        bloomThreshold = 0.92f, bloomIntensity = 0.35f, bloomScatter = 0.6f, bloomTint = H("FFC8DA"),
        exposure = 0f, contrast = 6f, saturation = 4f,
        vignette = H("1A1530"), vignetteIntensity = 0.28f,
        grainType = FilmGrainLookup.Medium2, filmGrain = 0.26f, filmGrainResponse = 0.7f,
        ssaoIntensity = 0.8f,
        // Scene 3's rock backdrop + ChasmFog (same placement), recoloured for the Penrose palette:
        // a dim crimson sun lights the rocks red-magenta, a deep purple ambient shades them, and
        // everything sinks into an indigo -> magenta-purple backdrop. The path fades into it too.
        rocks = true,
        sunColor = new Color(1f, 0.28f, 0.40f), sunIntensity = 0.85f, ambient = new Color(0.13f, 0.08f, 0.22f),
        // Scene 4's placement (scene 3's far-out one leaves this narrow portrait view empty most of the
        // time); belowViewRays still keeps every rock under the sight-lines. Lighter fog than scene 3 so
        // the red-lit rock reads instead of dissolving.
        // Placement and fog exactly as scene 3 (subtle, far out); the houses (Houses object) add the ground.
        rockVeil = 0.35f,
        skyHaze = H("5A2A5E"),
        pathFogFromChasm = 1f, screenFogHeight = 0.3f, screenFogStrength = 0.6f,
    };

    // 2: pale grey stone tower on a dark teal radial backdrop, cyan spirit glow, base dissolving into the dark.
    static Look TealStone() => new Look
    {
        key = "PC2",
        top = H("D6D9CE"), left = H("A0A49D"), right = H("7B8387"), bottom = H("3F5258"),
        shadow = new Color(0.52f, 0.64f, 0.68f), shadowStrength = 0.6f,
        seam = new Color(0.84f, 0.86f, 0.86f), seamStrength = 1f, seamWidth = 0.014f,
        rim = H("F4F6EE"), rimStrength = 0.25f,
        grain = 0.02f,
        fogTop = H("17252A"), fogBottom = H("141D22"), fogStart = 2f, fogEnd = 20f, fogMax = 0.92f,
        pointLight = 1f, glowTint = H("8FEFFF"), glowGain = 0.55f,
        n0 = new Color(0.98f, 0.98f, 0.98f), n1 = new Color(0.96f, 0.97f, 0.97f), n2 = new Color(0.90f, 0.93f, 0.94f), n3 = new Color(0.82f, 0.87f, 0.89f),
        travelled = new Color(0.60f, 0.68f, 0.71f),
        skyTop = H("141F21"), skyBottom = H("121B1F"), skyMid = H("3A4B51"), vignetteCol = H("0A1112"),
        skyCentre = new Vector2(0.5f, 0.6f), skyRadius = 0.85f, skyStrength = 1f, skyFalloff = 1.25f, skyVignette = 0.4f, rings = 0.035f, ringCount = 7f, skyGrain = 0.01f,
        pTop = H("FAFCFB"), pLeft = H("E3E9E8"), pRight = H("C3CDCF"), pEmissive = new Color(0.02f, 0.06f, 0.07f),
        oTop = H("3C474C"), oLeft = H("1B2226"), oRight = H("262F34"),
        obstacleGlow = H("7FE8FF"), obstacleGlowIntensity = 1.0f, obstacleGlowRange = 1.8f,
        accent = H("7FE8FF"), doorStone = H("C9CDC4"), doorGlow = H("BFF6FF"), plank = new Color(0.55f, 0.72f, 0.80f),
        faceHighlight = new Color(0.50f, 0.92f, 1f, 0.45f), faceSwitch = new Color(0.85f, 1f, 1f, 0.95f), arrow = H("9FF0FF"),
        bloomThreshold = 0.9f, bloomIntensity = 0.55f, bloomScatter = 0.7f, bloomTint = H("A8F2FF"),
        exposure = 0f, contrast = 8f, saturation = -6f,
        vignette = H("050A0B"), vignetteIntensity = 0.3f,
        grainType = FilmGrainLookup.Thin1, filmGrain = 0.12f,
        ssaoIntensity = 1.1f,
    };

    // 3: dark slate stone, glowing yellow totem, warm orange-brown haze swallowing the base.
    static Look DarkStone() => new Look
    {
        key = "PC3",
        top = H("858B84"), left = H("50554F"), right = H("323B38"), bottom = H("1B2324"),
        shadow = new Color(0.50f, 0.55f, 0.55f), shadowStrength = 0.6f,
        seam = new Color(0.78f, 0.80f, 0.78f), seamStrength = 1f, seamWidth = 0.016f,
        grain = 0.035f,
        fogTop = H("03191F"), fogBottom = H("3A3329"), fogStart = 2f, fogEnd = 20f, fogMax = 0.95f,
        screenFogHeight = 0.32f, screenFogStrength = 0.75f,
        pointLight = 1.6f, glowTint = H("FFD35A"), glowGain = 0.65f,
        n0 = new Color(1f, 0.99f, 0.96f), n1 = new Color(0.97f, 0.96f, 0.94f), n2 = new Color(0.88f, 0.90f, 0.90f), n3 = new Color(0.78f, 0.84f, 0.86f),
        travelled = new Color(0.60f, 0.66f, 0.68f),
        skyTop = H("011A21"), skyBottom = H("2C2A24"), skyMid = H("0E2226"), skyHaze = H("3C352B"), vignetteCol = H("000C10"),
        skyCentre = new Vector2(0.62f, 0.22f), skyRadius = 0.7f, skyStrength = 0.6f, skyFalloff = 1.5f, hazeHeight = 0.45f, hazeStrength = 0.9f, skyVignette = 0.3f, skyGrain = 0.01f,
        pTop = H("FFFFFF"), pLeft = H("EDE7DA"), pRight = H("CFC6B4"),
        oTop = H("E8EC72"), oLeft = H("F1D051"), oRight = H("D0B569"), oEmissive = new Color(0.16f, 0.12f, 0.02f),
        obstacleGlow = H("FFC94A"), obstacleGlowIntensity = 2.2f, obstacleGlowRange = 3.2f,
        accent = H("FFD35A"), doorStone = H("4A504C"), doorGlow = H("FFD27A"), plank = new Color(0.95f, 0.45f, 0.30f),
        faceHighlight = new Color(1f, 0.86f, 0.40f, 0.45f), faceSwitch = new Color(1f, 0.96f, 0.80f, 0.95f), arrow = H("FFC94A"),
        bloomThreshold = 0.8f, bloomIntensity = 0.9f, bloomScatter = 0.75f, bloomTint = H("FFD58A"),
        exposure = 0.1f, contrast = 10f, saturation = 0f,
        vignette = H("000608"), vignetteIntensity = 0.32f,
        grainType = FilmGrainLookup.Thin1, filmGrain = 0.1f,
        ssaoIntensity = 1.2f,
        rocks = true,
    };

    // 4: orange Monument Valley canyon over a glowing yellow sea. The light comes from BELOW: tops are
    // the darker tan, the sides bright orange, undersides glow, and whatever sinks down dissolves into
    // bright yellow (not into darkness as in 1-3). The walkway in the reference has a carved square
    // panel per block - that is the path's top and side faces here.
    static Look Canyon() => new Look
    {
        key = "PC4",
        top = H("A36C3A"), left = H("D07A36"), right = H("F59A40"), bottom = H("FFB444"),
        shadow = new Color(0.80f, 0.60f, 0.48f), shadowStrength = 0.35f,
        seam = new Color(0.86f, 0.76f, 0.68f), seamStrength = 0.45f, seamWidth = 0.012f,
        tileTop = 1f, tileSide = 0.8f, tileInset = 0.12f, tileGroove = 0.04f,
        tileGroove_ = new Color(0.60f, 0.40f, 0.28f), tilePanel = new Color(0.94f, 0.90f, 0.86f), tileLip = new Color(1.14f, 1.07f, 0.98f),
        grain = 0.03f,
        fogTop = H("F2A02A"), fogBottom = H("FFC21A"), fogStart = 3f, fogEnd = 26f, fogMax = 0.6f,
        screenFogHeight = 0.25f, screenFogStrength = 0.35f,
        pointLight = 1.2f, glowTint = H("FFD24A"), glowGain = 0.5f,
        n0 = new Color(1f, 1f, 1f), n1 = new Color(0.98f, 0.97f, 0.95f), n2 = new Color(0.94f, 0.92f, 0.90f), n3 = new Color(0.88f, 0.86f, 0.84f),
        travelled = new Color(0.82f, 0.70f, 0.60f),
        skyTop = H("4A2A12"), skyBottom = H("C8641A"), skyMid = H("FFD000"), skyHaze = H("FFD000"), vignetteCol = H("3E2410"),
        skyCentre = new Vector2(0.5f, 0.28f), skyRadius = 0.6f, skyStrength = 0.9f, hazeHeight = 0.4f, hazeStrength = 0.8f,
        pTop = H("FFFFFF"), pLeft = H("FFF3E2"), pRight = H("F0D9BC"),
        oTop = H("6A2A14"), oLeft = H("3E170A"), oRight = H("52200F"),
        obstacleGlow = H("FF7A2A"), obstacleGlowIntensity = 1.2f, obstacleGlowRange = 2f,
        accent = H("FFF0B8"), doorStone = H("D88A40"), doorGlow = H("FFF6C8"), plank = new Color(0.55f, 0.32f, 0.22f),
        faceHighlight = new Color(1f, 0.97f, 0.85f, 0.45f), faceSwitch = new Color(1f, 1f, 0.95f, 0.95f), arrow = H("FFFFFF"),
        bloomThreshold = 0.9f, bloomIntensity = 0.45f, bloomScatter = 0.7f, bloomTint = H("FFD27A"),
        exposure = 0f, contrast = 6f, saturation = 6f,
        vignette = H("3A200C"), vignetteIntensity = 0.28f,
        grainType = FilmGrainLookup.Thin1, filmGrain = 0.08f,
        rocks = true,
        sunColor = new Color(1f, 0.64f, 0.30f), sunIntensity = 1.6f, ambient = new Color(0.72f, 0.38f, 0.14f),
        rockVeil = 0.03f, rockHazeStart = 14f, rockHazeMax = 0.3f, fogGlowStrength = 0.85f, fogGlowRadius = 0.55f,
        chasmScreenFogStrength = 0.3f, pathFogFromChasm = 1f, rockFogMax = 0.35f,
        rockFloatingPerCell = 2, rockDebrisPerCell = 3,
        // Close in and under the track (this portrait camera is only ~25 units wide); belowViewRays
        // still keeps every rock under the sight-lines, so none can cover the route or pop in view.
        rockWallClearance = 8f, rockFloatingClearance = 5f, rockFloatingHeight = new Vector2(-30f, -6f),
        rockFutureReach = 80f, rockFutureDrop = 30f,
        restyleRocks = true,
        rockTop = H("C8803F"), rockLeft = H("EE8A36"), rockRight = H("8A4C1E"), rockBottom = H("FFB040"),
        glowFloor = true,
        floorCenter = H("FFE030"), floorEdge = H("E88A20"), floorHaze = H("FFF3A0"), floorShadow = new Color(0.9f, 0.5f, 0.15f),
        floorGap = 24f, floorInner = 8f, floorFade = 40f, floorOpacity = 0.65f, floorShadowStrength = 0.4f,
        motesRate = 30f, moteColor = new Color(1f, 0.82f, 0.38f),
    };

    // 5: the red Monument Valley tower: pale pink stepping stones on a flat red world with a lighter glow
    // low in the middle, a stacked blue/cyan/mint tower with a coral-framed archway on a pink tiled slab,
    // dark-red Polybox crags sinking into the red, and long sharp shadows falling toward the lower left.
    // Measured from Downloads\download (7).jpg.
    static Look RedTower() => new Look
    {
        key = "PC5",
        top = H("FDCBCD"), left = H("EE8A8E"), right = H("F77884"), bottom = H("D45A66"),
        shadow = new Color(0.80f, 0.52f, 0.58f), shadowStrength = 0.45f,
        seam = H("E58A92"), seamStrength = 0.5f, seamWidth = 0.014f,
        rim = H("FFE8EA"), rimStrength = 0.22f,
        grain = 0.02f,
        fogTop = H("D0383B"), fogBottom = H("C72A2E"), fogStart = 3f, fogEnd = 26f, fogMax = 0.85f,
        screenFogHeight = 0.28f, screenFogStrength = 0.4f,
        pointLight = 1f, glowTint = H("FFB4BC"), glowGain = 0.5f,
        n0 = new Color(1f, 0.97f, 0.97f), n1 = new Color(0.98f, 0.93f, 0.94f), n2 = new Color(0.94f, 0.88f, 0.90f), n3 = new Color(0.90f, 0.82f, 0.85f),
        travelled = new Color(0.86f, 0.70f, 0.74f),
        skyTop = H("C42B2E"), skyBottom = H("C22F30"), skyMid = H("D24345"), skyHaze = H("E65356"), vignetteCol = H("BF1F25"),
        skyCentre = new Vector2(0.55f, 0.40f), skyRadius = 0.6f, skyStrength = 0.9f, skyFalloff = 1.3f, skyVignette = 0.18f, skyGrain = 0.006f,
        pTop = H("2E3C78"), pLeft = H("212C5E"), pRight = H("2A3670"),
        oTop = H("5F6666"), oLeft = H("373D42"), oRight = H("474E52"),
        obstacleGlow = H("FFB0B8"), obstacleGlowIntensity = 0.6f, obstacleGlowRange = 1.6f,
        accent = H("7FE8E8"), doorStone = Color.white, doorGlow = H("FFD6E8"), plank = H("2C7D6C"),
        faceHighlight = new Color(0.50f, 0.93f, 0.92f, 0.45f), faceSwitch = new Color(0.9f, 1f, 1f, 0.95f), arrow = H("1F6749"),
        bloomThreshold = 0.92f, bloomIntensity = 0.25f, bloomScatter = 0.65f, bloomTint = H("FFC8CC"),
        exposure = 0f, contrast = 4f, saturation = 5f,
        vignette = H("A81B20"), vignetteIntensity = 0.2f,
        grainType = FilmGrainLookup.Thin1, filmGrain = 0.04f,
        ssaoIntensity = 0.9f,
        rocks = true,
        sunColor = new Color(1f, 0.86f, 0.86f), sunIntensity = 1.0f, ambient = new Color(0.42f, 0.2f, 0.22f),
        rockVeil = 0.03f, rockHazeStart = 14f, rockHazeMax = 0.45f, fogGlowStrength = 0.85f, fogGlowRadius = 0.55f,
        chasmScreenFogStrength = 0.3f, pathFogFromChasm = 1f, rockFogMax = 0.4f,
        rockFloatingPerCell = 2, rockDebrisPerCell = 3,
        rockWallClearance = 8f, rockFloatingClearance = 5f, rockFloatingHeight = new Vector2(-24f, -3f),
        rockFutureReach = 60f, rockFutureDrop = 18f,
        // More rock stays in view when the route climbs or falls: a smaller margin under the sight-lines
        // (the camera holds 27.9 deg, the guard 22), a shorter look-ahead, and fog that takes the deep rock later.
        rockRayMargin = 2f, rockGuardPitch = 22f, rockPathMarginVertical = 6f, chasmFogStart = 12f, chasmFogSolid = 90f,
        restyleRocks = true,
        rockTop = H("DC4A52"), rockLeft = H("B3232C"), rockRight = H("931A23"), rockBottom = H("6E1019"),
        glowFloor = true,
        // The floor is a ground the track casts its long sharp shadow on (like the reference), drawn right
        // after the backdrop so the rocks below it are never veiled by it.
        floorCenter = H("E65356"), floorEdge = H("C72427"), floorHaze = H("F06368"), floorShadow = new Color(0.80f, 0.42f, 0.45f),
        floorGap = 7f, floorInner = 10f, floorFade = 45f, floorOpacity = 0.8f, floorShadowStrength = 0.85f, floorBlur = 0.35f, floorQueue = 1001,
        motesRate = 14f, moteColor = H("FFB36B"),
        towerSkin = true, disablePortalTiles = true, portalTileShade = 0.86f, startPortal = true,
        sunPitch = 38f, sunYawOffset = -135f,
    };

    // 6: Jade Moon Garden (no reference image - designed as checkpoint 4 of MainScene prop camera 3.5): ivory-marble beams
    // with coral/rose sides and a gilded rim, on a deep emerald night with a mint moon glow; dark-jade runner, ink-plum
    // obstacles with a gold glow, gold planks, faceted jade crags, a mint glow pool and firefly motes. Coral is green's
    // complement, so the path reads against everything behind it; the path sinks into the emerald like PC1 sinks into indigo.
    static Look JadeGarden() => new Look
    {
        key = "PC6",
        top = H("FFF3E4"), right = H("FF8A70"), left = H("E2566A"), bottom = H("8E2F55"),
        shadow = H("7FB8A6"), shadowStrength = 0.4f,
        seamStrength = 0f,
        rim = H("FFD27A"), rimStrength = 0.6f,
        grain = 0.03f, grainScale = 14f,
        fogTop = H("0E4A3E"), fogBottom = H("0B3F30"), fogStart = 3f, fogEnd = 26f, fogMax = 0.62f,
        screenFogHeight = 0.28f, screenFogStrength = 0.35f,
        pointLight = 1f, glowTint = H("FFC2A8"), glowGain = 0.5f,
        n0 = new Color(1f, 1f, 1f), n1 = new Color(0.98f, 0.98f, 0.97f), n2 = new Color(0.93f, 0.95f, 0.94f), n3 = new Color(0.86f, 0.90f, 0.88f),
        travelled = new Color(0.62f, 0.75f, 0.70f),
        skyTop = H("021A14"), skyBottom = H("0A3F2F"), skyMid = H("7DFFC8"), skyHaze = H("1E8A62"), vignetteCol = H("021A12"),
        skyCentre = new Vector2(0.5f, 0.78f), skyRadius = 0.5f, skyStrength = 0.95f, skyFalloff = 1.6f, hazeHeight = 0.3f, hazeStrength = 0.5f,
        skyVignette = 0.35f, rings = 0.04f, ringCount = 7f, skyGrain = 0.01f,
        pTop = H("1B7A5E"), pLeft = H("0E4C3B"), pRight = H("126049"),
        oTop = H("4A2A5E"), oLeft = H("24122F"), oRight = H("361C46"),
        obstacleGlow = H("FFC861"), obstacleGlowIntensity = 1.1f, obstacleGlowRange = 1.8f,
        accent = H("7CF2C8"), doorStone = H("F3E3CF"), doorGlow = H("CFFFE9"), plank = H("D9A441"),
        faceHighlight = new Color(0.62f, 1f, 0.86f, 0.45f), faceSwitch = new Color(0.92f, 1f, 0.97f, 0.95f), arrow = H("5FFFC0"),
        bloomThreshold = 0.9f, bloomIntensity = 0.4f, bloomScatter = 0.65f, bloomTint = H("BFFFE6"),
        exposure = 0f, contrast = 8f, saturation = 10f,
        vignette = H("021A12"), vignetteIntensity = 0.3f,
        grainType = FilmGrainLookup.Thin1, filmGrain = 0.06f,
        ssaoIntensity = 0.8f,
        rocks = true,
        sunColor = H("DFFFF0"), sunIntensity = 1.0f, ambient = new Color(0.04f, 0.18f, 0.14f),
        rockVeil = 0.03f, rockHazeStart = 14f, rockHazeMax = 0.35f, fogGlowStrength = 0.75f, fogGlowRadius = 0.5f,
        chasmScreenFogStrength = 0.35f, pathFogFromChasm = 1f, rockFogMax = 0.4f,
        rockFloatingPerCell = 2, rockDebrisPerCell = 3,
        rockWallClearance = 8f, rockFloatingClearance = 5f, rockFloatingHeight = new Vector2(-30f, -6f),
        rockFutureReach = 80f, rockFutureDrop = 30f,
        restyleRocks = true,
        rockTop = H("3BA67F"), rockLeft = H("1F7A5A"), rockRight = H("125440"), rockBottom = H("082B21"),
        glowFloor = true,
        floorCenter = H("3FE0A0"), floorEdge = H("0E4A36"), floorHaze = H("8CFFD0"), floorShadow = new Color(0.05f, 0.25f, 0.18f),
        floorGap = 24f, floorInner = 8f, floorFade = 40f, floorOpacity = 0.45f, floorShadowStrength = 0.4f,
        motesRate = 22f, moteColor = H("E8FF9A"),
    };

    public static Look ForScene(int n) => n == 1 ? Penrose() : n == 2 ? TealStone() : n == 3 ? DarkStone() : n == 5 ? RedTower() : n == 6 ? JadeGarden() : Canyon();

    [MenuItem("Tools/Prop Camera Looks/Apply Look To Open Scene")]
    static void MenuApply()
    {
        string name = EditorSceneManager.GetActiveScene().name;
        int n = name.EndsWith(" 1") ? 1 : name.EndsWith(" 2") ? 2 : name.EndsWith(" 3") ? 3 : name.EndsWith(" 4") ? 4 : name.EndsWith(" 5") ? 5 : name.EndsWith(" 6") ? 6 : 0;
        if (n == 0 || !name.StartsWith("MainScene prop camera")) { Debug.LogWarning("[PropLooks] open one of the 'MainScene prop camera 1-6' scenes first."); return; }
        Debug.Log(Apply(n));
        Look L = ForScene(n);
        if (L.rocks) Debug.Log(AddRocks(L));
    }

    // ------------------------------------------------------------------ materials

    static Material Mat(string name, string shader)
    {
        string path = Root + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find(shader)) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader.name != shader) m.shader = Shader.Find(shader);
        return m;
    }

    static void Faces(Material m, Color top, Color left, Color right, Color bottom)
    {
        m.SetColor("_TopColor", top); m.SetColor("_LeftColor", left);
        m.SetColor("_RightColor", right); m.SetColor("_BottomColor", bottom);
        m.SetColor("_BaseColor", Color.white);
    }

    static Material BlockMaterial(Look L, string name)
    {
        var m = Mat(name, "PropLooks/FlatFaceBlock");
        Faces(m, L.top, L.left, L.right, L.bottom);
        m.SetColor("_ShadowColor", L.shadow); m.SetFloat("_ShadowStrength", L.shadowStrength);
        m.SetColor("_SeamColor", L.seam); m.SetFloat("_SeamStrength", L.seamStrength); m.SetFloat("_SeamWidth", L.seamWidth);
        m.SetColor("_EdgeColor", L.rim); m.SetFloat("_EdgeStrength", L.rimStrength); m.SetFloat("_EdgeWidth", 0.025f);
        m.SetFloat("_GrainStrength", L.grain); m.SetFloat("_GrainScale", L.grainScale);
        m.SetColor("_FogTopColor", L.fogTop); m.SetColor("_FogBottomColor", L.fogBottom);
        m.SetFloat("_FogStart", L.fogStart); m.SetFloat("_FogEnd", L.fogEnd); m.SetFloat("_FogMax", L.fogMax);
        m.SetFloat("_ScreenFogHeight", L.screenFogHeight); m.SetFloat("_ScreenFogStrength", L.screenFogStrength);
        m.SetFloat("_PointLightStrength", L.pointLight); m.SetColor("_GlowTint", L.glowTint); m.SetFloat("_GlowGain", L.glowGain);
        m.SetFloat("_GlowBase", 0.55f);
        m.SetColor("_EmissiveColor", Color.black);
        m.SetFloat("_FogColorFromChasm", L.pathFogFromChasm); m.SetFloat("_ChasmFogMix", 0f);
        m.SetFloat("_TileTop", L.tileTop); m.SetFloat("_TileSide", L.tileSide);
        m.SetFloat("_TileInset", L.tileInset); m.SetFloat("_TileGroove", L.tileGroove);
        m.SetColor("_TileGrooveColor", L.tileGroove_); m.SetColor("_TilePanelColor", L.tilePanel); m.SetColor("_TileLipColor", L.tileLip);
        EditorUtility.SetDirty(m);
        return m;
    }

    // A small solid prop (runner, obstacle) in the same flat style: no seams, no fog, no grain.
    static Material PropMaterial(Look L, string name, Color top, Color left, Color right, Color emissive)
    {
        var m = BlockMaterial(L, name);
        Faces(m, top, left, right, Color.Lerp(right, Color.black, 0.3f));
        m.SetFloat("_SeamStrength", 0f); m.SetFloat("_EdgeStrength", 0f); m.SetFloat("_GrainStrength", 0f);
        m.SetFloat("_TileTop", 0f); m.SetFloat("_TileSide", 0f);
        m.SetFloat("_FogMax", 0f); m.SetFloat("_ScreenFogStrength", 0f);
        m.SetFloat("_ShadowStrength", 0.3f);
        m.SetFloat("_PointLightStrength", 0.3f);
        m.SetColor("_EmissiveColor", emissive);
        m.SetFloat("_GlowGain", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material SkyMaterial(Look L)
    {
        var m = Mat("M_" + L.key + "_Sky", "PropLooks/RadialSky");
        m.SetColor("_TopColor", L.skyTop); m.SetColor("_BottomColor", L.skyBottom); m.SetColor("_MidColor", L.skyMid);
        m.SetVector("_GlowCenter", new Vector4(L.skyCentre.x, L.skyCentre.y, 0, 0));
        m.SetFloat("_GlowRadius", L.skyRadius); m.SetFloat("_GlowStrength", L.skyStrength); m.SetFloat("_GlowFalloff", L.skyFalloff);
        m.SetColor("_GlowColor", L.skyHaze); m.SetFloat("_HazeHeight", L.hazeHeight); m.SetFloat("_HazeStrength", L.hazeStrength);
        m.SetColor("_VignetteColor", L.vignetteCol); m.SetFloat("_Vignette", L.skyVignette);
        m.SetFloat("_Rings", L.rings); m.SetFloat("_RingCount", L.ringCount); m.SetFloat("_Grain", L.skyGrain);
        m.SetColor("_StarColor", L.accent);
        EditorUtility.SetDirty(m);
        return m;
    }

    static VolumeProfile Profile(Look L)
    {
        string path = Root + "/Settings/" + L.key + "_Volume.asset";
        var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (p == null) { p = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(p, path); }

        T Get<T>() where T : VolumeComponent
        {
            if (!p.TryGet(out T c)) { c = p.Add<T>(true); c.name = typeof(T).Name; AssetDatabase.AddObjectToAsset(c, p); }
            c.active = true;
            return c;
        }

        var bloom = Get<Bloom>();
        bloom.threshold.Override(L.bloomThreshold); bloom.intensity.Override(L.bloomIntensity);
        bloom.scatter.Override(L.bloomScatter); bloom.tint.Override(L.bloomTint); bloom.highQualityFiltering.Override(true);

        var ca = Get<ColorAdjustments>();
        ca.postExposure.Override(L.exposure); ca.contrast.Override(L.contrast); ca.saturation.Override(L.saturation);
        ca.colorFilter.Override(L.colorFilter);

        var vg = Get<Vignette>();
        vg.color.Override(L.vignette); vg.intensity.Override(L.vignetteIntensity); vg.smoothness.Override(L.vignetteSmooth);
        vg.center.Override(new Vector2(0.5f, 0.5f));

        var fg = Get<FilmGrain>();
        fg.type.Override(L.grainType); fg.intensity.Override(L.filmGrain); fg.response.Override(L.filmGrainResponse);

        // The SSAO strength itself is on the renderer feature (shared by the three scenes); this
        // scales it per scene through the look material instead (see Apply).
        foreach (var c in p.components) EditorUtility.SetDirty(c);
        EditorUtility.SetDirty(p);
        return p;
    }

    // ------------------------------------------------------------------ scene 3: rocks + warm haze

    /// <summary>
    /// Copies the Polybox rock backdrop (PathBackgroundGenerator) and its fog (ChasmFog) from
    /// TrialScene3 into the open scene - TrialScene3 is opened additively, read and closed WITHOUT
    /// saving - then recolours the fog to the dark-teal-over-warm-brown of the reference. The
    /// MonumentSkyBackdrop is switched off: ChasmFog draws the backdrop, and the rocks fade into
    /// exactly that backdrop colour.
    /// </summary>
    public static string AddRocks(Look L)
    {
        var log = new System.Text.StringBuilder();
        var target = EditorSceneManager.GetActiveScene();
        var src = EditorSceneManager.OpenScene("Assets/TrialScene3.unity", OpenSceneMode.Additive);
        try
        {
            foreach (var root in src.GetRootGameObjects())
            {
                foreach (var comp in new MonoBehaviour[] { root.GetComponentInChildren<ChasmFog>(true), root.GetComponentInChildren<PathBackgroundGenerator>(true) })
                {
                    if (comp == null) continue;
                    string goName = comp.GetType().Name == "ChasmFog" ? "ChasmFog" : "PathBackground";
                    GameObject dst = null;
                    foreach (var r in target.GetRootGameObjects()) if (r.name == goName) dst = r;
                    if (dst == null)
                    {
                        dst = new GameObject(goName);
                        SceneManager_Move(dst, target);
                        Undo.RegisterCreatedObjectUndo(dst, "Prop look rocks");
                    }
                    var copy = dst.GetComponent(comp.GetType()) as MonoBehaviour;
                    if (copy == null) copy = dst.AddComponent(comp.GetType()) as MonoBehaviour;
                    EditorUtility.CopySerialized(comp, copy);
                    log.AppendLine("copied " + comp.GetType().Name + " from " + root.name);
                }
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(src, true);
        }

        // Scene references must be this scene's own (they are all found automatically when empty).
        var fog = Object.FindFirstObjectByType<ChasmFog>(FindObjectsInactive.Include);
        var bg = Object.FindFirstObjectByType<PathBackgroundGenerator>(FindObjectsInactive.Include);
        if (fog != null)
        {
            fog.cam = null; fog.generator = null; fog.player = null;
            fog.enableFog = true;
            fog.topColor = L.skyTop;
            fog.bottomColor = L.skyBottom;
            fog.glowColor = L.skyHaze;
            fog.glowStrength = L.fogGlowStrength;
            fog.glowCentre = L.skyCentre;
            fog.glowRadius = L.fogGlowRadius;
            fog.veil = L.rockVeil;
            fog.hazeStart = L.rockHazeStart;
            fog.hazeMax = L.rockHazeMax;
            if (L.screenFogSolid >= 0f) fog.screenFogSolid = L.screenFogSolid;
            if (L.screenFogTop >= 0f) fog.screenFogTop = L.screenFogTop;
            if (L.chasmScreenFogStrength >= 0f) fog.screenFogStrength = L.chasmScreenFogStrength;
            if (L.chasmFogStart >= 0f) fog.fogStart = L.chasmFogStart;
            if (L.chasmFogSolid >= 0f) fog.fogSolid = L.chasmFogSolid;
            EditorUtility.SetDirty(fog);
        }
        if (bg != null)
        {
            bg.generator = null; bg.player = null; bg.cam = null;
            // Keep rock clear of this scene's own houses (scene 1's Houses object), if it has any.
            bg.houses = Object.FindFirstObjectByType<NeonWindowDecorator>(FindObjectsInactive.Include);
            bg.enableBackground = true;
            // Smaller crags than TrialScene3's, and kept under this camera's flatter pitch (27.9 deg).
            bg.floatingWidth = new Vector2(8f, 24f);
            bg.guardPitch = 24f;
            if (L.rockFloatingPerCell >= 0) bg.floatingPerCell = L.rockFloatingPerCell;
            if (L.rockDebrisPerCell >= 0) bg.debrisPerCell = L.rockDebrisPerCell;
            if (L.rockWallClearance >= 0f) bg.wallClearance = L.rockWallClearance;
            if (L.rockFloatingClearance >= 0f) bg.floatingClearance = L.rockFloatingClearance;
            if (!float.IsNaN(L.rockFloatingHeight.x)) bg.floatingHeightRange = L.rockFloatingHeight;
            if (L.rockFutureReach >= 0f) bg.futureReach = L.rockFutureReach;
            if (L.rockFutureDrop >= 0f) bg.futureDrop = L.rockFutureDrop;
            if (L.rockRayMargin >= 0f) bg.rayMargin = L.rockRayMargin;
            if (L.rockGuardPitch >= 0f) bg.guardPitch = L.rockGuardPitch;
            if (L.rockPathMarginVertical >= 0f) bg.pathMarginVertical = L.rockPathMarginVertical;

            // Flat-look rocks: every facet a crisp camera-relative tone, sinking into the track's fog.
            var restyle = bg.GetComponent<PropRockRestyle>();
            if (L.restyleRocks)
            {
                var rm = BlockMaterial(L, "M_" + L.key + "_Rock");
                Faces(rm, L.rockTop, L.rockLeft, L.rockRight, L.rockBottom);
                rm.SetFloat("_SideSoftness", L.rockSideSoftness); rm.SetFloat("_TopSharpness", L.rockTopSharpness);
                rm.SetFloat("_SeamStrength", 0f); rm.SetFloat("_EdgeStrength", 0f);
                rm.SetFloat("_TileTop", 0f); rm.SetFloat("_TileSide", 0f);
                rm.SetFloat("_FogStart", L.rockFogStart); rm.SetFloat("_FogEnd", L.rockFogEnd); rm.SetFloat("_FogMax", L.rockFogMax);
                rm.SetFloat("_ShadowStrength", 0.3f); rm.SetFloat("_GlowGain", 0f); rm.SetFloat("_PointLightStrength", 0.4f);
                rm.SetFloat("_GrainScale", 6f);
                rm.SetFloat("_FogColorFromChasm", 1f); rm.SetFloat("_ChasmFogMix", 1f);
                rm.SetFloat("_ScreenFogStrength", 0f);
                EditorUtility.SetDirty(rm);
                if (restyle == null) restyle = bg.gameObject.AddComponent<PropRockRestyle>();
                restyle.source = bg;
                restyle.rockMaterial = rm;
                restyle.enabled = true;
                EditorUtility.SetDirty(restyle);
                log.AppendLine("rocks restyled with " + rm.name);
            }
            else if (restyle != null) restyle.enabled = false;
            EditorUtility.SetDirty(bg);
        }

        // The rocks are the only lit (Polybox) surfaces: a dim warm sun and a dark teal ambient give
        // them the reference's warm-brown lit faces and near-black shaded ones. The look shader does
        // not use the sun's colour (only its shadows), so the path is unaffected.
        var sun = RenderSettings.sun;
        if (sun == null) foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional && l.isActiveAndEnabled) sun = l;
        if (sun != null)
        {
            Undo.RecordObject(sun, "Prop look rocks");
            sun.color = L.sunColor;
            sun.intensity = L.sunIntensity;
            EditorUtility.SetDirty(sun);
        }
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = L.ambient;
        var backdrop = Object.FindFirstObjectByType<MonumentSkyBackdrop>(FindObjectsInactive.Include);
        if (backdrop != null) { backdrop.enabled = false; EditorUtility.SetDirty(backdrop); }
        return log.ToString();
    }

    static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene)
    {
        if (go.scene != scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
    }

    // The totem at the foot of the start structure glows: brighter yellow copies of its materials and
    // two warm point lights that bleed onto the grey stone around it (the look shader picks them up).
    public static string GlowTotem(Look L)
    {
        var log = new System.Text.StringBuilder();
        int swapped = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || !m.name.StartsWith("M_Totem_")) continue;
                string path = Root + "/Materials/" + L.key + "_" + m.name + ".mat";
                var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (copy == null) { copy = new Material(m); AssetDatabase.CreateAsset(copy, path); }
                else copy.CopyPropertiesFromMaterial(m);
                // Push the yellows toward the lit reference totem (#F1D051 faces, #E8EC72 top).
                foreach (string prop in new[] { "_BaseColor", "_Color", "_FaceTintX", "_FaceTintZ", "_FaceTintTop" })
                {
                    if (!copy.HasProperty(prop)) continue;
                    Color c = copy.GetColor(prop);
                    Color.RGBToHSV(c, out float h, out float s, out float v);
                    if (h > 0.08f && h < 0.2f && s > 0.3f) c = Color.HSVToRGB(h, Mathf.Min(1f, s * 1.05f), Mathf.Min(1f, v * 1.18f));
                    else if (prop.StartsWith("_FaceTint")) c = new Color(Mathf.Min(1f, c.r * 1.12f), Mathf.Min(1f, c.g * 1.12f), Mathf.Min(1f, c.b * 1.12f), c.a);
                    copy.SetColor(prop, c);
                }
                if (copy.HasProperty("_Brightness")) copy.SetFloat("_Brightness", Mathf.Min(1.5f, m.GetFloat("_Brightness") * 1.12f));
                EditorUtility.SetDirty(copy);
                mats[i] = copy;
                changed = true;
            }
            if (changed) { Undo.RecordObject(r, "Prop look totem"); r.sharedMaterials = mats; swapped++; }
        }
        log.AppendLine("totem renderers: " + swapped);

        // No point lights here: the start structure (and the totem) is hidden once the game starts, so
        // a light at the totem would leave a warm patch on the track with no visible source. In play the
        // yellow glow comes from the obstacles (emissive + their own point light) and the boost legs.
        var old = GameObject.Find("TotemGlow");
        if (old != null) { Object.DestroyImmediate(old); log.AppendLine("removed TotemGlow lights"); }
        return log.ToString();
    }

    // ------------------------------------------------------------------ apply

    // Sparks drifting up out of the glow (the specks in the canyon reference): a world-space particle
    // system PropLookController keeps under the runner. No colliders, fades in and out on its own.
    static Transform Motes(Look L, Transform parent)
    {
        var t = parent.Find("Motes");
        GameObject go = t != null ? t.gameObject : new GameObject("Motes");
        go.transform.SetParent(parent, false);
        var ps = go.GetComponent<ParticleSystem>();
        if (ps == null) ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(L.moteColor, Color.Lerp(L.moteColor, Color.white, 0.5f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;

        var em = ps.emission;
        em.rateOverTime = L.motesRate;

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(70f, 2f, 70f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        vel.y = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.3f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var mat = Mat("M_" + L.key + "_Mote", "PropLooks/Mote");
        mat.SetColor("_Color", new Color(1.2f, 1.05f, 0.8f, 1f));
        EditorUtility.SetDirty(mat);
        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = mat;
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.shadowCastingMode = ShadowCastingMode.Off;
        pr.receiveShadows = false;
        EditorUtility.SetDirty(ps);
        return go.transform;
    }

    static void SetColors(Object target, params (string field, Color value)[] values)
    {
        var so = new SerializedObject(target);
        foreach (var (field, value) in values)
        {
            var p = so.FindProperty(field);
            if (p != null) p.colorValue = value;
        }
        so.ApplyModifiedProperties();
    }

    public static string Apply(int n)
    {
        Look L = ForScene(n);
        var log = new System.Text.StringBuilder("[PropLooks] look " + L.key + " -> " + EditorSceneManager.GetActiveScene().path + "\n");
        int rendererIndex = PropLooksRendererSetup.EnsureRenderer();

        Material block = BlockMaterial(L, "M_" + L.key + "_Block");
        Material fade = BlockMaterial(L, "M_" + L.key + "_Block_Fade");
        Material sky = SkyMaterial(L);
        Material player = PropMaterial(L, "M_" + L.key + "_Player", L.pTop, L.pLeft, L.pRight, L.pEmissive);
        Material obstacle = PropMaterial(L, "M_" + L.key + "_Obstacle", L.oTop, L.oLeft, L.oRight, L.oEmissive);
        VolumeProfile profile = Profile(L);

        // Start structure + firstCube (the prefab every path cube is cloned from).
        var gen = Object.FindFirstObjectByType<DynamicPathGenerator>();
        int swapped = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && (mats[i].name == "M_MonumentBlock" || mats[i].name.StartsWith("M_PC") && mats[i].name.EndsWith("_Block")))
                { mats[i] = block; changed = true; }
            if (changed) { Undo.RecordObject(r, "Prop look"); r.sharedMaterials = mats; swapped++; }
        }
        log.AppendLine("block renderers swapped: " + swapped);

        var pds = gen.GetComponent<PathDepthShading>();
        Undo.RecordObject(pds, "Prop look");
        pds.fadeMaterial = fade;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(L.n0, 0f), new GradientColorKey(L.n1, 0.3f), new GradientColorKey(L.n2, 0.65f), new GradientColorKey(L.n3, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        pds.aheadGradient = g;
        pds.travelledColor = L.travelled;
        pds.nearBrightness = L.nearBrightness;
        pds.farBrightness = L.farBrightness;
        pds.boostBrightness = 1f;
        EditorUtility.SetDirty(pds);

        Undo.RecordObject(gen, "Prop look");
        gen.obstaclePyramidMaterial = obstacle;
        gen.obstacleGlowColor = L.obstacleGlow;
        gen.obstacleGlowIntensity = L.obstacleGlowIntensity;
        gen.obstacleGlowRange = L.obstacleGlowRange;
        gen.bridgePlankColor = L.plank;
        if (L.disablePortalTiles) gen.portalSquarePaths = false;   // scene 5: plain stepping stones; the tiled slab is the tower's
        if (L.portalTileShade >= 0f) gen.portalTileSeamShade = L.portalTileShade;
        EditorUtility.SetDirty(gen);

        var pz = gen.GetComponent<PortalZoneController>();
        if (pz != null)
        {
            Undo.RecordObject(pz, "Prop look");
            pz.blockMaterial = block;
            pz.skyMaterial = sky;
            pz.keepScenePalette = true;
            pz.sceneAccent = L.accent;
            pz.doorStoneColor = L.doorStone;
            pz.doorGlowColor = L.doorGlow;
            EditorUtility.SetDirty(pz);
        }

        var backdrop = Object.FindFirstObjectByType<MonumentSkyBackdrop>(FindObjectsInactive.Include);
        if (backdrop != null) { Undo.RecordObject(backdrop, "Prop look"); backdrop.skyMaterial = sky; EditorUtility.SetDirty(backdrop); }
        var floor = Object.FindFirstObjectByType<MonumentShadowFloor>(FindObjectsInactive.Include);
        if (floor != null)
        {
            Undo.RecordObject(floor, "Prop look");
            floor.enabled = L.glowFloor;
            if (L.glowFloor)
            {
                var fm = Mat("M_" + L.key + "_GlowFloor", "PropLooks/GlowFloor");
                fm.SetColor("_CenterColor", L.floorCenter); fm.SetColor("_EdgeColor", L.floorEdge);
                fm.SetColor("_HazeColor", L.floorHaze); fm.SetColor("_ShadowTint", L.floorShadow);
                fm.SetFloat("_ShadowStrength", L.floorShadowStrength); fm.SetFloat("_Opacity", L.floorOpacity);
                fm.SetFloat("_InnerRadius", L.floorInner); fm.SetFloat("_FadeRadius", L.floorFade);
                if (L.floorBlur >= 0f) fm.SetFloat("_Blur", L.floorBlur);
                fm.renderQueue = L.floorQueue;   // -1 resets to the shader's queue
                EditorUtility.SetDirty(fm);
                floor.floorMaterial = fm;
                floor.gapBelowTrack = L.floorGap;
                floor.size = Mathf.Max(floor.size, L.floorFade * 2.4f);
            }
            EditorUtility.SetDirty(floor);
        }

        // Runner: both of its overlapping cube renderers take the runner material.
        var runner = gen.player != null ? gen.player : Object.FindFirstObjectByType<CubeRailRunner>();
        if (runner != null)
        {
            foreach (var r in runner.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name != "Cube" || !r.gameObject.activeSelf) continue;
                Undo.RecordObject(r, "Prop look");
                r.sharedMaterial = player;
                EditorUtility.SetDirty(r);
            }
            var trailFx = runner.GetComponent<RunnerTrailFX>();
            if (trailFx != null)
            {
                // Face-switch ring and the wisps: the zone palette only sets the trail colours.
                Color.RGBToHSV(L.accent, out float ah, out float asat, out float av);
                SetColors(trailFx, ("switchColor", Color.Lerp(L.accent, Color.white, 0.35f)),
                                   ("wispColorA", Color.Lerp(L.accent, Color.white, 0.2f)),
                                   ("wispColorB", Color.HSVToRGB(Mathf.Repeat(ah + 0.04f, 1f), Mathf.Min(1f, asat * 1.1f), av)));
            }
            var afh = runner.GetComponent<ActiveFaceHighlight>();
            if (afh != null) SetColors(afh, ("faceColor", L.faceHighlight), ("switchColor", L.faceSwitch));
        }
        var arrows = gen.GetComponent<TurnFaceArrows>();
        if (arrows != null) SetColors(arrows, ("arrowColor", L.arrow));

        // Camera: the SSAO renderer + post-processing on. Position, rotation and size untouched.
        Camera cam = Camera.main;
        var cd = cam.GetComponent<UniversalAdditionalCameraData>();
        Undo.RecordObject(cd, "Prop look");
        Undo.RecordObject(cam, "Prop look");
        cd.SetRenderer(rendererIndex);
        cd.renderPostProcessing = true;
        cd.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        cam.backgroundColor = L.skyBottom;
        EditorUtility.SetDirty(cd);
        EditorUtility.SetDirty(cam);

        // Global volume.
        var volGO = GameObject.Find("PropLook_Volume");
        if (volGO == null) { volGO = new GameObject("PropLook_Volume"); Undo.RegisterCreatedObjectUndo(volGO, "Prop look"); }
        var vol = volGO.GetComponent<Volume>();
        if (vol == null) vol = volGO.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 10;
        vol.sharedProfile = profile;
        EditorUtility.SetDirty(vol);

        // Fog reference height.
        var ctlGO = GameObject.Find("PropLook");
        if (ctlGO == null) { ctlGO = new GameObject("PropLook"); Undo.RegisterCreatedObjectUndo(ctlGO, "Prop look"); }
        var ctl = ctlGO.GetComponent<PropLookController>();
        if (ctl == null) ctl = ctlGO.AddComponent<PropLookController>();
        ctl.pathGen = gen;
        ctl.followRunner = L.motesRate > 0f ? Motes(L, ctlGO.transform) : null;
        EditorUtility.SetDirty(ctl);

        // Scene 5: the stacked-cube tower skin on the zone doors, and the camera-relative sun.
        if (L.towerSkin)
        {
            var skin = ctlGO.GetComponent<PropPortalTowerSkin>();
            if (skin == null) skin = ctlGO.AddComponent<PropPortalTowerSkin>();
            EditorUtility.SetDirty(skin);
        }
        if (L.startPortal)
        {
            var sp = ctlGO.GetComponent<PropStartPortal>();
            if (sp == null) sp = ctlGO.AddComponent<PropStartPortal>();
            EditorUtility.SetDirty(sp);
        }
        if (L.sunPitch > 0f)
        {
            var rig = ctlGO.GetComponent<PropSunRig>();
            if (rig == null) rig = ctlGO.AddComponent<PropSunRig>();
            rig.pitch = L.sunPitch; rig.yawOffset = L.sunYawOffset;
            EditorUtility.SetDirty(rig);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        log.AppendLine("renderer index " + rendererIndex + ", volume " + AssetDatabase.GetAssetPath(profile));
        return log.ToString();
    }
}
