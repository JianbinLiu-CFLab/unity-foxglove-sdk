"""Shared loaders and paths for Unity package validation tests."""

from __future__ import annotations

import importlib.util
import inspect
import json
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[4]
VALIDATE_PACKAGE_PATH = ROOT / "Scripts" / "package" / "validate_unity_package.py"
VALIDATE_SOURCE_GENERATOR_PATH = ROOT / "Scripts" / "package" / "validate_source_generator_dll.py"


def load_module(name: str, path: Path):
    """Load a Python script as an isolated module."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


__all__ = [
    "ET",
    "Path",
    "ROOT",
    "VALIDATE_PACKAGE_PATH",
    "VALIDATE_SOURCE_GENERATOR_PATH",
    "importlib",
    "inspect",
    "json",
    "load_module",
    "mock",
    "subprocess",
    "sys",
    "tempfile",
    "unittest",
]
