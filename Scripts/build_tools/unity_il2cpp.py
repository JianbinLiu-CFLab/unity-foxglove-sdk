#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Cross-platform IL2CPP standalone Player build script.
# Usage: python Scripts/build_tools/unity_il2cpp.py --target win64
# Inputs: --target (win64|linux64|macos), --unity (path, optional)
# Outputs: Defaults to build/Unity/<target>-il2cpp-<timestamp>/; overridable via --build-dir and --output.

"""Build the Unity Foxglove demo project for IL2CPP standalone.

The script resolves project and output paths relative to its own location.
No hard-coded absolute paths - safe to use across clones.

Examples:
  python Scripts/build_tools/unity_il2cpp.py
  python Scripts/build_tools/unity_il2cpp.py --target linux64
  python Scripts/build_tools/unity_il2cpp.py --target macos --unity /path/to/Unity
"""

from __future__ import annotations

import argparse
import os
import platform
import re
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path
from typing import List, Optional, Tuple


# Build targets supported by the Unity-side FoxgloveBuild method.
TARGETS = ("win64", "linux64", "macos")

# Number of parent directories between this script and the repository root.
REPO_ROOT_PARENT_DEPTH = 2

# Process exit codes returned by this build CLI.
EXIT_SUCCESS = 0
EXIT_USAGE_ERROR = 2

# Time constants used for elapsed-time formatting and log polling.
SECONDS_PER_HOUR = 3_600
SECONDS_PER_MINUTE = 60
LOG_POLL_SLEEP_SECONDS = 1

# Keep progress heartbeats useful while avoiding console spam.
DEFAULT_PROGRESS_INTERVAL_SECONDS = 15
MIN_PROGRESS_INTERVAL_SECONDS = 1

# Split only the ProjectVersion key/value separator.
PROJECT_VERSION_SPLIT_MAX = 1
PROJECT_VERSION_VALUE_INDEX = 1

# Initial offsets and command indexes used for log tailing and diagnostics.
INITIAL_LOG_OFFSET = 0
UNITY_EXECUTABLE_COMMAND_INDEX = 0

# Log markers that indicate important Unity/Bee/IL2CPP build progress or failures.
IMPORTANT_LOG_MARKERS = (
    "[Foxrun",
    "[FoxgloveBuild]",
    "Build Finished",
    "Build succeeded",
    "Build failed",
    "Scripts have compiler errors",
    "Script Compilation",
    "Tundra build failed",
    "error CS",
    "Exception",
    "NullReference",
    "IL2CPP",
    "Csc ",
    "Bee",
)


def repo_root() -> Path:
    """Repository root resolved from the configured parent-depth constant."""
    return Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]


def default_target() -> str:
    """Detect the current host platform as the default build target."""
    system = platform.system().lower()
    if system == "windows":
        return "win64"
    if system == "darwin":
        return "macos"
    if system == "linux":
        return "linux64"
    return "win64"


def unity_version_key(path: Path) -> Tuple[int, ...]:
    """Extract a comparable Unity version tuple from a Hub editor path."""
    for part in reversed(path.parts):
        if re.match(r"^\d+\.\d+\.\d+", part):
            return tuple(int(number) for number in re.findall(r"\d+", part))
    return ()


def newest_existing(paths: List[Path]) -> Optional[Path]:
    """Return the newest Unity version among those that exist."""
    existing = [p for p in paths if p.exists()]
    if not existing:
        return None
    return max(existing, key=lambda p: (unity_version_key(p), p.stat().st_mtime))


def find_unity_explicit(path: Optional[str]) -> Optional[Path]:
    """Resolve the Unity executable from an explicit --unity argument."""
    if not path:
        return None
    unity = Path(path).expanduser()
    if unity.exists():
        return unity
    raise FileNotFoundError(f"--unity path does not exist: {unity}")


def find_unity_from_env() -> Optional[Path]:
    """Try UNITY_EXE or UNITY_PATH environment variables."""
    for name in ("UNITY_EXE", "UNITY_PATH"):
        value = os.environ.get(name)
        if value:
            unity = Path(value).expanduser()
            if unity.exists():
                return unity
            raise FileNotFoundError(f"{name} points to a missing file: {unity}")
    return None


def find_unity_from_project_version(project_path: Path) -> Optional[Path]:
    """Resolve Unity from ProjectSettings/ProjectVersion.txt when available."""
    version_file = project_path / "ProjectSettings" / "ProjectVersion.txt"
    if not version_file.exists():
        return None

    editor_version = None
    for line in version_file.read_text(encoding="utf-8", errors="replace").splitlines():
        if line.startswith("m_EditorVersion:"):
            editor_version = line.split(":", PROJECT_VERSION_SPLIT_MAX)[PROJECT_VERSION_VALUE_INDEX].strip()
            break
    if not editor_version:
        return None

    system = platform.system().lower()
    if system == "windows":
        roots = [
            Path(os.environ.get("PROGRAMFILES", r"C:\Program Files")),
            Path(os.environ.get("PROGRAMFILES(X86)", r"C:\Program Files (x86)")),
        ]
        for root in roots:
            unity = root / "Unity" / "Hub" / "Editor" / editor_version / "Editor" / "Unity.exe"
            if unity.exists():
                return unity
    elif system == "darwin":
        unity = Path("/Applications/Unity/Hub/Editor") / editor_version / "Unity.app" / "Contents" / "MacOS" / "Unity"
        if unity.exists():
            return unity
    elif system == "linux":
        for root in (Path.home() / "Unity" / "Hub" / "Editor", Path("/opt/Unity/Hub/Editor")):
            unity = root / editor_version / "Editor" / "Unity"
            if unity.exists():
                return unity

    raise FileNotFoundError(
        f"Project requires Unity {editor_version}, but that editor was not found. "
        "Pass --unity or set UNITY_EXE/UNITY_PATH."
    )


def find_unity_from_hub() -> Optional[Path]:
    """Search Unity Hub installations on the current platform."""
    system = platform.system().lower()
    candidates: List[Path] = []

    if system == "windows":
        program_files = [
            Path(os.environ.get("PROGRAMFILES", r"C:\Program Files")),
            Path(os.environ.get("PROGRAMFILES(X86)", r"C:\Program Files (x86)")),
        ]
        for root in program_files:
            candidates.extend(root.glob(r"Unity/Hub/Editor/*/Editor/Unity.exe"))

    elif system == "darwin":
        candidates.extend(Path("/Applications/Unity/Hub/Editor").glob("*/Unity.app/Contents/MacOS/Unity"))
        candidates.append(Path("/Applications/Unity/Unity.app/Contents/MacOS/Unity"))

    elif system == "linux":
        home = Path.home()
        candidates.extend((home / "Unity/Hub/Editor").glob("*/Editor/Unity"))
        candidates.extend(Path("/opt/Unity/Hub/Editor").glob("*/Editor/Unity"))
        candidates.append(Path("/opt/Unity/Editor/Unity"))

    return newest_existing(candidates)


def find_unity(path: Optional[str], project_path: Path) -> Path:
    """Resolve Unity executable, preferring explicit paths and the newest Hub install."""
    unity = (
        find_unity_explicit(path)
        or find_unity_from_env()
        or find_unity_from_hub()
        or find_unity_from_project_version(project_path)
    )
    if unity:
        return unity

    raise FileNotFoundError(
        "Unity executable was not found. Pass --unity or set UNITY_EXE/UNITY_PATH."
    )


def relative_to_root(path: Path, root: Path) -> str:
    """Format a path relative to repo root for readable log output."""
    try:
        return str(path.resolve().relative_to(root.resolve()))
    except ValueError:
        return str(path)


def resolve_unity_for_command(args: argparse.Namespace, project_path: Path) -> str:
    """Resolve Unity, allowing an explicit dry-run placeholder for CI path checks."""
    try:
        return str(find_unity(args.unity, project_path))
    except FileNotFoundError as exc:
        if args.dry_run and args.allow_missing_unity:
            return f"<Unity not found: {exc}>"
        raise


def build_command(args: argparse.Namespace) -> Tuple[List[str], Path, Path, Path]:
    """Build the full Unity batchmode command line from parsed arguments."""
    root = repo_root()
    project_path = (root / args.project).resolve()
    build_dir = (root / args.build_dir).resolve() if args.build_dir else default_build_dir(root, args.target)
    log_path = (root / args.log).resolve() if args.log else build_dir / "build.log"
    output_path = (root / args.output).resolve() if args.output else default_output_path(build_dir, args.target)
    unity = resolve_unity_for_command(args, project_path)

    if not project_path.exists():
        raise FileNotFoundError(f"Unity project was not found: {project_path}")

    cmd = [
        unity,
        "-batchmode",
        "-quit",
        "-projectPath",
        str(project_path),
        "-executeMethod",
        "FoxgloveBuild.BuildIl2CppFromCommandLine",
        "-foxgloveBuildTarget",
        args.target,
        "-foxgloveOutputPath",
        str(output_path),
        "-logFile",
        str(log_path),
    ]

    return cmd, project_path, log_path, output_path


def default_build_dir(root: Path, target: str) -> Path:
    """Default build output directory with platform and timestamp."""
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    return root / "build" / "Unity" / f"{target}-il2cpp-{stamp}"


def default_output_path(build_dir: Path, target: str) -> Path:
    """Default Player executable path for the given target."""
    if target == "win64":
        return build_dir / "WindowsIL2CPP" / "FoxgloveDemo.exe"
    if target == "linux64":
        return build_dir / "LinuxIL2CPP" / "FoxgloveDemo.x86_64"
    if target == "macos":
        return build_dir / "MacOSIL2CPP" / "FoxgloveDemo.app"
    raise ValueError(f"Unknown target: {target}")


def format_elapsed(seconds: float) -> str:
    """Format elapsed seconds as mm:ss or hh:mm:ss."""
    total = int(seconds)
    hours, remainder = divmod(total, SECONDS_PER_HOUR)
    minutes, seconds = divmod(remainder, SECONDS_PER_MINUTE)
    if hours:
        return f"{hours:02d}:{minutes:02d}:{seconds:02d}"
    return f"{minutes:02d}:{seconds:02d}"


def is_important_log_line(line: str) -> bool:
    """Check if a log line matches a known important marker."""
    stripped = line.strip()
    if not stripped:
        return False
    return any(marker in stripped for marker in IMPORTANT_LOG_MARKERS)


def read_new_important_lines(log_path: Path, offset: int) -> Tuple[int, List[str]]:
    """Read new important log lines since the given byte offset."""
    if not log_path.exists():
        return offset, []

    try:
        with log_path.open("r", encoding="utf-8", errors="replace") as handle:
            handle.seek(offset)
            lines = handle.readlines()
            new_offset = handle.tell()
    except OSError:
        return offset, []

    important = [line.strip() for line in lines if is_important_log_line(line)]
    return new_offset, important


def run_with_progress(cmd: List[str], root: Path, log_path: Path, interval: int) -> int:
    """Run the Unity process, tailing important log lines at the given interval."""
    started = time.monotonic()
    next_heartbeat = started + interval
    offset = INITIAL_LOG_OFFSET

    process = subprocess.Popen(cmd, cwd=root)
    while True:
        offset, lines = read_new_important_lines(log_path, offset)
        for line in lines:
            print(f"[unity-log] {line}", flush=True)

        returncode = process.poll()
        now = time.monotonic()
        if returncode is not None:
            break

        if now >= next_heartbeat:
            elapsed = format_elapsed(now - started)
            print(
                f"[build_unity_il2cpp] Elapsed {elapsed}; still building. "
                f"Log: {relative_to_root(log_path, root)}",
                flush=True,
            )
            next_heartbeat = now + interval

        time.sleep(LOG_POLL_SLEEP_SECONDS)

    offset, lines = read_new_important_lines(log_path, offset)
    for line in lines:
        print(f"[unity-log] {line}", flush=True)

    elapsed = format_elapsed(time.monotonic() - started)
    print(f"[build_unity_il2cpp] Unity exited after {elapsed}.", flush=True)
    return returncode


def parse_args() -> argparse.Namespace:
    """Parse CLI arguments for the build script."""
    parser = argparse.ArgumentParser(
        description="Run Unity batchmode IL2CPP build for the Foxglove demo project."
    )
    parser.add_argument(
        "--target",
        choices=TARGETS,
        default=default_target(),
        help="Build target. Defaults to the current host platform.",
    )
    parser.add_argument(
        "--unity",
        help="Path to the Unity executable. Defaults to UNITY_EXE/UNITY_PATH or Unity Hub discovery.",
    )
    parser.add_argument(
        "--project",
        default="Unity2Foxglove",
        help="Unity project path relative to the workspace root.",
    )
    parser.add_argument(
        "--log",
        help="Log path relative to the workspace root. Defaults to <build-dir>/build.log.",
    )
    parser.add_argument(
        "--build-dir",
        help="Build run directory relative to the workspace root. Defaults to build/Unity/<target>-il2cpp-<timestamp>/.",
    )
    parser.add_argument(
        "--output",
        help="Player output path relative to the workspace root. Defaults inside <build-dir>.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print the resolved project, target, and log path without starting Unity.",
    )
    parser.add_argument(
        "--allow-missing-unity",
        action="store_true",
        help="Allow dry-run path validation when Unity is not installed. Valid only with --dry-run.",
    )
    parser.add_argument(
        "--progress-interval",
        type=int,
        default=DEFAULT_PROGRESS_INTERVAL_SECONDS,
        help="Seconds between progress heartbeats while Unity is running.",
    )
    return parser.parse_args()


def main() -> int:
    """Main entry: parse args, build command, run Unity, report result."""
    args = parse_args()
    root = repo_root()

    if args.allow_missing_unity and not args.dry_run:
        print(
            "[build_unity_il2cpp] --allow-missing-unity is only valid with --dry-run.",
            file=sys.stderr,
        )
        return EXIT_USAGE_ERROR

    try:
        cmd, project_path, log_path, output_path = build_command(args)
    except Exception as exc:
        print(f"[build_unity_il2cpp] {exc}", file=sys.stderr)
        return EXIT_USAGE_ERROR

    print(f"[build_unity_il2cpp] Unity:    {cmd[UNITY_EXECUTABLE_COMMAND_INDEX]}")
    print(f"[build_unity_il2cpp] Project:   {relative_to_root(project_path, root)}")
    print(f"[build_unity_il2cpp] Target:    {args.target}")
    print(f"[build_unity_il2cpp] Log:       {relative_to_root(log_path, root)}")
    print(f"[build_unity_il2cpp] Output:    {relative_to_root(output_path, root)}")

    if args.dry_run:
        print("[build_unity_il2cpp] Dry run only; Unity was not started.")
        return EXIT_SUCCESS

    log_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    print("[build_unity_il2cpp] Starting Unity batchmode build...")

    returncode = run_with_progress(cmd, root, log_path, max(MIN_PROGRESS_INTERVAL_SECONDS, args.progress_interval))
    if returncode == EXIT_SUCCESS:
        print("[build_unity_il2cpp] Build command completed successfully.")
    else:
        print(
            f"[build_unity_il2cpp] Build failed with exit code {returncode}. "
            f"See log: {relative_to_root(log_path, root)}",
            file=sys.stderr,
        )

    return returncode


if __name__ == "__main__":
    raise SystemExit(main())
