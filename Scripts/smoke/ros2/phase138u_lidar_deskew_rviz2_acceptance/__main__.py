"""Executable entrypoint for the decomposed Phase138U acceptance helper."""

from __future__ import annotations

import sys

from .acceptance_cli import run_cli

if __name__ == "__main__":
    raise SystemExit(run_cli(sys.argv[1:]))
