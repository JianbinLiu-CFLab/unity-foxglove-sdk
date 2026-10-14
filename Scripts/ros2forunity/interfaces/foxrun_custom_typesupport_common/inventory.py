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

import json
import re
from pathlib import Path, PurePosixPath
from typing import Any, Iterable, Mapping

from .identity import file_sha256
from .models import AddonValidationError, SUPPORTED_DISTROS, _normalize_distro

_SHA256 = re.compile(r"^[0-9a-f]{64}$")
_ABSOLUTE_PATH = re.compile(r"^(?:[A-Za-z]:[\\/]|[\\/]{2}|/)")

def _load_json(path: Path, remediation: str) -> dict[str, Any]:
    """Implement the internal load json step."""
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise AddonValidationError(remediation) from exc
    if not isinstance(payload, dict):
        raise AddonValidationError(remediation)
    return payload


def _require_text(mapping: Mapping[str, Any], key: str, remediation: str) -> str:
    """Implement the internal require text step."""
    value = mapping.get(key)
    if not isinstance(value, str) or not value:
        raise AddonValidationError(remediation)
    return value


def _require_sha256(mapping: Mapping[str, Any], key: str, remediation: str) -> str:
    """Implement the internal require sha256 step."""
    value = _require_text(mapping, key, remediation)
    if _SHA256.fullmatch(value) is None:
        raise AddonValidationError(remediation)
    return value

def _validate_payload_entry(
    addon_root: Path,
    entry: Mapping[str, Any],
    *,
    expected_name: str | None,
    remediation: str,
) -> Path:
    """Implement the internal validate payload entry step."""
    relative = _safe_relative_path(_require_text(entry, "path", remediation))
    path = addon_root / relative
    if not path.is_file() or file_sha256(path) != _require_sha256(entry, "sha256", remediation):
        raise AddonValidationError(remediation)
    if expected_name is not None and entry.get("name") != expected_name:
        raise AddonValidationError(remediation)
    return path


def _validate_inventory(addon_root: Path, inventory: Mapping[str, Any]) -> None:
    """Implement the internal validate inventory step."""
    if inventory.get("schemaVersion") != 1:
        raise AddonValidationError("repair-typesupport-inventory")
    entries = inventory.get("entries")
    if not isinstance(entries, list):
        raise AddonValidationError("repair-typesupport-inventory")
    seen: set[str] = set()
    inventory_paths: list[str] = []
    allowed_roles = {"managed", "native", "catalog", "importer", "metadata", "notice"}
    allowed_classifications = {"direct", "transitive", "metadata"}
    for entry in entries:
        if not isinstance(entry, dict):
            raise AddonValidationError("repair-typesupport-inventory")
        relative = _safe_relative_path(_require_text(entry, "path", "repair-typesupport-inventory")).as_posix()
        if relative in seen:
            raise AddonValidationError("repair-typesupport-inventory")
        seen.add(relative)
        inventory_paths.append(relative)
        path = addon_root / relative
        if not path.is_file() or path.stat().st_size != entry.get("byteLength"):
            raise AddonValidationError("repair-typesupport-inventory")
        if file_sha256(path) != _require_sha256(entry, "sha256", "repair-typesupport-inventory"):
            raise AddonValidationError("repair-typesupport-inventory")
        if entry.get("role") not in allowed_roles or entry.get("classification") not in allowed_classifications:
            raise AddonValidationError("repair-typesupport-inventory")
    if inventory_paths != sorted(inventory_paths, key=str.lower):
        raise AddonValidationError("repair-typesupport-inventory")

    excluded = {"RuntimeSupport/typesupport-inventory.json"}
    recorded_importers = {
        _safe_relative_path(_require_text(entry, "path", "repair-typesupport-inventory")).as_posix()
        for entry in entries
        if isinstance(entry, dict) and entry.get("role") == "importer"
    }
    actual = {
        path.relative_to(addon_root).as_posix()
        for path in addon_root.rglob("*")
        if path.is_file()
        and path.relative_to(addon_root).as_posix() not in excluded
        and (
            not path.name.endswith(".meta")
            or path.relative_to(addon_root).as_posix() in recorded_importers
        )
    }
    if actual != seen:
        raise AddonValidationError("repair-typesupport-inventory")


def _safe_relative_path(value: str) -> PurePosixPath:
    """Implement the internal safe relative path step."""
    if _ABSOLUTE_PATH.match(value) or "\\" in value:
        raise AddonValidationError("remove-machine-private-path")
    path = PurePosixPath(value)
    if not value or path.is_absolute() or any(part in {"", ".", ".."} for part in path.parts):
        raise AddonValidationError("remove-machine-private-path")
    return path


def _reject_absolute_values(value: object) -> None:
    """Implement the internal reject absolute values step."""
    if isinstance(value, str):
        if _ABSOLUTE_PATH.match(value):
            raise AddonValidationError("remove-machine-private-path")
        return
    if isinstance(value, Mapping):
        for item in value.values():
            _reject_absolute_values(item)
        return
    if isinstance(value, Iterable) and not isinstance(value, (bytes, bytearray)):
        for item in value:
            _reject_absolute_values(item)

__all__ = [name for name in globals() if not name.startswith("__")]
