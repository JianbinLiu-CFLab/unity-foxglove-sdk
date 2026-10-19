"""Executable entrypoint for the decomposed schema regression package."""

from __future__ import annotations

import unittest
import sys
from pathlib import Path

_PHASE192_REPO_ROOT = Path(__file__).resolve().parents[4]
if str(_PHASE192_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_PHASE192_REPO_ROOT))


try:
    from .test_schema_tooling import *
except ImportError:
    from test_schema_tooling import *

if __name__ == "__main__":
    raise SystemExit(unittest.main(module=__name__))
