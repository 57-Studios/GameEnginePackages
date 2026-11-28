# 57Studios ProceduralGrass - Development Record

## Ownership

`com.57studios.procedural-grass` is an MIT-licensed 57Studios rendering feature. Keep all feature-owned scripts, compute shaders, materials, editor tooling, test scenes, and documentation below this folder. Do not place production feature code in third-party asset folders or modify third-party packages to make the feature work.

## Purpose

Render terrain grass as GPU-generated, GPU-compacted, indirectly drawn procedural blades. The primary surface source is Unity Terrain data: height, normal, holes, and painted TerrainLayer/splat weights.

The complete intended architecture and delivery phases are in [ImplementationPlan.md](ImplementationPlan.md). This file is the living handoff record: update it whenever a decision, measurement, feature, limitation, or benchmark changes.

## Current status

| Item | Status |
|---|---|
| Feature folder | Established |
| Implementation | Phase 1 vertical slice validated in temporary Play Mode |
| Active phase | Phase 1 - GPU placement, compaction, and indirect debug draw |
| Render pipeline | URP 17.3.0 |
| Unity version | 6000.3.14f1 |
| First benchmark target | `Assets/NatureManufacture Assets/River Auto Material/Demo/URP RAM 2019 Demo Mobile.unity` |
| Selected Terrain | Scene object `Terrain`; 500 x 500 x 500 m; 9 TerrainLayers; 512 alphamap resolution |
| Secondary candidate | `Assets/Industrial_Zone_Modular_Pack/Scenes/Industrial_Zone.unity` with `Terrain_Industrial_Zone.asset` |
| Chosen target hardware | Pending user decision |
| Frame-time budget | Pending user decision |

## Phase 0 progress

### Completed

- Confirmed this project uses Unity 6000.3.14f1 and URP 17.3.0.
- Confirmed that the project contains an `Industrial_Zone` scene with a Unity Terrain that references `Terrain_Industrial_Zone.asset`.
- Inspected the currently open `URP RAM 2019 Demo Mobile` scene. It contains two Terrain components: `Floating Island` (70 x 100 x 70 m) and `Terrain` (500 x 500 x 500 m). Selected the 500 m `Terrain` as the initial benchmark surface because it is large enough to exercise chunk visibility and distance LOD.
- Recorded the selected Terrain's 9 alphamap layers and 512-resolution alphamap. TerrainLayer semantic mapping remains pending visual/art review.
- Confirmed the planned Unity 6 render path: compute shader + GPU buffer compaction + `Graphics.RenderMeshIndirect`.
- Read the Unity console before baseline capture: no grass-related errors are present. One existing project-wide warning reports that the legacy Input Manager is deprecated; it is unrelated to this renderer and is not being changed by this feature.
- Defined the required Phase 0 benchmark routes and measurements below.

### Not yet measured

- Baseline CPU/GPU frame time.
- Draw calls, batch count, triangles, and visible overdraw.
- Shadow-map cost.
- Terrain dimensions, TerrainLayer names/order, alphamap count/resolution, and terrain-hole coverage.
- Target device capabilities and quality-tier budget.

### Next Phase 0 action

Open the candidate scene in Unity, choose a representative camera route, and capture a baseline using the Unity Profiler and Frame Debugger. Record the results in the benchmark table below before writing rendering code.

### 2026-09-29 handoff entry

- Moved the original plan into this feature folder as `Documentation/ImplementationPlan.md`.
- Created this development record as the mandatory source of truth for future human and AI work.
- Made no implementation or scene changes.
- Phase 0 selection is complete; baseline performance capture is the next task. Do not treat the demo scene as a production-art decision. It is only a safe, large Terrain for validating chunking and splatmap access.

### 2026-09-29 Phase 1 vertical-slice entry

- Added `Runtime/GpuGrassRenderer.cs`, `Runtime/Studios57.ProceduralGrass.Runtime.asmdef`, `Compute/GpuGrassPlacement.compute`, and `Shaders/GpuGrassDebug.shader`.
- The renderer builds a procedural topology mesh in memory, samples a selected TerrainLayer control-map channel in a compute shader, derives terrain height and slope normal from the heightmap, compacts valid blade instances in an append buffer, copies that count to indirect arguments, and submits one `Graphics.RenderMeshIndirect` draw.
- It deliberately excludes art textures, alpha clipping, wind, shadows, interaction, multiple species, persistent scene attachment, and production LOD. Those remain later phases.
- Unity imported the new assets with no console errors, and the debug shader resolved successfully through `Shader.Find`.
- The initial temporary Play Mode test reported 1 indirect command and 1,618 compacted blade instances, but the grass was **not visible**. Those counters validated dispatch and draw submission only, not the Phase 1 visual exit criterion. The recorded 77 draw calls, 110,571 triangles, and `frameTime = 0.0090931` seconds are editor diagnostics, not a production performance conclusion.
- Important test-harness lesson: `FindObjectsByType<Terrain>` ordering changed between Edit and Play Mode. Future automation must identify the target Terrain by explicit reference, stable object path, or verified TerrainData identity - never by an array index.
- Exited Play Mode after validation; the third-party demo scene was not saved or modified.

### 2026-09-29 Phase 1 visual correction

- Reproduced the missing-grass screenshot in a headless Unity 6000.4.0f1 Editor using the Unity Pipeline camera capture. A GPU readback showed a generated blade at world Y=6.76 while `Terrain.SampleHeight` reported Y=13.44 at the same X/Z: the blades were buried.
- Root cause: `TerrainData.heightmapTexture` uses Unity's normalized height scale of `32766 / 65535`. The compute shader had treated its R16 sample as a full-range normalized height. Decode now multiplies by `65535 / 32766`, with packed-channel handling for Vulkan/GLES3/WebGPU.
- The renderer now submits from `RenderPipelineManager.beginCameraRendering` for the main camera, which also makes the separate Pipeline camera capture include its indirect draw. The height decode, not the earlier instance-count checks, resolved the visual failure.
- After reimporting the compute shader, the first inspected blade was world Y=11.23 versus Terrain Y=11.21, and the camera capture visibly contains grass: [Phase1_Grass_Visible.png](Phase1_Grass_Visible.png). The validation object remained unsaved in the third-party demo scene. This image intentionally uses oversized debug blades (1.5-2.2 m height, 0.08-0.12 m width) to make the result unambiguous; it is not an art-quality or performance benchmark.

## Benchmark protocol

Use the same build quality, resolution, scene state, and camera route for every comparison.

| Route | Purpose |
|---|---|
| Ground-level field | Tests alpha clipping, density, vertex work, and wind later |
| Broad vista | Tests far LOD, chunk activation, and shadows |
| Fast camera turn | Tests culling stability and frame spikes |
| Terrain edge | Tests chunk seams and bounds |
| Painted path/rock transition | Tests splatmap density mapping |
| Dense grass region | Tests worst-case overdraw and shadow cost |

Record the baseline and each feature milestone here:

| Date | Build/device | Resolution / quality | Route | CPU ms | GPU ms | Draw calls | Triangles | Shadow ms | Notes |
|---|---|---|---|---:|---:|---:|---:|---:|---|
| Pending | Pending | Pending | Pending | - | - | - | - | - | Baseline not captured |
| 2026-09-29 | NVIDIA GeForce RTX 3060 Laptop GPU; Intel i7-11800H | 1920x1080; Ultra; Editor Play Mode | Current default game view on selected 500 m Terrain | 7.88* | 2.37* | 80 | 134625 | Not isolated | Preliminary static-view snapshot; 0 indirect draws; 0 target-device frame cap |

\* Unity's live `UnityStats` editor counters reported `frameTime = 0.0078786` and `renderTime = 0.002368`; values are shown as milliseconds. This is **not** a shipping-build GPU profile or a repeatable camera-route measurement. It is a labeled starting snapshot only.

### Baseline machine and renderer capability

| Item | Measured value |
|---|---|
| GPU | NVIDIA GeForce RTX 3060 Laptop GPU (5994 MB reported) |
| CPU | 11th Gen Intel Core i7-11800H @ 2.30 GHz |
| Unity quality level | Ultra |
| Game-view resolution | 1920x1080 |
| Target frame rate | Uncapped (`-1`) |
| Compute shaders | Supported |
| GPU instancing | Supported |
| Indirect argument buffers | Supported |

The preliminary snapshot confirms that the development machine supports the planned compute-plus-indirect path. It does not establish the final performance budget. Before Phase 1, capture the six benchmark routes in a reproducible standalone player build and record comparable CPU/GPU timings.

## Non-negotiable technical rules

- Do not create a GameObject or Transform for each grass blade.
- Do not perform GPU-to-CPU readback in the per-frame grass render path.
- Use deterministic world/chunk seeds so grass does not reshuffle at chunk borders or after culling.
- Sample terrain height, normal, holes, and mapped TerrainLayer weights consistently in world-space terrain UVs.
- Keep GPU buffers owned and released by the feature; rebuild safely after Terrain reassignment, editor recompilation, disable, or destroy.
- Use chunk bounds tight enough for Unity's indirect-render culling.
- Treat Hi-Z occlusion, advanced shadows, motion vectors, interaction, and multiple species as profiling-gated additions rather than baseline requirements.
- Benchmark visible alpha-clipped overdraw and shadows independently from compute compaction. Eliminating invisible blades does not make visible grass free.

## Expected feature layout

Create folders only when their corresponding implementation phase begins:

```text
Packages/com.57studios.procedural-grass/
  Documentation~/
    DEVELOPMENT.md
    ImplementationPlan.md
  Runtime/                 # Phase 1 C# runtime ownership
  Compute/                 # Phase 1 compute shaders
  Shaders/                 # Phase 2 URP blade shaders
  Materials/               # Phase 3 authored materials/profiles
  Editor/                  # Debugging and authoring tools
  Tests/                   # Automated tests and benchmark helpers
  Samples~/                # Isolated test scene only; no production-scene changes
```

## Required handoff update format

When any work is completed, append a dated entry with:

1. What changed and exact project paths.
2. Why the design decision was made.
3. Unity version, pipeline, and test device.
4. Validation completed: console state, screenshots if visual, and profiling data if performance-related.
5. Known limitations, risks, and the next smallest safe task.

Never state that a performance optimization worked without a recorded baseline and comparable after-measurement.

## Open decisions

1. Confirm the initial benchmark scene/Terrain, or nominate another Terrain.
2. Define desktop target GPU(s), target resolution, and frame-time budget.
3. Map existing TerrainLayers to grass, path, rock, mud, and no-growth semantics.
4. Choose whether the first release needs only wind, or also shadow casting/receiving and player interaction.
5. Provide visual target: stylized field grass, short turf, reeds, or multi-species vegetation.

## External design references

- User-provided *Procedural Grass in Ghost of Tsushima* GDC presentation.
- [Unity Graphics.RenderMeshIndirect](https://docs.unity3d.com/ScriptReference/Graphics.RenderMeshIndirect.html)
- [Unity TerrainData](https://docs.unity3d.com/ScriptReference/TerrainData.html)
- [Unity TerrainData.alphamapTextures](https://docs.unity3d.com/ScriptReference/TerrainData-alphamapTextures.html)
- [Unity TerrainData.heightmapTexture](https://docs.unity3d.com/ScriptReference/TerrainData-heightmapTexture.html)

## 2026-11-26 UPM migration into GameEnginePackages

- Moved the package from `Assets/57Studios/ProceduralGrass` to `Unity/Packages/com.57studios.procedural-grass` in the GameEnginePackages repository.
- Added `package.json`, `README.md`, `CHANGELOG.md`, and `LICENSE.md`. The package ID is `com.57studios.procedural-grass`; Unity minimum is 6000.3 and the URP dependency is 17.3.0.
- Renamed `Documentation` to `Documentation~` so Unity does not import it into consumer projects.
- Added an EditMode smoke test assembly for the runtime component.
- TestBench now consumes the package through a local `file:` dependency instead of an `Assets/` copy.
