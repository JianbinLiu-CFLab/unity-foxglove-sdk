#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Validate the optional ROS2 For Unity package boundary.
# Usage: python Scripts/ros2forunity/windows/jazzy/validate_ros2forunity_package.py
# Inputs: Packages/dev.unity2foxglove.ros2forunity package metadata and compliance manifest.
# Outputs: Prints package boundary checks and exits nonzero on failure.

"""Compatibility facade for the Phase192 decomposed source package."""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

_phase192_module = _phase192_sys.modules.get(__name__)
_phase192_canonical_self = (
    _phase192_sys.modules.get(__name__)
    if __package__ and _phase192_sys.modules.get(__name__) is not None
    else None
)
_phase192_previous_submodules = {
    name: _phase192_sys.modules[name]
    for name in _phase192_sys.modules
    if name == "validate_ros2forunity_package"
    or name.startswith("validate_ros2forunity_package.")
}
_phase192_previous_path = _phase192_sys.path[:]
_phase192_previous_name = __name__ if _phase192_canonical_self is not None else None
_phase192_previous_package = __package__ if _phase192_canonical_self is not None else None

try:
    if _phase192_canonical_self is not None:
        _phase192_canonical_self.__package__ = ""
        _phase192_canonical_self.__name__ = "validate_ros2forunity_package"
        _phase192_sys.modules["validate_ros2forunity_package"] = _phase192_canonical_self
        _phase192_sys.modules.pop("validate_ros2forunity_package", None)

    for _phase192_name in tuple(_phase192_sys.modules):
        if (
            _phase192_name == "validate_ros2forunity_package"
            or _phase192_name.startswith("validate_ros2forunity_package.")
        ):
            _phase192_sys.modules.pop(_phase192_name, None)

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
finally:
    _phase192_sys.path[:] = _phase192_previous_path
    for _phase192_name in tuple(_phase192_sys.modules):
        if (
            (
                _phase192_name == "validate_ros2forunity_package"
                or _phase192_name.startswith("validate_ros2forunity_package.")
            )
            and _phase192_name not in _phase192_previous_submodules
        ):
            _phase192_sys.modules.pop(_phase192_name, None)
    for _phase192_name in _phase192_previous_submodules:
        _phase192_sys.modules[_phase192_name] = _phase192_previous_submodules[_phase192_name]
    if _phase192_canonical_self is not None:
        setattr(_phase192_canonical_self, "__name__", _phase192_previous_name)
        setattr(_phase192_canonical_self, "__package__", _phase192_previous_package)

__package__ = __spec__.parent if __spec__ is not None else __package__
__name__ = __spec__.name if __spec__ is not None else __name__

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
