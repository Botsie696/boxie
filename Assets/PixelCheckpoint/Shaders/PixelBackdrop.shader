// Pixel checkpoint: the cave behind everything (the reference's backdrop). Drawn as a screen-filling quad pushed to the far
// plane, after the sky and before the transparents, with a depth test - so it only ever shows where nothing else was drawn
// (rocks and path stay in front). Back to front:
//   far masses   big lighter-blue rock clouds in the middle distance
//   jellyfish    glowing cyan jellyfish rising slowly through the cave, domes pulsing, tentacles swaying and broken
//   pillars      dark cave walls at the screen's sides
//   vines        thin dark vines hanging from the ceiling with a few glowing leaves
//   ceiling      the dark cave roof with stalactite drips
//   plankton     tiny glowing specks drifting upward
// Everything is drawn smooth; the pixel-art pass (PixelPost) turns it into pixels. Visible only where the pixel look is
// in force (PixelCommon.hlsl, follows the look wipe). Driven by PixelCheckpointController (_PixBackdrop).
Shader "PixelCheckpoint/Backdrop"
{
    Properties
    {
        _MassColor ("Far Rock Mass", Color) = (0.27, 0.31, 0.93, 1)
        _MassColor2 ("Far Rock Mass (lit edge)", Color) = (0.30, 0.46, 0.93, 1)
        _MassOpacity ("Far Rock Opacity", Range(0, 1)) = 0.5
        _PillarColor ("Cave Pillars", Color) = (0.06, 0.01, 0.37, 1)
        _PillarEdge ("Cave Pillar Edge", Color) = (0.11, 0.11, 0.49, 1)
        _CeilingColor ("Ceiling", Color) = (0.04, 0.02, 0.28, 1)
        _VineColor ("Vines", Color) = (0.04, 0.02, 0.28, 1)
        _JellyRim ("Jelly Rim", Color) = (0.44, 1.0, 0.98, 1)
        _JellyTop ("Jelly Top", Color) = (0.36, 0.88, 0.95, 1)
        _JellyBody ("Jelly Body", Color) = (0.14, 0.62, 0.93, 1)
        _JellyBand ("Jelly Inner Band", Color) = (0.12, 0.47, 0.78, 1)
        _JellyTentacle ("Jelly Tentacles", Color) = (0.25, 0.61, 0.84, 1)
        _SpeckColor ("Plankton", Color) = (0.31, 0.78, 0.94, 1)
        _SpeckColor2 ("Plankton (bright)", Color) = (0.49, 0.94, 0.98, 1)
        _SpeckDensity ("Plankton Density", Range(0, 0.05)) = 0.006
        _JellyCount ("Jellyfish (0-7)", Range(0, 7)) = 6
        _JellySpeed ("Jelly Rise Speed", Range(0, 0.05)) = 0.012
        _CeilingHeight ("Ceiling Starts (screen height)", Range(0.5, 1)) = 0.86
        _PillarWidth ("Pillar Width (screen widths)", Range(0, 0.4)) = 0.12
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+502" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "PixelBackdrop"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/PixelCheckpoint/Shaders/PixelCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _MassColor, _MassColor2, _PillarColor, _PillarEdge, _CeilingColor, _VineColor;
                half4 _JellyRim, _JellyTop, _JellyBody, _JellyBand, _JellyTentacle, _SpeckColor, _SpeckColor2;
                half _MassOpacity, _SpeckDensity, _JellyCount, _JellySpeed, _CeilingHeight, _PillarWidth;
            CBUFFER_END
            float4 _PixBackdrop;   // x: offset from the camera's yaw (screen widths), y: time, zw: camera travel (screen heights, sideways / up)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float2 p = v.uv * 2.0 - 1.0;
                #if UNITY_REVERSED_Z
                    o.positionCS = float4(p, 1e-6, 1.0);
                #else
                    o.positionCS = float4(p, 1.0 - 1e-6, 1.0);
                #endif
                return o;
            }

            float Fbm(float2 p)
            {
                float a = PixNoise(float3(p, 0.5)) * 0.55;
                a += PixNoise(float3(p * 2.1 + 5.3, 1.5)) * 0.3;
                a += PixNoise(float3(p * 4.3 + 1.7, 2.5)) * 0.15;
                return a;
            }

            // "Over", premultiplied: c holds colour x coverage.
            void Over(inout half3 c, inout half a, half3 col, half alpha)
            {
                c = lerp(c, col, alpha);
                a = a + alpha * (1.0h - a);
            }

            // One jellyfish: q in units of its dome radius (y up), returns colour and coverage.
            void Jelly(float2 q, float t, float phase, float tentLen, inout half3 c, inout half a)
            {
                float pulse = 1.0 + 0.07 * sin(t * 1.7 + phase);
                float2 d = float2(q.x / pulse, q.y * pulse);
                // Tentacles: thin swaying strands, broken into dashes, under the dome.
                if (d.y < 0.0 && d.y > -tentLen && abs(d.x) < 1.1)
                {
                    half tent = 0.0h;
                    [unroll] for (int k = 0; k < 7; k++)
                    {
                        float xs = -0.72 + k * 0.24;
                        float len = tentLen * (0.55 + 0.45 * frac(sin((k + phase) * 12.9898) * 43758.5453));
                        float sway = 0.09 * sin(d.y * 3.2 + t * 1.6 + k * 1.3 + phase) * saturate(-d.y);
                        float w = (k == 3 ? 0.085 : 0.045);
                        float on = step(abs(d.x - xs - sway), w) * step(-len, d.y);
                        float gap = step(0.18, PixHash2(float2(floor(d.y * 7.0), k + phase * 3.0)));
                        tent = max(tent, on * gap * (1.0 - 0.6 * saturate(-d.y / len)));
                    }
                    Over(c, a, _JellyTentacle.rgb, tent * 0.85h);
                }
                // Dome: a half ellipse over a scalloped skirt.
                float skirt = -0.1 * (0.6 + 0.4 * cos(d.x * 18.0));
                float e = d.x * d.x + (d.y / 0.82) * (d.y / 0.82);
                if (e < 1.0 && d.y > skirt)
                {
                    float rim = step(0.72, e) + step(d.y, skirt + 0.08);
                    half3 col = lerp(_JellyBody.rgb, _JellyTop.rgb, smoothstep(0.15, 0.7, d.y));
                    col = lerp(col, _JellyBand.rgb, step(0.12, d.y) * step(d.y, 0.26) * step(abs(d.x), 0.78));
                    // A few lighter spots on top.
                    float2 spot = floor(d * 6.0);
                    col = lerp(col, _JellyRim.rgb, step(0.86, PixHash2(spot + phase)) * step(0.3, d.y));
                    col = lerp(col, _JellyRim.rgb, saturate(rim));
                    Over(c, a, col, 0.95h);
                }
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                half pix = PixAt(suv);
                if (pix < 0.001h) discard;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float t = _PixBackdrop.y;
                // Screen in height units, x centred.
                float2 p = float2((suv.x - 0.5) * aspect, suv.y);
                float yaw = _PixBackdrop.x;
                // Camera travel: nearer layers slide further (parallax), so the cave moves with the run.
                float2 cam = _PixBackdrop.zw;

                half3 c = 0;
                half a = 0;

                // ---- far rock masses ----
                {
                    float2 mp = float2(p.x + yaw * 0.35 + cam.x * 0.25 + t * 0.006, p.y - cam.y * 0.25 + 0.012 * sin(t * 0.35));
                    float dens = Fbm(mp * float2(2.4, 1.7) + 3.0);
                    half band = smoothstep(0.18, 0.35, p.y) * (1.0 - smoothstep(0.7, 0.9, p.y));
                    half mass = step(0.5, dens) * band;
                    half lit = step(0.5, Fbm((mp + float2(0.0, -0.012)) * float2(2.4, 1.7) + 3.0));
                    Over(c, a, lerp(_MassColor2.rgb, _MassColor.rgb, lit), mass * _MassOpacity);
                }

                // ---- slanted light shafts from the roof, slowly sweeping and breathing ----
                {
                    float sx = p.x + p.y * 0.35 + yaw * 0.2 + cam.x * 0.15;
                    float shaft = PixNoise(float3(sx * 6.0 + t * 0.05, 0.0, t * 0.12));
                    half on = step(0.68, shaft) * smoothstep(0.2, 0.85, p.y);
                    half breathe = 0.6h + 0.4h * sin(t * 0.7 + sx * 3.0);
                    Over(c, a, _MassColor2.rgb, on * 0.16h * breathe);
                }

                // ---- jellyfish ----
                {
                    int n = (int)_JellyCount;
                    [unroll] for (int j = 0; j < 7; j++)
                    {
                        if (j >= n) break;
                        float fj = j;
                        float size = lerp(0.028, 0.075, frac(fj * 0.618 + 0.31));
                        float speed = _JellySpeed * lerp(0.6, 1.4, frac(fj * 0.381 + 0.17));
                        float x0 = (frac(fj * 0.4142 + 0.2) - 0.5) * aspect * 0.9;
                        float yj = frac(fj * 0.2718 + t * speed) * 1.7 - 0.35;
                        float xj = x0 + 0.03 * sin(t * 0.3 + fj * 2.1) + (yaw + cam.x) * lerp(0.2, 0.5, size / 0.075);
                        yj -= cam.y * lerp(0.2, 0.5, size / 0.075);
                        float2 q = (p - float2(xj, yj)) / size;
                        if (abs(q.x) < 1.3 && q.y < 1.1 && q.y > -3.4)
                        {
                            half3 jc = 0; half ja = 0;
                            Jelly(q, t, fj * 1.7, 2.6 + fj * 0.12, jc, ja);
                            // Small (far) jellies sit further back in the haze.
                            half far = 1.0h - smoothstep(0.03, 0.07, size);
                            Over(c, a, lerp(jc, _MassColor2.rgb, far * 0.35h), ja * lerp(1.0h, 0.65h, far));
                        }
                    }
                }

                // ---- cave pillars at the sides ----
                {
                    float y = p.y - cam.y * 0.8;
                    float wL = _PillarWidth * (0.6 + 0.9 * Fbm(float2(y * 3.0 + yaw + cam.x, 1.3))) * smoothstep(0.0, 0.25, p.y + 0.1);
                    float wR = _PillarWidth * (0.5 + 1.0 * Fbm(float2(y * 3.0 - yaw - cam.x, 7.9))) * smoothstep(0.1, 0.4, p.y);
                    float xl = suv.x, xr = 1.0 - suv.x;
                    half inL = step(xl, wL), inR = step(xr, wR);
                    half edge = max(inL * step(wL - 0.012, xl), inR * step(wR - 0.012, xr));
                    Over(c, a, lerp(_PillarColor.rgb, _PillarEdge.rgb, edge), max(inL, inR));
                }

                // ---- hanging vines ----
                {
                    float cols = 150.0;
                    float cx = floor((suv.x + yaw * 0.6 + cam.x * 0.7) * cols);
                    float hv = PixHash2(float2(cx, 3.7));
                    if (hv < 0.06)
                    {
                        float len = lerp(0.12, 0.62, PixHash2(float2(cx, 9.1)));
                        float sway = floor(sin(p.y * 9.0 + cx + t * 1.4) * 1.2 + 0.5 * sin(t * 0.6 + cx));
                        half on = step(1.0 - len, p.y) * step(abs(sway), 0.5);
                        half leaf = step(0.93, PixHash2(float2(cx, floor(p.y * 90.0))));
                        Over(c, a, lerp(_VineColor.rgb, _SpeckColor.rgb, leaf), on);
                    }
                }

                // ---- ceiling with stalactite drips ----
                {
                    float roof = _CeilingHeight + 0.05 * (Fbm(float2((suv.x + cam.x * 0.9) * 5.0 + yaw, 2.2)) - 0.5);
                    float cols = 110.0;
                    float cx = floor((suv.x + yaw * 0.8 + cam.x * 0.9) * cols);
                    float drip = PixHash2(float2(cx, 1.1));
                    float dripLen = drip < 0.25 ? lerp(0.01, 0.09, PixHash2(float2(cx, 4.4))) : drip < 0.6 ? 0.012 : 0.0;
                    half roofOn = step(roof - dripLen, p.y);
                    half deep = step(roof + 0.05, p.y);
                    Over(c, a, lerp(_PillarEdge.rgb, _CeilingColor.rgb, saturate(deep + 0.5h)), roofOn);
                    // Glowing drops falling from the long drips.
                    if (drip < 0.25)
                    {
                        float fall = frac(t * lerp(0.25, 0.5, PixHash2(float2(cx, 7.7))) + PixHash2(float2(cx, 2.9)));
                        float dropY = roof - dripLen - fall * 0.55;
                        half drop = step(abs(p.y - dropY), 0.006) * (1.0h - fall);
                        Over(c, a, _SpeckColor2.rgb, drop);
                    }
                }

                // ---- plankton specks drifting up ----
                {
                    float cols = 170.0;
                    float2 g = float2((suv.x + yaw * 0.15 + cam.x * 1.2 + 0.01 * sin(t * 0.5 + p.y * 6.0)) * cols, (p.y - t * 0.014 - cam.y * 1.2) * cols);
                    float2 cell = floor(g);
                    float hs = PixHash2(cell);
                    if (hs < _SpeckDensity)
                    {
                        half tw = 0.5h + 0.5h * sin(t * (1.5 + hs * 60.0) + hs * 400.0);
                        Over(c, a, lerp(_SpeckColor.rgb, _SpeckColor2.rgb, step(0.5, frac(hs * 97.0))), 0.4h + 0.6h * tw);
                    }
                }

                return half4(c * pix, a * pix);   // c is premultiplied (Over)
            }
            ENDHLSL
        }
    }
}
