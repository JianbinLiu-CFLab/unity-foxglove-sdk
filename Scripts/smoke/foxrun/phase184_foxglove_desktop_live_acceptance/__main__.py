from __future__ import annotations

import sys
from pathlib import Path

from . import main

if __name__ == "__main__":
    sys.argv[0] = str(Path(__file__).resolve().parents[1] / "phase184_foxglove_desktop_live_acceptance.py")
    raise SystemExit(main())
