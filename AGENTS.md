# AGENTS.md — 57 Studios Labs Game Engine Packages

This file is the operating contract for anyone — human or agent — working in this repository.

## 1. What this repository is

This repository is the **canonical source of truth** for 57 Studios Labs game-engine packages.

- Unity packages live under `Unity/Packages/`.
- Unreal plugins will live under `Unreal/Plugins/`.
- Godot addons will live under `Godot/addons/`.
- Store builds, tarballs, and `.unitypackage` files are **generated artifacts**, not source.

A Unity project is an editor and test host. It is **not** the source of truth for a package.

## 2. The source-of-truth rule

**Do not maintain two editable copies of a package.**

The normal loop is:

```text
repo package folder          ← source of truth
        ↓ file: or symlink
Unity project                ← editor/test host
        ↓ build
store artifact               ← generated copy
```

Not:

```text
Unity project                ← source of truth
        ↓ copy every change
repo                         ← stale mirror
```

Rules:

1. Create and edit packages in this repository.
2. Point Unity projects at the repository package with a local `file:` dependency or a symlink/junction.
3. Commit from this repository.
4. Copy **from** this repository **to** a release/staging artifact only when publishing.
5. Copying a package **out of** a Unity project into this repository is allowed only as a one-time migration.

If a package already exists inside a Unity project, migrate it once, then remove the old `Assets/` copy and make the project consume the repository package.

## 3. Repository layout

```text
GameEnginePackages/
├── AGENTS.md
├── README.md
├── Unity/
│   └── Packages/
│       └── com.57studios.<name>/
│           ├── package.json
│           ├── README.md
│           ├── CHANGELOG.md
│           ├── LICENSE.md
│           ├── Runtime/
│           ├── Editor/
│           ├── Tests/
│           ├── Documentation~/
│           └── Samples~/
├── Unreal/
│   └── Plugins/
├── Godot/
│   └── addons/
└── tools/
```

- A package folder name **must** exactly match the `name` field in its `package.json`.
- Do not put package source under `Assets/` in this repository.
- Do not add a nested `.git` directory inside a package folder.
- Add engine folders only when the first package for that engine exists.

## 4. Unity package contract

Every Unity package must be a self-contained UPM package.

### Required files

- `package.json`
- `README.md`
- `CHANGELOG.md`
- `LICENSE.md` (or `LICENSE` plus a `license` field in `package.json`)
- Assembly definitions for runtime and editor code
- Tests under `Tests/` with a test assembly definition
- All `.meta` files committed

### Package manifest template

```json
{
  "name": "com.57studios.example",
  "version": "0.1.0",
  "displayName": "57 Studios Example",
  "description": "What this package does and which render pipeline it supports.",
  "unity": "6000.3",
  "author": {
    "name": "57 Studios Labs"
  },
  "keywords": [],
  "license": "See LICENSE.md"
}
```

Rules:

- `name` must be lowercase reverse-DNS: `com.57studios.<name>`.
- Do not use `com.unity` or the word `unity` in the technical name.
- `version` must be SemVer.
- `unity` is the **lowest tested Unity version**, not the version you happen to use.
- Declare real package dependencies in `package.json`. Unity supports SemVer versions, not version ranges.
- Do **not** put Git URLs in a package's `dependencies`. Unity only supports Git dependencies in a project's `manifest.json`.
- If a package requires URP, declare the URP package dependency or clearly document the requirement and validate it.

### Source layout

```text
com.57studios.example/
├── package.json
├── README.md
├── CHANGELOG.md
├── LICENSE.md
├── Runtime/
│   ├── Studios57.Example.Runtime.asmdef
│   └── ...
├── Editor/
│   ├── Studios57.Example.Editor.asmdef
│   └── ...
├── Tests/
│   └── Editor/
│       ├── Studios57.Example.Editor.Tests.asmdef
│       └── ...
├── Documentation~/
└── Samples~/
```

- Use `Documentation~` and `Samples~` with the trailing tilde so Unity does not import those folders into consumer projects.
- Test-only MonoBehaviours and test walkers belong under `Tests/` or `Samples~`, **never** in `Runtime/`, unless they are deliberately part of the public runtime API.
- Editor-only code must live under `Editor/` with an editor-only asmdef.
- Runtime asmdefs must not reference editor assemblies.
- Do not add `link.xml` inside the package and assume it works. Unity ignores `link.xml` in packages; document any project-side linker requirements.

## 5. Working in any Unity project

### Preferred: local `file:` dependency

Add the package to the project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.57studios.example": "file:../../GameEnginePackages/Unity/Packages/com.57studios.example"
  }
}
```

The path is relative to the project's `Packages` folder.

Use a relative path when the checkout layout is stable. Do not commit a machine-specific absolute path to a shared project.

### Alternative: symlink or junction

```bash
ln -s /path/to/GameEnginePackages/Unity/Packages/com.57studios.example \
  /path/to/MyGame/Packages/com.57studios.example
```

- Symlink the **individual package folder**, not the whole repository.
- Put it under `Packages/`, not `Assets/`.
- Do not commit a symlink whose target is machine-specific.
- On Windows, use a junction or Developer Mode symlink.
- If symlinks are unreliable in the project's Unity version, use the `file:` manifest path instead.

### Developer vs consumer

- Projects actively developing a package may use a local `file:` reference or symlink.
- Normal consumer projects must install a released tag from Git or a registry.
- Do not make every game project a package-development environment.

Example consumer install:

```text
https://github.com/57Studios/GameEnginePackages.git?path=/Unity/Packages/com.57studios.example#unity-example-v1.0.0
```

The `?path=` parameter must come before `#tag`.

## 6. Migrating an existing package into this repository

This is a one-time operation, not the daily workflow.

1. Create the target folder:
   `Unity/Packages/com.57studios.<name>/`
2. Copy the package source with its `.meta` files.
3. Exclude:
   - `.git/`
   - `Library/`
   - `Temp/`
   - `obj/`
   - `Logs/`
   - `UserSettings/`
   - `*.csproj`
   - `*.sln`
   - generated build output
4. Add `package.json`, `README.md`, `CHANGELOG.md`, and `LICENSE.md`.
5. Rename `Documentation/` to `Documentation~/` and update links.
6. Move test harness code out of `Runtime/Testing/` into `Tests/` or `Samples~/`.
7. Replace hardcoded `Assets/57Studios/...` paths with package-relative lookups.
8. Remove any nested `.git` directory after confirming it has no history worth preserving.
9. In the Unity project, remove the old `Assets/57Studios/<Package>/` copy.
10. Add the local package reference described in section 5.
11. Let Unity reimport, then run the package tests and a clean compile.
12. Commit the repository package and the project reference change separately.

After migration, the `Assets/57Studios/<Package>/` copy must not come back.

## 7. Current packages

As of **2026-11-26**, three packages have been migrated into this repository and TestBench consumes them as local `file:` packages.

| Package | Package ID | Path | License | Status |
|---|---|---|---|---|
| Deformable Snow | `com.57studios.deformable-snow` | `Unity/Packages/com.57studios.deformable-snow` | MIT | Migrated. 16 EditMode tests pass. Test harness is editor-only under `Tests/PlayMode`. |
| Procedural Grass | `com.57studios.procedural-grass` | `Unity/Packages/com.57studios.procedural-grass` | Proprietary | Migrated. 1 EditMode smoke test passes. Still a Phase 1 debug renderer. |
| Terrain Prefab Scatter | `com.57studios.terrain-scatter` | `Unity/Packages/com.57studios.terrain-scatter` | Proprietary | Migrated. 2 EditMode tests pass. |

Additional facts:

- TestBench is the current integration project. It references the packages through `Packages/manifest.json` and lists all three in `testables`.
- The old `Assets/57Studios` copy has been removed; do not recreate it.
- The current development baseline is Unity 6000.3.14f1 with URP 17.3.0.
- Existing package documentation uses `Documentation~/DEVELOPMENT.md` as a living handoff record.
- Deformable Snow's editor-only test harness assembly uses `UNITY_EDITOR` as a define constraint so the walker and performance probe cannot enter a player build.

## 8. Code and asset rules

- Do not modify `Library/PackageCache`, third-party assets, or imported asset-store content to make a package work.
- Do not create a GameObject or Transform per grass blade or per generated decoration.
- Do not perform GPU-to-CPU readback in a per-frame render path.
- Do not hardcode `Assets/57Studios/...` paths. Resolve package assets with `PackageInfo.FindForAssembly`, `AssetDatabase.FindAssets`, or an explicit serialized reference.
- Do not run destructive project-wide changes, scene saves, or asset moves without explicit approval.
- Do not auto-modify project settings, renderer assets, layers, tags, or input settings from an import hook. Provide explicit editor tooling instead.
- Keep generated screenshots and benchmark captures in `Documentation~`, not in the runtime package root.
- Commit `.meta` files for every asset and folder. Never add `*.meta` to `.gitignore`.
- Do not commit `Library/`, `Temp/`, `obj/`, `Logs/`, `UserSettings/`, IDE project files, or build outputs.

### Validation expectations

For rendering or performance work:

- Record the Unity version, render pipeline, device, resolution, and scene.
- Record a baseline before claiming an optimization.
- Update the package's `Documentation~/DEVELOPMENT.md` with a dated entry covering:
  1. what changed and exact paths
  2. why the decision was made
  3. environment
  4. validation results and screenshots
  5. known limitations and the next smallest safe task
- Never claim a performance improvement without a comparable before/after measurement.

## 9. Versioning and releases

- Version each package independently with SemVer.
- Tags are immutable. Never move or delete a published tag.
- Tag format:

```text
unity-<short-name>-v<major>.<minor>.<patch>
```

Example:

```text
unity-deformable-snow-v0.1.0
```

- The tag must match the package's `package.json` version.
- Update `CHANGELOG.md` before tagging.
- Keep `main` as the integration branch.
- Use short-lived feature branches, not long-lived branches per package.
- Use a release branch only when a generated packaging artifact genuinely needs one. Do not develop features on it.
- Do not create tags, releases, or publish packages unless explicitly asked.

## 10. Asset Store and other distribution

### Asset Store `.unitypackage`

- Build from a tagged repository state.
- Put all package assets under a top-level folder whose name matches the package.
- Validate with the Asset Store Publishing Tools before upload.
- Do not upload the development project, tests, benchmark scenes, or internal documentation.
- Do not commit the generated `.unitypackage` unless explicitly requested; attach it to a release.

### Asset Store UPM product

- The Asset Store UPM format supports versioning, dependencies, immutable packages, and multiple packages per product.
- Use the Asset Store Publisher Portal technical names exactly:
  - single-package product: `com.57studios.<product>`
  - multi-package product: `com.57studios.<product>.<package>`
- The package folder and `package.json` name must match the reserved technical name.
- A dependency shared by multiple products should be its own product, not an internal folder only.
- Do not give a public Git URL for paid content. Use Asset Store UPM or a private registry so entitlement is enforced.
- For assets that need write access to their own package folder, installers, or self-modifying import steps, use `.unitypackage`, not an immutable UPM package.

### Git URL / registry

- Use GitHub tags for free/open-source packages.
- Use OpenUPM or another registry when transitive dependencies or a cleaner install experience are needed.
- Public Git URLs are not a storefront and do not enforce Asset Store entitlement.

## 11. Agent checklist

Before editing:

1. Read this file and the target package's `README.md` and `Documentation~/DEVELOPMENT.md`.
2. Run `git status` and confirm you are not about to overwrite unrelated work.
3. Confirm whether the package is already migrated or still lives in `Assets/57Studios`.
4. If testing in Unity, confirm the project is pointing at the repository package, not a duplicate `Assets/` copy.

While editing:

1. Work in `Unity/Packages/<package-id>/`.
2. Preserve `.meta` files and GUIDs.
3. Keep runtime, editor, tests, samples, and docs in their proper folders.
4. Do not add nested Git repositories or package caches.
5. Do not copy files back into a Unity project's `Assets/` folder.

Before finishing:

1. Run the package tests and a clean compile.
2. Check for hardcoded `Assets/57Studios` paths.
3. Check that test-only code is not in `Runtime/`.
4. Update `CHANGELOG.md` if the change is user-facing.
5. Update `Documentation~/DEVELOPMENT.md` with a dated entry.
6. Confirm no generated artifacts or machine-specific paths are staged.
7. Report exactly what changed, what was tested, and what remains.

## 12. Do not

- Do not maintain a second editable copy in a game project.
- Do not use one long-lived Git branch per package.
- Do not commit `Library/`, `Temp/`, `obj/`, `Logs/`, `UserSettings/`, or IDE files.
- Do not ignore `.meta` files.
- Do not edit `Library/PackageCache`.
- Do not put Git URLs in a package's `package.json` dependencies.
- Do not ship test harnesses or benchmark-only scripts in `Runtime/`.
- Do not hardcode `Assets/57Studios/...` paths.
- Do not move, delete, or overwrite user work without explicit confirmation.
- Do not create tags, releases, or store uploads unless asked.
