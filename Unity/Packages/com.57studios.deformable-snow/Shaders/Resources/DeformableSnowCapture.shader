// 57Studios Deformable Snow - capture override material.
// Renders every registered deformer as a flat coverage mask, but only the part of it
// that is within ContactBand metres of the local terrain. Without that gate the tall
// orthographic slab would record a lifted foot as if it were planted.

Shader "Hidden/57Studios/DeformableSnowCapture"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "Capture"
            ZTest Always
            ZWrite Off
            Cull Off
            ColorMask R

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _TerrainHeightTex;
            float4 _CaptureOrigin;   // world XZ of the volume's minimum corner
            float4 _CaptureExtent;   // world XZ footprint
            float _ContactBand;      // metres above the local ground
            float4 _FootprintGroundPlane; // local contact plane for a tracked foot box

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float relativeY : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);

                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float2 uv = (world.xz - _CaptureOrigin.xy) / max(_CaptureExtent.xy, 1e-4);
                float ground = tex2Dlod(_TerrainHeightTex, float4(saturate(uv), 0.0, 0.0)).r;
                o.relativeY = _FootprintGroundPlane.y > 0.1
                    ? dot(_FootprintGroundPlane, float4(world, 1.0)) / _FootprintGroundPlane.y
                    : world.y - ground;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                clip(_ContactBand - i.relativeY);
                return 1.0;
            }
            ENDCG
        }
    }

    Fallback Off
}
