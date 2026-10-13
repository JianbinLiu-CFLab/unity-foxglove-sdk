#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Shared Windows ROS2 helper utilities for smoke acceptance scripts.

"""Shared Windows ROS2 helper utilities for smoke acceptance scripts."""

from __future__ import annotations

from collections.abc import Iterator
import os
import pathlib
import re
import subprocess
import time
from contextlib import contextmanager
from datetime import datetime

_ROS2_OPT_BIN_PATHS_CACHE: dict[pathlib.Path, tuple[pathlib.Path, ...]] = {}
_QT_PLUGIN_PATH_CACHE: dict[pathlib.Path, pathlib.Path | None] = {}
_SENSITIVE_ENV_NAME_FRAGMENTS = ("TOKEN", "PASSWORD", "SECRET", "API_KEY", "ACCESS_KEY")


def find_workspace_root() -> pathlib.Path:
    """Find the repository root from either cwd or this module location."""

    starts = [pathlib.Path.cwd(), pathlib.Path(__file__).resolve().parent]
    for start in starts:
        for candidate in (start, *start.parents):
            if (candidate / "Packages").is_dir() and (candidate / "Scripts").is_dir():
                return candidate
    return pathlib.Path.cwd()


def default_ros2_root(distro: str = "jazzy", workspace_root: pathlib.Path | None = None) -> pathlib.Path:
    """Return the repo-local ROS2 Windows junction for a distro."""

    normalized = distro.lower().strip()
    root = workspace_root or find_workspace_root()
    return root / "ros2-windows" / f"ros2_{normalized}"


DEFAULT_JAZZY_ROS2_ROOT = default_ros2_root("jazzy")
DEFAULT_HUMBLE_ROS2_ROOT = default_ros2_root("humble")
DEFAULT_LYRICAL_ROS2_ROOT = default_ros2_root("lyrical")

# Backward-compatible default for older Jazzy smoke scripts. New distro-specific
# helpers should use default_ros2_root("<distro>") or the explicit constants above.
DEFAULT_ROS2_ROOT = DEFAULT_JAZZY_ROS2_ROOT

if os.name == "nt":
    import ctypes
    from ctypes import wintypes

    _USER32 = ctypes.windll.user32
    _ENUM_WINDOWS_PROC_TYPE = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
else:
    ctypes = None
    wintypes = None
    _USER32 = None
    _ENUM_WINDOWS_PROC_TYPE = None


def timestamp() -> str:
    """Return a local wall-clock timestamp for acceptance diagnostics."""

    return datetime.now().astimezone().isoformat(timespec="milliseconds")


def log_event(log_prefix: str, message: str) -> None:
    """Print a timestamped acceptance diagnostic line."""

    print(f"[{timestamp()}] [{log_prefix}] {message}", flush=True)


def terminate_owned_process(process: subprocess.Popen, timeout_seconds: float = 5.0) -> None:
    """Stop one explicitly owned helper process with a bounded fallback."""

    if process.poll() is not None:
        return
    if os.name == "nt":
        try:
            subprocess.run(
                ["taskkill", "/PID", str(process.pid), "/T", "/F"],
                check=False,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                timeout=timeout_seconds,
            )
        except (OSError, subprocess.TimeoutExpired):
            pass
        else:
            try:
                process.wait(timeout=timeout_seconds)
                return
            except subprocess.TimeoutExpired:
                pass
    if process.poll() is None:
        try:
            process.terminate()
        except OSError:
            return
    try:
        process.wait(timeout=timeout_seconds)
    except subprocess.TimeoutExpired:
        try:
            process.kill()
            process.wait(timeout=timeout_seconds)
        except (OSError, subprocess.TimeoutExpired):
            if process.poll() is None:
                raise RuntimeError(f"Owned process pid={process.pid} did not exit after bounded cleanup.")
    if process.poll() is None:
        raise RuntimeError(f"Owned process pid={process.pid} remains live after cleanup.")


@contextmanager
def cleanup_owned_processes_on_error(processes: list[subprocess.Popen]) -> Iterator[None]:
    """Terminate already-started helpers if a later launch step fails."""

    try:
        yield
    except BaseException:
        for process in reversed(processes):
            try:
                terminate_owned_process(process)
            except Exception as exc:
                log_event("ros2-process-cleanup", f"Failed to stop owned pid={process.pid}: {exc}")
        raise


def resolve_existing_path(path_text: str, description: str, workspace_root: pathlib.Path) -> pathlib.Path:
    """Resolve an absolute path or a path relative to the workspace root."""

    path = pathlib.Path(path_text)
    candidates = [path] if path.is_absolute() else [workspace_root / path, pathlib.Path.cwd() / path]
    for candidate in candidates:
        try:
            return candidate.resolve(strict=True)
        except FileNotFoundError:
            continue

    path = candidates[0]
    try:
        return path.resolve(strict=True)
    except FileNotFoundError as exc:
        raise FileNotFoundError(f"{description} does not exist: {path}") from exc


def infer_ros_distro(ros2_root: pathlib.Path) -> str:
    """Infer the Windows ROS2 distro and authenticate an optional workspace marker."""

    root_text = str(ros2_root).lower()
    path_distro = None
    if "humble" in root_text:
        path_distro = "humble"
    elif "lyrical" in root_text:
        path_distro = "lyrical"
    elif "jazzy" in root_text:
        path_distro = "jazzy"
    if path_distro is None:
        raise ValueError(f"Cannot infer ROS_DISTRO from ROS2 root path: {ros2_root}")
    marker_path = ros2_root / "pixi.toml"
    if marker_path.is_file():
        try:
            marker_text = marker_path.read_text(encoding="utf-8", errors="replace")[:8192]
        except OSError as exc:
            raise ValueError(f"Cannot read ROS2 workspace identity marker: {marker_path}") from exc
        marker_match = re.search(r'^\s*name\s*=\s*[\"\']pixi_ros2_(humble|jazzy|lyrical)[\"\']\s*$', marker_text, re.MULTILINE)
        if marker_match is None:
            raise ValueError(f"ROS2 workspace identity marker missing: {marker_path}")
        marker_distro = marker_match.group(1)
        if marker_distro != path_distro:
            raise ValueError(
                f"ROS2 workspace identity mismatch: path={path_distro} marker={marker_distro}"
            )
    return path_distro


def _workspace_distro_marker(ros2_root: pathlib.Path) -> str | None:
    """Read the bounded pixi workspace distro marker, if present."""
    marker_path = ros2_root / "pixi.toml"
    if not marker_path.is_file():
        return None
    text = marker_path.read_text(encoding="utf-8", errors="replace")[:8192]
    match = re.search(
        r'^\s*name\s*=\s*[\"\']pixi_ros2_(humble|jazzy|lyrical)[\"\']\s*$',
        text,
        re.MULTILINE,
    )
    if match is None:
        raise ValueError(f"ROS2 workspace identity marker missing: {marker_path}")
    return match.group(1)


def ros2_opt_bin_paths(ros2_root: pathlib.Path) -> list[pathlib.Path]:
    """Return existing ROS2 vendor DLL directories under opt."""

    key = ros2_root.resolve()
    cached = _ROS2_OPT_BIN_PATHS_CACHE.get(key)
    if cached is not None:
        return list(cached)

    opt_root = ros2_root / "opt"
    if not opt_root.is_dir():
        _ROS2_OPT_BIN_PATHS_CACHE[key] = ()
        return []
    priority_vendors = ("rviz_ogre_vendor", "gz_math_vendor")
    priority_paths = [opt_root / name / "bin" for name in priority_vendors]
    discovered = sorted(path for path in opt_root.glob("*/bin") if path.is_dir())
    result: list[pathlib.Path] = []
    for path in (*priority_paths, *discovered):
        if path.is_dir() and path not in result:
            result.append(path)
    _ROS2_OPT_BIN_PATHS_CACHE[key] = tuple(result)
    return result




__all__ = ['Iterator', 'os', 'pathlib', 're', 'subprocess', 'time', 'contextmanager', 'datetime', '_ROS2_OPT_BIN_PATHS_CACHE', '_QT_PLUGIN_PATH_CACHE', '_SENSITIVE_ENV_NAME_FRAGMENTS', 'find_workspace_root', 'default_ros2_root', 'DEFAULT_JAZZY_ROS2_ROOT', 'DEFAULT_HUMBLE_ROS2_ROOT', 'DEFAULT_LYRICAL_ROS2_ROOT', 'DEFAULT_ROS2_ROOT', 'timestamp', 'log_event', 'terminate_owned_process', 'cleanup_owned_processes_on_error', 'resolve_existing_path', 'infer_ros_distro', '_workspace_distro_marker', 'ros2_opt_bin_paths']
