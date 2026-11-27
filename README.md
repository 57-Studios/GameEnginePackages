# 57 Studios Labs — Game Engine Packages

Canonical source for 57 Studios Labs packages. All packages are MIT licensed.

- **Workflow and rules:** [`AGENTS.md`](AGENTS.md)
- **Unity packages:** [`Unity/Packages/`](Unity/Packages/)
- **Unreal plugins:** `Unreal/Plugins/` (when the first plugin lands)
- **Godot addons:** `Godot/addons/` (when the first addon lands)
- **Build and release tooling:** `tools/`

## Current state

The first three Unity packages are migrated and are consumed locally by the TestBench project:

- `com.57studios.deformable-snow` — MIT, 16 EditMode tests passing
- `com.57studios.procedural-grass` — MIT, 1 EditMode smoke test passing
- `com.57studios.terrain-scatter` — MIT, 2 EditMode tests passing

The old `Assets/57Studios` copies have been removed. TestBench references the repository packages through its `Packages/manifest.json`.

## Install

In Unity, open **Window → Package Manager → + → Add package from git URL** and use one of these pinned URLs.

### Deformable Snow

```text
https://github.com/57-Studios/GameEnginePackages.git?path=/Unity/Packages/com.57studios.deformable-snow#unity-deformable-snow-v0.1.0
```

### Procedural Grass

```text
https://github.com/57-Studios/GameEnginePackages.git?path=/Unity/Packages/com.57studios.procedural-grass#unity-procedural-grass-v0.1.0
```

### Terrain Prefab Scatter

```text
https://github.com/57-Studios/GameEnginePackages.git?path=/Unity/Packages/com.57studios.terrain-scatter#unity-terrain-scatter-v0.1.0
```

Do not point users at `main`. Use tags. The `?path=` parameter must come before `#tag`.
