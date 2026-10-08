"""Shared loader and filesystem fixtures for sample synchronization tests."""

from __future__ import annotations

import importlib.util
import json
import os
import re
import stat
import sys
import tempfile
import unittest
from io import StringIO
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[4]


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


__all__ = [
    "Path",
    "ROOT",
    "StringIO",
    "importlib",
    "json",
    "load_module",
    "mock",
    "os",
    "re",
    "stat",
    "sys",
    "tempfile",
    "unittest",
]
