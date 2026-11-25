#ifndef VOXEL_CP_COMMON_INCLUDED
#define VOXEL_CP_COMMON_INCLUDED

// Voxel checkpoint ("Brick Meadow", MainScene prop camera 3.10 checkpoint 7 + MainScene prop camera Voxel): shared helpers
// for the blocky Minecraft / Super Mario meadow (reference: a voxel Mario scene - yellow slab bridge on red bricks with
// the yellow dripping over the bricks, pixel grass, pixel flowers, square-crowned trees, a brick tower, a blue block
// house, blocky white clouds on a bright blue sky, blue water below).
//
// Where the look is on screen: VoxelCheckpointController writes _VxLook every frame - x = 1 when the look in force (the
// director's _LTA slots) is the voxel look, y = 1 when the look being wiped in (_LTB) is, z = 1 for a scene that is voxel
// for the whole run. Without the controller everything here is zero = no change.
//
// Everything is drawn as pixel textures: a surface coordinate in "voxel units" (uv), a number of texture pixels per voxel,
// and a kind (grass, brick, ...). Colours are flat per texture pixel, varied by a hash - no smooth gradients.

#include "Assets/PixelCheckpoint/Shaders/PixelCommon.hlsl"   // LookWipe + the hashes / noise

float4 _VxLook;          // x look A is voxel, y look B is, z always
float4 _VxLegTops[10];   // leg slab colours (linear), one per leg in turn
float4 _VxLegBricks[6];  // leg brick colours (linear)
float4 _VxGrass;         // rgb grass (linear), a flower pixels on grass
float4 _VxGrassDark;     // rgb grass in shade / drip ends (linear)
float4 _VxDirt;          // rgb dirt (linear)
float4 _VxWater;         // rgb water (linear), a ripple speed
float4 _VxWaterLight;    // rgb ripple highlight (linear)
float4 _VxParams;        // x drip share of the leg-side pixel columns, y longest drip (pixels), z leg pixels per unit, w lip share of the leg side
float4 _VxLightDir;      // xyz camera-relative light for the cliffs / props, w rock block size (world units)

// Kinds (uv1.x of the voxel meshes).
#define VX_PLAIN 0
#define VX_GRASS_TOP 1
#define VX_GRASS_SIDE 2
#define VX_DIRT 3
#define VX_BRICK 4
#define VX_STONE 5
#define VX_LEAVES 6
#define VX_LOG 7
#define VX_PLANK 8
#define VX_QBLOCK 9
#define VX_PIPE 10
#define VX_WINDOW 11
#define VX_LIT_WINDOW 12
#define VX_WATER 13
#define VX_CLOUD 14
#define VX_ROOF 15
#define VX_SAND 16
#define VX_SPOTS 17
#define VX_PETAL 18
#define VX_LAMP 19
#define VX_EYE 20

// Voxel amount on this pixel's side of the wipe (0 other look .. 1 voxel).
half VxAt(float2 screenUV)
{
    half w = LTWipeAt(screenUV);
    return (half)max(_VxLook.z, lerp(_VxLook.x, _VxLook.y, w));
}

half3 VxLegTop(float serial)    { return (half3)_VxLegTops[(uint)(serial * 7.0 + 3.0) % 10u].rgb; }
half3 VxLegBrick(float serial)  { return (half3)_VxLegBricks[(uint)(serial * 5.0 + 1.0) % 6u].rgb; }

// One bit of the 8x8 "?" (x 0..7 left to right, y 0..7 bottom to top).
bool VxQBit(int x, int y)
{
    if (x < 0 || x > 7 || y < 0 || y > 7) return false;
    int row = 7 - y;
    uint m = row == 1 ? 0x3Cu : row == 2 ? 0x66u : row == 3 ? 0x06u : row == 4 ? 0x0Cu : row == 5 ? 0x18u : row == 7 ? 0x18u : 0u;
    return ((m >> (uint)(7 - x)) & 1u) != 0u;
}

// Brick wall: 4 courses per voxel, 2 bricks per course, staggered; mortar on the bottom pixel row and the left pixel column
// of each brick; a light top-left pixel on each brick. p in texture pixels (8 per voxel).
half3 VxBrick(float2 p, half3 base, float key)
{
    float rowH = 2.0;
    float row = floor(p.y / rowH);
    float off = fmod(abs(row), 2.0) * 2.0;
    float bx = floor((p.x + off) / 4.0);
    float lx = fmod(p.x + off + 4000.0, 4.0);
    float ly = fmod(p.y + 4000.0, rowH);
    half3 c = base * (half)lerp(0.86, 1.12, PixHash(float3(bx, row, key + 3.1)));
    c *= (half)lerp(0.94, 1.05, PixHash(float3(floor(p), key + 5.7)));
    bool mortar = ly < 1.0 || lx < 1.0;
    if (mortar) c = base * 0.5h + half3(0.02h, 0.015h, 0.02h);
    else if (ly >= rowH - 1.0 && lx < 2.0) c *= 1.15h;
    return c;
}

// The pixel texture of one surface. uv in voxel units, px texture pixels per voxel, n the surface normal (world),
// time for water / blocks. Returns the unlit colour.
half3 VxPattern(int kind, float2 uv, half3 base, float px, float3 n, float key)
{
    float2 p = floor(uv * px);
    float2 f = frac(uv);
    float2 lp = floor(f * px);                     // pixel inside the voxel (0..px-1)
    float h = PixHash(float3(p, key + kind * 1.7));
    half3 c = base * (half)lerp(0.9, 1.08, h);

    if (kind == VX_GRASS_TOP)
    {
        half blot = (half)PixNoise(float3(p * 0.35, key + 2.0));
        c = lerp(_VxGrassDark.rgb, _VxGrass.rgb, (half)step(0.38, blot) * 0.6h + 0.4h) * (half)lerp(0.9, 1.1, h);
        if (h > 0.9) c *= 1.18h;
        float fl = PixHash(float3(p, key + 9.3));
        if (fl < _VxGrass.a * 0.05)
            c = fl < _VxGrass.a * 0.017 ? half3(0.9h, 0.12h, 0.08h) : fl < _VxGrass.a * 0.034 ? half3(1.0h, 0.78h, 0.1h) : half3(0.95h, 0.95h, 0.9h);
    }
    else if (kind == VX_GRASS_SIDE)
    {
        // Dirt with a grass cap; grass drips down some pixel columns (the reference's dripping turf).
        float r = px - 1.0 - lp.y;                 // pixel row from the top of the voxel
        float hc = PixHash(float3(p.x, key, 41.0));
        float drip = 2.0 + floor(hc * 2.5) + (hc > 0.7 ? floor(PixHash(float3(p.x, key, 43.0)) * 3.0) : 0.0);
        half3 dirt = base * (half)lerp(0.82, 1.1, h);
        if (h < 0.1) dirt *= 0.75h;
        if (r < drip)
        {
            c = lerp(_VxGrassDark.rgb, _VxGrass.rgb, (half)step(0.35, PixHash(float3(p, 7.0)))) * (half)lerp(0.92, 1.06, h);
            if (r >= drip - 1.0) c = _VxGrassDark.rgb * 0.85h;
        }
        else c = dirt;
    }
    else if (kind == VX_DIRT || kind == VX_SAND)
    {
        half blot = (half)step(0.55, PixNoise(float3(p * 0.4, key + 4.0)));
        c = base * lerp(0.86h, 1.06h, blot) * (half)lerp(0.92, 1.08, h);
        if (h > 0.93) c = base * 1.25h;
        if (kind == VX_SAND && fmod(abs(p.y), 4.0) < 1.0) c *= 0.88h;
    }
    else if (kind == VX_BRICK)
    {
        c = VxBrick(uv * 8.0 * (px / 8.0), base, key);
    }
    else if (kind == VX_STONE)
    {
        half v = (half)PixNoise(float3(p * 0.45, key + 11.0));
        c = base * (v > 0.62h ? 1.12h : v > 0.38h ? 0.97h : 0.8h) * (half)lerp(0.94, 1.05, h);
        if (h < 0.06) c *= 0.7h;
    }
    else if (kind == VX_LEAVES)
    {
        half v = (half)PixNoise(float3(p * 0.5, key + 13.0));
        c = base * (v > 0.6h ? 1.14h : 0.92h);
        if (h < 0.16) c = base * 0.62h;
        else if (h > 0.9) c = base * 1.28h;
    }
    else if (kind == VX_LOG)
    {
        if (abs(n.y) > 0.6)
        {
            float2 d = abs(f - 0.5) * px;
            float ring = floor(max(d.x, d.y));
            c = lerp(base * 1.35h, base * 1.1h, (half)fmod(ring, 2.0));
        }
        else
        {
            c = base * (half)lerp(0.8, 1.1, PixHash(float3(p.x, 3.0, key)));
            if (h < 0.12) c *= 0.75h;
        }
    }
    else if (kind == VX_PLANK)
    {
        float row = floor(lp.y / 2.0);
        c = base * (half)lerp(0.9, 1.08, PixHash(float3(row, floor((lp.x + row * 3.0) / 8.0), key)));
        if (fmod(lp.y, 2.0) < 1.0) c *= 0.72h;
        if (fmod(lp.x + row * 3.0, 8.0) < 1.0) c *= 0.8h;
    }
    else if (kind == VX_QBLOCK)
    {
        int x = (int)lp.x, y = (int)lp.y;
        half pulse = 0.93h + 0.07h * (half)sin(_Time.y * 3.0);
        c = base * pulse;
        if (x == 0 || y == 0 || x == 7 || y == 7) c = base * 0.62h;
        if ((x == 1 || x == 6) && (y == 1 || y == 6)) c = half3(0.25h, 0.12h, 0.04h);
        if (VxQBit(x - 1, y + 1) && !VxQBit(x, y)) c = half3(0.42h, 0.2h, 0.05h);   // the shadow under the "?"
        if (VxQBit(x, y)) c = half3(1.0h, 0.94h, 0.78h);
    }
    else if (kind == VX_PIPE)
    {
        // A face spans uv.x 0..1 (Box with uv scale 1 / width): a light stripe, a dark side, a dark outline.
        float u = frac(uv.x);
        c = base;
        if (u > 0.12 && u < 0.28) c = base * 1.45h + 0.05h;
        else if (u > 0.33 && u < 0.4) c = base * 1.2h;
        else if (u > 0.72) c = base * 0.7h;
        if (u < 0.04 || u > 0.96) c = base * 0.35h;
        if (abs(n.y) > 0.6) c = base * 0.3h;      // the pipe mouth
    }
    else if (kind == VX_WINDOW || kind == VX_LIT_WINDOW)
    {
        bool frame = lp.x < 1.0 || lp.y < 1.0 || lp.x > px - 2.0 || lp.y > px - 2.0 || abs(lp.x - px * 0.5 + 0.5) < 0.6;
        if (kind == VX_WINDOW)
        {
            c = half3(0.05h, 0.12h, 0.2h);
            if (abs((lp.x - lp.y) - 1.0) < 1.0 && lp.x > 1.0) c = half3(0.35h, 0.55h, 0.7h);
        }
        else c = half3(1.0h, 0.72h, 0.25h) * (half)lerp(0.9, 1.1, h);
        if (frame) c = base * 0.75h;
    }
    else if (kind == VX_WATER)
    {
        float t = _Time.y * max(_VxWater.a, 0.1);
        float2 q = p + float2(0.0, (abs(n.y) < 0.6 ? t * 6.0 : 0.0));   // falls run down
        q = floor(q);
        half v = (half)PixNoise(float3(q * float2(0.5, 0.25), t * 0.6));
        c = _VxWater.rgb * (v > 0.6h ? 1.15h : 0.95h);
        if (v > 0.74h) c = _VxWaterLight.rgb;
        if (PixHash(float3(q, floor(t * 3.0))) > 0.97) c = half3(0.95h, 0.98h, 1.0h);
    }
    else if (kind == VX_CLOUD)
    {
        c = n.y < -0.5 ? half3(0.72h, 0.8h, 0.92h) : n.y > 0.5 ? half3(1.0h, 1.0h, 1.0h) : half3(0.9h, 0.93h, 0.98h);
        if (n.y > -0.5 && lp.y < 2.0) c *= 0.92h;
    }
    else if (kind == VX_ROOF)
    {
        float row = floor(lp.y / 2.0);
        c = base * (half)lerp(0.9, 1.08, PixHash(float3(row, floor((lp.x + row * 2.0) / 3.0), key)));
        if (fmod(lp.y, 2.0) < 1.0) c *= 0.7h;
        else if (fmod(lp.x + row * 2.0, 3.0) < 1.0) c *= 0.85h;
    }
    else if (kind == VX_SPOTS)
    {
        float hv = PixHash(float3(floor(uv), key + 21.0));
        float2 d = abs(f - 0.5) * px;
        c = base * (half)lerp(0.92, 1.06, h);
        if (hv > 0.45 && max(d.x, d.y) < px * 0.28) c = half3(0.98h, 0.96h, 0.92h);
    }
    else if (kind == VX_PETAL)
    {
        c = base * (half)lerp(0.95, 1.08, h);
        if (lp.x < 1.0 || lp.y < 1.0 || lp.x > px - 2.0 || lp.y > px - 2.0) c = base * 0.75h;
    }
    else if (kind == VX_LAMP)
    {
        c = half3(1.0h, 0.85h, 0.45h);
    }
    else if (kind == VX_EYE)
    {
        c = half3(0.04h, 0.03h, 0.03h);
        if (lp.x > px * 0.5 && lp.y > px * 0.6) c = half3(1, 1, 1);
    }
    return c;
}

// Flat block shading from the camera-relative light: tops brightest, the lit side, the shade side, undersides darkest.
half VxShade(float3 n)
{
    float3 L = normalize(_VxLightDir.xyz);
    if (n.y > 0.6) return 1.0h;
    if (n.y < -0.6) return 0.52h;
    half lit = (half)saturate(dot(normalize(float3(n.x, 0, n.z)), normalize(float3(L.x, 0, L.z))) * 0.5 + 0.5);
    return lerp(0.66h, 0.9h, lit);
}

#endif
