#!/usr/bin/env python3
"""Executable entrypoint for decomposed Lyrical runtime validator tests."""

from __future__ import annotations

import unittest
import sys
from pathlib import Path

_PHASE192_REPO_ROOT = Path(__file__).resolve().parents[6]
if str(_PHASE192_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_PHASE192_REPO_ROOT))


if __name__ == "__main__":
    raise SystemExit(unittest.main(module="Scripts.ros2forunity.windows.lyrical.regression_checks.test_validate_r2fu_runtime_package"))
