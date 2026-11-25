#ifndef LOOK_WIPE_INCLUDED
#define LOOK_WIPE_INCLUDED

// LookTransition (MainScene prop camera 3.2): the screen wipe that repaints the world from one look ("A", the look in
// force) to the next ("B"). Every LookTransition shader copy asks the same question per pixel - how far is this pixel
// into B - so cubes, sky, fog backdrop and rocks all change along one edge. Set by LookTransitionDirector.
//
// _LTWipe  x: the edge's position across the screen (0 = left edge, 1 = right edge; it sweeps left to right)
//          y: half-width of the soft edge, in screen widths
//          z: slant - how far the edge is shifted per screen height (negative = the top leads)
//          w: 1 while the look slots are in use; 0 (the default everywhere else) = every pixel shows A

float4 _LTWipe;

// 0 = look A, 1 = look B. screenUV is 0..1 with y = 0 at the bottom (GetNormalizedScreenSpaceUV).
half LTWipeAt(float2 screenUV)
{
    if (_LTWipe.w < 0.5) return 0.0h;
    float x = screenUV.x + (screenUV.y - 0.5) * _LTWipe.z;
    float soft = max(_LTWipe.y, 1e-4);
    return (half)(1.0 - smoothstep(_LTWipe.x - soft, _LTWipe.x + soft, x));
}

#endif
