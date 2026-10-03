#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the pure Phase184-G acceptance evidence protocol."""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
import copy
import importlib.util
import json
import pathlib
import sys
import tempfile
import typing
import unittest
from Scripts.phase192.source_layout import load_fresh_module as __load_fresh_module
ROOT = pathlib.Path(__file__).resolve().parents[4]
PROTOCOL_PATH = (
    ROOT
    / "Scripts"
    / "smoke"
    / "foxrun"
    / "phase184_profile_acceptance_protocol.py"
)
PHASE184_TEST_ROOT = ROOT / "build" / "Tests" / "Phase184"
def load_protocol_module():
    """Load the Phase184-G protocol module under test."""

    return __load_fresh_module(
        "phase184_profile_acceptance_protocol",
        PROTOCOL_PATH,
    )
def temporary_directory(prefix: str):
    """Return a Phase184-owned temporary directory context."""

    PHASE184_TEST_ROOT.mkdir(parents=True, exist_ok=True)
    return tempfile.TemporaryDirectory(prefix=prefix, dir=PHASE184_TEST_ROOT)
def run_config(
    protocol,
    *,
    case: str = "multi-target",
    profile: str = "jazzy-fastrtps",
) -> dict[str, object]:
    """Build one valid immutable run-config fixture."""

    run_id = "phase184g-20260726-a1b2c3d4"
    output = ROOT / "build" / "phase184" / "acceptance" / run_id
    contract = protocol.CASE_CONTRACTS[case]
    actors = sorted(contract.required_actors | contract.deliberately_absent_actors.keys())
    bridge_install = (
        ROOT
        / "build"
        / "phase184"
        / "bridge-cache"
        / profile
        / "bridge-overlay"
        / "install"
        if "bridge" in contract.required_actors
        else output / "bridge-overlay" / "install"
    )
    return {
        "schemaVersion": protocol.RUN_CONFIG_SCHEMA_VERSION,
        "executionMode": "batch",
        "runId": run_id,
        "token": "p184g_A1b2C3d4E5f6",
        "case": case,
        "profile": profile,
        "projectPath": str(ROOT / "Unity2Foxglove"),
        "outputRoot": str(output),
        "rosDistro": protocol.PROFILE_CONTRACTS[profile].runtime,
        "rmw": protocol.PROFILE_CONTRACTS[profile].rmw,
        "domainId": 48,
        "discoveryRange": (
            "SUBNET" if profile == "jazzy-fastrtps" else "LOCALHOST"
        ),
        "zenohTopologyId": "phase184-local" if profile == "lyrical-zenoh" else "",
        "phase181Workspace": str(ROOT / "build" / "phase181" / profile),
        "phase181Install": str(ROOT / "build" / "phase181" / profile / "install"),
        "bridgeOverlayInstall": str(bridge_install),
        "foxgloveHost": "127.0.0.1",
        "foxglovePort": 18765,
        "bridgeHost": "127.0.0.1",
        "bridgePort": 18766,
        "interfacePackage": "unity2foxglove_phase181_v1",
        "interfaceType": "unity2foxglove_phase181_v1/msg/Phase181State",
        "interfaceDigest": "a" * 64,
        "topics": list(contract.topics),
        "observationWindows": {
            "positiveSeconds": 3,
            "negativeSeconds": 3,
            "streamProductionSeconds": 2,
            "terminalSeconds": 30,
            "teardownSeconds": 30,
        },
        "readyFiles": {
            actor: str(output / "ready" / f"{actor}.json")
            for actor in actors
        },
        "resultFiles": {
            actor: str(output / "results" / f"{actor}.json")
            for actor in actors
        },
        "unityLog": str(output / "unity-editor.log"),
    }


__all__ = [name for name in globals() if not name.startswith("__")]
