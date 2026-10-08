"""Executable entrypoint for the decomposed Phase138T RViz2 helper."""

from __future__ import annotations

import sys

from . import main

if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
