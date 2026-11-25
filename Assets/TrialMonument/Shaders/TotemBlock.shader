// The yellow 4-block "Totem" from Monument Valley, drawn procedurally on a plain Unity cube: no
// textures, every design is a signed-distance shape evaluated per pixel. One shader, one material per
// block - _BlockIndex (1 top .. 4 bottom) picks the design.
//
//   1  eye on one side face (white disc, magenta pupil set to the left), nested V lines on the other,
//      concentric squares on top
//   2  maze: nested L-shaped paths entering from the bottom and turning out to the right edge
//   3  two paths entering from the bottom and turning out to the sides on one face, a rivet on the other
//   4  a rivet on every side face
//   0  plain: just _BaseColor with the face tints - for the 3D eye / rivet discs stuck on the blocks
//
// Faces are found from the cube's OWN geometry turned into world orientation, not from mesh UVs, so the
// designs stand upright ("up" is world up) however the cube is rotated - the tower cubes in the scene
// are rotated about X - and the eye face / pattern face follow world X / Z (_SwapFaces flips them).
//
// Unlit, with one flat tint per face direction (_FaceTintX/Z/Top), so it reads as a solid block the way
// the path cubes do. Defaults are balanced to the path cubes' on-screen brightness in MainScene prop.
Shader "TrialMonument/TotemBlock"
{
    Properties
    {
        [IntRange] _BlockIndex ("Block Index (1 top .. 4 bottom, 0 = plain part)", Range(0, 4)) = 1
        [Toggle] _SwapFaces ("Swap Eye / Pattern Faces", Float) = 0

        [Header(Colours)]
        _BaseColor ("Block Colour", Color) = (0.96, 0.77, 0.18, 1)
        _LineColor ("Line Colour", Color) = (0.8, 0.8, 0.95, 1)
        _AccentColor ("Rivet / Edge Colour", Color) = (1, 0.55, 0.2, 1)
        _EyeColor ("Eye White", Color) = (1, 1, 1, 1)
        _PupilColor ("Pupil", Color) = (0.55, 0.08, 0.4, 1)

        [Header(Brightness and Shading)]
        _Brightness ("Brightness", Range(0.2, 1.5)) = 1
        _FaceTintX ("Tint: faces along X", Color) = (0.97, 0.87, 0.78, 1)
        _FaceTintZ ("Tint: faces along Z", Color) = (1, 1, 1, 1)
        _FaceTintTop ("Tint: top face", Color) = (1, 1, 1, 1)

        [Header(Shapes)]
        _LineWidth ("Line Width", Range(0.02, 0.2)) = 0.1
        _EdgeBand ("Top Edge Band", Range(0, 0.2)) = 0.06
        _EyeRadius ("Eye Radius", Range(0.1, 0.45)) = 0.32
        _PupilRadius ("Pupil Radius", Range(0.05, 0.35)) = 0.2
        _PupilOffset ("Pupil Offset (x, y)", Vector) = (-0.06, -0.03, 0, 0)
        _RivetRadius ("Rivet Radius", Range(0.1, 0.45)) = 0.28

        [Header(Depth)]
        _CircleShadow ("Circle Contact Shadow", Range(0, 1)) = 0.55
        _CircleShadowWidth ("Circle Shadow Width", Range(0.005, 0.2)) = 0.07
        _CircleShadowDrop ("Circle Shadow Drop", Range(0, 0.15)) = 0.04
        _WallShade ("Part Side Wall Shade", Range(0, 1)) = 0.6
        _WallShadeDown ("Part Underside Extra Shade", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _BlockIndex;
                float _SwapFaces;
                half4 _BaseColor;
                half4 _LineColor;
                half4 _AccentColor;
                half4 _EyeColor;
                half4 _PupilColor;
                half _Brightness;
                half4 _FaceTintX;
                half4 _FaceTintZ;
                half4 _FaceTintTop;
                float _LineWidth;
                float _EdgeBand;
                float _EyeRadius;
                float _PupilRadius;
                float4 _PupilOffset;
                float _RivetRadius;
                half _CircleShadow;
                float _CircleShadowWidth;
                float _CircleShadowDrop;
                half _WallShade;
                half _WallShadeDown;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 local      : TEXCOORD0;   // cube-space position, turned to world orientation (-0.5..0.5)
                float3 normal     : TEXCOORD1;   // face normal, same orientation
                float  fog        : TEXCOORD2;
                float  capness    : TEXCOORD3;   // |object-space normal.y|: 1 on a cylinder's caps, 0 on its side wall
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                // Rotation only: the columns of the object matrix, normalised, so scale does not
                // change the -0.5..0.5 face coordinates.
                float3x3 m = (float3x3)GetObjectToWorldMatrix();
                float3 c0 = normalize(float3(m[0][0], m[1][0], m[2][0]));
                float3 c1 = normalize(float3(m[0][1], m[1][1], m[2][1]));
                float3 c2 = normalize(float3(m[0][2], m[1][2], m[2][2]));
                o.local  = c0 * v.positionOS.x + c1 * v.positionOS.y + c2 * v.positionOS.z;
                o.normal = c0 * v.normalOS.x + c1 * v.normalOS.y + c2 * v.normalOS.z;
                o.capness = abs(v.normalOS.y);

                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // ---- signed distances (negative inside) ----
            float sdCircle(float2 p, float2 c, float r) { return length(p - c) - r; }

            float sdSegment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            // A polyline of two segments (a -> b -> c), as one stroke.
            float sdPath(float2 p, float2 a, float2 b, float2 c)
            {
                return min(sdSegment(p, a, b), sdSegment(p, b, c));
            }

            // Anti-aliased coverage of a distance field, about one pixel soft.
            float Fill(float d)
            {
                float w = max(fwidth(d), 1e-5);
                return saturate(0.5 - d / w);
            }

            float Stroke(float d) { return Fill(d - _LineWidth * 0.5); }

            // A soft dark shadow on the face around a circle that stands out of it, dropped a little
            // below it (light from above) - what makes the 3D discs read as sitting ON the block.
            half3 ContactShadow(float2 uv, float2 c, float r, half3 col)
            {
                float d = sdCircle(uv, c + float2(0, -_CircleShadowDrop), r);
                float s = 1.0 - smoothstep(0.0, _CircleShadowWidth, d);
                return col * (1.0 - _CircleShadow * s);
            }

            half3 Rivet(float2 uv, half3 col)
            {
                float2 c = float2(0.5, 0.5);
                col = ContactShadow(uv, c, _RivetRadius, col);
                col = lerp(col, _AccentColor.rgb, Fill(sdCircle(uv, c, _RivetRadius)));
                col = lerp(col, _BaseColor.rgb,   Fill(sdCircle(uv, c, _RivetRadius * 0.64)));
                col = lerp(col, _AccentColor.rgb, Fill(sdCircle(uv, c, _RivetRadius * 0.32)));
                return col;
            }

            half3 Eye(float2 uv, half3 col)
            {
                float2 c = float2(0.5, 0.5);
                col = ContactShadow(uv, c, _EyeRadius, col);
                col = lerp(col, _EyeColor.rgb,   Fill(sdCircle(uv, c, _EyeRadius)));
                col = lerp(col, _PupilColor.rgb, Fill(sdCircle(uv, c + _PupilOffset.xy, _PupilRadius)));
                return col;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 d = i.local;
                int block = (int)round(_BlockIndex);
                half3 col = _BaseColor.rgb;

                if (block == 0)
                {
                    // Plain part (the 3D eye / rivet discs stuck on the blocks). The flat front cap keeps
                    // the colour; the side wall is shaded darker, darkest where it faces down, so every
                    // disc has a hard lit-edge / dark-edge like the bevels in the reference.
                    float wall = 1.0 - saturate(i.capness);
                    half wallTint = lerp(_WallShade, 1.0, saturate(n.y)) * (1.0 - _WallShadeDown * saturate(-n.y));
                    col *= lerp(1.0, wallTint, wall);
                }
                else if (abs(n.y) > 0.7)
                {
                    // Top (and bottom) face. Only the top block's top is ever seen: nested squares.
                    if (block == 1 && n.y > 0)
                    {
                        float2 p = abs(d.xz);
                        float sq = max(p.x, p.y);
                        float rings = min(min(abs(sq - 0.14), abs(sq - 0.28)), abs(sq - 0.42));
                        col = lerp(col, _LineColor.rgb, Stroke(rings));
                    }
                }
                else
                {
                    // Side face: u runs to the viewer's right as they look at the face, v runs up.
                    float3 right = normalize(cross(n, float3(0, 1, 0)));
                    float2 uv = float2(dot(d, right), d.y) + 0.5;

                    bool faceA = abs(n.x) > abs(n.z);          // eye / rivet face
                    if (_SwapFaces > 0.5) faceA = !faceA;

                    if (block == 1)
                    {
                        if (faceA) col = Eye(uv, col);
                        else
                        {
                            float v1 = sdPath(uv, float2(0.12, 1.0), float2(0.5, 0.14), float2(0.88, 1.0));
                            float v2 = sdPath(uv, float2(0.33, 1.0), float2(0.5, 0.58), float2(0.67, 1.0));
                            col = lerp(col, _LineColor.rgb, Stroke(min(v1, v2)));
                        }
                    }
                    else if (block == 2)
                    {
                        // The L from bottom centre up to the middle and out to the right edge, with a
                        // path nested either side of it - the maze look of the reference.
                        float l1 = sdPath(uv, float2(0.5, 0.0), float2(0.5, 0.5), float2(1.0, 0.5));
                        float l2 = sdPath(uv, float2(0.22, 0.0), float2(0.22, 0.78), float2(1.0, 0.78));
                        float l3 = sdPath(uv, float2(0.78, 0.0), float2(0.78, 0.22), float2(1.0, 0.22));
                        col = lerp(col, _LineColor.rgb, Stroke(min(l1, min(l2, l3))));
                    }
                    else if (block == 3)
                    {
                        if (faceA) col = Rivet(uv, col);
                        else
                        {
                            // Two paths in from the bottom, each turning away to its own side.
                            float a = sdPath(uv, float2(0.38, 0.0), float2(0.38, 0.55), float2(0.0, 0.55));
                            float b = sdPath(uv, float2(0.62, 0.0), float2(0.62, 0.55), float2(1.0, 0.55));
                            float top = sdSegment(uv, float2(0.0, 0.82), float2(1.0, 0.82));
                            col = lerp(col, _LineColor.rgb, Stroke(min(min(a, b), top)));
                        }
                    }
                    else
                    {
                        col = Rivet(uv, col);
                    }

                    // The darker lip along the top of every side face, where one block sits on the next.
                    if (_EdgeBand > 0) col = lerp(col, _AccentColor.rgb, Fill((1.0 - _EdgeBand) - uv.y));
                }

                // One flat tint per face direction, like the flat-shaded faces of the reference (and the
                // path cubes beside it): nothing view- or light-dependent, never brighter than the colours.
                half3 tint = abs(n.y) > 0.7 ? _FaceTintTop.rgb : (abs(n.x) > abs(n.z) ? _FaceTintX.rgb : _FaceTintZ.rgb);
                col *= tint * _Brightness;

                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // Lets the blocks cast shadows and write depth like the path cubes.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; };

            V vertShadow(A v)
            {
                V o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nw, _LightDirection));
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };

            V vertDepth(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half4 fragDepth(V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
