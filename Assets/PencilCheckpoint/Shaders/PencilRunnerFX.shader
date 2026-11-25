// Pencil checkpoint: the runner's extra trail effects while the pencil look is in force (PencilRunnerFX.cs). Graphite on
// the paper, alpha blended, depth tested, only where the pencil look is on screen (PcAt, follows the look wipe).
//   _Mode 0  Front sketch: the leg ahead of the runner being drawn just before it gets there - two wobbly construction
//            lines along the face, a dashed centre guide, light hatching that thins out ahead, and a scribbling pencil
//            tip at the far end. Lines "boil" (redrawn a few times a second) like a pencil animation.
//            uv0 = (world units along the leg, across the face -0.5..0.5), uv1.x = 0 at the runner .. 1 at the far end.
//   _Mode 1  Fleck: a graphite speck / pencil shaving shed behind the runner (particle; vertex colour r = seed, a = fade).
//   _Mode 2  Streak: a hand-drawn speed line (stretched particle).
Shader "PencilCheckpoint/RunnerFX"
{
    Properties
    {
        [Enum(Front,0,Fleck,1,Streak,2)] _Mode ("Mode", Float) = 0
        _Strength ("Strength", Range(0, 1.5)) = 0.85
        _Alpha ("Alpha (script)", Range(0, 1)) = 1
        _Boil ("Line Boil (redraws per second)", Range(0, 24)) = 8
        _LineWidth ("Line Half Width (face units)", Range(0.002, 0.05)) = 0.012
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/PencilCheckpoint/Shaders/PencilCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _Mode, _Strength, _Alpha, _Boil, _LineWidth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "PencilRunnerFX"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; half4 color : COLOR; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.uv1 = v.uv1;
                o.color = v.color;
                return o;
            }

            half Front(Varyings i)
            {
                float2 p = i.uv;                    // x along the leg (world, so strokes stay put as the runner nears), y across
                float t = saturate(i.uv1.x);        // 0 runner .. 1 far end
                float aa = max(fwidth(p.x), fwidth(p.y)) + 1e-4;
                float frame = floor(_Time.y * _Boil);  // the line boil: a new hand every frame
                float w = _LineWidth;

                // Two construction lines near the face's edges, each a double wobbly stroke.
                half ink = PcSketchEdge(p.y - 0.36, p.x * 2.0, w, aa, 3.0 + frame * 1.3) * 0.9h;
                ink = max(ink, PcSketchEdge(p.y + 0.36, p.x * 2.0, w, aa, 11.0 + frame * 0.7) * 0.9h);

                // A dashed guide down the middle: long dashes near the runner, shorter and fainter ahead.
                float wob = sin(p.x * 3.1 + frame * 2.1) * 0.012 + sin(p.x * 8.3 + frame) * 0.006;
                float dashLen = lerp(0.75, 0.35, t);
                half dash = (half)step(frac(p.x * 1.6 + PcHash(frame) * 0.15), dashLen);
                ink = max(ink, PcLine(p.y - wob, w * 1.1, aa) * dash * PcGrain(p, 5.0) * (half)lerp(1.0, 0.55, t));

                // Light diagonal hatching between the lines, dense at the runner, gone before the tip.
                half inside = (half)(1.0 - smoothstep(0.3, 0.34, abs(p.y)));
                half hatch = PcHatch(p * float2(1.0, 1.0), 0.9, 0.09, w * 0.8, aa, 17.0 + frame * 0.5);
                ink = max(ink, hatch * inside * 0.75h * (half)(1.0 - smoothstep(0.15, 0.7, t)));

                // Everything is still being drawn: strokes thin out unevenly toward the far end.
                half drawn = (half)(1.0 - smoothstep(0.72, 0.96, t + (PixHash(float3(floor(p.x * 6.0), floor(p.y * 9.0), frame)) - 0.5) * 0.18));
                ink *= drawn;

                // The pencil tip at the far end: a small tight scribble that jitters every boil frame.
                float2 tipC = float2(0.0, 0.0);
                float2 q = float2((t - 0.9) * 6.0, p.y * 2.2);     // roughly round on the face
                float r = length(q - tipC);
                float ang = atan2(q.y, q.x);
                float loops = sin(ang * 3.0 + frame * 2.7) * 0.18 + sin(ang * 7.0 - frame * 1.9) * 0.08;
                half tip = PcLine(r - 0.35 - loops, 0.05, fwidth(r) + 1e-4) * (half)step(r, 0.9);
                tip = max(tip, PcLine(r - 0.18 - loops * 0.5, 0.04, fwidth(r) + 1e-4) * 0.8h);
                ink = max(ink, tip);

                // Fade in off the runner (so nothing is drawn under it) and out past the tip.
                ink *= (half)(smoothstep(0.0, 0.08, t) * (1.0 - smoothstep(0.97, 1.0, t)));
                return ink;
            }

            half Fleck(Varyings i)
            {
                float2 c = i.uv * 2.0 - 1.0;
                float seed = i.color.r * 37.0;
                float ang = atan2(c.y, c.x);
                // An uneven chip of graphite: a lumpy outline, darker core, grain.
                float lump = sin(ang * 3.0 + seed) * 0.16 + sin(ang * 5.0 + seed * 2.3) * 0.09;
                float d = length(c) - (0.62 + lump);
                float aa = fwidth(d) + 1e-4;
                half body = (half)(1.0 - smoothstep(-aa, aa, d));
                half grain = (half)lerp(0.55, 1.0, PixHash(float3(floor(i.uv * 6.0), seed)));
                // Every other one is a curl of shaving: a hollow ring with a gap.
                half shaving = (half)step(0.55, frac(seed * 0.173));
                half ring = PcLine(length(c) - 0.55 - lump * 0.5, 0.12, aa) * (half)step(0.0, sin(ang + seed));
                return lerp(body * grain, ring, shaving);
            }

            half Streak(Varyings i)
            {
                // A soft-ended line along whichever way the particle was stretched, broken by the paper's tooth.
                float2 c = abs(i.uv * 2.0 - 1.0);
                half ends = (half)(1.0 - smoothstep(0.5, 1.0, max(c.x, c.y)));
                half grain = (half)lerp(0.45, 1.0, PixNoise(float3(i.uv * float2(9.0, 9.0), i.color.r * 13.0)));
                return ends * grain;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half pc = PcAt(GetNormalizedScreenSpaceUV(i.positionCS));
                if (pc < 0.001h) return 0;
                half ink = _Mode < 0.5 ? Front(i) : _Mode < 1.5 ? Fleck(i) : Streak(i);
                half a = saturate(ink) * (half)_PcGraphite.a * (half)_Strength * (half)_Alpha * pc;
                if (_Mode > 0.5) a *= i.color.a;
                return half4(_PcGraphite.rgb, a);
            }
            ENDHLSL
        }
    }
}
