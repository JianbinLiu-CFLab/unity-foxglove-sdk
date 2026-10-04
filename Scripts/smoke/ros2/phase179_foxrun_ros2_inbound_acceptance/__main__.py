"""Executable entrypoint for the Phase179 inbound acceptance package."""

from __future__ import annotations

import sys

from . import main

if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
