// Flat, non-PBR block shader for the TrialMonument scene (Monument Valley look).
//
//   - Flat base colour (_BaseColor, also driven per cube by PathDepthShading's property block).
//   - One directional light: faces turned to it keep the base colour, faces turned away and
//     areas in shadow are tinted towards a cool blue (_ShadowColor) instead of going black.
//   - A thin highlight along the long edges of each beam (object-space, so it works on the
//     unit cube mesh the path uses) gives the polished, bevelled feel without extra geometry.
//   - Carries the URP "Lit"-style blend properties (_SrcBlend/_DstBlend/_ZWrite/_Surface) so
//     PathDepthShading.ConfigureAsFade can turn a copy of it into the see-through crossed-leg
//     material exactly as it does for URP/Lit.
Shader "TrialMonument/MonumentBlock"
{
    Properties
    {
        _BaseColor ("Base Colour", Color) = (0.78, 0.92, 0.95, 1)
        [HideInInspector] _Color ("Colour (legacy)", Color) = (1, 1, 1, 1)
        [HideInInspector] _EmissionColor ("Emission (ignored)", Color) = (0, 0, 0, 1)

        [Header(Light and Shadow)]
        _ShadowColor ("Shadow Tint (cool blue)", Color) = (0.50, 0.62, 0.84, 1)
        _LightWrap ("Light Wrap", Range(0, 1)) = 0.15
        _TopBoost ("Top Face Boost", Range(0, 0.5)) = 0.08
        _ShadowStrength ("Cast Shadow Strength", Range(0, 1)) = 0.85

        [Header(Edge Highlight)]
        _EdgeColor ("Edge Colour", Color) = (1, 1, 1, 1)
        _EdgeWidth ("Edge Width (object units)", Range(0.001, 0.2)) = 0.035
        _EdgeStrength ("Edge Strength", Range(0, 1)) = 0.45
        [Toggle] _EdgeAllAxes ("Edges On All Axes (off = only along the beam, no seams between cubes)", Float) = 0

        [Header(Portal Masking)]
        [IntRange] _StencilRef ("Stencil Ref", Range(0, 255)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Compare (Always = off, NotEqual + Ref 1 = hidden inside a portal opening)", Float) = 8

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

        // Zone palette (PortalZoneController, TrialMonument2). Globals, so they recolour every block
        // material at once. _ZoneBlend 0 (the default, nobody sets it) = the material as authored.
        half4 _ZoneShadowColor;
        half4 _ZoneEdgeColor;
        half _ZoneBlend;
        // A line sweeping across the screen turns the world from the old palette to the new one: pixels
        // right of _ZoneWipeX (screen 0..1) still show the old one. 2 = the sweep is over / not running.
        half4 _ZoneOldShadowColor;
        half4 _ZoneOldEdgeColor;
        half4 _ZoneOldMul;   // old / new, per channel: maps the new-palette cube colours back to the old look
        float _ZoneWipeX;
        half _ZoneGoneShadow;   // 1 once the sweep is over: left-behind blocks stop casting shadows too

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _Color;
            half4 _EmissionColor;
            half4 _ShadowColor;
            half _LightWrap;
            half _TopBoost;
            half _ShadowStrength;
            half4 _EdgeColor;
            half _EdgeWidth;
            half _EdgeStrength;
            half _EdgeAllAxes;
            half _Surface;
            half _StencilRef;
            half _StencilComp;
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
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);

                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 sc = i.shadowCoord;
                #else
                    float4 sc = TransformWorldToShadowCoord(i.positionWS);
                #endif
                Light light = GetMainLight(sc);
                half shadow = lerp(1.0h, light.shadowAttenuation, _ShadowStrength);

                // Wrapped Lambert, then banded by the shadow: 1 = full base colour, 0 = cool shade.
                half ndl = saturate((dot(n, light.direction) + _LightWrap) / (1.0h + _LightWrap));
                half lit = ndl * shadow;
                half oldSide = step(_ZoneWipeX, i.positionCS.x / _ScreenParams.x);
                // Track and doors of the zone just left vanish as the sweep line passes them.
                if (_ZoneGone > 0.5h && oldSide < 0.5h) discard;
                half3 shadowTint = lerp(_ShadowColor.rgb, lerp(_ZoneShadowColor.rgb, _ZoneOldShadowColor.rgb, oldSide), _ZoneBlend);
                half3 edgeTint = lerp(_EdgeColor.rgb, lerp(_ZoneEdgeColor.rgb, _ZoneOldEdgeColor.rgb, oldSide), _ZoneBlend);
                half3 shade = lerp(shadowTint, half3(1, 1, 1), lit);
                shade += _TopBoost * saturate(n.y) * shadow;

                half3 baseTint = _BaseColor.rgb * lerp(half3(1, 1, 1), _ZoneOldMul.rgb, oldSide * _ZoneBlend);
                half3 col = baseTint * shade;

                // Edge highlight. Distance to the nearest edge running along the beam (local z);
                // with _EdgeAllAxes on, every cube edge counts.
                float3 e = 0.5 - abs(i.positionOS);
                float along = max(e.x, e.y);
                float all = min(min(max(e.x, e.y), max(e.y, e.z)), max(e.x, e.z));
                float d = lerp(along, all, _EdgeAllAxes);
                float aa = fwidth(d) + 1e-4;
                half edge = 1.0h - smoothstep(_EdgeWidth - aa, _EdgeWidth + aa, d);
                col = lerp(col, edgeTint * max(max(_BaseColor.r, _BaseColor.g), _BaseColor.b) * 1.15h, edge * _EdgeStrength);

                half alpha = _Surface > 0.5h ? _BaseColor.a : 1.0h;
                // See-through cubes: skip the end faces between neighbouring cubes of a beam
                // (local +/-z), which would otherwise show through as a zig-zag inside the leg.
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
