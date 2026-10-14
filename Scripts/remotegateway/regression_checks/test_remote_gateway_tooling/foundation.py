"""Shared loaders and trust-anchor fixtures for remote-gateway tests."""

from __future__ import annotations

import datetime as dt
import hashlib
import importlib.util
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

ROOT = Path(__file__).resolve().parents[4]
BUILD_PATH = ROOT / "Scripts/remotegateway/build_foxglove_c_win64.py"
ACCEPTANCE_PATH = ROOT / "Scripts/remotegateway/run_cloud_acceptance.py"


def load_module(name: str, path: Path):
    """Load one repository script as an isolated module."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def verify_pinned_source_manifest() -> None:
    """Verify the committed trust anchor against the pinned source checkout."""
    build = load_module("remote_gateway_provenance_check", BUILD_PATH)
    manifest_path = ROOT / (
        "Packages/dev.unity2foxglove.remotegateway.win64/Runtime/"
        "Plugins/Windows/x86_64/foxglove-gateway-native-artifact.json"
    )
    payload = json.loads(manifest_path.read_text(encoding="utf-8"))
    actual_revision = build.foxglove_source_revision()
    actual_header = build.sha256_pinned_source(build.FOXGLOVE_HEADER).lower()
    actual_cargo_lock = build.sha256_pinned_source(build.FOXGLOVE_ROOT / "Cargo.lock").lower()
    if actual_revision != payload["sourceCommit"]:
        raise AssertionError(f"source commit mismatch: {actual_revision} != {payload['sourceCommit']}")
    if actual_header != payload["cHeaderSha256"].lower():
        raise AssertionError(f"C header hash mismatch: {actual_header} != {payload['cHeaderSha256']}")
    if actual_cargo_lock != payload["cargoLockSha256"].lower():
        raise AssertionError(f"Cargo.lock hash mismatch: {actual_cargo_lock} != {payload['cargoLockSha256']}")
    print("REMOTE_GATEWAY_PROVENANCE_OK")


__all__ = [
    "ACCEPTANCE_PATH",
    "BUILD_PATH",
    "Path",
    "ROOT",
    "SimpleNamespace",
    "dt",
    "hashlib",
    "importlib",
    "json",
    "load_module",
    "mock",
    "os",
    "sys",
    "tempfile",
    "unittest",
    "verify_pinned_source_manifest",
]
