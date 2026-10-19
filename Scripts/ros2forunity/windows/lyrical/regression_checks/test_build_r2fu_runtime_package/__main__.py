#!/usr/bin/env python3
"""Executable entrypoint for decomposed Lyrical runtime-package tests."""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

_PHASE192_REPO_ROOT = Path(__file__).resolve().parents[6]
if str(_PHASE192_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_PHASE192_REPO_ROOT))

try:
    from .test_build_r2fu_runtime_package import *
except ImportError:
    from test_build_r2fu_runtime_package import *

if __name__ == "__main__":
    raise SystemExit(unittest.main(module=__name__))
