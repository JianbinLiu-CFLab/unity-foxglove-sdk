#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for schema generation helper edge cases.

from __future__ import annotations

import contextlib
import hashlib
import importlib.util
import io
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]


from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "test_schema_tooling.py"
__file__ = str(_PHASE192_FACADE_FILE)
ROOT = Path(__file__).resolve().parents[3]
del _Phase192Path, _PHASE192_FACADE_FILE

def load_module(name: str, relative: str):
    """Load one repository helper script as an isolated module."""
    path = ROOT / relative
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    original_path = list(sys.path)
    sys.path.insert(0, str(path.parent))
    try:
        spec.loader.exec_module(module)
    finally:
        sys.path[:] = original_path
    return module




_phase192_load_tests_used = False

def load_tests(loader, tests, pattern):
    """Collect split facade tests once during unittest discovery."""
    global _phase192_load_tests_used
    if pattern is None:
        return tests
    if _phase192_load_tests_used:
        return ()
    _phase192_load_tests_used = True
    return tests

__all__ = [name for name in globals() if not name.startswith("__")]
