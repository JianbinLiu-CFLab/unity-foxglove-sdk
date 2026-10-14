#!/usr/bin/env python3
"""Non-mutating Phase181 Win64 custom-typesupport toolchain preflight.

The tool accepts explicit ROS2 and source roots, builds a minimal child
environment, and probes only the selected toolchain. It never downloads a
tool, sources a user shell profile, or writes below ``Packages/``. Successful
results are recorded as redacted provenance below ``build/phase181``.
"""

from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "verify_foxrun_custom_typesupport_toolchain.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import hashlib
import json
import os
from pathlib import Path
from typing import Mapping

from .models import ToolchainPreflightRequest, ToolchainPreflightResult

def _write_provenance(request: ToolchainPreflightRequest, result: ToolchainPreflightResult) -> None:
    """Implement the internal write provenance step."""
    target = request.build_root / "phase181" / request.distro / "provenance" / "toolchain.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "schemaVersion": 2,
        "distro": result.distro,
        "generator": result.generator,
        "requirements": list(result.requirements),
        "inputIdentity": result.input_identity,
    }
    temporary = target.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    os.replace(temporary, target)


def _toolchain_input_identity(request: ToolchainPreflightRequest, tools: Mapping[str, Path]) -> dict[str, object]:
    """Return content identities for every build-affecting root and executable."""
    roots = {
        "ros2Root": _tree_sha256(request.ros2_root),
        "ros2csSource": _tree_sha256(request.ros2cs_source),
        "r2fuSource": _tree_sha256(request.r2fu_source),
    }
    if request.ros2cs_install:
        roots["ros2csInstall"] = _tree_sha256(request.ros2cs_install)
    executables = {
        label: _file_sha256(path)
        for label, path in sorted(tools.items())
        if label not in {"visualStudio"}
    }
    executables["visualStudio"] = _tree_sha256(tools["visualStudio"])
    return {"roots": roots, "executables": executables}


def _file_sha256(path: Path) -> str:
    """Return the SHA-256 digest of one toolchain file."""
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _tree_sha256(root: Path) -> str:
    """Return a deterministic SHA-256 digest for a directory tree."""
    digest = hashlib.sha256()
    root = Path(root)
    for path in sorted((item for item in root.rglob("*") if item.is_file()), key=lambda item: item.relative_to(root).as_posix()):
        digest.update(path.relative_to(root).as_posix().encode("utf-8"))
        digest.update(b"\0")
        with path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
    return digest.hexdigest()


def _ready(label: str, detail: str) -> dict[str, str]:
    """Implement the internal ready step."""
    return {"label": label, "status": "ready", "detail": _bounded(detail)}


def _first_line(value: str) -> str:
    """Implement the internal first line step."""
    return _bounded((value or "").strip().splitlines()[0] if (value or "").strip() else "available")


def _bounded(value: str) -> str:
    """Implement the internal bounded step."""
    return " ".join((value or "").split())[:160]

__all__ = [name for name in globals() if not name.startswith("__")]
