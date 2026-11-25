#ifndef SOUL_COMMON_INCLUDED
#define SOUL_COMMON_INCLUDED

// Soul cubes: shared helpers. The tone matches the game's Monument Valley look (TotemPlayer / the path): faces are
// lit by their direction relative to the camera, not by lights - top brightest, one side lit, the other in shade,
// the bottom darkest - only softer, so a character's colours stay friendly.

float4 _DCPlayer;   // 3.7: x = +1 the camera's right side is the lit one, -1 the left (0 = right)

half SoulTone(float3 nWS, half top, half lit, half shade, half bottom)
{
    float3 camRight = normalize(UNITY_MATRIX_V[0].xyz);
    float3 flat = nWS - float3(0, nWS.y, 0);
    float side = dot(flat, camRight) / max(length(flat), 1e-4) * (_DCPlayer.x < 0 ? -1.0 : 1.0);
    half sideTone = lerp(shade, lit, smoothstep(-0.3, 0.3, side));
    half up = saturate(nWS.y); up *= up;
    half down = saturate(-nWS.y); down *= down;
    return lerp(lerp(sideTone, top, up), bottom, down);
}

// Anti-aliased step: 1 where d < 0.
half SoulFill(float d) { float w = max(fwidth(d), 1e-5); return saturate(0.5 - d / w); }

// A square wave from a sine, anti-aliased: 1 on the "on" half.
half SoulStripe(float x)
{
    float s = sin(x * 6.2831853);
    return saturate(0.5 + s / (2.0 * max(fwidth(s), 1e-4)));
}

#endif
