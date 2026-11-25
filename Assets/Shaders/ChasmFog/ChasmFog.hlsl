#ifndef CHASM_FOG_INCLUDED
#define CHASM_FOG_INCLUDED

// Shared by the rock shader and the background, so fogged rock always ends in exactly the colour drawn behind it.
// Everything here is set each frame by ChasmFog.cs. With no ChasmFog running the strength (_ChasmFogView.w) is 0.

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

// Lit windows, set by NeonWindowDecorator (see the House Lights header). Global, not per material, so the houses stay
// SRP-batched: every house in a scene has its windows lit the same way.
float4 _HouseWindowGlow;        // rgb: colour of the light behind the windows, a: strength
float4 _HouseWindowGlowShape;   // x: brightness a texel needs before it counts as a lit window, y: how soft that edge is, z: how warm it must be, w: softness of that edge

// The window panes of the house models are painted into the albedo - a bright warm patch in an otherwise dull wall - so
// that is what is picked out here and turned into light. Bright-but-cold (plaster, stone) and warm-but-dark (roof tiles,
// timber, sand) both fall outside the band and stay unlit.
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

// The background: a vertical gradient with a soft glow, in screen space.
half3 ChasmFogColor(float2 screenUV)
{
    half3 c = lerp(_ChasmFogBottom.rgb, _ChasmFogTop.rgb, smoothstep(0.0, 1.0, screenUV.y));
    float2 d = (screenUV - _ChasmFogGlowShape.xy) * float2(_ScaledScreenParams.x / _ScaledScreenParams.y, 1.0);
    float g = saturate(1.0 - length(d) / max(_ChasmFogGlowShape.z, 1e-3));
    return c + _ChasmFogGlow.rgb * (_ChasmFogGlow.a * g * g);
}

// 0 = clear, 1 = lost in the fog.
// Height fog: rock sinks into a fog sea under the route. Its surface billows, so the line on the rock is never a flat cut.
// Depth haze: rock further behind the route than the shot's focus turns misty. Measured along the ground, not the tilted view,
// so a tall wall behind the route is hazy all the way up rather than crisp at the top (which the view ray counts as near).
// Screen fog: rock fades out towards the bottom of the screen, so the chasm below the route always reads as open, even when
// the route has dropped past rock that now stands level with it.
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

// A house and the rock it stands on sit in the same fog but only take a share of it, so they stay readable: the least on the
// house and the top of its rock, growing down the column. depthBelowHouse is 0 (or less) on the house itself.
float ChasmFogHouseAmount(float3 positionWS, float2 screenUV, float depthBelowHouse)
{
    float t = saturate(depthBelowHouse * _ChasmFogHouse.z);
    t = t * t * (3.0 - 2.0 * t);
    return ChasmFogAmount(positionWS, screenUV) * _ChasmFogHouse.x * lerp(_ChasmFogHouse.y, 1.0, t);
}

// positionCS is the fragment's SV_POSITION.
half3 ApplyChasmFog(half3 color, float3 positionWS, float4 positionCS)
{
    float2 uv = GetNormalizedScreenSpaceUV(positionCS);
    return lerp(color, ChasmFogColor(uv), ChasmFogAmount(positionWS, uv));
}

#endif
