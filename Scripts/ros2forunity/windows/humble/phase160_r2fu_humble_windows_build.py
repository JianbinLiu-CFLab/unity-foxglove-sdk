#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/ros2forunity/windows/humble
# Purpose: Phase160 R2FU Humble Windows build toolchain orchestrator.

"""Run a deterministic R2FU Humble Windows build attempt.

This is intentionally a Python orchestrator, not a project-owned PowerShell
build wrapper. It may call upstream R2FU PowerShell scripts because those are
the upstream build entry points being evaluated.
"""

from __future__ import annotations

import argparse
import datetime as _dt
import os
import pathlib
import queue
import re
import shutil
import stat
import subprocess
import sys
import threading
import time
from dataclasses import dataclass
from typing import Iterable


R2FU_REPO_URL = "https://github.com/RobotecAI/ros2-for-unity.git"
R2FU_BRANCH = "feature/humble-support"
ROS2CS_REPO_URL = "https://github.com/RobotecAI/ros2cs.git"
ROS2CS_BRANCH = "feature/humble-support"
DEFAULT_BUILD_ROOT = pathlib.Path(__file__).resolve().parents[4] / "build" / "r2fu-humble-win64"
DEFAULT_WORK_ROOT = DEFAULT_BUILD_ROOT / "work"
DEFAULT_TEMP_ROOT = DEFAULT_BUILD_ROOT / "tmp"
DEFAULT_EVIDENCE_PATH = pathlib.Path("build") / "phase160" / "r2fu_humble_windows_build_evidence.md"
DEFAULT_COMMAND_TIMEOUT_SECONDS = 2 * 60 * 60
CHECKOUT_DIR_NAME = "r2u"
HUMBLE_PIXI_RUNTIME_DLLS = ("yaml.dll", "spdlog.dll", "fmt.dll")

VERDICTS = (
    "BUILD_ORCHESTRATOR_GREEN",
    "BLOCKED_MSBUILD_QUERY",
    "BLOCKED_VSDEV_ENV",
    "BLOCKED_PYTHON_SELECTION",
    "BLOCKED_CL_TEMP_IL",
    "BLOCKED_CMAKE_GENERATOR",
    "BLOCKED_WINDOWS_PATH_LENGTH",
    "BLOCKED_R2FU_BUILD_SCRIPT",
    "BLOCKED_ROS2CS_BUILD",
    "BLOCKED_NATIVE_DEPENDENCY",
    "BLOCKED_UNKNOWN_TOOLCHAIN",
)


class Phase160Error(RuntimeError):
    """Build orchestration failure with a stable verdict label."""

    def __init__(self, verdict: str, message: str) -> None:
        """Create an error carrying the stable build verdict."""
        super().__init__(message)
        self.verdict = verdict


@dataclass
class CommandResult:
    """Captured subprocess result."""

    args: list[str]
    cwd: pathlib.Path
    exit_code: int
    output: str


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command-line arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", default=str(find_repo_root(pathlib.Path.cwd())))
    parser.add_argument("--work-root", default=str(DEFAULT_WORK_ROOT))
    parser.add_argument("--temp-root", default=str(DEFAULT_TEMP_ROOT))
    parser.add_argument("--ros2-root", default=str(default_ros2_root("humble")))
    parser.add_argument("--vs-dev-cmd", default="")
    parser.add_argument("--generator", choices=("auto", "visualstudio", "ninja"), default="auto")
    parser.add_argument("--clean", action="store_true")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--log-prefix", default="phase160")
    parser.add_argument(
        "--evidence-path",
        default="",
        help="Optional evidence markdown output path. Relative paths resolve from --repo-root.",
    )
    return parser.parse_args(argv)


def find_repo_root(start: pathlib.Path) -> pathlib.Path:
    """Find the repository root from a starting path."""

    current = start.resolve()
    for candidate in (current, *current.parents):
        if (candidate / ".git").exists():
            return candidate
    raise Phase160Error("BLOCKED_UNKNOWN_TOOLCHAIN", f"Could not locate repo root from {start}")


def default_ros2_root(distro: str) -> pathlib.Path:
    """Return the repo-local Windows ROS2 junction for a distro."""

    return find_repo_root(pathlib.Path.cwd()) / "ros2-windows" / f"ros2_{distro}"



def resolve_tool(tool: str, env: dict[str, str]) -> str:
    """Resolve a trusted system tool without accepting inherited PATH shims."""
    candidates = []
    if tool == "git":
        candidates.extend([
            pathlib.Path(os.environ.get("ProgramFiles", r"C:\\Program Files")) / "Git" / "cmd" / "git.exe",
            pathlib.Path(os.environ.get("ProgramFiles", r"C:\\Program Files")) / "Git" / "bin" / "git.exe",
        ])
    elif tool == "powershell":
        candidates.append(pathlib.Path(os.environ.get("SystemRoot", r"C:\\Windows")) / "System32" / "WindowsPowerShell" / "v1.0" / "powershell.exe")
    for candidate in candidates:
        if candidate.is_file():
            return str(candidate.resolve())
    resolved = shutil.which(tool, path=env.get("Path") or env.get("PATH"))
    if resolved and pathlib.Path(resolved).is_file():
        return str(pathlib.Path(resolved).resolve())
    raise Phase160Error("BLOCKED_UNKNOWN_TOOLCHAIN", f"Trusted {tool} executable was not found")


def record_checkout_identity(checkout: pathlib.Path, env: dict[str, str], log_file: pathlib.Path) -> None:
    """Attest the exact resolved source commit and tree in the build log."""
    for label, revision in (("R2FU_COMMIT", "HEAD"), ("R2FU_TREE", "HEAD^{tree}")):
        result = run_command(
            [resolve_tool("git", env), "rev-parse", revision],
            cwd=checkout, env=env, log_file=log_file, check=True)
        with log_file.open("a", encoding="utf-8") as log:
            log.write(f"{label}={result.output.strip()}\n")

def run_command(
    args: Iterable[str],
    *,
    cwd: pathlib.Path,
    env: dict[str, str],
    log_file: pathlib.Path,
    check: bool = False,
    timeout: float | None = DEFAULT_COMMAND_TIMEOUT_SECONDS,
) -> CommandResult:
    """Run a command and append captured output to the log file."""

    command = list(args)
    header = f"\n\n$ {' '.join(command)}\n# cwd={cwd}\n"
    with log_file.open("a", encoding="utf-8", errors="replace") as log:
        log.write(header)

    timed_out = False
    try:
        process = subprocess.Popen(
            command,
            cwd=str(cwd),
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            errors="replace",
        )
        output_parts: list[str] = []
        deadline = time.monotonic() + timeout if timeout is not None else None
        output_queue: queue.Queue[str] = queue.Queue()
        reader_done = threading.Event()

        def read_output() -> None:
            """Move child output off the blocking pipe without owning its deadline."""
            try:
                assert process.stdout is not None
                while True:
                    line = process.stdout.readline()
                    if not line:
                        break
                    output_queue.put(line)
            finally:
                reader_done.set()

        reader = threading.Thread(target=read_output, name="phase160-command-output", daemon=True)
        reader.start()
        with log_file.open("a", encoding="utf-8", errors="replace") as log:
            while True:
                remaining = None if deadline is None else deadline - time.monotonic()
                if remaining is not None and remaining <= 0 and process.poll() is None:
                    timed_out = True
                    kill_process_tree_windows(process.pid)
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        try:
                            process.wait(timeout=5)
                        except subprocess.TimeoutExpired:
                            pass
                wait_seconds = 0.1 if remaining is None else max(0.0, min(0.1, remaining))
                try:
                    line = output_queue.get(timeout=wait_seconds)
                    output_parts.append(line)
                    log.write(line)
                    log.flush()
                except queue.Empty:
                    pass
                if process.poll() is not None and reader_done.is_set() and output_queue.empty():
                    break
            if timed_out:
                output_parts.append("\nCOMMAND_TIMEOUT\n")
                log.write("\nCOMMAND_TIMEOUT\n")
                log.flush()
        reader.join(timeout=1)
        if process.stdout is not None:
            process.stdout.close()
        exit_code = 124 if timed_out else (process.returncode if process.returncode is not None else 124)
        output = "".join(output_parts)
    except FileNotFoundError as exc:
        exit_code = 127
        output = f"{exc}\n"
    with log_file.open("a", encoding="utf-8", errors="replace") as log:
        log.write(f"\n# exit={exit_code}\n")

    result = CommandResult(command, cwd, exit_code, output)
    if check and exit_code != 0:
        raise Phase160Error(classify_output(output), f"Command failed: {' '.join(command)}")
    return result


def reject_cmd_shell_unsafe_path(label: str, path: pathlib.Path) -> None:
    """Reject a path that cannot be safely embedded in a cmd.exe quoted string."""

    unsafe = next((character for character in '"%&|^<>\r\n' if character in str(path)), None)
    if unsafe is not None:
        raise Phase160Error(
            "BLOCKED_VSDEV_ENV",
            f"{label} contains unsupported cmd.exe metacharacter {unsafe!r}: {path}",
        )



def _load_source_sections() -> None:
    """Load the decomposed source sections into the facade namespace."""

    section_dir = pathlib.Path(__file__).with_name("Decomposed") / "phase160_r2fu_humble_windows_build"
    manifest_path = section_dir / "sections.manifest"
    section_names = tuple(
        line.strip()
        for line in manifest_path.read_text(encoding="utf-8").splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    )
    if len(section_names) != len(set(section_names)) or any(
        not section_name.endswith(".py") or "/" in section_name or "\\" in section_name
        for section_name in section_names
    ):
        raise RuntimeError(f"Invalid build section manifest: {manifest_path}")
    actual_names = tuple(
        sorted(
            path.name
            for path in section_dir.glob("*.py")
            if path.name != "__init__.py"
        )
    )
    if set(actual_names) != set(section_names):
        raise RuntimeError(
            "Build section manifest mismatch: "
            f"missing={sorted(set(section_names) - set(actual_names))}; "
            f"unexpected={sorted(set(actual_names) - set(section_names))}"
        )
    for section_name in section_names:
        section_path = section_dir / section_name
        source = section_path.read_text(encoding="utf-8")
        exec(compile(source, str(section_path), "exec"), globals(), globals())

_load_source_sections()
del _load_source_sections

def main(argv: list[str]) -> int:
    """Program entry point."""

    args = parse_args(argv)
    repo_root = pathlib.Path(args.repo_root).resolve()
    work_root = pathlib.Path(args.work_root).resolve()
    temp_root = pathlib.Path(args.temp_root).resolve()
    ros2_root = pathlib.Path(args.ros2_root).resolve()
    log_dir = repo_root / "build" / "tmp"
    ensure_dir(log_dir)
    log_file = log_dir / f"{args.log_prefix}_{timestamp()}.log"
    evidence_path = (
        (repo_root / args.evidence_path).resolve()
        if args.evidence_path and not pathlib.Path(args.evidence_path).is_absolute()
        else pathlib.Path(args.evidence_path).resolve()
        if args.evidence_path
        else (repo_root / DEFAULT_EVIDENCE_PATH).resolve()
    )
    vs_dev_cmd: pathlib.Path | None = None
    effective_generator = "unresolved"

    try:
        assert_safe_root("WorkRoot", work_root, repo_root)
        assert_safe_root("TempRoot", temp_root, repo_root)
        ensure_dir(temp_root)
        if args.clean:
            ensure_dir(work_root)
            ensure_dir(temp_root)
            remove_known_subdir(work_root / CHECKOUT_DIR_NAME, work_root)
            remove_known_subdir(work_root / "ros2-for-unity", work_root)

        env = dict(os.environ)
        vs_dev_cmd = resolve_vs_dev_cmd(args.vs_dev_cmd, env, log_file)
        env = capture_cmd_environment(vs_dev_cmd, env, log_file)
        env = scrub_environment(env, ros2_root, temp_root)
        env = capture_ros2_environment(ros2_root, env, log_file)
        record_where_diagnostics(env, repo_root, log_file)
        verify_python(ros2_root, env, log_file)
        verify_cl_compile(temp_root, env, log_file)
        effective_generator = resolve_cmake_generator(args.generator, env, log_file)

        verdict = "BLOCKED_UNKNOWN_TOOLCHAIN"
        if args.no_build:
            with log_file.open("a", encoding="utf-8", errors="replace") as log:
                log.write("\n# --no-build was set; build execution intentionally skipped.\n")
        else:
            checkout = ensure_r2fu_checkout(work_root, env, log_file, clean=False)
            patch_ros2cs_humble_windows_standalone(checkout, log_file)
            run_humble_dependency_import(checkout, env, log_file)
            verdict = run_upstream_build(checkout, env, log_file, ros2_root, effective_generator)

        write_evidence(
            evidence_path,
            verdict=verdict,
            log_file=log_file,
            repo_root=repo_root,
            work_root=work_root,
            temp_root=temp_root,
            ros2_root=ros2_root,
            vs_dev_cmd=vs_dev_cmd,
            effective_generator=effective_generator,
            no_build=args.no_build,
        )
        print(verdict)
        print(f"log={log_file}")
        print(f"evidence={evidence_path}")
        return 0 if verdict == "BUILD_ORCHESTRATOR_GREEN" or args.no_build else 2
    except Phase160Error as exc:
        verdict = exc.verdict if exc.verdict in VERDICTS else "BLOCKED_UNKNOWN_TOOLCHAIN"
        write_evidence(
            evidence_path,
            verdict=verdict,
            log_file=log_file,
            repo_root=repo_root,
            work_root=work_root,
            temp_root=temp_root,
            ros2_root=ros2_root,
            vs_dev_cmd=vs_dev_cmd,
            effective_generator=effective_generator,
            no_build=args.no_build,
        )
        print(verdict)
        print(str(exc), file=sys.stderr)
        print(f"log={log_file}")
        print(f"evidence={evidence_path}")
        return 2


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
