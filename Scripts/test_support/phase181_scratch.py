"""Repository-local scratch roots for Phase181 regression fixtures."""

from __future__ import annotations

import pathlib
import tempfile


_REPOSITORY_ROOT = pathlib.Path(__file__).resolve().parents[2]
_PHASE181_SCRATCH_ROOT = _REPOSITORY_ROOT / "build" / "Tests" / "Phase181" / "python"


def temporary_directory(prefix: str) -> tempfile.TemporaryDirectory[str]:
    """Create one automatically cleaned Phase181 test directory inside the repository build root."""
    _PHASE181_SCRATCH_ROOT.mkdir(parents=True, exist_ok=True)
    return tempfile.TemporaryDirectory(prefix=prefix, dir=_PHASE181_SCRATCH_ROOT)
