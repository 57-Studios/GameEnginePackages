# 57 Studios Labs — Game Engine Packages

Canonical source for 57 Studios Labs packages.

- **Workflow and rules:** [`AGENTS.md`](AGENTS.md)
- **Unity packages:** [`Unity/Packages/`](Unity/Packages/)
- **Unreal plugins:** `Unreal/Plugins/` (when the first plugin lands)
- **Godot addons:** `Godot/addons/` (when the first addon lands)
- **Build and release tooling:** `tools/`

## Current state

The first three Unity packages are migrated and are consumed locally by the TestBench project:

- `com.57studios.deformable-snow` — MIT, 16 EditMode tests passing
- `com.57studios.procedural-grass` — proprietary, 1 EditMode smoke test passing
- `com.57studios.terrain-scatter` — proprietary, 2 EditMode tests passing

The old `Assets/57Studios` copies have been removed. TestBench references the repository packages through its `Packages/manifest.json`.

## Install pattern

Each package will be installable from a tagged release:

```text
https://github.com/57Studios/GameEnginePackages.git?path=/Unity/Packages/com.57studios.<name>#unity-<name>-vX.Y.Z
```

Do not point users at `main`. Use tags.
