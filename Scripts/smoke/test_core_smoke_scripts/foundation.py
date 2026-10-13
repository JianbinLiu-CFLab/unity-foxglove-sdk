#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for smoke helper failure handling.

from __future__ import annotations

import asyncio
import contextlib
import io
import importlib.util
import json
import os
import ssl
import struct
import subprocess
import sys
import tempfile
import threading
import time
import unittest
import urllib.error
from pathlib import Path
from types import SimpleNamespace
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
SMOKE = ROOT / "Scripts" / "smoke"
_MODULE_CACHE = {}
_SOURCE_CACHE = {}


def _resolved_sys_path(entry: str) -> Path:
    """Resolve a sys.path entry, treating the empty entry as the current directory."""
    return Path(entry or os.getcwd()).resolve()


def _remove_sys_path_entries(paths: set[Path]) -> list[tuple[int, str]]:
    """Temporarily remove selected sys.path entries while preserving restore order."""
    removed = []
    for index in range(len(sys.path) - 1, -1, -1):
        if _resolved_sys_path(sys.path[index]) in paths:
            removed.append((index, sys.path.pop(index)))
    removed.reverse()
    return removed


def _restore_sys_path_entries(removed: list[tuple[int, str]]) -> None:
    """Restore entries removed by _remove_sys_path_entries."""
    for index, entry in removed:
        sys.path.insert(min(index, len(sys.path)), entry)


def read_source(relative: str) -> str:
    """Read a smoke/helper source file once per test process."""
    path = (SMOKE / relative).resolve()
    if path not in _SOURCE_CACHE:
        _SOURCE_CACHE[path] = path.read_text(encoding="utf-8")
    return _SOURCE_CACHE[path]


def read_source_with_split_sections(relative: str) -> str:
    """Read a facade and its semantic implementation sections for source assertions."""
    path = (SMOKE / relative).resolve()
    source = read_source(relative)
    package = path.with_suffix("")
    if not package.is_dir():
        return source
    sections = [
        section.read_text(encoding="utf-8")
        for section in sorted(package.glob("*.py"))
        if section.name not in {"__init__.py", "__main__.py"}
    ]
    return "\n".join([source, *sections])


def read_repo_source(relative: str) -> str:
    """Read a repository source file once per test process."""
    path = (ROOT / relative).resolve()
    if path not in _SOURCE_CACHE:
        _SOURCE_CACHE[path] = path.read_text(encoding="utf-8")
    return _SOURCE_CACHE[path]


def read_repo_source_with_split_sections(relative: str) -> str:
    """Read a repository facade and its semantic implementation sections."""
    path = (ROOT / relative).resolve()
    source = read_repo_source(relative)
    package = path.with_suffix("")
    if not package.is_dir():
        return source
    sections = [
        section.read_text(encoding="utf-8")
        for section in sorted(package.glob("*.py"))
        if section.name not in {"__init__.py", "__main__.py"}
    ]
    return "\n".join([source, *sections])


def load_smoke_module(name: str, relative: str, *, include_sibling_path: bool = True, excluded_paths=()):
    """Load one smoke helper with only its sibling directory on sys.path."""
    path = (SMOKE / relative).resolve()
    cached = _MODULE_CACHE.get(path)
    if cached is not None:
        sys.modules[name] = cached
        return cached

    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    inserted_paths = []
    removed_paths = _remove_sys_path_entries({Path(item).resolve() for item in excluded_paths})
    sibling_path = str(path.parent)
    if include_sibling_path and sibling_path not in sys.path:
        sys.path.insert(0, sibling_path)
        inserted_paths.append(sibling_path)
    try:
        spec.loader.exec_module(module)
        _MODULE_CACHE[path] = module
    finally:
        for inserted_path in inserted_paths:
            try:
                sys.path.remove(inserted_path)
            except ValueError:
                pass
        _restore_sys_path_entries(removed_paths)
    return module


class NeverAdvertisesWebSocket:
    """Fake websocket whose receive call outlives the advertise timeout."""

    async def recv(self):
        """Sleep longer than any test advertise timeout."""
        await asyncio.sleep(60)


class AdvertisesMalformedAndValidChannel:
    """Fake websocket that first advertises malformed channels, then a valid one."""

    def __init__(self):
        """Initialize the scripted advertise frames."""
        self._frames = [
            json_bytes(
                {
                    "op": "advertise",
                    "channels": [
                        {"topic": "/tf"},
                        {"id": "not-an-int", "topic": "/tf"},
                        "bad-channel",
                    ],
                }
            ),
            json_bytes({"op": "advertise", "channels": [{"id": 7, "topic": "/tf"}]}),
        ]

    async def recv(self):
        """Return the next scripted frame."""
        if self._frames:
            return self._frames.pop(0)
        await asyncio.sleep(60)


class BinaryFrameFeed:
    """Minimal async websocket stand-in that emits a fixed frame sequence."""

    def __init__(self, frames: list[bytes]):
        """Initialize the scripted binary frames."""
        self._frames = list(frames)

    async def recv(self):
        """Return the next scripted binary frame."""
        if self._frames:
            return self._frames.pop(0)
        await asyncio.sleep(60)


def json_bytes(payload: dict) -> str:
    """Encode a JSON websocket text frame."""
    return json.dumps(payload)




__all__ = ['AdvertisesMalformedAndValidChannel', 'BinaryFrameFeed', 'NeverAdvertisesWebSocket', 'Path', 'ROOT', 'SMOKE', 'SimpleNamespace', '_MODULE_CACHE', '_SOURCE_CACHE', '_remove_sys_path_entries', '_resolved_sys_path', '_restore_sys_path_entries', 'annotations', 'asyncio', 'contextlib', 'importlib', 'io', 'json', 'json_bytes', 'load_smoke_module', 'mock', 'os', 'read_repo_source', 'read_repo_source_with_split_sections', 'read_source', 'read_source_with_split_sections', 'ssl', 'struct', 'subprocess', 'sys', 'tempfile', 'threading', 'time', 'unittest', 'urllib']
