"""Crash-safe publication helpers for smoke/acceptance evidence files."""
from __future__ import annotations

import os
import tempfile
from pathlib import Path
from typing import Union


def _atomic_replace(path: Path, payload: bytes, *, inject_failure: bool = False) -> None:
    """Publish bytes through a same-directory temporary file and atomic replace."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temp_name = tempfile.mkstemp(prefix=f".{path.name}.", suffix=".tmp", dir=path.parent)
    temp_path = Path(temp_name)
    try:
        with os.fdopen(fd, "wb") as handle:
            handle.write(payload)
            handle.flush()
            os.fsync(handle.fileno())
        if inject_failure:
            raise RuntimeError("injected atomic publication failure")
        os.replace(temp_path, path)
        try:
            dir_fd = os.open(path.parent, os.O_RDONLY)
            try:
                os.fsync(dir_fd)
            finally:
                os.close(dir_fd)
        except (OSError, PermissionError):
            pass
    finally:
        try:
            temp_path.unlink()
        except FileNotFoundError:
            pass


def atomic_write_text(path: Union[str, Path], text: str, *, encoding: str = "utf-8", inject_failure: bool = False) -> None:
    """Encode UTF-8 text and publish it atomically."""
    if encoding.lower().replace("_", "-") != "utf-8":
        raise ValueError("atomic_write_text only supports UTF-8")
    _atomic_replace(Path(path), text.encode("utf-8"), inject_failure=inject_failure)


def atomic_write_bytes(path: Union[str, Path], payload: bytes, *, inject_failure: bool = False) -> None:
    """Publish a byte payload atomically, preserving any prior destination on failure."""
    _atomic_replace(Path(path), payload, inject_failure=inject_failure)

