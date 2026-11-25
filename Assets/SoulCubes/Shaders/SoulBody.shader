// Soul cubes: the character's body - a (bevelled) unit cube dressed in its own object space, so the clothes roll with it.
//   head / fur above _ShirtTop, shirt down to _PantsTop, pants below (each band can be switched off by moving it out of
//   -0.5..0.5); optional stripes on shirt / pants, a belt with a buckle, a V collar and buttons on the front (+Z, the
//   running direction), a leg seam + cuffs on the pants, a belly patch, and a thin darker outline on the cube's edges.
// Toned like the game's path (SoulTone), so it sits in every checkpoint look.
Shader "SoulCubes/SoulBody"
{
    Properties
    {
        _HeadColor ("Head / Fur", Color) = (0.98, 0.82, 0.62, 1)
        _ShirtColor ("Shirt", Color) = (0.93, 0.42, 0.3, 1)
        _PantsColor ("Pants", Color) = (0.25, 0.38, 0.62, 1)
        _StripeColor ("Stripes", Color) = (1, 0.92, 0.78, 1)
        _BeltColor ("Belt", Color) = (0.36, 0.22, 0.14, 1)
        _BuckleColor ("Buckle", Color) = (1, 0.8, 0.3, 1)
        _ButtonColor ("Buttons", Color) = (1, 0.95, 0.85, 1)
        _BellyColor ("Belly patch", Color) = (1, 0.93, 0.85, 1)
        _ShirtTop ("Shirt top (y)", Range(-0.6, 0.6)) = -0.08
        _PantsTop ("Pants top (y)", Range(-0.6, 0.6)) = -0.3
        _CollarDepth ("Front V collar depth", Range(0, 0.3)) = 0.08
        _ShirtStripes ("Shirt stripes", Range(0, 1)) = 0
        _PantsStripes ("Pants stripes", Range(0, 1)) = 0
        _StripeFreq ("Stripes per unit", Range(1, 30)) = 9
        _VerticalStripes ("Stripes run vertically", Range(0, 1)) = 0
        _BeltWidth ("Belt width", Range(0, 0.12)) = 0
        _Buttons ("Buttons", Range(0, 1)) = 0
        _LegGap ("Leg seam", Range(0, 1)) = 1
        _Belly ("Belly patch", Range(0, 1)) = 0
        _BellyY ("Belly centre (y)", Range(-0.5, 0.5)) = -0.2
        _BellySize ("Belly half size", Vector) = (0.24, 0.18, 0, 0)
        _EdgeDarken ("Edge outline", Range(0, 1)) = 0.35
        _TopShade ("Top tone", Range(0, 1.5)) = 1
        _LitShade ("Lit side tone", Range(0, 1.5)) = 0.86
        _ShadeShade ("Shaded side tone", Range(0, 1.5)) = 0.66
        _BottomShade ("Bottom tone", Range(0, 1.5)) = 0.5
        _Brightness ("Brightness", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _HeadColor, _ShirtColor, _PantsColor, _StripeColor, _BeltColor, _BuckleColor, _ButtonColor, _BellyColor;
            float _ShirtTop, _PantsTop, _CollarDepth, _ShirtStripes, _PantsStripes, _StripeFreq, _VerticalStripes;
            float _BeltWidth, _Buttons, _LegGap, _Belly, _BellyY;
            float4 _BellySize;
            half _EdgeDarken, _TopShade, _LitShade, _ShadeShade, _BottomShade, _Brightness;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "SoulBody"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "SoulCommon.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 local      : TEXCOORD0;
                float3 normalOS   : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.local = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = i.local;
                float3 no = normalize(i.normalOS);
                float3 ao = abs(no);
                bool side = ao.y < max(ao.x, ao.z);
                bool front = side && no.z > 0.7;
                bool frontBack = side && ao.z > 0.7;
                bool bottom = no.y < -0.7;

                // bands (the V collar dips the shirt line on the front)
                float shirtTop = _ShirtTop;
                if (front) shirtTop -= _CollarDepth * saturate(1.0 - abs(d.x) / 0.2);
                half shirt = SoulFill(d.y - shirtTop);
                half pants = SoulFill(d.y - _PantsTop);

                // stripes: horizontal round the body, or vertical on the sides
                float along = _VerticalStripes > 0.5 ? (ao.x > 0.7 ? d.z : d.x) : d.y;
                half stripe = SoulStripe(along * _StripeFreq);

                half3 col = _HeadColor.rgb;
                half3 shirtCol = lerp(_ShirtColor.rgb, _StripeColor.rgb, stripe * _ShirtStripes);
                half3 pantsCol = lerp(_PantsColor.rgb, _StripeColor.rgb, stripe * _PantsStripes);
                col = lerp(col, shirtCol, shirt);
                col = lerp(col, pantsCol, pants);

                // a soft collar shadow just under the shirt line
                col *= 1.0 - 0.14 * shirt * (1.0 - smoothstep(0.0, 0.035, shirtTop - d.y)) * step(-0.5, _ShirtTop) * step(_ShirtTop, 0.5);

                // belly patch on the front (fur characters)
                if (front && _Belly > 0)
                {
                    float2 q = abs(float2(d.x, d.y - _BellyY)) - _BellySize.xy + 0.06;
                    float rr = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.06;
                    col = lerp(col, _BellyColor.rgb, SoulFill(rr) * _Belly);
                }

                // pants: a seam between the legs on the front, back and bottom, and slightly darker cuffs
                if (_LegGap > 0 && _PantsTop > -0.5)
                {
                    half legTop = _PantsTop - 0.1;
                    half seam = (frontBack || bottom) ? SoulFill(abs(d.x) - 0.012) : 0;
                    if (frontBack) seam *= SoulFill(d.y - legTop);
                    col = lerp(col, col * 0.45, seam * _LegGap * pants);
                    if (side) col *= lerp(1.0, 0.82, SoulFill(d.y + 0.44) * pants * _LegGap);
                }

                // belt + buckle
                if (_BeltWidth > 0 && side)
                {
                    half belt = SoulFill(abs(d.y - _PantsTop) - _BeltWidth * 0.5);
                    col = lerp(col, _BeltColor.rgb, belt);
                    if (front)
                    {
                        float2 b = abs(float2(d.x, d.y - _PantsTop)) - float2(0.055, _BeltWidth * 0.62);
                        half buckle = SoulFill(max(b.x, b.y));
                        half hole = SoulFill(max(b.x + 0.025, b.y + 0.022));
                        col = lerp(col, _BuckleColor.rgb, buckle * (1.0 - hole));
                    }
                }

                // buttons down the front of the shirt
                if (front && _Buttons > 0)
                {
                    float top = shirtTop - 0.07, bot = max(_PantsTop, -0.5) + 0.06;
                    float pitch = 0.11;
                    float k = clamp(round((top - d.y) / pitch), 0.0, floor((top - bot) / pitch));
                    float by = top - k * pitch;
                    half btn = SoulFill(length(float2(d.x, d.y - by)) - 0.022) * step(bot - 0.02, by);
                    col = lerp(col, _ButtonColor.rgb, btn * _Buttons * shirt * (1.0 - pants));
                }

                // thin darker outline along the cube's edges
                float3 e = 0.5 - abs(d);
                float edgeD = min(min(max(e.x, e.y), max(e.y, e.z)), max(e.x, e.z));
                col *= 1.0 - _EdgeDarken * (1.0 - smoothstep(0.012, 0.03, edgeD));

                col *= SoulTone(normalize(i.normalWS), _TopShade, _LitShade, _ShadeShade, _BottomShade) * _Brightness;
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #define SOUL_SHADOW_PASS
            #include "SoulPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #include "SoulPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDN
            #include "SoulPasses.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
