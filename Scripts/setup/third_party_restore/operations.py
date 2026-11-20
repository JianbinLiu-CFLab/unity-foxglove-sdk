"""Safe inspection and restoration of the pinned third-party checkouts."""

from __future__ import annotations

from collections.abc import Mapping
from pathlib import Path
import sys

from .configuration import SOURCE_URLS, VENDOR_ROOTS, vendor_pins
from .git import git_output, is_link_or_junction, restore_missing


def effective_source_urls(
    source_urls: Mapping[str, str] | None,
    vendor_roots: Mapping[str, str],
) -> dict[str, str]:
    """Return production URLs with optional fixture-only source overrides."""
    urls = dict(SOURCE_URLS)
    if source_urls:
        unknown = set(source_urls) - set(vendor_pins(vendor_roots))
        if unknown:
            raise ValueError(f"unknown third-party source override: {sorted(unknown)}")
        urls.update(source_urls)
    return urls


def inspect_or_restore(
    repository_root: Path,
    relative: str,
    revision: str,
    source_url: str,
    restore: bool,
) -> None:
    """Inspect one vendor checkout and restore it only when it is missing."""
    path = repository_root / relative
    if is_link_or_junction(path):
        raise RuntimeError("path is a symlink or junction")
    if not path.exists():
        if not restore:
            print(f"{relative}: missing")
            return
        restore_missing(path, revision, source_url)
        print(f"{relative}: restored {revision}")
        return
    if not path.is_dir():
        raise RuntimeError("path is not a directory")

    git_output(path, "rev-parse", "--git-dir")
    origin = git_output(path, "remote", "get-url", "origin")
    if origin != source_url:
        raise RuntimeError(f"origin differs from expected source URL: {origin}")
    status = git_output(path, "status", "--porcelain", "--untracked-files=all")
    if status:
        raise RuntimeError("working tree is not clean")
    head = git_output(path, "rev-parse", "HEAD")
    if head != revision:
        raise RuntimeError(f"HEAD {head} differs from expected revision {revision}")
    print(f"{relative}: ready {revision}")


def restore_third_party(
    repository_root: Path,
    *,
    restore: bool = False,
    source_urls: Mapping[str, str] | None = None,
    vendor_roots: Mapping[str, str] = VENDOR_ROOTS,
) -> int:
    """Report or safely restore the pinned third-party repositories."""
    pins = vendor_pins(vendor_roots)
    urls = effective_source_urls(source_urls, vendor_roots)
    failures = 0
    for relative, revision in pins.items():
        try:
            inspect_or_restore(
                repository_root,
                relative,
                revision,
                urls[relative],
                restore,
            )
        except (OSError, RuntimeError, ValueError) as exc:
            print(f"{relative}: ERROR {exc}", file=sys.stderr)
            failures += 1
    return 1 if failures else 0
