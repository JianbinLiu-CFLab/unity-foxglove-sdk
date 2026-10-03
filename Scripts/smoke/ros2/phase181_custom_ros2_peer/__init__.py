"""Import-compatible package for the Phase192 decomposed source."""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types
from pathlib import Path as _Phase192Path

_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase181_custom_ros2_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
_PHASE192_PARENT = _PHASE192_FACADE_FILE.parent
if str(_PHASE192_PARENT) not in _phase192_sys.path:
    _phase192_sys.path.insert(0, str(_PHASE192_PARENT))

from . import foundation as _phase192_section_0
from . import require_matching_unity_readiness_and_worker_phase_deadline as _phase192_section_1
from . import apply_explicit_zenoh_session_config_and_require_valid_domain_id as _phase192_section_2
from . import classify_evidence_and_can_complete_live_evidence as _phase192_section_3
from . import evaluate_graph_evidence_and_summarize_graph_endpoints as _phase192_section_4
from . import run_typed_worker_and_default_unity_editor_log_path as _phase192_section_5
from . import release_subst_mapping_and_require_player_path as _phase192_section_6
from . import run_windows_surface as _phase192_section_7
from . import parse_args_and_main as _phase192_section_8

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
del _phase192_sys, _phase192_types, _Phase192Path, _PHASE192_FACADE_FILE, _PHASE192_PARENT
del _phase192_module, _phase192_section, _phase192_name
