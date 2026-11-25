// Flat, hard-edged marks on a cube face under the power diamond (DynamicPathGeneratorPowerLook.cs):
// its contact shadow (a filled octagon the shape of the gem) and the square frame that lights up the
// face it is on. No radial falloff - straight edges and corners, to match the blocky track.
//   _Frame = 0     : fill the whole mesh.
//   _Frame > 0     : only a border of this width (in UV) round a 0..1 quad - a square frame.
// Lives in Resources so Shader.Find keeps working in builds.
Shader "Custom/PowerGemDecal"
{
    Properties
    {
        [HDR] _Color("Colour", Color) = (0, 0, 0, 0.6)
        _Frame("Frame width (0 = fill)", Range(0, 0.5)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Decal"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Frame;
            CBUFFER_END

            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };

            V vert(A a)
            {
                V o;
                o.p = TransformObjectToHClip(a.p.xyz);
                o.uv = a.uv;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half a = 1.0h;
                if (_Frame > 0.0h)
                {
                    // Distance to the nearest edge of the quad; one pixel of smoothing keeps the edge crisp, not jaggy.
                    float2 e = min(i.uv, 1.0 - i.uv);
                    float d = min(e.x, e.y);
                    float aa = max(fwidth(d), 1e-4);
                    a = (half)(1.0 - smoothstep(_Frame - aa, _Frame + aa, d));
                }
                return half4(_Color.rgb, _Color.a * a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
