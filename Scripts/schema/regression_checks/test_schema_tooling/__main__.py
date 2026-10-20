#!/usr/bin/env python3
"""Executable entrypoint for decomposed regression tests."""

from __future__ import annotations

import importlib
import sys
import unittest
from pathlib import Path


if __package__:
    from . import *
    _phase192_package = importlib.import_module(__package__)
else:
    _phase192_repo_root = Path(__file__).resolve().parents[4]
    sys.path.insert(0, str(_phase192_repo_root))
    _phase192_package = importlib.import_module(
        "Scripts.schema.regression_checks.test_schema_tooling"
    )

if __name__ == "__main__":
    _phase192_suite = unittest.defaultTestLoader.loadTestsFromModule(_phase192_package)
    _phase192_result = unittest.TextTestRunner(verbosity=1).run(_phase192_suite)
    raise SystemExit(0 if _phase192_result.wasSuccessful() else 1)
