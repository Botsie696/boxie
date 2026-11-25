#ifndef VOXEL_CP_ROCK_INCLUDED
#define VOXEL_CP_ROCK_INCLUDED

// Voxel checkpoint: the scene's own Polybox rocks where they still turn up among the checkpoint's voxel cliffs
// (PixelRockLit, 3.9/3.10-only copy). Repainted as blocks in world space: everything facing up is pixel grass, the faces
// are strata of block rows (orange brick, red brick, sandstone, dirt, cobble) - one material per row of blocks, so a
// whole cliff reads as layered blocks like the checkpoint's own mesas. Colour depends only on world position and normal.
// Needs VoxelCommon.hlsl.

half3 VxRockColor(float3 wp, float3 n)
{
    float B = max(_VxLightDir.w, 0.5);
    float2 nh = n.xz;
    float2 t = dot(nh, nh) > 1e-4 ? normalize(float2(-nh.y, nh.x)) : float2(1, 0);
    half shade = VxShade(n);
    half3 grassTop = VxPattern(VX_GRASS_TOP, wp.xz / B, _VxGrass.rgb, 8.0, float3(0, 1, 0), 3.0) * shade;
    float2 uv = float2(dot(wp.xz, t), wp.y) / B;
    float row = floor(uv.y);
    float band = floor(row / 3.0);
    float hb = PixHash(float3(band, 5.0, 2.0));
    int kind = hb < 0.3 ? VX_BRICK : hb < 0.5 ? VX_SAND : hb < 0.72 ? VX_BRICK : hb < 0.86 ? VX_DIRT : VX_STONE;
    half3 base = hb < 0.3 ? half3(0.62h, 0.2h, 0.08h) : hb < 0.5 ? half3(0.8h, 0.55h, 0.2h)
               : hb < 0.72 ? half3(0.74h, 0.3h, 0.08h) : hb < 0.86 ? _VxDirt.rgb : half3(0.3h, 0.3h, 0.34h);
    // Turf drips over the top row of a face that is just under an upward slope.
    if (n.y > 0.25) { kind = VX_GRASS_SIDE; base = _VxDirt.rgb; }
    half3 face = VxPattern(kind, uv, base, 8.0, n, band) * shade;
    return n.y > 0.55 ? grassTop : face;
}

#endif
