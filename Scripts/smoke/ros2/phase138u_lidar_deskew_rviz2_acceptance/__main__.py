"""Executable entrypoint for the decomposed Phase138U acceptance helper."""

from __future__ import annotations

import sys

from .acceptance_cli import InconclusiveError, main

if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except InconclusiveError as exc:
        print(f"[phase138u-lidar-deskew] INCONCLUSIVE: {exc}", file=sys.stderr)
        raise SystemExit(2)
    except Exception as exc:
        print(f"[phase138u-lidar-deskew] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
