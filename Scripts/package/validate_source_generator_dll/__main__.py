#!/usr/bin/env python3
"""Executable entrypoint for source-generator validation."""

from __future__ import annotations

import sys

from . import main

if __name__ == "__main__":
    sys.argv[0] = "validate_source_generator_dll.py"
    raise SystemExit(main())
