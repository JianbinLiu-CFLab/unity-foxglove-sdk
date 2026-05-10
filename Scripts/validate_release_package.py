#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Validate release-facing Unity package structure and sample hygiene.

from __future__ import annotations

import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


ROOT = Path(__file__).resolve().parent.parent
PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.sdk"
SAMPLES = PACKAGE / "Samples~"
DOCS = PACKAGE / "Documentation~"

UNITY_META_EXTENSIONS = {
    ".asset",
    ".cs",
    ".dll",
    ".inputactions",
    ".json",
    ".unity",
    ".xml",
}

FORBIDDEN_PUBLIC_PATTERNS = (
    ("local Windows path", re.compile(r"\b[A-Za-z]:[\\/]")),
    ("Developer/ reference", re.compile(r"Developer[\\/]")),
    ("Obsidian pasted image", re.compile(r"Pasted image", re.IGNORECASE)),
    ("Obsidian embed", re.compile(r"!\[\[")),
    ("TODO marker", re.compile(r"\bTODO\b")),
    ("TBD marker", re.compile(r"\bTBD\b")),
    ("FIXME marker", re.compile(r"\bFIXME\b")),
)

FORBIDDEN_SAMPLE_PARTS = {
    "Library",
    "Logs",
    "Recordings",
    "Generated",
}

FORBIDDEN_SAMPLE_NAMES = {
    "FoxRun_link.xml",
}

FORBIDDEN_SAMPLE_NAME_PATTERNS = (
    re.compile(r".*_FoxRun\.g\.cs$", re.IGNORECASE),
    re.compile(r"PerformanceTestRun.*", re.IGNORECASE),
)


@dataclass
class CheckResult:
    name: str
    ok: bool
    detail: str


def rel(path: Path) -> str:
    try:
        return path.resolve().relative_to(ROOT.resolve()).as_posix()
    except ValueError:
        return str(path)


def iter_files(root: Path) -> Iterable[Path]:
    if not root.exists():
        return ()
    return (p for p in root.rglob("*") if p.is_file())


def add(results: list[CheckResult], name: str, ok: bool, detail: str = "") -> None:
    results.append(CheckResult(name, ok, detail))


def load_package_json(results: list[CheckResult]) -> dict:
    path = PACKAGE / "package.json"
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        add(results, "package.json parses", False, f"{rel(path)}: {exc}")
        return {}
    add(results, "package.json parses", True, rel(path))
    return data


def check_package_identity(results: list[CheckResult], data: dict) -> None:
    expected = {
        "name": "dev.unity2foxglove.sdk",
        "displayName": "Unity2Foxglove SDK",
        "license": "Apache-2.0",
    }
    for key, value in expected.items():
        actual = data.get(key)
        add(results, f"package {key}", actual == value, f"expected {value!r}, got {actual!r}")

    version = data.get("version")
    add(results, "package version", isinstance(version, str) and bool(version.strip()), f"version={version!r}")

    samples = data.get("samples")
    add(results, "package samples list", isinstance(samples, list) and len(samples) == 2, f"count={len(samples) if isinstance(samples, list) else 'n/a'}")
    if not isinstance(samples, list):
        return

    expected_samples = {
        "Basic Visualization": "Samples~/BasicVisualization",
        "Full Demo Visualization": "Samples~/FullDemoVisualization",
    }
    for display_name, sample_path in expected_samples.items():
        match = next((s for s in samples if s.get("displayName") == display_name), None)
        add(
            results,
            f"sample declared: {display_name}",
            match is not None,
            "declared" if match is not None else "missing from package.json samples",
        )
        if match is None:
            continue
        actual_path = match.get("path")
        add(results, f"sample path: {display_name}", actual_path == sample_path, f"expected {sample_path!r}, got {actual_path!r}")
        add(results, f"sample path exists: {display_name}", (PACKAGE / actual_path).exists(), rel(PACKAGE / str(actual_path)))


def check_required_files(results: list[CheckResult]) -> None:
    required = [
        PACKAGE / "README.md",
        PACKAGE / "LICENSE",
        PACKAGE / "Runtime" / "Unity.FoxgloveSDK.asmdef",
        PACKAGE / "Editor" / "Unity.FoxgloveSDK.Editor.asmdef",
        PACKAGE / "Editor" / "SourceGenerators" / "src" / "Unity.FoxgloveSDK.SourceGenerators.asmdef",
        PACKAGE / "Runtime" / "Schemas" / "Proto" / "Unity.FoxgloveSDK.Proto.asmdef",
        PACKAGE / "Runtime" / "link.xml",
        PACKAGE / "Plugins" / "Google.Protobuf" / "Google.Protobuf.dll",
    ]
    for path in required:
        add(results, f"required file: {path.name}", path.exists(), rel(path))


def check_sample_meta(results: list[CheckResult]) -> None:
    missing: list[str] = []
    for path in iter_files(SAMPLES):
        if path.suffix == ".meta" or path.name == "README.md":
            continue
        if path.suffix.lower() not in UNITY_META_EXTENSIONS:
            continue
        if not Path(str(path) + ".meta").exists():
            missing.append(rel(path))
    add(results, "sample Unity asset .meta files", not missing, "; ".join(missing[:10]) if missing else "all checked sample assets have .meta")


def check_sample_boundaries(results: list[CheckResult]) -> None:
    basic = SAMPLES / "BasicVisualization"
    full = SAMPLES / "FullDemoVisualization"

    forbidden_basic = [
        basic / "Scripts",
        basic / "Settings",
        basic / "InputSystem_Actions.inputactions",
        basic / "FoxgloveFullLayout.json",
        basic / "Scenes" / "FullDemoVisualization.unity",
    ]
    leaks = [rel(p) for p in forbidden_basic if p.exists()]
    add(results, "Basic sample remains minimal", not leaks, "; ".join(leaks) if leaks else "no FullDemo-only files")

    required_full = [
        full / "FoxgloveFullLayout.json",
        full / "FoxgloveFullLayout.json.meta",
        full / "InputSystem_Actions.inputactions",
        full / "Scenes" / "FullDemoVisualization.unity",
        full / "Scripts" / "FoxgloveDemoSetup.cs",
        full / "Scripts" / "MouseDragCube.cs",
        full / "Scripts" / "TestLog.cs",
        full / "Settings" / "DefaultVolumeProfile.asset",
        full / "Settings" / "UniversalRenderPipelineGlobalSettings.asset",
    ]
    missing = [rel(p) for p in required_full if not p.exists()]
    add(results, "FullDemo required files", not missing, "; ".join(missing) if missing else "all required files present")


def check_forbidden_public_content(results: list[CheckResult]) -> None:
    roots = [SAMPLES, DOCS, PACKAGE / "README.md"]
    offenders: list[str] = []
    for root in roots:
        paths = [root] if root.is_file() else list(iter_files(root))
        for path in paths:
            if path.suffix.lower() not in {".md", ".json", ".cs", ".unity", ".asset", ".inputactions", ".xml"}:
                continue
            text = path.read_text(encoding="utf-8", errors="replace")
            for label, pattern in FORBIDDEN_PUBLIC_PATTERNS:
                if pattern.search(text):
                    offenders.append(f"{rel(path)} ({label})")
                    break
    add(results, "public docs/samples have no forbidden markers", not offenders, "; ".join(offenders[:12]) if offenders else "no forbidden markers found")


def check_forbidden_sample_artifacts(results: list[CheckResult]) -> None:
    offenders: list[str] = []
    for path in SAMPLES.rglob("*"):
        parts = set(path.relative_to(SAMPLES).parts)
        if parts & FORBIDDEN_SAMPLE_PARTS:
            offenders.append(rel(path))
            continue
        if path.name in FORBIDDEN_SAMPLE_NAMES:
            offenders.append(rel(path))
            continue
        if any(pattern.match(path.name) for pattern in FORBIDDEN_SAMPLE_NAME_PATTERNS):
            offenders.append(rel(path))
    add(results, "samples contain no generated/local artifacts", not offenders, "; ".join(offenders[:12]) if offenders else "no forbidden sample artifacts")


def check_package_build_artifacts(results: list[CheckResult]) -> None:
    forbidden_dirs = {"bin", "obj", "__pycache__"}
    offenders: list[str] = []
    for path in PACKAGE.rglob("*"):
        if path.is_dir() and path.name in forbidden_dirs:
            offenders.append(rel(path))
    add(results, "package contains no build/cache directories", not offenders, "; ".join(offenders[:12]) if offenders else "no build/cache directories")


def check_google_protobuf_collision(results: list[CheckResult]) -> None:
    plugin_dir = PACKAGE / "Plugins" / "Google.Protobuf"
    dll_stems = {p.stem for p in plugin_dir.glob("*.dll")}
    asmdef_files = list(plugin_dir.glob("*.asmdef"))
    filename_collisions = [rel(p) for p in asmdef_files if p.stem in dll_stems]

    name_collisions: list[str] = []
    for asmdef in asmdef_files:
        try:
            name = json.loads(asmdef.read_text(encoding="utf-8")).get("name")
        except Exception:
            continue
        if name in dll_stems:
            name_collisions.append(f"{rel(asmdef)} name={name}")

    offenders = filename_collisions + name_collisions
    add(results, "Google.Protobuf DLL/asmdef naming", not offenders, "; ".join(offenders) if offenders else "no collision")


def print_results(results: list[CheckResult]) -> None:
    name_width = max(len(r.name) for r in results) if results else 0
    for result in results:
        status = "PASS" if result.ok else "FAIL"
        print(f"[{status}] {result.name:<{name_width}}  {result.detail}")


def main() -> int:
    results: list[CheckResult] = []
    data = load_package_json(results)
    if data:
        check_package_identity(results, data)
    check_required_files(results)
    check_sample_meta(results)
    check_sample_boundaries(results)
    check_forbidden_public_content(results)
    check_forbidden_sample_artifacts(results)
    check_package_build_artifacts(results)
    check_google_protobuf_collision(results)

    print_results(results)
    failed = [r for r in results if not r.ok]
    if failed:
        print(f"\nvalidate_release_package: {len(failed)} check(s) failed.", file=sys.stderr)
        return 1

    print(f"\nvalidate_release_package: {len(results)} check(s) passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
