#!/usr/bin/env python3
from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

from .foundation import *
from .logging import *
from .process import *
from .cli import *
from . import foundation as _phase192_foundation
from . import logging as _phase192_logging
from . import process as _phase192_process
from . import cli as _phase192_cli

_phase192_foundation.REPO_ROOT_PARENT_DEPTH = 2
_PHASE192_SECTION_MODULES = (
    _phase192_foundation,
    _phase192_logging,
    _phase192_process,
    _phase192_cli,
)
_PHASE192_EXPORT_NAMES = set()
for _phase192_section in _PHASE192_SECTION_MODULES:
    for _phase192_name in _phase192_section.__all__:
        globals()[_phase192_name] = getattr(_phase192_section, _phase192_name)
        _PHASE192_EXPORT_NAMES.add(_phase192_name)
for _phase192_section in _PHASE192_SECTION_MODULES:
    for _phase192_name in _PHASE192_EXPORT_NAMES:
        _phase192_section.__dict__[_phase192_name] = globals()[_phase192_name]


class _Phase192ImplementationModule(_phase192_types.ModuleType):
    """Forward implementation mutations to the split section modules."""

    def __setattr__(self, name, value):
        """Propagate exported state changes to every split section."""
        super().__setattr__(name, value)
        if not name.startswith("__") and not name.startswith("_phase192_") and name not in {"_PHASE192_SECTION_MODULES", "_PHASE192_EXPORT_NAMES"}:
            for _phase192_section in _PHASE192_SECTION_MODULES:
                _phase192_section.__dict__[name] = value

    def __delattr__(self, name):
        """Propagate exported state removals to every split section."""
        super().__delattr__(name)
        if not name.startswith("__") and not name.startswith("_phase192_") and name not in {"_PHASE192_SECTION_MODULES", "_PHASE192_EXPORT_NAMES"}:
            for _phase192_section in _PHASE192_SECTION_MODULES:
                _phase192_section.__dict__.pop(name, None)


__all__ = sorted(_PHASE192_EXPORT_NAMES)
_phase192_module = _phase192_sys.modules.get(__name__)
if _phase192_module is not None:
    _phase192_module.__class__ = _Phase192ImplementationModule
