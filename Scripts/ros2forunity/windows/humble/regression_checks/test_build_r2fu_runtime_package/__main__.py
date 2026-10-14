#!/usr/bin/env python3
"""Executable entrypoint for the decomposed Humble runtime-package tests."""

from __future__ import annotations

import unittest

if __name__ == "__main__":
    raise SystemExit(unittest.main(module="Scripts.ros2forunity.windows.humble.regression_checks.test_build_r2fu_runtime_package"))
