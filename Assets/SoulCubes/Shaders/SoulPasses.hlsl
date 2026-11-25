#ifndef SOUL_PASSES_INCLUDED
#define SOUL_PASSES_INCLUDED

// Shadow caster / depth / depth-normals vertex + fragment programs shared by the soul shaders.

struct SoulA { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
struct SoulV { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

#ifdef SOUL_SHADOW_PASS
float3 _LightDirection;
float3 _LightPosition;
SoulV vertShadow(SoulA v)
{
    SoulV o;
    float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
    float3 nWS = TransformObjectToWorldNormal(v.normalOS);
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 lightDir = normalize(_LightPosition - posWS);
    #else
        float3 lightDir = _LightDirection;
    #endif
    float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
    #if UNITY_REVERSED_Z
        cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
    #else
        cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
    #endif
    o.positionCS = cs;
    o.normalWS = nWS;
    return o;
}
half4 fragShadow(SoulV i) : SV_Target { return 0; }
#endif

SoulV vertDepth(SoulA v)
{
    SoulV o;
    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    return o;
}
half fragDepth(SoulV i) : SV_Target { return i.positionCS.z; }
half4 fragDN(SoulV i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }

#endif
