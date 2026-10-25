#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Public re-export surface for ROS 2 CDR generator helpers."""

from __future__ import annotations

from . import shared, models, parsing, generation_helpers, serialization, deserialization, samples, registry, io, cli

_sections = (shared, models, parsing, generation_helpers, serialization, deserialization, samples, registry, io, cli)

REPO_ROOT = shared.REPO_ROOT
DEFAULT_INPUT = shared.DEFAULT_INPUT
PROTO_MESSAGES = REPO_ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Runtime" / "Schemas" / "Proto" / "Generated" / "Messages"
DEFAULT_OUTPUT_DIR = REPO_ROOT / "Packages" / "dev.unity2foxglove.ros2bridge" / "Runtime" / "Schemas" / "Ros2Msg" / "Generated"
shared.REPO_ROOT = REPO_ROOT
shared.DEFAULT_INPUT = DEFAULT_INPUT
shared.PROTO_MESSAGES = PROTO_MESSAGES
shared.DEFAULT_OUTPUT_DIR = DEFAULT_OUTPUT_DIR
for _module in _sections:
    _module.REPO_ROOT = REPO_ROOT
    _module.DEFAULT_INPUT = DEFAULT_INPUT
    _module.PROTO_MESSAGES = PROTO_MESSAGES
    _module.DEFAULT_OUTPUT_DIR = DEFAULT_OUTPUT_DIR
for _module in _sections:
    for _name, _value in list(vars(_module).items()):
        if not _name.startswith("__"):
            globals().setdefault(_name, _value)
_public = [(name, value) for name, value in list(globals().items()) if not name.startswith("__")]
for _module in _sections:
    for _name, _value in _public:
        _module.__dict__.setdefault(_name, _value)

__all__ = [name for name in globals() if not name.startswith("__") and name not in {"_module", "_name", "_value", "_sections", "_public"}]
