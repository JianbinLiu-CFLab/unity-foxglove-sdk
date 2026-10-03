"""Compatibility facade for the Phase192 decomposed source package."""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .phase184_profile_acceptance_protocol import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + "." + "phase184_profile_acceptance_protocol")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "phase184_profile_acceptance_protocol" else None
    if _phase192_self is not None: _phase192_sys.modules.pop("phase184_profile_acceptance_protocol", None)
    try:
        from phase184_profile_acceptance_protocol import *
        _phase192_package_module = _phase192_sys.modules.get("phase184_profile_acceptance_protocol")
    finally:
        if _phase192_self is not None: _phase192_sys.modules["phase184_profile_acceptance_protocol"] = _phase192_self

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
