# 57 Studios Procedural Grass

MIT licensed. Copyright © 2026 57Studios.

GPU-generated, GPU-compacted, indirectly drawn procedural terrain grass for URP. The renderer samples Unity Terrain height, normal, holes, and painted TerrainLayer weights, builds a procedural blade topology in memory, compacts valid instances on the GPU, and submits one indirect draw.

## Installation

### Assets install

Download [ProceduralGrass-v0.1.3.unitypackage](https://github.com/57-Studios/GameEnginePackages/releases/download/unity-procedural-grass-v0.1.3/ProceduralGrass-v0.1.3.unitypackage) and import it with **Assets → Import Package → Custom Package**. It lands under `Assets/57Studios/ProceduralGrass/`.

### UPM install

In Unity, open **Window → Package Manager → + → Add package from git URL**:

```text
https://github.com/57-Studios/GameEnginePackages.git?path=/Unity/Packages/com.57studios.procedural-grass#unity-procedural-grass-v0.1.3
```

UPM installs under `Packages/` and is read-only. Use the `.unitypackage` when you want the files under `Assets/`.

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

MIT. See `LICENSE.md`. You may use, modify, and redistribute this package, including commercially, provided the copyright and permission notice travels with it.
