// Pixel checkpoint (MainScene prop camera 3.9 / Pixel only): a copy of DepthCues37/FlatFaceBlockDepthCue (left untouched)
// plus the pixel-art leg texture (PixelLeg.hlsl: moss top with pixel drips, coral bricks, cobble bottom) where the pixel
// look is in force (PixelCommon.hlsl, follows the look wipe). Everywhere else it is FlatFaceBlockDepthCue pixel for pixel.
// The 3.9 LookTransitionDirector.blockWipeShader points here.
//
// MainScene prop camera 3.7 only: FlatFaceBlockWipe (PropCameraLooks/LookTransition/Shaders, left untouched) plus the
// depth / orientation cues DepthCueController drives through globals (all zero = this is FlatFaceBlockWipe pixel for pixel).
// 3.7's LookTransitionDirector.blockWipeShader points here, so path cubes, crossed legs, the runner and obstacles use it.
//   Face ladder   every face keeps its hue but is held under a share of the top face's luminance: the lit side, the shade
//                 side and the bottom step down, so top / side / bottom never read as one flat colour.
//   Top lip       a thin pale line where a top face meets its sides (Monument Valley's lit edge).
//   Contact       a soft square of light on the face right under the runner (no round glow), and on the runner itself a
//                 rim of the same colour along its edges that touch the rail - grounds the exact contact point.
//   Rail          a thin line down the middle of the ridden face, from the runner to the end of the leg, closed by a
//                 crossbar where the turn comes - only while the camera sees that face. When the ridden face is turned
//                 away, there is no rail; a short contact mark on the edge it shares with a visible face stays.
//
// --- FlatFaceBlockWipe notes ---
// LookTransition copy of PropLooks/FlatFaceBlock (Assets/PropCameraLooks/Shaders, left untouched) for MainScene prop camera 3.2.
// Everything below is the original, except where the look lives: the face colours, shadow, seams, grain, fog and glow
// settings are read from two global slots - _LTA (the look in force) and _LTB (the next look), 16 float4 per role, the
// role being this material's _LTRole - and picked per pixel by the look wipe (LookWipe.hlsl). LookTransitionDirector fills
// the slots from AtmosphereLook assets (AtmosphereLook.PackBlock). Copies other scripts make of the material (PathDepthShading's
// lit / fade / ghost copies) keep _LTRole, so they follow the wipe too. Properties not in the slots stay the material's own.
//
// --- original notes ---
// Flat, matte, normal-based block shader for the "MainScene prop camera 1/2/3" look scenes
// (Monument Valley style references). No PBR, no specular, no smoothness.
//
//   Faces     Every pixel takes its colour from which way its face points RELATIVE TO THE CAMERA:
//             up = _TopColor, sides turned to the screen's left = _LeftColor, to the right =
//             _RightColor, down = _BottomColor. The camera rig yaws between shots, so "left" and
//             "right" are measured against the camera's own right vector, not world X/Z - the two
//             visible side faces of every leg always read as two different, crisp tones, and a cube
//             corner is always three flat colours meeting at a hard edge. The directional light is
//             only used for its cast shadow.
//   Tint      _BaseColor (set per cube by PathDepthShading through a property block) multiplies the
//             face colours, so the route's distance cues (brighter ahead, cooler behind) still work;
//             the scene's gradient is kept near white for that.
//   Seams     Thin darker lines where one cube meets the next (the brick joints in the references)
//             and an optional pale rim along the long top edges of each beam.
//   Grain     Soft world-anchored mottling (the printed-paper grain of the Penrose image).
//   Fog       Height fog relative to a reference height the PropLookController moves with the runner
//             (_PropFogRefY), so the bottom of the structure dissolves into the backdrop whether the
//             path is climbing or falling. Its colour is a screen-space gradient that matches the sky.
//   Glow      Point lights (additional lights) add a soft coloured bleed - the yellow totem light on
//             grey stone - and the boost self-light PathDepthShading writes into _EmissionColor turns
//             into a coloured glow (_GlowTint) instead of just brightening.
//
// Carries the URP Lit-style blend properties so PathDepthShading.ConfigureAsFade can turn a copy of
// it into the see-through material exactly as it does for MonumentBlock.
Shader "PixelCheckpoint/FlatFaceBlockPixel"
{
    Properties
    {
        _BaseColor ("Tint (driven per cube by PathDepthShading)", Color) = (1, 1, 1, 1)
        [HideInInspector] _Color ("Colour (legacy)", Color) = (1, 1, 1, 1)
        [HideInInspector] _EmissionColor ("Emission (self-light, set per cube)", Color) = (0, 0, 0, 1)

        [Header(Face Colours (camera relative))]
        _TopColor ("Top", Color) = (0.86, 0.88, 0.88, 1)
        _LeftColor ("Left Side", Color) = (0.70, 0.74, 0.75, 1)
        _RightColor ("Right Side", Color) = (0.52, 0.57, 0.59, 1)
        _BottomColor ("Bottom", Color) = (0.30, 0.33, 0.35, 1)
        _SideSoftness ("Left/Right Softness (0 = crisp, for cubes)", Range(0, 1)) = 0.02
        _TopSharpness ("Top/Side Sharpness (1 = linear, high = crisp)", Range(1, 16)) = 8

        [Header(Cast Shadow)]
        _ShadowColor ("Shadow Tint", Color) = (0.55, 0.55, 0.62, 1)
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.6

        [Header(Top Rim Highlight)]
        _EdgeColor ("Rim Colour", Color) = (1, 1, 1, 1)
        _EdgeWidth ("Rim Width (object units)", Range(0.001, 0.2)) = 0.03
        _EdgeStrength ("Rim Strength", Range(0, 1)) = 0.0
        [Toggle] _EdgeAllAxes ("Rim On All Axes", Float) = 0

        [Header(Seams Between Cubes)]
        _SeamColor ("Seam Colour (multiplier)", Color) = (0.72, 0.74, 0.76, 1)
        _SeamWidth ("Seam Width (object units)", Range(0.001, 0.1)) = 0.018
        _SeamStrength ("Seam Strength", Range(0, 1)) = 0.0

        [Header(Carved Square Panels (off by default))]
        _TileTop ("Panels on Top Faces", Range(0, 1)) = 0
        _TileSide ("Panels on Side Faces", Range(0, 1)) = 0
        _TileInset ("Panel Inset (object units from edge)", Range(0.02, 0.3)) = 0.13
        _TileGroove ("Groove Width", Range(0.005, 0.1)) = 0.035
        _TileGrooveColor ("Groove Colour (multiplier)", Color) = (0.62, 0.45, 0.35, 1)
        _TilePanelColor ("Panel Colour (multiplier)", Color) = (0.93, 0.9, 0.88, 1)
        _TileLipColor ("Lit Lip Colour (multiplier)", Color) = (1.15, 1.08, 1.0, 1)

        [Header(Grain)]
        _GrainStrength ("Grain Strength", Range(0, 0.3)) = 0.0
        _GrainScale ("Grain Cells per Unit", Range(1, 60)) = 14
        _GrainDarkBias ("More Grain on Dark Faces", Range(0, 1)) = 0.5

        [Header(Height Fog (below the runner))]
        _FogTopColor ("Fog Colour (screen top)", Color) = (0.2, 0.2, 0.3, 1)
        _FogBottomColor ("Fog Colour (screen bottom)", Color) = (0.2, 0.2, 0.3, 1)
        _FogStart ("Fog Start (units below runner)", Float) = 6
        _FogEnd ("Fog Full (units below runner)", Float) = 40
        _FogMax ("Fog Max", Range(0, 1)) = 0.6
        _ScreenFogHeight ("Screen-Bottom Fog Height (0..1)", Range(0, 1)) = 0
        _ScreenFogStrength ("Screen-Bottom Fog Strength", Range(0, 1)) = 0
        _FogColorFromChasm ("Fog Colour = ChasmFog Backdrop (needs a ChasmFog)", Range(0, 1)) = 0
        _ChasmFogMix ("Take ChasmFog's Layers (height / depth haze / screen)", Range(0, 1)) = 0

        [Header(Glow)]
        _PointLightStrength ("Point Light Bleed", Range(0, 4)) = 1
        _PointLightWrap ("Point Light Wrap", Range(0, 1)) = 0.35
        _GlowTint ("Boost Glow Colour", Color) = (1, 0.85, 0.4, 1)
        _GlowBase ("Self-Light Baseline (PathDepthShading.selfLight)", Range(0, 2)) = 0.55
        _GlowGain ("Boost Glow Gain", Range(0, 2)) = 0.6
        [HDR] _EmissiveColor ("Constant Emission (for glowing props)", Color) = (0, 0, 0, 1)

        [Header(Runner Face Highlight (needs PropLookController.highlightRunnerFace))]
        _RunnerGlowStrength ("Strength (0 = off)", Range(0, 1)) = 0
        _RunnerGlowColor ("Colour", Color) = (0.25, 0.95, 0.9, 1)
        _RunnerGlowEdge ("Glow on the edge next to a face the camera can't see", Range(0, 1)) = 1
        _RunnerGlowDim ("Dim the other faces near the runner", Range(0, 1)) = 0.25

        [Header(Portal Masking)]
        [IntRange] _StencilRef ("Stencil Ref", Range(0, 255)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Compare", Float) = 8

        [Header(Look Transition)]
        _LTRole ("Look Role (0 block, 1 block fade, 2 player, 3 obstacle)", Float) = 0

        [HideInInspector] _ZoneGone ("Left behind by a zone change (set per renderer)", Float) = 0
        [HideInInspector] _ToonLeg ("Toon leg: along / length / serial / set (per cube, ToonLegDesigns)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _LegStageFace ("Leg stage: face target (linear rgb), amount (per cube, LegStageColors)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _LegStageSel ("Leg stage: hue pick (source hue, half width, hue shift, amount)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _LegStageSel2 ("Leg stage: (saturation x, value x, threshold line, stripes)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _Surface ("__surface", Float) = 0
        [HideInInspector] _Blend ("__blend", Float) = 0
        [HideInInspector] _AlphaClip ("__clip", Float) = 0
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _SrcBlendAlpha ("__srcA", Float) = 1
        [HideInInspector] _DstBlendAlpha ("__dstA", Float) = 0
        [HideInInspector] _ZWrite ("__zw", Float) = 1
        [HideInInspector] _Cull ("__cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Set every frame by PropLookController: the smoothed height of the runner.
        float _PropFogRefY;
        // Shared with MonumentBlock: 1 once a zone-change sweep is over (never animated any more).
        half _ZoneGoneShadow;
        float _ZoneWipeX;
        // Set every frame by PropLookController (highlightRunnerFace): _PropRunnerP = point on the face
        // under the runner (w = 0..1 on-ness), _PropRunnerN = that face's world normal (w = radius).
        float4 _PropRunnerP;
        float4 _PropRunnerN;

        // DepthCueController (3.7). All zero when nothing drives them = no cue at all.
        float4 _DCFace;      // x ladder strength, y lit side / top, z shade side / top, w bottom / top (linear luminance shares)
        float4 _DCFace2;     // x top lip strength, y lip width (object units), z ladder strength on the runner, w runner rim strength
        float4 _DCRunnerP;   // xyz point on the ridden face under the runner, w contact strength
        float4 _DCRunnerN;   // xyz ridden face normal, w how visible that face is to the camera (0 hidden .. 1 facing)
        float4 _DCRunnerF;   // xyz direction of travel along the leg, w distance from the runner to the leg's far end
        float4 _DCContact;   // rgb contact colour (linear), a half size of the contact square (world units)
        float4 _DCRail;      // rgb rail colour (linear), a rail strength
        float4 _DCRail2;     // x rail half width, y length kept behind the runner, z switch flash 0..1, w fade-in length at the far end

        // FaceFormController (scene 3.11 only). All zero when nothing drives them = no form shading at all.
        float4 _FDForm;      // x strength 0..1 (0 = off), y shade side multiplier, z bottom multiplier, w top lift (added to 1)
        float4 _FDEdge;      // x crease under the top edge of side faces, y its width (cube units), z grounding at the side's foot, w its width
        float4 _FDLip;       // rgb lip colour (linear, scaled by the cube tint), a lip strength on the long top edges
        float4 _FDLip2;      // x lip width (cube units), y lit side multiplier, z/w unused

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _Color;
            half4 _EmissionColor;
            half4 _TopColor, _LeftColor, _RightColor, _BottomColor;
            half _SideSoftness, _TopSharpness;
            half4 _ShadowColor;
            half _ShadowStrength;
            half4 _EdgeColor;
            half _EdgeWidth, _EdgeStrength, _EdgeAllAxes;
            half4 _SeamColor;
            half _SeamWidth, _SeamStrength;
            half _TileTop, _TileSide, _TileInset, _TileGroove;
            half4 _TileGrooveColor, _TilePanelColor, _TileLipColor;
            half _GrainStrength, _GrainScale, _GrainDarkBias;
            half4 _FogTopColor, _FogBottomColor;
            float _FogStart, _FogEnd;
            half _FogMax, _ScreenFogHeight, _ScreenFogStrength;
            half _FogColorFromChasm, _ChasmFogMix;
            half _PointLightStrength, _PointLightWrap;
            half4 _GlowTint;
            half _GlowBase, _GlowGain;
            half4 _EmissiveColor;
            half _RunnerGlowStrength, _RunnerGlowEdge, _RunnerGlowDim;
            half4 _RunnerGlowColor;
            half _Surface;
            half _StencilRef, _StencilComp;
            half _ZoneGone;
            half _LTRole;
            float4 _ToonLeg;
            float4 _LegStageFace, _LegStageSel, _LegStageSel2;
        CBUFFER_END

        // Look slots, 16 float4 per role (linear colours), see AtmosphereLook.PackBlock:
        //  0 top  1 left  2 right  3 bottom  4 shadow colour, a shadow strength  5 rim colour, a rim strength
        //  6 seam colour, a seam strength  7 fog top  8 fog bottom  9 glow tint, a glow gain  10 emissive, a glow base
        // 11 (fog start, fog end, fog max, screen fog height)  12 (screen fog strength, fog from chasm, chasm mix, point light strength)
        // 13 (grain strength, grain scale, grain dark bias, point light wrap)  14 (side softness, top sharpness, rim width, seam width)
        // 15 (panels on top, panels on sides, -, -)
        float4 _LTA[128];
        float4 _LTB[128];
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Stencil { Ref [_StencilRef] Comp [_StencilComp] }
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // ChasmFog's backdrop colour and fog layers (all zero / off when the scene has no ChasmFog).
            #include "Assets/PropCameraLooks/LookTransition/Shaders/LookChasmFog.hlsl"
            // Toon checkpoint: this role's slot 15.z (toon amount) / 15.w (ink). Zero in every other look = no change.
            #define TOON_LT_SLOTS_DECLARED
            #include "Assets/ToonCheckpoint/Shaders/ToonCommon.hlsl"
            #include "Assets/ToonCheckpoint/Shaders/ToonLegDesigns.hlsl"
            // Pixel checkpoint: the pixel look amount (_PixLook + the wipe) and the leg texture.
            #include "Assets/PixelCheckpoint/Shaders/PixelCommon.hlsl"
            #include "Assets/PixelCheckpoint/Shaders/PixelLeg.hlsl"
            // Storybook checkpoint: the ink-and-watercolour house / bridge texture (StorybookLeg.hlsl).
            #include "Assets/StorybookCheckpoint/Shaders/StorybookCommon.hlsl"
            #include "Assets/StorybookCheckpoint/Shaders/StorybookLeg.hlsl"
            // Flower checkpoint: the painted meadow / moss / flower-drip texture (FlowerLeg.hlsl).
            #include "Assets/FlowerCheckpoint/Shaders/FlowerCommon.hlsl"
            #include "Assets/FlowerCheckpoint/Shaders/FlowerLeg.hlsl"
            // Voxel checkpoint: the slab-on-bricks leg with drips, a new colour every leg (VoxelLeg.hlsl).
            #include "Assets/VoxelCheckpoint/Shaders/VoxelCommon.hlsl"
            #include "Assets/VoxelCheckpoint/Shaders/VoxelLeg.hlsl"
            // Pencil checkpoint: graphite-on-paper legs, four sketchbook pages per level + art friezes (PencilLeg.hlsl).
            #include "Assets/PencilCheckpoint/Shaders/PencilCommon.hlsl"
            #include "Assets/PencilCheckpoint/Shaders/PencilLeg.hlsl"
            // Space checkpoint: oil-painted spaceship legs - hull deck, glass corridors, engine bays, whole ships (SpaceLeg.hlsl).
            #include "Assets/SpaceCheckpoint/Shaders/SpaceCommon.hlsl"
            #include "Assets/SpaceCheckpoint/Shaders/SpaceLeg.hlsl"
            // Spice Route checkpoint: Indian then Arabian craft legs - temple stone, rangoli, mirror work, Madhubani, girih, Iznik,
            // mashrabiya, carpets, zellige, ebru (SpiceLeg.hlsl).
            #include "Assets/SpiceRouteCheckpoint/Shaders/SpiceCommon.hlsl"
            #include "Assets/SpiceRouteCheckpoint/Shaders/SpiceLeg.hlsl"
            float4 _ToonDesign;   // ToonCheckpointController: x designs on, y ink, z light, w moss (opacity of each paint)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                float3 normalOS : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                o.shadowCoord = GetShadowCoord(p);
                return o;
            }

            float hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            // Smooth value noise, anchored to the world so it never swims across a face.
            float vnoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash31(i), n100 = hash31(i + float3(1, 0, 0));
                float n010 = hash31(i + float3(0, 1, 0)), n110 = hash31(i + float3(1, 1, 0));
                float n001 = hash31(i + float3(0, 0, 1)), n101 = hash31(i + float3(1, 0, 1));
                float n011 = hash31(i + float3(0, 1, 1)), n111 = hash31(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            // Leg stage hue pick (LegStageColors).
            float3 LsRgbToHsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + 1e-5)), d / (q.x + 1e-5), q.x);
            }
            float3 LsHsvToRgb(float3 c)
            {
                float3 p = abs(frac(c.xxx + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
                return c.z * lerp(float3(1, 1, 1), saturate(p - 1.0), c.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                // Left behind by a zone change (PortalZoneController): gone, as in MonumentBlock.
                half oldSide = step(_ZoneWipeX, i.positionCS.x / _ScreenParams.x);
                if (_ZoneGone > 0.5h && oldSide < 0.5h) discard;

                float3 n = normalize(i.normalWS);

                // ---- the look this pixel is in (LookTransition): A, B, or in between on the wipe's soft edge ----
                half ltW = LTWipeAt(GetNormalizedScreenSpaceUV(i.positionCS));
                int ltB = (int)(_LTRole + 0.5h) * 16;
                half3 lTop    = lerp(_LTA[ltB + 0], _LTB[ltB + 0], ltW).rgb;
                half3 lLeft   = lerp(_LTA[ltB + 1], _LTB[ltB + 1], ltW).rgb;
                half3 lRight  = lerp(_LTA[ltB + 2], _LTB[ltB + 2], ltW).rgb;
                half3 lBottom = lerp(_LTA[ltB + 3], _LTB[ltB + 3], ltW).rgb;
                half4 lShadow = lerp(_LTA[ltB + 4], _LTB[ltB + 4], ltW);
                half4 lEdge   = lerp(_LTA[ltB + 5], _LTB[ltB + 5], ltW);
                half4 lSeam   = lerp(_LTA[ltB + 6], _LTB[ltB + 6], ltW);
                half3 lFogTop = lerp(_LTA[ltB + 7], _LTB[ltB + 7], ltW).rgb;
                half3 lFogBot = lerp(_LTA[ltB + 8], _LTB[ltB + 8], ltW).rgb;
                half4 lGlow   = lerp(_LTA[ltB + 9], _LTB[ltB + 9], ltW);
                half4 lEmis   = lerp(_LTA[ltB + 10], _LTB[ltB + 10], ltW);
                float4 lFog1  = lerp(_LTA[ltB + 11], _LTB[ltB + 11], ltW);
                half4 lFog2   = lerp(_LTA[ltB + 12], _LTB[ltB + 12], ltW);
                half4 lGrain  = lerp(_LTA[ltB + 13], _LTB[ltB + 13], ltW);
                half4 lShape  = lerp(_LTA[ltB + 14], _LTB[ltB + 14], ltW);
                half4 lTiles  = lerp(_LTA[ltB + 15], _LTB[ltB + 15], ltW);
                bool isRunner = _LTRole > 1.5h && _LTRole < 2.5h;

                // ---- in-level leg stage (LegStageColors, scene 3.10 only - zero everywhere else): the look's face colours
                // re-hued to the stage colour, every face keeping its luminance share of the top, so the shading is unchanged. ----
                half lsFace = saturate((half)_LegStageFace.a);
                if (lsFace > 0.001h && (_LTRole < 1.5h || _LTRole > 2.5h))   // path, crossed legs, obstacle blockers
                {
                    half3 lsW = half3(0.2126h, 0.7152h, 0.0722h);
                    half3 lsT = (half3)_LegStageFace.rgb;
                    half lsK = 1.0h / max(dot(lTop, lsW), 1e-4h);
                    lLeft   = lerp(lLeft,   lsT * dot(lLeft, lsW) * lsK, lsFace);
                    lRight  = lerp(lRight,  lsT * dot(lRight, lsW) * lsK, lsFace);
                    lBottom = lerp(lBottom, lsT * dot(lBottom, lsW) * lsK, lsFace);
                    lTop    = lerp(lTop, lsT, lsFace);
                }

                // ---- depth cue: face ladder. Each face keeps its hue; it is only ever darkened, down to its share
                // of the top's luminance (the brighter of the two camera sides is the lit one). ----
                half ladder = _DCFace.x * (isRunner ? _DCFace2.z : 1.0h);
                if (ladder > 0.001h)
                {
                    half3 lw = half3(0.2126h, 0.7152h, 0.0722h);
                    half yTop = max(dot(lTop, lw), 1e-4h);
                    half yL = max(dot(lLeft, lw), 1e-4h), yR = max(dot(lRight, lw), 1e-4h), yB = max(dot(lBottom, lw), 1e-4h);
                    half leftLit = step(yR, yL);
                    half capL = yTop * lerp(_DCFace.z, _DCFace.y, leftLit);
                    half capR = yTop * lerp(_DCFace.y, _DCFace.z, leftLit);
                    lLeft   *= lerp(1.0h, min(1.0h, capL / yL), ladder);
                    lRight  *= lerp(1.0h, min(1.0h, capR / yR), ladder);
                    lBottom *= lerp(1.0h, min(1.0h, yTop * _DCFace.w / yB), ladder);
                }

                // ---- face colour, relative to the camera ----
                float3 camRight = normalize(UNITY_MATRIX_V[0].xyz);
                float3 flat = n - float3(0, n.y, 0);
                float side = dot(flat, camRight) / max(length(flat), 1e-4);
                half s = lShape.x > 0.001h ? smoothstep(-lShape.x, lShape.x, side) : step(0.0, side);
                half3 sideCol = lerp(lLeft, lRight, s);
                half up = pow(saturate(n.y), lShape.y);
                half down = pow(saturate(-n.y), lShape.y);
                half3 col = sideCol;
                col = lerp(col, lTop, up);
                col = lerp(col, lBottom, down);

                // ---- per-cube tint from PathDepthShading ----
                half3 tint = _BaseColor.rgb;
                col *= tint;

                // ---- seams between cubes (local z ends) and the rim along the beam ----
                float3 e = 0.5 - abs(i.positionOS);
                float aaS = fwidth(e.z) + 1e-4;
                // Only on faces that run along the beam (not the end caps themselves).
                half seam = (1.0h - smoothstep(lShape.w - aaS, lShape.w + aaS, e.z)) * (1.0h - step(0.5, abs(i.normalOS.z)));
                col *= lerp(half3(1, 1, 1), lSeam.rgb, seam * lSeam.a);

                // ---- carved square panel on each face (the walkway in the orange canyon reference) ----
                if (lTiles.x + lTiles.y > 0.001h)
                {
                    float3 an = abs(i.normalOS);
                    // Distance to this face's own edge, and a 2D coordinate across the face.
                    float2 fe = an.x > 0.5 ? e.yz : an.y > 0.5 ? e.xz : e.xy;
                    float2 fp = an.x > 0.5 ? i.positionOS.yz : an.y > 0.5 ? i.positionOS.xz : i.positionOS.xy;
                    float fd = min(fe.x, fe.y);
                    float aaT = fwidth(fd) + 1e-4;
                    half inPanel = smoothstep(_TileInset - aaT, _TileInset + aaT, fd);
                    half groove = inPanel * (1.0h - smoothstep(_TileInset + _TileGroove - aaT, _TileInset + _TileGroove + aaT, fd));
                    half panel = inPanel - groove;
                    // The groove's two far sides catch light, the near two sit in shade - reads as cut in.
                    half lit = step(fp.x + fp.y, 0.0);
                    half isTop = step(0.5, abs(i.normalWS.y));
                    half amt = lerp(lTiles.y, lTiles.x, isTop) * (1.0h - step(0.5, an.z));
                    half3 grooveCol = lerp(_TileGrooveColor.rgb, _TileLipColor.rgb, lit * 0.5h);
                    half3 m = lerp(half3(1, 1, 1), grooveCol, groove);
                    m = lerp(m, _TilePanelColor.rgb, panel);
                    col *= lerp(half3(1, 1, 1), m, amt);
                }

                float along = max(e.x, e.y);
                float all = min(min(max(e.x, e.y), max(e.y, e.z)), max(e.x, e.z));
                float d = lerp(along, all, _EdgeAllAxes);
                float aa = fwidth(d) + 1e-4;
                half edge = 1.0h - smoothstep(lShape.z - aa, lShape.z + aa, d);
                col = lerp(col, lEdge.rgb * max(max(tint.r, tint.g), tint.b), edge * lEdge.a);

                // ---- depth cue: pale lip along the long edges of upward faces ----
                if (_DCFace2.x > 0.001h && !isRunner && n.y > 0.7)
                {
                    float3 aoL = abs(i.positionOS);
                    float latL = 0.5 - (abs(i.normalOS.x) > 0.5 ? aoL.y : aoL.x);
                    float aaL = fwidth(latL) + 1e-4;
                    half lip = (1.0h - smoothstep(_DCFace2.y - aaL, _DCFace2.y + aaL, latL)) * (1.0h - step(0.5, abs(i.normalOS.z)));
                    col = lerp(col, col * 1.45h + lTop * tint * 0.06h, lip * _DCFace2.x * smoothstep(0.7h, 0.9h, n.y));
                }

                // ---- toon checkpoint: cel band, moss, pen hatching, paper grain. Everything is measured across the leg
                // (object x / y - the cube's z runs along the leg) or in world space, never per cube, so a leg of many
                // cubes reads as one carved block. ----
                half toon = lTiles.z;
                bool toonLeg = toon > 0.001h && !isRunner;
                float toonUpCross = 0.0;   // 0 on a climbing leg: no "up" across its section
                float toonH = 0.0;         // height across the section: -0.5 bottom edge .. 0.5 top edge
                half toonTop = 0.0h;
                if (toonLeg)
                {
                    float3 upOS = normalize(mul((float3x3)unity_WorldToObject, float3(0, 1, 0)));
                    toonUpCross = saturate((1.0 - abs(upOS.z)) * 4.0 - 3.0);
                    toonH = dot(i.positionOS, upOS);
                    toonTop = smoothstep(0.6h, 0.8h, n.y);
                    half toonSide = (1.0h - toonTop) * (1.0h - smoothstep(0.6h, 0.8h, -n.y)) * (1.0h - step(0.5, abs(i.normalOS.z)));
                    float3 wp = i.positionWS;
                    float mBig = ToonNoise(wp * 0.33);
                    float mSmall = ToonNoise(wp * 1.6 + 11.3);
                    half3 t = col;
                    half lumTop = max(ToonLum(lTop * tint), 1e-3h);

                    // Lower band of each side face sits in a hard-edged cartoon shade (edge wobbles along the leg).
                    float bandAt = -0.14 + (mBig - 0.5) * 0.22;
                    float aaB = fwidth(toonH) + 1e-4;
                    half lower = (1.0h - smoothstep(bandAt - aaB, bandAt + aaB, toonH)) * toonSide * toonUpCross;
                    t *= lerp(1.0h, _ToonParams.y, lower);

                    // Moss: a ragged lip hanging over the top edge of the sides, and creeping in from the edges on top.
                    float3 latOS = normalize(cross(mul((float3x3)unity_WorldToObject, float3(0, 1, 0)), float3(0, 0, 1)));
                    float lat = 0.5 - abs(dot(i.positionOS, latOS));               // top face: distance to its long edge
                    float lipDepth = 0.07 + 0.16 * mBig + 0.07 * mSmall;
                    float aaM = fwidth(toonH) + 1e-4;
                    half mossSide = (1.0h - smoothstep(lipDepth - aaM, lipDepth + aaM, 0.5 - toonH)) * toonSide;
                    float creep = 0.03 + 0.2 * saturate(mBig * 1.6 - 0.55) + 0.05 * mSmall;
                    float aaL = fwidth(lat) + 1e-4;
                    half mossTop = (1.0h - smoothstep(creep - aaL, creep + aaL, lat)) * toonTop;
                    half moss = saturate(mossSide + mossTop) * toonUpCross * _ToonMoss.a;
                    half faceLight = saturate(ToonLum(t) / lumTop);
                    half3 mossCol = lerp(_ToonMossDark.rgb, _ToonMoss.rgb, smoothstep(0.35h, 0.9h, faceLight)) * max(max(tint.r, tint.g), tint.b);
                    t = lerp(t, mossCol, moss);

                    // Pen hatching on the darker faces (diagonal world-space strokes, broken into dashes).
                    half dark = 1.0h - saturate(ToonLum(t) / lumTop);
                    half hatch = ToonHatch(wp, _ToonParams.z, 1.0h) * smoothstep(0.3h, 0.55h, dark) * (1.0h - moss) * _ToonMossDark.a;
                    t = lerp(t, t * 0.62h, hatch);

                    // Paper grain.
                    t *= 1.0h + (mSmall - 0.5h) * _ToonLightDir.w;
                    col = lerp(col, t, toon);
                }

                // ---- toon checkpoint: the leg's line art (ToonLegDesigns.hlsl). u runs from the leg's start to its end over
                // every cube, v across the face; the design is chosen per leg and per face. Derivatives are taken here,
                // outside any branch. ----
                float tlU = _ToonLeg.x + i.positionOS.z;
                bool tlXFace = abs(i.normalOS.x) > 0.5;
                float tlV = tlXFace ? i.positionOS.y : i.positionOS.x;
                float tlAA = max(fwidth(tlU), fwidth(tlV)) + 1e-4;
                float tlCapAA = max(fwidth(i.positionOS.x), fwidth(i.positionOS.y)) + 1e-4;
                if (toonLeg && _ToonLeg.w > 0.5 && _ToonDesign.x > 0.001)
                {
                    float L = max(_ToonLeg.y, 1.0);
                    half dInk = 0.0h, dLight = 0.0h, dMoss = 0.0h;
                    float serial = floor(_ToonLeg.z + 0.5);
                    float wVar = lerp(0.8, 1.25, THash1(serial * 3.3 + 1.0));
                    float thick = 0.05 * wVar, thin = 0.022;
                    if (abs(i.normalOS.z) > 0.5)
                        ToonLegCap(i.positionOS.xy, thick, thin, tlCapAA, dInk, dLight);
                    else
                    {
                        float face = tlXFace ? (i.normalOS.x > 0 ? 0.0 : 1.0) : (i.normalOS.y > 0 ? 2.0 : 3.0);
                        int design = (int)fmod(serial * 7.0 + face * 5.0 + 3.0, 18.0);
                        ToonLegMarks(tlU, tlV, L, serial * 4.0 + face, design, thick, thin, tlAA, dInk, dLight, dMoss);
                        ToonLegEnds(tlU, L, thick, thin, tlAA, dInk, dLight);
                    }
                    half tintMaxD = max(max(tint.r, tint.g), tint.b);
                    half k = toon * _ToonDesign.x;
                    // Each leg is cut from a slightly different stone (warm / green / blue slate, same brightness).
                    int stone = (int)fmod(serial, 4.0);
                    half3 stoneTint = stone == 0 ? half3(1.0h, 1.0h, 1.0h) : stone == 1 ? half3(1.07h, 0.99h, 0.9h)
                                    : stone == 2 ? half3(0.94h, 1.04h, 0.98h) : half3(0.95h, 0.97h, 1.08h);
                    col *= lerp(half3(1.0h, 1.0h, 1.0h), stoneTint, k);
                    col = lerp(col, _ToonHighlight.rgb * tintMaxD, saturate(dLight) * _ToonDesign.z * k);
                    col = lerp(col, _ToonMoss.rgb * tintMaxD, saturate(dMoss) * _ToonDesign.w * k);
                    col = lerp(col, lerp(_ToonInk.rgb, col * 0.3h, 0.2h), saturate(dInk) * _ToonDesign.y * k);
                }

                // ---- pixel checkpoint: the Minecraft-like leg texture (path and crossed legs only) ----
                half pixAmt = PixAt(GetNormalizedScreenSpaceUV(i.positionCS));
                if (pixAmt > 0.001h && _LTRole < 1.5h)
                    col = lerp(col, PixLegColor(i.positionOS, i.normalOS, n, i.positionWS, col, lLeft, lRight, tint), pixAmt);

                // ---- storybook checkpoint: every leg a pen-and-watercolour building (roof, walls, windows, bridges) ----
                half sbAmt = SbAt(GetNormalizedScreenSpaceUV(i.positionCS));
                if (sbAmt > 0.001h && _LTRole < 1.5h)
                    col = lerp(col, SbLegColor(i.positionOS, i.normalOS, n, i.positionWS, col, lLeft, lRight, tint, _ToonLeg), sbAmt);

                // ---- flower checkpoint: every leg a strip of painted meadow (flowers on top, moss sides, flower drips) ----
                half flAmt = FlAt(GetNormalizedScreenSpaceUV(i.positionCS));
                if (flAmt > 0.001h && _LTRole < 1.5h)
                    col = lerp(col, FlLegColor(i.positionOS, i.normalOS, n, i.positionWS, col, lLeft, lRight, tint, _ToonLeg), flAmt);

                // ---- voxel checkpoint: every leg a pixel slab bridge on bricks, the slab dripping over the bricks ----
                half vxAmt = VxAt(GetNormalizedScreenSpaceUV(i.positionCS));
                if (vxAmt > 0.001h && _LTRole < 1.5h)
                    col = lerp(col, VxLegColor(i.positionOS, i.normalOS, n, i.positionWS, col, lLeft, lRight, tint, _ToonLeg), vxAmt);

                // ---- pencil checkpoint: every leg drawn in graphite on paper (also the obstacles and the runner cube) ----
                half pcAmt = PcAt(GetNormalizedScreenSpaceUV(i.positionCS));
                if (pcAmt > 0.001h)
                {
                    half3 pcCol = PcLegColor(i.positionOS, i.normalOS, n, i.positionWS, _ToonLeg, _LTRole);
                    // The obstacle runway (only those legs carry stripes in the pencil level) is a graphite-washed leg, so the
                    // danger section reads dark; the hatching and sketch strokes stay visible through the wash.
                    if (_LTRole < 1.5h && _LegStageSel2.w > 0.001) pcCol *= 0.34h;
                    col = lerp(col, pcCol, pcAmt);
                }

                // ---- space checkpoint: every leg a strip of oil-painted spaceship (deck, portholes / glass corridor, engines) ----
                half spAmt = SpAt(GetNormalizedScreenSpaceUV(i.positionCS));
                if (spAmt > 0.001h && _LTRole < 1.5h)
                    col = lerp(col, SpLegColor(i.positionOS, i.normalOS, n, i.positionWS, col, lLeft, lRight, tint, _ToonLeg), spAmt);

                // ---- spice route checkpoint: every leg a painted strip of Indian (first half) or Arabian (second half) craft ----
                half srAmt = SrAt(GetNormalizedScreenSpaceUV(i.positionCS));
                if (srAmt > 0.001h && _LTRole < 1.5h)
                    col = lerp(col, SrLegColor(i.positionOS, i.normalOS, n, i.positionWS, col, lLeft, lRight, tint, _ToonLeg), srAmt);

                // ---- in-level leg stage, after the leg textures: pick one hue band (moss, roofs) and move it; stripes on
                // obstacle runways; a pale line round the first cube of a new stage. All zero = nothing. ----
                if (_LTRole < 1.5h)
                {
                    if (_LegStageSel.w > 0.001)
                    {
                        float3 hsv = LsRgbToHsv(saturate(col));
                        float dh = abs(frac(hsv.x - _LegStageSel.x + 0.5) - 0.5);
                        half pick = (half)((1.0 - smoothstep(_LegStageSel.y * 0.7, _LegStageSel.y, dh)) * smoothstep(0.08, 0.2, hsv.y)) * (half)_LegStageSel.w;
                        float3 moved = float3(frac(hsv.x + _LegStageSel.z), saturate(hsv.y * _LegStageSel2.x), hsv.z * _LegStageSel2.y);
                        col = lerp(col, (half3)LsHsvToRgb(moved), pick);
                    }
                    if (_LegStageSel2.w > 0.001)
                    {
                        float st = frac(dot(i.positionWS, float3(0.55, 0.55, 0.55)));
                        float aaSt = fwidth(st) + 1e-4;
                        half band = (half)(smoothstep(0.5 - aaSt, 0.5 + aaSt, st) * (1.0 - smoothstep(1.0 - aaSt * 2.0, 1.0, st)));
                        col *= 1.0h - band * (half)_LegStageSel2.w;
                    }
                    if (_LegStageSel2.z > 0.001)
                    {
                        float zl = abs(i.positionOS.z);
                        float aaZ = fwidth(zl) + 1e-4;
                        half lsLine = (half)(1.0 - smoothstep(0.07 - aaZ, 0.07 + aaZ, zl)) * (1.0h - step(0.5, abs(i.normalOS.z)));
                        col = lerp(col, lerp(col, half3(1, 1, 1), 0.65h) * 1.15h, lsLine * (half)_LegStageSel2.z);
                    }
                }

                // ---- form shading (FaceFormController, scene 3.11 only - zero everywhere else): after every leg texture, so each
                // theme keeps its own painting and only gains a gentle read of its planes. Top / lit side / shade side / foot
                // step down a little in brightness (never recoloured), the top edges get a thin lip, the side face a soft crease
                // under that lip and a darker foot. Long edges only - never across the joints between cubes. ----
                half fdOn = (half)_FDForm.x;
                if (fdOn > 0.001h && _LTRole < 1.5h)
                {
                    half wTop = smoothstep(0.55h, 0.85h, (half)n.y);
                    half wBot = smoothstep(0.55h, 0.85h, (half)(-n.y));
                    half wSide = saturate(1.0h - wTop - wBot);
                    half3 fdLw = half3(0.2126h, 0.7152h, 0.0722h);
                    half litIsRight = step(dot(lLeft, fdLw), dot(lRight, fdLw));
                    half litAmt = lerp(1.0h - s, s, litIsRight);                 // 1 on the lit side, 0 on the shade side
                    half mSide = lerp((half)_FDForm.y, (half)_FDLip2.y, litAmt);
                    half mTop = 1.0h + (half)_FDForm.w;
                    half mulFace = wTop * mTop + wSide * mSide + wBot * (half)_FDForm.z;
                    col *= lerp(1.0h, mulFace, fdOn);

                    half tintMaxF = max(max(tint.r, tint.g), max(tint.b, 1e-3h));
                    bool capF = abs(i.normalOS.z) > 0.5;
                    // Thin lip along the two long edges of a top face.
                    float3 aoF = abs(i.positionOS);
                    float latF = 0.5 - (abs(i.normalOS.x) > 0.5 ? aoF.y : aoF.x);
                    float aaF = fwidth(latF) + 1e-4;
                    half lipF = (1.0h - smoothstep(_FDLip2.x - aaF, _FDLip2.x + aaF, latF)) * (capF ? 0.0h : 1.0h) * wTop;
                    col = lerp(col, (half3)_FDLip.rgb * tintMaxF, lipF * (half)_FDLip.a * fdOn);
                    // Crease under the top edge and a darker foot on side faces whose cube has an axis standing up.
                    float3 upOS = float3(unity_ObjectToWorld._m10, unity_ObjectToWorld._m11, unity_ObjectToWorld._m12);
                    upOS = upOS / max(length(upOS), 1e-4);
                    if (abs(upOS.z) < 0.35 && wSide > 0.01h && !capF)
                    {
                        float hv = dot(i.positionOS, upOS);
                        float aaH = fwidth(hv) + 1e-4;
                        half crease = 1.0h - smoothstep(0.0, _FDEdge.y + aaH, 0.5 - hv);
                        half foot = 1.0h - smoothstep(0.0, _FDEdge.w + aaH, 0.5 + hv);
                        col *= 1.0h - (crease * (half)_FDEdge.x + foot * (half)_FDEdge.z) * wSide * fdOn;
                    }
                }

                // ---- cast shadow (the only thing the directional light does) ----
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 sc = i.shadowCoord;
                #else
                    float4 sc = TransformWorldToShadowCoord(i.positionWS);
                #endif
                Light mainLight = GetMainLight(sc);
                // Faces turned away from the sun sit "behind" their own cube in the shadow map; they keep
                // their flat face colour - only real shadows cast onto sun-facing faces are tinted.
                half facesSun = smoothstep(0.02h, 0.12h, dot(n, mainLight.direction));
                half inShadow = (1.0h - mainLight.shadowAttenuation) * lShadow.a * facesSun;
                col *= lerp(half3(1, 1, 1), lShadow.rgb, inShadow);

                // ---- screen-space ambient occlusion (URP renderer feature, when enabled) ----
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    float2 ssUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(ssUV);
                    col *= ao.indirectAmbientOcclusion;
                #endif

                // ---- grain ----
                if (lGrain.x > 0.0001h)
                {
                    float g = vnoise(i.positionWS * lGrain.y) * 0.65 + vnoise(i.positionWS * lGrain.y * 2.7 + 17.0) * 0.35;
                    half lum = dot(col, half3(0.299h, 0.587h, 0.114h));
                    half amt = lGrain.x * lerp(1.0h, 1.6h - lum, lGrain.z);
                    col *= 1.0h + (g - 0.5h) * 2.0h * amt;
                }

                // ---- point-light bleed (e.g. the yellow totem on grey stone) ----
                half3 bleed = 0;
                #if defined(_ADDITIONAL_LIGHTS)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS;
                    inputData.normalWS = n;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                        half ndl = saturate((dot(n, l.direction) + lGrain.w) / (1.0h + lGrain.w));
                        bleed += l.color * (l.distanceAttenuation * ndl);
                    LIGHT_LOOP_END
                }
                #endif
                // Light adds onto the stone in the stone's own hue, plus a little of the light's own colour.
                col += bleed * lFog2.w * (col * 0.75h + 0.25h * lTop);

                // ---- boost glow: PathDepthShading writes emission = tint * glowAmount ----
                half tintMax = max(max(tint.r, tint.g), max(tint.b, 1e-3h));
                half glowAmount = max(max(_EmissionColor.r, _EmissionColor.g), _EmissionColor.b) / tintMax;
                half boost = saturate(glowAmount - lEmis.a) * lGlow.a;
                // Scales each face by its own colour so top / left / right keep their contrast while lit.
                col = col * (1.0h + boost * lGlow.rgb) + lGlow.rgb * (boost * 0.12h);
                col += lEmis.rgb;

                // ---- toon checkpoint: ink outline along the leg's long edges and round the end caps (never across the
                // joints between cubes), with a warm sunlit lip just inside it on the top. Before the fog, so far track
                // still dissolves. ----
                if (toonLeg && lTiles.w > 0.001h)
                {
                    bool cap = abs(i.normalOS.z) > 0.5;
                    float dEdge = cap ? min(e.x, e.y) : max(e.x, e.y);
                    float wInk = _ToonParams.x * lTiles.w;
                    float aaI = fwidth(dEdge) + 1e-4;
                    half ink = 1.0h - smoothstep(wInk - aaI, wInk + aaI, dEdge);
                    half litLip = toonTop * (1.0h - smoothstep(wInk * 2.8 - aaI, wInk * 2.8 + aaI, dEdge)) * (1.0h - ink);
                    col = lerp(col, _ToonHighlight.rgb * max(max(tint.r, tint.g), tint.b), litLip * _ToonHighlight.a * toon);
                    col = lerp(col, _ToonInk.rgb, ink * _ToonInk.a * toon);
                }

                // ---- height fog below the runner, coloured like the backdrop behind it ----
                float below = _PropFogRefY - i.positionWS.y;
                half hf = saturate((below - lFog1.x) / max(0.01, lFog1.y - lFog1.x));
                hf = hf * hf * (3.0h - 2.0h * hf);
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                half sf = lFog1.w > 0.001h ? saturate(1.0h - suv.y / lFog1.w) : 0.0h;
                sf = sf * sf * lFog2.x;
                half fog = saturate(max(hf * lFog1.z, sf));
                half3 fogCol = lerp(lFogBot, lFogTop, saturate(suv.y));
                // Optionally end in exactly the ChasmFog backdrop behind this pixel, and take its layers
                // too (the rock backdrop scenes), so fully fogged geometry is indistinguishable from it.
                half chasmOn = _ChasmFogView.w;
                fogCol = lerp(fogCol, ChasmFogColor(suv), lFog2.y * chasmOn);
                half chasmFog = saturate(ChasmFogAmount(i.positionWS, suv)) * chasmOn * lFog2.z;
                fog = 1.0h - (1.0h - fog) * (1.0h - chasmFog);
                col = lerp(col, fogCol, fog);

                // ---- which face is the runner on? Lights that face of the rail around the runner ----
                // After the fog, so the lane stays vivid. The face under the runner glows with an outline
                // along its long edges; the faces next to it dim a little; and on a face the camera cannot
                // see, the edge it shares with a visible face glows instead.
                half runnerOn = _RunnerGlowStrength * _PropRunnerP.w;
                if (runnerOn > 0.002h)
                {
                    float3 rn = _PropRunnerN.xyz;
                    float3 rel = i.positionWS - _PropRunnerP.xyz;
                    float h = dot(rel, rn);                         // height above the runner's face plane
                    float dist = length(rel - rn * h);              // distance along that plane
                    float rad = max(_PropRunnerN.w, 0.01);
                    half fall = 1.0h - smoothstep(rad * 0.45, rad, dist);

                    half fd = dot(n, rn);
                    half onFace = smoothstep(0.9h, 0.99h, fd) * (1.0h - smoothstep(0.03h, 0.12h, abs(h)));
                    half perp = 1.0h - smoothstep(0.1h, 0.3h, abs(fd));
                    half edgeBand = perp * smoothstep(-0.34h, -0.06h, h) * (1.0h - smoothstep(0.03h, 0.09h, h));

                    // Bright line along the two long edges of the lit face (not around every cube joint).
                    float3 ao = abs(i.positionOS);
                    float lateral = 0.5 - (ao.x > 0.45 ? ao.y : ao.x);
                    float aaR = fwidth(lateral) + 1e-4;
                    half rim = 1.0h - smoothstep(0.05 - aaR, 0.09 + aaR, lateral);

                    half lit = onFace * fall * runnerOn;
                    half edgeLit = edgeBand * fall * runnerOn * _RunnerGlowEdge;
                    half other = (1.0h - onFace) * (1.0h - edgeBand) * fall * runnerOn;

                    col *= 1.0h - _RunnerGlowDim * other;
                    col = lerp(col, _RunnerGlowColor.rgb, lit * 0.62h);
                    col = lerp(col, lerp(_RunnerGlowColor.rgb, half3(1, 1, 1), 0.55h), lit * rim * 0.9h);
                    col = lerp(col, lerp(_RunnerGlowColor.rgb, half3(1, 1, 1), 0.2h), edgeLit);
                }

                // ---- depth cues around the runner (after the fog, so they stay readable) ----
                half contactOn = _DCRunnerP.w;
                half railOn = _DCRail.a;
                if (contactOn + railOn > 0.002h && _LTRole < 2.5h)
                {
                    float3 rn = _DCRunnerN.xyz;
                    float3 rf = _DCRunnerF.xyz;
                    float3 ra = cross(rn, rf);
                    float3 rel = i.positionWS - _DCRunnerP.xyz;
                    float h = dot(rel, rn);          // height above the ridden face's plane
                    float along = dot(rel, rf);      // ahead of the runner along the leg
                    float lat = dot(rel, ra);        // across the face
                    half fd = dot(n, rn);
                    half vis = saturate(_DCRunnerN.w);
                    half3 cc = _DCContact.rgb;
                    float cs = max(_DCContact.a, 0.05);

                    if (isRunner)
                    {
                        // The runner's own edges where it stands on the rail take the contact colour.
                        half standing = 1.0h - smoothstep(0.3h, 0.6h, abs(fd));
                        half low = 1.0h - smoothstep(0.05, 0.22, h);
                        col = lerp(col, cc, saturate(standing * low * _DCFace2.w * contactOn));
                    }
                    else
                    {
                        half onFace = smoothstep(0.9h, 0.98h, fd) * (1.0h - smoothstep(0.06h, 0.14h, abs(h)));
                        // Faces next to the ridden one, the strip along the edge they share with it.
                        half perp = 1.0h - smoothstep(0.15h, 0.35h, abs(fd));
                        half besideLeg = smoothstep(0.2, 0.3, abs(lat)) * (1.0h - smoothstep(0.75, 0.85, abs(lat)));
                        float dEdge = -h;
                        float aaE = fwidth(dEdge) + 1e-4;
                        half edgeLine = perp * besideLeg * step(-0.03, dEdge) * (1.0h - smoothstep(0.07 - aaE, 0.07 + aaE, dEdge));

                        // Contact: a soft square on the face, or a short bright stretch of the edge when the face is hidden.
                        float q = max(abs(along), abs(lat));
                        half sq = 1.0h - smoothstep(cs * 0.72, cs, q);
                        half seg = 1.0h - smoothstep(cs * 0.5, cs * 1.1, abs(along));
                        half contact = (onFace * sq * vis + 0.55h * edgeLine * seg * (1.0h - vis)) * contactOn;

                        // Rail: from just behind the runner to the leg's far end, closed by a crossbar.
                        float railLen = max(_DCRunnerF.w, 0.0);
                        float aaA = fwidth(along) + 1e-4;
                        half alongMask = smoothstep(-_DCRail2.y, 0.0, along) * (1.0h - smoothstep(railLen - aaA, railLen + aaA, along));
                        half ease = lerp(0.45h, 1.0h, exp(-max(along, 0.0) / 9.0));
                        half nearEnd = smoothstep(railLen - _DCRail2.w, railLen, along);
                        float w = _DCRail2.x;
                        float aaLat = fwidth(lat) + 1e-4;
                        half railLine = 1.0h - smoothstep(w - aaLat, w + aaLat, abs(lat));
                        float barAt = railLen - 0.14;
                        half bar = (1.0h - smoothstep(w - aaA, w + aaA, abs(along - barAt))) * (1.0h - smoothstep(0.3 - aaLat, 0.3 + aaLat, abs(lat)))
                                 * step(0.6, railLen);
                        // Only on a face the camera sees: a line ahead along the leg's edge while the runner is
                        // behind / under the leg read as a second path (user, 3.7) - there the contact mark,
                        // the trail's fin and the runner's cape show where it is instead.
                        half railFace = onFace * vis * vis * (railLine * alongMask * lerp(ease, 1.0h, nearEnd) + bar);
                        half rail = saturate(railFace * railOn * (1.0h + 0.8h * _DCRail2.z));
                        half3 railCol = lerp(_DCRail.rgb, half3(1, 1, 1), 0.5h * _DCRail2.z);

                        col = lerp(col, cc, saturate(contact) * 0.6h);
                        col = lerp(col, railCol, rail * 0.85h);
                    }
                }

                half alpha = _Surface > 0.5h ? _BaseColor.a : 1.0h;
                // See-through cubes: skip the end faces between neighbouring cubes of a beam.
                if (_Surface > 0.5h && _BaseColor.a < 0.99h && abs(i.normalOS.z) > 0.5)
                    discard;
                return half4(col, alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { clip(0.5h - _ZoneGone * _ZoneGoneShadow); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v) { UNITY_SETUP_INSTANCE_ID(v); Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
