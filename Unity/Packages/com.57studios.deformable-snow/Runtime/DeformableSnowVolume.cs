using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Studios57.DeformableSnow
{
    /// <summary>
    /// One bounded snow heightfield over the terrain. Each frame the volume renders every
    /// registered deformer into a thin orthographic slab, blurs the coverage mask, and
    /// accumulates it into a persistent GPU texture. Nothing is read back to the CPU.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeformableSnowVolume : MonoBehaviour
    {
        private const int ThreadsPerAxis = 8;
        private const float CaptureBaseMargin = 0.25f;
        private static Mesh footprintMesh;
        private static readonly int GroundPlaneId = Shader.PropertyToID("_FootprintGroundPlane");
        private MaterialPropertyBlock footprintProperties;
        private Transform followTarget;
        private LayerMask footprintGroundLayers;
        private Vector2Int followCell;
        private bool haveFollowCell;
        private float previousFollowY;

        [Header("Bounds")]
        [Tooltip("World-space XZ footprint of the volume. Phase 1 is axis aligned.")]
        [SerializeField] private Vector2 worldSize = new Vector2(10f, 10f);
        [Tooltip("World Y of the deepest point of the snow surface inside the volume.")]
        [SerializeField] private float surfaceHeight;
        [Tooltip("Height of the capture slab above surfaceHeight. Must cover the terrain height spread.")]
        [Min(0.05f)] [SerializeField] private float captureDepth = 0.8f;
        [Min(64)] [SerializeField] private int resolution = 1024;

        [Header("Accumulation")]
        [Tooltip("World-space depth represented by a height of 1. Used by shading, not by Phase 1.")]
        [Min(0f)] [SerializeField] private float snowDepth = 0.06f;
        [Min(0f)] [SerializeField] private float contactGain = 2.5f;
        [Min(0f)] [SerializeField] private float refillPerSecond = 0.02f;
        [Tooltip("How far above the local terrain a deformer must be to stop writing. Without this, a lifted foot still marks the snow because the capture slab is taller than the contact band.")]
        [Min(0.001f)] [SerializeField] private float contactBand = 0.06f;
        [SerializeField] private bool captureEveryFrame = true;

        [Header("Optional overrides")]
        [SerializeField] private ComputeShader computeOverride;
        [SerializeField] private Terrain terrain;

        private RenderTexture captureTarget;
        private RenderTexture blurHorizontal;
        private RenderTexture blurredTarget;
        private RenderTexture heightA;
        private RenderTexture heightB;
        private RenderTexture visualTarget;
        private Texture2D terrainHeightReference;

        private Material captureMaterial;
        private ComputeShader compute;
        private int blurHorizontalKernel;
        private int blurVerticalKernel;
        private int accumulateKernel;
        private int visualizeKernel;

        private CommandBuffer captureCommands;
        private CommandBuffer computeCommands;
        private int debugMode = -1;
        private readonly List<Material> materialBuffer = new List<Material>();

        public RenderTexture VisualTexture => visualTarget;
        public RenderTexture HeightTexture => heightA;
        public RenderTexture CaptureTexture => captureTarget;
        public int Resolution => resolution;
        public Vector2 WorldSize => worldSize;
        public float SnowDepth => snowDepth;
        public float SurfaceHeight => surfaceHeight;
        public float CaptureDepth => captureDepth;
        public float HeightTexelSize => 1f / Mathf.Max(1, resolution);
        public float ContactBand => contactBand;

        /// <summary>Diagnostics for Phase 1 validation only.</summary>
        public int DeformerCount => DeformableSnowSystem.ActiveDeformers.Count;
        public int LastCapturedDraws { get; private set; }

        // The capture basis and the D3D render-into-texture Y flip cancel on X but not on
        // Y, so the raw mask arrives mirrored in texture V (world Z). The blur kernel
        // applies this correction before anything consumes the heightfield, which is why
        // the residual is invisible to the trail, the shading and the probes.
        private bool captureFlipX = false;
        private bool captureFlipY = true;

        /// <summary>Diagnostics / tuning hook for the capture orientation.</summary>
        public void SetCaptureFlip(bool flipX, bool flipY)
        {
            captureFlipX = flipX;
            captureFlipY = flipY;
        }

        public Bounds Bounds
        {
            get
            {
                Vector3 center = new Vector3(transform.position.x, surfaceHeight + captureDepth * 0.5f, transform.position.z);
                return new Bounds(center, new Vector3(worldSize.x, captureDepth, worldSize.y));
            }
        }

        private void OnEnable()
        {
            DeformableSnowSystem.Register(this);
        }

        private void OnDisable()
        {
            DeformableSnowSystem.Unregister(this);
            ReleaseResources();
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        /// <summary>Configured by the Phase 1 scene builder and by future authoring tools.</summary>
        public void Configure(Vector2 size, float surfaceY, float depth, int texels, float depthMetres, float gain, float refill,
            float contactBandMetres = 0.06f)
        {
            worldSize = new Vector2(Mathf.Max(0.5f, size.x), Mathf.Max(0.5f, size.y));
            surfaceHeight = surfaceY;
            captureDepth = Mathf.Max(0.05f, depth);
            resolution = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(64, texels)), 64, 2048);
            snowDepth = Mathf.Max(0f, depthMetres);
            contactGain = Mathf.Max(0f, gain);
            refillPerSecond = Mathf.Max(0f, refill);
            contactBand = Mathf.Max(0.001f, contactBandMetres);
        }

        /// <summary>
        /// Binds the terrain this volume sits on, so the capture bakes its reference from the
        /// right surface instead of guessing. Authoring tools set this explicitly.
        /// </summary>
        public void SetTerrain(Terrain targetTerrain)
        {
            if (terrain == targetTerrain)
                return;

            terrain = targetTerrain;
            ReleaseTerrainReference();
            if (captureMaterial != null)
                BakeTerrainReference();
        }

        /// <summary>Tracks a character and uses the given collision layers as the ground for its footprint boxes.</summary>
        public void Follow(Transform target, LayerMask groundLayers)
        {
            followTarget = target;
            footprintGroundLayers = groundLayers;
            haveFollowCell = false;
            ReleaseResources();
        }

        /// <summary>Called by the debug view. Pass -1 to stop visualising.</summary>
        public void SetDebugMode(int mode)
        {
            debugMode = mode;
            if (mode < 0)
                DestroyTarget(ref visualTarget);
        }

        private void EnsureVisualTarget()
        {
            if (visualTarget == null)
                visualTarget = CreateTarget("Visual", RenderTextureFormat.ARGB32, resolution, 0, true);
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
                return;

            if (!EnsureResources())
                return;

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            Vector2Int offset = UpdateFollowPosition();
            bool captured = false;
            if (captureEveryFrame)
            {
                Capture();
                captured = true;
            }

            Simulate(dt, captured, offset);
        }

        private Vector2Int UpdateFollowPosition()
        {
            if (followTarget == null)
                return Vector2Int.zero;

            const int texelsPerCell = 32;
            Vector3 position = followTarget.position;
            var cellSize = new Vector2(worldSize.x, worldSize.y) * (texelsPerCell / (float)resolution);
            var cell = new Vector2Int(Mathf.RoundToInt(position.x / cellSize.x), Mathf.RoundToInt(position.z / cellSize.y));
            Vector2Int offset = haveFollowCell ? (cell - followCell) * texelsPerCell : Vector2Int.zero;
            if (haveFollowCell && Mathf.Abs(position.y - previousFollowY) > captureDepth * 0.5f)
                offset = new Vector2Int(resolution, 0);
            followCell = cell;
            haveFollowCell = true;
            previousFollowY = position.y;
            transform.position = new Vector3(cell.x * cellSize.x, position.y, cell.y * cellSize.y);
            surfaceHeight = position.y - captureDepth * 0.5f;
            return offset;
        }

        private bool EnsureResources()
        {
            if (captureTarget != null && captureTarget.width == resolution)
                return true;

            ReleaseResources();

            compute = computeOverride != null ? computeOverride : Resources.Load<ComputeShader>("DeformableSnow");
            Shader captureShader = Resources.Load<Shader>("DeformableSnowCapture");
            if (compute == null || captureShader == null)
            {
                Debug.LogError("[DeformableSnow] Missing compute shader or capture shader resource.", this);
                return false;
            }

            blurHorizontalKernel = compute.FindKernel("BlurH");
            blurVerticalKernel = compute.FindKernel("BlurV");
            accumulateKernel = compute.FindKernel("Accumulate");
            visualizeKernel = compute.FindKernel("Visualize");

            captureMaterial = new Material(captureShader)
            {
                name = "DeformableSnow Capture (runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };

            // The capture mask always overwrites, so it needs no depth buffer.
            captureTarget = CreateTarget("Capture", RenderTextureFormat.R8, resolution, 0, false);
            blurHorizontal = CreateTarget("BlurH", RenderTextureFormat.R8, resolution, 0, true);
            blurredTarget = CreateTarget("BlurV", RenderTextureFormat.R8, resolution, 0, true);
            // Bilinear heights: the surface shader samples between texels for displacement
            // and normals, and point sampling would stair-step the print edges.
            heightA = CreateTarget("HeightA", RenderTextureFormat.RFloat, resolution, 0, true, FilterMode.Bilinear);
            heightB = CreateTarget("HeightB", RenderTextureFormat.RFloat, resolution, 0, true, FilterMode.Bilinear);

            captureCommands = new CommandBuffer { name = "DeformableSnow Capture" };
            computeCommands = new CommandBuffer { name = "DeformableSnow Compute" };

            if (followTarget == null)
                BakeTerrainReference();
            ClearHeightfields();
            return true;
        }

        private RenderTexture CreateTarget(string label, RenderTextureFormat format, int size, int depthBits, bool randomWrite,
            FilterMode filter = FilterMode.Point)
        {
            var target = new RenderTexture(size, size, depthBits, format, RenderTextureReadWrite.Linear)
            {
                name = "DeformableSnow " + label,
                enableRandomWrite = randomWrite,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            target.Create();
            return target;
        }

        private void ClearHeightfields()
        {
            computeCommands.Clear();
            computeCommands.SetRenderTarget(heightA);
            computeCommands.ClearRenderTarget(false, true, Color.clear);
            computeCommands.SetRenderTarget(heightB);
            computeCommands.ClearRenderTarget(false, true, Color.clear);
            Graphics.ExecuteCommandBuffer(computeCommands);
        }

        /// <summary>
        /// Small world-height grid over the volume, used by the capture shader to reject
        /// geometry that is above the local ground. The terrain is a bilinear heightmap, so
        /// 33 x 33 over an 8 m volume reproduces it to well under a millimetre.
        /// </summary>
        private void BakeTerrainReference()
        {
            Terrain target = ResolveTerrain();
            if (target == null || target.terrainData == null)
            {
                Debug.LogWarning("[DeformableSnow] No terrain found; the capture cannot reject lifted deformers.", this);
                return;
            }

            const int res = 33;
            var texture = new Texture2D(res, res, TextureFormat.RFloat, false, true)
            {
                name = "DeformableSnow TerrainRef",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            var heights = new float[res * res];
            float terrainY = target.transform.position.y;
            float x0 = transform.position.x - worldSize.x * 0.5f;
            float z0 = transform.position.z - worldSize.y * 0.5f;
            for (int z = 0; z < res; z++)
            {
                float worldZ = z0 + worldSize.y * z / (res - 1f);
                for (int x = 0; x < res; x++)
                {
                    float worldX = x0 + worldSize.x * x / (res - 1f);
                    heights[z * res + x] = target.SampleHeight(new Vector3(worldX, 0f, worldZ)) + terrainY;
                }
            }

            texture.SetPixelData(heights, 0);
            texture.Apply(false, true);
            terrainHeightReference = texture;
        }

        private Terrain ResolveTerrain()
        {
            if (terrain != null && terrain.terrainData != null)
                return terrain;

            Terrain covering = null;
            Terrain fallback = null;
            float bestArea = 0f;
            Vector3 position = transform.position;
            foreach (Terrain candidate in Terrain.activeTerrains)
            {
                if (candidate.terrainData == null)
                    continue;

                bool sameScene = candidate.gameObject.scene == gameObject.scene;
                Vector3 origin = candidate.transform.position;
                Vector3 size = candidate.terrainData.size;
                if (position.x >= origin.x && position.x <= origin.x + size.x
                    && position.z >= origin.z && position.z <= origin.z + size.z)
                {
                    if (sameScene)
                        return candidate;
                    covering ??= candidate;
                }

                float area = size.x * size.z;
                if (sameScene && area > bestArea)
                {
                    bestArea = area;
                    fallback = candidate;
                }
            }

            return covering != null ? covering : fallback;
        }

        private void Capture()
        {
            captureCommands.Clear();
            captureCommands.SetRenderTarget(captureTarget);
            captureCommands.ClearRenderTarget(false, true, Color.clear);

            if (terrainHeightReference != null)
            {
                captureMaterial.SetTexture("_TerrainHeightTex", terrainHeightReference);
                captureMaterial.SetVector("_CaptureOrigin", new Vector4(
                    transform.position.x - worldSize.x * 0.5f,
                    transform.position.z - worldSize.y * 0.5f, 0f, 0f));
                captureMaterial.SetVector("_CaptureExtent", new Vector4(worldSize.x, worldSize.y, 0f, 0f));
                captureMaterial.SetFloat("_ContactBand", contactBand);
            }
            else
            {
                // No terrain reference: keep capturing rather than silently producing an
                // empty mask, but the lifted-foot rejection will not work.
                captureMaterial.SetFloat("_ContactBand", followTarget != null ? contactBand : 1000f);
            }

            // Orthographic slab looking straight up at the scene. The basis from
            // LookRotation(up, forward) is right = -X, up = +Z, forward = +Y, and the
            // render-into-texture Y flip applied by GL.GetGPUProjectionMatrix leaves the
            // image mirrored on one axis. The residue is corrected by the CaptureFlip
            // flags in the blur kernel rather than by nudging these bounds: flipping the
            // ortho Y bounds moves the slab out of the render range and captures nothing.
            Vector3 origin = new Vector3(transform.position.x, surfaceHeight - CaptureBaseMargin, transform.position.z);
            Quaternion rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            // Unity cameras look down -Z in view space; without this scale the whole
            // slab ends up behind the capture camera and nothing rasterises.
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(origin, rotation, Vector3.one).inverse;

            float half = worldSize.x * 0.5f;
            Matrix4x4 ortho = Matrix4x4.Ortho(half, -half, -half, half, 0.01f, CaptureBaseMargin + captureDepth);
            // Command buffers bypass the camera's platform conversion. Without this the
            // OpenGL-style depth range clips half the slab on D3D.
            captureCommands.SetViewProjectionMatrices(view, GL.GetGPUProjectionMatrix(ortho, true));

            Bounds captureBounds = Bounds;
            captureBounds.Expand(0.5f);

            IReadOnlyList<DeformableSnowDeformer> deformers = DeformableSnowSystem.ActiveDeformers;
            LastCapturedDraws = 0;
            for (int d = 0; d < deformers.Count; d++)
            {
                DeformableSnowDeformer deformer = deformers[d];
                if (deformer == null)
                    continue;

                if (deformer.Footprint != null)
                {
                    if (followTarget == null || deformer.transform.IsChildOf(followTarget))
                        CaptureFootprint(deformer.Footprint, captureBounds);
                    continue;
                }

                if (followTarget != null)
                    continue;

                IReadOnlyList<Renderer> renderers = deformer.Renderers;
                for (int r = 0; r < renderers.Count; r++)
                {
                    Renderer renderer = renderers[r];
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                        continue;
                    if (!renderer.bounds.Intersects(captureBounds))
                        continue;

                    materialBuffer.Clear();
                    renderer.GetSharedMaterials(materialBuffer);
                    for (int s = 0; s < materialBuffer.Count; s++)
                    {
                        captureCommands.DrawRenderer(renderer, captureMaterial, s, 0);
                        LastCapturedDraws++;
                    }
                }
            }

            Graphics.ExecuteCommandBuffer(captureCommands);
        }

        private void CaptureFootprint(BoxCollider shape, Bounds captureBounds)
        {
            if (!shape.gameObject.activeInHierarchy)
                return;

            Matrix4x4 matrix = shape.transform.localToWorldMatrix
                * Matrix4x4.TRS(shape.center, Quaternion.identity, shape.size);
            // The configured box describes a unit cube. Collider.bounds is empty when physics is disabled.
            var size = new Vector3(
                Mathf.Abs(matrix.m00) + Mathf.Abs(matrix.m01) + Mathf.Abs(matrix.m02),
                Mathf.Abs(matrix.m10) + Mathf.Abs(matrix.m11) + Mathf.Abs(matrix.m12),
                Mathf.Abs(matrix.m20) + Mathf.Abs(matrix.m21) + Mathf.Abs(matrix.m22));
            var bounds = new Bounds(matrix.GetColumn(3), size);
            if (!bounds.Intersects(captureBounds))
                return;

            Vector4 groundPlane = Vector4.zero;
            if (followTarget != null)
            {
                Vector3 origin = bounds.center + Vector3.up * (bounds.extents.y + contactBand);
                float distance = bounds.size.y + 2f * contactBand;
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance, footprintGroundLayers,
                        QueryTriggerInteraction.Ignore) || hit.normal.y <= 0.1f)
                    return;

                Vector3 normal = hit.normal;
                groundPlane = new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, hit.point));
            }

            if (footprintMesh == null)
                footprintMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            footprintProperties ??= new MaterialPropertyBlock();
            footprintProperties.SetVector(GroundPlaneId, groundPlane);
            captureCommands.DrawMesh(footprintMesh, matrix, captureMaterial, 0, 0, footprintProperties);
            LastCapturedDraws++;
        }

        private void Simulate(float dt, bool captured, Vector2Int offset)
        {
            int groups = Mathf.CeilToInt(resolution / (float)ThreadsPerAxis);

            computeCommands.Clear();
            computeCommands.SetComputeIntParam(compute, "Resolution", resolution);
            computeCommands.SetComputeIntParam(compute, "CaptureFlipX", captureFlipX ? 1 : 0);
            computeCommands.SetComputeIntParam(compute, "CaptureFlipY", captureFlipY ? 1 : 0);

            // The blur only feeds the contact term, so when the capture was skipped the
            // whole chain can be skipped. Accumulate still runs, with zero contact, so the
            // refill keeps advancing.
            bool blur = captured;
            if (blur)
            {
                computeCommands.SetComputeTextureParam(compute, blurHorizontalKernel, "CaptureTex", captureTarget);
                computeCommands.SetComputeTextureParam(compute, blurHorizontalKernel, "OutputTex", blurHorizontal);
                computeCommands.DispatchCompute(compute, blurHorizontalKernel, groups, groups, 1);

                computeCommands.SetComputeTextureParam(compute, blurVerticalKernel, "CaptureTex", blurHorizontal);
                computeCommands.SetComputeTextureParam(compute, blurVerticalKernel, "OutputTex", blurredTarget);
                computeCommands.DispatchCompute(compute, blurVerticalKernel, groups, groups, 1);
            }

            computeCommands.SetComputeIntParams(compute, "HeightOffset", offset.x, offset.y);
            computeCommands.SetComputeFloatParam(compute, "Gain", contactGain);
            computeCommands.SetComputeFloatParam(compute, "DeltaTime", dt);
            computeCommands.SetComputeFloatParam(compute, "RefillPerSecond", refillPerSecond);
            // Zero when nothing was captured: the mask here is last frame's, and re-adding it
            // would deepen a print under a foot that is no longer moving.
            computeCommands.SetComputeFloatParam(compute, "ContactScale", captured ? 1f : 0f);
            computeCommands.SetComputeTextureParam(compute, accumulateKernel, "CaptureTex", blurredTarget);
            computeCommands.SetComputeTextureParam(compute, accumulateKernel, "SourceTex", heightA);
            computeCommands.SetComputeTextureParam(compute, accumulateKernel, "OutputTex", heightB);
            computeCommands.DispatchCompute(compute, accumulateKernel, groups, groups, 1);

            if (debugMode >= 0)
            {
                EnsureVisualTarget();
                computeCommands.SetComputeIntParam(compute, "Mode", debugMode);
                computeCommands.SetComputeFloatParam(compute, "NormalStrength", resolution * 0.02f);
                computeCommands.SetComputeTextureParam(compute, visualizeKernel, "SourceTex", heightB);
                computeCommands.SetComputeTextureParam(compute, visualizeKernel, "CaptureTex", captureTarget);
                computeCommands.SetComputeTextureParam(compute, visualizeKernel, "VisualTex", visualTarget);
                computeCommands.DispatchCompute(compute, visualizeKernel, groups, groups, 1);
            }

            Graphics.ExecuteCommandBuffer(computeCommands);

            // heightB holds this frame's result; it becomes the source next frame.
            RenderTexture previous = heightA;
            heightA = heightB;
            heightB = previous;
        }

        private void ReleaseResources()
        {
            captureCommands?.Release();
            computeCommands?.Release();
            captureCommands = null;
            computeCommands = null;

            DestroyTarget(ref captureTarget);
            DestroyTarget(ref blurHorizontal);
            DestroyTarget(ref blurredTarget);
            DestroyTarget(ref heightA);
            DestroyTarget(ref heightB);
            DestroyTarget(ref visualTarget);

            ReleaseTerrainReference();

            if (captureMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(captureMaterial);
                else
                    DestroyImmediate(captureMaterial);
                captureMaterial = null;
            }
        }

        private void ReleaseTerrainReference()
        {
            if (terrainHeightReference == null)
                return;
            if (Application.isPlaying)
                Destroy(terrainHeightReference);
            else
                DestroyImmediate(terrainHeightReference);
            terrainHeightReference = null;
        }

        private static void DestroyTarget(ref RenderTexture target)
        {
            if (target == null)
                return;
            target.Release();
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
            target = null;
        }

        private void OnDrawGizmosSelected()
        {
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(
                new Vector3(transform.position.x, surfaceHeight, transform.position.z),
                Quaternion.identity,
                new Vector3(worldSize.x, captureDepth, worldSize.y));
            Gizmos.color = new Color(0.55f, 0.75f, 1f, 0.25f);
            Gizmos.DrawWireCube(new Vector3(0f, 0.5f, 0f), Vector3.one);
            Gizmos.matrix = previous;
        }
    }
}

