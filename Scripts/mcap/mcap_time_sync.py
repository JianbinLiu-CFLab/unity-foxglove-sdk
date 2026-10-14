#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Offline MCAP timestamp sync validation helper.

This script checks timestamp coherence for point-cloud and IMU streams:

* Point-cloud topic logTime vs payload timestamp
* IMU payload timestamp vs point-cloud payload timestamp (nearest-neighbor)

Common usage:

    python Scripts/mcap/mcap_time_sync.py
    python Scripts/mcap/mcap_time_sync.py --mcap Unity2Foxglove/Recordings/my_run.mcap
    python Scripts/mcap/mcap_time_sync.py --skip-frames 80
"""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .mcap_time_sync import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".mcap_time_sync")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "mcap_time_sync" else None
    if _phase192_self is not None: _phase192_sys.modules.pop("mcap_time_sync", None)
    try:
        from mcap_time_sync import *
        _phase192_package_module = _phase192_sys.modules.get("mcap_time_sync")
    finally:
        if _phase192_self is not None: _phase192_sys.modules["mcap_time_sync"] = _phase192_self

class _Phase192FacadeModule(_phase192_types.ModuleType):
    """Mirror facade attribute updates into the decomposed implementation module."""
    def __setattr__(self, name, value):
        """Update this facade and its implementation module."""
        super().__setattr__(name, value)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            setattr(_phase192_package_module, name, value)
    def __delattr__(self, name):
        """Remove an attribute from this facade and its implementation module."""
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
