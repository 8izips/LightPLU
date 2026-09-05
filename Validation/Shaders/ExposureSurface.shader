Shader "LightPLUValidation/ExposureSurface"
{
    Properties
    {
        _Value ("Scene Linear Value", Float) = 1.0
        _EV100 ("EV100", Float) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ExposureSurface"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Value;
                float _EV100;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
            float4 Frag(Varyings i) : SV_Target
            {
                float exposed = _Value * exp2(-_EV100);
                return float4(exposed, exposed, exposed, 1);
            }
            ENDHLSL
        }
    }
}
