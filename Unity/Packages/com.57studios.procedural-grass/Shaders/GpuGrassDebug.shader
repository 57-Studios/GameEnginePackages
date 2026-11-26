Shader "57Studios/ProceduralGrass/Debug"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.12, 0.45, 0.08, 1)
        _TipColor("Tip Color", Color) = (0.45, 0.8, 0.16, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"

            struct GrassInstance
            {
                float3 position;
                float height;
                float2 facing;
                float width;
                float random;
            };

            StructuredBuffer<GrassInstance> _GrassInstances;
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TipColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 color : COLOR0;
            };

            Varyings Vert(Attributes input)
            {
                InitIndirectDrawArgs(0);
                uint instanceID = GetIndirectInstanceID(input.instanceID);
                GrassInstance blade = _GrassInstances[instanceID];
                float t = input.uv.y;
                float2 facing = normalize(blade.facing);
                float3 forward = float3(facing.x, 0.0, facing.y);
                float3 side = float3(-facing.y, 0.0, facing.x);
                float bend = sin(t * 1.5707963) * t * blade.height * (0.10 + blade.random * 0.08);
                float width = blade.width * (1.0 - t * 0.75);
                float signedSide = input.uv.x * 2.0 - 1.0;
                float3 worldPosition = blade.position + float3(0.0, blade.height * t, 0.0) + forward * bend + side * (signedSide * width);

                Varyings output;
                output.positionCS = TransformWorldToHClip(worldPosition);
                output.color = lerp(_BaseColor.rgb, _TipColor.rgb, t) * lerp(0.8, 1.15, blade.random);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(input.color, 1.0);
            }
            ENDHLSL
        }
    }
}
