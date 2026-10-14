#!/usr/bin/env python3
"""Shared validation primitives for Phase181 custom ROS2 typesupport add-ons.

The add-on package is intentionally a verified artifact layer.  This module
contains no build invocation, ROS environment mutation, or Unity API use; it
only compares declared package data against the selected static interface and
base runtime inputs.
"""


from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "foxrun_custom_typesupport_common.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import hashlib
import json
from pathlib import Path

from .models import (
    AddonValidationError,
    AddonValidationRequest,
    AddonValidationResult,
    ROS_PACKAGE_NAME,
    STATIC_INTERFACE_PACKAGE_ID,
    SUPPORTED_DISTROS,
    _normalize_distro,
)

def addon_package_id(distro: str) -> str:
    """Return the sole supported custom typesupport add-on identity."""

    normalized = _normalize_distro(distro)
    return "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport." + normalized + ".win64"


def base_runtime_package_id(distro: str) -> str:
    """Return the exact R2FU base-runtime package identity for a distro."""

    normalized = _normalize_distro(distro)
    return "dev.unity2foxglove.ros2forunity.runtime." + normalized + ".win64"


def normalized_json_sha256(value: object) -> str:
    """Hash normalized JSON without retaining formatting or machine paths."""

    normalized = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()


def file_sha256(path: Path) -> str:
    """Return a payload SHA-256 using bounded streaming reads."""

    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def compute_static_interface_digest(static_interface_package: Path) -> str:
    """Compute the exact public length-framed static-interface source digest."""

    root = Path(static_interface_package)
    lock_path = "RuntimeSupport/foxrun-ros2-interface-lock.json"
    inputs: list[tuple[str, bytes]] = []
    try:
        candidates = list(root.rglob("*"))
    except OSError as exc:
        raise AddonValidationError("repair-static-interface-source") from exc
    for path in candidates:
        if not path.is_file():
            continue
        relative = path.relative_to(root).as_posix()
        if relative == lock_path or relative.endswith(".meta"):
            continue
        _safe_relative_path(relative)
        try:
            text = path.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError) as exc:
            raise AddonValidationError("repair-static-interface-source") from exc
        if text.startswith("\ufeff"):
            raise AddonValidationError("repair-static-interface-source")
        inputs.append((relative, text.replace("\r\n", "\n").replace("\r", "\n").encode("utf-8")))
    if not inputs:
        raise AddonValidationError("repair-static-interface-source")
    normalized = sorted(inputs, key=lambda item: item[0])
    if len({path.lower() for path, _ in normalized}) != len(normalized):
        raise AddonValidationError("repair-static-interface-source")
    digest = hashlib.sha256()
    _append_interface_digest_frame(digest, b"unity2foxglove:foxrun-ros2-interface-digest:v1")
    _append_interface_digest_frame(digest, b"1")
    for relative, content in normalized:
        _append_interface_digest_frame(digest, relative.encode("utf-8"))
        _append_interface_digest_frame(digest, content)
    return digest.hexdigest()


def _append_interface_digest_frame(digest: "hashlib._Hash", content: bytes) -> None:
    """Implement the internal append interface digest frame step."""
    digest.update(len(content).to_bytes(8, byteorder="big", signed=False))
    digest.update(content)

__all__ = [name for name in globals() if not name.startswith("__")]
