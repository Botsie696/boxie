// Spice Route checkpoint: the far cliffs and everything on them (temples, Ganesha colossi, palaces, mosques, houses,
// trees, peacocks, elephants, flags) made by SpiceRouteCheckpointController with SpiceMesh. Vertex colour = the painted
// colour (sRGB), uv.x = the kind of surface, uv.y = a per-piece seed:
//   0 paint (plaster)  1 gold leaf  2 foliage  3 glow (windows, lamps, flames)  4 carved stone (temple courses + carving)
//   5 cliff rock       6 tiled dome (scales + gold ribs)  7 marble with inlay  8 fabric (stripes)  9 peacock feathers
// Gouache shading: three flat values from the camera-relative light, shade pulled toward violet, paper grain; then the
// chasm fog like the rocks. Only drawn where the spice look is in force (the look wipe cuts it).
Shader "SpiceRouteCheckpoint/Prop"
{
    Properties
    {
        _Scale ("Pattern Scale (world units)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/SpiceRouteCheckpoint/Shaders/SpiceCommon.hlsl"
        #include "Assets/SpiceRouteCheckpoint/Shaders/SpiceRock.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _Scale;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Assets/PropCameraLooks/LookTransition/Shaders/LookChasmFog.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                float2 kind : TEXCOORD3;
            };

            // Only the feet of the cliffs sink fully into the mist; above them a light share of the chasm fog.
            half3 SrFog(half3 c, float3 wp, float4 positionCS)
            {
                half3 f = ApplyChasmFog(c, wp, positionCS);
                half foot = (half)saturate((_ChasmFogHeight.x - wp.y) / max(_ChasmFogHeight.x - _ChasmFogHeight.y, 1.0));
                return lerp(c, f, max(foot * foot, 0.3h));
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.kind = v.uv;
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                clip(SrAt(suv) - 0.5h);

                float3 wp = i.positionWS;
                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                int kind = (int)(i.kind.x + 0.5);
                float seed = i.kind.y;
                float S = max(_Scale, 0.05);
                half3 base = SrLin(i.color.rgb);

                if (kind == 5)
                {
                    // Cliff rock: the vertex colour is the lit stone; shade and strata derived from it.
                    half3 shade = base * half3(0.5h, 0.42h, 0.62h);
                    half3 band = base * half3(0.92h, 0.78h, 0.7h);
                    half3 rc = SrRockPaint(wp, n, base, shade, band);
                    return half4(SrFog(rc, wp, i.positionCS), 1);
                }

                float2 nh = n.xz;
                float2 tg = dot(nh, nh) > 1e-4 ? normalize(float2(-nh.y, nh.x)) : float2(1, 0);
                bool flatUp = abs(n.y) > 0.7;
                float2 q = (flatUp ? wp.xz : float2(dot(wp.xz, tg), wp.y)) / S;
                float aa = max(length(fwidth(q)), 1e-4) * 0.75;

                float3 L = normalize(_SrLightDir.xyz);
                half ndl = (half)dot(n, L);
                half l = saturate(ndl * 0.6h + 0.45h);
                half v = (floor(l * 3.0h) + smoothstep(0.35h, 0.65h, frac(l * 3.0h))) / 3.0h;
                half3 c = base;
                half emissive = 0.0h;

                if (kind == 1)
                {
                    c = SrGold(q * 2.0 + seed, v);
                    emissive = 0.15h;
                }
                else if (kind == 2)
                {
                    float2 g = q * 2.2;
                    float2 ci = floor(g);
                    float2 f = frac(g) - 0.5;
                    half dab = (half)PixHash(float3(ci, seed + 3.0));
                    c = base * lerp(0.75h, 1.15h, dab);
                    c = lerp(c, base * 1.35h + 0.02h, SrFill(length(f + float2(0.15, -0.1)) - 0.18, aa * 2.2) * 0.5h);
                }
                else if (kind == 3)
                {
                    c = base * (1.1h + 0.6h * (half)_SrParams.y);
                    emissive = 1.0h;
                }
                else if (kind == 4)
                {
                    // Carved stone: courses every 0.35 units, a carved band of niches / beads on alternate courses.
                    float course = q.y / 0.35;
                    float cf = frac(course);
                    c = base * (0.9h + 0.16h * (half)SrNoise2(q * 3.0 + seed));
                    c *= 1.0h - SrLine(cf - 0.02, 0.025, aa / 0.35) * 0.35h;
                    if (fmod(floor(course), 3.0) < 0.5)
                    {
                        float bx = frac(q.x / 0.22) - 0.5;
                        float niche = SrBox(float2(bx, cf - 0.5), float2(0.25, 0.28));
                        c *= 1.0h - SrFill(niche, aa / 0.22) * 0.3h;
                    }
                    else if (fmod(floor(course), 3.0) < 1.5)
                    {
                        float bead = length(float2(frac(q.x / 0.12) - 0.5, (cf - 0.5) * 2.9)) - 0.32;
                        c = lerp(c, c * 1.15h, SrFill(bead, aa / 0.12));
                    }
                }
                else if (kind == 6)
                {
                    // Tiled dome: fish scales, gold ribs.
                    float2 sp = float2(atan2(wp.z - unity_ObjectToWorld._m23, wp.x - unity_ObjectToWorld._m03) * 3.0, wp.y / S * 3.0);
                    float row = floor(sp.y);
                    float2 sq = float2(frac(sp.x + fmod(row, 2.0) * 0.5) - 0.5, frac(sp.y));
                    half scale = SrLine(length(sq) - 0.55, 0.05, 0.04);
                    c = base * (0.9h + 0.15h * (half)PixHash(float3(floor(sp.x), row, seed)));
                    c = lerp(c, SrGold(q, 0.85h), scale * 0.8h);
                }
                else if (kind == 7)
                {
                    float2 mq = frac(q * 2.0) - 0.5;
                    float fl = SrPetals(mq, 4.0, 0.22, 0.7, 0.785);
                    c = base;
                    c = lerp(c, SrLin(half3(0.78h, 0.25h, 0.3h)), SrFill(fl, aa * 2.0) * 0.8h);
                    c = lerp(c, SrLin(half3(0.2h, 0.5h, 0.35h)), SrFill(SrPetals(mq, 4.0, 0.32, 0.9, 0.0), aa * 2.0) * (1.0h - SrFill(fl, aa * 2.0)) * 0.6h);
                }
                else if (kind == 8)
                {
                    half stripe = (half)step(0.5, frac(q.y * 2.5 + seed));
                    c = lerp(base, base * 0.72h + SrLin(half3(0.9h, 0.7h, 0.2h)) * 0.25h, stripe);
                }
                else if (kind == 9)
                {
                    float2 eq = frac(q * 1.6 + seed) - 0.5;
                    float e = length(eq * float2(1.0, 0.75));
                    c = base;
                    c = lerp(c, SrLin(half3(0.85h, 0.7h, 0.2h)), SrFill(e - 0.2, aa * 1.6));
                    c = lerp(c, SrLin(half3(0.1h, 0.6h, 0.55h)), SrFill(e - 0.14, aa * 1.6));
                    c = lerp(c, SrLin(half3(0.05h, 0.1h, 0.35h)), SrFill(e - 0.07, aa * 1.6));
                    emissive = 0.1h;
                }
                else
                {
                    // Paint: a little mottled, a darker wash toward the foot.
                    c = base * (0.92h + 0.12h * (half)SrNoise2(q * 1.5 + seed));
                }

                half3 shadeCol = c * half3(0.52h, 0.48h, 0.7h);
                half3 lit = lerp(shadeCol, c, v);
                lit *= 1.0h + SrGrain(q * 3.0) * (half)_SrParams.z;
                c = lerp(lit, c, emissive);
                c = SrFog(c, wp, i.positionCS);
                if (kind == 3) c = lerp(c, base * (1.1h + 0.6h * (half)_SrParams.y), 0.55h);   // lamps still glow through the haze
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half4 frag(Varyings i) : SV_Target
            {
                clip(SrAt(GetNormalizedScreenSpaceUV(i.positionCS)) - 0.5h);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(SrAt(GetNormalizedScreenSpaceUV(i.positionCS)) - 0.5h);
                return half4(NormalizeNormalPerPixel(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
