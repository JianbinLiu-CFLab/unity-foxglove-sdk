#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Import-compatible package for the decomposed ROS 2 CDR generator."""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

from . import shared as _phase192_shared
from . import models as _phase192_models
from . import parsing as _phase192_parsing
from . import generation_helpers as _phase192_generation_helpers
from . import serialization as _phase192_serialization
from . import deserialization as _phase192_deserialization
from . import samples as _phase192_samples
from . import registry as _phase192_registry
from . import io as _phase192_io
from . import cli as _phase192_cli

_PHASE192_SECTION_MODULES = (
    _phase192_shared,
    _phase192_models,
    _phase192_parsing,
    _phase192_generation_helpers,
    _phase192_serialization,
    _phase192_deserialization,
    _phase192_samples,
    _phase192_registry,
    _phase192_io,
    _phase192_cli,
)

_PHASE192_REPO_ROOT = _phase192_shared.REPO_ROOT
_PHASE192_DEFAULT_INPUT = _phase192_shared.DEFAULT_INPUT
_PHASE192_PROTO_MESSAGES = _PHASE192_REPO_ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Runtime" / "Schemas" / "Proto" / "Generated" / "Messages"
_PHASE192_DEFAULT_OUTPUT_DIR = _PHASE192_REPO_ROOT / "Packages" / "dev.unity2foxglove.ros2bridge" / "Runtime" / "Schemas" / "Ros2Msg" / "Generated"
_phase192_shared.REPO_ROOT = _PHASE192_REPO_ROOT
_phase192_shared.DEFAULT_INPUT = _PHASE192_DEFAULT_INPUT
_phase192_shared.PROTO_MESSAGES = _PHASE192_PROTO_MESSAGES
_phase192_shared.DEFAULT_OUTPUT_DIR = _PHASE192_DEFAULT_OUTPUT_DIR
for _phase192_section in _PHASE192_SECTION_MODULES:
    _phase192_section.REPO_ROOT = _PHASE192_REPO_ROOT
    _phase192_section.DEFAULT_INPUT = _PHASE192_DEFAULT_INPUT
    _phase192_section.PROTO_MESSAGES = _PHASE192_PROTO_MESSAGES
    _phase192_section.DEFAULT_OUTPUT_DIR = _PHASE192_DEFAULT_OUTPUT_DIR

_PHASE192_EXPORT_NAMES = set()
for _phase192_section in _PHASE192_SECTION_MODULES:
    for _phase192_name in _phase192_section.__all__:
        _PHASE192_EXPORT_NAMES.add(_phase192_name)
for _phase192_name in _PHASE192_EXPORT_NAMES:
    for _phase192_section in _PHASE192_SECTION_MODULES:
        if hasattr(_phase192_section, _phase192_name):
            globals()[_phase192_name] = getattr(_phase192_section, _phase192_name)
            break
for _phase192_section in _PHASE192_SECTION_MODULES:
    for _phase192_name in _PHASE192_EXPORT_NAMES:
        if _phase192_name in globals():
            _phase192_section.__dict__[_phase192_name] = globals()[_phase192_name]


class _Phase192PackageModule(_phase192_types.ModuleType):
    """Mirror facade state into the decomposed generator modules."""

    def __setattr__(self, name, value):
        """Mirror assignments into the decomposed implementation."""
        super().__setattr__(name, value)
        if (
            not name.startswith("__")
            and not name.startswith("_phase192_")
            and name not in {"_PHASE192_SECTION_MODULES", "_PHASE192_EXPORT_NAMES"}
        ):
            for _phase192_section in _PHASE192_SECTION_MODULES:
                if hasattr(_phase192_section, name):
                    setattr(_phase192_section, name, value)

    def __delattr__(self, name):
        """Mirror deletions into the decomposed implementation."""
        super().__delattr__(name)
        if (
            not name.startswith("__")
            and not name.startswith("_phase192_")
            and name not in {"_PHASE192_SECTION_MODULES", "_PHASE192_EXPORT_NAMES"}
        ):
            for _phase192_section in _PHASE192_SECTION_MODULES:
                if hasattr(_phase192_section, name):
                    try:
                        delattr(_phase192_section, name)
                    except AttributeError:
                        pass


__all__ = sorted(_PHASE192_EXPORT_NAMES)
_phase192_module = _phase192_sys.modules.get(__name__)
if _phase192_module is not None:
    _phase192_module.__class__ = _Phase192PackageModule
del _phase192_sys, _phase192_types, _phase192_module
