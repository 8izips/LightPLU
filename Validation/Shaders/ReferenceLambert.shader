Shader "LightPLUValidation/ReferenceLambert"
{
    Properties { _Reflectance ("Diffuse Reflectance", Range(0,1)) = 0.18 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ReferenceLambert"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Reflectance;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(i.normalOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                return o;
            }
            float3 Eval(float3 normalWS, Light light)
            {
                float ndotl = saturate(dot(normalWS, light.direction));
                float atten = light.distanceAttenuation * light.shadowAttenuation;
                return (float3)light.color * atten * ndotl * (_Reflectance / PI);
            }
            float4 Frag(Varyings i) : SV_Target
            {
                // Keep this exact variable name. URP's LIGHT_LOOP_BEGIN macro
                // references `inputData` internally in clustered variants.
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = normalize(i.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);

                float3 c = Eval(inputData.normalWS, GetMainLight());

                #if USE_CLUSTER_LIGHT_LOOP
                UNITY_LOOP for (
                    uint lightIndex = 0;
                    lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS);
                    lightIndex++)
                {
                    Light light = GetAdditionalLight(
                        lightIndex,
                        inputData.positionWS,
                        half4(1, 1, 1, 1));
                    c += Eval(inputData.normalWS, light);
                }
                #endif

                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(
                        lightIndex,
                        inputData.positionWS,
                        half4(1, 1, 1, 1));
                    c += Eval(inputData.normalWS, light);
                LIGHT_LOOP_END

                return float4(c, 1);
            }
            ENDHLSL
        }
    }
}
