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
