#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Assemble the public WindowsJobOwner class from bounded sections."""

from __future__ import annotations

from . import owner_cleanup as _owner_cleanup
from . import owner_launch as _owner_launch
from . import owner_observation as _owner_observation
from . import owner_validation as _owner_validation

class WindowsJobOwner:
    """Own one unnamed kill-on-close Job and launch roots before first resume."""

for _phase192_part in (
    _owner_validation._WindowsJobOwnerValidation,
    _owner_launch._WindowsJobOwnerLaunch,
    _owner_observation._WindowsJobOwnerObservation,
    _owner_cleanup._WindowsJobOwnerCleanup,
):
    for _phase192_name, _phase192_value in vars(_phase192_part).items():
        if _phase192_name not in {"__dict__", "__doc__", "__module__", "__weakref__"}:
            setattr(WindowsJobOwner, _phase192_name, _phase192_value)

__all__ = ["WindowsJobOwner"]
