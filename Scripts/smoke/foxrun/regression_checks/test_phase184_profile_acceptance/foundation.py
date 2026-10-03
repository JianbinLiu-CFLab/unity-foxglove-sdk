#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the owned Phase184-G acceptance orchestrator."""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
import copy
import importlib.util
import inspect
import json
import os
import pathlib
import re
import struct
import sys
import tempfile
import unittest
from unittest import mock
from Scripts.phase192.source_layout import load_fresh_module, read_split_source
ROOT = pathlib.Path(__file__).resolve().parents[4]
MODULE_PATH = ROOT / "Scripts" / "smoke" / "foxrun" / "phase184_profile_acceptance.py"
TEST_ROOT = ROOT / "build" / "phase184" / "test-orchestrator"
def read_orchestrator_source():
    """Read the facade and exported orchestrator package sections."""

    return read_split_source(MODULE_PATH)
def load_module():
    """Load the script as a fresh module without running its CLI."""

    return load_fresh_module(
        "phase184_profile_acceptance_under_test",
        MODULE_PATH,
    )
class FakeProcess:
    """Minimal helper-owned child used by cleanup and Job tests."""

    def __init__(self, pid: int = 184):
        """Initialize the fake process."""

        self.pid = pid
        self.returncode = None
        self.terminated = 0

    def poll(self):
        """Handle the poll step."""

        return self.returncode

    def send_signal(self, _signal):
        """Handle the send signal step."""

        self.terminated += 1
        self.returncode = 0

    def wait(self, timeout=None):
        """Wait for the configured owned process."""

        del timeout
        if self.returncode is None:
            self.returncode = 0
        return self.returncode

    def kill(self):
        """Handle the kill step."""

        self.terminated += 1
        self.returncode = -9
class FakeJobApi:
    """Records the exact hard-close ownership operations."""

    def __init__(self, *, assign_ok: bool = True):
        """Initialize the fake job API."""

        self.assign_ok = assign_ok
        self.created = 0
        self.configured = 0
        self.assigned: list[tuple[int, int]] = []
        self.closed: list[int] = []

    def create_kill_on_close_job(self):
        """Handle the create kill on close job step."""

        self.created += 1
        return 731

    def assign_pid(self, handle, pid):
        """Handle the assign PID step."""

        self.assigned.append((handle, pid))
        return self.assign_ok

    def close_handle(self, handle):
        """Handle the close handle step."""

        self.closed.append(handle)


__all__ = [name for name in globals() if not name.startswith("__")]
