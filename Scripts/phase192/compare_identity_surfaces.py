"""Legacy entry point for source-decomposition identity comparison."""

from __future__ import annotations

import sys
from pathlib import Path

_REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
if str(_REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPOSITORY_ROOT))

from Scripts.validation.source_decomposition.compare_identity_surfaces import *

if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
