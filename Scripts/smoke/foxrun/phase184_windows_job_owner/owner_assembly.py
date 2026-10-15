#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Assemble the public WindowsJobOwner class from bounded sections."""

from __future__ import annotations

from .owner_cleanup import _WindowsJobOwnerCleanup
from .owner_launch import _WindowsJobOwnerLaunch
from .owner_observation import _WindowsJobOwnerObservation
from .owner_validation import _WindowsJobOwnerValidation

class WindowsJobOwner(
    _WindowsJobOwnerValidation,
    _WindowsJobOwnerLaunch,
    _WindowsJobOwnerObservation,
    _WindowsJobOwnerCleanup,
):
    """Own one unnamed kill-on-close Job and launch roots before first resume."""

__all__ = ["WindowsJobOwner"]
