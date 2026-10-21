#!/usr/bin/env python3
"""Executable entrypoint for decomposed regression tests."""

from __future__ import annotations

import unittest
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[6]))
from Scripts.ros2forunity.windows.lyrical.regression_checks.test_build_r2fu_runtime_package import *
 
if __name__ == "__main__":
    raise SystemExit(unittest.main(module=__name__))
