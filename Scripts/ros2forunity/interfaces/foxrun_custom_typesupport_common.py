#!/usr/bin/env python3
"""Shared validation primitives for Phase181 custom ROS2 typesupport add-ons.

The add-on package is intentionally a verified artifact layer.  This module
contains no build invocation, ROS environment mutation, or Unity API use; it
only compares declared package data against the selected static interface and
base runtime inputs.
"""


from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .foxrun_custom_typesupport_common import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".foxrun_custom_typesupport_common")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "foxrun_custom_typesupport_common" else None
    if _phase192_self is not None:
        _phase192_sys.modules.pop("foxrun_custom_typesupport_common", None)
    try:
        from foxrun_custom_typesupport_common import *
        _phase192_package_module = _phase192_sys.modules.get("foxrun_custom_typesupport_common")
    finally:
        if _phase192_self is not None:
            _phase192_sys.modules["foxrun_custom_typesupport_common"] = _phase192_self

class _Phase192FacadeModule(_phase192_types.ModuleType):
    """Mirror facade attribute updates into the decomposed package."""

    def __setattr__(self, name, value):
        """Mirror facade attribute writes into the decomposed package."""
        super().__setattr__(name, value)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            setattr(_phase192_package_module, name, value)

    def __delattr__(self, name):
        """Mirror facade attribute deletions into the decomposed package."""
        super().__delattr__(name)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            try:
                delattr(_phase192_package_module, name)
            except AttributeError:
                pass

_phase192_module = _phase192_sys.modules.get(__name__)
if _phase192_module is not None:
    _phase192_module.__class__ = _Phase192FacadeModule
