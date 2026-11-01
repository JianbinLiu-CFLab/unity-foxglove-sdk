#!/usr/bin/env python3
from __future__ import annotations

import sys as _phase192_sys
from pathlib import Path as _Phase192Path

_phase192_repo_root = _Phase192Path(__file__).resolve().parents[4]
if str(_phase192_repo_root) not in _phase192_sys.path:
    _phase192_sys.path.insert(0, str(_phase192_repo_root))

_phase192_template_name = None
_phase192_template_call_is_safe = None
_phase192_template_validate_calls = None
_phase192_template_validate_imports = None
_phase192_template_validate_class = None
_phase192_template_validate_facade = None
_phase192_validate_template_registry_mutations = None

if False:
    from .template_registry import *

from Scripts.validation.source_decomposition.template_registry_compat import (
    _phase192_install_template_registry_facade,
)

_phase192_install_template_registry_facade(__name__)

__all__ = [
    "_phase192_template_name",
    "_phase192_template_call_is_safe",
    "_phase192_template_validate_calls",
    "_phase192_template_validate_imports",
    "_phase192_template_validate_class",
    "_phase192_template_validate_facade",
    "_phase192_validate_template_registry_mutations",
]
