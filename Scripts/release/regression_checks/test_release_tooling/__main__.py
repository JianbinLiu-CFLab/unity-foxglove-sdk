#!/usr/bin/env python3
"""Executable entrypoint for decomposed release-tooling regression tests."""

from __future__ import annotations

import unittest


if __name__ == "__main__":
    raise SystemExit(unittest.main(module="Scripts.release.regression_checks.test_release_tooling"))
