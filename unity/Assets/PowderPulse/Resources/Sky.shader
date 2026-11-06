// Dusk sky: deep blue overhead fading to a warm haze at the horizon, with a soft low sun.
Shader "PowderPulse/Sky"
{
    Properties
    {
        _Top ("Top", Color) = (0.13, 0.15, 0.33, 1)
        _Mid ("Middle", Color) = (0.42, 0.38, 0.62, 1)
        _Horizon ("Horizon", Color) = (0.95, 0.72, 0.62, 1)
        _Bottom ("Below horizon", Color) = (0.78, 0.66, 0.72, 1)
        _SunColor ("Sun", Color) = (1, 0.85, 0.65, 1)
        _SunDir ("Sun direction", Vector) = (0, 0.3, 1, 0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Top, _Mid, _Horizon, _Bottom, _SunColor, _SunDir;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.dir = i.positionOS.xyz;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float3 col;
                if (h > 0)
                {
                    float t = pow(saturate(h), 0.5);
                    col = lerp(_Horizon.rgb, _Mid.rgb, saturate(t * 2.2));
                    col = lerp(col, _Top.rgb, saturate(t * 1.6 - 0.45));
                }
                else col = lerp(_Horizon.rgb, _Bottom.rgb, saturate(-h * 6));
                float3 s = normalize(_SunDir.xyz);
                float sd = saturate(dot(d, s));
                col += _SunColor.rgb * (pow(sd, 900) * 4 + pow(sd, 24) * 0.35 + pow(sd, 4) * 0.12);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
