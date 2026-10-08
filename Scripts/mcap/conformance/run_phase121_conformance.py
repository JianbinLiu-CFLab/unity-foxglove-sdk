#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Compatibility facade for the decomposed Phase121 MCAP conformance package."""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .run_phase121_conformance import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".run_phase121_conformance")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "run_phase121_conformance" else None
    if _phase192_self is not None:
        _phase192_sys.modules.pop("run_phase121_conformance", None)
    try:
        from run_phase121_conformance import *
        _phase192_package_module = _phase192_sys.modules.get("run_phase121_conformance")
    finally:
        if _phase192_self is not None:
            _phase192_sys.modules["run_phase121_conformance"] = _phase192_self

class _Phase192FacadeModule(_phase192_types.ModuleType):
    """Mirror facade attribute updates into the decomposed package."""

    def __setattr__(self, name, value):
        super().__setattr__(name, value)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            setattr(_phase192_package_module, name, value)

    def __delattr__(self, name):
        super().__delattr__(name)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            try:
                delattr(_phase192_package_module, name)
            except AttributeError:
                pass

_phase192_module = _phase192_sys.modules.get(__name__)
if _phase192_module is not None:
    _phase192_module.__class__ = _Phase192FacadeModule

if __name__ == "__main__":
    try:
        raise SystemExit(main(_phase192_sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase121-conformance] FAIL: {exc}", file=_phase192_sys.stderr)
        raise SystemExit(1)
