using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Studios57.DeformableSnow
{
    /// <summary>
    /// Screen-space snow layer. Shades the existing depth buffer after opaques instead of
    /// generating a terrain-conforming mesh, so there is exactly one description of the ground
    /// and no surface that can drift out of agreement with the terrain.
    ///
    /// Deformation still comes from <see cref="DeformableSnowVolume"/>: the heightfield supplies
    /// relief and the wet/compacted blend. Only the coverage decision moved to screen space.
    ///
    /// Depth is already required by the active pipeline asset, so this adds no prepass. Normals
    /// come from the derivative of the reconstructed world position rather than the normal
    /// texture, because requesting normals in Forward would force a full DepthNormals pass that
    /// re-renders every opaque object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeformableSnowLayerFeature : ScriptableRendererFeature
    {
        private const string ShaderName = "57Studios/DeformableSnow/Layer";
        private const string SolidShaderName = "Hidden/57Studios/DeformableSnowSolid";
        internal const string NormalsKeyword = "_SNOW_LAYER_NORMALS";

        [Tooltip("Material holding the snow appearance. The feature copies its values every frame, so the asset stays the single source of truth.")]
        [SerializeField] private Material appearanceMaterial;

        [Tooltip("Objects on these layers are never painted with snow. Characters and props belong here; the terrain must not.")]
        [SerializeField] private LayerMask exclusionLayers;

        [Tooltip("Draw the exclusion mask. Turn off to confirm the mask is what keeps objects clear of snow.")]
        [SerializeField] private bool useExclusionMask = true;

        [SerializeField] private bool injectInSceneView = true;

        private Shader solidShader;
        private Material layerMaterial;
        private Material exclusionMaterial;
        private SnowLayerPass pass;

        private bool layerEnabled = true;
        private static readonly System.Collections.Generic.List<DeformableSnowLayerFeature> Instances =
            new System.Collections.Generic.List<DeformableSnowLayerFeature>();

        /// <summary>
        /// Whether the snow layer draws. Safe to set any time, and runtime-only: it resets to on
        /// when the domain reloads. Returning early in <see cref="AddRenderPasses"/> also stops
        /// the feature from claiming the DepthNormals input, so the prepass URP would otherwise
        /// run on its behalf is skipped too. Toggling only the composite would leave that cost
        /// behind.
        ///
        /// Deliberately not <see cref="ScriptableRendererFeature.SetActive"/>: that writes the
        /// serialized field, which marks the renderer asset dirty in the editor and can persist
        /// the change to disk.
        /// </summary>
        public bool Enabled
        {
            get => layerEnabled;
            set => layerEnabled = value;
        }

        /// <summary>
        /// Turns the layer on or off without the caller needing a reference to the feature.
        /// Gameplay code normally has none, because the feature lives in the renderer asset
        /// rather than in the scene.
        /// </summary>
        public static void SetLayerEnabled(bool value)
        {
            for (int i = 0; i < Instances.Count; i++)
            {
                if (Instances[i] != null)
                    Instances[i].layerEnabled = value;
            }
        }

        /// <summary>Aggregate state, for tests and diagnostics.</summary>
        public static bool LayerEnabled => Instances.Count > 0 && Instances[0] != null && Instances[0].layerEnabled;

        public override void Create()
        {
            // Registration happens here rather than in OnEnable: the base class already defines
            // OnEnable to call Create(), so declaring one here would hide it and stop the
            // feature from ever initialising.
            if (!Instances.Contains(this))
                Instances.Add(this);

            Shader layerShader = Shader.Find(ShaderName);
            solidShader = Shader.Find(SolidShaderName);
            if (layerShader == null)
                return;

            if (layerMaterial == null)
            {
                layerMaterial = CoreUtils.CreateEngineMaterial(layerShader);
                layerMaterial.hideFlags = HideFlags.HideAndDontSave;
            }

            if (solidShader != null && exclusionMaterial == null)
            {
                exclusionMaterial = CoreUtils.CreateEngineMaterial(solidShader);
                exclusionMaterial.hideFlags = HideFlags.HideAndDontSave;
            }

            pass ??= new SnowLayerPass();
            pass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            // Depth and normals. The normal texture carries interpolated vertex normals, which
            // is the difference between snow that shades like the terrain and snow that shades
            // like the terrain's triangles. Derivative normals are per-triangle and facet
            // visibly on a low-poly terrain mesh, and there is no snow mesh to smooth.
            pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (layerMaterial == null || pass == null || !layerEnabled)
                return;

            // Preview cameras render with a different depth setup and no game volume in range,
            // so they are deliberately excluded rather than left to sample a missing depth.
            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
                return;
            if (cameraType == CameraType.SceneView && !injectInSceneView)
                return;

            pass.Setup(layerMaterial, useExclusionMask ? exclusionMaterial : null,
                useExclusionMask ? exclusionLayers.value : 0, appearanceMaterial);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            Instances.Remove(this);
            CoreUtils.Destroy(layerMaterial);
            CoreUtils.Destroy(exclusionMaterial);
            layerMaterial = null;
            exclusionMaterial = null;
        }

        private sealed class SnowLayerPass : ScriptableRenderPass
        {
            private class MaskPassData
            {
                public RendererListHandle rendererList;
            }

            private struct VolumeDraw
            {
                public Vector4 min;
                public Vector4 size;
                public float texel;
                public Texture texture;
            }

            private class CompositePassData
            {
                public Material material;
                public Material appearance;
                public bool useNormals;
                public TextureHandle sceneCopy;
                public TextureHandle exclusion;
                public readonly List<VolumeDraw> volumes = new List<VolumeDraw>();
                public Texture detailTexture;
                public Vector4[] ambientProbe;
            }

            private Material layerMaterial;
            private Material exclusionMaterial;
            private Material appearanceSource;
            private int exclusionMask;

            private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
            private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int VolumeModeId = Shader.PropertyToID("_VolumeMode");
            private static readonly int VolumeMinId = Shader.PropertyToID("_VolumeMin");
            private static readonly int VolumeSizeId = Shader.PropertyToID("_VolumeSize");
            private static readonly int HeightTexelId = Shader.PropertyToID("_HeightTexel");
            private static readonly int HeightTextureId = Shader.PropertyToID("_HeightTex");
            private static readonly Vector4 BlitScaleBias = new Vector4(1f, 1f, 0f, 0f);
            private static readonly MaterialPropertyBlock DrawProperties = new MaterialPropertyBlock();
            private readonly Plane[] frustumPlanes = new Plane[6];

            public SnowLayerPass()
            {
                profilingSampler = new ProfilingSampler("Deformable Snow Layer");
            }

            public void Setup(Material material, Material solid, int layerMask, Material appearance)
            {
                layerMaterial = material;
                exclusionMaterial = solid;
                exclusionMask = layerMask;
                appearanceSource = appearance;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (layerMaterial == null)
                    return;

                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer)
                    return;

                var source = resourceData.activeColorTexture;
                if (!source.IsValid())
                    return;

                var renderingData = frameData.Get<UniversalRenderingData>();
                var sourceDesc = renderGraph.GetTextureDesc(source);

                // ------------------------------------------------------------ exclusion mask
                TextureHandle exclusion = TextureHandle.nullHandle;
                bool wantMask = exclusionMaterial != null && exclusionMask != 0;
                if (wantMask)
                {
                    var maskDesc = new TextureDesc(sourceDesc.width, sourceDesc.height)
                    {
                        name = "DeformableSnowExclusion",
                        colorFormat = GraphicsFormat.R8_UNorm,
                        clearBuffer = true,
                        clearColor = Color.black,
                        filterMode = FilterMode.Point
                    };
                    exclusion = renderGraph.CreateTexture(maskDesc);

                    var drawingSettings = CreateDrawingSettings(cameraData.camera, exclusionMaterial);
                    var filteringSettings = new FilteringSettings(RenderQueueRange.opaque, exclusionMask);
                    var rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);
                    var rendererList = renderGraph.CreateRendererList(rendererListParams);

                    using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>(
                        "Deformable Snow Exclusion", out var passData, profilingSampler))
                    {
                        passData.rendererList = rendererList;
                        builder.UseRendererList(rendererList);
                        builder.SetRenderAttachment(exclusion, 0, AccessFlags.Write);
                        builder.AllowPassCulling(false);
                        builder.SetRenderFunc((MaskPassData data, RasterGraphContext context) =>
                            context.cmd.DrawRendererList(data.rendererList));
                    }
                }

                // ------------------------------------------------------------ composite
                // The pass reads the scene colour and writes it back, so it needs a copy: a
                // texture cannot be bound as an attachment while being sampled.
                var copyDesc = sourceDesc;
                copyDesc.name = "DeformableSnowSceneCopy";
                copyDesc.clearBuffer = false;
                copyDesc.depthBufferBits = 0;
                var sceneCopy = renderGraph.CreateTexture(copyDesc);
                renderGraph.AddBlitPass(source, sceneCopy, Vector2.one, Vector2.zero, passName: "Deformable Snow Copy");

                using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>(
                    "Deformable Snow Composite", out var passData, profilingSampler))
                {
                    passData.material = layerMaterial;
                    passData.appearance = appearanceSource;
                    // The pass only gets the normal texture when ConfigureInput asked for it and
                    // the renderer produced a DepthNormals pass; sampling it otherwise reads a
                    // cleared texture and every surface would shade as a flat facet.
                    passData.useNormals = resourceData.cameraNormalsTexture.IsValid();
                    passData.sceneCopy = sceneCopy;
                    passData.exclusion = exclusion;
                    CollectVisibleVolumes(cameraData.camera, passData.volumes);
                    passData.detailTexture = SnowDetailTexture.Get();
                    passData.ambientProbe = PackAmbientProbe(RenderSettings.ambientProbe);

                    builder.UseTexture(sceneCopy, AccessFlags.Read);
                    if (exclusion.IsValid())
                        builder.UseTexture(exclusion, AccessFlags.Read);
                    if (resourceData.cameraNormalsTexture.IsValid())
                        builder.UseTexture(resourceData.cameraNormalsTexture, AccessFlags.Read);
                    if (resourceData.cameraDepthTexture.IsValid())
                        builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                    if (resourceData.mainShadowsTexture.IsValid())
                        builder.UseTexture(resourceData.mainShadowsTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(source, 0, AccessFlags.Write);
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc((CompositePassData data, RasterGraphContext context) =>
                    {
                        var cmd = context.cmd;
                        var material = data.material;

                        // The appearance asset is the single source of truth: the Appearance
                        // tab edits it, so its values are copied in rather than duplicated as
                        // serialized fields on the feature.
                        if (data.appearance != null)
                            material.CopyPropertiesFromMaterial(data.appearance);

                        material.SetVector("_AmbientProbe0", data.ambientProbe[0]);
                        material.SetVector("_AmbientProbe1", data.ambientProbe[1]);
                        material.SetVector("_AmbientProbe2", data.ambientProbe[2]);
                        material.SetVector("_AmbientProbe3", data.ambientProbe[3]);
                        material.SetVector("_AmbientProbe4", data.ambientProbe[4]);
                        material.SetVector("_AmbientProbe5", data.ambientProbe[5]);
                        material.SetVector("_AmbientProbe6", data.ambientProbe[6]);

                        if (data.useNormals)
                            material.EnableKeyword(NormalsKeyword);
                        else
                            material.DisableKeyword(NormalsKeyword);

                        material.SetFloat("_ExclusionEnabled", data.exclusion.IsValid() ? 1f : 0f);

                        // Neutral normal-and-height for the detail tap: 0.5 decodes to a flat
                        // normal, and 0.5 height leaves the albedo breakup unharmed.
                        material.SetTexture("_DetailTex",
                            data.detailTexture != null ? data.detailTexture : Texture2D.grayTexture);
                        if (data.exclusion.IsValid())
                            material.SetTexture("_ExclusionTex", data.exclusion);

                        var properties = DrawProperties;
                        properties.Clear();
                        RTHandle sceneCopyHandle = data.sceneCopy;
                        properties.SetTexture(BlitTextureId, sceneCopyHandle.rt);
                        properties.SetVector(BlitScaleBiasId, BlitScaleBias);
                        if (data.volumes.Count == 0)
                        {
                            properties.SetFloat(VolumeModeId, 0f);
                            DrawSnow(cmd, material, properties);
                            return;
                        }

                        // The first draw also shades untouched snow. Each later draw reads the
                        // same opaque colour and writes only inside its own world-space slab.
                        for (int i = 0; i < data.volumes.Count; i++)
                        {
                            VolumeDraw volume = data.volumes[i];
                            properties.SetFloat(VolumeModeId, i == 0 ? 1f : 2f);
                            properties.SetVector(VolumeMinId, volume.min);
                            properties.SetVector(VolumeSizeId, volume.size);
                            properties.SetFloat(HeightTexelId, volume.texel);
                            properties.SetTexture(HeightTextureId, volume.texture);
                            DrawSnow(cmd, material, properties);
                        }
                    });
                }
            }

            private static Vector4[] PackAmbientProbe(SphericalHarmonicsL2 probe)
            {
                var coefficients = new SHCoefficients(probe);
                return new[]
                {
                    coefficients.SHAr, coefficients.SHAg, coefficients.SHAb,
                    coefficients.SHBr, coefficients.SHBg, coefficients.SHBb, coefficients.SHC
                };
            }

            private static void DrawSnow(RasterCommandBuffer cmd, Material material, MaterialPropertyBlock properties)
            {
                cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, properties);
            }

            private void CollectVisibleVolumes(Camera camera, List<VolumeDraw> draws)
            {
                draws.Clear();
                bool testFrustum = !camera.stereoEnabled;
                if (testFrustum)
                    GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);

                var volumes = DeformableSnowSystem.ActiveVolumes;
                for (int i = 0; i < volumes.Count; i++)
                {
                    DeformableSnowVolume volume = volumes[i];
                    if (volume == null || volume.HeightTexture == null)
                        continue;

                    Bounds bounds = volume.Bounds;
                    // Terrain LOD can move reconstructed ground slightly outside the capture slab.
                    bounds.Expand(new Vector3(0f, 0.5f, 0f));
                    if (testFrustum && !GeometryUtility.TestPlanesAABB(frustumPlanes, bounds))
                        continue;

                    draws.Add(new VolumeDraw
                    {
                        min = new Vector4(bounds.min.x, bounds.min.z, bounds.min.y, 0f),
                        size = new Vector4(bounds.size.x, bounds.size.z, bounds.size.y, 0f),
                        texel = volume.HeightTexelSize,
                        texture = volume.HeightTexture
                    });
                }
            }

            private static DrawingSettings CreateDrawingSettings(Camera camera, Material overrideMaterial)
            {
                var sorting = new SortingSettings(camera) { criteria = SortingCriteria.CommonOpaque };
                var settings = new DrawingSettings(new ShaderTagId("UniversalForward"), sorting)
                {
                    overrideMaterial = overrideMaterial,
                    overrideMaterialPassIndex = 0,
                    enableDynamicBatching = false,
                    enableInstancing = false,
                    perObjectData = PerObjectData.None
                };
                settings.SetShaderPassName(1, new ShaderTagId("UniversalForwardOnly"));
                settings.SetShaderPassName(2, new ShaderTagId("SRPDefaultUnlit"));
                return settings;
            }
        }
    }
}
