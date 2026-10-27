#!/usr/bin/env python3
from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types
_phase192_module = _phase192_sys.modules[__name__]
if __name__ == "unity_il2cpp":
    try:
        import Scripts.unity_build
    except ImportError:
        pass
for _phase192_alias in ("unity_il2cpp", "Scripts.unity_build.unity_il2cpp"):
    _phase192_sys.modules.setdefault(_phase192_alias, _phase192_module)

from . import implementation as _phase192_section_0

for _phase192_suffix in (
    "implementation",
    "parts",
    "parts.foundation",
    "parts.il2cpp_logging",
    "parts.process",
    "parts.cli",
):
    _phase192_source_name = f"{__name__}.{_phase192_suffix}"
    _phase192_source_module = _phase192_sys.modules.get(_phase192_source_name)
    if _phase192_source_module is not None:
        for _phase192_alias_prefix in ("unity_il2cpp", "Scripts.unity_build.unity_il2cpp"):
            _phase192_sys.modules.setdefault(f"{_phase192_alias_prefix}.{_phase192_suffix}", _phase192_source_module)

_phase192_section_0.REPO_ROOT_PARENT_DEPTH = 4
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

    def __delattr__(self, name):
        """Propagate exported state removals to the implementation module."""
        super().__delattr__(name)
        if not name.startswith("__") and not name.startswith("_phase192_") and name not in {"_PHASE192_SECTION_MODULES", "_PHASE192_EXPORT_NAMES"}:
            _phase192_section_0.__dict__.pop(name, None)


__all__ = sorted(_PHASE192_EXPORT_NAMES)
if _phase192_module is not None:
    _phase192_module.__class__ = _Phase192PackageModule
