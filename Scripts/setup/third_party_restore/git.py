"""Small Git and filesystem operations used by the restore workflow."""

from __future__ import annotations

import os
from pathlib import Path
import stat
import shutil
import subprocess


def run_git(repository: Path, *arguments: str) -> subprocess.CompletedProcess[str]:
    """Run Git in a vendor repository and capture its output."""
    return subprocess.run(
        ["git", *arguments],
        cwd=str(repository),
        capture_output=True,
        text=True,
        check=False,
    )


def git_output(repository: Path, *arguments: str) -> str:
    """Return Git output or raise a descriptive repository error."""
    result = run_git(repository, *arguments)
    if result.returncode != 0:
        detail = result.stderr.strip() or result.stdout.strip() or "unknown Git error"
        raise RuntimeError(detail)
    return result.stdout.strip()


def is_link_or_junction(path: Path) -> bool:
    """Detect symlinks and Windows reparse-point directories without following them."""
    try:
        if path.is_symlink():
            return True
        if os.name == "nt":
            attributes = getattr(os.lstat(path), "st_file_attributes", 0)
            return bool(attributes & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400))
    except FileNotFoundError:
        return False
    return False


def has_link_component(root: Path, path: Path) -> bool:
    """Detect a symlink or junction in a vendor path without following it."""
    current = path
    while current != root:
        if is_link_or_junction(current):
            return True
        current = current.parent
    return False


def restore_missing(path: Path, revision: str, source_url: str) -> None:
    """Initialize and detach a missing vendor checkout at one revision."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.mkdir()
    try:
        git_output(path, "init")
        git_output(path, "remote", "add", "origin", source_url)
        git_output(path, "fetch", "--depth", "1", "origin", revision)
        git_output(path, "checkout", "--detach", "FETCH_HEAD")
    except BaseException:
        if not is_link_or_junction(path):
            shutil.rmtree(path)
        raise
