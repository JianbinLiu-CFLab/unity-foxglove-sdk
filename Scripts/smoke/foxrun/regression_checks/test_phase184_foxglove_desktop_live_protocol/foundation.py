#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Pure regressions for the Phase184-H Foxglove tooling protocol."""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_desktop_live_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
import copy
import dataclasses
import hashlib
import inspect
import json
import os
import pathlib
import tempfile
import unittest
from unittest import mock
from Scripts.smoke.foxrun import phase184_foxglove_desktop_live_protocol as protocol
ROOT = pathlib.Path(__file__).resolve().parents[4]
TEST_ROOT = ROOT / "build" / "Tests" / "Phase184HProtocol"
def valid_receipt() -> dict[str, object]:
    """Handle the valid receipt step."""

    return {
        "schemaVersion": 1,
        "releaseTag": "v1.2.3",
        "releaseVersion": "1.2.3",
        "architecture": "windows-amd64",
        "assetName": "foxglove-windows-amd64.exe",
        "assetUrl": (
            "https://github.com/foxglove/foxglove-cli/releases/download/"
            "v1.2.3/foxglove-windows-amd64.exe"
        ),
        "downloadSha256": "A" * 64,
        "downloadVersion": "v1.2.3",
        "installedPath": r"C:\Users\Tester\go\bin\foxglove.exe",
        "installedSha256": "A" * 64,
        "installedVersion": "1.2.3",
        "previousSha256": "B" * 64,
        "backupPath": r"C:\Users\Tester\go\bin\foxglove.dev-BBBBBBBB.exe",
        "installedUtc": "2026-07-27T12:34:56Z",
    }
def valid_barrier_config(
    output_root: pathlib.Path,
    *,
    run_id: str = "phase184g-20260727-desktop01",
    token: str = "p184g_A1b2C3d4E5f6",
    positive_seconds: int = 1,
) -> dict[str, object]:
    """Handle the valid barrier config step."""

    return {
        "runId": run_id,
        "token": token,
        "outputRoot": str(output_root),
        "observationWindows": {"positiveSeconds": positive_seconds},
    }
def valid_barrier_document(config: dict[str, object]) -> dict[str, object]:
    """Handle the valid barrier document step."""

    token = config["token"]
    assert isinstance(token, str)
    return {
        "schemaVersion": 1,
        "runId": config["runId"],
        "tokenDigest": hashlib.sha256(token.encode("utf-8")).hexdigest().upper(),
        "state": "desktop-client-proved",
        "acceptedClients": 1,
    }


__all__ = [name for name in globals() if not name.startswith("__")]
