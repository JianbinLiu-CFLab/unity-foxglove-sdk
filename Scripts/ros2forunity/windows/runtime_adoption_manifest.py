#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Keep the shared R2FU adoption manifest aligned with one refreshed
# Windows runtime package without changing metadata owned by another distro.

"""Provider-neutral helpers for updating the shared R2FU adoption manifest."""

from __future__ import annotations

import json
import os
import tempfile
from pathlib import Path
from typing import Iterable


CORE_ARTIFACT_KEYS = (
    "artifactSha256",
    "artifactSize",
    "inventoryFileCount",
)


def read_json(path: Path) -> dict[str, object]:
    """Read a UTF-8 JSON object."""
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise ValueError(f"Expected a JSON object: {path}")
    return data


def write_json(path: Path, data: dict[str, object]) -> None:
    """Atomically replace one stable UTF-8 JSON object."""
    content = json.dumps(data, indent=2, ensure_ascii=False) + "\n"
    _atomic_write_bytes(path, content.encode("utf-8"))


def _atomic_write_bytes(path: Path, content: bytes) -> None:
    """Replace one file from a fully flushed sibling temporary file."""
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{path.name}.",
        suffix=".tmp",
        dir=path.parent,
    )
    temporary = Path(temporary_name)
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(content)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def _atomic_copy(source: Path, destination: Path) -> None:
    """Atomically replace a compliance copy from its complete source bytes."""
    _atomic_write_bytes(destination, source.read_bytes())


def _copy_manifest_fields(
    target: dict[str, object],
    runtime_manifest: dict[str, object],
    keys: Iterable[str],
) -> None:
    """Copy only explicitly owned fields that exist in the runtime manifest."""
    for key in keys:
        if key in runtime_manifest:
            target[key] = runtime_manifest[key]


def _require_manifest_fields(
    runtime_manifest: dict[str, object],
    keys: Iterable[str],
) -> None:
    """Reject incomplete artifact identity before any adoption data is changed."""
    missing = [key for key in keys if key not in runtime_manifest]
    if missing:
        raise RuntimeError(
            "Runtime manifest is missing required artifact metadata: "
            + ", ".join(missing)
        )


def sync_runtime_adoption_manifest(
    project_root: Path,
    package_path: Path,
    package_name: str,
    *,
    update_current_recommended: bool = False,
    additional_manifest_keys: Iterable[str] = (),
    notices_relative_path: str | None = None,
) -> dict[str, object]:
    """Update one runtime row while preserving every other distro's metadata."""
    compliance_dir = project_root / "Packages" / "dev.unity2foxglove.ros2forunity" / "Compliance"
    adoption_path = compliance_dir / "ros2-for-unity-adoption-manifest.json"
    runtime_manifest = read_json(package_path / "RuntimeSupport" / "runtime-manifest.json")
    _require_manifest_fields(runtime_manifest, CORE_ARTIFACT_KEYS)
    adoption = read_json(adoption_path)
    runtimes = adoption.get("supportedRuntimePackages")
    if not isinstance(runtimes, list):
        raise RuntimeError(f"{adoption_path} has no supportedRuntimePackages array")

    target = next(
        (
            item
            for item in runtimes
            if isinstance(item, dict) and item.get("packageName") == package_name
        ),
        None,
    )
    if target is None:
        raise RuntimeError(f"{adoption_path} does not contain {package_name}")

    keys = (*CORE_ARTIFACT_KEYS, *tuple(additional_manifest_keys))
    _copy_manifest_fields(target, runtime_manifest, keys)

    if update_current_recommended:
        current = adoption.get("currentRecommendedRuntime")
        if not isinstance(current, dict) or current.get("packageName") != package_name:
            raise RuntimeError(
                f"{adoption_path} currentRecommendedRuntime is not {package_name}"
            )
        _copy_manifest_fields(current, runtime_manifest, CORE_ARTIFACT_KEYS)

    notices_path = None
    previous_notices: bytes | None = None
    if notices_relative_path is not None:
        notices_path = compliance_dir / notices_relative_path
        previous_notices = notices_path.read_bytes() if notices_path.exists() else None
        _atomic_copy(package_path / "THIRD_PARTY_NOTICES.md", notices_path)

    try:
        write_json(adoption_path, adoption)
    except BaseException:
        if notices_path is not None:
            if previous_notices is None:
                notices_path.unlink(missing_ok=True)
            else:
                _atomic_write_bytes(notices_path, previous_notices)
        raise

    return {
        "adoptionManifestPath": str(adoption_path),
        "runtimeNoticesPath": None if notices_path is None else str(notices_path),
    }
