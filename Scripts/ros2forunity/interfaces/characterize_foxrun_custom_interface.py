#!/usr/bin/env python3
"""Build and characterize the Phase181 static ROS2 interface out of tree.

This is deliberately a characterization gate, not package generation.  It
uses an explicitly selected ROS2 root, ros2cs overlay, and Visual Studio
installation to build the source-only package into ``build/phase181``.  The
only products are machine-local evidence and candidate files below that build
root; no Unity package is modified here.
"""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .characterize_foxrun_custom_interface import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".characterize_foxrun_custom_interface")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "characterize_foxrun_custom_interface" else None
    if _phase192_self is not None:
        _phase192_sys.modules.pop("characterize_foxrun_custom_interface", None)
    try:
        from characterize_foxrun_custom_interface import *
        _phase192_package_module = _phase192_sys.modules.get("characterize_foxrun_custom_interface")
    finally:
        if _phase192_self is not None:
            _phase192_sys.modules["characterize_foxrun_custom_interface"] = _phase192_self

class _Phase192FacadeModule(_phase192_types.ModuleType):
    """Mirror facade attribute updates into the decomposed package."""

    def __setattr__(self, name, value):
        """Update this facade and the decomposed implementation module."""
        super().__setattr__(name, value)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            setattr(_phase192_package_module, name, value)

    def __delattr__(self, name):
        """Remove an attribute from this facade and the implementation module."""
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
    raise SystemExit(cli_entry(_phase192_sys.argv[1:]))
