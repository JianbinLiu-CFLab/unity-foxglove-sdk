"""Shared loader and path fixtures for Lyrical runtime-package tests."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[6]
assert (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "package.json").exists(), (
    f"Repo root resolution failed: {ROOT}"
)
BUILDER_PATH = ROOT / "Scripts" / "ros2forunity" / "windows" / "lyrical" / "build_r2fu_runtime_package.py"


def load_builder_module():
    """Load the runtime package builder module under test."""
    spec = importlib.util.spec_from_file_location("build_r2fu_runtime_package", BUILDER_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


_phase192_load_tests_used = False

def load_tests(loader, tests, pattern):
    """Collect split facade tests once during unittest discovery."""
    global _phase192_load_tests_used
    if pattern is None:
        return tests
    if _phase192_load_tests_used:
        return loader.suiteClass()
    _phase192_load_tests_used = True
    return tests

def _phase192_package_getattr(name):
    """Expose the single-pass discovery hook without changing package exports."""
    if name == "load_tests":
        return load_tests
    raise AttributeError(name)

_phase192_package_module = sys.modules.get(__package__)
if _phase192_package_module is not None:
    _phase192_package_module.__getattr__ = _phase192_package_getattr
del _phase192_package_module

__all__ = [
    "BUILDER_PATH",
    "Path",
    "ROOT",
    "hashlib",
    "importlib",
    "json",
    "load_builder_module",
    "mock",
    "sys",
    "tempfile",
    "unittest",
    "zipfile",
]
