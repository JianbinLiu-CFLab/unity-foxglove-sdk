#!/usr/bin/env python3
"""Executable entrypoint for decomposed regression tests."""

from __future__ import annotations

import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[6]))
from Scripts.ros2forunity.windows.lyrical.regression_checks.test_validate_r2fu_runtime_package import *
 
if __name__ == "__main__":
    raise SystemExit(unittest.main(module=__name__))
