// Soft snow: _Mode 0 = round powder puffs (particles), 1 = board track stripe (trail renderer).
Shader "PowderPulse/SnowFx"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _Mode ("Mode", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Mode;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float fogFactor : TEXCOORD1; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.color = i.color * _Color;
                o.uv = i.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float a;
                if (_Mode < 0.5) a = smoothstep(0.5, 0.15, length(i.uv - 0.5));
                else a = smoothstep(0.5, 0.3, abs(i.uv.y - 0.5));
                float4 c = i.color;
                c.rgb = MixFog(c.rgb, i.fogFactor);
                return half4(c.rgb, c.a * a);
            }
            ENDHLSL
        }
    }
}
