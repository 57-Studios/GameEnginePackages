# Deformable Snow — Implementation Plan

This is the original phased plan, including the proposed displacement mesh and volume scheduling. The current implementation uses a screen-space layer with scene-local deformation volumes and a character prefab setup tool. See the [setup guide](guide.html) for current workflows, the [development log](DEVELOPMENT.md) for implementation and validation, and [RESEARCH.md](RESEARCH.md) for the technique study.

## Goal

Add a 57Studios snow volume that records where geometry presses into a snow-covered surface, keeps that history over time, and shades the result as displaced snow. It must work on ordinary scene meshes, not only Unity Terrain, and must not use raycasts or per-footprint meshes.

## Project decisions

- Unity `6000.4.0f1`, URP `17.4.0`, desktop-first.
- Surface is **Unity Terrain** first; the same path must accept ordinary meshes.
- Visual target is **packed snow / slush**.
- Displacement is **real geometry**: a generated snow mesh whose vertices sample the heightfield in the vertex stage, with a matching shadow-caster pass. URP has no hull/domain tessellation stage, so density comes from mesh resolution, not hardware tessellation.
- Relief mapping is retained only as the far path beyond the displacement patch, where per-vertex detail would be wasted.
- Capture and accumulation run on the GPU through a URP `ScriptableRendererFeature`. No per-frame readback.
- One heightfield per snow volume, pooled by resolution. Volumes are bounded and prioritised, never all-updated-every-frame.
- Runtime assembly `Studios57.DeformableSnow.Runtime`, editor assembly `Studios57.DeformableSnow.Editor`, tests in `Studios57.DeformableSnow.Editor.Tests`.

## Architecture

```text
Deforming renderers (layer mask)
              |
              v
Capture pass: ortho slab from below the surface,
white override material -> coverage/depth RT
              |
              v
Compute: 4-tap blur -> additive accumulate -> snowfall subtraction
              |
              v
Persistent heightfield RT (pooled, per volume)
     |            |                    |
     v            v                    v
Displaced       Slush material      Relief shader
snow mesh       (wet/dark/flat      (far path beyond
(vertex stage)   transition)         the patch)
     |
     v
Matching shadow-caster pass (same heightfield, same UV transform)
```

### Displacement tier

Real displacement is required, so the geometry plan is explicit:

| Concern | Approach |
|---|---|
| Mesh source | Generated grid in the volume's XZ frame, built once at the volume's resolution; never rebuilt per frame. |
| Vertex displacement | Heightfield sampled in the vertex stage with the volume's 0–1 surface UVs; amplitude from the volume's snow depth. |
| Density | 4 cm quads, denser than the 3.9 cm heightfield texel so prints stay sharp. Decoupled from texture size. |
| Extent | 16 m × 16 m high-density patch at 4 cm quads (≈320 k triangles including the shadow pass), anchored where the camera is. Relief shading covers 16–40 m. Phase 2 builds it at a fixed test spot; Phase 3 makes it follow the player. |
| Shadows | A shadow-caster pass using the same displacement. Footprint self-shadowing needs a dedicated local shadow map or a tight final cascade; default 150 m cascades cannot resolve a 4 cm dip. |
| Depth | The displaced mesh must write depth so the terrain is correctly occluded and the asset composites with transparents. |
| Amplitude vs. texel | A slush trail must be thicker than one heightfield texel or it disappears. This bounds volume extent for a given texture size. |

### Volume model

| Field | Purpose |
|---|---|
| Bounds | Box or mesh-bounds surface; defines capture extent and UV projection |
| Up axis / normal | Capture direction and height interpretation |
| World size | Drives texel density and relief scale |
| Snow thickness | Maximum displacement amplitude in world units |
| Resolution | Capped per axis; square texels; pooled |
| Deformer layers | Which renderers may write |
| Refill rate | Height subtracted per second |
| Priority | Distance/visibility ordering |

UVs are surface-aligned 0–1 across the capture frustum. Height sampling never tiles; detail normals may.

### Terrain adoption

Unity Terrain draws through a generated LOD mesh and a `TerrainLayer` material that cannot be replaced per volume, and the heightmap is far too coarse for footprints (1.95 m per texel at the benchmark resolution). Snow therefore never writes into `TerrainData`.

- A `DeformableSnowVolume` placed over terrain defines a world XZ square that supplies both capture bounds and snow UVs; the terrain's own height is sampled only to keep the relief origin on the surface.
- The snow pass is contributed by the renderer feature, not by editing terrain materials, so the same code path works on terrain and on ordinary meshes.
- A terrain-replacement material variant is a later option for surfaces where snow must fully occlude the ground; it is not part of v1.
- Depth is taken from the existing depth texture to reject snow behind geometry and to keep insertion before transparents.

### Capture pass

1. Select volumes that need an update (player proximity, visibility, dirty flag).
2. For each, bind its ping-pong texture pair.
3. Render deforming renderers with a full-white unlit material through an orthographic view aligned to the volume.
4. Run blur, then additive accumulate against the persistent heightfield, then subtract `refillRate * deltaTime`.

Rendering the slab from below, as in the talk, produces a geometry-intersection mask rather than a projected footprint. This is what makes capes, props, and ragdolls deform correctly.

### Shading

- Two-stage material: wet slush on top and the compressed/flattened underlying surface below, blended by height. Slush is darker, glossier, and lower-roughness than powder, so the same heightfield reads with less amplitude.
- Slush uses a smooth dielectric specular response with a wetness mask; disturbed slush near a print is wetter and more reflective than untouched slush.
- Sky-colour tint and a thin-crust band at the transition edge, as in the talk, to avoid a hard cut between the two stages.
- Normals: Sobel from the heightfield for the relief path, plus the vertex-stage geometric normal on the displaced patch. Combine with macro and detail normals using Reoriented Normal Mapping.
- The displaced patch uses the geometric normal directly; no relief march is needed where geometry already carries the shape.
- Far path (outside the patch): relief mapping in tangent space with surface-aligned UVs, step count scaled by view angle.
- Cheap path for slush that has never been deformed.

### Data and ownership

- Heightfields are `RFloat` or `RHalf`, clamp-wrapped, no mips.
- Buffers and textures are owned by the volume, released on disable/destroy, and safely rebuilt after resolution or bounds changes.
- Persistence, if required, uses `AsyncGPUReadback` at checkpoints only.

## Delivery phases

### Phase 0 — Target and baseline

Status: **baseline captured 2026-11-19; scope confirmed same day** (packed slush, real displacement, ≤ 1.0 ms / 1024² / 40 m).

1. ~~Choose the first snow scene and surface type.~~ **Decided:** Unity Terrain, `/Terrain` in `Assets/NatureManufacture Assets/River Auto Material/Demo/URP RAM 2019 Demo Mobile.unity`. It is the same surface used by the existing `57Studios/ProceduralGrass` benchmark, so snow can be compared directly against that feature.
2. Visual target: proposed fresh powder over grass; awaiting sign-off.
3. Hardware recorded: RTX 3060 Laptop 6 GB, i7-11800H, 16 GB, Windows 10, 1920 × 1080, URP 17.4.0, Unity 6000.4.0f1.
4. ~~Capture a baseline.~~ **Done.** Terrain-only route medians: 57 draw calls / 115,815 triangles / 1.04 ms GPU at the ground preset, and 51 / 131,639 / 1.06 ms at the vista preset. Full numbers, camera presets, and screenshots are in [DEVELOPMENT.md](DEVELOPMENT.md#2026-11-19-phase-0-baseline-entry).
5. Silhouette and shadow response: proposed as later tiers, not v1.

Budget to confirm: ≤ +1.0 ms GPU and ≤ 8 MB transient GPU memory per 40 m volume at 1080p on the reference laptop.

**Exit:** a repeatable scene, camera route, written budget, and an explicit v1 scope.

### Phase 1 — Heightfield core and debug views

Status: **complete 2026-11-19.** Exit criteria met: a scripted two-footed rig leaves a persistent trail that refills, with no raycasts, no per-foot objects, and no per-frame readback. Measured cost 0.07–0.10 ms GPU, 11 MB at 1024². Full numbers and screenshots in [DEVELOPMENT.md](DEVELOPMENT.md#2026-11-19-phase-1-entry--heightfield-core-and-debug-views).

1. ~~Add `DeformableSnowVolume` with bounds, resolution, thickness, layer mask, and refill rate.~~ **Done**, with an explicit deformer registry instead of a layer mask.
2. ~~Implement capture through a URP renderer feature with a white override material.~~ **Done differently:** capture runs through an explicit `CommandBuffer` + `Graphics.ExecuteCommandBuffer` with a flat mask shader. No URP renderer asset is touched and there is no RenderGraph coupling. Revisit only if Phase 2 needs the snow inside the camera's render graph.
3. ~~Implement blur, additive accumulate, and refill in compute.~~ **Done.** Blur is separable (5 taps × 2 passes); accumulate is contact × gain × dt, then refill × dt, so it is frame-rate independent.
4. ~~Add editor debug views: raw capture, accumulated height, derived normal, and per-volume texture allocation.~~ **Done** as a runtime IMGUI overlay (F1/F2) rather than an editor window, so it also works in Play Mode and builds. Texture allocation is documented instead of displayed; add a window in Phase 5.
5. ~~Add pooling and safe allocation/release.~~ **Done** for one volume: resources are created on first use, released on disable/destroy, and rebuilt if the resolution changes. Multi-volume pooling is Phase 3.

**Exit:** a character walking across a flat test surface leaves a persistent, deepening trail that refills. No raycasts, no per-foot objects, no per-frame readback. ✅

### Phase 2 — Slush displacement and shading

Status: **in progress 2026-11-19.** Steps 1–4 are working in Play Mode: the displaced patch samples the heightfield in the vertex stage, the shadow-caster and depth passes share the same displacement and silhouette rule, and slush shading plus a generated detail normal are in. Step 6 (far relief path) and acceptance measurement are outstanding. See [DEVELOPMENT.md](DEVELOPMENT.md#2026-11-19-phase-2-entry--slush-surface-detail-texture-broken-patch-edge).

1. Implement the compressed/flattened blend driven by height.
2. Generate the snow mesh over the volume (or patch) once; displace vertices in the vertex stage from the heightfield.
3. Write a matching shadow-caster pass that uses the same displacement and UV transform.
4. Add slush shading: wet specular response, wetness mask, sky-tint transition band, and the cheap never-deformed path.
5. Derive normals from the heightfield with Sobel and combine with macro and detail normals via RNM on the far path only.
6. Implement the far relief path outside the displaced patch, with surface-aligned UVs and angle-scaled steps.
7. Validate at 5–15° grazing angles for swimming and normal stretching, and at profile angles for silhouette.

**Exit:** trails read as displaced slush at normal gameplay distances, prints cast a shadow where the shadow setup can resolve them, the flattened stage reveals the terrain plausibly, no relief swimming on the benchmark route, and the pass stays inside the approved budget.

### Phase 3 — Scheduling and open-world behaviour

1. Add distance/visibility priority with a configurable per-frame volume budget.
2. Add quadtree or spatial-hash lookup so only deforming renderers near a volume are considered.
3. Add distance-based texture resolution and pooling reuse for unloaded volumes.
4. Move or rebuild the displacement patch as the player travels, without hitching.
5. Verify stability with many volumes, streaming, and editor domain reloads.

**Exit:** 20+ volumes in range stay within budget; only prioritised volumes update; the patch follows the player without a visible rebuild; memory does not grow across load/unload cycles.

### Phase 4 — Shadow fidelity tier (profiling-gated)

1. Evaluate a dedicated local shadow map for footprint self-shadowing against the cost of tightening the final cascade.
2. Add adaptive mesh density if a single density cannot satisfy both the patch and its shadow pass.
3. Compare cost and visual gain against the relief baseline on the same route.
4. Abandon any sub-feature that does not justify its cost.

**Exit:** a documented decision, with measurements, on the shadow and density setup that ships.

### Phase 5 — Authoring, persistence, and polish

- Snow volume authoring window and scene gizmos. **Window done 2026-11-19** — `57Studios → Deformable Snow Setup` with Setup / Appearance / Validate tabs; gizmos still open.
- Baked base height for static props and drifts.
- Optional persistence across saves via checkpoint readback.
- Footstep audio/debris hooks and gameplay query API.
- Generalisation to sand, mud, or ash only if reuse is proven.

## Non-goals for v1

- Per-frame raycasts or physics probes against the snow surface.
- Per-footprint decal GameObjects.
- CPU heightfield readback in the update loop.
- Hardware tessellation (URP has no hull/domain stage) or modifying `TerrainData` heightmap/alphamap.
- Runtime terrain-mesh vertex modification; displacement is an overlay mesh, not a terrain edit.
- Network replication of deformation.

## Validation checklist

### Correctness

- Footprints align with contact points on sloped and uneven surfaces.
- Displaced mesh and shadow-caster pass produce identical silhouettes for the same frame.
- Volume bounds changes and resolution changes rebuild cleanly.
- No texture or buffer leaks after disable, destroy, scene unload, or domain reload.
- Refill and accumulation are frame-rate independent.
- Moving the patch does not teleport or reseed deformation.

### Visual

- Deformation reads at gameplay camera height and at grazing angles.
- Prints read in silhouette at profile angles, not only in shading.
- No swimming, smearing, or stretched normals along view direction.
- Slush/compressed transition does not pop, and the wetness response does not shimmer.
- Detail and macro normals combine without flattening or halos.
- The far relief path and the near displaced patch match at their boundary.

### Performance

- Record capture draw, blur, accumulate, snow fragment cost, and memory per volume.
- Confirm the per-frame volume budget holds with the player volume prioritised.
- Compare against an undeformed snow baseline on the same camera route.

## First checkpoint

Build Phase 1 only: one volume, one deforming character, capture, accumulate, refill, and debug views. Do not start displacement or slush shading until the heightfield behaviour is proven.

Phase 2 then adds the displaced mesh and its shadow pass together, as one unit. Never ship the mesh without the matching shadow pass, or the feature will light and not shadow.
