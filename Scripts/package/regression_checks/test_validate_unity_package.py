#!/usr/bin/env python3
"""Compatibility facade for decomposed Unity package validation tests."""

from __future__ import annotations

from .test_validate_unity_package import *

if __name__ == "__main__":
    import unittest
    raise SystemExit(unittest.main(module=__name__))
