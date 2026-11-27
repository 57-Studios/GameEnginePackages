#!/usr/bin/env python3
"""Build Assets-installable .unitypackage files from UPM packages.

Unity's Package Manager installs UPM packages into Packages/, not Assets/.
This script exports each package from the current project, rewrites the
embedded pathnames from Packages/<id> to Assets/57Studios/<name>, and writes
a .unitypackage that imports into the Assets folder.
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import tarfile
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent

PACKAGES = [
    {
        "id": "com.57studios.deformable-snow",
        "name": "DeformableSnow",
        # Keep the editor-only PlayMode harness: the editor assembly references it.
        "exclude": ["Tests/Editor"],
    },
    {
        "id": "com.57studios.procedural-grass",
        "name": "ProceduralGrass",
        "exclude": ["Tests"],
    },
    {
        "id": "com.57studios.terrain-scatter",
        "name": "TerrainScatter",
        "exclude": ["Tests"],
    },
]


def run_export(unity: str, project: Path, package_id: str, output: Path) -> None:
    output.unlink(missing_ok=True)
    code = f"""
var path = {json.dumps(str(output))};
System.IO.File.Delete(path);
AssetDatabase.ExportPackage(
    new[] {{ {json.dumps(f"Packages/{package_id}")} }},
    path,
    ExportPackageOptions.Recurse);
return System.IO.File.Exists(path) ? new System.IO.FileInfo(path).Length : -1;
"""
    result = subprocess.run(
        [unity, "command", "--project-path", str(project), "script", "eval", code],
        check=False,
        text=True,
        capture_output=True,
    )
    if result.returncode != 0:
        raise RuntimeError(
            f"Unity export failed for {package_id}\n"
            f"stdout:\n{result.stdout}\n"
            f"stderr:\n{result.stderr}"
        )
    if not output.exists() or output.stat().st_size == 0:
        raise RuntimeError(f"Unity did not create {output}")


def rewrite_package(
    source: Path,
    output: Path,
    package_id: str,
    target_name: str,
    excludes: list[str],
) -> int:
    source_prefix = f"Packages/{package_id}"
    target_prefix = f"Assets/57Studios/{target_name}"
    written = 0

    with tempfile.TemporaryDirectory(prefix="57studios-unitypackage-") as tmp:
        tmp_path = Path(tmp)
        with tarfile.open(source, "r:gz") as archive:
            archive.extractall(tmp_path)

        with tarfile.open(output, "w:gz", format=tarfile.GNU_FORMAT) as archive:
            for guid_dir in sorted(tmp_path.iterdir()):
                if not guid_dir.is_dir():
                    continue

                pathname_file = guid_dir / "pathname"
                if not pathname_file.exists():
                    continue

                pathname = pathname_file.read_text()
                if not pathname.startswith(source_prefix):
                    continue

                relative = pathname[len(source_prefix):].lstrip("/")
                if any(
                    relative == excluded or relative.startswith(excluded + "/")
                    for excluded in excludes
                ):
                    continue

                pathname_file.write_text(
                    target_prefix if not relative else f"{target_prefix}/{relative}"
                )

                for file in sorted(guid_dir.rglob("*")):
                    if file.is_file():
                        archive.add(
                            file,
                            arcname=str(file.relative_to(tmp_path)),
                            recursive=False,
                        )
                written += 1

    return written


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--project",
        default="/mnt/gaming/Unity/UnityProjects/TestBench",
        help="Unity project that can resolve the packages from Packages/",
    )
    parser.add_argument(
        "--out",
        default="dist",
        help="Output folder for .unitypackage files",
    )
    parser.add_argument("--unity", default="unity", help="Unity CLI executable")
    args = parser.parse_args()

    project = Path(args.project).expanduser().resolve()
    out_dir = Path(args.out).expanduser().resolve()
    out_dir.mkdir(parents=True, exist_ok=True)

    with tempfile.TemporaryDirectory(prefix="57studios-export-") as tmp:
        tmp_path = Path(tmp)
        for package in PACKAGES:
            package_id = package["id"]
            name = package["name"]
            manifest = REPO_ROOT / "Unity/Packages" / package_id / "package.json"
            version = json.loads(manifest.read_text())["version"]
            print(f"Exporting {package_id} {version} ...")
            raw = tmp_path / f"{package_id}.unitypackage"
            run_export(args.unity, project, package_id, raw)

            output = out_dir / f"{name}-v{version}.unitypackage"
            written = rewrite_package(
                raw,
                output,
                package_id,
                name,
                package["exclude"],
            )
            print(f"  {output} ({output.stat().st_size} bytes, {written} assets)")


if __name__ == "__main__":
    main()
