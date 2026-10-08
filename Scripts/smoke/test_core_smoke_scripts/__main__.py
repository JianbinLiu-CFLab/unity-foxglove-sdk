#!/usr/bin/env python3
"""Executable entrypoint for decomposed core smoke tests."""

from __future__ import annotations

import unittest

if __name__ == "__main__":
    raise SystemExit(unittest.main(module="Scripts.smoke.test_core_smoke_scripts"))
