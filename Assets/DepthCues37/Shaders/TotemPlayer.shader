// MainScene prop camera 3.7 only: the runner as the yellow Monument Valley eye block (Left_5 (15)'s design,
// TrialMonument/TotemBlock left untouched). Same signed-distance designs, but laid out in the cube's OWN frame, because
// the runner tumbles: +Z = the way it is running (the eye), +Y = away from the rail (its head), X = its sides.
//
//   Eye face    the eye socket, on the face PlayerTotem turned the eye to (_EyeFace: the face most turned to the
//               camera of front / sides / top); the 3D eye / pupil discs sit on it
//   Sides (X)   nested V lines whose point is at the rail side - the block's "feet"
//   Back (-Z)   a rivet (the cape hangs over it)
//   Top (+Y)    nested squares; every side face has an accent band along its top edge - the block's "head"
//   Bottom      plain
//
// Shading is the path's: one flat tone per face relative to the camera - faces pointing up brightest, the lit side,
// the shade side, the bottom darkest (_DCPlayer.x picks which camera side is lit, like the checkpoint's path) - so the
// cube reads as a solid in the same light as the rail. The edges standing on the rail take the contact colour
// (DepthCueController's _DCRunnerP / _DCRunnerN / _DCContact / _DCFace2.w), like the old runner.
// _BlockIndex 0 = a plain part (no design) with the same shading.
Shader "DepthCues37/TotemPlayer"
{
    Properties
    {
        [IntRange] _BlockIndex ("Design (1 eye block, 0 plain part)", Range(0, 1)) = 1

        [Header(Colours)]
        _BaseColor ("Block Colour", Color) = (0.96, 0.77, 0.18, 1)
        _LineColor ("Line Colour", Color) = (0.8, 0.8, 0.95, 1)
        _AccentColor ("Band / Rivet Colour", Color) = (1, 0.55, 0.2, 1)
        _EyeColor ("Eye White", Color) = (1, 1, 1, 1)
        _PupilColor ("Pupil", Color) = (0.55, 0.08, 0.4, 1)

        [Header(Face Shading (linear multipliers))]
        _Brightness ("Brightness", Range(0.2, 1.5)) = 1
        _TopShade ("Faces pointing up", Range(0, 1.5)) = 1
        _LitShade ("Lit side", Range(0, 1)) = 0.58
        _ShadeShade ("Shade side", Range(0, 1)) = 0.36
        _BottomShade ("Faces pointing down", Range(0, 1)) = 0.2
        _TopSharpness ("Top / side sharpness", Range(1, 16)) = 8

        [Header(Shapes)]
        _LineWidth ("Line Width", Range(0.02, 0.2)) = 0.08
        _EdgeBand ("Head Band (top of every side face)", Range(0, 0.25)) = 0.09
        _EyeRadius ("Eye Socket Radius", Range(0.1, 0.45)) = 0.34
        _RivetRadius ("Rivet Radius", Range(0.1, 0.45)) = 0.24
        [HideInInspector] _EyeFace ("Eye face (object normal, set by PlayerTotem)", Vector) = (0, 0, 1, 0)

        [Header(Depth)]
        _CircleShadow ("Circle Contact Shadow", Range(0, 1)) = 0.55
        _CircleShadowWidth ("Circle Shadow Width", Range(0.005, 0.2)) = 0.07
        _CircleShadowDrop ("Circle Shadow Drop", Range(0, 0.15)) = 0.04
        _EdgeDarken ("Dark Outline On Cube Edges", Range(0, 1)) = 0.35

        [Header(Glow)]
        _GlowWhite ("Glow (warm-white lift on the faces, not the outline)", Range(0, 0.6)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _BlockIndex;
            half4 _BaseColor, _LineColor, _AccentColor, _EyeColor, _PupilColor;
            half _Brightness, _TopShade, _LitShade, _ShadeShade, _BottomShade, _TopSharpness;
            float _LineWidth, _EdgeBand, _EyeRadius, _RivetRadius;
            half _CircleShadow;
            float _CircleShadowWidth, _CircleShadowDrop;
            half _EdgeDarken;
            half _GlowWhite;
            float4 _EyeFace;
        CBUFFER_END

        // DepthCueController globals (zero when nothing drives them).
        float4 _DCFace2;     // w = runner rim strength
        float4 _DCRunnerP;   // xyz point on the ridden face, w contact strength
        float4 _DCRunnerN;   // xyz ridden face normal
        float4 _DCContact;   // rgb contact colour (linear)
        float4 _DCPlayer;    // x = +1 the camera's right side is the lit one, -1 the left
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 local      : TEXCOORD0;   // object position, -0.5..0.5 on the unit cube
                float3 normalOS   : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.local = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            float sdCircle(float2 p, float2 c, float r) { return length(p - c) - r; }
            float sdSegment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }
            float sdPath(float2 p, float2 a, float2 b, float2 c) { return min(sdSegment(p, a, b), sdSegment(p, b, c)); }
            float Fill(float d) { float w = max(fwidth(d), 1e-5); return saturate(0.5 - d / w); }
            float Stroke(float d) { return Fill(d - _LineWidth * 0.5); }

            half3 ContactShadow(float2 uv, float2 c, float r, half3 col)
            {
                float d = sdCircle(uv, c + float2(0, -_CircleShadowDrop), r);
                return col * (1.0 - _CircleShadow * (1.0 - smoothstep(0.0, _CircleShadowWidth, d)));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 no = i.normalOS;
                float3 ao = abs(no);
                float3 d = i.local;
                half3 col = _BaseColor.rgb;
                bool designs = _BlockIndex > 0.5;

                bool eyeFace = dot(no, _EyeFace.xyz) > 0.7;
                if (designs && eyeFace)
                {
                    // The eye socket on whichever face PlayerTotem turned the eye to (the 3D discs sit on it).
                    float3 eu = abs(_EyeFace.y) > 0.7 ? float3(0, 0, 1) : float3(0, 1, 0);
                    float3 er = normalize(cross(eu, no));
                    float2 uv = float2(dot(d, er), dot(d, eu)) + 0.5;
                    col = ContactShadow(uv, float2(0.5, 0.5), _EyeRadius, col);
                    col = lerp(col, _EyeColor.rgb, Fill(sdCircle(uv, float2(0.5, 0.5), _EyeRadius)));
                    if (ao.y < 0.7 && _EdgeBand > 0) col = lerp(col, _AccentColor.rgb, Fill((1.0 - _EdgeBand) - (d.y + 0.5)));
                }
                else if (designs)
                {
                    if (ao.y > 0.7)
                    {
                        if (no.y > 0)
                        {
                            float2 p = abs(d.xz);
                            float sq = max(p.x, p.y);
                            float rings = min(min(abs(sq - 0.14), abs(sq - 0.28)), abs(sq - 0.42));
                            col = lerp(col, _LineColor.rgb, Stroke(rings));
                        }
                    }
                    else
                    {
                        // Side face: u to the right as seen from outside, v up (+Y = away from the rail).
                        float3 right = normalize(cross(no, float3(0, 1, 0)));
                        float2 uv = float2(dot(d, right), d.y) + 0.5;
                        if (no.z < -0.7)
                        {
                            float2 c = float2(0.5, 0.5);
                            col = ContactShadow(uv, c, _RivetRadius, col);
                            col = lerp(col, _AccentColor.rgb, Fill(sdCircle(uv, c, _RivetRadius)));
                            col = lerp(col, _BaseColor.rgb, Fill(sdCircle(uv, c, _RivetRadius * 0.64)));
                            col = lerp(col, _AccentColor.rgb, Fill(sdCircle(uv, c, _RivetRadius * 0.32)));
                        }
                        else
                        {
                            float v1 = sdPath(uv, float2(0.12, 0.86), float2(0.5, 0.12), float2(0.88, 0.86));
                            float v2 = sdPath(uv, float2(0.33, 0.86), float2(0.5, 0.52), float2(0.67, 0.86));
                            col = lerp(col, _LineColor.rgb, Stroke(min(v1, v2)));
                        }
                        // The head band along the top of every side face.
                        if (_EdgeBand > 0) col = lerp(col, _AccentColor.rgb, Fill((1.0 - _EdgeBand) - uv.y));
                    }
                }

                // Thin darker outline on the cube's edges (silhouette against a bright path).
                float3 e = 0.5 - abs(d);
                float edgeD = min(min(max(e.x, e.y), max(e.y, e.z)), max(e.x, e.z));
                float aa = fwidth(edgeD) + 1e-4;
                half outline = (1.0 - smoothstep(0.02 - aa, 0.035 + aa, edgeD)) * (designs ? 1.0 : 0.0);
                col *= 1.0 - _EdgeDarken * outline;

                // Face tone relative to the camera, in the path's light.
                float3 n = normalize(i.normalWS);
                float3 camRight = normalize(UNITY_MATRIX_V[0].xyz);
                float3 flat = n - float3(0, n.y, 0);
                float side = dot(flat, camRight) / max(length(flat), 1e-4) * (_DCPlayer.x < 0 ? -1.0 : 1.0);
                half sideTone = lerp(_ShadeShade, _LitShade, smoothstep(-0.05, 0.05, side));
                half up = pow(saturate(n.y), _TopSharpness);
                half down = pow(saturate(-n.y), _TopSharpness);
                half tone = lerp(lerp(sideTone, _TopShade, up), _BottomShade, down);
                col *= tone * _Brightness;

                // Glow: the faces are lifted toward a warm white (what lets bloom pick the block up like the totem tower), the
                // cube-edge outline is left dark so the corners keep their depth.
                col += _GlowWhite * tone * (1.0h - outline * 0.85h) * half3(1.0h, 0.86h, 0.45h);

                // Rim of contact colour along the edges standing on the rail.
                half contactOn = _DCRunnerP.w * _DCFace2.w;
                if (contactOn > 0.002)
                {
                    float h = dot(i.positionWS - _DCRunnerP.xyz, _DCRunnerN.xyz);
                    half standing = 1.0h - smoothstep(0.3h, 0.6h, abs(dot(n, _DCRunnerN.xyz)));
                    half low = 1.0h - smoothstep(0.05, 0.2, h);
                    col = lerp(col, _DCContact.rgb, saturate(standing * low * contactOn));
                }
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
            float3 _LightDirection;
            float3 _LightPosition;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; };
            V vertShadow(A v)
            {
                V o;
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
            half4 fragShadow(V i) : SV_Target { return 0; }
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
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };
            V vertDepth(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half fragDepth(V i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            V vertDN(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 fragDN(V i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
