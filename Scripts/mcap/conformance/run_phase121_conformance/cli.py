#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from . import configuration as _configuration
from . import checkout_reporting as _checkout
from . import execution as _execution
from .configuration import *
from .checkout_reporting import *
from .execution import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "run_phase121_conformance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def main(argv: list[str]) -> int:
    """Program entry point."""

    args = _configuration.parse_args(argv)
    if args.report_path:
        _configuration.REPORT_PATH = Path(args.report_path).resolve()
    if args.ci_smoke:
        _checkout.write_ci_smoke_report()
        return 0
    return _execution.run_conformance(bool(args.release_blocking), args.official_root)

__all__ = [name for name in globals() if not name.startswith("__")]
