"""Shared fixtures and loader support for the Phase186 package-matrix tests."""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[4]
VALIDATOR_PATH = ROOT / "Scripts/package/validate_phase186_package_matrix.py"


def load_module(name: str, path: Path):
    """Load one validation script as an isolated module."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


__all__ = [
    "Path",
    "ROOT",
    "VALIDATOR_PATH",
    "importlib",
    "json",
    "load_module",
    "mock",
    "subprocess",
    "sys",
    "tempfile",
    "unittest",
]
