from __future__ import annotations

import sys

from . import main

if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase181-characterization] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
