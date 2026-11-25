// The tutorial's ghost runner: unlit, transparent, a faint body with a rim in the guidance colour.
// No lighting, no shadows; drawn after the opaque world so it reads as a hint, not a second runner.
Shader "Tutorial/GhostFresnel"
{
    Properties
    {
        _BaseColor ("Base (white, low alpha)", Color) = (1, 1, 1, 0.2)
        _RimColor ("Rim (guidance)", Color) = (1, 0.79, 0.29, 0.7)
        _RimPower ("Rim Power", Range(0.5, 6)) = 2.2
        _Pulse ("Pulse", Range(0, 2)) = 1
        _Fade ("Fade", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+150" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _BaseColor;
            half4 _RimColor;
            half _RimPower;
            half _Pulse;
            half _Fade;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; };

            v2f vert (appdata i)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(i.vertex);
                o.n = UnityObjectToWorldNormal(i.normal);
                float3 wp = mul(unity_ObjectToWorld, i.vertex).xyz;
                // Orthographic cameras: the view direction is the camera's forward everywhere.
                o.v = unity_OrthoParams.w > 0.5 ? -UNITY_MATRIX_V[2].xyz : normalize(_WorldSpaceCameraPos - wp);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half ndv = saturate(dot(normalize(i.n), normalize(i.v)));
                half rim = pow(1.0 - ndv, _RimPower);
                half3 rgb = lerp(_BaseColor.rgb, _RimColor.rgb, rim);
                half a = lerp(_BaseColor.a, _RimColor.a, rim) * _Pulse * _Fade;
                return half4(rgb, saturate(a));
            }
            ENDCG
        }
    }
}
