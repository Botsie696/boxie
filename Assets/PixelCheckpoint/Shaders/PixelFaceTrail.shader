// Pixel checkpoint copy of ToonCheckpoint/TrailFX Face Ribbon Toon (left untouched; itself a copy of Runner/TrailFX Face
// Ribbon). Pixel for pixel the toon shader, except where the pixel look is in force (PixelCommon.hlsl, follows the look
// wipe): there the trail is a chunky pixel-art ribbon of glowing moss light - texel blocks, a glowing core, stepped edges
// that narrow toward the tail, the tail breaking up into loose blocks, the odd coral spark.
// Toon notes: where the toon look is in force the glow ribbon becomes a flat cartoon speed-stripe.
// Original notes:
// The runner's trail laid flat on the face of the path it rides (FaceHuggingTrail.cs).
// Same look as Runner/TrailFX Ribbon (same property names, so RunnerTrailFX's per-frame block drives it), but:
//  - no pull toward the camera: the ribbon is depth tested against the path, so on a hidden face the leg covers it
//  - nothing is drawn through a cube: the fin (FaceHuggingTrail) is what keeps a hidden face readable
// uv.x runs 0 at the runner to 1 at the tail, uv.y across the width; uv2.x is the distance along the trail (for dashes).
// Vertex colour rgb above 1 brightens a stretch (the fold over an edge).
Shader "PixelCheckpoint/TrailFX Face Ribbon Pixel"
{
    Properties
    {
        [HDR] _HeadColor ("Head Colour", Color) = (0.85, 1, 1, 1)
        [HDR] _MidColor ("Mid Colour", Color) = (0.25, 0.85, 1, 1)
        [HDR] _TailColor ("Tail Colour", Color) = (0.42, 0.3, 1, 1)
        [HDR] _CoreColor ("Core Colour", Color) = (1, 1, 1, 1)
        _CoreWidth ("Core Width", Range(0.01, 1)) = 0.14
        _CoreLength ("Core Length", Range(0.01, 1)) = 0.25
        _GlowFalloff ("Glow Falloff", Range(0.5, 12)) = 2.2
        _Intensity ("Intensity", Range(0, 4)) = 1
        _Opacity ("Body Opacity", Range(0, 1)) = 0.55
        _TailPower ("Tail Fade Power", Range(0.2, 4)) = 1.2
        _FlowScale ("Shimmer Scale", Range(0.05, 4)) = 0.8
        _FlowSpeed ("Shimmer Speed", Range(0, 10)) = 1.6
        _FlowStrength ("Shimmer Strength", Range(0, 1)) = 0.55
        _Flash ("Flash", Range(0, 2)) = 0
        [HDR] _FlashColor ("Flash Colour", Color) = (1, 1, 1, 1)
        _CameraPull ("Pull Toward Camera", Range(0, 0.2)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/ToonCheckpoint/Shaders/ToonCommon.hlsl"
        #include "Assets/PixelCheckpoint/Shaders/PixelCommon.hlsl"
        #include "Assets/FlowerCheckpoint/Shaders/FlowerCommon.hlsl"
        #include "Assets/VoxelCheckpoint/Shaders/VoxelCommon.hlsl"
        #include "Assets/PencilCheckpoint/Shaders/PencilCommon.hlsl"
        #include "Assets/SpaceCheckpoint/Shaders/SpaceCommon.hlsl"
        #include "Assets/SpiceRouteCheckpoint/Shaders/SpiceCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _HeadColor;
            half4 _MidColor;
            half4 _TailColor;
            half4 _CoreColor;
            half _CoreWidth;
            half _CoreLength;
            half _GlowFalloff;
            half _Intensity;
            half _Opacity;
            half _TailPower;
            float _FlowScale;
            float _FlowSpeed;
            half _FlowStrength;
            half _Flash;
            half4 _FlashColor;
            float _CameraPull;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
            float2 uv2 : TEXCOORD1;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            float dist : TEXCOORD2;
        };

        float Hash31(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float ValueNoise(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash31(i),                   Hash31(i + float3(1, 0, 0)), f.x),
                             lerp(Hash31(i + float3(0, 1, 0)), Hash31(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(Hash31(i + float3(0, 0, 1)), Hash31(i + float3(1, 0, 1)), f.x),
                             lerp(Hash31(i + float3(0, 1, 1)), Hash31(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

        Varyings Vert(Attributes input)
        {
            Varyings output;
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionWS = positionWS;
            // Just enough to win the depth test against the face it lies on, not enough to show through the leg.
            positionWS += GetWorldSpaceNormalizeViewDir(positionWS) * _CameraPull;
            output.positionCS = TransformWorldToHClip(positionWS);
            output.color = input.color;
            output.uv = input.uv;
            output.dist = input.uv2.x;
            return output;
        }

        // ---- pixel: texel blocks (4 per unit along the trail, 5 across), a stepped taper, a broken tail ----
        half4 PixelRibbon(Varyings input, half t)
        {
            float ta = floor(input.dist * 4.0);
            float tc = clamp(floor(saturate(input.uv.y) * 5.0), 0.0, 4.0);
            half across = abs(tc - 2.0);                                   // 0 core, 1 body, 2 edge
            half halfWidth = t < 0.22h ? 2.0h : t < 0.55h ? 1.0h : 0.0h;
            half inBody = step(across, halfWidth + 0.5h);
            // The tail breaks into loose blocks that thin out.
            float flick = floor(_Time.y * 8.0);
            half keep = lerp(0.85h, 0.0h, saturate((t - 0.5h) / 0.45h));
            half alive = t < 0.5h ? 1.0h : step(PixHash2(float2(ta, tc)), keep);
            half3 c = across < 0.5h ? _PixCaveMossGlow.rgb : across < 1.5h ? _PixCaveMossLight.rgb : _PixCaveMoss.rgb;
            // No white head: the runner sits on the trail's start and must stay readable.
            // Twinkling blocks inside the body and the odd coral spark.
            half tw = step(0.9h, PixHash2(float2(ta, tc + flick)));
            c = lerp(c, half3(1, 1, 1), tw * 0.4h);
            c = lerp(c, _PixShroomLight.rgb, step(0.97h, PixHash2(float2(ta * 1.3, tc + 7.0))));
            c = lerp(c, _FlashColor.rgb, saturate(_Flash) * 0.5h * (1.0h - t));
            c *= saturate(input.color.rgb + 0.25h);
            half a = inBody * alive * (t < 0.5h ? 0.95h : 0.8h) * smoothstep(0.0h, 0.1h, t) * saturate(input.color.a * 1.5h) * (1.0h - step(0.95h, t));
            return half4(c * a, a);
        }

        // ---- flower: a painted vine of blossoms - a lime brush stroke with a leafy edge, flowers of every blossom colour
        // strewn along it, thinning and shrinking toward the tail ----
        half4 FlowerRibbon(Varyings input, half t)
        {
            float across = abs(input.uv.y * 2.0 - 1.0);
            float aaX = fwidth(across) + 1e-4;
            // The stroke: ragged edge (leaves), narrows toward the tail.
            float leafy = (PixNoise(float3(input.dist * 3.0, 0.0, 2.0)) - 0.5) * 0.25;
            float w = lerp(0.55, 0.15, t) + leafy;
            half stroke = (half)(1.0 - smoothstep(w - aaX, w + aaX, across));
            half3 c = lerp(_FlGrass.rgb * 1.1h, _FlGrassDark.rgb, (half)smoothstep(0.0, w, across) * 0.6h);
            half a = stroke * 0.75h;
            // Blossoms: cells along the trail, three across.
            float2 p = float2(input.dist * 2.2, input.uv.y * 2.4);
            float aaP = max(fwidth(p.x), fwidth(p.y)) + 1e-4;
            half3 fc; half ctr;
            half f = FlFlower(p, 1.0, lerp(0.85, 0.2, (float)t), 41.0, aaP, fc, ctr);
            f *= (half)step(t, 0.92h);
            c = lerp(c, fc * 1.1h, f);
            c = lerp(c, half3(1.0h, 0.95h, 0.8h), ctr * f * 0.8h);
            a = max(a, f);
            c = lerp(c, _FlashColor.rgb, saturate(_Flash) * 0.5h * (1.0h - t));
            c *= saturate(input.color.rgb + 0.25h);
            a *= smoothstep(0.0h, 0.08h, t) * saturate(input.color.a * 1.5h) * (1.0h - smoothstep(0.85h, 0.98h, t));
            return half4(c * a, a);
        }

        // ---- voxel: a trail of square pixel blocks in the leg colours, thinning and shrinking toward the tail ----
        half4 VoxelRibbon(Varyings input, half t)
        {
            float2 p = float2(input.dist * 3.0, input.uv.y * 3.0);
            float2 g = floor(p);
            float2 f = frac(p);
            float h = PixHash(float3(g, 61.0));
            if (h > lerp(0.95, 0.2, (float)t) || t > 0.95h) return 0;
            float s = lerp(0.44, 0.16, (float)t);
            float m = max(abs(f.x - 0.5), abs(f.y - 0.5));
            half cov = (half)step(m, s);
            half3 c = (half3)_VxLegTops[(uint)(PixHash(float3(g, 67.0)) * 9.99)].rgb;
            if (m > s * 0.62) c *= 0.62h;
            else if (f.x < 0.5 && f.y > 0.5) c = c * 1.2h + 0.04h;
            c = lerp(c, _FlashColor.rgb, saturate(_Flash) * 0.5h * (1.0h - t));
            half a = cov * smoothstep(0.0h, 0.06h, t) * saturate(input.color.a * 1.5h);
            return half4(c * a, a);
        }

        // ---- pencil: graphite shading rubbed onto the face behind the runner - dense diagonal strokes at the head,
        // thinning and breaking up toward the tail, ragged edges, a darker stroke along each side ----
        half4 PencilRibbon(Varyings input, half t)
        {
            float2 p = float2(input.dist, input.uv.y * 0.9);
            float aa = max(fwidth(p.x), fwidth(p.y)) + 1e-4;
            half across = abs(input.uv.y * 2.0 - 1.0);
            // Ragged width: the edge wanders like a hand-shaded patch, narrowing toward the tail.
            half edge = (half)(lerp(0.95, 0.45, (float)t) + (sin(input.dist * 5.3) * 0.5 + sin(input.dist * 13.1) * 0.3) * 0.1);
            half body = (half)(1.0 - smoothstep(edge - 0.08, edge, across));
            half dense = (half)lerp(0.95, 0.25, (float)t);
            half ink = PcHatch(p, 0.9, lerp(0.05, 0.11, (float)t), 0.012, aa, 3.0) * (half)smoothstep(0.1, 0.25, dense);
            ink = max(ink, PcHatch(p, -0.6, 0.06, 0.01, aa, 7.0) * (half)smoothstep(0.55, 0.75, dense));
            ink = max(ink, dense * 0.35h * (half)lerp(0.6, 1.0, PixHash(float3(floor(p * 60.0), 2.0))));
            ink *= body;
            half rim = PcLine(across - edge + 0.04, 0.03, aa * 2.0) * (half)step(t, 0.7h);
            ink = max(ink, rim * 0.8h);
            half a = saturate(ink) * (half)_PcGraphite.a * smoothstep(0.0h, 0.05h, t) * (1.0h - smoothstep(0.75h, 0.98h, t)) * saturate(input.color.a * 1.5h);
            a = saturate(a + _Flash * 0.3h * body * (1.0h - t));
            return half4(_PcGraphite.rgb * a, a);
        }

        // ---- space: an oil brush stroke dragged across the face - cream core, orange, magenta edge, fading to blue at
        // the tail, with bristle streaks along it ----
        half4 SpaceRibbon(Varyings input, half t)
        {
            float across = abs(input.uv.y * 2.0 - 1.0);
            float aaX = fwidth(across) + 1e-4;
            float w = lerp(0.62, 0.14, t) + (PixNoise(float3(input.dist * 2.5, 0.0, 5.0)) - 0.5) * 0.2;
            half stroke = (half)(1.0 - smoothstep(w - aaX, w + aaX, across));
            half r = (half)saturate(across / max(w, 1e-3));
            half3 c = lerp(_SpCream.rgb * 1.15h, _SpAccent.rgb * 1.1h, smoothstep(0.1h, 0.55h, r));
            c = lerp(c, _SpMagenta.rgb, smoothstep(0.55h, 1.0h, r));
            c = lerp(c, _SpSide.rgb * 1.4h, saturate(t * 1.2h));
            c *= 0.82h + 0.36h * (half)PixNoise(float3(input.dist * 1.6, input.uv.y * 16.0, 8.0));   // bristles
            c = lerp(c, _FlashColor.rgb, saturate(_Flash) * 0.5h * (1.0h - t));
            c *= saturate(input.color.rgb + 0.25h);
            half a = stroke * 0.88h * smoothstep(0.0h, 0.08h, t) * saturate(input.color.a * 1.5h) * (1.0h - smoothstep(0.8h, 0.98h, t));
            return half4(c * a, a);
        }

        // ---- spice route: a silk ribbon laid on the face - saffron to rani pink (India) or turquoise to cobalt (Arabia), a gold
        // zari edge with woven dots, fading at the tail ----
        half4 SpiceRibbon(Varyings input, half t)
        {
            float across = abs(input.uv.y * 2.0 - 1.0);
            float aaX = fwidth(across) + 1e-4;
            float w = lerp(0.7, 0.2, t);
            half body = (half)(1.0 - smoothstep(w - aaX, w + aaX, across));
            half h = SrEnvHalf();
            half3 a0 = lerp(SrLin(half3(0.98h, 0.62h, 0.16h)), SrLin(half3(0.12h, 0.7h, 0.74h)), h);
            half3 a1 = lerp(SrLin(half3(0.9h, 0.22h, 0.47h)), SrLin(half3(0.1h, 0.2h, 0.6h)), h);
            half3 c = lerp(a0, a1, saturate(t * 1.4h));
            half rim = (half)smoothstep(w - 0.2 - aaX, w - 0.2 + aaX, across) * body;
            half3 gold = SrGold(float2(input.dist, input.uv.y), 0.9h);
            float2 dq = float2(frac(input.dist * 2.0) - 0.5, (across - (w - 0.1)) * 6.0);
            gold = lerp(gold, a1 * 0.6h, (half)(1.0 - smoothstep(0.12, 0.2, length(dq))) * rim);
            c = lerp(c, gold, rim);
            c *= 0.9h + 0.2h * (half)SrNoise2(float2(input.dist * 3.0, input.uv.y * 10.0));
            c = lerp(c, _FlashColor.rgb, saturate(_Flash) * 0.5h * (1.0h - t));
            c *= saturate(input.color.rgb + 0.25h);
            half a = body * 0.92h * smoothstep(0.0h, 0.06h, t) * saturate(input.color.a * 1.5h) * (1.0h - smoothstep(0.82h, 0.98h, t));
            return half4(c * a, a);
        }

        half4 Ribbon(Varyings input)
        {
            half t = saturate(input.uv.x);
            half across = abs(input.uv.y * 2.0 - 1.0);
            half glow = exp(-across * across * _GlowFalloff) * (1.0 - across * across);
            half core = saturate(1.0 - across / _CoreWidth);
            core *= core * saturate(1.0 - t / _CoreLength);

            half3 grad = t < 0.5 ? lerp(_HeadColor.rgb, _MidColor.rgb, t * 2.0)
                                 : lerp(_MidColor.rgb, _TailColor.rgb, t * 2.0 - 1.0);

            float time = _Time.y * _FlowSpeed;
            float3 p = input.positionWS * _FlowScale;
            half n = ValueNoise(p + float3(0.31, 0.63, 0.47) * time) * 0.65
                   + ValueNoise(p * 2.3 - float3(0.5, 0.2, 0.7) * time) * 0.35;
            half shimmer = lerp(1.0, 0.4 + 1.2 * n + 0.9 * n * n * n * n, _FlowStrength);

            half3 col = grad * glow * shimmer
                      + _CoreColor.rgb * core
                      + _FlashColor.rgb * (_Flash * 0.7 * glow * (1.0 - t));
            half fade = pow(1.0 - t, _TailPower) * input.color.a;
            col *= input.color.rgb * fade * _Intensity;
            half4 glowOut = half4(col, saturate(glow * fade * _Opacity));

            // ---- toon: a flat painted speed-stripe instead of the glow ----
            float2 suv = GetNormalizedScreenSpaceUV(input.positionCS);
            half pix = PixAt(suv);
            if (pix > 0.001h) glowOut = lerp(glowOut, PixelRibbon(input, t), pix);
            half fl = FlAt(suv);
            if (fl > 0.001h) glowOut = lerp(glowOut, FlowerRibbon(input, t), fl);
            half vxr = VxAt(suv);
            if (vxr > 0.001h) glowOut = lerp(glowOut, VoxelRibbon(input, t), vxr);
            half pcr = PcAt(suv);
            if (pcr > 0.001h) glowOut = lerp(glowOut, PencilRibbon(input, t), pcr);
            half spr = SpAt(suv);
            if (spr > 0.001h) glowOut = lerp(glowOut, SpaceRibbon(input, t), spr);
            half srr = SrAt(suv);
            if (srr > 0.001h) glowOut = lerp(glowOut, SpiceRibbon(input, t), srr);
            half toon = ToonAt(suv);
            if (toon < 0.001h) return glowOut;
            float aaX = fwidth(across) + 1e-4;
            half body = 1.0h - smoothstep(0.8h - aaX, 0.8h + aaX, across);
            half inkBorder = smoothstep(0.6h - aaX, 0.6h + aaX, across) * body;
            half coreStripe = (1.0h - smoothstep(0.2h - aaX, 0.2h + aaX, across)) * (1.0h - smoothstep(0.5h, 0.56h, t));
            // The tail breaks into dashes that get shorter, then stop.
            float seg = frac(input.dist * 0.9);
            half dashLen = lerp(1.0h, 0.3h, smoothstep(0.3h, 0.8h, t));
            half dashOn = step(seg, dashLen);
            half alive = (1.0h - step(0.9h, t)) * dashOn;
            half3 bodyCol = t < 0.45h ? _MidColor.rgb : lerp(_MidColor.rgb, _TailColor.rgb, saturate((t - 0.45h) / 0.45h) * 0.6h);
            half3 tc = lerp(bodyCol, _CoreColor.rgb, coreStripe);
            tc = lerp(tc, _ToonInk.rgb, inkBorder * 0.85h);
            tc = lerp(tc, _FlashColor.rgb, saturate(_Flash) * 0.5h * (1.0h - t));
            tc *= saturate(input.color.rgb);
            half a = body * alive * (t < 0.5h ? 1.0h : 0.8h) * saturate(input.color.a * 1.5h);
            half4 toonOut = half4(tc * a, a);
            return lerp(glowOut, toonOut, toon);
        }
        ENDHLSL

        // The visible part: on the face, hidden by anything in front of it.
        Pass
        {
            Name "FaceRibbon"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { return Ribbon(input); }
            ENDHLSL
        }
    }
}
