# 57 Studios Procedural Grass

Copyright © 2026 57Studios. All rights reserved. Proprietary addon.

GPU-generated, GPU-compacted, indirectly drawn procedural terrain grass for URP. The renderer samples Unity Terrain height, normal, holes, and painted TerrainLayer weights, builds a procedural blade topology in memory, compacts valid instances on the GPU, and submits one indirect draw.

## Status

Phase 1 vertical slice. It is a debug renderer, not a production grass system yet.

Included:

- compute-shader placement with deterministic world/chunk seeds
- GPU compaction into an append buffer
- indirect draw submission with chunk bounds
- TerrainLayer weight and heightmap sampling
- debug blade shader

Not included yet:

- authored blade assets or textures
- wind, shadows, player interaction, or multiple species
- production LOD or Hi-Z occlusion
- final art direction

## Requirements

- Unity 6000.3
- URP 17.3 or newer
- compute shaders, GPU instancing, and indirect argument buffers

## Setup

1. Add a `GpuGrassRenderer` component to a GameObject in the scene.
2. Assign the target `Terrain`.
3. Assign `Shaders/GpuGrassDebug.shader` to the debug material, or leave it unset to use the shader lookup fallback.
4. Select the Terrain Layer index to sample.
5. Enter Play Mode. The renderer submits from `RenderPipelineManager.beginCameraRendering` for the main camera.

The test harness uses oversized debug blades so the result is visible. It is not an art-quality or performance benchmark.

## Documentation

- `Documentation~/DEVELOPMENT.md` — current state, decisions, measurements, and next work
- `Documentation~/ImplementationPlan.md` — intended architecture and delivery phases

## License

Proprietary. See `LICENSE.md`. No permission is granted to use, modify, or redistribute this package except under a separate written agreement with 57Studios.
