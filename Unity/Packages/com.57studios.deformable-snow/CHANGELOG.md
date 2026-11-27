# Changelog

All notable changes to this package are documented in this file.

## [0.1.2] - 2026-11-27

### Fixed

- Declared the built-in Terrain, Terrain Physics, Physics, and IMGUI modules so a clean project can compile the package from a Git URL.

## [0.1.1] - 2026-11-27

### Added

- Assets-installable `.unitypackage` release. Importing it places the package under `Assets/57Studios/DeformableSnow`.

### Fixed

- The appearance material resolves correctly when the package is installed under `Assets/` instead of `Packages/`.

## [0.1.0] - 2026-11-26

### Added

- Initial UPM package migration from the 57 Studios TestBench project.
- Runtime heightfield snow with capture, blur, accumulation, refill, and debug views.
- URP screen-space snow layer with depth-based coverage, relief, slush shading, and detail normals.
- Editor setup window for terrain rigs, appearance, validation, and character footprints.
- EditMode tests for the setup path.
- Package-relative material lookup so the package can be installed outside `Assets/`.
