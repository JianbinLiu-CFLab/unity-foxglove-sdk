#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Compile and boundary-check all Phase186 Unity package combinations."""

from __future__ import annotations

import json
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


def fail(message: str) -> int:
    """Report one stable package-matrix failure."""
    print(f"[FAIL] {message}", file=sys.stderr)
    return 1


def load_json(path: Path) -> dict:
    """Load one UTF-8 JSON package descriptor."""
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


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
        if completed.returncode != 0:
            raise RuntimeError(
                f"{name} compile failed\n{completed.stdout}"
            )
        print(f"[PASS] {name} compile gate")
    return results


def validate_boundaries() -> list[str]:
    """Validate package dependencies, asmdefs, and analyzer ownership."""
    sdk = ROOT / "Packages/dev.unity2foxglove.sdk"
    r2fu = ROOT / "Packages/dev.unity2foxglove.ros2forunity"
    bridge = ROOT / "Packages/dev.unity2foxglove.ros2bridge"
    packages = {
        "sdk": load_json(sdk / "package.json"),
        "r2fu": load_json(r2fu / "package.json"),
        "bridge": load_json(bridge / "package.json"),
    }
    if packages["bridge"].get("name") != "dev.unity2foxglove.ros2bridge":
        raise RuntimeError("Bridge package ID is not locked")
    if packages["bridge"].get("version") != "0.1.0-preview.1":
        raise RuntimeError("Bridge package version is not locked")
    bridge_dependencies = packages["bridge"].get("dependencies", {})
    if set(bridge_dependencies) != {"dev.unity2foxglove.sdk"}:
        raise RuntimeError(
            "Bridge package must depend directly and only on the SDK"
        )
    if "dev.unity2foxglove.ros2bridge" in packages["sdk"].get(
        "dependencies", {}
    ):
        raise RuntimeError("SDK package depends back on Bridge")
    if "dev.unity2foxglove.ros2bridge" in packages["r2fu"].get(
        "dependencies", {}
    ):
        raise RuntimeError("R2FU package depends on Bridge")

    forbidden_by_root = (
        (sdk, "Unity2Foxglove.Ros2Bridge"),
        (r2fu, "Unity2Foxglove.Ros2Bridge"),
        (bridge, "Unity2Foxglove.Ros2ForUnity"),
    )
    checked: list[str] = []
    for package_root, forbidden in forbidden_by_root:
        for asmdef in package_root.rglob("*.asmdef"):
            text = asmdef.read_text(encoding="utf-8")
            if forbidden.lower() in text.lower():
                raise RuntimeError(
                    f"{asmdef.relative_to(ROOT)} references {forbidden}"
                )
            checked.append(asmdef.relative_to(ROOT).as_posix())

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
    try:
        compile_results = compile_matrix()
        boundary_paths = validate_boundaries()
    except (OSError, RuntimeError, ValueError, json.JSONDecodeError) as exc:
        return fail(str(exc))

    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.write_text(
        json.dumps(
            {
                "phase": "186A",
                "verdict": "PASS",
                "compileGates": compile_results,
                "boundaryPaths": boundary_paths,
            },
            indent=2,
            sort_keys=True,
        )
        + "\n",
        encoding="utf-8",
    )
    print(
        "[PASS] Phase186 package matrix and optional-package boundaries "
        f"({REPORT.relative_to(ROOT).as_posix()})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
