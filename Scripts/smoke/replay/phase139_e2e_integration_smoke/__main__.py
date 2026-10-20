"""Run the decomposed Phase139 E2E smoke helper as a module."""

from __future__ import annotations

import sys

from . import main

if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
