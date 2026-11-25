using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// LookTransition: everything that makes one "MainScene prop camera" scene LOOK the way it does, as plain values - the
/// block materials' face colours / fog / grain / glow (packed per role, linear), the sky, ChasmFog's backdrop, the sun,
/// ambient, camera background, the post-processing profile, the path's tint gradient and the small accent colours
/// (trail, face highlight, turn arrows, planks, portal doors). Gameplay is not in here.
///
/// Written by Tools > Look Transition > Capture Look From Scene (which opens the scene additively, reads it and closes it
/// unsaved); the director also captures the running scene's own look at start with <see cref="Capture"/>, so the look
/// before the first portal is exactly the scene's.
/// </summary>
[CreateAssetMenu(menuName = "Look Transition/Atmosphere Look", fileName = "Look_")]
public class AtmosphereLook : ScriptableObject
{
    public const int RoleBlock = 0, RoleBlockFade = 1, RolePlayer = 2, RoleObstacle = 3, RoleCount = 4;
    /// <summary>float4 per role in the shader's _LTA / _LTB slots (FlatFaceBlockWipe), and the slot array length.</summary>
    public const int BlockStride = 16, BlockSlots = 128;
    public const int SkyStride = 8;
    public static readonly string[] RoleNames = { "Block (path cubes)", "Block fade (crossed legs)", "Player", "Obstacle (pyramid)" };

    [Tooltip("The scene this look was captured from.")]
    public string sourceScene;

    [Header("Block materials (FlatFaceBlock family), packed per role - see PackBlock")]
    public Vector4[] blockRoles = new Vector4[RoleCount * BlockStride];
    public bool[] hasRole = new bool[RoleCount];

    [Header("Sky (RadialSky), packed - see PackSky")]
    public Vector4[] sky = new Vector4[SkyStride];
    public bool hasSky;
    [Tooltip("The sky's top colour as authored (the runner trail's tail is mixed toward it).")]
    public Color skyTopColor = Color.black;

    [Header("ChasmFog backdrop")]
    public bool hasChasm;
    public Color chasmTop, chasmBottom, chasmGlow;
    public float chasmGlowStrength;
    public Vector2 chasmGlowCentre;
    public float chasmGlowRadius;

    [Header("Light")]
    public Color sunColor = Color.white;
    public float sunIntensity = 1f;
    public float sunShadowStrength = 1f;
    public Vector3 sunEuler;
    [Tooltip("0 skybox, 1 trilight, 3 flat (UnityEngine.Rendering.AmbientMode).")]
    public int ambientMode = 3;
    public Color ambientSky, ambientEquator, ambientGround;
    public Color cameraBackground;

    [Header("Post-processing (a copy in Looks/Profiles for captured looks)")]
    public VolumeProfile volumeProfile;

    [Header("Path tint (PathDepthShading) and accents")]
    [Tooltip("aheadGradient at ZonePalette.GradientTimes.")]
    public Color[] aheadKeys = new Color[4];
    public Color travelledColor = Color.white;
    public Color doorGlowColor = Color.white, doorStoneColor = Color.white, sceneAccent = Color.white;
    public Color wispColorA = Color.white, wispColorB = Color.white, trailSwitchColor = Color.white;
    public Color faceColor = Color.white, faceSwitchColor = Color.white;
    public Color arrowColor = Color.white;
    public Color plankColor = Color.white;
    public Color obstacleGlowColor = Color.white;
    public float obstacleGlowIntensity = 1f, obstacleGlowRange = 1f;

    // ------------------------------------------------------------------ packing

    static Vector4 Lin(Material m, string p, float a)
    {
        Color c = m.HasProperty(p) ? m.GetColor(p).linear : Color.black;
        return new Vector4(c.r, c.g, c.b, a);
    }

    static float F(Material m, string p) => m.HasProperty(p) ? m.GetFloat(p) : 0f;

    /// <summary>Writes a FlatFaceBlock-family material's look into dst[offset .. offset + 16) in the FlatFaceBlockWipe slot layout (linear colours).</summary>
    public static void PackBlock(Material m, Vector4[] dst, int offset)
    {
        dst[offset + 0] = Lin(m, "_TopColor", 1f);
        dst[offset + 1] = Lin(m, "_LeftColor", 1f);
        dst[offset + 2] = Lin(m, "_RightColor", 1f);
        dst[offset + 3] = Lin(m, "_BottomColor", 1f);
        dst[offset + 4] = Lin(m, "_ShadowColor", F(m, "_ShadowStrength"));
        dst[offset + 5] = Lin(m, "_EdgeColor", F(m, "_EdgeStrength"));
        dst[offset + 6] = Lin(m, "_SeamColor", F(m, "_SeamStrength"));
        dst[offset + 7] = Lin(m, "_FogTopColor", 1f);
        dst[offset + 8] = Lin(m, "_FogBottomColor", 1f);
        dst[offset + 9] = Lin(m, "_GlowTint", F(m, "_GlowGain"));
        dst[offset + 10] = Lin(m, "_EmissiveColor", F(m, "_GlowBase"));
        dst[offset + 11] = new Vector4(F(m, "_FogStart"), F(m, "_FogEnd"), F(m, "_FogMax"), F(m, "_ScreenFogHeight"));
        dst[offset + 12] = new Vector4(F(m, "_ScreenFogStrength"), F(m, "_FogColorFromChasm"), F(m, "_ChasmFogMix"), F(m, "_PointLightStrength"));
        dst[offset + 13] = new Vector4(F(m, "_GrainStrength"), F(m, "_GrainScale"), F(m, "_GrainDarkBias"), F(m, "_PointLightWrap"));
        dst[offset + 14] = new Vector4(F(m, "_SideSoftness"), F(m, "_TopSharpness"), F(m, "_EdgeWidth"), F(m, "_SeamWidth"));
        dst[offset + 15] = new Vector4(F(m, "_TileTop"), F(m, "_TileSide"), 0f, 0f);
    }

    /// <summary>Writes a RadialSky material's look into dst[0 .. 8) in the RadialSkyWipe slot layout (linear colours).</summary>
    public static void PackSky(Material m, Vector4[] dst)
    {
        dst[0] = Lin(m, "_TopColor", 1f);
        dst[1] = Lin(m, "_BottomColor", 1f);
        dst[2] = Lin(m, "_MidColor", 1f);
        dst[3] = Lin(m, "_GlowColor", 1f);
        dst[4] = Lin(m, "_VignetteColor", 1f);
        Vector4 gc = m.HasProperty("_GlowCenter") ? m.GetVector("_GlowCenter") : new Vector4(0.5f, 0.55f, 0f, 0f);
        dst[5] = new Vector4(gc.x, gc.y, F(m, "_GlowRadius"), F(m, "_GlowStrength"));
        dst[6] = new Vector4(F(m, "_GlowFalloff"), F(m, "_HazeHeight"), F(m, "_HazeStrength"), F(m, "_Vignette"));
        dst[7] = new Vector4(F(m, "_Rings"), F(m, "_RingCount"), F(m, "_Grain"), 0f);
    }

    /// <summary>The objects a look is read from. Any may be null; what is missing is left at its default and flagged off.</summary>
    public struct Sources
    {
        public Material block, blockFade, player, obstacle, sky;
        public ChasmFog chasm;
        public Light sun;
        public Camera camera;
        public Volume volume;
        public PathDepthShading shading;
        public PortalZoneController zones;
        public RunnerTrailFX trail;
        public ActiveFaceHighlight faceHighlight;
        public TurnFaceArrows arrows;
        public DynamicPathGenerator generator;
    }

    /// <summary>
    /// Reads every look value from the given objects. Ambient is NOT read here (it lives in the scene's RenderSettings,
    /// which belong to the active scene only): set ambientMode / ambientSky / ... yourself.
    /// </summary>
    public void Capture(Sources s)
    {
        Material[] roles = { s.block, s.blockFade, s.player, s.obstacle };
        for (int r = 0; r < RoleCount; r++)
        {
            hasRole[r] = roles[r] != null;
            if (hasRole[r]) PackBlock(roles[r], blockRoles, r * BlockStride);
        }
        hasSky = s.sky != null;
        if (hasSky)
        {
            PackSky(s.sky, sky);
            skyTopColor = s.sky.HasProperty("_TopColor") ? s.sky.GetColor("_TopColor") : Color.black;
        }

        hasChasm = s.chasm != null;
        if (hasChasm)
        {
            chasmTop = s.chasm.topColor; chasmBottom = s.chasm.bottomColor; chasmGlow = s.chasm.glowColor;
            chasmGlowStrength = s.chasm.glowStrength; chasmGlowCentre = s.chasm.glowCentre; chasmGlowRadius = s.chasm.glowRadius;
        }

        if (s.sun != null)
        {
            sunColor = s.sun.color; sunIntensity = s.sun.intensity; sunShadowStrength = s.sun.shadowStrength;
            sunEuler = s.sun.transform.eulerAngles;
        }
        if (s.camera != null) cameraBackground = s.camera.backgroundColor;
        if (s.volume != null) volumeProfile = s.volume.sharedProfile;

        if (s.shading != null && s.shading.aheadGradient != null)
        {
            for (int i = 0; i < 4; i++) aheadKeys[i] = s.shading.aheadGradient.Evaluate(ZonePalette.GradientTimes[i]);
            travelledColor = s.shading.travelledColor;
        }
        if (s.zones != null)
        {
            doorGlowColor = s.zones.doorGlowColor;
            doorStoneColor = s.zones.doorStoneColor;
            sceneAccent = s.zones.sceneAccent;
        }
        if (s.trail != null) { wispColorA = s.trail.wispColorA; wispColorB = s.trail.wispColorB; trailSwitchColor = s.trail.switchColor; }
        if (s.faceHighlight != null) { faceColor = s.faceHighlight.faceColor; faceSwitchColor = s.faceHighlight.switchColor; }
        if (s.arrows != null) arrowColor = s.arrows.arrowColor;
        if (s.generator != null)
        {
            plankColor = s.generator.bridgePlankColor;
            obstacleGlowColor = s.generator.obstacleGlowColor;
            obstacleGlowIntensity = s.generator.obstacleGlowIntensity;
            obstacleGlowRange = s.generator.obstacleGlowRange;
        }
    }

    /// <summary>One line per value, "Section | name | value", for the survey table.</summary>
    public void Describe(System.Collections.Generic.List<string> rows)
    {
        string[] blockNames = { "top", "left", "right", "bottom", "shadow (a=strength)", "rim (a=strength)", "seam (a=strength)", "fog top", "fog bottom",
                                "glow tint (a=gain)", "emissive (a=glow base)", "fog start/end/max/screen h", "screen fog/from chasm/chasm mix/point light",
                                "grain str/scale/dark bias/point wrap", "side soft/top sharp/rim w/seam w", "panels top/side" };
        for (int r = 0; r < RoleCount; r++)
        {
            if (!hasRole[r]) { rows.Add(RoleNames[r] + " | (none) | -"); continue; }
            for (int k = 0; k < BlockStride; k++) rows.Add(RoleNames[r] + " | " + blockNames[k] + " | " + V(blockRoles[r * BlockStride + k], k <= 10));
        }
        string[] skyNames = { "top", "bottom", "radial centre", "low haze", "vignette colour", "glow centre xy/radius/strength", "falloff/haze h/haze str/vignette", "rings/ring count/grain" };
        for (int k = 0; k < SkyStride; k++) rows.Add("Sky | " + skyNames[k] + " | " + (hasSky ? V(sky[k], k <= 4) : "-"));
        rows.Add("ChasmFog | top / bottom | " + (hasChasm ? Hex(chasmTop) + " / " + Hex(chasmBottom) : "-"));
        rows.Add("ChasmFog | glow / strength / centre / radius | " + (hasChasm ? Hex(chasmGlow) + " / " + chasmGlowStrength.ToString("0.##") + " / " + chasmGlowCentre.ToString("0.##") + " / " + chasmGlowRadius.ToString("0.##") : "-"));
        rows.Add("Sun | colour / intensity / shadow / euler | " + Hex(sunColor) + " / " + sunIntensity.ToString("0.##") + " / " + sunShadowStrength.ToString("0.##") + " / " + sunEuler.ToString("0"));
        rows.Add("RenderSettings | ambient mode / sky / equator / ground | " + ambientMode + " / " + Hex(ambientSky) + " / " + Hex(ambientEquator) + " / " + Hex(ambientGround));
        rows.Add("Camera | background | " + Hex(cameraBackground));
        rows.Add("Post | profile | " + (volumeProfile != null ? volumeProfile.name : "-"));
        if (volumeProfile != null)
            foreach (VolumeComponent c in volumeProfile.components)
            {
                if (!c.active) { rows.Add("Post | " + c.GetType().Name + " | (inactive)"); continue; }
                foreach (System.Reflection.FieldInfo f in c.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    if (typeof(VolumeParameter).IsAssignableFrom(f.FieldType) && f.GetValue(c) is VolumeParameter p && p.overrideState)
                        rows.Add("Post | " + c.GetType().Name + "." + f.Name + " | " + ParamValue(p));
            }
        rows.Add("Path tint | ahead keys | " + Hex(aheadKeys[0]) + " " + Hex(aheadKeys[1]) + " " + Hex(aheadKeys[2]) + " " + Hex(aheadKeys[3]));
        rows.Add("Path tint | travelled | " + Hex(travelledColor));
        rows.Add("Exit portal | glow / stone / accent | " + Hex(doorGlowColor) + " / " + Hex(doorStoneColor) + " / " + Hex(sceneAccent));
        rows.Add("Runner | trail wisps A / B / switch | " + Hex(wispColorA) + " / " + Hex(wispColorB) + " / " + Hex(trailSwitchColor));
        rows.Add("Runner | face highlight / switch | " + Hex(faceColor) + " / " + Hex(faceSwitchColor));
        rows.Add("Hazards | turn arrows | " + Hex(arrowColor));
        rows.Add("Hazards | plank colour | " + Hex(plankColor));
        rows.Add("Hazards | obstacle glow / intensity / range | " + Hex(obstacleGlowColor) + " / " + obstacleGlowIntensity.ToString("0.##") + " / " + obstacleGlowRange.ToString("0.##"));
    }

    static string ParamValue(VolumeParameter p)
    {
        object v = p.GetType().GetProperty("value")?.GetValue(p);
        return v is Color c ? Hex(c) : v is float fl ? fl.ToString("0.###") : v != null ? v.ToString() : "null";
    }

    // Linear packed colours are shown back in gamma, as an inspector shows them.
    static string V(Vector4 v, bool colour) => colour
        ? Hex(new Color(v.x, v.y, v.z, 1f).gamma) + " a=" + v.w.ToString("0.###")
        : v.x.ToString("0.###") + " / " + v.y.ToString("0.###") + " / " + v.z.ToString("0.###") + " / " + v.w.ToString("0.###");

    public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c) + (c.a < 0.999f ? "(a" + c.a.ToString("0.##") + ")" : "");
}
