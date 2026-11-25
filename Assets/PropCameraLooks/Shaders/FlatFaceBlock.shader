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
Shader "PropLooks/FlatFaceBlock"
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

        [HideInInspector] _ZoneGone ("Left behind by a zone change (set per renderer)", Float) = 0
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
        CBUFFER_END
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
            #include "Assets/Shaders/ChasmFog/ChasmFog.hlsl"

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

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                // Left behind by a zone change (PortalZoneController): gone, as in MonumentBlock.
                half oldSide = step(_ZoneWipeX, i.positionCS.x / _ScreenParams.x);
                if (_ZoneGone > 0.5h && oldSide < 0.5h) discard;

                float3 n = normalize(i.normalWS);

                // ---- face colour, relative to the camera ----
                float3 camRight = normalize(UNITY_MATRIX_V[0].xyz);
                float3 flat = n - float3(0, n.y, 0);
                float side = dot(flat, camRight) / max(length(flat), 1e-4);
                half s = _SideSoftness > 0.001h ? smoothstep(-_SideSoftness, _SideSoftness, side) : step(0.0, side);
                half3 sideCol = lerp(_LeftColor.rgb, _RightColor.rgb, s);
                half up = pow(saturate(n.y), _TopSharpness);
                half down = pow(saturate(-n.y), _TopSharpness);
                half3 col = sideCol;
                col = lerp(col, _TopColor.rgb, up);
                col = lerp(col, _BottomColor.rgb, down);

                // ---- per-cube tint from PathDepthShading ----
                half3 tint = _BaseColor.rgb;
                col *= tint;

                // ---- seams between cubes (local z ends) and the rim along the beam ----
                float3 e = 0.5 - abs(i.positionOS);
                float aaS = fwidth(e.z) + 1e-4;
                // Only on faces that run along the beam (not the end caps themselves).
                half seam = (1.0h - smoothstep(_SeamWidth - aaS, _SeamWidth + aaS, e.z)) * (1.0h - step(0.5, abs(i.normalOS.z)));
                col *= lerp(half3(1, 1, 1), _SeamColor.rgb, seam * _SeamStrength);

                // ---- carved square panel on each face (the walkway in the orange canyon reference) ----
                if (_TileTop + _TileSide > 0.001h)
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
                    half amt = lerp(_TileSide, _TileTop, isTop) * (1.0h - step(0.5, an.z));
                    half3 grooveCol = lerp(_TileGrooveColor.rgb, _TileLipColor.rgb, lit * 0.5h);
                    half3 m = lerp(half3(1, 1, 1), grooveCol, groove);
                    m = lerp(m, _TilePanelColor.rgb, panel);
                    col *= lerp(half3(1, 1, 1), m, amt);
                }

                float along = max(e.x, e.y);
                float all = min(min(max(e.x, e.y), max(e.y, e.z)), max(e.x, e.z));
                float d = lerp(along, all, _EdgeAllAxes);
                float aa = fwidth(d) + 1e-4;
                half edge = 1.0h - smoothstep(_EdgeWidth - aa, _EdgeWidth + aa, d);
                col = lerp(col, _EdgeColor.rgb * max(max(tint.r, tint.g), tint.b), edge * _EdgeStrength);

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
                half inShadow = (1.0h - mainLight.shadowAttenuation) * _ShadowStrength * facesSun;
                col *= lerp(half3(1, 1, 1), _ShadowColor.rgb, inShadow);

                // ---- screen-space ambient occlusion (URP renderer feature, when enabled) ----
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    float2 ssUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(ssUV);
                    col *= ao.indirectAmbientOcclusion;
                #endif

                // ---- grain ----
                if (_GrainStrength > 0.0001h)
                {
                    float g = vnoise(i.positionWS * _GrainScale) * 0.65 + vnoise(i.positionWS * _GrainScale * 2.7 + 17.0) * 0.35;
                    half lum = dot(col, half3(0.299h, 0.587h, 0.114h));
                    half amt = _GrainStrength * lerp(1.0h, 1.6h - lum, _GrainDarkBias);
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
                        half ndl = saturate((dot(n, l.direction) + _PointLightWrap) / (1.0h + _PointLightWrap));
                        bleed += l.color * (l.distanceAttenuation * ndl);
                    LIGHT_LOOP_END
                }
                #endif
                // Light adds onto the stone in the stone's own hue, plus a little of the light's own colour.
                col += bleed * _PointLightStrength * (col * 0.75h + 0.25h * _TopColor.rgb);

                // ---- boost glow: PathDepthShading writes emission = tint * glowAmount ----
                half tintMax = max(max(tint.r, tint.g), max(tint.b, 1e-3h));
                half glowAmount = max(max(_EmissionColor.r, _EmissionColor.g), _EmissionColor.b) / tintMax;
                half boost = saturate(glowAmount - _GlowBase) * _GlowGain;
                // Scales each face by its own colour so top / left / right keep their contrast while lit.
                col = col * (1.0h + boost * _GlowTint.rgb) + _GlowTint.rgb * (boost * 0.12h);
                col += _EmissiveColor.rgb;

                // ---- height fog below the runner, coloured like the backdrop behind it ----
                float below = _PropFogRefY - i.positionWS.y;
                half hf = saturate((below - _FogStart) / max(0.01, _FogEnd - _FogStart));
                hf = hf * hf * (3.0h - 2.0h * hf);
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                half sf = _ScreenFogHeight > 0.001h ? saturate(1.0h - suv.y / _ScreenFogHeight) : 0.0h;
                sf = sf * sf * _ScreenFogStrength;
                half fog = saturate(max(hf * _FogMax, sf));
                half3 fogCol = lerp(_FogBottomColor.rgb, _FogTopColor.rgb, saturate(suv.y));
                // Optionally end in exactly the ChasmFog backdrop behind this pixel, and take its layers
                // too (the rock backdrop scenes), so fully fogged geometry is indistinguishable from it.
                half chasmOn = _ChasmFogView.w;
                fogCol = lerp(fogCol, ChasmFogColor(suv), _FogColorFromChasm * chasmOn);
                half chasmFog = saturate(ChasmFogAmount(i.positionWS, suv)) * chasmOn * _ChasmFogMix;
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
