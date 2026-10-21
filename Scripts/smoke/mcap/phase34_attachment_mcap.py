#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Generate and self-check a Phase 34 MCAP attachment smoke fixture.
# Usage: python Scripts/smoke/mcap/phase34_attachment_mcap.py --output build/test_mcap/phase34_attachment_smoke.mcap
# Inputs: Optional --output MCAP path.
# Outputs: Writes and self-checks a Phase 34 attachment MCAP fixture.

"""Generate and self-check a Phase 34 MCAP attachment smoke fixture."""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .phase34_attachment_mcap import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".phase34_attachment_mcap")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "phase34_attachment_mcap" else None
    if _phase192_self is not None:
        _phase192_sys.modules.pop("phase34_attachment_mcap", None)
    try:
        from phase34_attachment_mcap import *
        _phase192_package_module = _phase192_sys.modules.get("phase34_attachment_mcap")
    finally:
        if _phase192_self is not None:
            _phase192_sys.modules["phase34_attachment_mcap"] = _phase192_self

class _Phase192FacadeModule(_phase192_types.ModuleType):
    """Mirror facade attribute updates into the decomposed implementation package."""
    def __setattr__(self, name, value):
        """Update this facade and its implementation package."""
        super().__setattr__(name, value)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            setattr(_phase192_package_module, name, value)

    def __delattr__(self, name):
        """Remove an attribute from this facade and its implementation package."""
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
    _phase192_main = globals().get("main")
    if callable(_phase192_main):
        raise SystemExit(_phase192_main())
