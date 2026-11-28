# Changelog

All notable changes to this package are documented in this file.

## [0.1.3] - 2026-11-28

### Changed

- Package documentation is now tracked and included in the source tag.

## [0.1.2] - 2026-11-27

### Fixed

- Declared the built-in Terrain module so a clean project can compile the package from a Git URL.

## [0.1.1] - 2026-11-27

### Added

- Assets-installable `.unitypackage` release. Importing it places the package under `Assets/57Studios/ProceduralGrass`.

## [0.1.0] - 2026-11-26

### Changed

- Relicensed under MIT for public distribution.

### Added

- Initial UPM package migration from the 57 Studios TestBench project.
- Compute-shader terrain grass placement with TerrainLayer weight sampling.
- GPU compaction and indirect draw submission.
- Debug blade shader and runtime renderer.
- EditMode smoke test for the runtime component.
- Package README, license, and package manifest.
