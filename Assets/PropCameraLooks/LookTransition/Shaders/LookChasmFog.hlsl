#ifndef CHASM_FOG_INCLUDED
#define CHASM_FOG_INCLUDED
#define LOOK_CHASM_FOG_INCLUDED

// LookTransition copy of Assets/Shaders/ChasmFog/ChasmFog.hlsl (the original is left untouched). Same globals, same maths,
// plus one thing: on the side of the screen the look wipe has already passed (LookWipe.hlsl) the backdrop colours take the
// next look's values. ChasmFog.cs keeps pushing the CURRENT look's colours; LookTransitionDirector pushes the difference
// "next minus current" into the _LTChasm*D globals. With the wipe idle (w = 0) every function here returns exactly what the
// original does. Uses the original's include guard, so the two can never both be compiled into one shader.

#include "LookWipe.hlsl"

float4 _ChasmFogTop;        // rgb: fog / background colour at the top of the screen
float4 _ChasmFogBottom;     // rgb: ... at the bottom
float4 _ChasmFogGlow;       // rgb: light added around the glow centre, a: strength
float4 _ChasmFogGlowShape;  // xy: glow centre (0-1 of the screen), z: radius in screen heights
float4 _ChasmFogHeight;     // x: world y where the fog begins, y: where it is solid, z: billow height, w: 1 / billow size
float4 _ChasmFogDepth;      // x: distance behind the camera (see _ChasmFogView) where the haze begins, y: where it peaks, z: peak amount
float4 _ChasmFogWind;       // xy: billow drift (world units, already multiplied by time)
float4 _ChasmFogScreen;     // x: screen height (0-1) below which rock is fully fogged, y: height where that fog ends, z: its strength, w: veil (fog over every rock)
float4 _ChasmFogView;       // xyz: camera forward flattened onto the ground, w: strength (0 = off, 1 = on)
float4 _ChasmFogViewPos;    // xyz: camera position
float4 _ChasmFogHouse;      // x: share of the fog a house and its rock take, y: fraction of that share at the house, z: 1 / depth under the house where the rock takes all of it

float4 _HouseWindowGlow;
float4 _HouseWindowGlowShape;

// LookTransition: next look minus current look (linear), applied where the wipe has passed.
float4 _LTChasmTopD;        // rgb
float4 _LTChasmBottomD;     // rgb
float4 _LTChasmGlowD;       // rgb, a: strength
float4 _LTChasmShapeD;      // xy: glow centre, z: radius
float4 _LTSunD;             // rgb: main light colour (x intensity), for the rocks' own lighting
float4 _LTAmbD;             // rgb: ambient colour, for the rocks' own lighting

half3 HouseWindowGlow(half3 albedo)
{
    half brightness = dot(albedo, half3(0.2126h, 0.7152h, 0.0722h));
    half warmth = albedo.r - albedo.b;

    half lit = smoothstep(_HouseWindowGlowShape.x, _HouseWindowGlowShape.x + max(_HouseWindowGlowShape.y, 1e-3), brightness)
             * smoothstep(_HouseWindowGlowShape.z, _HouseWindowGlowShape.z + max(_HouseWindowGlowShape.w, 1e-3), warmth);

    return albedo * _HouseWindowGlow.rgb * (lit * _HouseWindowGlow.a);
}

float ChasmFogHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float ChasmFogNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = ChasmFogHash(i);
    float b = ChasmFogHash(i + float2(1.0, 0.0));
    float c = ChasmFogHash(i + float2(0.0, 1.0));
    float d = ChasmFogHash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// The background: a vertical gradient with a soft glow, in screen space - in the look the wipe gives this pixel.
half3 ChasmFogColor(float2 screenUV)
{
    half w = LTWipeAt(screenUV);
    float3 top = _ChasmFogTop.rgb + _LTChasmTopD.rgb * w;
    float3 bottom = _ChasmFogBottom.rgb + _LTChasmBottomD.rgb * w;
    float4 glow = _ChasmFogGlow + _LTChasmGlowD * w;
    float3 shape = _ChasmFogGlowShape.xyz + _LTChasmShapeD.xyz * w;

    half3 c = lerp(bottom, top, smoothstep(0.0, 1.0, screenUV.y));
    float2 d = (screenUV - shape.xy) * float2(_ScaledScreenParams.x / _ScaledScreenParams.y, 1.0);
    float g = saturate(1.0 - length(d) / max(shape.z, 1e-3));
    return c + glow.rgb * (glow.a * g * g);
}

float ChasmFogAmount(float3 positionWS, float2 screenUV)
{
    float2 p = (positionWS.xz + _ChasmFogWind.xy) * _ChasmFogHeight.w;
    float billow = (ChasmFogNoise(p) + 0.5 * ChasmFogNoise(p * 2.37 + 17.0)) * (1.0 / 1.5) - 0.5;
    float top = _ChasmFogHeight.x + billow * _ChasmFogHeight.z;
    float h = saturate((top - positionWS.y) / max(_ChasmFogHeight.x - _ChasmFogHeight.y, 1e-3));
    h = h * h * (3.0 - 2.0 * h);

    float depth = dot(positionWS - _ChasmFogViewPos.xyz, _ChasmFogView.xyz);
    float d = saturate((depth - _ChasmFogDepth.x) / max(_ChasmFogDepth.y - _ChasmFogDepth.x, 1e-3)) * _ChasmFogDepth.z;

    float s = saturate((_ChasmFogScreen.y - screenUV.y) / max(_ChasmFogScreen.y - _ChasmFogScreen.x, 1e-3));
    s = s * s * (3.0 - 2.0 * s) * _ChasmFogScreen.z;

    return 1.0 - (1.0 - h) * (1.0 - d) * (1.0 - s) * (1.0 - _ChasmFogScreen.w);
}

float ChasmFogHouseAmount(float3 positionWS, float2 screenUV, float depthBelowHouse)
{
    float t = saturate(depthBelowHouse * _ChasmFogHouse.z);
    t = t * t * (3.0 - 2.0 * t);
    return ChasmFogAmount(positionWS, screenUV) * _ChasmFogHouse.x * lerp(_ChasmFogHouse.y, 1.0, t);
}

half3 ApplyChasmFog(half3 color, float3 positionWS, float4 positionCS)
{
    float2 uv = GetNormalizedScreenSpaceUV(positionCS);
    return lerp(color, ChasmFogColor(uv), ChasmFogAmount(positionWS, uv));
}

#endif
