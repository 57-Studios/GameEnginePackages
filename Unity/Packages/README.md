# Unity packages

Each immediate subfolder is a UPM package.

- Folder name must exactly match the package's `package.json` `name`.
- Package IDs use `com.57studios.<name>`.
- Each package must contain `package.json`, `README.md`, `CHANGELOG.md`, `LICENSE.md`, and the appropriate `Runtime/`, `Editor/`, and `Tests/` folders.
- Documentation and samples use the tilde suffix: `Documentation~`, `Samples~`.
- Never commit `Library/`, `Temp/`, `obj/`, or nested `.git` folders here.

See [`../../AGENTS.md`](../../AGENTS.md) before adding or changing a package.
