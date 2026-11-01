#!/usr/bin/env python3
from __future__ import annotations

_phase192_template_name = None
_phase192_template_call_is_safe = None
_phase192_template_validate_calls = None
_phase192_template_validate_imports = None
_phase192_template_validate_class = None
_phase192_template_validate_facade = None
_phase192_validate_template_registry_mutations = None

if False:
    from .template_registry import *

from template_registry_compat import (
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

