"""Compatibility package for the renamed source-decomposition validation tooling."""

from importlib import import_module as _import_module
import sys as _sys
from pathlib import Path as _Path

_REPOSITORY_ROOT = _Path(__file__).resolve().parents[2]
if str(_REPOSITORY_ROOT) not in _sys.path:
    _sys.path.insert(0, str(_REPOSITORY_ROOT))

_IMPLEMENTATION = _import_module("Scripts.validation.source_decomposition")
for _name in (
    "compare_identity_surfaces",
    "freeze_baseline",
    "identity_contracts",
    "identity_initializer_contracts",
    "identity_namespace_analysis",
    "identity_namespace_scanner",
    "identity_surface_conditions",
    "sensitivity_proof",
    "source_layout",
    "task0_entry",
):
    _sys.modules[f"{__name__}.{_name}"] = _import_module(
        f"Scripts.validation.source_decomposition.{_name}"
    )

__all__ = [name for name in dir(_IMPLEMENTATION) if not name.startswith("_")]
globals().update({name: getattr(_IMPLEMENTATION, name) for name in __all__})
