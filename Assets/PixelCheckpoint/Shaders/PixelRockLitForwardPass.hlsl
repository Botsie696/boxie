// Pixel checkpoint copy of ToonRockLitForwardPass.hlsl (left untouched): the toon repaint plus the pixel-cave repaint
// (PixelRockColor, PixelCommon.hlsl) where the pixel look is in force. Elsewhere it is the toon pass pixel for pixel.
// The Polybox forward pass with ChasmFog added. At fog strength 0 it renders exactly as the pack does.
#ifndef PIXEL_ROCK_LIT_FORWARD_PASS_INCLUDED
#define PIXEL_ROCK_LIT_FORWARD_PASS_INCLUDED

// The fog needs the world position on every variant.
#ifndef REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
#define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR
#endif

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Assets/PropCameraLooks/LookTransition/Shaders/LookChasmFog.hlsl"
#include "Assets/ToonCheckpoint/Shaders/ToonCommon.hlsl"
#include "Assets/PixelCheckpoint/Shaders/PixelCommon.hlsl"
#include "Assets/StorybookCheckpoint/Shaders/StorybookCommon.hlsl"
#include "Assets/StorybookCheckpoint/Shaders/StorybookRock.hlsl"
#include "Assets/FlowerCheckpoint/Shaders/FlowerCommon.hlsl"
#include "Assets/FlowerCheckpoint/Shaders/FlowerRock.hlsl"
#include "Assets/VoxelCheckpoint/Shaders/VoxelCommon.hlsl"
#include "Assets/VoxelCheckpoint/Shaders/VoxelRock.hlsl"
#include "Assets/PencilCheckpoint/Shaders/PencilCommon.hlsl"
#include "Assets/PencilCheckpoint/Shaders/PencilRock.hlsl"
#include "Assets/SpaceCheckpoint/Shaders/SpaceCommon.hlsl"
#include "Assets/SpaceCheckpoint/Shaders/SpaceRock.hlsl"
#include "Assets/SpiceRouteCheckpoint/Shaders/SpiceCommon.hlsl"
#include "Assets/SpiceRouteCheckpoint/Shaders/SpiceRock.hlsl"

// GLES2 has limited amount of interpolators
#if defined(_PARALLAXMAP) && !defined(SHADER_API_GLES)
#define REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR
#endif

#if (defined(_NORMALMAP) || (defined(_PARALLAXMAP) && !defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR))) || defined(_DETAIL)
#define REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR
#endif

// keep this file in sync with LitGBufferPass.hlsl

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float4 tangentOS    : TANGENT;
    float2 texcoord     : TEXCOORD0;
    float2 lightmapUV   : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv                       : TEXCOORD0;
    DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 1);

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    float3 positionWS               : TEXCOORD2;
#endif

    float3 normalWS                 : TEXCOORD3;
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    float4 tangentWS                : TEXCOORD4;    // xyz: tangent, w: sign
#endif
    float3 viewDirWS                : TEXCOORD5;

    half4 fogFactorAndVertexLight   : TEXCOORD6; // x: fogFactor, yzw: vertex light

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord              : TEXCOORD7;
#endif

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    float3 viewDirTS                : TEXCOORD8;
#endif

    float4 positionCS               : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
};

void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    inputData.positionWS = input.positionWS;
#endif

    half3 viewDirWS = SafeNormalize(input.viewDirWS);
#if defined(_NORMALMAP) || defined(_DETAIL)
    float sgn = input.tangentWS.w;      // should be either +1 or -1
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    inputData.normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz));
#else
    inputData.normalWS = input.normalWS;
#endif

    inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
    inputData.viewDirectionWS = viewDirWS;

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif

    inputData.fogCoord = input.fogFactorAndVertexLight.x;
    inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
    inputData.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, inputData.normalWS);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
}

///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////

// Used in Standard (Physically Based) shader
Varyings LitPassVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);

    // normalWS and tangentWS already normalize.
    // this is required to avoid skewing the direction during interpolation
    // also required for per-vertex lighting and SH evaluation
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    half3 viewDirWS = GetWorldSpaceViewDir(vertexInput.positionWS);
    half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
    half fogFactor = ComputeFogFactor(vertexInput.positionCS.z);

    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);

    // already normalized from normal transform to WS.
    output.normalWS = normalInput.normalWS;
    output.viewDirWS = viewDirWS;
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR) || defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    real sign = input.tangentOS.w * GetOddNegativeScale();
    half4 tangentWS = half4(normalInput.tangentWS.xyz, sign);
#endif
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    output.tangentWS = tangentWS;
#endif

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS = GetViewDirectionTangentSpace(tangentWS, output.normalWS, viewDirWS);
    output.viewDirTS = viewDirTS;
#endif

    OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
    OUTPUT_SH(output.normalWS.xyz, output.vertexSH);

    output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    output.positionWS = vertexInput.positionWS;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(vertexInput);
#endif

    output.positionCS = vertexInput.positionCS;

    return output;
}

// Used in Standard (Physically Based) shader
half4 LitPassFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#if defined(_PARALLAXMAP)
#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS = input.viewDirTS;
#else
    half3 viewDirTS = GetViewDirectionTangentSpace(input.tangentWS, input.normalWS, input.viewDirWS);
#endif
    ApplyPerPixelDisplacement(viewDirTS, input.uv);
#endif

    //Reduce intensity of emission
    _EmissionColor *= .3;

    SurfaceData surfaceData;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    half4 color = UniversalFragmentPBR(inputData, surfaceData);

    // LookTransition: where the look wipe has passed, light the rock with the next look's sun and ambient. URP lit it with
    // the lights in force; the difference (_LTSunD / _LTAmbD, set by LookTransitionDirector) is added through the same
    // diffuse terms URP uses. Zero difference - and an idle wipe - add nothing.
    {
        half lw = LTWipeAt(GetNormalizedScreenSpaceUV(input.positionCS));
        if (lw > 0.0001h)
        {
            Light ltSun = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
            half ltNdl = saturate(dot(inputData.normalWS, ltSun.direction));
            half3 ltDiffuse = surfaceData.albedo * OneMinusReflectivityMetallic(surfaceData.metallic);
            half3 ltLight = _LTSunD.rgb * (ltNdl * ltSun.shadowAttenuation * ltSun.distanceAttenuation) + _LTAmbD.rgb * surfaceData.occlusion;
            color.rgb = max(color.rgb + ltDiffuse * ltLight * lw, 0.0h);
        }
    }

    // Toon checkpoint: where the toon look is in force the rock is repainted as cartoon stone (ToonRockColor) - lit by the
    // camera-relative toon light, not the pack's textures, so every piece of a cliff shares one palette. The fog below
    // still applies, so cliffs sink into the valley mist exactly as before.
    {
        half toon = ToonAt(GetNormalizedScreenSpaceUV(input.positionCS));
        if (toon > 0.001h)
            color.rgb = lerp(color.rgb, ToonRockColor(input.positionWS, normalize(input.normalWS)), toon);
    }

    // Pixel checkpoint: indigo cave stone, blue moss + drips, coral mushrooms (PixelCommon.hlsl).
    half pixAmt = PixAt(GetNormalizedScreenSpaceUV(input.positionCS));
    half3 pixGlow = 0;
    half pixGlowAmt = 0;
    if (pixAmt > 0.001h)
    {
        half3 pc = PixRockColor(input.positionWS, normalize(input.normalWS), pixGlow, pixGlowAmt);
        color.rgb = lerp(color.rgb, pc, pixAmt);
        pixGlowAmt *= pixAmt;
    }

    // Storybook checkpoint: pen-and-watercolour limestone with green bushes on top (StorybookRock.hlsl).
    {
        half sbAmt = SbAt(GetNormalizedScreenSpaceUV(input.positionCS));
        if (sbAmt > 0.001h)
            color.rgb = lerp(color.rgb, SbRockColor(input.positionWS, normalize(input.normalWS)), sbAmt);
    }

    // Flower checkpoint: pale painted karst, mossy tops with blossom clumps, trailing vines (FlowerRock.hlsl).
    {
        half flAmt = FlAt(GetNormalizedScreenSpaceUV(input.positionCS));
        if (flAmt > 0.001h)
            color.rgb = lerp(color.rgb, FlRockColor(input.positionWS, normalize(input.normalWS)), flAmt);
    }

    // Voxel checkpoint: the scene rocks repainted as layered blocks with pixel grass on top (VoxelRock.hlsl).
    {
        half vxAmt = VxAt(GetNormalizedScreenSpaceUV(input.positionCS));
        if (vxAmt > 0.001h)
            color.rgb = lerp(color.rgb, VxRockColor(input.positionWS, normalize(input.normalWS)), vxAmt);
    }

    // Pencil checkpoint: the rocks drawn in graphite - hatched away from the light, contour strokes, grass tufts (PencilRock.hlsl).
    {
        half pcAmt = PcAt(GetNormalizedScreenSpaceUV(input.positionCS));
        if (pcAmt > 0.001h)
            color.rgb = lerp(color.rgb, PcRockColor(input.positionWS, normalize(input.normalWS)), pcAmt);
    }

    // Space checkpoint: far Martian mesas - layered red sandstone, violet shade, pale dust on top (SpaceRock.hlsl).
    {
        half spAmt = SpAt(GetNormalizedScreenSpaceUV(input.positionCS));
        if (spAmt > 0.001h)
            color.rgb = lerp(color.rgb, SpRockColor(input.positionWS, normalize(input.normalWS)), spAmt);
    }

    // Spice Route checkpoint: painted sandstone - pink-ochre in India, pale limestone in Arabia (SpiceRock.hlsl).
    {
        half srAmt = SrAt(GetNormalizedScreenSpaceUV(input.positionCS));
        if (srAmt > 0.001h)
            color.rgb = lerp(color.rgb, SrRockColor(input.positionWS, normalize(input.normalWS)), srAmt);
    }

#if !defined(_CHASM_FOG_NONE)   // with _CHASM_FOG_NONE (ChasmFog.Apply noFog) the rock takes no fog of any kind: not the pack's, not Unity's, not the chasm's
    // Polybox height fog + Unity fog, as the pack does it...
    half3 packFog = color.rgb;
    half heightFog = saturate((_HeightFogEnd - input.positionWS.y) / (_HeightFogEnd - _HeightFogStart));
    heightFog = saturate(pow(heightFog, 2.2));
#if !defined(_CHASM_FOG_HOUSE)   // with _CHASM_FOG_HOUSE, Height Fog End holds the height of the house, not a fade
    packFog = lerp(packFog, _HeightFogColor.rgb, heightFog);
#endif
    packFog = MixFog(packFog, inputData.fogCoord);

    // ...or the chasm fog, which fades into exactly the background behind the rock.
#if defined(_CHASM_FOG_PROP)
    // Rock a house stands on keeps its own fade (the material's Height Fog Start/End, as the pack does it) so the column stays
    // clear well under the house; it only takes the chasm's background colour, so its foot still vanishes. No haze, no screen fog.
    half3 chasmFog = lerp(color.rgb, ChasmFogColor(GetNormalizedScreenSpaceUV(input.positionCS)), 1.0 - (1.0 - heightFog) * (1.0 - _ChasmFogScreen.w));
#elif defined(_CHASM_FOG_HOUSE)
    // Rock a house stands on takes a share of the chasm fog, more the further it is under the house (Height Fog End).
    float2 fogUV = GetNormalizedScreenSpaceUV(input.positionCS);
    half3 chasmFog = lerp(color.rgb, ChasmFogColor(fogUV), ChasmFogHouseAmount(input.positionWS, fogUV, _HeightFogEnd - input.positionWS.y));
#else
    half3 chasmFog = ApplyChasmFog(color.rgb, input.positionWS, input.positionCS);
#endif
    color.rgb = lerp(packFog, chasmFog, (half)saturate(_ChasmFogView.w));
#endif
    // Pixel checkpoint: mushrooms and drip beads keep glowing through the fog.
    color.rgb = lerp(color.rgb, pixGlow, saturate(pixGlowAmt) * 0.7h);
    color.a = OutputAlpha(color.a, _Surface);
   

    return color;
}

#endif
