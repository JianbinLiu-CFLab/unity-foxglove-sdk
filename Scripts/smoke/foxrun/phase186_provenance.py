"""Compatibility facade for the Phase192 decomposed source package."""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .phase186_provenance import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + "." + "phase186_provenance")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "phase186_provenance" else None
    if _phase192_self is not None: _phase192_sys.modules.pop("phase186_provenance", None)
    try:
        from phase186_provenance import *
        _phase192_package_module = _phase192_sys.modules.get("phase186_provenance")
    finally:
        if _phase192_self is not None: _phase192_sys.modules["phase186_provenance"] = _phase192_self

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
