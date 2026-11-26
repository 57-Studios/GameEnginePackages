using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Studios57.ProceduralGrass
{
    /// <summary>
    /// Phase 1 vertical slice: samples one Unity Terrain layer in a compute shader,
    /// compacts surviving blade instances on the GPU, and renders them indirectly.
    /// This is intentionally a debug renderer: no authored blade asset, wind, shadows,
    /// terrain interaction, or multi-species support is included yet.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GpuGrassRenderer : MonoBehaviour
    {
        private const int ThreadsPerAxis = 8;
        private const int InstanceStride = 32;

        [Header("Required")]
        [SerializeField] private Terrain terrain;
        [SerializeField] private ComputeShader placementCompute;
        [SerializeField] private Material debugMaterial;

        [Header("Terrain Layer Density")]
        [Min(0)] [SerializeField] private int terrainLayerIndex;
        [Range(0f, 1f)] [SerializeField] private float densityMultiplier = 1f;

        [Header("Phase 1 Chunk Grid")]
        [Min(4f)] [SerializeField] private float chunkSize = 32f;
        [Range(1, 7)] [SerializeField] private int chunksPerAxis = 3;
        [Range(4, 64)] [SerializeField] private int cellsPerChunkAxis = 24;
        [Min(1f)] [SerializeField] private float drawDistance = 60f;

        [Header("Debug Blade")]
        [Min(0.01f)] [SerializeField] private float minHeight = 0.25f;
        [Min(0.01f)] [SerializeField] private float maxHeight = 0.7f;
        [Min(0.001f)] [SerializeField] private float minWidth = 0.01f;
        [Min(0.001f)] [SerializeField] private float maxWidth = 0.03f;
        [Range(1, 12)] [SerializeField] private int bladeSegments = 4;

        private readonly GraphicsBuffer.IndirectDrawIndexedArgs[] drawArgs = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        private Material runtimeMaterial;
        private MaterialPropertyBlock materialProperties;
        private Mesh bladeTopology;
        private GraphicsBuffer visibleInstances;
        private GraphicsBuffer indirectArguments;
        private int kernel = -1;
        private int allocatedCandidateCount;

        private static readonly int VisibleGrassId = Shader.PropertyToID("_VisibleGrass");
        private static readonly int HeightmapId = Shader.PropertyToID("_Heightmap");
        private static readonly int ControlMapId = Shader.PropertyToID("_ControlMap");
        private static readonly int TerrainPositionId = Shader.PropertyToID("_TerrainPosition");
        private static readonly int TerrainSizeId = Shader.PropertyToID("_TerrainSize");
        private static readonly int HeightTexelSizeId = Shader.PropertyToID("_HeightTexelSize");
        private static readonly int CenterChunkId = Shader.PropertyToID("_CenterChunk");
        private static readonly int CurrentChunkId = Shader.PropertyToID("_CurrentChunk");
        private static readonly int ChunksPerAxisId = Shader.PropertyToID("_ChunksPerAxis");
        private static readonly int CellsPerChunkAxisId = Shader.PropertyToID("_CellsPerChunkAxis");
        private static readonly int ChunkSizeId = Shader.PropertyToID("_ChunkSize");
        private static readonly int TerrainLayerChannelId = Shader.PropertyToID("_TerrainLayerChannel");
        private static readonly int DensityMultiplierId = Shader.PropertyToID("_DensityMultiplier");
        private static readonly int DrawDistanceId = Shader.PropertyToID("_DrawDistance");
        private static readonly int CameraPositionId = Shader.PropertyToID("_CameraPosition");
        private static readonly int ViewProjectionId = Shader.PropertyToID("_ViewProjection");
        private static readonly int MinHeightId = Shader.PropertyToID("_MinHeight");
        private static readonly int MaxHeightId = Shader.PropertyToID("_MaxHeight");
        private static readonly int MinWidthId = Shader.PropertyToID("_MinWidth");
        private static readonly int MaxWidthId = Shader.PropertyToID("_MaxWidth");
        private static readonly int GrassInstancesId = Shader.PropertyToID("_GrassInstances");

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            TryInitialize();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            ReleaseResources();
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != Camera.main || !TryInitialize())
                return;

            Render(camera);
        }

        private bool TryInitialize()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsInstancing || !SystemInfo.supportsIndirectArgumentsBuffer)
            {
                enabled = false;
                return false;
            }

            if (terrain == null || terrain.terrainData == null || placementCompute == null)
                return false;

            if (kernel < 0)
                kernel = placementCompute.FindKernel("PlaceAndCull");

            int candidateCount = chunksPerAxis * chunksPerAxis * cellsPerChunkAxis * cellsPerChunkAxis;
            if (visibleInstances == null || allocatedCandidateCount != candidateCount)
            {
                ReleaseBuffers();
                allocatedCandidateCount = candidateCount;
                visibleInstances = new GraphicsBuffer(GraphicsBuffer.Target.Append | GraphicsBuffer.Target.Structured, candidateCount, InstanceStride);
                indirectArguments = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            }

            if (bladeTopology == null || bladeTopology.vertexCount != (bladeSegments + 1) * 2)
                CreateBladeTopology();

            if (runtimeMaterial == null)
            {
                runtimeMaterial = debugMaterial;
                if (runtimeMaterial == null)
                {
                    Shader shader = Shader.Find("57Studios/ProceduralGrass/Debug");
                    if (shader == null)
                        return false;

                    runtimeMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                }

                runtimeMaterial.enableInstancing = true;
            }

            materialProperties ??= new MaterialPropertyBlock();
            return true;
        }

        private void Render(Camera camera)
        {
            TerrainData terrainData = terrain.terrainData;
            Texture2D[] alphaMaps = terrainData.alphamapTextures;
            int controlMapIndex = terrainLayerIndex / 4;
            if (controlMapIndex < 0 || controlMapIndex >= alphaMaps.Length || terrainData.heightmapTexture == null)
                return;

            int cellsPerChunk = cellsPerChunkAxis;
            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = terrainData.size;
            Vector3 relativeCamera = camera.transform.position - terrainPosition;
            Vector2Int centerChunk = new Vector2Int(
                Mathf.FloorToInt(relativeCamera.x / chunkSize),
                Mathf.FloorToInt(relativeCamera.z / chunkSize));

            visibleInstances.SetCounterValue(0);
            placementCompute.SetBuffer(kernel, VisibleGrassId, visibleInstances);
            placementCompute.SetTexture(kernel, HeightmapId, terrainData.heightmapTexture);
            placementCompute.SetTexture(kernel, ControlMapId, alphaMaps[controlMapIndex]);
            placementCompute.SetVector(TerrainPositionId, terrainPosition);
            placementCompute.SetVector(TerrainSizeId, terrainSize);
            float heightTexel = 1f / Mathf.Max(1, terrainData.heightmapResolution - 1);
            placementCompute.SetVector(HeightTexelSizeId, new Vector4(heightTexel, heightTexel, 0f, 0f));
            placementCompute.SetInt(ChunksPerAxisId, chunksPerAxis);
            placementCompute.SetInt(CellsPerChunkAxisId, cellsPerChunk);
            placementCompute.SetFloat(ChunkSizeId, chunkSize);
            placementCompute.SetInt(TerrainLayerChannelId, terrainLayerIndex % 4);
            placementCompute.SetFloat(DensityMultiplierId, densityMultiplier);
            placementCompute.SetFloat(DrawDistanceId, drawDistance);
            placementCompute.SetVector(CameraPositionId, camera.transform.position);
            Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            placementCompute.SetMatrix(ViewProjectionId, gpuProjection * camera.worldToCameraMatrix);
            placementCompute.SetFloat(MinHeightId, Mathf.Min(minHeight, maxHeight));
            placementCompute.SetFloat(MaxHeightId, Mathf.Max(minHeight, maxHeight));
            placementCompute.SetFloat(MinWidthId, Mathf.Min(minWidth, maxWidth));
            placementCompute.SetFloat(MaxWidthId, Mathf.Max(minWidth, maxWidth));
            int chunkRadius = chunksPerAxis / 2;
            int groupsPerAxis = Mathf.CeilToInt(cellsPerChunk / (float)ThreadsPerAxis);
            for (int chunkZ = 0; chunkZ < chunksPerAxis; chunkZ++)
            {
                for (int chunkX = 0; chunkX < chunksPerAxis; chunkX++)
                {
                    placementCompute.SetInts(
                        CurrentChunkId,
                        centerChunk.x + chunkX - chunkRadius,
                        centerChunk.y + chunkZ - chunkRadius);
                    placementCompute.Dispatch(kernel, groupsPerAxis, groupsPerAxis, 1);
                }
            }

            drawArgs[0].indexCountPerInstance = bladeTopology.GetIndexCount(0);
            drawArgs[0].instanceCount = 0;
            drawArgs[0].startIndex = bladeTopology.GetIndexStart(0);
            drawArgs[0].baseVertexIndex = bladeTopology.GetBaseVertex(0);
            drawArgs[0].startInstance = 0;
            indirectArguments.SetData(drawArgs);
            GraphicsBuffer.CopyCount(visibleInstances, indirectArguments, sizeof(uint));

            materialProperties.Clear();
            // Bind directly as well as through the property block. The runtime fallback
            // material is feature-owned, and this keeps StructuredBuffer binding reliable
            // for RenderMeshIndirect across URP backends.
            runtimeMaterial.SetBuffer(GrassInstancesId, visibleInstances);
            materialProperties.SetBuffer(GrassInstancesId, visibleInstances);
            float coverage = chunksPerAxis * chunkSize;
            Bounds bounds = new Bounds(
                new Vector3(camera.transform.position.x, terrainPosition.y + terrainSize.y * 0.5f, camera.transform.position.z),
                new Vector3(coverage, terrainSize.y + maxHeight + 4f, coverage));
            RenderParams renderParams = new RenderParams(runtimeMaterial)
            {
                worldBounds = bounds,
                matProps = materialProperties,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
            Graphics.RenderMeshIndirect(renderParams, bladeTopology, indirectArguments, 1);
        }

        private void CreateBladeTopology()
        {
            if (bladeTopology != null)
                Destroy(bladeTopology);

            int rowCount = bladeSegments + 1;
            Vector3[] vertices = new Vector3[rowCount * 2];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] indices = new int[bladeSegments * 6];
            for (int row = 0; row < rowCount; row++)
            {
                float t = row / (float)bladeSegments;
                int vertex = row * 2;
                vertices[vertex] = Vector3.zero;
                vertices[vertex + 1] = Vector3.zero;
                uvs[vertex] = new Vector2(0f, t);
                uvs[vertex + 1] = new Vector2(1f, t);
            }

            for (int segment = 0; segment < bladeSegments; segment++)
            {
                int vertex = segment * 2;
                int index = segment * 6;
                indices[index] = vertex;
                indices[index + 1] = vertex + 2;
                indices[index + 2] = vertex + 1;
                indices[index + 3] = vertex + 1;
                indices[index + 4] = vertex + 2;
                indices[index + 5] = vertex + 3;
            }

            bladeTopology = new Mesh { name = "57Studios Procedural Grass Debug Topology", hideFlags = HideFlags.DontSave };
            bladeTopology.vertices = vertices;
            bladeTopology.uv = uvs;
            bladeTopology.triangles = indices;
            bladeTopology.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        }

        private void ReleaseResources()
        {
            ReleaseBuffers();
            if (bladeTopology != null)
                Destroy(bladeTopology);
            if (runtimeMaterial != null && runtimeMaterial != debugMaterial)
                Destroy(runtimeMaterial);
            bladeTopology = null;
            runtimeMaterial = null;
            materialProperties = null;
            kernel = -1;
        }

        private void ReleaseBuffers()
        {
            visibleInstances?.Release();
            indirectArguments?.Release();
            visibleInstances = null;
            indirectArguments = null;
            allocatedCandidateCount = 0;
        }
    }
}
