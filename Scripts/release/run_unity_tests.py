#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Run Unity Test Runner in batch mode and emit a stable JSON verdict."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ElementTree

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(errors="replace")

REPO_ROOT = Path(__file__).resolve().parents[2]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))
DEFAULT_UNITY = Path(r"C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe")
REQUIRED_ENVIRONMENT = ("ALLUSERSPROFILE", "ProgramData", "APPDATA", "LOCALAPPDATA", "USERPROFILE")
DEFAULT_TIMEOUT_SECONDS = 60 * 60
VALID_PLATFORMS = ("EditMode", "PlayMode")


def parse_nunit_results(results_path: Path) -> tuple[str, list[str], dict[str, int]]:
    """Parse NUnit XML and return verdict, failed test names, and summary counts."""
    if not results_path.is_file():
        return "FAIL", [f"Unity did not create test results: {results_path}"], {}
    try:
        root = ElementTree.parse(results_path).getroot()
    except (ElementTree.ParseError, OSError) as exc:
        return "FAIL", [f"Unable to parse Unity test results: {exc}"], {}
    failed = []
    for testcase in root.iter("test-case"):
        result = testcase.attrib.get("result", "").lower()
        if result in {"failed", "error"}:
            failed.append(testcase.attrib.get("fullname") or testcase.attrib.get("name") or "<unnamed>")
    summary = root if root.tag == "test-run" else root.find("./test-suite")
    if summary is None:
        return "FAIL", ["Unity results did not contain a test summary"], {}
    try:
        counts = {
            key: int(summary.attrib.get(key, "0"))
            for key in ("total", "passed", "failed", "inconclusive", "skipped")
        }
    except (TypeError, ValueError) as exc:
        return "FAIL", [f"Unity result summary contains invalid counts: {exc}"], {}
    if any(count < 0 for count in counts.values()):
        return "FAIL", ["Unity result summary contains negative counts"], counts
    observed = sum(counts[key] for key in ("passed", "failed", "inconclusive", "skipped"))
    if observed != counts["total"]:
        return "FAIL", [
            "Unity result summary counts are inconsistent: "
            f"total={counts['total']} observed={observed}"
        ], counts
    no_passing_tests = counts["passed"] <= 0 or counts["total"] <= 0
    has_non_passing_tests = any(counts[key] > 0 for key in ("failed", "inconclusive", "skipped"))
    return (
        "FAIL" if failed or no_passing_tests or has_non_passing_tests else "PASS",
        failed,
        counts,
    )


def _active_unity_project(project_path: Path) -> bool:
    """Detect a live Unity process that already owns this project."""
    try:
        import psutil
    except ImportError as exc:
        raise RuntimeError("psutil is required to verify Unity project ownership") from exc
    needle = str(project_path.resolve()).lower().rstrip("\\/")
    for process in psutil.process_iter(("name", "cmdline")):
        try:
            name = (process.info.get("name") or "").lower()
            command_line = " ".join(process.info.get("cmdline") or []).lower()
        except (psutil.Error, OSError):
            continue
        if "unity" in name and needle in command_line:
            return True
    return False


def _write_result(result_path: Path, result: dict) -> None:
    """Write and print the JSON test verdict."""
    result_path.parent.mkdir(parents=True, exist_ok=True)
    result_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))


def _not_run(result_path: Path, reason: str) -> int:
    """Record a preflight block without launching Unity."""
    result = {
        "verdict": "NOT_RUN",
        "exit_code": 7,
        "diagnostics": [reason],
        "timed_out": False,
        "residual_pids": [],
    }
    _write_result(result_path, result)
    return 7


def run(
    unity: Path,
    project_path: Path,
    platform: str,
    results_path: Path,
    log_path: Path,
    timeout_seconds: int = DEFAULT_TIMEOUT_SECONDS,
    nographics: bool = False,
    test_category: str | None = None,
) -> int:
    """Run one owned Unity test process and classify NUnit results."""
    if platform not in VALID_PLATFORMS:
        raise ValueError(f"unsupported test platform: {platform}")
    unity = unity.expanduser().resolve()
    project_path = project_path.expanduser().resolve()
    results_path = results_path.expanduser().resolve()
    log_path = log_path.expanduser().resolve()
    result_path = results_path.with_suffix(".json")
    missing = [name for name in REQUIRED_ENVIRONMENT if not os.environ.get(name)]
    if missing:
        return _not_run(result_path, "missing environment " + ", ".join(missing))
    if not unity.is_file():
        return _not_run(result_path, f"Unity executable not found: {unity}")
    if not project_path.is_dir():
        return _not_run(result_path, f"project path not found: {project_path}")
    try:
        active_project = _active_unity_project(project_path)
    except RuntimeError as exc:
        return _not_run(result_path, str(exc))
    if active_project:
        return _not_run(result_path, f"Unity already owns project: {project_path}")

    results_path.parent.mkdir(parents=True, exist_ok=True)
    log_path.parent.mkdir(parents=True, exist_ok=True)
    results_path.write_text("", encoding="utf-8")
    log_path.write_text("", encoding="utf-8")
    command = [
        str(unity),
        "-batchmode",
        "-runTests",
        "-testPlatform",
        platform,
        "-testResults",
        str(results_path),
        "-projectPath",
        str(project_path),
        "-logFile",
        str(log_path),
    ]
    if nographics:
        command.insert(1, "-nographics")
    if test_category:
        command.extend(["-testCategory", test_category])
    from Scripts.unity_build.unity_il2cpp import await_tree_quiescence, start_owned_process

    try:
        tree = start_owned_process(command, REPO_ROOT)
    except OSError as exc:
        return _not_run(result_path, f"failed to launch Unity: {exc}")
    residual = []
    timed_out = False
    try:
        try:
            exit_code = tree.process.wait(timeout=max(1, timeout_seconds))
        except subprocess.TimeoutExpired:
            timed_out = True
            exit_code = 124
            residual = tree.terminate()
        else:
            residual = await_tree_quiescence(tree, 10.0)
            if residual:
                tree.terminate()
    finally:
        tree.close()

    verdict, failed, counts = parse_nunit_results(results_path)
    diagnostics = list(failed)
    if timed_out:
        verdict = "FAIL"
        diagnostics.insert(0, "Unity test run timed out")
    if exit_code != 0 and verdict == "PASS":
        verdict = "FAIL"
        diagnostics.insert(0, f"Unity exited with code {exit_code}")
    if residual:
        verdict = "FAIL"
        diagnostics.append("Unity process tree did not quiesce after exit")
    result = {
        "verdict": verdict,
        "exit_code": exit_code,
        "platform": platform,
        "results": str(results_path),
        "log": str(log_path),
        "summary": counts,
        "failed_tests": failed,
        "diagnostics": diagnostics,
        "timed_out": timed_out,
        "residual_pids": residual,
    }
    _write_result(result_path, result)
    return 0 if verdict == "PASS" else 1


def main(argv: list[str] | None = None) -> int:
    """Parse command-line arguments and run the requested Unity test platform."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--unity", type=Path, default=DEFAULT_UNITY)
    parser.add_argument("--project-path", type=Path, default=REPO_ROOT / "Unity2Foxglove")
    parser.add_argument("--test-platform", choices=VALID_PLATFORMS, required=True)
    parser.add_argument("--test-results", type=Path, required=True)
    parser.add_argument("--log-file", type=Path, required=True)
    parser.add_argument("--timeout-seconds", type=int, default=DEFAULT_TIMEOUT_SECONDS)
    parser.add_argument("--nographics", action="store_true")
    parser.add_argument("--test-category")
    args = parser.parse_args(argv)
    return run(
        args.unity,
        args.project_path,
        args.test_platform,
        args.test_results,
        args.log_file,
        args.timeout_seconds,
        args.nographics,
        args.test_category,
    )


if __name__ == "__main__":
    raise SystemExit(main())
