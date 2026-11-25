// Voxel checkpoint: every voxel mesh made by VoxelCheckpointController - the checkpoint's own block cliffs (terraced
// grass-topped mesas with brick / sandstone / dirt strata and the odd waterfall) and the props on them (square-crowned
// trees, mushroom trees, brick towers, block houses, Mario fire flowers, pipes, "?" blocks, bushes, tufts).
// Vertex data: colour = base pigment (linear), alpha = wind sway weight; uv0 = surface coordinate in voxels; uv1.x = kind
// (VoxelCommon.hlsl), uv1.y = texture pixels per voxel. Shaded flat per face from the camera-relative light, then the
// chasm fog like the rocks. Not clipped by the look wipe: what is built stays as it is until it leaves the screen.
Shader "VoxelCheckpoint/Block"
{
    Properties
    {
        _SwaySpeed ("Sway Speed", Float) = 1.4
        _SwayAmount ("Sway Amount", Float) = 0.06
        _FogShare ("Fog Share", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/VoxelCheckpoint/Shaders/VoxelCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _SwaySpeed, _SwayAmount, _FogShare;
        CBUFFER_END

        float3 SwayWS(float3 wp, float w)
        {
            float t = _Time.y * _SwaySpeed;
            float ph = wp.x * 0.35 + wp.z * 0.27;
            wp.x += sin(t + ph) * _SwayAmount * w;
            wp.z += sin(t * 0.8 + ph * 1.3 + 1.7) * _SwayAmount * 0.6 * w;
            return wp;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/PropCameraLooks/LookTransition/Shaders/LookChasmFog.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float2 kind : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = SwayWS(TransformObjectToWorld(v.positionOS.xyz), v.color.a);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.uv = v.uv;
                o.kind = v.uv1;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                int kind = (int)(i.kind.x + 0.5);
                float px = max(i.kind.y, 1.0);
                half3 c = VxPattern(kind, i.uv, i.color.rgb, px, n, 0.0);
                bool glow = kind == VX_LIT_WINDOW || kind == VX_LAMP;
                if (!glow && kind != VX_EYE) c *= VxShade(n);
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                    c *= ao.indirectAmbientOcclusion;
                #endif
                half3 fogged = ApplyChasmFog(c, i.positionWS, i.positionCS);
                c = lerp(c, fogged, (half)_FogShare * (glow ? 0.5h : 1.0h));
                return half4(c, 1);
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
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(SwayWS(TransformObjectToWorld(v.positionOS.xyz), v.color.a));
                return o;
            }
            half frag(Varyings i) : SV_Target { return i.positionCS.z; }
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
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(SwayWS(TransformObjectToWorld(v.positionOS.xyz), v.color.a));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
