"""Executable entrypoint for the decomposed Phase138S smoke package."""

from __future__ import annotations

import sys
import subprocess

from . import main

if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except (RuntimeError, TimeoutError, FileNotFoundError, subprocess.TimeoutExpired) as exc:
        print(f"[phase138s-imu-dds] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1) from exc
