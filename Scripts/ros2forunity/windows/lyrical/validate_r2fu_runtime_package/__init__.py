#!/usr/bin/env python3
from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types
from pathlib import Path as _Phase192Path

_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "validate_r2fu_runtime_package.py"
__file__ = str(_PHASE192_FACADE_FILE)
_PHASE192_PARENT = _PHASE192_FACADE_FILE.parent
if str(_PHASE192_PARENT) not in _phase192_sys.path:
    _phase192_sys.path.insert(0, str(_PHASE192_PARENT))

from . import implementation_lyrical as _phase192_section_0

_phase192_section_0.ROOT = _Phase192Path(_phase192_section_0.__file__).resolve().parents[5]
_phase192_section_0.PACKAGE = _phase192_section_0.ROOT / "Packages" / _phase192_section_0.PACKAGE_NAME
_phase192_section_0.ADAPTER_PACKAGE = _phase192_section_0.ROOT / "Packages" / "dev.unity2foxglove.ros2forunity"
_phase192_section_0.CORE_PACKAGE = _phase192_section_0.ROOT / "Packages" / "dev.unity2foxglove.sdk"
_phase192_section_0.RUNTIME_ROOT = _phase192_section_0.PACKAGE / "Runtime" / "Ros2ForUnity"
_phase192_section_0.PLUGIN_ROOT = _phase192_section_0.RUNTIME_ROOT / "Plugins" / "Windows" / "x86_64"
_phase192_section_0.MANIFEST = _phase192_section_0.PACKAGE / "RuntimeSupport" / "runtime-manifest.json"
_phase192_section_0.INVENTORY = _phase192_section_0.PACKAGE / "RuntimeSupport" / f"r2fu-{_phase192_section_0.PACKAGE_NAME.split('.')[4]}-win64-runtime-inventory.json"
_phase192_section_0.PUBLIC_DOCS = (
    _phase192_section_0.PACKAGE / "README.md",
    _phase192_section_0.PACKAGE / "THIRD_PARTY_NOTICES.md",
    _phase192_section_0.PACKAGE / "package.json",
    _phase192_section_0.MANIFEST,
)

_PHASE192_SECTION_MODULES = (_phase192_section_0,)
_PHASE192_EXPORT_NAMES = set(_phase192_section_0.__all__)
for _phase192_name in _PHASE192_EXPORT_NAMES:
    globals()[_phase192_name] = getattr(_phase192_section_0, _phase192_name)
for _phase192_name in _PHASE192_EXPORT_NAMES:
    _phase192_section_0.__dict__[_phase192_name] = globals()[_phase192_name]


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
_phase192_module = _phase192_sys.modules.get(__name__)
if _phase192_module is not None:
    _phase192_module.__class__ = _Phase192PackageModule

