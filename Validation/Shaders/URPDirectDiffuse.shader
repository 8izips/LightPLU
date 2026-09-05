Shader "LightPLUValidation/URPDirectDiffuse"
{
    Properties { _Albedo ("Albedo", Range(0,1)) = 0.18 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "URPDirectDiffuse"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Albedo;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(i.normalOS);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS; o.normalWS = n.normalWS;
                return o;
            }
            half3 Eval(BRDFData brdf, InputData d, Light l)
            {
                BRDFData noCoat = (BRDFData)0;
                return LightingPhysicallyBased(brdf, noCoat, l, d.normalWS, d.viewDirectionWS, 0.0h, true);
            }
            half4 Frag(Varyings i) : SV_Target
            {
                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.normalWS = normalize(i.normalWS);
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                half alpha = 1.0h;
                BRDFData brdf;
                InitializeBRDFData(half3(_Albedo,_Albedo,_Albedo), 0.0h, half3(0,0,0), 0.0h, alpha, brdf);
                half3 c = Eval(brdf, d, GetMainLight());
                #if USE_FORWARD_PLUS
                UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    Light l = GetAdditionalLight(lightIndex, d.positionWS, half4(1,1,1,1));
                    c += Eval(brdf, d, l);
                }
                #endif
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, d.positionWS, half4(1,1,1,1));
                    c += Eval(brdf, d, l);
                LIGHT_LOOP_END
                return half4(c,1);
            }
            ENDHLSL
        }
    }
}
