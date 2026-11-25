// MainScene prop camera 3.9: Lumi's own light rig, inside her material - so it lights ONLY the player, the same way in
// every checkpoint, and the environment lighting is never touched. Classic character lighting, mobile cheap (one pass):
//   - key light fixed to the camera (upper left, a little in front): the side of her the player sees is always well lit,
//     whatever the checkpoint's sun does; half-wrapped so the fur reads soft, not plastic
//   - a cool, weak fill from the lower right and an ambient floor: shadows keep the fur colour instead of going brown
//   - a soft rim (fresnel) that separates her silhouette from the dark backdrops, like the 3.7 cube's bright edges
//   - height occlusion: her base and the fur under her are darker, so she sits on the rail with weight
//   - specular + a catch-light on the eyes (Gloss), so the eyes sparkle and read at game distance
//   - a little of the checkpoint's sun colour (_SceneTint) so she still belongs to each look
// Output is clamped below the bloom threshold: lit, not glowing.
Shader "Lumi/RunnerLit"
{
    Properties
    {
        _BaseMap ("Atlas", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff (0 = opaque)", Range(0, 1)) = 0.5

        [Header(Key light (camera space))]
        _KeyDir ("Key Direction (view space, towards the light)", Vector) = (-0.45, 0.7, 0.55, 0)
        _KeyColor ("Key Colour", Color) = (1, 0.97, 0.92, 1)
        _KeyStrength ("Key Strength", Range(0, 2)) = 0.95
        _Wrap ("Wrap (soft fur)", Range(0, 1)) = 0.45

        [Header(Fill and ambient)]
        _FillColor ("Fill Colour", Color) = (0.75, 0.85, 1, 1)
        _FillStrength ("Fill Strength", Range(0, 1)) = 0.2
        _Ambient ("Ambient Floor", Range(0, 1)) = 0.5

        [Header(Rim)]
        _RimColor ("Rim Colour", Color) = (1, 0.9, 0.82, 1)
        _RimStrength ("Rim Strength", Range(0, 1.5)) = 0.45
        _RimPower ("Rim Width (higher = thinner)", Range(0.5, 8)) = 3

        [Header(Shape)]
        _AOBottom ("Occlusion At The Base", Range(0, 1)) = 0.55
        _AOHeight ("Occlusion Height (model units)", Range(0.05, 1)) = 0.45

        [Header(Gloss)]
        _Spec ("Specular", Range(0, 2)) = 0
        _Gloss ("Glossiness", Range(1, 256)) = 40
        _CatchLight ("Catch-light (eyes)", Range(0, 2)) = 0

        [Header(Glow)]
        [HDR] _EmitColor ("Glow (added, tinted by the atlas)", Color) = (0, 0, 0, 1)

        [Header(Scene)]
        _SceneTint ("Checkpoint Sun Tint", Range(0, 1)) = 0.2
        _MaxBright ("Max Brightness (bloom guard)", Range(0.5, 2)) = 0.97
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            float4 _KeyDir;
            half4 _KeyColor;
            half _KeyStrength, _Wrap;
            half4 _FillColor;
            half _FillStrength, _Ambient;
            half4 _RimColor;
            half _RimStrength, _RimPower;
            half _AOBottom, _AOHeight;
            half _Spec, _Gloss, _CatchLight;
            half _SceneTint, _MaxBright;
            half4 _EmitColor;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        ENDHLSL

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half height : TEXCOORD3;
                half fog : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.height = v.positionOS.y;   // her pivot is the ground contact: 0 = the rail
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                clip(tex.a - _Cutoff);

                half3 n = normalize(i.normalWS);
                half3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                // camera-locked light directions (view space -> world)
                half3 key = normalize(mul((float3x3)UNITY_MATRIX_I_V, _KeyDir.xyz));
                half3 fill = normalize(mul((float3x3)UNITY_MATRIX_I_V, float3(0.6, -0.35, 0.5)));

                half ndk = saturate((dot(n, key) + _Wrap) / (1.0 + _Wrap));
                half ndf = saturate(dot(n, fill) * 0.5 + 0.5);

                Light sun = GetMainLight();
                half3 sunTint = lerp(half3(1, 1, 1), sun.color / max(max(sun.color.r, sun.color.g), max(sun.color.b, 0.001)), _SceneTint);

                half ao = lerp(_AOBottom, 1.0, saturate(i.height / _AOHeight));
                half3 light = (_Ambient + _KeyColor.rgb * _KeyStrength * ndk + _FillColor.rgb * _FillStrength * ndf) * ao * sunTint;

                half3 col = tex.rgb * light + _EmitColor.rgb * (0.5 + 0.5 * tex.rgb);

                // specular / catch-light (eyes): Blinn on the key + a tight highlight that always faces the camera
                half3 h = normalize(key + v);
                half spec = pow(saturate(dot(n, h)), _Gloss) * _Spec;
                half catchL = pow(saturate(dot(n, normalize(v + key * 0.6))), 180.0) * _CatchLight;
                col += (spec + catchL) * _KeyColor.rgb;

                half rim = pow(1.0 - saturate(dot(n, v)), _RimPower) * _RimStrength * (0.35 + 0.65 * ao);
                col += rim * _RimColor.rgb * lerp(half3(1, 1, 1), tex.rgb * 1.6, 0.5);   // rim takes on her colour

                col = min(col, _MaxBright.xxx);
                // no scene fog: like the 3.7 cube she stays crisp at the camera's distance (fog greyed her out)
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nws, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half3 normalWS : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                return half4(NormalizeNormalPerPixel(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
