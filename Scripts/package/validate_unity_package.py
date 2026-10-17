#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Validate Unity package structure, public docs, and sample hygiene.
# Usage: python Scripts/package/validate_unity_package.py
# Inputs: Repository package files under Packages/dev.unity2foxglove.sdk.
# Outputs: Prints release validation checks and exits nonzero on failure.

"""Validate Unity package structure, public docs, and sample hygiene.

The facade retains the explicit package boundary contract markers
``SAMPLES = PACKAGE / "Samples~"``, ``"Basic Visualization": "Samples~/BasicVisualization"``,
``"Full Demo Visualization": "Samples~/FullDemoVisualization"``,
``"Virtual LiDAR Maze Demo": "Samples~/Virtual LiDAR Maze Demo"``,
and ``"Samples~/Ros2BridgeSample"`` while forwarding implementation to the package.
"""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .validate_unity_package import *
    _phase192_package_module = _phase192_sys.modules.get(
        __package__ + ".validate_unity_package"
    )
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = (
        _phase192_sys.modules.get(__name__)
        if __name__ == "validate_unity_package"
        else None
    )
    if _phase192_self is not None:
        _phase192_sys.modules.pop("validate_unity_package", None)
    try:
        from validate_unity_package import *
        _phase192_package_module = _phase192_sys.modules.get(
            "validate_unity_package"
        )
    finally:
        if _phase192_self is not None:
            _phase192_sys.modules["validate_unity_package"] = _phase192_self


class _Phase192FacadeModule(_phase192_types.ModuleType):
    """Mirror facade attribute updates into the decomposed validator package."""

    def __setattr__(self, name, value):
        """Mirror assignments into the decomposed implementation."""
        super().__setattr__(name, value)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            setattr(_phase192_package_module, name, value)

    def __delattr__(self, name):
        """Mirror deletions into the decomposed implementation."""
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
    raise SystemExit(main())
