# 57Studios Deformable Snow

MIT licensed. Copyright © 2026 57Studios.

Runtime heightfield snow: surfaces accumulate a low-resolution displacement map that records where characters, props, and debris have pressed into the snow. The renderer turns that map into footprints, trails, and snow banks without per-foot decals or modified terrain meshes.

**Status.** A screen-space pass shades eligible surfaces using the depth buffer. Each snow volume supplies a local heightfield for footprints and trails. Multiple loaded volumes can contribute to the same camera view.

## Installation

### Assets install

Download [DeformableSnow-v0.1.1.unitypackage](https://github.com/57-Studios/GameEnginePackages/releases/download/unity-deformable-snow-v0.1.1/DeformableSnow-v0.1.1.unitypackage) and import it with **Assets → Import Package → Custom Package**. It lands under `Assets/57Studios/DeformableSnow/`.

### UPM install

In Unity, open **Window → Package Manager → + → Add package from git URL**:

```text
https://github.com/57-Studios/GameEnginePackages.git?path=/Unity/Packages/com.57studios.deformable-snow#unity-deformable-snow-v0.1.1
```

UPM installs under `Packages/` and is read-only. Use the `.unitypackage` when you want the files under `Assets/`.

**New to this addon?** Read [`Documentation~/guide.html`](Documentation~/guide.html) — a single self-contained page covering setup, concepts, the full API, tuning, and troubleshooting. Open it in any browser; it needs no server and no build step.

## Why this folder exists

This is a 57Studios rendering feature, not a third-party asset. All scripts, shaders, compute kernels, materials, editor tooling, tests, and documentation belong under this folder. Do not patch content packs or `Library/PackageCache` to make it work.

## Structure

```text
Packages/com.57studios.deformable-snow/
  LICENSE
  README.md
  Documentation~/
    guide.html              # The user-facing guide: setup, API, tuning, troubleshooting
    RESEARCH.md             # Technique study, primary-source notes, Unity translation
    ImplementationPlan.md   # Architecture and delivery phases
    DEVELOPMENT.md          # Living handoff record and decisions
  Runtime/
    DeformableSnowVolume.cs       # Capture -> blur -> accumulate/refill, owns every GPU resource
    DeformableSnowDeformer.cs     # Registers footprint boxes and renderer-based deformers
    DeformableSnowFollower.cs     # Creates a player-following volume in Play Mode
    DeformableSnowSystem.cs       # Volume/deformer registry
    DeformableSnowLayerFeature.cs # Screen-space snow pass, registered in the renderer assets
    SnowDetailTexture.cs          # Runtime-generated seamless detail normal, no texture asset
    DeformableSnowDebugView.cs    # IMGUI overlay, F1/F2
  Compute/                      # BlurH, BlurV, Accumulate, Visualize
  Shaders/
    DeformableSnowLayer.shader          # Depth -> world pos -> coverage -> relief -> slush
    DeformableSnowSolid.shader          # Exclusion mask override
    Resources/DeformableSnowCapture.shader
  Materials/                    # PackedSlush.mat - the single source of truth for appearance
  Editor/                       # 57Studios -> Deformable Snow Setup window
    DeformableSnowSetupWindow.cs   # Setup / Appearance / Validate / Characters tabs
    DeformableSnowSetup.cs         # Undoable rig build and clear
    DeformableSnowCharacterSetup.cs # Adds footprint boxes and deformers to character prefabs
    DeformableSnowValidator.cs     # Play-Mode tracking and gait checks
  Tests/
    Editor/                     # EditMode coverage for the setup path
    PlayMode/                   # Editor-only walker and performance probe; must not ship
```

## Runtime contract (as built)

- **Volume** — Each scene-local volume owns a heightfield for an axis-aligned XZ footprint and a vertical capture slab. At 1024², an 8 m volume gives about 7.8 mm per texel. Unloading the scene releases its volumes. Reloading starts with fresh heightfields.
- **Deformers** — `DeformableSnowDeformer` captures its assigned `BoxCollider` shape or the enabled renderers below it. Put renderer-based deformers on the specific parts that should leave prints, such as a boot or a prop.
- **Per frame:** render deformers into a thin orthographic R8 slab (`ZTest Always`, no depth), separable 9-tap blur, then `height = saturate(height + contact × gain × dt) − refill × dt`. Nothing is read back to the CPU.
- **Capture matrices:** the view needs `Matrix4x4.Scale(1, 1, -1)`, the ortho is X-mirrored, and the projection goes through `GL.GetGPUProjectionMatrix`. Omitting any one gives an empty or clipped capture. The basis mirror and the render-into-texture Y flip cancel on X but **not** on Y, so the raw mask still arrives mirrored in texture V (world Z); `captureFlipY` corrects it inside the blur kernel. A mirrored heightfield passes every single-value check, so verify it by correlating the trail against the authored patrol (`DeformableSnowValidator`), never by peak height alone.
- **Coverage:** a screen-space pass at `AfterRenderingOpaques` shades the existing depth buffer. Normals come from the depth-normals prepass, because that carries the interpolated vertex normal; a per-triangle derivative normal facets visibly on a low-poly terrain.
- **Cost** — The single-volume benchmark measured 0.07–0.10 ms GPU for the compute chain at 1920 × 1080. The composite has a separate cost. Each 1024² volume uses 11 MiB for its core textures (3 × R8 and 2 × RFloat).

## Setting it up

Open **57Studios → Deformable Snow Setup** and use the **Setup** tab.

1. Open the terrain scenes additively and expand **Snow Terrains**. Assign each terrain you want to set up. With every slot empty, setup uses the active scene’s main terrain.
2. Review each terrain’s placement preview and click **Set Up Rig**. This builds one volume and test walker per terrain. Include every terrain you want in each target scene because setup replaces that scene’s rig. One `Ctrl+Z` undoes the build.
3. Enable **Deformable Snow Layer** in the active Renderer asset.
4. Enter Play Mode and use the **Validate** tab to check the loaded test rigs.

### What the terrain decides

With **Fit to Terrain** on, the terrain drives the dimensions rather than hand-typed numbers:

| Derived | From |
|---|---|
| Volume centre | The slotted terrain's middle, unless the current centre already lies on it |
| Volume size | `Target Texel × Resolution` — the texel size is the real constraint, because a print is only resolvable if it spans several |
| Surface height, capture depth | Sampled from the terrain's height spread across the footprint, plus the slab margin |

At the default 8 mm texel and 1024², this reproduces the validated 8 m volume. Raising the texel size grows the volume. A volume that would hang off the edge of a small terrain is refused, with the terrain's size in the message.

**Appearance** edits the shared `PackedSlush.mat` material. **Clear Rig in Active Scene** removes the rig from the active scene.

### Character footsteps

Open **Characters**, click **Add Character**, and assign a prefab asset. Use a prefab variant for an imported model. Pick the left and right foot bones from the searchable lists, then click **Add Footprints to Prefabs**. Use **Add Foot Bone** for extra feet.

Setup saves a child with a `BoxCollider` and `DeformableSnowDeformer` under each selected bone. The box supplies the capture shape with physics disabled by default. Adjust its centre and size in Prefab Mode to fit the sole. Running setup again adds missing footprints and preserves existing boxes.

### Moving volume for the player

Enable **Add Following Snow Volume** in the player’s **Characters** slot before clicking **Add Footprints to Prefabs**, or add **Deformable Snow Follower** to the prefab root. On that component, set **Ground Layers** to the layers used by your walkable terrain and road colliders. It defaults to **Default**.

The player gets a 16 m deformation volume in Play Mode. Prints stay at their world positions while inside the window. Prints that move outside the window are discarded.

## Using it in Play Mode

1. Enter Play Mode in the benchmark scene.
2. Move a configured character or deformer through a snow volume, or watch the generated test walker.
3. Press **F1** to show the debug overlay, **F2** to cycle height / raw capture / derived normal.
4. The overlay prints resolution, footprint, and snow depth.

## Read first

- [`Documentation~/RESEARCH.md`](Documentation~/RESEARCH.md) — what the GDC 2014 talk actually does, numbers, caveats, and which parts survive translation to Unity 6 / URP 17.3.
- [`Documentation~/ImplementationPlan.md`](Documentation~/ImplementationPlan.md) — the phased build, data formats, and exit criteria.
- [`Documentation~/DEVELOPMENT.md`](Documentation~/DEVELOPMENT.md) — current state, measurements, non-negotiable rules, and open decisions.

## Source

Colin Barré-Brisebois (WB Games Montreal), *Deformable Snow Rendering in Batman: Arkham Origins*, GDC 2014, Programming track: <https://gdcvault.com/play/1020177/Deformable-Snow-Rendering-in-Batman>

## License

MIT. See [`LICENSE`](LICENSE). You may use, modify, and redistribute this package, including commercially, provided the copyright and permission notice travels with it.

The license covers **this package only**. Content in the project that consumes it remains under its own terms.

## Benchmark

Phase 0 route screenshots and the Phase 1 validation captures live beside the baseline record:

| File | Contents |
|---|---|
| `Documentation~/Phase0_TerrainGround_Only.png` | Ground preset, terrain only |
| `Documentation~/Phase0_TerrainGround_ScatterOn.png` | Ground preset, full scene |
| `Documentation~/Phase0_TerrainVista_Only.png` | Vista preset, terrain only |
| `Documentation~/Phase0_TerrainVista_ScatterOn.png` | Vista preset, full scene |
| `Documentation~/Phase1_TestRig.png` | Phase 1 test rig on the terrain, overlay hidden |
| `Documentation~/Phase1_Height_Overlay.png` | Live compression heightfield with two foot tracks |
| `Documentation~/Phase1_Capture_Mask.png` | Raw capture mask |
| `Documentation~/Phase1_Derived_Normal.png` | Normals derived from the heightfield |
