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

from Scripts.phase192 import compare_identity_surfaces, identity_contracts, sensitivity_proof, source_layout



_PHASE192_FACADE_FILE = Path(__file__).resolve().parents[1] / "test_identity_tooling.py"
__file__ = str(_PHASE192_FACADE_FILE)


class IdentityToolingBase(unittest.TestCase):
    """Shared base for decomposed identity tooling regression tests."""


_phase192_load_tests_used = False


def load_tests(loader, tests, pattern):
    """Collect split identity tooling tests once during discovery."""
    global _phase192_load_tests_used
    if pattern is None:
        return tests
    if _phase192_load_tests_used:
        return loader.suiteClass()
    _phase192_load_tests_used = True
    return tests


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
