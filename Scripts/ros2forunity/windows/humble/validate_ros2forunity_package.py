#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Validate the optional ROS2 For Unity package boundary.
# Usage: python Scripts/ros2forunity/windows/humble/validate_ros2forunity_package.py
# Inputs: Packages/dev.unity2foxglove.ros2forunity package metadata and compliance manifest.
# Outputs: Prints package boundary checks and exits nonzero on failure.

"""Compatibility facade for the Phase192 decomposed source package."""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

_phase192_sys.modules.pop("validate_ros2forunity_package", None) if __name__ in {"__main__", "<run_path>"} else None

if __package__:
    from .validate_ros2forunity_package import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".validate_ros2forunity_package")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "validate_ros2forunity_package" else None
    if _phase192_self is not None:
        _phase192_sys.modules.pop("validate_ros2forunity_package", None)
    try:
        from validate_ros2forunity_package import *
        _phase192_package_module = _phase192_sys.modules.get("validate_ros2forunity_package")
    finally:
        if _phase192_self is not None:
            _phase192_sys.modules["validate_ros2forunity_package"] = _phase192_self

class _Phase192FacadeModule(_phase192_types.ModuleType):
    """Mirror facade state into the decomposed implementation package."""
    def __setattr__(self, name, value):
        """Update this facade and its implementation package."""
        super().__setattr__(name, value)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            setattr(_phase192_package_module, name, value)
    def __delattr__(self, name):
        """Remove this facade attribute and its implementation package value."""
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
