#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Phase162 Lyrical Zenoh RViz2 acceptance helper.

"""Run Lyrical Zenoh RViz2 PointCloud2 acceptance against Unity/R2FU.

Start Unity Play Mode first with ROS2 Native (R2FU), Lyrical Win64, Zenoh
communication mode, PointCloud2 Native output, and deskew enabled. Bare runs
launch RViz2 and validate the raw + deskewed PointCloud2 topics. Pass
--echo-only for the older one-frame CLI smoke.
"""

from __future__ import annotations

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .phase162_lyrical_zenoh_player_smoke import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".phase162_lyrical_zenoh_player_smoke")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "phase162_lyrical_zenoh_player_smoke" else None
    if _phase192_self is not None: _phase192_sys.modules.pop("phase162_lyrical_zenoh_player_smoke", None)
    try:
        from phase162_lyrical_zenoh_player_smoke import *
        _phase192_package_module = _phase192_sys.modules.get("phase162_lyrical_zenoh_player_smoke")
    finally:
        if _phase192_self is not None: _phase192_sys.modules["phase162_lyrical_zenoh_player_smoke"] = _phase192_self

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
