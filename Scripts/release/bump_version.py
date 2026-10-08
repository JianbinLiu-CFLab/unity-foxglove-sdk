"""Synchronize Unity2Foxglove package version references.

This script updates the package version, the runtime package-metadata
validation assertion, README badges/notes, and release document stubs.
It intentionally does not create git commits, tags, or GitHub releases.
"""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .bump_version import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".bump_version")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "bump_version" else None
    if _phase192_self is not None: _phase192_sys.modules.pop("bump_version", None)
    try:
        from bump_version import *
        _phase192_package_module = _phase192_sys.modules.get("bump_version")
    finally:
        if _phase192_self is not None: _phase192_sys.modules["bump_version"] = _phase192_self

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
