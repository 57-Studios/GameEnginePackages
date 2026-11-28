# GPU-Driven Procedural Grass Rendering Plan

## Status and goal

Planning only. This document creates no scripts, shaders, materials, scenes, or project settings.

Build a reusable URP grass renderer inspired by the *Procedural Grass in Ghost of Tsushima* GDC talk. It will generate, cull, and draw grass on the GPU; use Unity Terrain layer weights as the main density source; and scale through world-space chunks and distance LOD.

It is intended to replace large grass-prefab GameObject populations. It is not intended to beat a well-built GPU-culled single-quad billboard system in raw cost at identical visible density. Its value is scalable visibility work plus better near-field blade form, wind, and material response.

## Project-specific decisions

- Project is Unity `6000.4.0f1` with URP `17.4.0`.
- Use `Graphics.RenderMeshIndirect` and `GraphicsBuffer` indirect commands. Unity 6 documents this as the current indirect mesh-rendering API.
- Build two small static blade *topology* meshes in code: near and mid LOD. They are not art assets; their vertices only encode segment position and left/right side. The vertex shader expands them into a blade from GPU instance data.
- Bind `TerrainData.alphamapTextures` for splat/layer weights, `TerrainData.heightmapTexture` for height, and the holes texture. Map world X/Z to normalized terrain UV from Terrain position and TerrainData size.
- Require compute shader and indirect-argument-buffer support at runtime. Unsupported devices use a deliberately simple fallback or disable this renderer; never silently spawn CPU per-blade objects.
- First target is desktop GPU. Mobile, WebGL, VR, and console are separate validation tracks, not assumed support.

## Architecture

```text
TerrainData: heightmap + alphamaps + holes
                  |
                  v
CPU chunk manager: identifies nearby chunks; uploads camera data
                  |
                  v
Compute: sample candidates -> reject / compact survivors -> instance buffers
                  |
                  +--> indirect argument buffer, per LOD / species
                  |
                  v
URP material + procedural blade vertex shader -> alpha-clipped draws
```

### Chunking

- Divide each Terrain into square X/Z chunks. Start experimentation at 16-32 metres, then choose from profiling rather than treating it as a fixed design value.
- Every chunk stores stable integer coordinates, world and UV bounds, deterministic seed, and candidate-cell resolution.
- CPU manages only active chunks. It never creates blade GameObjects or per-blade transforms.
- Compute work samples jittered candidate positions, terrain weights/height/normal, holes, density, distance, and frustum status. Valid blades are compacted into append buffers.
- Maintain separate near/mid buffers and indirect commands. Far grass becomes terrain-material tint/detail, not tiny blade geometry.
- Keep indirect draw bounds tight. Do not give every draw a terrain-sized world bound.

### Instance data

Keep the production layout compact and 16-byte aligned. Candidate fields are:

| Field | Purpose |
|---|---|
| Root position or terrain-local X/Z + height | Placement without a Transform |
| Packed facing and clump facing | Blade orientation and grouping |
| Random seed | Stable width, hue, tilt, bend, and wind variation |
| Species/material index | Grass-profile routing |
| LOD/fade | Dithered topology and density transition |
| Wind phase/sample | Coherent animation |

Do not store data that can be reconstructed from the seed, terrain UV, or profile constants.

## Art and authoring contract

### Minimum visual-prototype input

1. A Unity Terrain. A mesh-terrain adapter can be a later extension.
2. Existing painted TerrainLayers for grass-friendly soil, dirt/path, rock, mud, and other no-growth surfaces.
3. One grass profile: base and tip colour, width/height ranges, bend, tilt, density, wind response, and TerrainLayer-to-density mapping.
4. Optional albedo, normal, or vein texture. No bespoke grass-blade mesh is required.

### Density convention

Use Terrain splat weights first. An optional RGBA control texture handles art-directed exceptions:

| Channel | Initial use |
|---|---|
| R | Extra density/removal |
| G | Height scale |
| B | Dry-green tint or species |
| A | Wind attenuation; later interaction mask |

A grass/soil layer adds density while rock and path layers subtract it. This retains the existing terrain-painting workflow.

### Blade gradient

The shader derives `bladeT` from 0 at the root to 1 at the tip. It samples a 1D gradient lookup, or blends base and tip colours. This is independent from the terrain density map. Vein/edge detail and random hue are later material refinements.

## Delivery phases

### Phase 0 - Baseline and targets

1. Select the benchmark scene/Terrain, target resolution, target hardware tier, camera use cases, and frame budget.
2. Capture Profiler and Frame Debugger baseline: CPU/GPU frame time, draw calls, triangles, overdraw, shadow cost, and memory.
3. Define repeatable test routes: close ground, broad vista, rapid camera rotation, terrain edge, painted path/rock boundary, and dense field.
4. Decide shadow behaviour by quality level.

**Exit:** written budget and reproducible benchmark route.

### Phase 1 - GPU vertical slice

1. Add a `GpuGrassRenderer` component for one Unity Terrain and its GPU resources.
2. Build chunk grid and debug views for chunk state, candidate count, survivor count, and LOD.
3. Compute pass samples heightmap/alphamap, applies deterministic jitter, aligns roots to terrain normals, and appends valid near instances.
4. Copy append count to indirect args and draw one topology mesh with a flat debug material.
5. Add distance and frustum rejection. Confirm the indirect count changes without CPU readback.
6. Safely rebuild/release buffers on disable, destroy, resize, and Terrain reassignment.

**Exit:** grass appears only on a chosen TerrainLayer, follows terrain, culls by range, and uses no blade GameObjects.

### Phase 2 - Blade form and LOD

1. Add near topology of roughly 15 vertices and mid topology of roughly 7 vertices.
2. Use a cubic Bezier centreline in the vertex shader; derive a stable tangent/normal basis from its derivative.
3. Add seeded width, height, facing, tilt, bend, and clump variation.
4. Partition near/mid survivors and dither LOD transitions while reducing density and segment count.
5. Blend far distance into terrain material detail instead of drawing tiny blades.

**Exit:** no evident LOD ring or blade-count popping while walking or turning through the benchmark route.

### Phase 3 - Material and wind

1. Add base-to-tip gradient, hue/value variation, alpha clipping, and optional vein/edge detail.
2. Add world-space wind that offsets Bezier control points increasingly toward the tip.
3. Compare flat and rounded normals; add clump-normal/specular stabilization only if it reduces distant shimmer.
4. Match URP terrain lighting and shadow receiving behaviour.
5. Profile every extra texture, translucency, and AO feature.

**Exit:** close grass reads well, mid field is stable, and target camera angles show no objectionable alpha artifacts.

### Phase 4 - Performance hardening

1. Measure candidate generation, compaction, indirect submission, vertex work, fragment/overdraw work, and shadows separately.
2. Tune chunk size, active radius, density, LOD distances, and dither range from data.
3. Add conservative per-chunk frustum rejection before candidate work.
4. Add Hi-Z/depth-pyramid occlusion only when profiling shows terrain/structures hide enough grass to justify it; it is not a default feature for open fields.
5. Define Low/Medium/High profiles for density, draw distance, blade segments, wind, and shadows.
6. Validate target GPUs and fallback handling.

**Exit:** each quality profile meets its approved budget without culling errors, stalls, or buffer leaks.

### Phase 5 - Separate production approvals

- Multiple species and TerrainLayer profile mapping.
- Player/vehicle bending via a low-resolution interaction RenderTexture.
- Painted placement masks and runtime painting.
- Mesh, cliff, and slope-surface adapter.
- Custom shadow/impostor approach.
- Motion vectors/temporal-AA support after URP-specific verification.
- Seasons, wetness, snow, burning, and gameplay state.

## First-release non-goals

- Individual colliders, Rigidbody physics, or Transform components per blade.
- Unity Terrain Detail prototypes as this renderer.
- Geometry-shader or tessellation dependency.
- Hi-Z occlusion, motion vectors, and specialized shadow impostors before measured need.
- Untested support across every graphics API.

## Validation checklist

### Correctness

- Obey TerrainLayer painting, holes, height, and normals.
- No seams, duplicates, or density shifts across chunk boundaries.
- No stale indirect instances after camera/quality changes.
- No CPU readback in the per-frame render path.
- Safe allocation/disposal and Terrain reloads in editor and player.

### Visual

- Roots stay anchored while wind moves tips.
- LOD/fade stays stable in motion.
- No repeating clumps, excessive darkening, or specular shimmer.
- Alpha clipping, shadows, and far fade avoid a full-screen overdraw wall.

### Performance

- Record active chunks, candidates tested, survivors by LOD, indirect count, GPU dispatch time, vertex count, GPU frame time, and shadow-map time.
- Compare with the current grass/billboard solution on the same route and visual density.
- Treat GPU compaction as a measured hypothesis, not a blanket guarantee: after it removes invisible blades, visible alpha-clipped grass and shadows can dominate.

## Decisions needed before implementation

1. Which scene and Unity Terrain are the first benchmark target?
2. Desktop-only target, or a lower-spec GPU tier as well?
3. Which TerrainLayers mean grass, path, rock, mud, and no-growth?
4. Is first release wind-only, or must it include shadows and/or interaction?
5. Desired visual target: lush stylized grass, short turf, reeds, or several species?

## Research notes and sources

Official Unity 6 documentation checked on 2026-09-18:

- [Graphics.RenderMeshIndirect](https://docs.unity3d.com/ScriptReference/Graphics.RenderMeshIndirect.html) - GPU indirect rendering API with `GraphicsBuffer` commands and compute-shader requirement.
- [TerrainData](https://docs.unity3d.com/ScriptReference/TerrainData.html), [alphamapTextures](https://docs.unity3d.com/ScriptReference/TerrainData-alphamapTextures.html), and [heightmapTexture](https://docs.unity3d.com/ScriptReference/TerrainData-heightmapTexture.html) - native terrain data sources for height, layer weights, and holes.
- [ComputeShader.Dispatch](https://docs.unity3d.com/ScriptReference/ComputeShader.Dispatch.html), [ComputeShader.SetBuffer](https://docs.unity3d.com/ScriptReference/ComputeShader.SetBuffer.html), and [ComputeBuffer](https://docs.unity3d.com/ScriptReference/ComputeBuffer.html) - compute dispatch, GPU buffers, append counters, and `CopyCount`.
- [SystemInfo.supportsComputeShaders](https://docs.unity3d.com/ScriptReference/SystemInfo-supportsComputeShaders.html) - runtime capability guard.
- Source design reference: the provided *Procedural Grass in Ghost of Tsushima* PDF, especially its compute-driven placement/culling, compact blade attributes, Bezier construction, LOD, and far-field strategy.

## First implementation checkpoint

When implementation is authorized, start with **Phase 0 and Phase 1 only**. Do not add wind, shadows, interaction, Hi-Z occlusion, or multi-species work until the vertical slice is profiled and approved.
