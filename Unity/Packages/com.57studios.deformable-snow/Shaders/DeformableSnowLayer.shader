// 57Studios Deformable Snow - screen-space snow layer.
//
// Shades the existing depth buffer instead of generating a terrain-conforming mesh. There is
// no second copy of the ground to keep in agreement with the terrain, so the whole class of
// failures the mesh path had (winding, patch clipping, overhang, terrain resolution) cannot
// occur here.
//
// Deformation still comes from the volume's heightfield, sampled with surface-aligned 0-1 UVs
// that never tile. Only the coverage decision is screen-space.

// Not Hidden: the appearance values live in the PackedSlush material asset, and a Hidden
// shader cannot be assigned through the normal material picker. The feature still finds it
// by name and copies the asset's values onto its own blit material.
Shader "57Studios/DeformableSnow/Layer"
{
    Properties
    {
        [Header(Appearance)]
        _SnowColor("Snow Colour", Color) = (0.80, 0.85, 0.92, 1)
        _CompressedColor("Compressed Colour", Color) = (0.30, 0.33, 0.31, 1)
        _WetColor("Wet Tint", Color) = (0.55, 0.62, 0.68, 1)
        _WetSpecular("Wet Specular", Range(0, 4)) = 1.1
        _WetShininess("Wet Shininess", Range(1, 256)) = 48
        _CompressedThreshold("Compressed Threshold", Range(0, 1)) = 0.06
        _CompressedSoftness("Compressed Softness", Range(0.01, 1)) = 0.35
        _NormalStrength("Normal Strength", Range(0, 4)) = 1

        [Header(Coverage)]
        // How much of the eligible ground is snowed. 0 disables the layer entirely.
        _SnowAmount("Snow Amount", Range(0, 1)) = 1
        // A surface steeper than this keeps no snow. Grass billboards face sideways, so they
        // fall below the threshold and are rejected without any layer bookkeeping.
        _SlopeThreshold("Slope Threshold", Range(0, 1)) = 0.62
        _SlopeSoftness("Slope Softness", Range(0.01, 1)) = 0.22

        [Header(Volume)]
        _SnowThickness("Snow Thickness (m)", Range(0.005, 0.5)) = 0.06
        _MinimumThickness("Compressed Thickness (m)", Range(0, 0.5)) = 0.012
        [NoScaleOffset] _HeightTex("Heightfield", 2D) = "black" {}

        _VolumeMin("Volume Min (XZY)", Vector) = (0, 0, 0, 0)
        _VolumeSize("Volume Size (XZY)", Vector) = (1, 1, 0, 0)
        _HeightTexel("Height Texel", Float) = 0.0009765625
        [HideInInspector] _VolumeMode("Volume Mode", Float) = 0

        [Header(Detail)]
        _DetailTex("Snow Detail", 2D) = "grey" {}
        _DetailStrength("Detail Strength", Range(0, 2)) = 0.35
        _DetailScale("Detail Scale (1/m)", Range(0.05, 8)) = 0.62

        [Header(Exclusion)]
        // 1 when the exclusion mask is bound. Without it every sample would read the
        // default white texture and the layer would be discarded over its whole extent.
        _ExclusionEnabled("Exclusion Enabled", Float) = 0
        _ExclusionTex("Exclusion", 2D) = "black" {}
        [HideInInspector] _AmbientProbe0("Ambient Probe 0", Vector) = (0, 0, 0, 0)
        [HideInInspector] _AmbientProbe1("Ambient Probe 1", Vector) = (0, 0, 0, 0)
        [HideInInspector] _AmbientProbe2("Ambient Probe 2", Vector) = (0, 0, 0, 0)
        [HideInInspector] _AmbientProbe3("Ambient Probe 3", Vector) = (0, 0, 0, 0)
        [HideInInspector] _AmbientProbe4("Ambient Probe 4", Vector) = (0, 0, 0, 0)
        [HideInInspector] _AmbientProbe5("Ambient Probe 5", Vector) = (0, 0, 0, 0)
        [HideInInspector] _AmbientProbe6("Ambient Probe 6", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "SnowLayer"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            // Normals come from the DepthNormals prepass when available, because it carries the
            // interpolated vertex normal. Derivative normals are per-triangle, so on a low-poly
            // terrain mesh they shade every triangle as a flat facet and the snow reads as
            // low-poly even though there is no snow geometry at all.
            #pragma multi_compile _ _SNOW_LAYER_NORMALS
            // Shadows are sampled from the world position, so the composite must compile the
            // same shadow variants the opaque pass does. Without these, GetMainLight returns an
            // unshadowed light and terrain in shadow reads as fully lit snow.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_HeightTex);
            SAMPLER(sampler_HeightTex);
            TEXTURE2D(_DetailTex);
            SAMPLER(sampler_DetailTex);
            // Screen-sized, so it carries the XR array dimension exactly like the scene copy.
            TEXTURE2D_X(_ExclusionTex);
            SAMPLER(sampler_ExclusionTex);

            // These are set per-frame from the feature, not per-material, so they live in the
            // global constant buffer rather than UnityPerMaterial. Declaring them as plain
            // uniforms keeps them out of the SRP Batcher path, which a blit pass does not use.
            float4 _VolumeMin;
            float4 _VolumeSize;
            float _HeightTexel;
            float _VolumeMode;

            float4 _SnowColor;
            float4 _CompressedColor;
            float4 _WetColor;
            float _WetSpecular;
            float _WetShininess;
            float _CompressedThreshold;
            float _CompressedSoftness;
            float _NormalStrength;
            float _SnowAmount;
            float _SlopeThreshold;
            float _SlopeSoftness;
            float _SnowThickness;
            float _MinimumThickness;
            float _DetailStrength;
            float _DetailScale;
            float _ExclusionEnabled;
            float4 _AmbientProbe0;
            float4 _AmbientProbe1;
            float4 _AmbientProbe2;
            float4 _AmbientProbe3;
            float4 _AmbientProbe4;
            float4 _AmbientProbe5;
            float4 _AmbientProbe6;

            float3 SampleSnowAmbient(float3 normalWS)
            {
                float3 ambient = SHEvalLinearL0L1(normalWS, _AmbientProbe0, _AmbientProbe1, _AmbientProbe2);
                ambient += SHEvalLinearL2(normalWS, _AmbientProbe3, _AmbientProbe4, _AmbientProbe5, _AmbientProbe6);
                return max(ambient, 0.0);
            }

            // Heightfield sample in 0-1 volume UV, feathered to zero at the volume border so
            // ground outside the volume stays smooth instead of showing a cut edge.
            float SampleSnowHeight(float2 uv)
            {
                float2 t = _HeightTexel.xx;
                float2 inside = smoothstep(t, t * 3.0, uv) * smoothstep(t, t * 3.0, 1.0 - uv);
                float h = SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_HeightTex, saturate(uv), 0).r;
                return h * inside.x * inside.y;
            }

            float2 VolumeUV(float3 positionWS)
            {
                return (positionWS.xz - _VolumeMin.xy) / max(_VolumeSize.xy, 1e-4);
            }

            // World normal of the snow surface. The heightfield gradient is added to the
            // geometric normal; both the shading and the relief darkening use this one rule.
            float3 SnowNormalWS(float3 normalWS, float2 uv)
            {
                float2 t = _HeightTexel.xx;
                float hL = SampleSnowHeight(uv - float2(t.x, 0.0));
                float hR = SampleSnowHeight(uv + float2(t.x, 0.0));
                float hD = SampleSnowHeight(uv - float2(0.0, t.y));
                float hU = SampleSnowHeight(uv + float2(0.0, t.y));

                float2 texelWorld = max(t * _VolumeSize.xy, 1e-5);
                float2 grad = float2(hR - hL, hU - hD) / (2.0 * texelWorld);
                float depth = max(_SnowThickness - _MinimumThickness, 0.0);
                return normalize(normalWS + float3(grad.x, 0.0, grad.y) * (depth * _NormalStrength));
            }

            // Derivative normal from the reconstructed world position. Per-triangle, so it
            // facets on a low-poly terrain; kept as the fallback when the DepthNormals pass is
            // not being produced.
            float3 DerivativeNormal(float3 positionWS)
            {
                float3 n = normalize(cross(ddy(positionWS), ddx(positionWS)));
                return n.y < 0.0 ? -n : n;
            }

            // Interpolated vertex normal of whatever the depth buffer holds. This is what makes
            // the snow shade like the terrain rather than like its triangles: the terrain mesh
            // is low-poly, but its vertex normals come from the smooth heightmap.
            float3 SurfaceNormalWS(float2 uv, float3 positionWS)
            {
            #if defined(_SNOW_LAYER_NORMALS)
                // Linear filtering smooths the normal across triangle seams. The vertex normals
                // are already interpolated inside each triangle, so this only matters at the
                // edges, which is exactly where faceting is visible.
                float3 n = SampleSceneNormals(uv, sampler_LinearClamp);
                // URP stores world-space normals, but a defensive fallback costs one compare
                // and covers a renderer that did not populate the texture.
                if (dot(n, n) < 1e-4)
                    n = DerivativeNormal(positionWS);
                return n.y < 0.0 ? -n : n;
            #else
                return DerivativeNormal(positionWS);
            #endif
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float rawDepth = SampleSceneDepth(uv);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                bool volumeOnly = _VolumeMode > 1.5;
                half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (eyeDepth >= _ProjectionParams.z - 0.01)
                {
                    if (volumeOnly)
                        discard;
                    return scene;
                }

                float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, unity_MatrixInvVP);
                float3 normalWS = SurfaceNormalWS(uv, positionWS);
                float2 volumeUV = VolumeUV(positionWS);
                float heightAboveMin = positionWS.y - _VolumeMin.z;
                bool inVolume = _VolumeMode > 0.5 && all(volumeUV >= 0.0) && all(volumeUV <= 1.0)
                    && heightAboveMin >= 0.0 && heightAboveMin <= _VolumeSize.z;
                if (volumeOnly && !inVolume)
                    discard;

                // Flatness: 1 on level ground, 0 on anything steeper than the threshold. This is
                // what rejects grass cards, which face sideways, and it costs nothing because
                // the normal is already a derivative of the reconstructed position.
                float flatSurface = smoothstep(_SlopeThreshold, _SlopeThreshold + _SlopeSoftness,
                    normalWS.y);

                float cover = saturate(flatSurface * _SnowAmount);

                if (_ExclusionEnabled > 0.5)
                {
                    // The mask is a flat silhouette, so it is already a 0-1 coverage value;
                    // using it directly keeps antialiased silhouette edges soft instead of
                    // turning them into a hard, crawling cut.
                    float excluded = SAMPLE_TEXTURE2D_X(_ExclusionTex, sampler_PointClamp, uv).r;
                    cover *= saturate(1.0 - excluded);
                }
                if (cover <= 0.001)
                {
                    if (volumeOnly)
                        discard;
                    return scene;
                }

                float h = 0.0;
                float3 N = normalWS;
                if (inVolume)
                {
                    h = SampleSnowHeight(volumeUV);
                    N = SnowNormalWS(normalWS, volumeUV);
                    // Leave deformation from earlier volumes visible through untouched snow.
                    if (volumeOnly && h <= 1e-5 && all(abs(N - normalWS) <= 1e-5))
                        discard;
                }
                float3 V = GetWorldSpaceNormalizeViewDir(positionWS);

                // Untouched snow -> compacted, wet slush.
                float wet = smoothstep(_CompressedThreshold, _CompressedThreshold + _CompressedSoftness, h);

                float2 detailUv = positionWS.xz * _DetailScale;
                float4 detailA = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, detailUv);
                float4 detailB = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, detailUv * 4.7 + 0.37);
                float2 slope = (detailA.xy * 2.0 - 1.0) + (detailB.xy * 2.0 - 1.0) * 0.55;
                // Compaction presses the grain flat and fills it with water, so the print floor
                // is smoother than the surrounding snow.
                N = normalize(N + float3(slope.x, 0.0, slope.y) * (_DetailStrength * (1.0 - wet * 0.55)));

                float3 albedo = lerp(_SnowColor.rgb, _CompressedColor.rgb, wet);
                albedo = lerp(albedo, _WetColor.rgb, wet * 0.2);
                albedo *= 0.94 + 0.12 * detailB.a;

                // Use URP's distance fade so distant snow cannot pick up shadow map
                // samples beyond the range covered by the directional light.
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS), positionWS, half4(1, 1, 1, 1));
                float ndl = saturate(dot(N, mainLight.direction));
                float3 color = albedo * (mainLight.color * ndl * mainLight.shadowAttenuation + SampleSnowAmbient(N));

                // Wet sheen: only the compacted area gets a tight, strong highlight.
                float3 H = normalize(V + mainLight.direction);
                float specPower = exp2(lerp(5.0, _WetShininess, wet));
                float spec = pow(saturate(dot(N, H)), specPower) * lerp(0.04, _WetSpecular, wet);
                color += mainLight.color * spec * ndl * mainLight.shadowAttenuation;

                // Fog from the reconstructed clip depth. A screen-space pass has no vertex
                // stage to interpolate it from, and skipping it would leave distant snow
                // crisper than the terrain underneath it.
                float fogFactor = ComputeFogFactor(TransformWorldToHClip(positionWS).z);
                color = MixFog(color, fogFactor);

                return half4(lerp(scene.rgb, color, cover), scene.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
