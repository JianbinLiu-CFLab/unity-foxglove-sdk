#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from . import configuration as _configuration
from .configuration import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "run_phase121_conformance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def copy_directory_without_local_state(source: Path, destination: Path) -> None:
    """Copy a directory while excluding local checkout and package-manager state."""

    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True, exist_ok=True)

    for child in source.iterdir():
        if child.name in {".git", "node_modules"}:
            continue
        target = destination / child.name
        if child.is_dir():
            copy_directory_without_local_state(child, target)
        else:
            shutil.copy2(child, target)


def prepare_data_directory() -> None:
    """Create a fresh fixture root with official JSON inputs and regenerated MCAP bytes."""

    if _configuration.DATA_DIR.exists():
        shutil.rmtree(_configuration.DATA_DIR)
    _configuration.DATA_DIR.mkdir(parents=True, exist_ok=True)

    source_data = _configuration.OFFICIAL_CONFORMANCE / "data"
    for source in source_data.rglob("*.json"):
        target = _configuration.DATA_DIR / source.relative_to(source_data)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)


def add_csharp_runner_overlay() -> None:
    """Overlay C# conformance runners into the copied official MCAP harness."""

    runner_dest = _configuration.OVERLAY_ROOT / "tests/conformance/scripts/run-tests/runners"
    for filename in (
        "CsharpStreamedReaderTestRunner.ts",
        "CsharpIndexedReaderTestRunner.ts",
        "CsharpWriterTestRunner.ts",
    ):
        shutil.copy2(_configuration.RUNNER_SOURCE_ROOT / filename, runner_dest / filename)

    index_path = runner_dest / "index.ts"
    index = index_path.read_text(encoding="utf-8")
    if _configuration.RUNNER_ARRAY_DECLARATION not in index:
        raise RuntimeError(
            "Unable to inject C# conformance runners: upstream runner array declaration was not found."
        )
    imports = (
        'import CsharpIndexedReaderTestRunner from "./CsharpIndexedReaderTestRunner.ts";\n'
        'import CsharpStreamedReaderTestRunner from "./CsharpStreamedReaderTestRunner.ts";\n'
        'import CsharpWriterTestRunner from "./CsharpWriterTestRunner.ts";\n'
    )
    index = imports + "\n" + index
    index = index.replace(_configuration.RUNNER_ARRAY_DECLARATION, _configuration.CSHARP_RUNNER_INSERTION, 1)
    if "new CsharpIndexedReaderTestRunner()" not in index or "new CsharpWriterTestRunner()" not in index:
        raise RuntimeError("Unable to inject C# conformance runners: overlay output is missing C# runners.")
    index_path.write_text(index, encoding="utf-8")


__all__ = [name for name in globals() if not name.startswith("__")]
