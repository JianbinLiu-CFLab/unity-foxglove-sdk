#!/usr/bin/env python3
"""Executable entrypoint for Unity package validation."""

from __future__ import annotations

import sys

from . import main

if __name__ == "__main__":
    sys.argv[0] = "validate_unity_package.py"
    raise SystemExit(main())
