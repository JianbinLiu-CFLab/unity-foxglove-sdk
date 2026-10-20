"""Shared loader and path fixtures for Lyrical runtime validator tests."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[6]
VALIDATOR_PATH = ROOT / "Scripts" / "ros2forunity" / "windows" / "lyrical" / "validate_r2fu_runtime_package.py"


def load_validator_module():
    """Load the runtime package validator module under test."""
    spec = importlib.util.spec_from_file_location("validate_r2fu_runtime_package", VALIDATOR_PATH)
    module = importlib.util.module_from_spec(spec)
    if spec.loader is None:
        raise RuntimeError(f"Cannot load validator module: {VALIDATOR_PATH}")
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


__all__ = [
    "Path",
    "ROOT",
    "VALIDATOR_PATH",
    "hashlib",
    "importlib",
    "json",
    "load_validator_module",
    "mock",
    "shutil",
    "subprocess",
    "sys",
    "tempfile",
    "unittest",
]
