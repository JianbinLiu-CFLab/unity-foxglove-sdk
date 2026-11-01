from __future__ import annotations

import ast
import hashlib
import io
import os
import subprocess
import sys
import tarfile
import tempfile
import types
import unittest
from contextlib import contextmanager
from unittest import mock
from pathlib import Path

from Scripts.validation.source_decomposition import compare_identity_surfaces, identity_contracts, sensitivity_proof, source_layout



_PHASE192_FACADE_FILE = Path(__file__).resolve().parents[1] / "test_identity_tooling.py"
__file__ = str(_PHASE192_FACADE_FILE)


class IdentityToolingBase(unittest.TestCase):
    """Shared base for decomposed identity tooling regression tests."""


def _phase192_recursive_package_discovery() -> bool:
    """Return whether unittest is loading the duplicate package path during discovery."""
    try:
        caller = sys._getframe(3)
    except ValueError:
        return False
    return caller.f_code.co_name == "_find_test_path" and caller.f_locals.get("module") is not None


def load_tests(loader, tests, pattern):
    """Suppress only the duplicate package discovery within one loader traversal."""
    if pattern is None or not _phase192_recursive_package_discovery():
        return tests
    return loader.suiteClass()


__all__ = [
    "Path",
    "__file__",
    "annotations",
    "ast",
    "compare_identity_surfaces",
    "contextmanager",
    "hashlib",
    "IdentityToolingBase",
    "identity_contracts",
    "io",
    "load_tests",
    "mock",
    "os",
    "sensitivity_proof",
    "source_layout",
    "subprocess",
    "sys",
    "tarfile",
    "tempfile",
    "types",
    "unittest",
]
