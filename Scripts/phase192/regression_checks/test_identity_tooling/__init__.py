#!/usr/bin/env python3
"""Import-compatible package for decomposed identity tooling regression tests."""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types
from pathlib import Path as _Phase192Path

_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "test_identity_tooling.py"
__file__ = str(_PHASE192_FACADE_FILE)
_PHASE192_PARENT = _PHASE192_FACADE_FILE.parent
if str(_PHASE192_PARENT) not in _phase192_sys.path:
    _phase192_sys.path.insert(0, str(_PHASE192_PARENT))

from . import foundation as _phase192_section_0
from . import surface_rows_tests as _phase192_section_1
from . import split_surface_tests as _phase192_section_2
from . import split_contract_tests as _phase192_section_3
from . import compatibility_surface_tests as _phase192_section_4
from . import compatibility_initializer_tests as _phase192_section_5
from . import loader_isolation_tests as _phase192_section_6
from . import loader_entrypoint_tests as _phase192_section_7
from . import loader_mutation_tests as _phase192_section_8
from . import namespace_risk_tests as _phase192_section_9
from . import namespace_execution_core_tests as _phase192_section_10
from . import namespace_execution_edges_tests as _phase192_section_11
from . import aggregate as _phase192_section_12

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
    _phase192_section_11,
    _phase192_section_12,
)
_PHASE192_EXPORT_NAMES = set()
for _phase192_section in _PHASE192_SECTION_MODULES:
    for _phase192_name in _phase192_section.__all__:
        globals()[_phase192_name] = getattr(_phase192_section, _phase192_name)
        _PHASE192_EXPORT_NAMES.add(_phase192_name)


for _phase192_section in _PHASE192_SECTION_MODULES:
    for _phase192_name in _PHASE192_EXPORT_NAMES:
        if _phase192_name in globals():
            _phase192_section.__dict__[_phase192_name] = globals()[_phase192_name]

SurfaceRowsTests = None
SplitSurfaceTests = None
SplitContractTests = None
CompatibilitySurfaceTests = None
CompatibilityInitializerTests = None
LoaderIsolationTests = None
LoaderEntrypointTests = None
LoaderMutationTests = None
NamespaceRiskTests = None
NamespaceExecutionCoreTests = None
NamespaceExecutionEdgesTests = None


class _Phase192PackageModule(_phase192_types.ModuleType):
    """Mirror package state into decomposed identity tooling test modules."""

    def __setattr__(self, name, value):
        """Mirror facade assignments into the decomposed test modules."""
        super().__setattr__(name, value)
        if not name.startswith("__") and not name.startswith("_phase192_"):
            for _phase192_section in _PHASE192_SECTION_MODULES:
                _phase192_section.__dict__[name] = value

    def __delattr__(self, name):
        """Mirror facade deletions into the decomposed test modules."""
        super().__delattr__(name)
        if not name.startswith("__") and not name.startswith("_phase192_"):
            for _phase192_section in _PHASE192_SECTION_MODULES:
                _phase192_section.__dict__.pop(name, None)


__all__ = sorted(set(_phase192_section_0.__all__) | {"IdentityToolingTests"})
_phase192_module = _phase192_sys.modules.get(__name__)
if _phase192_module is not None:
    _phase192_module.__class__ = _Phase192PackageModule


del _phase192_sys, _phase192_types, _Phase192Path, _PHASE192_FACADE_FILE, _PHASE192_PARENT
del _phase192_module, _phase192_section, _phase192_name
