"""Deterministic identity surfaces used by the Phase192 split gates."""

from __future__ import annotations

import posixpath
import re
import unicodedata

__all__ = ["is_repository_relative"]

_WINDOWS_RESERVED_DEVICE_NAMES = frozenset(
    {"con", "prn", "aux", "nul", *(f"com{index}" for index in range(1, 10)),
     *(f"lpt{index}" for index in range(1, 10)), "com¹", "com²", "com³", "lpt¹", "lpt²", "lpt³"}
)

_WINDOWS_FORBIDDEN_SEGMENT_CHARACTERS = frozenset(':*?"<>|')

def _is_safe_path_segment(segment: str) -> bool:
    """Return whether a path segment is stable on Windows and POSIX."""
    if not segment or segment in {".", ".."}:
        return False
    if segment[-1] in {".", " "}:
        return False
    if any(ord(character) < 32 for character in segment):
        return False
    if any(character in _WINDOWS_FORBIDDEN_SEGMENT_CHARACTERS for character in segment):
        return False
    device_name = segment.split(".", 1)[0].casefold()
    return device_name not in _WINDOWS_RESERVED_DEVICE_NAMES

def is_repository_relative(value: str) -> bool:
    """Return whether a path is a canonical repository-relative identity."""
    if not isinstance(value, str) or not value or "\0" in value:
        return False
    if (
        value.startswith(("/", "\\"))
        or re.match(r"^[A-Za-z]:", value)
        or "\\" in value
    ):
        return False
    normalized = unicodedata.normalize("NFC", value)
    if normalized != value or posixpath.normpath(value) != value:
        return False
    return all(_is_safe_path_segment(segment) for segment in value.split("/"))
