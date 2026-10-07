#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Assemble the private Win32 API class from bounded sections."""

from __future__ import annotations

from . import win32_bindings as _win32_bindings
from . import win32_create as _win32_create
from . import win32_process as _win32_process
from . import win32_wait as _win32_wait

class _Win32Api:
    """Lazy ctypes bindings for the bounded Win32 ownership operations."""

for _phase192_part in (
    _win32_bindings._Win32ApiBindings,
    _win32_create._Win32ApiCreate,
    _win32_process._Win32ApiProcess,
    _win32_wait._Win32ApiWait,
):
    for _phase192_name, _phase192_value in vars(_phase192_part).items():
        if _phase192_name not in {"__dict__", "__doc__", "__module__", "__weakref__"}:
            setattr(_Win32Api, _phase192_name, _phase192_value)

__all__ = ["_Win32Api"]
