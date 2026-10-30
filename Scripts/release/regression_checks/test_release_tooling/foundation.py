#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for release helper correctness and diagnostics.

from __future__ import annotations

import ast
import contextlib
import ctypes
import importlib.util
import inspect
import io
import json
import os
import re
import signal
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import types
import unittest
from pathlib import Path
from unittest import mock

from Scripts.phase192.source_layout import read_split_source


ROOT = Path(__file__).resolve().parents[4]
BUMP_VERSION_PATH = ROOT / "Scripts" / "release" / "bump_version.py"
RUN_CI_PATH = ROOT / "Scripts" / "release" / "run_ci.py"
MCAP_CONFORMANCE_PATH = ROOT / "Scripts" / "mcap" / "conformance" / "run_phase121_conformance.py"
UNITY_IL2CPP_PATH = ROOT / "Scripts" / "unity_build" / "unity_il2cpp.py"
UNITY_BATCH_COMPILE_PATH = ROOT / "Scripts" / "release" / "run_unity_batch_compile.py"
LOCAL_ENTRYPOINT_VALIDATOR_PATH = ROOT / "Scripts" / "package" / "validate_local_entrypoints.py"
PHASE186_WINDOWS_LIVE_WORKFLOW_PATH = (
    ROOT / ".github" / "workflows" / "phase186-bridge-windows-live.yml"
)
DOCS_WORKFLOW_PATH = ROOT / ".github" / "workflows" / "docs-check.yml"
DOTNET_WORKFLOW_PATH = ROOT / ".github" / "workflows" / "dotnet-tests.yml"
PACKAGE_WORKFLOW_PATH = ROOT / ".github" / "workflows" / "package-check.yml"
REPOSITORY_BOUNDARY_WORKFLOW_PATH = (
    ROOT / ".github" / "workflows" / "repository-boundary-check.yml"
)
UNITY_COMPILE_WORKFLOW_PATH = ROOT / ".github" / "workflows" / "unity-compile.yml"
PHASE16_VALIDATION_PATH = (
    ROOT
    / "Packages"
    / "dev.unity2foxglove.sdk"
    / "Tests"
    / "Runtime"
    / "Phase16Validation.cs"
)


def read_runtime_validation_source(path: Path) -> str:
    """Read a validation facade together with its decomposed companions."""
    sources = [path.read_text(encoding="utf-8")]
    companion_dir = path.parent / "Decomposed" / path.stem
    if companion_dir.is_dir():
        sources.extend(
            companion.read_text(encoding="utf-8")
            for companion in sorted(companion_dir.glob("*.cs"))
        )
    return "\n".join(sources)


def read_python_validation_source(path: Path) -> str:
    """Read a Python validation facade together with its exported package section."""
    if path.with_suffix("").is_dir():
        return read_split_source(path)
    return path.read_text(encoding="utf-8")
WORKFLOW_PATHS = (
    DOCS_WORKFLOW_PATH,
    DOTNET_WORKFLOW_PATH,
    PACKAGE_WORKFLOW_PATH,
    PHASE186_WINDOWS_LIVE_WORKFLOW_PATH,
    REPOSITORY_BOUNDARY_WORKFLOW_PATH,
    UNITY_COMPILE_WORKFLOW_PATH,
)


def load_module(name: str, path: Path):
    """Load a Python script as an isolated module."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    if path == RUN_CI_PATH:
        for module_name in tuple(sys.modules):
            if module_name == "run_ci" or module_name.startswith("run_ci."):
                sys.modules.pop(module_name, None)
    sys.modules.pop(spec.name, None)
    sys.modules[spec.name] = module
    try:
        spec.loader.exec_module(module)
    except Exception:
        sys.modules.pop(spec.name, None)
        raise
    return module


def active_workflow_line_index(
    workflow: str,
    exact_line: str,
    start: int = 0,
) -> int:
    """Return the line index of one active exact YAML line."""
    lines = workflow.splitlines()
    for index in range(start, len(lines)):
        line = lines[index]
        if line.lstrip().startswith("#"):
            continue
        if line.strip() == exact_line:
            return index
    raise ValueError(f"active workflow line is missing: {exact_line}")

__all__ = ['BUMP_VERSION_PATH', 'DOCS_WORKFLOW_PATH', 'DOTNET_WORKFLOW_PATH', 'LOCAL_ENTRYPOINT_VALIDATOR_PATH', 'MCAP_CONFORMANCE_PATH', 'PACKAGE_WORKFLOW_PATH', 'PHASE16_VALIDATION_PATH', 'PHASE186_WINDOWS_LIVE_WORKFLOW_PATH', 'Path', 'REPOSITORY_BOUNDARY_WORKFLOW_PATH', 'ROOT', 'RUN_CI_PATH', 'UNITY_BATCH_COMPILE_PATH', 'UNITY_COMPILE_WORKFLOW_PATH', 'UNITY_IL2CPP_PATH', 'WORKFLOW_PATHS', 'active_workflow_line_index', 'annotations', 'ast', 'contextlib', 'ctypes', 'importlib', 'inspect', 'io', 'json', 'load_module', 'mock', 'os', 're', 'read_python_validation_source', 'read_runtime_validation_source', 'read_split_source', 'shutil', 'signal', 'subprocess', 'sys', 'tempfile', 'threading', 'time', 'types', 'unittest']
