#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Import-compatible package for the Phase192 decomposed source."""

from __future__ import annotations

import importlib as _phase192_importlib
import sys as _phase192_sys
import types as _phase192_types
from pathlib import Path as _Phase192Path

if __name__ == "phase184_windows_job_owner":
    _phase192_root = _Phase192Path(__file__).resolve().parents[3]
    if str(_phase192_root) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_root))
    _phase192_self = _phase192_importlib.import_module(
        "Scripts.smoke.foxrun.phase184_windows_job_owner")
    _phase192_sys.modules["phase184_windows_job_owner"] = _phase192_self
    __package__ = "Scripts.smoke.foxrun.phase184_windows_job_owner"

_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase184_windows_job_owner.py"
__file__ = str(_PHASE192_FACADE_FILE)

from . import foundation as _phase192_section_0
from . import owner_validation as _phase192_section_1
from . import owner_launch as _phase192_section_2
from . import owner_observation as _phase192_section_3
from . import owner_cleanup as _phase192_section_4
from . import win32_bindings as _phase192_section_5
from . import win32_create as _phase192_section_6
from . import win32_process as _phase192_section_7
from . import win32_wait as _phase192_section_8
from . import owner_assembly as _phase192_section_9
from . import win32_assembly as _phase192_section_10

_PHASE192_SECTION_MODULES = (
    _phase192_section_0,
    _phase192_section_1,
    _phase192_section_2,
    _phase192_section_3,
    _phase192_section_4,
    _phase192_section_5,
    _phase192_section_6,
    _phase192_section_7,
    _phase192_section_8,
    _phase192_section_9,
    _phase192_section_10,
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
    """Mirror facade state into the decomposed implementation modules."""
    def __setattr__(self, name, value):
        """Update this facade and its implementation modules."""
        super().__setattr__(name, value)
        if not name.startswith("__") and not name.startswith("_phase192_") and name not in {"_PHASE192_SECTION_MODULES", "_PHASE192_EXPORT_NAMES"}:
            for _phase192_section in _PHASE192_SECTION_MODULES:
                _phase192_section.__dict__[name] = value
    def __delattr__(self, name):
        """Remove an attribute from this facade and its implementation modules."""
        super().__delattr__(name)
        if not name.startswith("__") and not name.startswith("_phase192_") and name not in {"_PHASE192_SECTION_MODULES", "_PHASE192_EXPORT_NAMES"}:
            for _phase192_section in _PHASE192_SECTION_MODULES:
                _phase192_section.__dict__.pop(name, None)

__all__ = sorted(_PHASE192_EXPORT_NAMES)
_phase192_module = _phase192_sys.modules.get(__name__)
if _phase192_module is not None:
    _phase192_module.__class__ = _Phase192PackageModule
del _phase192_sys, _phase192_types, _Phase192Path, _PHASE192_FACADE_FILE
del _phase192_module, _phase192_section, _phase192_name
