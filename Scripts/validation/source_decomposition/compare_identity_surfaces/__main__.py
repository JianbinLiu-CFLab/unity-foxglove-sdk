#!/usr/bin/env python3
"""Executable entrypoint for decomposed Phase192 identity comparison."""

from __future__ import annotations

import sys

from . import main

if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
