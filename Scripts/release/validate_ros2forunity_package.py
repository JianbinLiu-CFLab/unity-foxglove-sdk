#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Validate the optional ROS2 For Unity package boundary.
# Usage: python Scripts/release/validate_ros2forunity_package.py
# Inputs: Packages/dev.unity2foxglove.ros2forunity package metadata and compliance manifest.
# Outputs: Prints package boundary checks and exits nonzero on failure.

"""Validate the optional ROS2 For Unity package boundary."""

from __future__ import annotations

import json
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


REPO_ROOT_PARENT_DEPTH = 2
EXIT_SUCCESS = 0
EXIT_FAILURE = 1
MAX_REPORTED_OFFENDERS = 12

ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.ros2forunity"
CORE_PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.sdk"
MANIFEST = PACKAGE / "Compliance" / "ros2-for-unity-adoption-manifest.json"

RUNTIME_BINARY_SUFFIXES = {
    ".dll",
    ".so",
    ".dylib",
    ".zip",
    ".unitypackage",
}

FORBIDDEN_RUNTIME_NAMES = {
    "metadata_ros2cs.xml",
    "metadata_ros2_for_unity.xml",
}


@dataclass
class CheckResult:
    """Structured result for one optional package validation check."""

    name: str
    ok: bool
    detail: str


def rel(path: Path) -> str:
    """Format a path relative to the repository root when possible."""
    try:
        return path.resolve().relative_to(ROOT.resolve()).as_posix()
    except ValueError:
        return str(path)


def add(results: list[CheckResult], name: str, ok: bool, detail: str = "") -> None:
    """Append one check result to the accumulated report."""
    results.append(CheckResult(name, ok, detail))


def iter_files(root: Path) -> Iterable[Path]:
    """Yield regular files below a root, returning an empty iterable when absent."""
    if not root.exists():
        return ()
    return (path for path in root.rglob("*") if path.is_file())


def load_json(path: Path, results: list[CheckResult], name: str) -> dict:
    """Load JSON and record whether parsing succeeded."""
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        add(results, name, False, f"{rel(path)}: {exc}")
        return {}
    add(results, name, True, rel(path))
    return data


def check_package_metadata(results: list[CheckResult]) -> None:
    """Validate the optional Unity package metadata."""
    add(results, "optional package folder exists", PACKAGE.is_dir(), rel(PACKAGE))
    package_json = PACKAGE / "package.json"
    data = load_json(package_json, results, "package.json parses") if package_json.exists() else {}
    if not data:
        return

    expected = {
        "name": "dev.unity2foxglove.ros2forunity",
        "version": "0.1.0-preview.1",
        "displayName": "Unity2Foxglove ROS2 For Unity",
        "license": "Apache-2.0",
        "unity": "6000.0",
    }
    for key, value in expected.items():
        add(results, f"package {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")

    dependencies = data.get("dependencies")
    add(results, "package has no dependencies", dependencies == {}, f"dependencies={dependencies!r}")
    add(results, "package has no samples", "samples" not in data, "samples key absent")


def check_required_files(results: list[CheckResult]) -> None:
    """Validate files required for the Phase107 boundary."""
    required = [
        PACKAGE / "README.md",
        PACKAGE / "LICENSE",
        PACKAGE / "THIRD_PARTY_NOTICES.md",
        PACKAGE / "Upstream" / "LICENSE.AL2",
        MANIFEST,
    ]
    for path in required:
        add(results, f"required file: {path.name}", path.exists(), rel(path))


def check_manifest(results: list[CheckResult]) -> None:
    """Validate the R2FU adoption manifest."""
    if not MANIFEST.exists():
        add(results, "manifest exists", False, rel(MANIFEST))
        return

    data = load_json(MANIFEST, results, "manifest parses")
    if not data:
        return

    expected = {
        "upstreamName": "RobotecAI ROS2 For Unity",
        "upstreamRepository": "https://github.com/RobotecAI/ros2-for-unity",
        "upstreamLicense": "Apache-2.0",
        "upstreamLicenseFile": "Upstream/LICENSE.AL2",
        "adoptedVersion": "1.3.0",
        "releaseAsset": "Ros2ForUnity_humble_standalone_windows11.zip",
        "releaseAssetSha256": "6650D1C68335087143237963E51A87751097FBFF58D6C0A5F6F93D399674D1AF",
        "bundleStatus": "not_bundled",
        "distributionPolicy": "optional_package_boundary_prepared_no_runtime_binaries",
        "knownRuntimeRmw": "rmw_fastrtps_cpp",
        "knownRuntimeRosDistro": "humble",
    }
    for key, value in expected.items():
        add(results, f"manifest {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")

    release_url = str(data.get("releaseAssetUrl", ""))
    add(results, "manifest release asset URL", "Ros2ForUnity_humble_standalone_windows11.zip" in release_url, release_url)

    evidence = str(data.get("phase106Evidence", ""))
    add(
        results,
        "manifest Phase106 evidence",
        "GREEN_WINDOWS_ROS2" in evidence and "BLOCKED_WSL_ROS2_DISCOVERY" in evidence,
        evidence,
    )

    support = str(data.get("upstreamSupportStatus", ""))
    add(
        results,
        "manifest upstream support caveat",
        "AWSIM/Autoware" in support and "general community" in support,
        support,
    )

    jazzy = str(data.get("jazzyInteropStatus", ""))
    add(results, "manifest Jazzy smoke status", "Windows ROS2 Jazzy" in jazzy, jazzy)
    add(results, "manifest modifications empty", data.get("modifications") == [], f"modifications={data.get('modifications')!r}")


def check_text_boundaries(results: list[CheckResult]) -> None:
    """Check README and notices wording for attribution and non-bundling boundaries."""
    readme = (PACKAGE / "README.md").read_text(encoding="utf-8", errors="replace") if (PACKAGE / "README.md").exists() else ""
    notices = (
        (PACKAGE / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8", errors="replace")
        if (PACKAGE / "THIRD_PARTY_NOTICES.md").exists()
        else ""
    )
    combined = readme + "\n" + notices

    add(results, "README says runtime not bundled", "runtime binaries are not bundled" in readme.lower(), rel(PACKAGE / "README.md"))
    add(results, "README says future adapter", "future adapter" in readme.lower(), rel(PACKAGE / "README.md"))
    add(results, "notices attribute R2FU", "RobotecAI ROS2 For Unity" in notices and "Apache-2.0" in notices, rel(PACKAGE / "THIRD_PARTY_NOTICES.md"))
    add(results, "notices attribute ros2cs", "ros2cs" in notices, rel(PACKAGE / "THIRD_PARTY_NOTICES.md"))
    add(results, "notices preserve support caveat", "AWSIM/Autoware" in combined and "general community" in combined, "support caveat")
    add(results, "notices require future inventory", "complete transitive inventory" in combined, "future binary bundling boundary")


def check_no_runtime_artifacts(results: list[CheckResult]) -> None:
    """Reject runtime binaries and imported R2FU artifacts while the manifest says not_bundled."""
    offenders: list[str] = []
    for path in iter_files(PACKAGE):
        if path.suffix.lower() in RUNTIME_BINARY_SUFFIXES or path.name in FORBIDDEN_RUNTIME_NAMES:
            offenders.append(rel(path))

    add(
        results,
        "optional package has no runtime binaries",
        not offenders,
        "; ".join(offenders[:MAX_REPORTED_OFFENDERS]) if offenders else "no runtime binaries found",
    )

    forbidden_dirs = [PACKAGE / "Runtime", PACKAGE / "Editor", PACKAGE / "Samples~"]
    existing = [rel(path) for path in forbidden_dirs if path.exists()]
    add(results, "optional package has no adapter directories", not existing, "; ".join(existing) if existing else "no adapter directories")


def check_core_boundary(results: list[CheckResult]) -> None:
    """Confirm the core package does not depend on the optional R2FU package."""
    core_json = CORE_PACKAGE / "package.json"
    data = load_json(core_json, results, "core package.json parses") if core_json.exists() else {}
    dependencies = data.get("dependencies", {}) if isinstance(data, dict) else {}
    add(
        results,
        "core package does not depend on optional package",
        isinstance(dependencies, dict) and "dev.unity2foxglove.ros2forunity" not in dependencies,
        f"dependencies={dependencies!r}",
    )


def print_results(results: list[CheckResult]) -> None:
    """Print check results as aligned PASS/FAIL lines."""
    name_width = max(len(result.name) for result in results) if results else 0
    for result in results:
        status = "PASS" if result.ok else "FAIL"
        print(f"[{status}] {result.name:<{name_width}}  {result.detail}")


def main() -> int:
    """Run optional package validation and return a process exit code."""
    results: list[CheckResult] = []
    check_package_metadata(results)
    check_required_files(results)
    check_manifest(results)
    check_text_boundaries(results)
    check_no_runtime_artifacts(results)
    check_core_boundary(results)

    print_results(results)
    failed = [result for result in results if not result.ok]
    if failed:
        print(f"\nvalidate_ros2forunity_package: {len(failed)} check(s) failed.", file=sys.stderr)
        return EXIT_FAILURE

    print(f"\nvalidate_ros2forunity_package: {len(results)} check(s) passed.")
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main())
