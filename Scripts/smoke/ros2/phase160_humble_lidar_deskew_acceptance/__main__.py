"""Executable entrypoint for the decomposed Phase160 Humble acceptance helper."""

from __future__ import annotations

import sys

from . import main

if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase160-humble] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
