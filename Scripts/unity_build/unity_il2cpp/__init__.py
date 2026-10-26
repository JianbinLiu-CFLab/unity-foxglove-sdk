#!/usr/bin/env python3
from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types
from pathlib import Path as _Phase192Path

_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "unity_il2cpp.py"
__file__ = str(_PHASE192_FACADE_FILE)
_PHASE192_PARENT = _PHASE192_FACADE_FILE.parent
if str(_PHASE192_PARENT) not in _phase192_sys.path:
    _phase192_sys.path.insert(0, str(_PHASE192_PARENT))

from . import implementation as _phase192_section_0

_phase192_section_0.REPO_ROOT_PARENT_DEPTH = 3
_PHASE192_SECTION_MODULES = (_phase192_section_0,)
_PHASE192_EXPORT_NAMES = set()
for _phase192_section in _PHASE192_SECTION_MODULES:
    for _phase192_name in _phase192_section.__all__:
        globals()[_phase192_name] = getattr(_phase192_section, _phase192_name)
        _PHASE192_EXPORT_NAMES.add(_phase192_name)
for _phase192_section in _PHASE192_SECTION_MODULES:
    for _phase192_name in _PHASE192_EXPORT_NAMES:
        _phase192_section.__dict__[_phase192_name] = globals()[_phase192_name]


class _Phase192PackageModule(_phase192_types.ModuleType):
    """Forward package mutations to the implementation module."""

    def __setattr__(self, name, value):
        """Propagate exported state changes to the implementation module."""
        super().__setattr__(name, value)
        if not name.startswith("__") and not name.startswith("_phase192_") and name not in {"_PHASE192_SECTION_MODULES", "_PHASE192_EXPORT_NAMES"}:
            _phase192_section_0.__dict__[name] = value
            for _phase192_split_section in _phase192_section_0._PHASE192_SECTION_MODULES:
                _phase192_split_section.__dict__[name] = value

    def __delattr__(self, name):
        """Propagate exported state removals to the implementation module."""
        super().__delattr__(name)
        if not name.startswith("__") and not name.startswith("_phase192_") and name not in {"_PHASE192_SECTION_MODULES", "_PHASE192_EXPORT_NAMES"}:
            _phase192_section_0.__dict__.pop(name, None)
            for _phase192_split_section in _phase192_section_0._PHASE192_SECTION_MODULES:
                _phase192_split_section.__dict__.pop(name, None)


__all__ = sorted(_PHASE192_EXPORT_NAMES)
_phase192_module = _phase192_sys.modules.get(__name__)
if _phase192_module is not None:
    _phase192_module.__class__ = _Phase192PackageModule
