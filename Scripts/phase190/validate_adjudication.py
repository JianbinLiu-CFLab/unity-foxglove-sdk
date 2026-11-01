"""Legacy entry point for validation.conformance.validate_adjudication."""

from __future__ import annotations

import sys
from pathlib import Path

_REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
if str(_REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPOSITORY_ROOT))

# ADJUDICATION_VALID
from Scripts.validation.conformance.validate_adjudication import *

if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
