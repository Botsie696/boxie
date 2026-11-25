// Cube skins (CubeSkin): one shader, eight personalities picked by _Mode. Works on a unit cube in object space, so every
// face gets its own square pattern. Lit by a fixed soft key light plus ambient so it reads the same in every checkpoint.
//   0 Classic   flat white, soft edge line          4 Lava     dark rock, glowing cracks that breathe
//   1 Neon      dark body, thick glowing edges      5 Ice      pale blue, frost speckle, white rim
//   2 Glass     see-through tint, bright rim/edges  6 Pixel    retro 6 x 6 cells in three colours
//   3 Gold      metal gradient, moving sheen band   7 Galaxy   deep nebula with twinkling stars
Shader "Boxicle/SkinCube"
{
    Properties
    {
        [IntRange] _Mode ("Mode: 0 Classic 1 Neon 2 Glass 3 Gold 4 Lava 5 Ice 6 Pixel 7 Galaxy", Range(0, 7)) = 0
        _BaseColor ("Base", Color) = (1,1,1,1)
        _Color2 ("Second", Color) = (0.6,0.6,0.7,1)
        _Color3 ("Third", Color) = (0.2,0.2,0.3,1)
        _EdgeColor ("Edge / glow", Color) = (1,1,1,1)
        _EdgeWidth ("Edge width (0-0.5)", Range(0,0.25)) = 0.04
        _Glow ("Glow", Float) = 1
        _Alpha ("Alpha (glass)", Range(0,1)) = 1
        _Scale ("Pattern scale", Float) = 6
        _Speed ("Animation speed", Float) = 1
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Skin"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _Mode, _EdgeWidth, _Glow, _Alpha, _Scale, _Speed;
            float4 _BaseColor, _Color2, _Color3, _EdgeColor;
            float _SrcBlend, _DstBlend, _ZWrite;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 op : TEXCOORD0; float3 on : TEXCOORD1; float3 nw : TEXCOORD2; float3 vw : TEXCOORD3; };

            V vert (A i)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.pos = p.positionCS;
                o.op = i.pos.xyz;
                o.on = i.n;
                o.nw = TransformObjectToWorldNormal(i.n);
                o.vw = GetWorldSpaceViewDir(p.positionWS);
                return o;
            }

            float H(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float N(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(H(i), H(i + float2(1,0)), f.x), lerp(H(i + float2(0,1)), H(i + 1), f.x), f.y); }

            half4 frag (V i) : SV_Target
            {
                float3 n = normalize(i.on);
                float2 uv = abs(n.x) > 0.5 ? i.op.zy : (abs(n.y) > 0.5 ? i.op.xz : i.op.xy);   // -0.5..0.5 on this face
                float faceId = abs(n.x) > 0.5 ? (n.x > 0 ? 1 : 2) : (abs(n.y) > 0.5 ? (n.y > 0 ? 3 : 4) : (n.z > 0 ? 5 : 6));
                float ed = 0.5 - max(abs(uv.x), abs(uv.y));                                   // 0 at the face edge
                float aa = fwidth(ed) * 1.5 + 1e-4;
                float edge = 1 - smoothstep(_EdgeWidth, _EdgeWidth + aa, ed);
                float3 nw = normalize(i.nw), vw = normalize(i.vw);
                float key = saturate(dot(nw, normalize(float3(0.45, 0.85, -0.35))));
                float lam = 0.58 + 0.42 * key;
                float fres = pow(1 - saturate(dot(nw, vw)), 3);
                float t = _Time.y * _Speed;
                float3 c = _BaseColor.rgb * lam;
                float a = 1;
                int m = (int)round(_Mode);

                if (m == 0)        // Classic
                {
                    c = lerp(c, _EdgeColor.rgb, edge * 0.55);
                }
                else if (m == 1)   // Neon wireframe
                {
                    float inner = 1 - smoothstep(0.0, aa * 2, abs(ed - _EdgeWidth * 2.6) - 0.004);
                    float pulse = 0.75 + 0.25 * sin(t * 3 + faceId);
                    c = _BaseColor.rgb * (0.35 + 0.25 * key) + _EdgeColor.rgb * (edge * _Glow + inner * 0.35 * pulse);
                }
                else if (m == 2)   // Glass
                {
                    c = _BaseColor.rgb * (0.55 + 0.45 * key) + _EdgeColor.rgb * (fres * 0.9 + edge * 0.8);
                    a = saturate(_Alpha + fres * 0.5 + edge * 0.7);
                }
                else if (m == 3)   // Gold
                {
                    float g = saturate(key * 0.8 + fres * 0.4);
                    c = lerp(_Color3.rgb, _BaseColor.rgb, g);
                    float band = frac((uv.x + uv.y) * 0.7 - t * 0.35 + faceId * 0.13);
                    float sheen = smoothstep(0.0, 0.06, band) * (1 - smoothstep(0.06, 0.16, band));
                    c += _EdgeColor.rgb * (sheen * 1.2 + fres * 0.5);
                    c = lerp(c, _Color2.rgb, edge * 0.6);
                }
                else if (m == 4)   // Lava
                {
                    float2 q = uv * _Scale + faceId * 3.1;
                    float n1 = N(q), n2 = N(q * 2.1 + 7);
                    float crack = 1 - smoothstep(0.03, 0.09, abs(n1 - 0.5) + n2 * 0.05);
                    float breathe = 0.65 + 0.35 * sin(t * 2.2 + n2 * 6);
                    c = _BaseColor.rgb * (0.45 + 0.35 * key) * (0.8 + 0.4 * n2);
                    c += _EdgeColor.rgb * (crack * _Glow * breathe + edge * 0.6 * breathe);
                }
                else if (m == 5)   // Ice
                {
                    float speck = step(0.86, H(floor(uv * _Scale * 4) + faceId));
                    float frost = N(uv * _Scale + faceId * 2.0);
                    c = lerp(_BaseColor.rgb, _Color2.rgb, frost * 0.6) * (0.7 + 0.35 * key);
                    c += _EdgeColor.rgb * (fres * 0.8 + edge * 0.6 + speck * 0.35);
                }
                else if (m == 6)   // Pixel
                {
                    float2 cell = floor((uv + 0.5) * 6);
                    float h = H(cell + faceId * 13.7);
                    float3 pc = h < 0.45 ? _BaseColor.rgb : (h < 0.8 ? _Color2.rgb : _Color3.rgb);
                    float2 f = frac((uv + 0.5) * 6);
                    float grid = step(min(f.x, f.y), 0.08);
                    c = pc * (0.62 + 0.38 * key) * (1 - grid * 0.35);
                    c = lerp(c, _EdgeColor.rgb, edge * 0.7);
                }
                else               // Galaxy
                {
                    float2 q = uv * 2.4 + faceId * 1.7;
                    float neb = N(q + t * 0.05) * 0.6 + N(q * 2.3 - t * 0.03) * 0.4;
                    c = lerp(_BaseColor.rgb, _Color2.rgb, smoothstep(0.35, 0.8, neb)) * (0.7 + 0.3 * key);
                    float2 sc = floor(uv * _Scale * 3 + faceId * 5);
                    float star = step(0.93, H(sc));
                    float tw = 0.5 + 0.5 * sin(t * 4 + H(sc + 3) * 30);
                    float2 sf = frac(uv * _Scale * 3 + faceId * 5) - 0.5;
                    float sq = 1 - smoothstep(0.05, 0.16, max(abs(sf.x), abs(sf.y)));
                    c += _EdgeColor.rgb * (star * sq * tw * _Glow + fres * 0.4 + edge * 0.35);
                }
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
