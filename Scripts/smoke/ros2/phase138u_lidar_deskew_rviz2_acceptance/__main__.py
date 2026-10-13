"""Executable entrypoint for the decomposed Phase138U acceptance helper."""

from __future__ import annotations

import sys

from .acceptance_cli import main

if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
