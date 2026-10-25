#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/ros2forunity/windows/lyrical
# Purpose: Phase146B R2FU Lyrical Windows build toolchain orchestrator facade.

"""Run the deterministic Phase146B R2FU Lyrical Windows build orchestrator."""

from __future__ import annotations

import importlib.util
from pathlib import Path
import sys

_IMPLEMENTATION_PATH = Path(__file__).with_name(
    "phase146b_r2fu_lyrical_windows_build"
) / "implementation.py"
_SPEC = importlib.util.spec_from_file_location(
    "phase146b_r2fu_lyrical_windows_build.implementation",
    _IMPLEMENTATION_PATH,
)
if _SPEC is None or _SPEC.loader is None:
    raise ImportError(f"Unable to load Phase146B implementation from {_IMPLEMENTATION_PATH}")
_IMPLEMENTATION = importlib.util.module_from_spec(_SPEC)
sys.modules[_SPEC.name] = _IMPLEMENTATION
_SPEC.loader.exec_module(_IMPLEMENTATION)

for _name, _value in vars(_IMPLEMENTATION).items():
    if not _name.startswith("__"):
        globals()[_name] = _value

if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
