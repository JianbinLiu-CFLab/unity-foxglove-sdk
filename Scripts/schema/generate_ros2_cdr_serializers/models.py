#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Decomposed ROS 2 CDR generator section."""

from __future__ import annotations

from .shared import *

@dataclass(frozen=True)
class Field:
    """Parsed ROS 2 field plus the generated protobuf property mapping."""

    ros_type: str
    name: str
    base_type: str
    array_kind: str
    fixed_length: int | None
    property_name: str
    property_type: str


@dataclass(frozen=True)
class Schema:
    """Parsed root Foxglove ROS 2 schema used for serializer generation."""

    name: str
    schema_name: str
    source_file: str
    fields: tuple[Field, ...]



__all__ = [name for name in globals() if not name.startswith("__")]
