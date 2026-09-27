"""Import-compatible package for the Phase192 decomposed source."""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types
from pathlib import Path as _Phase192Path

_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "test_phase184_foxglove_desktop_live_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
_PHASE192_PARENT = _PHASE192_FACADE_FILE.parent
if str(_PHASE192_PARENT) not in _phase192_sys.path:
    _phase192_sys.path.insert(0, str(_PHASE192_PARENT))

from . import foundation as _phase192_section_0
from . import class_phase184_foxglove_desktop_live_protocol_tests_support as _phase192_section_1
from . import class_phase184_foxglove_desktop_live_protocol_tests_fixtures as _phase192_section_2
from . import class_phase184_foxglove_desktop_live_protocol_tests_validation as _phase192_section_3
from . import class_phase184_foxglove_desktop_live_protocol_tests_runtime_and_phase184_foxglove_desktop_live_protocol_tests_public as _phase192_section_4

_PHASE192_SECTION_MODULES = (
    _phase192_section_0,
    _phase192_section_1,
    _phase192_section_2,
    _phase192_section_3,
    _phase192_section_4,
)
_PHASE192_EXPORT_NAMES = set()
for _phase192_section in _PHASE192_SECTION_MODULES:
    for _phase192_name in _phase192_section.__all__:
        globals()[_phase192_name] = getattr(_phase192_section, _phase192_name)
        _PHASE192_EXPORT_NAMES.add(_phase192_name)
for _phase192_section in _PHASE192_SECTION_MODULES:
    for _phase192_name in _PHASE192_EXPORT_NAMES:
        _phase192_section.__dict__[_phase192_name] = globals()[_phase192_name]

class _Phase192PackageModule(_phase192_types.ModuleType):
    def __setattr__(self, name, value):
        super().__setattr__(name, value)
        if not name.startswith("__") and not name.startswith("_phase192_") and name not in {"_PHASE192_SECTION_MODULES", "_PHASE192_EXPORT_NAMES"}:
            for _phase192_section in _PHASE192_SECTION_MODULES:
                _phase192_section.__dict__[name] = value
    def __delattr__(self, name):
        super().__delattr__(name)
        if not name.startswith("__") and not name.startswith("_phase192_") and name not in {"_PHASE192_SECTION_MODULES", "_PHASE192_EXPORT_NAMES"}:
            for _phase192_section in _PHASE192_SECTION_MODULES:
                _phase192_section.__dict__.pop(name, None)

__all__ = sorted(_PHASE192_EXPORT_NAMES)
_phase192_module = _phase192_sys.modules.get(__name__)
if _phase192_module is not None:
    _phase192_module.__class__ = _Phase192PackageModule
del _phase192_sys, _phase192_types, _Phase192Path, _PHASE192_FACADE_FILE, _PHASE192_PARENT
del _phase192_module, _phase192_section, _phase192_name
