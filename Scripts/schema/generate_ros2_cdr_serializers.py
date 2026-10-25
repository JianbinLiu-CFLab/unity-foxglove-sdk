#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Generate direct ROS 2 CDR serializers/deserializers for official Foxglove .msg schemas.
# Usage: python Scripts/schema/generate_ros2_cdr_serializers.py
# Inputs: third-party/foxglove-sdk/schemas/ros2/*.msg and generated Foxglove protobuf C# classes.
# Outputs: Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/*.g.cs.
"""Compatibility facade for the decomposed ROS 2 CDR generator."""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .generate_ros2_cdr_serializers import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".generate_ros2_cdr_serializers")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    _phase192_repo_root = _Phase192Path(__file__).resolve().parents[2]
    for _phase192_path in (_phase192_parent, _phase192_repo_root):
        if str(_phase192_path) not in _phase192_sys.path:
            _phase192_sys.path.insert(0, str(_phase192_path))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "generate_ros2_cdr_serializers" else None
    if _phase192_self is not None:
        _phase192_sys.modules.pop("generate_ros2_cdr_serializers", None)
    try:
        from generate_ros2_cdr_serializers import *
        _phase192_package_module = _phase192_sys.modules.get("generate_ros2_cdr_serializers")
    finally:
        if _phase192_self is not None:
            _phase192_sys.modules["generate_ros2_cdr_serializers"] = _phase192_self

class _Phase192FacadeModule(_phase192_types.ModuleType):
    """Forward facade mutations to the decomposed CDR generator package."""

    def __setattr__(self, name, value):
        """Mirror facade assignments into the implementation module."""
        super().__setattr__(name, value)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            setattr(_phase192_package_module, name, value)

    def __delattr__(self, name):
        """Mirror facade deletions into the implementation module."""
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
