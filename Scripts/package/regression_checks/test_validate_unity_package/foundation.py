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
    """Load one maintained validator package fresh for the test case."""
    del name
    module_name = (
        "Scripts.package.validate_unity_package"
        if path == VALIDATE_PACKAGE_PATH
        else "Scripts.package.validate_source_generator_dll"
    )
    return importlib.reload(importlib.import_module(module_name))


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
