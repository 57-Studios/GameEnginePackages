# 57Studios DeformableSnow — Development Record

## Ownership

`com.57studios.deformable-snow` is a 57Studios rendering feature, released under the MIT license (see `LICENSE` in the addon root). Keep all feature-owned scripts, shaders, compute kernels, materials, editor tooling, test scenes, and documentation below this folder. Do not place production feature code in third-party asset folders and do not modify third-party packages to make the feature work.

## Purpose

Render snow-covered surfaces that record and preserve deformation from characters, props, and debris. The surface source is arbitrary scene geometry, optionally including Unity Terrain. The complete architecture and delivery phases are in [ImplementationPlan.md](ImplementationPlan.md); the technique study and Unity translation are in [RESEARCH.md](RESEARCH.md).

This file is the living handoff record. Update it whenever a decision, measurement, feature, limitation, or benchmark changes.

## Current status

| Item | Status |
|---|---|
| Feature folder | Established |
| Research | Complete — GDC 2014 and GTC 2014 decks reviewed |
| Implementation | **Phase 2 in progress** — displaced patch, slush shading, detail normals and broken patch edge working in Play Mode. Setup now ships as an editor window. |
| Active phase | **Phase 2** — trail readability at gameplay distance, then fragment cost |
| Setup | **57Studios → Deformable Snow Setup** (`Editor/DeformableSnowSetupWindow.cs`), replaces the Phase1/Phase2 CLI sequence |
| Render pipeline | URP 17.3.0 (`Mobile_RPAsset`) |
| Unity version | 6000.3.14f1 |
| First surface | **Unity Terrain** (`/Terrain`, 500 × 500 m) — confirmed by user |
| Visual target | **Packed snow / slush** over grass — confirmed by user |
| Displacement | **Real displaced mesh required** — relief mapping demoted to the far/LOD path |
| Tessellation tier | URP has no hull/domain stage; displacement comes from a generated mesh, not hardware tessellation |
| Benchmark scene | `Assets/NatureManufacture Assets/River Auto Material/Demo/URP RAM 2019 Demo Mobile.unity` |
| Test device | RTX 3060 Laptop 6 GB, i7-11800H, 16 GB, Windows 10, 1920 × 1080 |
| Frame-time budget | Approved: ≤ +1.0 ms GPU at 1080p, 1024² heightfield — must be re-validated for the displaced tier |

## 2026-11-19 research entry

- Created `Assets/57Studios/DeformableSnow/` with `README.md`, `Documentation/RESEARCH.md`, `Documentation/ImplementationPlan.md`, and this record.
- Scaffolded `Runtime/`, `Editor/`, `Shaders/`, `Compute/`, and `Tests/` with assembly definitions matching the house convention used by `57Studios/ProceduralGrass` and `57Studios/TerrainScatter`.
- No runtime scripts, shaders, materials, prefabs, scenes, or project settings were added or modified. The assemblies are intentionally empty.
- Read the GDC Vault session page, the author's published GDC 2014 slide deck, and the GTC 2014 companion deck. The Vault video is members-only; the decks carry the technical content, including the capture pipeline, accumulation stages, resolution and memory figures, shading rules, and the DX11 tessellation tier.
- Key conclusion: URP has no hull/domain tessellation stage, so the talk's PC tier has no direct equivalent here. The console relief-mapping path was the initial baseline. *(Superseded on the same day: the user requires real displacement, so relief mapping is now the far path and a generated displaced mesh is primary. See the Phase 0 scope decisions below.)*
- No Unity import validation was performed because no assets requiring compilation were added. Unity was running when the folders were created.
- Next smallest safe task: complete Phase 0 — choose the first snow surface, visual target, and budget, then capture a pre-feature baseline.

## 2026-11-19 Phase 0 baseline entry

### 1. What changed

- Measurement only. No feature code, shader, material, asset, or project setting was added or modified by this phase.
- Helper scripts used for probing live through the Unity pipeline CLI live outside the deliverable, in `AgentScripts/Phase0Inspect.cs`, `Phase0Probe.cs`, `Phase0Scene.cs`, `Phase0Camera.cs`, `Phase0Toggle.cs`, `Phase0Isolate.cs`, and `Phase0Hardware.cs`.
- Screenshots written to `Documentation/`:
  - `Phase0_TerrainGround_Only.png`, `Phase0_TerrainGround_ScatterOff.png`, `Phase0_TerrainGround_ScatterOn.png`
  - `Phase0_TerrainVista_Only.png`, `Phase0_TerrainVista_ScatterOff.png`, `Phase0_TerrainVista_ScatterOn.png`

### 2. Chosen benchmark surface

The main scene terrain `/Terrain` in `URP RAM 2019 Demo Mobile`, the same scene already documented by `57Studios/ProceduralGrass`. It is a real gameplay-style surface: 500 × 500 m, 75 m tall, nine splat layers, 73 % grass coverage, no trees.

| Terrain fact | Value | Consequence for the feature |
|---|---|---|
| Heightmap resolution | 257 (≈1.95 m per texel) | Terrain cannot store a footprint. Snow needs its own heightfield. |
| Alphamap | 512 × 512, 9 layers (≈0.98 m per texel) | Too coarse to select snow vs. grass per footprint; per-volume painting is required. |
| Height range | 0.00–73.63 m | Avoid the 0 m basin (water) for snow tests. |
| Collider | `TerrainCollider` present | Contacts can be resolved without custom collision geometry. |
| Renderer | Terrain LOD system, 115 k–131 k triangles on route | Terrain already spends the triangle budget, so snow geometry must be a small bounded patch, not a full-coverage mesh. |

Camera presets (`AgentScripts/Phase0Camera.cs`, 1920 × 1080):

| Preset | Position | Look at | Ground slope | Subject |
|---|---|---|---|---|
| `Ground` | (110.00, 30.26, 190.00) | (170.00, 29.14, 200.00) | 4.9° | Eye-level 1.7 m over open grass, uphill trail direction |
| `Vista` | (60.00, 70.00, 60.00) | (260.00, 15.64, 260.00) | 24.2° | Wide view across the whole terrain, rivers and roads |
| `Slope` | (265.00, h+1.7, 380.00) | (240, h, 340) | — | Steep grass slope, for relief stretching checks (Phase 2) |

### 3. Measured baseline

1920 × 1080, quality Ultra, vSync on, AA off, shadow distance 150, editor Play Mode. Three samples per label; median in bold, GPU `0` samples ignored (timer unavailable).

| Preset | Configuration | Draw calls | Triangles | CPU frame (ms) | GPU frame (ms) |
|---|---|---|---|---|---|
| Ground | Terrain only | **57** | **115,815** | 8.22 / **8.23** / 8.98 | 0.97 / **1.04** / 1.24 |
| Ground | Scene, scatter off | **82** | **156,863** | 8.38 / **8.79** / 9.63 | **1.13** / 1.18 |
| Ground | Full scene, scatter on | **230** | **191,791** | 8.64 / **9.13** / 9.58 | **1.08** |
| Vista | Terrain only | **51** | **131,639** | 7.55 / **7.90** / 7.95 | 1.00 / **1.06** |
| Vista | Scene, scatter off | **79** | **174,819** | 8.89 / **9.04** / 12.43 | **1.20** |
| Vista | Full scene, scatter on | **275** | **221,075** | 8.13 / **8.75** / 8.93 | **1.25** |

Observations:

- Terrain alone is 51–57 draw calls and 115 k–131 k triangles. That is the fixed cost any snow layer must be added to, not replace.
- The existing `57Studios Scatter - Terrain` root costs 148 draw calls / 35 k triangles at the ground preset and 196 / 46 k at vista. It is the largest single contributor in this scene.
- GPU frame time for the whole scene is only 1.0–1.3 ms on the RTX 3060. The GPU is not the constraint at 1080p here; a 1 ms snow budget is generous, a 2 ms budget is still affordable.
- CPU frame time sits at a 7.5–9.6 ms editor floor and is not representative of a build. Do not use it to judge the feature; use it only for relative regression checks in the same session.
- Everything here is editor Play Mode with vSync on. Numbers are valid for regression comparison on this route, not as shipping frame budgets.

### 4. Validation completed

- Pipeline connection on port 7800, Unity 6000.4.0f1, `status=ready`, `set_autotick` enabled.
- Scene entered and exited Play Mode with no errors; console contains only the known Input Manager deprecation warning.
- Toggles were applied in Play Mode only. The scene was never saved; the open scene remains dirty with the user's pre-existing content and all 17 roots are active again after exit.
- Captures use `source=screen` so post-processing and overlay compositing are included.

### 5. Scope decisions taken after the baseline

User answers on 2026-11-19:

| Decision | Answer | Consequence |
|---|---|---|
| Visual target | Packed snow / slush | Wet, dark, low-amplitude layer with specular trails. Suits displacement better than powder, because slush amplitude is small and edges are sharp. |
| Silhouette / shadow | Real displacement required | The relief-only v1 is dropped. A generated, vertex-displaced snow mesh becomes the primary path and must appear in the depth and shadow-caster passes. |
| Budget and first volume | ≤ 1.0 ms GPU, 1024² over 40 m | Accepted as the budget. It is now a constraint to prove, not an assumption. |

**Recorded conflict.** Real displacement and the approved 1024² / 40 m volume cannot both hold at footprint scale:

- 1024² over 40 m is a 3.9 cm texel. A slush trail is 1–6 cm deep, so a single footprint spans only 2–6 heightfield texels. That is enough for shading, but a displaced mesh must be denser than the feature to displace it.
- Matching the heightfield with mesh quads at 4 cm means 1000 × 1000 quads ≈ 2 M triangles, plus a duplicate shadow-caster pass. That does not fit 1.0 ms on the reference laptop.
- Coarser mesh quads (10 cm) recover the budget but erase the print shape, which is the thing displacement was chosen for.
- Footprint self-shadowing is additionally bounded by shadow-map resolution, not snow depth. At the 150 m shadow distance and default cascade split, a shadow texel on the ground is far larger than a 4 cm dip, so per-print self-shadowing is invisible without a dedicated local shadow map or a tight final cascade.

Resolution taken: a 16 m × 16 m high-density displacement patch (4 cm quads, ≈320 k triangles including the shadow pass) anchored at the camera, with relief shading from 16–40 m. That keeps prints sharp where they are visible, keeps the approved 1024² / 40 m heightfield and 1.0 ms budget intact, and avoids the 1.3–2 M triangle cost of covering all 40 m with geometry. Phase 2 builds it at a fixed test spot; Phase 3 makes it follow the player.

### 6. Known limitations, risks, next smallest safe task

- Measurements come from a scene with unsaved user content. A clean, reproducible sample scene is still required before Phase 1 acceptance measurements.
- Terrain is drawn by Unity's LOD terrain renderer; its material cannot be swapped per-volume. Snow therefore renders as an overlay surface, not as a replacement terrain material, and it must be depth-tested against the shoved-down terrain.
- Real displacement requires a second, matching shadow-caster pass; a mismatch there produces prints that light but do not shadow, or vice versa.
- `.meta` GUIDs, generated terrain mesh, and terrain material are third-party. Do not edit them.
- Next smallest safe task: build Phase 1 (one volume, one character, capture → accumulate → refill, debug views) unchanged. Displacement is Phase 2 and is explicitly out of scope for Phase 1.

## 2026-11-19 Phase 1 entry — heightfield core and debug views

### 1. What changed

Runtime, all below `Assets/57Studios/DeformableSnow/`:

| File | Role |
|---|---|
| `Runtime/DeformableSnowVolume.cs` | Owns the volume bounds, capture target, blur/height targets, kernels, and `CommandBuffer`s. Runs capture → horizontal blur → vertical blur → accumulate/refill every `LateUpdate`. No readback. |
| `Runtime/DeformableSnowDeformer.cs` | Marks a hierarchy as allowed to press into snow. Registers every child `Renderer`; place it on feet or props, never on a whole character. |
| `Runtime/DeformableSnowSystem.cs` | Static registry of live volumes and deformers. Linear list — Phase 3 replaces it with a spatial lookup without changing callers. |
| `Runtime/DeformableSnowDebugView.cs` | IMGUI overlay. **F1** shows/hides, **F2** cycles height / raw capture / derived normal. |
| `Runtime/Testing/SnowTestWalker.cs` | Test harness: ping-pong patrol along a fixed line with a two-foot gait. Not gameplay code. |
| `Runtime/Testing/SnowPerfProbe.cs` | Test harness: samples `FrameTiming.gpuFrameTime` in alternating 2 s volume-on/off blocks and logs paired deltas. Exists because polling the editor from outside is noisier than the feature. |
| `Compute/Resources/DeformableSnow.compute` | `BlurH`, `BlurV`, `Accumulate`, `Visualize`. Unprefixed uniform names. |
| `Shaders/Resources/DeformableSnowCapture.shader` | Flat white coverage mask, `ZTest Always`, `ZWrite Off`, `ColorMask R`. |

Scene `Assets/NatureManufacture Assets/River Auto Material/Demo/URP RAM 2019 Demo Mobile.unity` was saved with one new root, `DeformableSnow`:

- `Snow Volume` at (118, 0, 192), 8 × 8 m, `surfaceHeight` 27.39, `captureDepth` 1.52, 1024², snow depth 6 cm, contact gain 2.5, refill 0.02/s.
- `Snow Test Walker` (capsule body plus two foot cubes; deformers on the feet only) patrolling (115, 190.8) → (121, 193.2) at 1.1 m/s.
- `Snow Debug View`.

No third-party asset, material, terrain, or URP renderer asset was modified. The scene was saved once with only this root added; it is clean (`dirty=False`) and all other roots are untouched.

### 2. Design decisions

- **Capture via an explicit `CommandBuffer` + `Graphics.ExecuteCommandBuffer`, not a URP renderer feature** (the plan's Phase 1 step 2 proposed a renderer feature). Rationale: no `ScriptableRendererData` edit, no RenderGraph/SRP integration risk, and the capture runs before the camera renders so the heightfield is current for the same frame. A renderer feature can replace this in Phase 2 if the snow needs to share the camera's render graph.
- **Explicit deformer registry instead of a layer mask** (plan proposed a layer mask). Rationale: adding a layer to the volume requires no scene-layer bookkeeping, and a deformer can be placed on a foot without tagging unrelated geometry. Layer selection can be layered on later.
- **Capture target has no depth buffer.** The mask shader always overwrites, so z-testing and a depth surface would cost 4 MB and buy nothing.
- **Separable 5-tap blur, two passes.** The first working build used a single 25-tap pass; splitting it into horizontal + vertical kept the same visual result (verified: identical `max`, `mean`, coverage) and cut cost by about 60 %.
- **Symmetric ortho projection (`Ortho(half, -half, ...)`)** combined with `Matrix4x4.Scale(1, 1, -1)` in the view and `GL.GetGPUProjectionMatrix(ortho, true)`. Without all three, the slab is behind the camera or clipped by D3D's reversed depth range. This was the cause of the first empty-capture bug; it is written down so Phase 2 does not rediscover it.

### 3. Environment

Unity 6000.4.0f1, URP 17.4.0, RTX 3060 Laptop 6 GB, i7-11800H, 16 GB, Windows 10, 1920 × 1080, Editor Play Mode, `status=ready`, no compile errors.

### 4. Validation completed

All figures from Play Mode, `SnowView` camera at (113.5, 32.14, 185.5) looking at the volume centre.

| Check | Method | Result |
|---|---|---|
| Capture orientation | 32 × 32 ASCII map of the heightfield | Trail runs along the patrol diagonal from (115, 190.8) to (121, 193.2); no flip, no offset — **superseded, see the 2026-11-19 capture-mirror fix below.** The trail was mirrored in Z and the ASCII map could not show it, because the patrol is itself a diagonal and a diagonal mirrored about the diagonal is indistinguishable |
| Camera equals mask | `captureMax` readback | **1.0000** |
| Accumulation | 8 s of walking then readback | `maxHeight` **0.9997**, `meanHeight` 0.025, coverage **3.4 %**, centroid (117.84, 192.06) vs volume centre (118, 192) |
| Culling | Walker lifted out of the slab, readback | `draws=0`; capture mask empty |
| Refill | Walker lifted, sampled every 8 s | max **1.000 → 0.750 → 0.493** (≈ 0.02 / s, matches the configured rate) |
| Frame-rate independence | Refill measured with the same wall-clock window as `Simulate` uses `Time.deltaTime` | Consistent with the configured rate; no frame-count term in the kernel |
| R8 capture format | Same readback suite after switching from `RFloat` | Identical results; 8-bit is sufficient for a coverage mask |
| No per-frame readback | Code inspection | `AsyncGPUReadback` appears only in `AgentScripts/Phase1Test.cs` and `Phase1Stats.cs`, never in runtime |

Screenshots:

| File | Contents |
|---|---|
| `Phase1_TestRig.png` | Walker on the terrain, overlay hidden |
| `Phase1_Height_Overlay.png` | Live compression heightfield, two parallel foot tracks |
| `Phase1_Capture_Mask.png` | Raw capture mask |
| `Phase1_Derived_Normal.png` | Normals derived from the heightfield |

### 5. Performance and memory

In-engine `SnowPerfProbe`, 10 blocks × 2 s, 1920 × 1080, Vista camera preset (the volume is off-screen, so the delta is the compute pipeline only, not the rendered scene):

| Run | Pair deltas (ms) | Median | Mean |
|---|---|---|---|
| Final build | 0.123 / 0.097 / 0.074 / 0.041 / 0.073 | **0.074** | **0.081** |
| Earlier 4-phase run | 0.098 / 0.053 | 0.076 | 0.076 |

Steady-state cost: **≈ 0.07–0.10 ms GPU, median ≈ 0.08 ms** for capture + two blur passes + accumulate at 1024². Well inside the approved ≤ 1.0 ms budget. Sampling the same thing through CLI polling produced 0.1–0.3 ms spreads from GPU clock drift, which is why the paired-block probe is the recorded method.

Scene cost with the volume enabled: 245 draw calls vs 243, 162,970 vs 162,946 triangles (the plus is the two foot cubes; the volume itself renders nothing yet).

Memory at 1024²: capture R8 1 MB + blur-H R8 1 MB + blur-V R8 1 MB + two heightfields `RFloat` 4 MB each = **11 MB idle**, plus 4 MB `ARGB32` only while the debug overlay is visible. Capture target has no depth buffer.

### 6. Known limitations, risks, next smallest safe task

- **Memory is 11 MB, not the 8 MB planned for a 40 m volume.** The two `RFloat` heightfields are 8 MB of it. `RHalf` would bring the total to 7 MB; deferred because the heightfield is only 0–1 compression and the saving does not change any current decision. Recorded as a deviation, not silently accepted.
- The volume is a single axis-aligned 8 × 8 m box. Sloped terrain is only handled because `captureDepth` covers the local height spread (1.02 m here). A 40 m volume on this terrain would need either more capture depth or a terrain-conforming slab.
- The capture slab is `ZTest Always`, which is correct for a coverage mask but cannot represent overlapping geometry ordering. It does not matter at mask level 1; it would matter if phase 2 ever wants "depth of contact".
- The contact `Gain` of 2.5 saturates in ~0.4 s of contact, so a standing foot reaches 1.0 quickly. This is intentional for slush but has not been tuned against a real animation.
- Nothing renders the heightfield in the world yet, and the snow does not hide the grass. That is Phase 2 and is the reason the current build is debug-only.
- Walking still uses `SnowTestWalker`, a scripted patrol. It must not ship.
- Next smallest safe task: Phase 2 step 1–3 — generate the displaced patch mesh, sample the existing heightfield in the vertex stage, and add the matching shadow-caster pass before adding any shading.

## 2026-11-19 Phase 2 entry — slush surface, detail texture, broken patch edge

### 1. What changed

| File | Change |
|---|---|
| `Shaders/DeformableSnowSurface.shader` | Finished the interrupted edge-taper refactor. `SnowOffsetAt` / `EdgeFade` / `EdgeBreakup` / `EdgeGrain` / `ClipSnowEdge` added and wired into the forward, shadow-caster and depth passes. |
| `Runtime/SnowDetailTexture.cs` | New. Runtime-generated seamless tiling detail normal (RG), height (B) and tint (A) at 256², 5 octaves, 3 blur passes before differentiating. |
| `Runtime/DeformableSnowSurface.cs` | Assigns the generated `_DetailTex` in `EnsureMaterialInstance`. |
| `AgentScripts/Phase2Reimport.cs` | Test harness: force-reimports the shader and reports `ShaderUtil` messages. |
| `AgentScripts/Phase1Camera.cs` | Added `EdgeView` preset. |
| `AgentScripts/Phase2Setup.cs` | Sets `_EdgeFade`, `_DetailStrength`, `_DetailScale`, `_NormalStrength`, and the revised slush colours. |

### 2. Why

- **The refactor was unfinished, not merely unbuildable.** The previous session stopped mid-edit at a provider free-limit 429, having added `EdgeFade` and `EdgeBreakup` but wired neither into a pass. `SnowOffsetAt` also called `SnowOffsetMetres` before its declaration, so the shader failed to compile (`undeclared identifier 'SnowOffsetMetres'`, line 108, all passes). The last visual captures predate that edit, so the recorded images were of shader state that no longer existed.
- **One silhouette rule, not three.** The forward, shadow and depth passes previously disagreed: only the forward pass stippled the border, so the snow would have drawn an irregular outline and cast a rectangular shadow. The clip is now a single `ClipSnowEdge` helper called from all three.
- **The taper had to move the boundary, not shorten the ramp.** Modulating the *ramp width* leaves the fade pinned to the mesh rectangle, so the patch still ends on a straight cut — this was caught by capture, not by reading the code. The breakup now insets the end of the snow inward from the rectangle, and the meander amplitude is decoupled from the height ramp so it reads on a 16 m patch.
- **Detail is generated, not imported.** Shipping a normal-map asset would add a texture import and a `.meta` GUID to maintain for a few dozen instructions of noise. Generated once, world-XZ projected, no UV layout or lightmap concern.

### 3. Environment

Unity 6000.4.0f1, URP 17.4.0, RTX 3060 Laptop 6 GB, i7-11800H, 16 GB, Windows 10, 1920 × 1080, Editor Play Mode, `editor_status=ready`.

### 4. Validation completed

| Check | Method | Result |
|---|---|---|
| Shader compiles | `AssetDatabase.ImportAsset` + `ShaderUtil.GetShaderMessages` | **0 messages**, `isSupported=True`, 3 passes |
| No runtime errors | `console --level error --since 471` after the fix | **0 entries**; all 12 recorded errors are seq ≤ 470, pre-fix |
| Edge outline | `Phase1Camera.EdgeView` and `SnowView` captures | Border now meanders as a drift edge instead of a straight cut |
| Print shading | `Phase1Camera.SnowDetail` at 0.55 m | Compression, raised rim and wet sheen all read |
| Shadow / depth parity | Code inspection | All three passes share `SnowOffsetAt` and `ClipSnowEdge` |

Screenshots: `Phase2_Edge_Second/Third/Fourth.png` (edge iteration), `Phase2_Edge_Fourth_Wide.png`, `Phase2_Edge_Detail.png`, `Phase2_Detail_Third.png`.

### 5. Known limitations, risks, next smallest safe task

- **The trail merges into two continuous rails at close range.** This is the gait, not the shading: `SnowTestWalker` uses `stepLength` 0.30 m against a 0.30 m foot, so consecutive prints touch. It was already observed before this entry. Fix belongs to the gait (shorten the foot or lengthen the step), not the material.
- **`_DetailStrength` / `_DetailScale` are set from `Phase2Setup`, not the material asset.** The generated texture is `HideAndDontSave` and cannot be serialized into `PackedSlush.mat`; the floats *are* serialized, so the asset and the setup script must be kept in step.
- **No performance measurement for this phase yet.** The detail texture adds two fetches and the taper adds a few trig ops per fragment. Both are small but unmeasured; Phase 2 acceptance still requires a before/after on the benchmark route.
- **`ClipSnowEdge` discards in the shadow pass**, which means the snow casts an alpha-tested shadow. That is intended for the fringe, but it has not been checked for shadow-map crawl at distance.
- Next smallest safe task: hold the surface still and **fix the gait**, then capture the trail at gameplay height for Phase 2 visual acceptance. After that, measure the fragment cost before adding the far relief path.

## 2026-11-19 Phase 2 fix — capture was mirrored in Z; prints merged into rails

Reported symptom: "the footstep tracking is not correct". Two independent defects were behind it, and the second was only visible once the first was fixed.

### 1. What changed

| File | Change |
|---|---|
| `Runtime/DeformableSnowVolume.cs` | `captureFlipY` now defaults to `true`. The unused flip fields are initialised explicitly. |
| `Runtime/Testing/SnowTestWalker.cs` | `stepLift` 0.09 → 0.14 m; new `liftSharpness` (0.4) applied as `pow(sin, k)` to the swing curve. |
| `AgentScripts/Phase2FootProbe.cs` | New harness: correlates the heightfield against the rig, reports a tracking verdict, and profiles gait runs. |
| `AgentScripts/Phase2Gait.cs` | New harness: patches the serialized gait values in place. Re-running `Phase1Setup` would delete the Phase 2 surface. |

### 2. Why — defect 1: the heightfield was mirrored in world Z

The capture basis from `LookRotation(Vector3.up, Vector3.forward)` is right = −X, up = +Z, forward = +Y. The ortho bounds `Ortho(half, -half, -half, half)` mirror X, and `GL.GetGPUProjectionMatrix(ortho, renderIntoTexture: true)` flips Y on D3D. Those two effects **cancel on X but not on Y**, so the raw mask arrived mirrored in texture V (world Z).

Measured before the fix, with the walker at `(120.46, 192.99)`:

| Signal | Measured | Correct |
|---|---|---|
| Trail principal axis | `(0.929, −0.371)` = −21.8° | walker forward `(0.929, +0.371)` = +21.8° |
| Compression under foot L / R | `0.000` / `0.000` | ≈ 1.0 |
| Trail bbox Z | 190.6..193.4 | patrol Z 190.8..193.2 |

After the fix: `dot=1.000` (collinear), both feet read 0.99–1.00, and bbox `X 114.75..121.20 Z 190.58..193.42` matches the patrol line.

**The correction belongs in the flip flag, not the ortho bounds.** Flipping the Y ortho bounds was tried first and captured *nothing at all* — it moves the slab outside the render range rather than flipping the image. The existing `CaptureFlipY` hook applies the mirror inside the blur kernel, before any consumer reads the heightfield, so the trail, the shading and the probes all see the same corrected data.

### 3. Why — defect 2: the swing foot was still inside the contact band

With tracking correct the trail was still two continuous 6.46 m rails. Root cause is the gait, not the capture: at `stepLift` 0.09 m against a 0.06 m contact band, a plain sine holds the swinging foot within capture range for most of its travel. Foot 0.30 m + ≈0.19 m of smear on each shoulder ≈ 0.68 m, which is longer than the 0.61 m stride, so every print fused with its neighbour.

Fix: raise the lift above the band with margin (0.14 m) and flatten the sine near the ground (`pow(sin, 0.4)`) so the foot leaves and re-enters quickly.

| Metric | Before | After |
|---|---|---|
| Discrete runs along the patrol | **1** | **11** |
| Mean run length | 6.462 m (whole patrol) | 0.392–0.425 m |
| Pressed fraction | 100 % | 67–72 % |
| Run period | — | 0.587 m vs 0.611 m stride (one print per run) |

A run of ≈0.40 m against a 0.30 m foot is the print plus ≈0.05 m of blur ramp per side. The period confirms each run is a **single** print and not two merged.

### 4. Validation completed

- Probe `TRACKING: OK - trail lies along the path the feet took`, `dot=1.000 cross=0.000`.
- `Phase2FootProbe` gait profile: 11 runs, 0.392–0.425 m mean, at five lateral offsets.
- Screenshots: `Phase2_Track_Top_First.png` (mirror fixed, still merged), `Phase2_Track_Top_Second.png` (discrete prints), `Phase2_Track_Detail_Second.png`, `Phase2_Track_Close_First.png` (gameplay height).
- Console: only stale CLI invocation errors (seq ≤ 476); nothing emitted by the capture or gait work.

### 5. Known limitations, risks, next smallest safe task

- **A mirrored heightfield passes every check that does not compare two coordinates.** The Phase 1 validation recorded `captureMax=1.0000`, `maxHeight≈1.0`, correct coverage and a plausible refill curve — all of which a mirrored field satisfies. The 32 × 32 ASCII map in `Phase1Stats` was read as "no flip, no offset", but a diagonal trail mirrored about the diagonal is itself. **Correlation against an authored patrol line is the check that catches it**, which is what `Phase2FootProbe` now does; keep using it.
- The blur ramp widens each print by ~0.05 m per side. That is the 9-tap sigma-2 kernel from the edge work; it is correct for soft slush edges but it does mean pressure marks are physically larger than the foot.
- `liftSharpness` at 0.4 is tuned for this 0.30 m step at 1.8 Hz. A different stride or contact band needs re-tuning; the value is exposed rather than hard-coded.
- Still no Phase 2 fragment-cost measurement on the benchmark route.
- Next smallest safe task: re-run `Phase1Stats` against `Phase2FootProbe` and confirm the trail correlation is now part of the standing validation, then measure fragment cost.

## 2026-11-19 Phase 2 tooling — editor setup window

### 1. What changed

| File | Role |
|---|---|
| `Editor/DeformableSnowSetupWindow.cs` | New. `EditorWindow` with Setup / Appearance / Validate tabs, `[MenuItem("57Studios/Deformable Snow Setup")]`. |
| `Editor/DeformableSnowSetup.cs` | New. `DeformableSnowSetupSettings` plus `Run` / `Clear` / `CheckScene`. All scene and asset mutation. |
| `Editor/DeformableSnowValidator.cs` | New. Play-Mode correlation, stance-foot and gait-run checks. |
| `Tests/Editor/DeformableSnowSetupTests.cs` | New. 7 EditMode tests over the setup path. |
| `Runtime/DeformableSnowVolume.cs` | `Configure` takes an optional `contactBandMetres`. Additive; the old 7-argument call still compiles. |
| `Runtime/Testing/SnowTestWalker.cs` | New `ConfigureGait(...)` typed setter and `PatrolStartWorld` / `PatrolEndWorld` / `PatrolOrigin`. |
| `Documentation/*` | This record, the status table, and the plan/README status lines. |

`AgentScripts/` is unchanged and still works. The window is now the primary path; the CLI harnesses remain for automated probing.

### 2. Why

- **The setup was invisible.** Every step lived in `AgentScripts/`, which is outside `Assets/` and therefore ships with nothing and appears in no menu. The `Editor/` assembly existed but held no code.
- **The ordering rule was a trap.** `Phase2Gait`'s own comment said never re-run `Phase1Setup` or the surface is lost, because `Phase1Setup` called `DestroyImmediate` on the whole root with no undo. One combined, undoable operation removes the rule instead of documenting it.
- **Recreating the mesh asset was a silent GUID change.** `AssetDatabase.DeleteAsset` then `CreateAsset` at the same path assigns a new GUID and breaks every existing reference. The window copies the baked mesh into the existing asset instead. This is verified by test.
- **The old flow wrote whatever scene was open.** Setup now refuses to run without a main Terrain, and always names the target scene next to the button before anything is written.
- **Material values had two homes.** `Phase2Setup` held literals that duplicated `PackedSlush.mat`. The Appearance tab reads and writes the material itself, so there is one source of truth.
- **The contrast with the house pattern:** TerrainScatter already ships `EditorWindow` + `static class ...Generator` + EditMode tests. This matches it.

### 3. Validation completed

| Check | Method | Result |
|---|---|---|
| Compile | `recompile` → `recompile_status` | `completed`, no errors from the feature |
| Menu registration | reflection over the `MenuItem` attribute | `'57Studios/Deformable Snow Setup'`, window opens |
| Guard | `CheckScene` in a scene with no terrain | returns the "no main Terrain" reason; `Run` creates nothing |
| Idempotence | `Run` twice | exactly **1** root both times |
| GUID stability | `AssetPathToGUID` before/after a rebuild | **unchanged** (`44129919cd43ca0489be4ccce4741888`) |
| Undo | `Undo.PerformUndo` after one clean `Run` | root count **1 → 0** |
| Clear | `Clear()` | root removed, mesh asset **kept** |
| Production build | full 16 m / 4 cm setup | 160 801 verts / 320 000 tris, material bound, scene saved |
| Play-Mode validate | `DeformableSnowValidator.Run` | `COLLINEAR` (dot 1.000), stance foot compressed, 8–10 discrete runs |
| EditMode tests | `run_tests --mode EditMode` | **7/7 passed** |

Scene audit after the runs: the benchmark scene's own `SceneRoots` document lists 18 roots — the 17 native demo roots plus `DeformableSnow`. No demo content was removed.

### 4. Known limitations, risks, next smallest safe task

- **The validator is Play-Mode only**, because the heightfield does not exist in edit mode. The tab says so and disables its button.
- **`DeformableSnowSystem` is `internal`**, so the editor assembly cannot enumerate volumes through it and uses `FindAnyObjectByType`, like the CLI scripts. If tooling ever needs multi-volume enumeration, that registry needs a public read-only accessor.
- **The Appearance tab edits the material but not the shader defaults**, so "Reset to Defaults" restores the values the old script used, which are not the shader's own defaults. They differ for `_SnowColor`, `_CompressedColor`, `_CompressedThreshold`, `_CompressedSoftness`, `_WetSpecular`, `_WetShininess` and `_NormalStrength`.
- **`Run` still saves the scene** when the toggle is on (default), because the flow is meant to be one click. The scene path and dirty state are shown first, so it is never a surprise; turn the toggle off for a dry run.
- **Editor UI screenshots are not captured.** Granularity in this record is measured values, not window chrome.
- Next smallest safe task: use the Appearance tab to tune the slush against the current captures, then take the Phase 2 fragment-cost measurement.

## 2026-11-19 Phase 2 tooling — terrain-driven setup dimensions

Reported: the setup tab should accept a slotted-in Terrain and derive the dimensions from it.

### 1. What changed

| File | Change |
|---|---|
| `Editor/DeformableSnowSetup.cs` | `TerrainFit` struct; `FitToTerrain`, `ApplyFit`, and a terrain guard in `Run`. New settings `Terrain`, `AutoFit`, `TargetTexelSize` (8 mm), `PatchMultiple` (2). |
| `Editor/DeformableSnowSetupWindow.cs` | Terrain slot, terrain summary, live **Fit Preview**, and a disabled-while-auto group for the derived fields. Preview is cached by an input hash. |
| `Runtime/DeformableSnowVolume.cs` | `SetTerrain(Terrain)` so the capture bakes its ground reference from the slotted terrain rather than guessing. |
| `Tests/Editor/DeformableSnowSetupTests.cs` | 8 new tests for the fit; 15 total. |

### 2. Why

- **Texel size, not volume size, is the real constraint.** A footprint is only resolvable if it spans several heightfield texels. The approved 40 m / 1024² config is 3.9 cm per texel, so a 14 cm foot spans 3.6 texels; 8 m / 1024² is 7.8 mm and spans 17.9. Sliding the texel size therefore derives the volume extent from the property that actually matters.
- **Patch follows the volume** at a configurable ratio, because the two were previously hand-typed and could drift apart (the asset filename even encoded the old pair).
- **Choosing a terrain must not relocate a validated site.** The first implementation recentred to the terrain's middle whenever auto-fit was on, which silently moved the benchmark from (118, 192) to (250, 250) and changed `surfaceY` from 27.39 to 11.56. It now only moves a centre that falls outside the terrain.

### 3. Validation completed

`FitToTerrain` against the benchmark terrain (500 × 500 m, heightmap 257):

| Settings | Result |
|---|---|
| 8 mm, 1024, ratio 2 (default) | centre **(118, 192)** — the validated site — volume **8.00 m**, patch **16.00 m**, **320k tris**, `surfaceY` **27.39**, `captureDepth` **1.52**, 17.9 texels per foot |
| 2 cm, 1024 | volume 20 m, patch reduced 40 → **28.28 m** to hold 999k tris, 7.2 texels per foot |
| 3.9 cm, 1024 (old 40 m config) | volume 40 m, patch reduced 80 → 28.28 m, **3.6 texels per foot** — confirms the old config could not resolve a print |
| Centre outside terrain | relocated into the terrain, as intended |
| Oversized request | clamps to 1 M triangles and **reports the reduced size it will actually build** |

End-to-end (`Phase4FinalSetup`, terrain slotted explicitly): volume at (118, 192), 8 m, `surfaceY` 27.39, patch 16 m, 320 000 tris — **built vs fit matched on every field**. Scene saved clean.

Both terrains in the scene fit correctly and independently: main `Terrain` → (118, 192); `Floating Island` (70 m, at 291,441) → (326.3, 476.0), `surfaceY` 3.21.

Play-Mode `DeformableSnowValidator` on the fitted rig: `COLLINEAR` (dot 1.000), stance foot compressed, 7 discrete runs at **0.315 m** mean.

Tests: **15/15 passed** (EditMode).

### 4. Bugs found and fixed while doing this

- **The fit reported sizes it would not build.** The first clamp kept an 81.92 m patch while reporting 8.4 M triangles. It now reduces the patch to fit the budget and says so.
- **A volume hanging off the terrain was accepted.** Setup could build a rig over a 70 m island whose 8 m volume plus 16 m patch extended past the edge. `FitToTerrain` now returns invalid with the terrain's size, and `Run` refuses before writing anything.

### 5. Known limitations, risks, next smallest safe task

- **The Validator's stance-foot check is a single-instant spot check.** Accumulation needs ~0.4 s to reach 1.0, so a foot that has just landed reads low legitimately; the bar is therefore 0.2, and the trail-axis correlation is what carries the precision.
- **The test rig ping-pongs, so a long Play session saturates the rail** and prints stop being separable. The validator now detects this (mean rail height > 0.85) and says so instead of reporting "merged prints".
- **The fit only models the surface tier.** The 1 M triangle guard is a judgement call, above the 320k that has actually been measured; the warning says so.
- **`AutoFit` overrides `PatchSize` and `QuadSize`-derived settings silently** except for the visible preview. It is shown, not hidden, but there is no "apply these numbers" button that locks them into the settings object beyond `ApplyFit` at run time.
- Next smallest safe task: take the Phase 2 fragment-cost measurement at the validated 320k size, then decide whether the triangle guard should move.

## 2026-11-19 Phase 2 tooling — whole-terrain coverage via a graded mesh

Reported: slotting a terrain covered only a 16 m patch, so 99.9 % of a 500 m terrain had no snow. Requested: cover the whole terrain, keep deformation in a bounded cell.

### 1. What changed

| File | Change |
|---|---|
| `Runtime/SnowMeshBuilder.cs` | **New.** Graded ring mesh builder with a cached bilinear height sampler. |
| `Shaders/DeformableSnowSurface.shader` | `_EdgeEnabled` gate; `_TerrainRect` discard for the overhang. `SnowOffsetAt` skips the taper in continuous mode. |
| `Runtime/DeformableSnowSurface.cs` | `SetContinuousCoverage`, `ApplyCoverageMode`; binds the rect and the clip parameters in Edit Mode too. |

### 2. Why a graded ring mesh

One surface covering the terrain exactly once. A separate far mesh plus a dense patch would have to either overlap (z-fighting) or leave a hole to maintain; rings avoid both. With a ring scale of 4 each ring spans 4× as far at 4× the quad size, so cost stays bounded regardless of terrain size.

### 3. Four bugs found by measuring, not by reading

1. **The mesh was invisible.** `ClipSnowEdge` reads `_PatchParams`, which `Bind()` only set in Play Mode. In Edit Mode it was zero, so the clip discarded every pixel. `_EdgeEnabled` now gates the clip, and the surface binds in Edit Mode too.
2. **`SnowOffsetAt` would have flattened the surface.** It multiplied thickness by `EdgeFade`; in continuous mode that is 0 across the whole mesh. Now gated on `_EdgeEnabled`.
3. **121 of 501 sampled triangles were wound downward** and backface-culled. Verified by computing the cross product by hand: with the rim running clockwise and the ring outside it, `(a,c,b)` faces down. The full-scan check now reports **105 600 / 105 600 facing up**.
4. **The mesh sliced through hills.** Ring radial steps were ~19× the tangential step (3.0 m vs 0.16 m at ring 1), so each ring was a wide flat band. Radial count is now derived from the heightmap's texel size, because a cell finer than the height data cannot follow the ground any better.

A fifth was environmental: the editor had been left in `GlobalSnow/Demo/SnowScene_Forward`, so probes were measuring the wrong terrain (70 m island, not the 500 m one) and a third-party `GlobalSnow/Snow` shader error was polluting the console.

### 4. Measured result

On the 500 × 500 m benchmark terrain, 8 m dense cell, 4 cm quads:

| Metric | Value |
|---|---|
| Rings | 4 |
| Reach | 353.6 m (the terrain's half-diagonal) |
| Dense cell vertices | 40 401 (the movable, print-carrying part) |
| Total vertices / triangles | 194 801 / **388 800** |
| Build time | **134–186 ms** |
| Seam integrity | **800 open edges = the outer rim only**; every interior edge is shared, so no T-junction cracks |
| Winding | every triangle faces up |
| Coverage | mesh bounds 707 × 707 m vs a 500 m terrain |

Screenshots: `Phase5_Coverage_Wide.png` (whole terrain from above), `Phase5_Coverage_Grazing.png` (eye level, no seam cracks).

### 5. Known limitations, risks, next smallest safe task

- **388 800 triangles is above the 320 k that has been measured** against the 1 ms budget, and the shadow and depth passes double it. Not profiled yet — this is now the blocking measurement.
- **Water shows through.** The lakes and rivers sit above the terrain surface in this scene, so a terrain-conforming snow mesh is below them. Whether snow should cover water is a design question, not a mesh bug.
- **The dense cell does not move yet.** The mesh builder and the shader support it, but `MoveCell` / `ReprojectHeightfield` and the follower component are not written. Prints currently exist only near the initial centre.
- **The mesh is rebuilt whole, not incrementally.** At 134–186 ms that is fine for setup but too slow for a moving cell; moving will require re-anchoring only the dense band.
- Next smallest safe task: implement the dense-band re-anchor and the follower, then profile the full triangle load in Play Mode before deciding whether the ring scale needs to change.

## 2026-11-19 fix — "there is no snow": undeclared shader properties

Reported after the graded mesh landed: the terrain was bare.

**Root cause.** `_VolumeMin`, `_VolumeSize`, `_HeightTexel`, `_PatchParams` and `_TerrainRect` were declared inside `CBUFFER_START(UnityPerMaterial)` but **not in the shader's `Properties` block**. `Material.SetVector` / `SetFloat` cannot write a name that is not a declared property — the call fails silently and the shader reads zero.

Consequences, in order of how noticeable they were:

- `_TerrainRect` read `(0,0,0,0)`, so the continuous-coverage clip `if (outside the rect) discard` discarded **every pixel**. That is the bare terrain.
- `_PatchParams` read zero, so the patch-edge clip would have discarded everything too — the same failure in fixed-patch mode.
- `_VolumeMin` / `_VolumeSize` read zero, so `VolumeUV` divided by a guard epsilon and displacement was meaningless even when the surface did draw.

This had been latent since the surface was first written: the earlier screenshots only showed snow because `_EdgeFade` happened to be a real declared property and the patch mode used it instead of the rect. The bug became fatal the moment coverage relied on a vector property.

**Fix.** All five are declared in `Properties`. A properties-versus-CBUFFER audit is the check that catches this class of bug, and it is now the rule below.

A second, independent defect was fixed at the same time: the runtime material instance is `HideAndDontSave` and is **never serialized**, so the saved scene falls back to `PackedSlush.mat`. Any value the shader needs in order to draw at all must also live on the asset, so `DeformableSnowSetup` now writes `_EdgeEnabled` and `_TerrainRect` into the material asset during setup.

**Verification.** `_TerrainRect` now reads `(0, 0, 500, 500)` on the saved scene, and the properties-versus-CBUFFER audit reports no unsettable names. Screenshots: `Phase6_Overhead.png` (whole terrain covered), `Phase6_EdgeOn.png` (eye level, no seam cracks).

### New non-negotiable rule

> Every uniform written from C# must be declared as a material property, not only inside the CBUFFER. `Material.SetX` fails silently otherwise, and the failure mode is "the surface does not draw at all".

## 2026-11-20 — mesh path replaced by a screen-space snow layer

### 1. What changed

| File | Change |
|---|---|
| `Runtime/DeformableSnowLayerFeature.cs` | **New.** `ScriptableRendererFeature` at `AfterRenderingOpaques`: exclusion mask, then a composite blit that shades depth. Resolves and binds the volume. |
| `Shaders/DeformableSnowLayer.shader` | **New.** `57Studios/DeformableSnow/Layer`. Depth → world position → derivative normal → slope coverage → heightfield relief → slush shading. |
| `Shaders/DeformableSnowSolid.shader` | **New.** Flat-white override for the exclusion mask. |
| `Runtime/Studios57.DeformableSnow.Runtime.asmdef` | Added `Unity.RenderPipelines.Core.Runtime` and `Unity.RenderPipelines.Universal.Runtime`; the assembly had no URP reference at all. |
| `Runtime/DeformableSnowVolume.cs` | `Simulate(dt, captured)`: the contact term is zeroed on frames the capture was skipped, and the blur is skipped with it. `ResolveTerrain` result is cached and pinned state tracked. |
| `Compute/Resources/DeformableSnow.compute` | New `ContactScale` uniform, multiplied into the contact term. |
| `Materials/PackedSlush.mat` | Repointed at the layer shader; `_EdgeEnabled` / `_EdgeFade` / `_TerrainRect` / `_PatchParams` removed; `_SnowAmount` / `_SlopeThreshold` / `_SlopeSoftness` added. |
| `Assets/Settings/Mobile_Renderer.asset`, `PC_Renderer.asset` | Feature registered, `m_Active: 0`. |
| `Editor/DeformableSnowSetup.cs` | `BuildSurface` now only ensures the material; `StoreMeshAsset`, `MeshAssetPath`, `WholeTerrainCoverage`, `RingScale`, `PatchSize`, `QuadSize`, `PatchMultiple` and the triangle guard are gone. |
| `Editor/DeformableSnowSetupWindow.cs` | Patch/ring controls replaced by coverage controls. |
| `Runtime/Testing/SnowPerfProbe.cs` | `StartLayerProbe()` measures the shading pass; the volume toggle cannot, because the composite is fragment-bound. |
| `Tests/Editor/DeformableSnowSetupTests.cs` | 4 mesh tests deleted, 2 added. 13 total. |

**Deleted:** `Runtime/SnowMeshBuilder.cs`, `Runtime/DeformableSnowSurface.cs`,
`Shaders/DeformableSnowSurface.shader`, `Generated/SnowWhole_8m_4cm.asset` (18 MB),
`Generated/SnowPatch_16m_4cm.asset` (15 MB). The `Snow Surface` object was removed from
`URP RAM 2019 Demo Mobile.unity`.

### 2. Why

The mesh path maintained a **second description of the ground** that had to agree with the
terrain exactly, and every geometry bug in this record came from that agreement: the Z mirror,
the inverted winding (121 of 501 sampled triangles), `_EdgeEnabled` discarding the whole world,
the `_TerrainRect` overhang, the forward-only `Offset -1,-1`, and three mutually inconsistent
`ResolveTerrain` predicates. Deleting one of the two surfaces removes the whole class.

The numbers made it worse than a style problem: of 388 800 triangles, only the 80 000 in the 8 m
dense cell could ever receive a print, and the rest were drawn three times (forward, shadow,
depth). The dense cell was also pinned at (118, 192) because the follower was never written, and
a whole-mesh rebuild was 134–186 ms.

The decision that unlocked this was revisiting the "real displacement required" rule. The chosen
visual target is **packed slush**: 6 cm of snow compressed to 1.2 cm, which is roughly two
degrees of relief at eye height. There was almost no silhouette to buy. Coverage moved to screen
space; deformation stayed in the heightfield, because the heightfield was never the problem.

### 3. Environment

Unity 6000.4.0f1, URP 17.4.0, active pipeline `Mobile_RPAsset` → `Mobile_Renderer` (Forward,
`m_RequireDepthTexture: 1`, render scale 0.8, MSAA 2×). RTX 3060 Laptop 6 GB, i7-11800H, 16 GB,
Windows 10, 1920 × 1080.

### 4. Validation completed

- **Compile:** clean; the one error found (`Texture2D.greyTexture` does not exist) was caught and
  fixed.
- **Unit-test state:** the EditMode suite was updated but **not executed** — Unity stopped
  auto-refreshing with the project unfocused and the pipeline CLI rejected the call without its
  auth token. This is the outstanding item.
- **Play Mode:** not yet run. No visual or performance claim is made for this change, per the
  standing rule.

### 6. Follow-up — the layer shaded the terrain's triangles, not its surface

Reported after the first look: the terrain read as flat grey facets, like a low-poly mesh.

**Cause.** The composite derived its normal from `ddy/ddx` of the reconstructed world position.
That is a per-triangle normal by construction, so every triangle of the terrain mesh shaded as a
facet. The terrain itself looks smooth because `TerrainLit.shader`'s `DepthNormals` pass writes
`TransformObjectToWorldNormal(v.normalOS)` — the heightmap-derived vertex normal, interpolated
across the triangle. Snow was shading a different, coarser normal than the ground underneath it.

**Fix.** The feature now calls `ConfigureInput(Depth | Normal)` and the shader samples
`SampleSceneNormals` with linear filtering when `_SNOW_LAYER_NORMALS` is set, falling back to the
derivative normal when the texture is not valid. Linear filtering matters only at triangle seams,
which is exactly where faceting shows.

**Cost, stated honestly.** This forces a `DepthNormals` prepass, which re-renders every opaque
object. That is the cost originally avoided by choosing derivative normals. It is worth it: a
facet-free surface is not a detail, and the alternative was visibly broken. Confirmed viable
here because the benchmark terrain is URP's stock `TerrainLit.shader`, which has the pass
(`Terrain/TerrainLit.shader` line 230, writing `v.normalOS` at line 63).

**Risk.** A third-party terrain or mesh shader without a `DepthNormals` pass writes nothing to
that texture, and snow over it falls back to faceting. `TerrainLit` is fine; a custom terrain
shader is the case to check first if faceting reappears in another scene.

### 7. Colliders — there are none, by design

The capture reads **renderers**, not physics. `DeformableSnowVolume.Capture` calls
`DrawRenderer` on each `Renderer` under a `DeformableSnowDeformer`, and the capture shader keeps
only geometry within `_ContactBand` of the terrain. `DeformableSnowSetup.StripCollider`
deliberately removes the collider that `CreatePrimitive` adds to the feet and body.

So **adding a collider changes nothing about deformation.** What matters is that the object has a
`Renderer`, that the renderer is inside the volume bounds, and that its geometry comes within
`contactBand` (default 6 cm) of the local ground. An object with no collider still leaves prints;
an object with a collider but no renderer leaves none.

That is the point of the technique: no raycasts, no physics probes, and capes, ragdolls and props
work without any per-shape setup.

### 8. Turning the layer off at runtime

Three different switches exist and only one is meant for gameplay:

| Switch | Reachable from gameplay | Effect |
|---|---|---|
| `DeformableSnowLayerFeature.SetLayerEnabled(bool)` | **Yes** — static | Stops the composite *and* the `DepthNormals` prepass it requested. Runtime-only; resets to on after a domain reload. |
| `feature.Enabled` | Yes, if you have a reference | Same effect for one feature instance. Getting the reference is the awkward part, because the feature lives in the renderer asset, not the scene. |
| `feature.SetActive(bool)` | Technically | Writes the serialized `m_Active` field, so it marks the renderer asset dirty in the editor and can persist to disk. Not for gameplay. |
| `volume.enabled` | Yes | Stops capture/blur/accumulate only. Snow keeps drawing, just stops deforming. |
| `_SnowAmount` on the material | Yes | Fades cover to nothing, but the full-screen pass still runs. |

`SetLayerEnabled` is the right one: the early-out is in `AddRenderPasses`, and URP builds
`activeRenderPassQueue` from that call before deciding what prepasses to run
(`UniversalRenderer.GetRenderPassInputs`), so an early-out also removes the `DepthNormals`
prepass. Disabling only the composite would have left that cost behind.

Verified in package source: `ScriptableRenderer.AddRenderPasses` skips features where
`isActive` is false (line 1101), but that is the serialized flag, which is why the runtime flag
is separate.

One trap worth recording: `ScriptableRendererFeature` already defines `OnEnable()` to call
`Create()`. Declaring `OnEnable` in a derived feature silently hides it and the feature never
initialises. Instance registration therefore happens in `Create()` and removal in `Dispose()`.

### 5. Known limitations, risks, next smallest safe task

- **Not yet rendered once.** Every claim above is architectural. The first Play Mode run on the
  Ground preset is the thing that decides whether coverage, bind orientation and slush shading
  are right.
- **No silhouette, no snow self-shadow.** Snow can no longer rise above a rock and casts no
  shadow of its own. Accepted for slush; this is the honest cost of the change.
- **Characters need the exclusion layer configured.** Shoulders and heads are upward-facing and
  would otherwise be painted. The layer ships unset (`m_Bits: 0`), so today the mask pass is
  skipped entirely.
- **Grass is snowed over.** Alpha-tested scatter sits in the depth buffer, and the slope test
  only rejects cards that face sideways. Whether the Ground preset still reads correctly is an
  open visual question.
- **Water relies on draw order.** Lakes and rivers are transparent and composite after
  `AfterRenderingOpaques`, so they stay visible — but only because of ordering, not by design.
- **Composite cost is unmeasured.** At render scale 0.8 that is ~921k pixels with ~10 samples
  each. `SnowPerfProbe.StartLayerProbe` exists for this and has not been run.
- **`captureEveryFrame = false` was a real bug, now fixed.** `Simulate` kept accumulating the
  previous frame's capture, so a stationary foot deepened forever. Latent because the field is
  serialized `true`, but it would have surfaced the moment anyone used it for throttling.
- Next smallest safe task: **run the EditMode suite, then one Play Mode pass on the Ground preset
  with the layer enabled**, and record what the terrain, grass and water look like.


- Do not raycast per frame to find snow contacts.
- Do not create a GameObject or decal per footprint.
- Do not read the heightfield back to the CPU during the update loop.
- Sample the heightfield with surface-aligned 0–1 UVs. Tiling the height lookup causes swimming and misaligned prints.
- Keep heightfield resolution bounded and allocate textures per volume. Pooling and update prioritisation remain future work. Active volumes update independently, and the layer filters volumes by camera visibility for shading.
- Blend normals with Reoriented Normal Mapping, not lerp or overlay.
- Slush amplitude stays under the heightfield's own noise floor; if the trail is thinner than one texel, raise amplitude or reduce volume extent rather than increasing texture size.
- Own and release every `RenderTexture`, `GraphicsBuffer`, and material the feature creates; survive disable, destroy, scene unload, and domain reload.
- Treat dynamic shadow response, AO, and persistence as profiling-gated additions, not baseline requirements.
- Do not modify shared URP renderer assets or third-party demo scenes without explicit approval and a note here.

> **Superseded 2026-11-20.** Two rules were removed with the mesh path: "the displacement mesh and
> its shadow-caster pass must sample the same heightfield" and "displacement is a vertex-stage read
> of a GPU heightfield". There is no displacement mesh. Coverage and shading are a screen-space
> pass over the depth buffer; deformation is still a heightfield read, now in the fragment stage,
> still with surface-aligned 0–1 UVs. Capture remains GPU-based with bounded per-volume
> memory. Characters can now use one authored box per foot. Individual prints reuse the
> volume’s heightfield.

> **Added 2026-11-20.** Any uniform the renderer feature writes must be declared in the layer
> shader's `Properties` as well as its HLSL body, for the same silent-failure reason recorded in
> the 2026-11-19 fix. `CopyPropertiesFromMaterial` only carries declared properties.

## Expected feature layout

```text
Packages/com.57studios.deformable-snow/
  Documentation~/
    RESEARCH.md
    ImplementationPlan.md
    DEVELOPMENT.md
  Runtime/                 # Volume component, pooling, scheduling, renderer feature
  Compute/                 # Blur, accumulate, refill, optional displacement
  Shaders/                 # Snow shading, relief mapping, heightfield normals
  Editor/                  # Authoring window, gizmos, debug views
  Tests/                   # EditMode coverage and editor-only PlayMode harness
```

Add `Materials/` and `Samples/` only when the corresponding phase begins.

## Required handoff update format

When any work is completed, append a dated entry with:

1. What changed and exact project paths.
2. Why the design decision was made.
3. Unity version, pipeline, and test device.
4. Validation completed: console state, screenshots if visual, and profiling data if performance-related.
5. Known limitations, risks, and the next smallest safe task.

Never state that a visual or performance result worked without a recorded baseline and a comparable after-measurement.

## Open decisions

1. **Primary target — answered:** Unity Terrain first (`/Terrain` in `URP RAM 2019 Demo Mobile`). Arbitrary mesh roofs and streets remain supported by the architecture but are not the v1 benchmark.
2. **Silhouette and shadow response — answered: not required for slush.** Originally "required", which drove the generated mesh. Superseded 2026-11-20: the chosen target is packed slush at 6 cm compressed to 1.2 cm, roughly two degrees of relief at eye height, so there is almost no silhouette to buy with geometry. Snow has no silhouette and casts no shadow of its own. Revisit only if the visual target changes or a measured near patch is justified.
3. **Heightfield writers — proposed:** a layer mask that includes the player, NPCs, and movable props; static props are baked into the base height in Phase 5.
4. **Volume count and update budget — answered:** 1024 × 1024 heightfield, ≤ 1.0 ms GPU, 40 m volume. Coverage extent is no longer a geometric question.
5. **Visual target — answered:** packed snow / slush over grass.
6. **Persistence — proposed:** session-only in v1; checkpoint readback in Phase 5.
7. **Gameplay coupling — proposed:** visual only in v1, with a query API layered on later.
8. **Displacement geometry — answered: none.** Superseded 2026-11-20 by the screen-space layer. No mesh is generated, so there is no patch to anchor, follow, or rebuild. If silhouette is later required, the cheap form is a ~2 m camera-anchored patch at 4 cm (~25k triangles), not the 388k whole-terrain mesh.

## External design references

- [Deformable Snow Rendering in Batman: Arkham Origins (GDC Vault)](https://gdcvault.com/play/1020177/Deformable-Snow-Rendering-in-Batman)
- [GDC 2014 slide deck](https://www.slideshare.net/colinbb/gdc2014-deformable-snow-rendering-in-batman-arkham-origins)
- [GTC 2014 companion deck](https://www.slideshare.net/colinbb/gtc-2014-directx-11-rendering-and-nvidia-gameworks-in-batman-arkham-origins)
- [Reoriented Normal Mapping](https://blog.selfshadow.com/publications/blending-in-detail/)
- [URP ScriptableRendererFeature](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.4/manual/renderer-features/how-to-custom-effect-render-objects.html)

## 2026-11-23 Deformable Snow shadow distance

The fullscreen layer now uses URP's world-position-aware main light lookup in
`Shaders/DeformableSnowLayer.shader`, so realtime shadows fade at the configured
distance. The previous lookup sampled the shadow map without the distance fade.
The explicit ambient probe added earlier lit the affected pixels but left the
same incorrect shadow shapes visible in blue. The shader also compiles URP's
low, medium, and high soft shadow variants.

Unity 6000.4.0f1, URP Mobile renderer, Direct3D 11 on an RTX 3060 Laptop GPU:
the distant blue patches disappeared in matching Game view and Scene view
captures with soft shadows. The Game view also passed with hard shadows.
Unity reported no shader import messages. Validation covered the river bend
reproduction view with the existing snow overlay.

Captures are under `Temp/pipeline-screenshots/` with the `snow-shadow-` prefix.

## 2026-11-23 Multi-terrain setup and character footprints

- Terrain authoring creates one volume per selected terrain under a rig in the terrain’s own scene. It validates placement before rebuilding and scopes clearing to the active scene. [Terrain authoring code](../Editor/DeformableSnowSetup.cs)
- The snow layer draws base coverage and samples each visible volume within its own bounds. A single camera can show deformation from multiple loaded scenes. Scene reload creates fresh textures. Each 1024² volume uses 11 MiB for its core textures. [Snow layer code](../Runtime/DeformableSnowLayerFeature.cs)
- Character authoring validates prefab and bone assignments before saving. It adds reusable footprint children to selected bones and preserves existing box settings on reruns. Capture uses the box transform and dimensions, allowing a foot bone to stamp snow while physics is disabled. [Character authoring code](../Editor/DeformableSnowCharacterSetup.cs) · [Volume capture code](../Runtime/DeformableSnowVolume.cs)

Validation used the Unity command pipeline in Unity 6000.4.0f1 with `Mobile_RPAsset` on Windows and Direct3D 11. All 15 setup tests passed. Play Mode checks captured and rendered two volumes at different elevations in the same frame, then confirmed that one volume kept working while the other scene unloaded and was recreated. Batch authoring checks used two temporary character prefabs, verified invalid bone assignments were rejected before saving, and confirmed that a rerun wrote no duplicate footprints. GPU capture registered both planted feet. Raising one foot cleared its half of the capture mask while the other foot kept stamping. The editor console reported no errors.

The check used synthetic characters. Box size and centre still need to fit each character’s animation and sole position. Multi-volume performance was outside this validation pass.

## 2026-11-23 Documentation audit

Updated the documentation for terrain slots, scene ownership, and character footprint setup. Added the box-capture API and troubleshooting steps and linked the original implementation plan to the current workflow. Validation for this pass covered documentation links and labels. [README](../README.md) · [HTML guide](guide.html) · [Implementation plan](ImplementationPlan.md)

## 2026-11-23 Moving player volume in TestBench

Ported the Claim Back The World terrain and road contact path, whole-texel heightfield shifts, and overlap shading. TestBench exposes the moving volume through `DeformableSnowFollower` and the **Add Following Snow Volume** checkbox in the Characters tab. The component defaults to the Default ground layer and lets each prefab select its ground and road collision layers.

Unity 6000.4.0f1 Play Mode validation used a temporary prefab authored through the Characters setup, a terrain, and an elevated mesh road. Both planted feet contributed to capture on both surfaces. Raised feet contributed zero pixels. After a 32-texel shift on both axes, compression at the original world position changed only by the configured refill. Disabling and re-enabling the follower reused the same volume.

## 2026-11-26 UPM migration into GameEnginePackages

- Moved the package from `Assets/57Studios/DeformableSnow` to `Unity/Packages/com.57studios.deformable-snow` in the GameEnginePackages repository.
- Added `package.json`, `CHANGELOG.md`, and package metadata. The package ID is `com.57studios.deformable-snow`; the Unity minimum is 6000.3 and the URP dependency is 17.3.0.
- Renamed `Documentation` to `Documentation~` so Unity does not import it into consumer projects.
- Moved the PlayMode test harness from `Runtime/Testing` to `Tests/PlayMode` and isolated it in the editor-only `Studios57.DeformableSnow.Testing` assembly. The runtime assembly no longer ships the walker or performance probe.
- Replaced the hardcoded `Assets/57Studios/DeformableSnow/Materials` lookup with a package-relative `PackageInfo.FindForAssembly` lookup.
- TestBench now consumes the package through a local `file:` dependency instead of an `Assets/` copy.

## 2026-11-28 Guide direction merge

- Merged the old guide's Labs palette, brand treatment, and direct instructional voice into `Documentation~/guide.html`.
- Kept the newer content: multi-terrain setup, character footprint authoring, the following snow volume, footprint-box API, and revised troubleshooting.
- Restored the explicit warnings that colliders alone do not drive deformation and that renderer-based deformers belong on the parts that should stamp snow.
- Removed `guide_old.html`; the merged guide is the single source.
- Fixed `.gitignore` so `Documentation~` and `Samples~` folders are tracked. The previous `*~` rule excluded both.
