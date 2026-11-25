// Voxel checkpoint: the sky behind everything (the reference's backdrop). A screen-filling quad pushed to the far plane,
// drawn after the opaque scene with a depth test, so it only shows where nothing else was drawn. All on a coarse pixel
// grid (blocky, never smooth). Back to front:
//   sky        bright blue at the top to a pale blue haze, in flat pixel bands
//   clouds     blocky white cumulus, a shaded blue-grey underside and a lit top row, two layers drifting
//   mountains  pale stepped peaks far away, lost in the haze
//   hills      two rows of blocky grass hills with dirt / brick under the turf, square-crowned pixel trees, mushroom
//              trees and the odd brick tower with a lit window, rising out of a pale valley haze
// Visible only where the voxel look is in force (VoxelCommon.hlsl, follows the look wipe). Driven by
// VoxelCheckpointController (_VxBackdrop).
Shader "VoxelCheckpoint/Backdrop"
{
    Properties
    {
        _SkyTop ("Sky Top", Color) = (0.1, 0.45, 0.9, 1)
        _SkyMid ("Sky Mid", Color) = (0.3, 0.68, 0.95, 1)
        _SkyLow ("Sky Low (haze)", Color) = (0.78, 0.92, 0.98, 1)
        _Cloud ("Cloud", Color) = (1, 1, 1, 1)
        _CloudShade ("Cloud Shade", Color) = (0.72, 0.82, 0.95, 1)
        _Mountain ("Mountain", Color) = (0.7, 0.75, 0.9, 1)
        _HillGrass ("Hill Grass", Color) = (0.35, 0.72, 0.25, 1)
        _HillDirt ("Hill Dirt", Color) = (0.6, 0.38, 0.22, 1)
        _Brick ("Brick", Color) = (0.72, 0.3, 0.18, 1)
        _Haze ("Valley Haze", Color) = (0.82, 0.93, 0.95, 1)
        _PixelRows ("Pixel Rows (screen)", Float) = 160
        _CloudCover ("Cloud Cover", Range(0, 1)) = 0.5
        _CloudSpeed ("Cloud Drift", Range(0, 0.05)) = 0.006
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+502" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "VoxelBackdrop"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/VoxelCheckpoint/Shaders/VoxelCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SkyTop, _SkyMid, _SkyLow, _Cloud, _CloudShade, _Mountain, _HillGrass, _HillDirt, _Brick, _Haze;
                float _PixelRows, _CloudCover, _CloudSpeed;
            CBUFFER_END
            float4 _VxBackdrop;   // x: offset from the camera's yaw (screen widths), y: time, zw: camera travel (screen heights)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float2 p = v.uv * 2.0 - 1.0;
                #if UNITY_REVERSED_Z
                    o.positionCS = float4(p, 1e-6, 1.0);
                #else
                    o.positionCS = float4(p, 1.0 - 1e-6, 1.0);
                #endif
                o.uv = v.uv;
                #if UNITY_UV_STARTS_AT_TOP
                    if (_ProjectionParams.x < 0) o.uv.y = 1.0 - o.uv.y;
                #endif
                return o;
            }

            float Fbm(float2 p)
            {
                float a = PixNoise(float3(p, 0.5)) * 0.55;
                a += PixNoise(float3(p * 2.03 + 5.3, 1.5)) * 0.28;
                a += PixNoise(float3(p * 4.1 + 1.7, 2.5)) * 0.17;
                return a;
            }

            // Blocky cloud field on the pixel grid g (in pixels). True where there is cloud.
            bool CloudAt(float2 g, float scale, float seed, float cover)
            {
                float2 q = g / scale;
                float f = Fbm(float2(q.x * 0.55, q.y * 1.4) + seed);
                // Fade toward the horizon: clouds sit in the upper sky.
                return f > 1.0 - cover * 0.55;
            }

            void Clouds(float2 g, float scale, float seed, float cover, inout half3 c)
            {
                if (!CloudAt(g, scale, seed, cover)) return;
                half3 col = _Cloud.rgb;
                if (!CloudAt(g - float2(0.0, 2.0), scale, seed, cover)) col = _CloudShade.rgb;          // underside
                else if (!CloudAt(g - float2(0.0, 4.0), scale, seed, cover)) col = lerp(_Cloud.rgb, _CloudShade.rgb, 0.45h);
                if (!CloudAt(g + float2(1.0, 1.0), scale, seed, cover)) col = _Cloud.rgb * 1.02h;       // lit top / right edge
                c = col;
            }

            // A row of blocky hills: height per 4-pixel column, stepped. g in pixels, rows = pixels per screen height.
            void Hills(float2 g, float baseY, float amp, float colW, float seed, half fade, inout half3 c)
            {
                float col = floor(g.x / colW);
                float hN = Fbm(float2(col * 0.09, seed));
                float top = baseY + floor(hN * amp / 2.0) * 2.0;
                float hc = PixHash2(float2(col, seed + 3.0));
                // Square-crowned trees, mushroom trees and towers on the crest.
                float local = g.x - col * colW;
                if (g.y > top)
                {
                    float cx = floor(g.x / (colW * 3.0));
                    float th = PixHash2(float2(cx, seed + 7.0));
                    float x0 = cx * colW * 3.0 + floor(PixHash2(float2(cx, seed + 8.0)) * colW * 2.0);
                    float dx = g.x - x0;
                    float colTop = baseY + floor(Fbm(float2(floor(x0 / colW) * 0.09, seed)) * amp / 2.0) * 2.0;
                    float dy = g.y - colTop;
                    half3 hit = 0; bool any = false;
                    if (th < 0.55)
                    {
                        // Tree: 1-px trunk 3 high, a crown 7 wide x 5 high.
                        if (dx >= 3.0 && dx < 4.0 && dy < 3.0) { hit = half3(0.35h, 0.22h, 0.12h); any = true; }
                        if (dx >= 0.0 && dx < 7.0 && dy >= 3.0 && dy < 8.0 && !((dx < 1.0 || dx >= 6.0) && (dy >= 7.0 || dy < 4.0)))
                        {
                            half3 leaf = th < 0.3 ? _HillGrass.rgb * 0.75h : th < 0.42 ? half3(0.85h, 0.42h, 0.12h) : half3(0.18h, 0.42h, 0.38h);
                            hit = leaf * (PixHash(float3(g, seed)) > 0.8 ? 1.25h : 1.0h);
                            any = true;
                        }
                    }
                    else if (th < 0.68)
                    {
                        // Mushroom tree: pale stem, red cap with white dots.
                        if (dx >= 3.0 && dx < 5.0 && dy < 6.0) { hit = half3(0.92h, 0.86h, 0.72h); any = true; }
                        if (dx >= 0.0 && dx < 8.0 && dy >= 6.0 && dy < 9.0 && !((dx < 1.0 || dx >= 7.0) && dy >= 8.0))
                        {
                            hit = (fmod(dx + dy, 3.0) < 1.0 && dy >= 7.0) ? half3(1, 1, 1) : half3(0.86h, 0.16h, 0.12h);
                            any = true;
                        }
                    }
                    else if (th < 0.76)
                    {
                        // Brick tower with crenellations and a lit window.
                        if (dx >= 0.0 && dx < 7.0 && dy < 16.0 && !(dy >= 15.0 && fmod(dx, 2.0) >= 1.0))
                        {
                            hit = _Brick.rgb * ((fmod(dy, 2.0) < 1.0 || fmod(dx + floor(dy / 2.0) * 2.0, 4.0) < 1.0) ? 0.72h : 1.0h);
                            if (dx >= 3.0 && dx < 4.0 && dy >= 10.0 && dy < 12.0) hit = half3(1.0h, 0.8h, 0.35h);
                            any = true;
                        }
                    }
                    if (!any) return;
                    c = lerp(hit, _Haze.rgb, fade);
                    return;
                }
                half3 col2;
                float below = top - g.y;
                if (below < 2.0) col2 = _HillGrass.rgb * (PixHash(float3(g, seed)) > 0.75 ? 1.18h : 1.0h);
                else if (below < 3.0 + floor(hc * 3.0) && PixHash2(float2(g.x, seed + 1.0)) > 0.4) col2 = _HillGrass.rgb * 0.8h;   // drips
                else
                {
                    bool brick = hc > 0.6;
                    col2 = brick ? _Brick.rgb : _HillDirt.rgb;
                    if (brick && (fmod(g.y, 3.0) < 1.0 || fmod(g.x + floor(g.y / 3.0) * 3.0, 6.0) < 1.0)) col2 *= 0.7h;
                    else col2 *= (PixHash(float3(g, seed + 2.0)) > 0.85 ? 0.85h : 1.0h);
                }
                // Each hill column's left edge in a slightly darker tone (block faces).
                if (local < 1.0 && Fbm(float2((col - 1.0) * 0.09, seed)) > hN) col2 *= 0.85h;
                c = lerp(col2, _Haze.rgb, fade);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half vx = VxAt(uv);
                if (vx < 0.001h) return 0;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float yaw = _VxBackdrop.x;
                float t = _VxBackdrop.y;
                float2 cam = _VxBackdrop.zw;
                float rows = _PixelRows;
                float2 g = floor(float2(uv.x * aspect, uv.y) * rows);      // the pixel grid

                // ---- sky in flat pixel bands ----
                float sy = floor(uv.y * 22.0) / 22.0;
                half3 c = sy > 0.5 ? lerp(_SkyMid.rgb, _SkyTop.rgb, (half)saturate((sy - 0.5) / 0.5))
                                   : lerp(_SkyLow.rgb, _SkyMid.rgb, (half)saturate((sy - 0.15) / 0.35));

                // ---- clouds: far small, then big ----
                float drift = t * _CloudSpeed * rows;
                float2 gc = g + float2(floor(drift * 0.5 + (yaw * 0.3 + cam.x * 0.3) * rows), floor(-cam.y * 0.2 * rows));
                if (uv.y > 0.45) Clouds(gc, 5.0, 3.0, _CloudCover * 0.8, c);

                // ---- far stepped mountains ----
                {
                    float2 gm = g + float2(floor((yaw * 0.15 + cam.x * 0.15) * rows), 0.0);
                    float colM = floor(gm.x / 3.0);
                    float hM = Fbm(float2(colM * 0.035, 9.0));
                    float topM = rows * 0.3 + floor(hM * rows * 0.32 / 3.0) * 3.0 - floor(abs(frac(colM * 0.021) - 0.5) * 30.0);
                    if (g.y < topM)
                    {
                        half3 mc = _Mountain.rgb * (PixHash2(float2(colM, 4.0)) > 0.5 ? 1.0h : 0.94h);
                        if (topM - g.y < 3.0) mc = lerp(mc, half3(1, 1, 1), 0.45h);
                        c = lerp(mc, _SkyLow.rgb, 0.35h);
                    }
                }

                float2 g2 = g + float2(floor(drift + (yaw * 0.6 + cam.x * 0.6) * rows), floor(-cam.y * 0.3 * rows));
                if (uv.y > 0.5) Clouds(g2, 9.0, 11.0, _CloudCover, c);

                // ---- hills, far then near ----
                float2 gf = g + float2(floor((yaw * 0.3 + cam.x * 0.3) * rows), floor(-cam.y * 0.12 * rows));
                Hills(gf, rows * 0.2, rows * 0.16, 4.0, 21.0, 0.45h, c);
                float2 gn = g + float2(floor((yaw * 0.5 + cam.x * 0.5) * rows) + 37.0, floor(-cam.y * 0.2 * rows));
                Hills(gn, rows * 0.1, rows * 0.16, 5.0, 31.0, 0.15h, c);

                // ---- valley haze over their feet ----
                half haze = (half)saturate((0.2 - floor(uv.y * 40.0) / 40.0) / 0.2);
                c = lerp(c, _Haze.rgb, haze * 0.85h);

                return half4(c * vx, vx);
            }
            ENDHLSL
        }
    }
}
