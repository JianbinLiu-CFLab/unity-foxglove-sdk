"""Command-line generation and self-check orchestration for Phase34."""

from __future__ import annotations

import argparse
import os as __phase192_os
import tempfile as __phase192_tempfile
from pathlib import Path
from typing import Union as __phase192_Union

from .fixture_builder import *
from .fixture_constants import *
from .fixture_validator import *


def __atomic_replace(path: Path, payload: bytes, *, inject_failure: bool = False) -> None:
    """Publish bytes through a same-directory temporary file and atomic replace."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temp_name = __phase192_tempfile.mkstemp(
        prefix=f".{path.name}.", suffix=".tmp", dir=path.parent
    )
    temp_path = Path(temp_name)
    try:
        with __phase192_os.fdopen(fd, "wb") as handle:
            handle.write(payload)
            handle.flush()
            __phase192_os.fsync(handle.fileno())
        if inject_failure:
            raise RuntimeError("injected atomic publication failure")
        __phase192_os.replace(temp_path, path)
        try:
            dir_fd = __phase192_os.open(path.parent, __phase192_os.O_RDONLY)
            try:
                __phase192_os.fsync(dir_fd)
            finally:
                __phase192_os.close(dir_fd)
        except (OSError, PermissionError):
            pass
    finally:
        try:
            temp_path.unlink()
        except FileNotFoundError:
            pass


def atomic_write_text(
    path: __phase192_Union[str, Path],
    text: str,
    *,
    encoding: str = "utf-8",
    inject_failure: bool = False,
) -> None:
    """Encode UTF-8 text and publish it atomically."""
    if encoding.lower().replace("_", "-") != "utf-8":
        raise ValueError("atomic_write_text only supports UTF-8")
    __atomic_replace(Path(path), text.encode("utf-8"), inject_failure=inject_failure)


def atomic_write_bytes(
    path: __phase192_Union[str, Path],
    payload: bytes,
    *,
    inject_failure: bool = False,
) -> None:
    """Publish a byte payload atomically, preserving any prior destination on failure."""
    __atomic_replace(Path(path), payload, inject_failure=inject_failure)


def resolve_output(output: str | None) -> Path:
    """Resolve an optional output path relative to the repository root."""
    repo_root = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
    candidate = Path(output) if output else DEFAULT_OUTPUT
    if not candidate.is_absolute():
        candidate = repo_root / candidate
    return candidate.resolve()


def main() -> int:
    """Generate the fixture, self-check it, and print output details."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", "-o", help="Output MCAP path. Defaults to build/test_mcap/phase34_attachment_smoke.mcap.")
    args = parser.parse_args()

    output = resolve_output(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)

    data, attachment_data = build_smoke_file()
    atomic_write_bytes(output, data)
    self_check(output, attachment_data)

    print(f"[phase34-smoke] Generated: {output}")
    print(f"[phase34-smoke] Size:      {output.stat().st_size} bytes")
    print(
        "[phase34-smoke] Self-check: leading/trailing magic, summary_crc, "
        "ChunkIndex, AttachmentIndex, payload, and attachment CRC passed."
    )
    return EXIT_SUCCESS



__all__ = [name for name in globals() if not name.startswith("__")]
