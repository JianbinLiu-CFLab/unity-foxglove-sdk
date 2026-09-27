#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the Phase186 provenance and pre-move inventory gate."""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase186_provenance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
import importlib.util
import hashlib
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
ROOT = pathlib.Path(__file__).resolve().parents[4]
MODULE_PATH = ROOT / "Scripts/smoke/foxrun/phase186_provenance.py"
LEDGER_PATH = (
    ROOT
    / "Tools"
    / "ros2_bridge"
    / "unity2foxglove_ros2_bridge"
    / "PROVENANCE.json"
)
INVENTORY_PATH = (
    ROOT
    / "Packages"
    / "dev.unity2foxglove.sdk"
    / "Tests"
    / "Unit"
    / "Phase186"
    / "Fixtures"
    / "pre_move_sdk_ros_inventory.json"
)
REFERENCE_REMOTE = "https://github.com/Unity-Technologies/ROS-TCP-Connector.git"
REFERENCE_REVISION = "c27f00c6cf750d2d0564349b3039d19aa3925e7c"
REFERENCE_DATE = "2022-02-02T09:25:06-08:00"
REFERENCE_SUBJECT = "Release 0.7.0"
FIXTURE_PATH = (
    "Tools/ros2_bridge/unity2foxglove_ros2_bridge/"
    "test/fixtures/u2r2_protocol_vectors.json"
)
V1_TOP_LEVEL_KEYS = [
    "fixtureVersion",
    "protocol",
    "limits",
    "health",
    "preparePublisher",
    "publish",
    "negativeVectors",
]
def load_module():
    """Load the provenance gate from its repository path."""

    spec = importlib.util.spec_from_file_location("phase186_provenance", MODULE_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("Could not load the Phase186 provenance gate.")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module
def sha256_text(value: str) -> str:
    """Return the exact UTF-8 SHA-256 used by synthetic ledger records."""

    return hashlib.sha256(value.encode("utf-8")).hexdigest()
def canonical_json_sha256(value: object) -> str:
    """Return the canonical JSON digest used by the v1 authority record."""

    encoded = json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()
def reference_payload(
    *,
    revision: str = REFERENCE_REVISION,
    commit_date: str = REFERENCE_DATE,
    subject: str = REFERENCE_SUBJECT,
) -> dict[str, object]:
    """Create one complete synthetic copy of the locked upstream metadata."""

    return {
        "repository": REFERENCE_REMOTE,
        "origin": REFERENCE_REMOTE,
        "revision": revision,
        "commitDate": commit_date,
        "subject": subject,
        "license": "Apache-2.0",
        "inspectedFiles": [
            "com.unity.robotics.ros-tcp-connector/Runtime/TcpConnector/ROSConnection.cs",
            "com.unity.robotics.ros-tcp-connector/Runtime/TcpConnector/OutgoingMessageSender.cs",
            "com.unity.robotics.ros-tcp-connector/Runtime/TcpConnector/MessagePool.cs",
            "com.unity.robotics.ros-tcp-connector/Runtime/TcpConnector/TopicMessageSender.cs",
            "com.unity.robotics.ros-tcp-connector/Editor/MessageGeneration/MessageParser.cs",
        ],
        "ideasReviewed": [
            "one component owns a connection lifecycle",
            "outbound work crosses a bounded sender boundary",
            "payload ownership can be pooled explicitly",
            "topic routing has a stable per-topic sender identity",
            "code generation separates parsing from emitted artifacts",
        ],
        "materialCopied": False,
    }
def run_git(repository: pathlib.Path, *arguments: str) -> str:
    """Run one deterministic local Git command for a temporary fixture."""

    completed = subprocess.run(
        ["git", *arguments],
        cwd=repository,
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="strict",
    )
    return completed.stdout.strip()
def initialize_git_repository(repository: pathlib.Path) -> None:
    """Initialize one temporary repository without using global identity."""

    run_git(repository, "init", "--quiet")
    run_git(repository, "config", "user.name", "Phase186 Test")
    run_git(repository, "config", "user.email", "phase186@example.invalid")
def initialize_reference_checkout(reference: pathlib.Path) -> str:
    """Create a minimal official-origin reference checkout for gate tests."""

    reference.mkdir(parents=True)
    initialize_git_repository(reference)
    (reference / "LICENSE").write_text(
        "Apache License\nVersion 2.0, January 2004\n",
        encoding="utf-8",
    )
    inspected = reference_payload()["inspectedFiles"]
    for relative in inspected:
        target = reference / pathlib.PurePosixPath(str(relative))
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(
            "internal sealed class UpstreamReference {}\n",
            encoding="utf-8",
        )
    run_git(reference, "add", ".")
    run_git(reference, "commit", "--quiet", "-m", REFERENCE_SUBJECT)
    run_git(reference, "remote", "add", "origin", REFERENCE_REMOTE)
    return run_git(reference, "rev-parse", "HEAD")
def synthetic_ledger_path(repository: pathlib.Path) -> pathlib.Path:
    """Create the canonical release-ledger parent path in a synthetic repo."""

    ledger = (
        repository
        / "Tools"
        / "ros2_bridge"
        / "unity2foxglove_ros2_bridge"
        / "PROVENANCE.json"
    )
    ledger.parent.mkdir(parents=True, exist_ok=True)
    return ledger


__all__ = [name for name in globals() if not name.startswith("__")]
