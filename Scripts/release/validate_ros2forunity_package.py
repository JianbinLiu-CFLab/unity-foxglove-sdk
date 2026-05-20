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
SAMPLE = PACKAGE / "Samples~" / "ROS2 For Unity External Adapter"

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

ALLOWED_RUNTIME_SUFFIXES = {
    ".cs",
    ".asmdef",
    ".meta",
}

ALLOWED_SAMPLE_SUFFIXES = {
    ".cs",
    ".md",
    ".meta",
}

FORBIDDEN_RUNTIME_TOKENS = (
    "using ROS2;",
    "namespace ROS2",
    "ROS2UnityComponent",
    "ROS2Node",
    "IPublisher<",
    "ISubscription<",
    "std_msgs",
    "ros2cs",
)


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

    samples = data.get("samples")
    add(results, "package has one external adapter sample", isinstance(samples, list) and len(samples) == 1, f"samples={samples!r}")
    if isinstance(samples, list) and samples:
        sample = samples[0]
        add(
            results,
            "sample displayName",
            sample.get("displayName") == "ROS2 For Unity External Adapter",
            f"displayName={sample.get('displayName')!r}",
        )
        add(
            results,
            "sample path",
            sample.get("path") == "Samples~/ROS2 For Unity External Adapter",
            f"path={sample.get('path')!r}",
        )
        description = str(sample.get("description", ""))
        add(
            results,
            "sample description names external R2FU",
            "externally imported ROS2 For Unity" in description,
            description,
        )


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
        "schemaVersion": 2,
        "upstreamName": "RobotecAI ROS2 For Unity",
        "upstreamRepository": "https://github.com/RobotecAI/ros2-for-unity",
        "upstreamLicense": "Apache-2.0",
        "upstreamLicenseFile": "Upstream/LICENSE.AL2",
        "bundleStatus": "not_bundled",
        "adapterStatus": "external_assets_sample",
        "distributionModel": "one_repo_multi_package_release_artifacts",
        "distributionPolicy": "runtime_artifacts_live_in_separate_runtime_packages",
        "activeRuntimePolicy": "one_runtime_package_per_project",
        "knownRuntimeRmw": "rmw_fastrtps_cpp",
        "knownRuntimeRosDistro": "jazzy",
    }
    for key, value in expected.items():
        add(results, f"manifest {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")

    current = data.get("currentRecommendedRuntime", {})
    add(
        results,
        "manifest current runtime object",
        isinstance(current, dict),
        f"currentRecommendedRuntime={current!r}",
    )
    if isinstance(current, dict):
        current_expected = {
            "packageName": "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
            "runtimeId": "r2fu-jazzy-win64",
            "rosDistro": "jazzy",
            "platform": "win64",
            "supportLevel": "Recommended",
            "distributionLevel": "BundleCandidate",
            "artifact": "Ros2ForUnity_Jazzy_standalone_windows10.zip",
            "artifactSha256": "ac06054e05282b4ebd53b31ff4a48b815ebadc7f6985a5cebcbe35e01c830936",
        }
        for key, value in current_expected.items():
            add(
                results,
                f"manifest current runtime {key}",
                current.get(key) == value,
                f"expected {value!r}, got {current.get(key)!r}",
            )
        caveats = " ".join(str(item) for item in current.get("knownCaveats", []))
        add(
            results,
            "manifest current runtime caveats",
            "WSL2 NAT" in caveats and "graph snapshots" in caveats,
            caveats,
        )

    legacy = data.get("legacyRuntime", {})
    add(results, "manifest legacy runtime object", isinstance(legacy, dict), f"legacyRuntime={legacy!r}")
    release_url = str(legacy.get("releaseAssetUrl", "")) if isinstance(legacy, dict) else ""
    add(results, "manifest legacy release asset URL", "Ros2ForUnity_humble_standalone_windows11.zip" in release_url, release_url)

    evidence = str(legacy.get("evidence", "")) if isinstance(legacy, dict) else ""
    add(
        results,
        "manifest legacy evidence",
        "WINDOWS_ROS2_GREEN" in evidence and "WSL_ROS2_DISCOVERY_BLOCKED" in evidence,
        evidence,
    )

    support = str(data.get("upstreamSupportStatus", ""))
    add(
        results,
        "manifest upstream support caveat",
        "AWSIM/Autoware" in support and "general community" in support,
        support,
    )

    packages = data.get("plannedRuntimePackages", [])
    add(
        results,
        "manifest planned runtime packages",
        isinstance(packages, list)
        and "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64" in packages
        and "dev.unity2foxglove.ros2forunity.runtime.humble.win64" in packages
        and "dev.unity2foxglove.ros2forunity.runtime.lyrical.ubuntu2604.x64" in packages,
        f"plannedRuntimePackages={packages!r}",
    )
    composition = data.get("packageComposition", {})
    add(
        results,
        "manifest package composition",
        isinstance(composition, dict)
        and "adapterAlone" in composition
        and "runtimeAlone" in composition
        and "adapterPlusRuntime" in composition,
        f"packageComposition={composition!r}",
    )
    add(results, "manifest modifications empty", data.get("modifications") == [], f"modifications={data.get('modifications')!r}")


def check_text_boundaries(results: list[CheckResult]) -> None:
    """Check README and notices wording for attribution and non-bundling boundaries."""
    readme = (PACKAGE / "README.md").read_text(encoding="utf-8", errors="replace") if (PACKAGE / "README.md").exists() else ""
    notices = (
        (PACKAGE / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8", errors="replace")
        if (PACKAGE / "THIRD_PARTY_NOTICES.md").exists()
        else ""
    )
    sample_readme = (
        (SAMPLE / "README.md").read_text(encoding="utf-8", errors="replace")
        if (SAMPLE / "README.md").exists()
        else ""
    )
    combined = readme + "\n" + notices + "\n" + sample_readme

    add(results, "README says runtime not bundled", "runtime binaries are not bundled" in readme.lower(), rel(PACKAGE / "README.md"))
    add(results, "README says external adapter sample", "ros2 for unity external adapter" in readme.lower(), rel(PACKAGE / "README.md"))
    add(results, "README says WSL2 is not GREEN gate", "wsl2 nat" in readme.lower() and "not a green gate" in readme.lower(), rel(PACKAGE / "README.md"))
    add(
        results,
        "README defers standard visualization",
        "standard ros2 visualization" in readme.lower(),
        rel(PACKAGE / "README.md"),
    )
    add(results, "notices attribute R2FU", "RobotecAI ROS2 For Unity" in notices and "Apache-2.0" in notices, rel(PACKAGE / "THIRD_PARTY_NOTICES.md"))
    add(results, "notices attribute ros2cs", "ros2cs" in notices, rel(PACKAGE / "THIRD_PARTY_NOTICES.md"))
    add(results, "notices preserve support caveat", "AWSIM/Autoware" in combined and "general community" in combined, "support caveat")
    add(results, "notices require future inventory", "complete transitive inventory" in combined, "future binary bundling boundary")
    forbidden_public_tokens = ["Phase 137B", "Phase106B", "Phase110", "phase110", "Phase 108", "phase", "Phase"]
    hits = [token for token in forbidden_public_tokens if token in combined]
    manifest_text = (PACKAGE / "Compliance" / "ros2-for-unity-adoption-manifest.json").read_text(
        encoding="utf-8",
        errors="replace",
    )
    hits.extend(token for token in forbidden_public_tokens if token in manifest_text)
    add(results, "public R2FU docs avoid internal phase names", not hits, ", ".join(hits) if hits else "no phase tokens")


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

    forbidden_dirs = [PACKAGE / "Editor"]
    existing = [rel(path) for path in forbidden_dirs if path.exists()]
    add(results, "optional package has no editor adapter directory", not existing, "; ".join(existing) if existing else "no editor directory")


def check_sample_source_boundary(results: list[CheckResult]) -> None:
    """Validate the external adapter sample is source-only and names the stable smoke boundary."""
    required = [
        SAMPLE / "README.md",
        SAMPLE / "Phase110Ros2ForUnityContextFactory.cs",
        SAMPLE / "Phase110Ros2ForUnityContext.cs",
        SAMPLE / "Phase110Ros2ForUnityStringSmoke.cs",
    ]
    for path in required:
        add(results, f"sample file: {path.name}", path.exists(), rel(path))

    invalid_files = [rel(path) for path in iter_files(SAMPLE) if path.suffix.lower() not in ALLOWED_SAMPLE_SUFFIXES]
    add(
        results,
        "sample contains only source/docs/meta files",
        not invalid_files,
        "; ".join(invalid_files[:MAX_REPORTED_OFFENDERS]) if invalid_files else "source-only sample",
    )

    readme = (SAMPLE / "README.md").read_text(encoding="utf-8", errors="replace") if (SAMPLE / "README.md").exists() else ""
    add(
        results,
        "sample README documents external import",
        "Assets/Ros2ForUnity" in readme and "UNITY2FOXGLOVE_ROS2_FOR_UNITY" in readme,
        rel(SAMPLE / "README.md"),
    )
    add(
        results,
        "sample README documents live topics",
        "/unity2foxglove/ros2forunity/string/out" in readme
        and "/unity2foxglove/ros2forunity/string/in" in readme,
        rel(SAMPLE / "README.md"),
    )


def check_runtime_source_boundary(results: list[CheckResult]) -> None:
    """Allow facade-only Runtime source while rejecting upstream ROS2/R2FU dependencies."""
    runtime = PACKAGE / "Runtime"
    if not runtime.exists():
        add(results, "optional Runtime absent or facade-only", True, "Runtime absent")
        return

    invalid_files: list[str] = []
    token_hits: list[str] = []
    for path in iter_files(runtime):
        if path.suffix.lower() not in ALLOWED_RUNTIME_SUFFIXES:
            invalid_files.append(rel(path))
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        for token in FORBIDDEN_RUNTIME_TOKENS:
            if token in text:
                token_hits.append(f"{rel(path)} contains {token}")
                break

    add(
        results,
        "optional Runtime contains only facade source",
        not invalid_files,
        "; ".join(invalid_files[:MAX_REPORTED_OFFENDERS]) if invalid_files else "all Runtime files are source/asmdef/meta",
    )
    add(
        results,
        "optional Runtime has no upstream ROS2/R2FU API references",
        not token_hits,
        "; ".join(token_hits[:MAX_REPORTED_OFFENDERS]) if token_hits else "no forbidden Runtime tokens",
    )


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
    check_sample_source_boundary(results)
    check_runtime_source_boundary(results)
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
