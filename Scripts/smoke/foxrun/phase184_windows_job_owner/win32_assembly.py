#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Assemble the private Win32 API class from bounded sections."""

from __future__ import annotations

from .win32_bindings import _Win32ApiBindings
from .win32_create import _Win32ApiCreate
from .win32_process import _Win32ApiProcess
from .win32_wait import _Win32ApiWait

class _Win32Api(
    _Win32ApiBindings,
    _Win32ApiCreate,
    _Win32ApiProcess,
    _Win32ApiWait,
):
    """Lazy ctypes bindings for the bounded Win32 ownership operations."""

__all__ = ["_Win32Api"]
