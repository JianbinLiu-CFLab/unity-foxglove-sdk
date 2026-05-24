#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Inspect a ROS2 For Unity standalone runtime artifact and write a compliance inventory.
# Usage: python Scripts/release/inspect_r2fu_runtime_artifact.py
# Inputs: build/dist/Ros2ForUnity_jazzy_standalone_windows_x86_64.zip and optional sha256 sidecar.
# Outputs: Packages/dev.unity2foxglove.ros2forunity/Compliance/r2fu-jazzy-win64-runtime-inventory.json

"""Inspect the local ROS2 For Unity Jazzy Windows runtime artifact."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import zipfile
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


REPO_ROOT_PARENT_DEPTH = 2
EXIT_SUCCESS = 0
EXIT_FAILURE = 1

ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
DEFAULT_ARTIFACT = ROOT / "build" / "dist" / "Ros2ForUnity_jazzy_standalone_windows_x86_64.zip"
DEFAULT_SHA256 = ROOT / "build" / "dist" / "Ros2ForUnity_jazzy_standalone_windows_x86_64.sha256.txt"
DEFAULT_OUTPUT = (
    ROOT
    / "Packages"
    / "dev.unity2foxglove.ros2forunity"
    / "Compliance"
    / "r2fu-jazzy-win64-runtime-inventory.json"
)

ARTIFACT_NAME = "Ros2ForUnity_jazzy_standalone_windows_x86_64.zip"
ARTIFACT_ID = "r2fu-jazzy-win64"
CRITICAL_FILES = (
    "rcl.dll",
    "yaml.dll",
    "spdlog.dll",
    "fmt.dll",
)


@dataclass(frozen=True)
class ArtifactPaths:
    """Resolved paths used by the artifact inspection."""

    artifact: Path
    sha256_file: Path
    output: Path


def parse_args(argv: list[str]) -> ArtifactPaths:
    """Parse command-line arguments into resolved artifact paths."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--zip", type=Path, default=DEFAULT_ARTIFACT, help="Runtime zip artifact to inspect.")
    parser.add_argument("--sha256-file", type=Path, default=DEFAULT_SHA256, help="Optional sha256 sidecar file.")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUTPUT, help="Inventory JSON output path.")
    args = parser.parse_args(argv)
    return ArtifactPaths(args.zip.resolve(), args.sha256_file.resolve(), args.out.resolve())


def sha256_bytes(data: bytes) -> str:
    """Return the hexadecimal SHA-256 digest for a byte string."""
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    """Return the hexadecimal SHA-256 digest for a local file."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_sidecar_sha256(path: Path) -> str:
    """Read a sha256 sidecar in the common '<hash>  <file>' format."""
    if not path.exists():
        return ""
    text = path.read_text(encoding="utf-8", errors="replace").strip()
    if not text:
        return ""
    return text.split()[0].lower()


def classify_file(name: str) -> str:
    """Classify one zip entry into the runtime inventory categories."""
    lower = name.lower()
    file_name = Path(name).name.lower()
    suffix = Path(name).suffix.lower()

    if file_name.endswith("_assembly.dll") or file_name.endswith("_assembly.deps.json"):
        return "generated_message_assemblies"
    if "/plugins/windows/x86_64/" in lower and suffix == ".dll":
        return "native_libraries"
    if "/plugins/" in lower and suffix == ".dll":
        return "managed_assemblies"
    if suffix in {".json", ".xml", ".meta"}:
        return "metadata_files"
    if suffix == ".md" or "license" in file_name or "notice" in file_name:
        return "licenses_notices"
    return "other_support_files"


def summarize_components(file_names: Iterable[str]) -> list[dict[str, object]]:
    """Summarize recognizable runtime component families from file names."""
    names = list(file_names)
    component_patterns = [
        ("RobotecAI ROS2 For Unity", ("ros2forunity/", "ros2forunity/")),
        ("ros2cs", ("ros2cs",)),
        ("ROS2 core native runtime", ("rcl.dll", "rcutils.dll", "rmw_")),
        ("Fast DDS / Fast CDR", ("fastrtps", "fastcdr", "fastdds")),
        ("RMW FastRTPS", ("rmw_fastrtps",)),
        ("ROSIDL generated message support", ("rosidl", "__rosidl_", "_native.dll")),
        ("Pixi runtime closure DLLs", ("yaml.dll", "spdlog.dll", "fmt.dll")),
    ]

    components: list[dict[str, object]] = []
    for label, patterns in component_patterns:
        matches = [name for name in names if any(pattern in name.lower() for pattern in patterns)]
        if matches:
            components.append(
                {
                    "name": label,
                    "matchCount": len(matches),
                    "examples": matches[:12],
                }
            )
    return components


def inspect_zip(paths: ArtifactPaths) -> dict[str, object]:
    """Inspect the zip artifact and return a JSON-serializable inventory."""
    if not paths.artifact.exists():
        raise FileNotFoundError(f"Missing artifact: {paths.artifact}")

    artifact_hash = sha256_file(paths.artifact)
    sidecar_hash = read_sidecar_sha256(paths.sha256_file)
    if sidecar_hash and sidecar_hash != artifact_hash:
        raise ValueError(f"sha256 sidecar mismatch: {sidecar_hash} != {artifact_hash}")

    files: list[dict[str, object]] = []
    with zipfile.ZipFile(paths.artifact) as archive:
        infos = sorted((info for info in archive.infolist() if not info.is_dir()), key=lambda item: item.filename)
        for info in infos:
            data = archive.read(info.filename)
            files.append(
                {
                    "path": info.filename,
                    "category": classify_file(info.filename),
                    "size": info.file_size,
                    "compressedSize": info.compress_size,
                    "sha256": sha256_bytes(data),
                }
            )

    category_counts = Counter(str(item["category"]) for item in files)
    names = [str(item["path"]) for item in files]
    critical = [
        {
            "name": critical,
            "present": any(Path(name).name.lower() == critical for name in names),
            "paths": [name for name in names if Path(name).name.lower() == critical],
        }
        for critical in CRITICAL_FILES
    ]

    return {
        "schemaVersion": 1,
        "runtimeId": ARTIFACT_ID,
        "artifactName": paths.artifact.name,
        "artifactSize": paths.artifact.stat().st_size,
        "sha256": artifact_hash,
        "sha256Sidecar": paths.sha256_file.name if paths.sha256_file.exists() else "",
        "source": {
            "upstream": "RobotecAI/ros2-for-unity",
            "basis": "local Jazzy rebuild from R2FU and ros2cs sources",
        },
        "rosDistro": "jazzy",
        "rmw": "rmw_fastrtps_cpp",
        "platform": "win64",
        "buildType": "standalone",
        "redistributionStatus": "candidate_not_published",
        "fileCount": len(files),
        "categoryCounts": dict(sorted(category_counts.items())),
        "knownCriticalFiles": critical,
        "detectedComponents": summarize_components(names),
        "knownCaveats": [
            "This inventory is generated from a local runtime artifact that is not committed to git.",
            "This is an engineering inventory, not a complete legal audit.",
            "The Jazzy package must include transitive DLLs such as yaml.dll, spdlog.dll, and fmt.dll or Unity may fail to load rcl.dll.",
            "WSL2 NAT remains diagnostic-only; use Windows ROS2 Jazzy or a real remote Linux topology for acceptance.",
        ],
        "files": files,
    }


def write_inventory(paths: ArtifactPaths, inventory: dict[str, object]) -> None:
    """Write the inventory JSON to disk with stable formatting."""
    paths.output.parent.mkdir(parents=True, exist_ok=True)
    paths.output.write_text(json.dumps(inventory, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def main(argv: list[str]) -> int:
    """Run artifact inspection from command-line arguments."""
    paths = parse_args(argv)
    try:
        inventory = inspect_zip(paths)
        write_inventory(paths, inventory)
    except Exception as exc:
        print(f"[FAIL] {exc}", file=sys.stderr)
        return EXIT_FAILURE
    print(f"[PASS] wrote {paths.output.relative_to(ROOT)}")
    print(f"[PASS] files={inventory['fileCount']} sha256={inventory['sha256']}")
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
