#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from . import configuration as _configuration
from . import checkout_reporting as _checkout
from .configuration import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "run_phase121_conformance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def summarize_failure_details(text: str) -> str:
    """Bound failure output while retaining the final diagnostic lines."""

    details = "\n".join(line for line in text.strip().splitlines() if line.strip())
    if len(details) <= _configuration.FAILURE_DETAILS_MAX_CHARACTERS:
        return details

    marker = "\n... output truncated; tail follows ...\n"
    head_characters = _configuration.FAILURE_DETAILS_MAX_CHARACTERS - _configuration.FAILURE_DETAILS_TAIL_CHARACTERS - len(marker)
    return details[:head_characters] + marker + details[-_configuration.FAILURE_DETAILS_TAIL_CHARACTERS:]


def measure_runner_output(name: str, kind: str, result: _configuration.CommandResult) -> dict[str, object]:
    """Summarize official conformance runner console output."""

    text = result.stdout + "\n" + result.stderr
    clean_text = re.sub(r"\x1b\[[0-9;]*m", "", text)
    tested = len(re.findall(r"(?m)^\s*testing\s+", clean_text))
    explicit_passes = len(re.findall(r"(?m)^\s*pass\s+", clean_text, re.IGNORECASE))
    skipped = len(
        re.findall(r"(?m)^\s*(?:not supported|unsupported)\s+", clean_text, re.IGNORECASE)
    )
    explicit_failures = len(re.findall(r"(?m)^\s*fail\s+", clean_text, re.IGNORECASE))
    errors = len(re.findall(r"(?m)^\s*(?:Error:|\w+Error:)", clean_text)) + explicit_failures
    if result.exit_code != 0 and errors == 0:
        errors = 1
    passed = explicit_passes if explicit_passes > 0 else max(0, tested - errors)
    failures: list[dict[str, object]] = []
    if errors > 0:
        failures.append(
            {
                "exitCode": result.exit_code,
                "timedOut": result.timed_out,
                "details": summarize_failure_details(text),
            }
        )
    return _checkout.new_runner_report(name=name, kind=kind, passed=passed, failed=errors, skipped=skipped, failures=failures)


def package_manager_command() -> tuple[list[str], str] | None:
    """Return the package-manager command used for the official TypeScript harness."""

    if shutil.which("corepack"):
        return ["corepack", "yarn"], "corepack"
    if shutil.which("yarn"):
        return ["yarn"], "yarn"
    return None


def run_package_manager(base_command: list[str], args: Iterable[str], *, cwd: Path) -> _configuration.CommandResult:
    """Run the selected package manager with additional arguments."""

    return _configuration.invoke_command_capture(base_command + list(args), cwd=cwd)


def count_generated_variants() -> int:
    """Count generated MCAP variants in the official conformance data directory."""

    if not _configuration.DATA_DIR.exists():
        return 0
    return sum(1 for path in _configuration.DATA_DIR.rglob("*.mcap") if path.is_file())


def read_target_framework() -> str:
    """Read the target framework from the C# conformance project."""

    import xml.etree.ElementTree as ET
    tree = ET.parse(str(_configuration.PROJECT_PATH))
    root = tree.getroot()
    tf = root.find(".//TargetFramework")
    if tf is None:
        tf = root.find(".//{http://schemas.microsoft.com/developer/msbuild/2003}TargetFramework")
    if tf is not None and tf.text:
        return tf.text.strip()
    raise RuntimeError(f"TargetFramework was not found in {_configuration.PROJECT_PATH}")


def resolve_conformance_dll_path() -> Path:
    """Resolve the built C# conformance DLL from the project target framework."""

    target_framework = read_target_framework()
    expected = _configuration.REPO_ROOT / "build" / "McapConformance" / "Release" / target_framework / _configuration.CSHARP_CONFORMANCE_DLL_NAME
    if expected.exists():
        return expected

    candidates = sorted(
        (_configuration.REPO_ROOT / "build" / "McapConformance" / "Release").glob(f"*/{_configuration.CSHARP_CONFORMANCE_DLL_NAME}")
    )
    if len(candidates) == 1:
        return candidates[0]
    if candidates:
        raise RuntimeError("Multiple C# conformance DLL candidates found: " + ", ".join(str(path) for path in candidates))
    return expected

__all__ = [name for name in globals() if not name.startswith("__")]
