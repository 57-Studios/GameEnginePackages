// 57Studios Deformable Snow - exclusion mask override material.
// Renders a flat white silhouette into the layer's R8 mask so the composite can leave those
// pixels alone. Used for characters, props and anything else that must not be painted snow.

Shader "Hidden/57Studios/DeformableSnowSolid"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Name "Exclusion"

            ZWrite Off
            ZTest LEqual
            Cull Back
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return 1.0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
