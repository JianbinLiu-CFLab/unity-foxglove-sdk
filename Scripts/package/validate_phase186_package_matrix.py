#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Compile and boundary-check all Phase186 Unity package combinations."""

from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MATRIX_ROOT = (
    ROOT
    / "Packages/dev.unity2foxglove.sdk/Tests/PackageMatrix"
)
PROJECTS = (
    ("sdk-only", "Unity2Foxglove.SdkOnly.Compile.csproj"),
    ("sdk-r2fu", "Unity2Foxglove.SdkR2fu.Compile.csproj"),
    ("sdk-bridge", "Unity2Foxglove.SdkBridge.Compile.csproj"),
    ("all-providers", "Unity2Foxglove.AllProviders.Compile.csproj"),
)
REPORT = ROOT / "build/phase186/package-matrix/report.json"
GUID_PATTERN = re.compile(r"(?mi)^guid:\s*([0-9a-f]{32})\s*$")


def fail(message: str) -> int:
    """Report one stable package-matrix failure."""
    print(f"[FAIL] {message}", file=sys.stderr)
    return 1


def load_json(path: Path) -> object:
    """Load one UTF-8 JSON descriptor."""
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


PACKAGE_MATRIX_SPECS = (
    (
        "sdk",
        "Packages/dev.unity2foxglove.sdk",
        "dev.unity2foxglove.sdk",
        "1.9.6",
        {
            "com.unity.nuget.newtonsoft-json": "3.2.1",
            "com.unity.burst": "1.8.18",
            "com.unity.collections": "2.5.5",
            "com.unity.mathematics": "1.3.2",
        },
    ),
    (
        "r2fu",
        "Packages/dev.unity2foxglove.ros2forunity",
        "dev.unity2foxglove.ros2forunity",
        "0.1.0-preview.1",
        {"dev.unity2foxglove.sdk": "1.9.6"},
    ),
    (
        "bridge",
        "Packages/dev.unity2foxglove.ros2bridge",
        "dev.unity2foxglove.ros2bridge",
        "0.1.0-preview.1",
        {"dev.unity2foxglove.sdk": "1.9.6"},
    ),
    (
        "remote_gateway",
        "Packages/dev.unity2foxglove.remotegateway.win64",
        "dev.unity2foxglove.remotegateway.win64",
        "0.1.0-preview.1",
        {"dev.unity2foxglove.sdk": "1.9.6"},
    ),
)


def _package_matrix_paths() -> dict[str, Path]:
    """Resolve package roots from the patchable repository root."""
    return {
        key: ROOT / relative_path
        for key, relative_path, _name, _version, _dependencies in PACKAGE_MATRIX_SPECS
    }


def validate_package_matrix() -> dict[str, dict]:
    """Authenticate identity, version, and exact dependencies for every package."""
    packages: dict[str, dict] = {}
    paths = _package_matrix_paths()
    for key, _relative_path, expected_name, expected_version, expected_dependencies in PACKAGE_MATRIX_SPECS:
        manifest = paths[key] / "package.json"
        try:
            data = load_json(manifest)
        except (OSError, TypeError, ValueError, json.JSONDecodeError) as exc:
            raise RuntimeError(
                f"package matrix {key} manifest: {manifest}: {exc}"
            ) from exc
        if not isinstance(data, dict):
            raise RuntimeError(
                f"package matrix {key} manifest: expected JSON object, got {type(data).__name__}"
            )
        if data.get("name") != expected_name:
            raise RuntimeError(
                f"package matrix {key} name: expected {expected_name!r}, got {data.get('name')!r}"
            )
        if data.get("version") != expected_version:
            raise RuntimeError(
                f"package matrix {key} version: expected {expected_version!r}, got {data.get('version')!r}"
            )
        dependencies = data.get("dependencies")
        if dependencies != expected_dependencies:
            raise RuntimeError(
                f"package matrix {key} dependencies: expected {expected_dependencies!r}, got {dependencies!r}"
            )
        packages[key] = data
    return packages


def compile_matrix() -> list[dict]:
    """Compile every supported Phase186 package composition."""
    results: list[dict] = []
    for name, project_name in PROJECTS:
        project = MATRIX_ROOT / project_name
        completed = subprocess.run(
            [
                "dotnet",
                "build",
                str(project),
                "--nologo",
                "--verbosity",
                "quiet",
            ],
            cwd=ROOT,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
        )
        results.append(
            {
                "name": name,
                "project": project.relative_to(ROOT).as_posix(),
                "exitCode": completed.returncode,
                "output": completed.stdout,
            }
        )
        if completed.returncode == 0:
            print(f"[PASS] {name} compile gate")
        else:
            print(f"[FAIL] {name} compile gate", file=sys.stderr)
    return results


def _is_assembly_or_child(value: object, assembly_name: str) -> bool:
    """Return whether a name is the forbidden assembly or one of its children."""
    if not isinstance(value, str):
        return False
    normalized = value.casefold()
    root = assembly_name.casefold()
    return normalized == root or normalized.startswith(root + ".")


def _assembly_guids(package_root: Path, assembly_name: str) -> set[str]:
    """Return GUIDs owned by a named assembly and its child assemblies.

    Unity resolves an assembly-reference GUID across the package graph.
    Runtime child assemblies are deliberately shipped in sibling UPM packages,
    so restricting the lookup to ``package_root`` would make a sibling-owned
    reference invisible.  Search the named package and its declared R2FU
    runtime siblings in deterministic order; unrelated packages are not part
    of this ownership boundary.
    """
    result: set[str] = set()
    search_roots = [package_root]
    package_parent = package_root.parent
    if package_parent.is_dir():
        runtime_prefix = package_root.name.casefold() + ".runtime."
        search_roots.extend(
            child
            for child in sorted(package_parent.iterdir(), key=lambda path: path.as_posix().casefold())
            if (
                child.is_dir()
                and child != package_root
                and child.name.casefold().startswith(runtime_prefix)
            )
        )
    for search_root in search_roots:
        for asmdef in sorted(search_root.rglob("*.asmdef"), key=lambda path: path.as_posix().casefold()):
            descriptor = load_json(asmdef)
            if not _is_assembly_or_child(descriptor.get("name"), assembly_name):
                continue
            meta = Path(str(asmdef) + ".meta")
            if not meta.is_file():
                raise RuntimeError(f"assembly definition meta is missing: {meta}")
            match = GUID_PATTERN.search(meta.read_text(encoding="utf-8"))
            if match is None:
                raise RuntimeError(f"assembly definition GUID is missing: {meta}")
            result.add(match.group(1).lower())
    if not result:
        raise RuntimeError(
            f"forbidden assembly definition is missing: {assembly_name}"
        )
    return result


def _references_forbidden_assembly(
    descriptor_path: Path,
    assembly_name: str,
    forbidden_root: Path,
) -> bool:
    """Detect name- and GUID-form references to one forbidden assembly."""
    descriptor = load_json(descriptor_path)
    values = descriptor.get("references", [])
    if descriptor_path.suffix == ".asmref":
        values = [descriptor.get("reference")]
    if not isinstance(values, list):
        raise RuntimeError(f"invalid references collection: {descriptor_path}")
    references = [value for value in values if isinstance(value, str)]
    if any(_is_assembly_or_child(value, assembly_name) for value in references):
        return True
    guid_references = {
        value[5:].lower()
        for value in references
        if value.lower().startswith("guid:")
    }
    if not guid_references:
        return False
    return bool(guid_references & _assembly_guids(forbidden_root, assembly_name))


def validate_boundaries() -> list[str]:
    """Validate package dependencies, asmdefs, and analyzer ownership."""
    packages = validate_package_matrix()
    paths = _package_matrix_paths()
    sdk = paths["sdk"]
    r2fu = paths["r2fu"]
    bridge = paths["bridge"]
    if "dev.unity2foxglove.ros2bridge" in packages["sdk"].get(
        "dependencies", {}
    ):
        raise RuntimeError("SDK package depends back on Bridge")
    if "dev.unity2foxglove.ros2bridge" in packages["r2fu"].get(
        "dependencies", {}
    ):
        raise RuntimeError("R2FU package depends on Bridge")

    forbidden_by_root = (
        (sdk, bridge, "Unity2Foxglove.Ros2Bridge"),
        (r2fu, bridge, "Unity2Foxglove.Ros2Bridge"),
        (bridge, r2fu, "Unity2Foxglove.Ros2ForUnity"),
    )
    checked: list[str] = []
    for package_root, forbidden_root, forbidden in forbidden_by_root:
        descriptors = tuple(package_root.rglob("*.asmdef")) + tuple(
            package_root.rglob("*.asmref")
        )
        for descriptor in descriptors:
            if _references_forbidden_assembly(
                descriptor,
                forbidden,
                forbidden_root,
            ):
                raise RuntimeError(
                    f"{descriptor.relative_to(ROOT)} references {forbidden}"
                )
            checked.append(descriptor.relative_to(ROOT).as_posix())

    analyzer_specs = (
        (
            sdk,
            "FoxgloveLogSourceGenerator.dll",
            "FoxgloveLogSourceGenerator.csproj",
        ),
        (
            r2fu,
            "Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll",
            "FoxRunR2fuSourceGenerator.csproj",
        ),
        (
            bridge,
            "Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll",
            "FoxRunBridgeSourceGenerator.csproj",
        ),
    )
    for package_root, dll_name, project_name in analyzer_specs:
        generator_root = package_root / "Editor/SourceGenerators"
        dll = generator_root / "analyzers/dotnet/cs" / dll_name
        project = generator_root / project_name
        if not dll.is_file() or not (Path(str(dll) + ".meta")).is_file():
            raise RuntimeError(f"controlled analyzer is incomplete: {dll}")
        if not project.is_file():
            raise RuntimeError(f"analyzer project is missing: {project}")
        checked.extend(
            [
                dll.relative_to(ROOT).as_posix(),
                project.relative_to(ROOT).as_posix(),
            ]
        )
    return sorted(set(checked))


def main() -> int:
    """Run the package matrix and write its deterministic report."""
    compile_results: list[dict] = []
    boundary_paths: list[str] = []
    failures: list[str] = []
    try:
        compile_results = compile_matrix()
        failed_gates = [
            result for result in compile_results
            if result["exitCode"] != 0
        ]
        failures.extend(
            f"{result['name']} compile failed\n{result['output']}"
            for result in failed_gates
        )
    except (OSError, RuntimeError, ValueError) as exc:
        failures.append(str(exc))
    try:
        boundary_paths = validate_boundaries()
    except (OSError, RuntimeError, ValueError, json.JSONDecodeError) as exc:
        failures.append(str(exc))

    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(
        json.dumps(
            {
                "phase": "186A",
                "verdict": "FAIL" if failures else "PASS",
                "compileGates": compile_results,
                "boundaryPaths": boundary_paths,
                "failures": failures,
            },
            indent=2,
            sort_keys=True,
        )
        + "\n",
        encoding="utf-8",
    )
    if failures:
        return fail("\n".join(failures))
    print(
        "[PASS] Phase186 package matrix and optional-package boundaries "
        f"({REPORT.relative_to(ROOT).as_posix()})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
