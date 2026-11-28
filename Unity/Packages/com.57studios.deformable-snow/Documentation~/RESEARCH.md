# Deformable Snow — Research Notes

Research date: 2026-11-19
Primary source: Colin Barré-Brisebois (Lead Rendering Programmer, WB Games Montreal), *Deformable Snow Rendering in Batman: Arkham Origins*, GDC 2014, Programming track.
Companion source: Colin Barré-Brisebois, *DirectX 11 Rendering and NVIDIA GameWorks in Batman: Arkham Origins*, GTC 2014.

The GDC Vault session page for `1020177` publishes only the abstract; the video is members-only. The technical findings below were taken from the author's published 43-slide GDC deck and the 66-slide GTC edition, which repeats Part 1 verbatim and adds the NVIDIA tessellation detail. Statements marked **Inference** are our translation to this project, not claims from the talk.

---

## 1. What problem the talk solves

Three stated requirements:

1. Iconic visuals of deformable snow.
2. Organic deformation from walking, falling, sliding, fighting, and similar actions.
3. Low memory and low performance cost for an open world.

Arkham Origins has no heightmap terrain for most playable space. It has rooftops and streets — arbitrary meshes — so the console-era recipe of raycasting a terrain and displacing terrain vertices did not apply. The team also explicitly did not want to add raycasts. Prior art cited:

| Prior work | Why it did not fit |
|---|---|
| Assassin's Creed 3 (St-Amour 2013) — raycast terrain, modify terrain mesh | No terrain; raycast cost; needs variable triangle density |
| Journey (Edwards 2012) — modify sand mesh | Same density requirement; mesh-authoring cost |

The density requirement is the crux: convincing vertex displacement needs triangles finer than the footprint. That is expensive everywhere and impossible on consoles without tessellation.

### Scope boundaries

The talk covers the **surface** system only: recording deformation and shading the snow. Falling snow, blizzard turbulence, and airborne particles were separate NVIDIA GameWorks work in the shipped game, listed in Part 3 of the GTC deck. A snow tool based on this technique should therefore own accumulation, trails, shading, and refill, and treat precipitation particles as an independent system.

## 2. The core idea

Treat snow deformation as a **semi-low-frequency detail effect** and implement it as a **runtime-generated displacement heightmap**, not as simulation and not as per-footprint meshes.

> "Snow prints are a semi-low frequency detail effect. Cheap approximation works with footsteps & more. Great performance, and low memory usage."

The same heightfield feeds two different renderers:

| Platform | Path | Displacement |
|---|---|---|
| PS3 / 360 / Wii U | Virtual displacement via **relief mapping** | Fragment-shader parallax; independent of triangle density; minimal taps; "no swimming" |
| PC (DX11) | **Tessellation** plus runtime dicing, developed with NVIDIA (Evgeny Makarov) | Real geometry; thicker deformation; correct dynamic shadows and AO |

Both paths share the capture and accumulation pipeline. Only the final displacement method differs.

## 3. Heightfield generation

### 3.1 Capture

Render every snow-affecting object **looking from under the surface through an ankle-high orthogonal frustum**:

1. Clear to black.
2. Render actors in white with a simple override material.
3. Filter and accumulate into a persistent texture (ping-pong).

Anything whose geometry enters that thin slab writes to the heightmap: feet, hands, a cape dragging, a body sliding, a thug thrown to the ground. This is why the talk calls the deformation "arbitrary" — it is real geometry intersection, not a capsule or a projected footprint decal. Silhouette accuracy comes for free from the renderer.

### 3.2 Update loop, per active snow surface

1. Determine whether any surface-affecting object is on the surface. A **quadtree lookup** is used instead of keeping an actor list per surface.
2. Override materials on all relevant parts with a flat white material.
3. Render the actors through the capture frustum.
4. Process and accumulate with a custom post-process chain.

### 3.3 Post-process chain

| Stage | Operation | Note |
|---|---|---|
| 1 | Get capture result, apply small blur | 4-tap bilinear Poisson |
| 2 | Add to existing heightmap | Optionally subtract a small constant so snow gradually replenishes while it is snowing |
| 3 | Shading | Deferred to the material |

The additive accumulation is what makes trails persist and deepen. The subtraction is the entire "snowfall" model: no particle simulation is required for the surface itself.

### 3.4 Resolution, memory, and scheduling

| Property | Stated value |
|---|---|
| Resolution | Scaled to surface world size, kept square, capped at 512 per axis |
| Stated formula | `Min(512, ¼ × (SurfaceX, SurfaceY))` |
| UV convention | Surface UVs align with the ortho frustum, 0–1 range, simplifying heightmap-to-displacement lookup |
| Memory, consoles | ~2 MB total |
| Memory, PC | 2–4 MB depending on FP16 vs FP32 |
| Update cost | Heightmap update < 1.0 ms GPU on PS3 / 360 |
| Surfaces updated | Only 2 surfaces per frame (tweakable), distance-based priorities; this is the capture/accumulate tick, not the shading of every visible surface |
| Priority | The surface the player stands on wins |
| Allocation | Dynamic alloc/dealloc by size, player/AI presence, and visibility; reuse memory from old heightmaps in unstreamed zones |

The resolution is deliberately low. The talk notes it "doesn't need to be high-res, looks better in lower resolutions" and that relief-mapping parameters must be scaled to match.

## 4. Shading

Two materials:

1. **Basic Snow Material** — used when a surface is not being deformed. New, clean, untouched snow. Cheaper.
2. **Deformable Snow Material** — used when deformation exists. Blends two states:
   - *Non-deformed*: same appearance as basic snow.
   - *Fully flattened*: reveals the underlying rooftop tile or concrete.
   - The blend factor comes from the heightmap through relief mapping.

Blending rules:

- Diffuse and specular: simple `lerp`.
- Transition band: tint diffuse toward sky colour to fake subsurface scattering in the thin snow crust.
- Normals: **Reoriented Normal Mapping (RNM)**, not lerp and not overlay.

> "Normals are not colors. You can't lerp/overlay between directions!"

RNM as printed in the deck:

```hlsl
float3 t = tex2D(BaseNormal, uv)   * float3( 2,  2, 2) + float3(-1, -1,  0);
float3 u = tex2D(DetailNormal, uv) * float3(-2, -2, 2) + float3( 1,  1, -1);
float3 r = t * dot(t, u) / t.z - u;
```

RNM is used twice in the shipped material: to combine snow detail normals with the macro "wave" snow normal, and to layer detail normals everywhere. The macro normal is what gives snow its large soft undulation rather than a uniform white plane.

Relief mapping derives the deformation in the fragment shader. The talk's stated virtues are minimal texture taps, no "swimming" (unstable parallax convergence at grazing angles), and independence from triangle density. The stated weakness is thickness: relief-mapped deformation "will never be as thick as tessellation". Suggested upgrades were parallax occlusion mapping and parametric AO derived from the heightfield.

## 5. The DX11 tessellation path

This is a quality tier, not the baseline. Two differences from the console path:

### 5.1 Depth-based capture

Instead of a binary coverage mask, capture the heightfield **like a z-buffer** with two channels:

- **Minimum height field** — the lowest surface reached by geometry in the slab.
- **Projected displacement** — how far the actor projects down from the snow surface.

This allows additive capture with smoother results and supports **deformable snow banks**, not just footprints. It also improves slopes, listed later as a future direction.

### 5.2 Real geometry

Runtime dicing tessellates snow meshes and displaces them from the heightfield, plus an additional global macro displacement derived from a normal map ("macro waves", breaking surface uniformity). Because the result is real vertices:

- Dynamic shadows work. Character shadows follow the deformed surface and shift as it deforms.
- Self-shadowing and self-shading work.
- Dynamic AO fills in the trails.

### 5.3 Costs and caveats

- Content was authored before displacement existed: pre-pass actors, open mesh edges, and geometry that sits just under the snow can become visible.
- Some meshes could not be tessellated at all because hidden geometry sat directly beneath the snow.
- This is a DX11-only tier. It is not a portable baseline.

The deck is candid that the relief path is a visual approximation: it reads as deformation but does not change silhouette or shadow.

## 6. Reported limitations and future work (from the talk)

- Relief mapping cannot match tessellated thickness.
- POM and heightfield-derived AO were proposed but not shipped as part of this talk.
- Heightmap persistence across sessions was future work; the shipped system regenerates.
- The technique generalizes to sand and mud.
- Tessellated slopes needed more work.

## 7. Why the design works

These are the transferable lessons, independent of engine:

1. **Decouple interaction from geometry.** The expensive thing is not knowing where a foot is; it is turning that into mesh detail. A heightfield turns an unbounded set of interactions into a fixed-size texture update.
2. **Render the interaction instead of computing it.** Using the main renderer to capture geometry intersection avoids a parallel physics/probe system and automatically handles capes, props, ragdolls, and non-humanoid shapes.
3. **Exploit frequency.** Deformation is low frequency, so 512² is enough and the cost is bounded per surface rather than per actor.
4. **Budget by visibility.** Two surfaces per frame, player-proximity priority, and pooling keep an open world bounded.
5. **Design the fallback first.** The console path defined the feature; tessellation was an enhancement. A project without tessellation can still ship the idea.
6. **Accumulate, then decay.** Additive blending plus a small subtraction produces history, depth, and refill with two texture operations.

## 8. Translating to Unity 6.0.4 / URP 17.4 in this project

**Project facts:** Unity `6000.4.0f1`, URP `17.4.0`, desktop-first, compute shaders available and already used by `Studios57.ProceduralGrass`.

### 8.1 The tessellation path does not port directly

URP has no hull/domain (tessellation) shader stage, and Unity 6 Shader Graph has no tessellation output. The DX11 tier therefore has no one-to-one equivalent. Options, in order of likely value:

| Option | What it gives | Cost / risk |
|---|---|---|
| **Parallax / relief mapping** (port of the console path) | Deformation reads correctly at normal viewing distances; no mesh changes; works on any mesh | No silhouette change, no shadow response, grazing-angle artifacts if steps are too few |
| **Dense procedural snow mesh** + vertex displacement | Real silhouette, shadows, self-shadowing | Geometry cost; needs its own mesh/quadtree; not applicable to arbitrary existing meshes |
| **Compute-displaced chunks drawn indirectly** | Real geometry without per-vertex CPU work; fits the project's existing `Graphics.RenderMeshIndirect` pattern | Significant engineering; normals and bounds must be rebuilt; still only for owned snow meshes |
| **Screen-space snow decals** | Cheap, many surfaces, no per-surface RT | Breaks on steep angles, no per-surface history, sorting and depth issues |

**Recommendation:** ship the relief path as the baseline, exactly as the talk did, and treat real displacement as a later, profiling-gated tier. Do not promise tessellation parity.

> **Updated 2026-11-19 (project decision):** the user requires real displacement in v1, so this recommendation was overridden. The shipped plan is a 16 m × 16 m displaced mesh at 4 cm quads with a matching shadow-caster pass, and relief mapping is demoted to the 16–40 m far path. The option table above still describes the trade-offs correctly; the "dense procedural snow mesh" row is now the primary path. See [ImplementationPlan.md](ImplementationPlan.md#displacement-tier).

### 8.2 Capture pipeline mapping

| Talk concept | Unity 6 / URP equivalent |
|---|---|
| Ankle-high orthogonal frustum | `Matrix4x4.Ortho` × view matrix, submitted with `CommandBuffer.SetViewProjectionMatrices`; near/far bound the slab |
| Override actors with white material | `CommandBuffer.DrawRenderer(renderer, whiteMaterial, submesh, pass)` with an unlit full-white shader |
| Clear to black | `CommandBuffer.ClearRenderTarget` or `SetRenderTarget` with a black clear |
| Ping-pong heightmap | Two `RenderTexture`s in `RFloat` or `RHalf`, `FilterMode.Bilinear`, clamp wrap, no mips |
| 4-tap Poisson blur + accumulate | A compute kernel is preferred over `Graphics.Blit`; this project already uses compute |
| Quadtree lookup | A spatial hash or quadtree over deforming renderers; do not iterate all scene objects per surface |
| 2 surfaces per frame | A priority queue keyed by player distance and visibility |
| Dynamic alloc/dealloc | A pooled `RenderTexture` cache keyed by resolution |

**Inference — implementation notes:**

- A `ScriptableRendererFeature` / `ScriptableRenderPass` is the URP-native home for the capture pass. The existing grass renderer instead hooks `RenderPipelineManager.beginCameraRendering`; either is viable, but a renderer feature avoids per-camera main-camera assumptions.
- Prefer `RFloat` for the heightfield if memory allows; `RHalf` is acceptable at these amplitudes and halves memory. The talk shipped FP16 on PC.
- If a depth-based capture is added later, remember Unity's reversed-Z platform difference (`SystemInfo.usesReversedZBuffer`); store a normalized height or projected displacement rather than raw clip depth.
- Do not read the heightfield back per frame. Use `AsyncGPUReadback` only for save/checkpoint.

### 8.3 Height, normals, and relief in a URP shader

- Sample height with point or bilinear filtering depending on whether crisp footprint edges or smooth banks are wanted.
- Derive normals with central differences / Sobel scaled by texel world size, then combine with the macro and detail normals via RNM.
- Implement relief mapping as a tangent-space ray march. Use surface-aligned 0–1 UVs for the height lookup — tiling world UVs are the usual cause of "swimming" and of footprints that do not line up with the capture.
- Keep detail normals on a separate, tiled UV set. The talk uses the same RNM path for both, but the height lookup must not tile.

### 8.4 Project-specific constraints

- Snow volumes must not modify third-party demo scenes or content packs. Use isolated sample scenes and `57Studios`-owned prefabs.
- URP renderer-feature registration touches `Assets/Settings/PC_Renderer.asset`; that is a shared project asset, so any change needs explicit approval and a note in `DEVELOPMENT.md`.
- The project's benchmark surfaces differ from Arkham's rooftops: most candidate scenes are terrain-based. Terrain can be treated as one more arbitrary surface, but its own heightmap and splat data must not be confused with the snow heightfield.
- Existing grass work establishes the house pattern for GPU buffer ownership, indirect draws, and deterministic seeds; reuse it rather than inventing a second renderer architecture.

## 9. Comparison of approaches (for this project)

| Approach | Arbitrary meshes | History / trails | Thickness / silhouette | Shadows follow | Memory | Complexity |
|---|---|---|---|---|---|---|
| Heightfield + relief (talk's console path) | Yes | Yes | No | No | Low | Medium |
| Heightfield + tessellation (talk's PC path) | Yes | Yes | Yes | Yes | Low | High, and unavailable in URP |
| Heightfield + owned dense mesh | No | Yes | Yes | Yes | Low–medium | Medium–high |
| Per-foot decal pool | Yes | Partial | No | No | Very low | Low |
| Raycast + terrain vertex edit | Terrain only | Yes | Yes | Yes | Low | Medium, but needs density |
| Screen-space deformation | Yes | Limited | No | No | Low | Medium |

The talk's differentiator is the second column: arbitrary mesh geometry, arbitrary shapes, persistent history, at texture cost. That is the property worth preserving.

## 10. Validation experiments worth running early

1. **Capture fidelity** — feet, cape, and a ragdoll through the slab produce expected silhouettes at 256², 512², and 1024².
2. **Persistence and refill** — a trail remains legible after 30 s and disappears on the configured snow-refill timescale.
3. **Grazing-angle stability** — walk a camera along the surface at 5–15°; check for relief "swimming" and stretched normals.
4. **Resolution/size scaling** — square texels across a 4 m ledge and a 40 m street; verify footprint scale is consistent.
5. **Priority scheduling** — with 20 volumes in range, confirm only the configured number update per frame and the player's volume wins.
6. **Cost isolation** — measure capture draw, blur, accumulate, and snow shading separately; the original quote of <1 ms covers update only.
7. **Shadow/AO honesty** — confirm the relief path does not pretend to occlude; if the dense-mesh tier is built, verify shadow and AO response.
8. **Save/load** — checkpoint via `AsyncGPUReadback` without a frame hitch.

## 11. Open questions

1. Primary target: rooftops/streets (original scope), terrain snow, or both?
2. Is silhouette and shadow response required at launch, or is relief-mapped shading acceptable for v1?
3. Which layers and object types may write to the heightfield, and should static props be baked into the base height?
4. Maximum simultaneous volumes and the per-frame update budget.
5. Does snow need gameplay effects (slowed movement, footstep audio, thermal trails), or is this purely visual?
6. Persistence expectations: within a session, across saves, or not at all?

## 12. References

- Colin Barré-Brisebois, *Deformable Snow Rendering in Batman: Arkham Origins*, GDC 2014 — <https://gdcvault.com/play/1020177/Deformable-Snow-Rendering-in-Batman>
- Author's published GDC 2014 deck — <https://www.slideshare.net/colinbb/gdc2014-deformable-snow-rendering-in-batman-arkham-origins>
- Colin Barré-Brisebois, *DirectX 11 Rendering and NVIDIA GameWorks in Batman: Arkham Origins*, GTC 2014 — <https://www.slideshare.net/colinbb/gtc-2014-directx-11-rendering-and-nvidia-gameworks-in-batman-arkham-origins>
- Barré-Brisebois & Hill, *Blending in Detail — Reoriented Normal Mapping*, 2012.
- Policarpo & Oliveira, *Rendering Surface Details in Games with Relief Mapping Using a Minimally Invasive Approach*, Shader X4, 2006.
- Edwards, *Sand Rendering in Journey*, SIGGRAPH 2012 Advances in Real-Time Rendering.
- St-Amour, *Rendering Assassin's Creed*, GDC 2013.
- Unity, `Graphics.RenderMeshIndirect` — <https://docs.unity3d.com/ScriptReference/Graphics.RenderMeshIndirect.html>
- Unity, URP `ScriptableRendererFeature` — <https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.4/manual/renderer-features/how-to-custom-effect-render-objects.html>
