#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Shared Phase179 Zenoh topology selection and lifecycle helpers.

"""Keep Phase179 Zenoh router ownership explicit and transport-specific."""

from __future__ import annotations

import pathlib
import re
import os
import signal
import subprocess
import time
from dataclasses import dataclass
from typing import TextIO


ZENOH_RMW = "rmw_zenoh_cpp"
_TOPOLOGY_ID_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,95}$")
_SESSION_CONFIG_SUFFIXES = frozenset({".json", ".json5", ".yaml", ".yml"})


class ZenohTopologyError(RuntimeError):
    """A stable topology failure category without machine-specific details."""

    def __init__(self, category: str, message: str) -> None:
        """Initialize the stable failure category."""

        super().__init__(message)
        self.category = category


@dataclass(frozen=True)
class ZenohTopologyOptions:
    """Validated topology ownership selection before any process is launched."""

    mode: str
    topology_id: str | None
    router: pathlib.Path | None


@dataclass
class ZenohTopologyHandle:
    """One helper's topology state and, only when applicable, owned router process."""

    mode: str
    topology_id: str | None
    readiness: str
    process: subprocess.Popen[str] | None
    log_path: pathlib.Path | None
    _log_stream: TextIO | None


def parse_topology_id(value: str) -> str:
    """Accept only bounded opaque topology identities safe for summaries."""

    normalized = value.strip()
    if not _TOPOLOGY_ID_RE.fullmatch(normalized):
        raise ValueError("Zenoh topology id must be a 1-96 character safe token.")
    return normalized


def validate_topology_options(
    rmw: str,
    *,
    router: pathlib.Path | None,
    no_router: bool,
    topology_id: str | None,
) -> ZenohTopologyOptions:
    """Return explicit topology ownership, rejecting cross-transport ambiguity."""

    if rmw != ZENOH_RMW:
        if router is not None or no_router or topology_id is not None:
            raise ValueError("Zenoh topology arguments are valid only with rmw_zenoh_cpp.")
        return ZenohTopologyOptions("not-applicable", None, None)

    if topology_id is None:
        raise ZenohTopologyError("ENVIRONMENT", "Zenoh requires an explicit non-secret topology id.")
    normalized_id = parse_topology_id(topology_id)
    if router is not None and no_router:
        raise ValueError("--zenoh-router and --no-zenoh-router are mutually exclusive.")
    if router is None and not no_router:
        raise ZenohTopologyError("ENVIRONMENT", "Zenoh requires --zenoh-router or --no-zenoh-router.")
    if no_router:
        return ZenohTopologyOptions("external-certified-topology", normalized_id, None)

    normalized_router = pathlib.Path(router)
    if normalized_router.suffix.lower() in _SESSION_CONFIG_SUFFIXES:
        return ZenohTopologyOptions("external-session-config", normalized_id, normalized_router)
    return ZenohTopologyOptions("owned-router", normalized_id, normalized_router)


def wait_for_marker(log_path: pathlib.Path, marker: str, timeout_seconds: float) -> bool:
    """Wait for a bounded router-ready marker in a helper-owned log."""

    deadline = time.monotonic() + timeout_seconds
    last_position = 0
    tail = ""
    while True:
        if log_path.is_file():
            size = log_path.stat().st_size
            if size < last_position:
                last_position = 0
                tail = ""
            with log_path.open("r", encoding="utf-8", errors="replace") as stream:
                stream.seek(last_position)
                chunk = stream.read()
                last_position = stream.tell()
            if chunk:
                combined = tail + chunk
                if marker in combined:
                    return True
                tail = combined[-max(len(marker) - 1, 0) :]
        if time.monotonic() >= deadline:
            return False
        time.sleep(min(0.1, max(0.0, deadline - time.monotonic())))


def terminate_owned_process(process: subprocess.Popen[str]) -> None:
    """Stop only the router process tree created by this helper."""

    if process.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(
            ["taskkill", "/PID", str(process.pid), "/T", "/F"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        try:
            process.wait(timeout=10.0)
        except subprocess.TimeoutExpired:
            pass
        return

    try:
        os.killpg(process.pid, signal.SIGTERM)
    except (OSError, ProcessLookupError):
        try:
            process.terminate()
        except OSError:
            return
    try:
        process.wait(timeout=3.0)
    except subprocess.TimeoutExpired:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except (OSError, ProcessLookupError):
            try:
                process.kill()
            except OSError:
                return


def start_topology(
    options: ZenohTopologyOptions,
    *,
    env: dict[str, str],
    cwd: pathlib.Path,
    log_path: pathlib.Path,
    ready_timeout_seconds: float,
    ready_marker: str = "Started",
) -> ZenohTopologyHandle:
    """Start one explicitly owned router or configure an external topology selection."""

    if options.mode == "not-applicable":
        return ZenohTopologyHandle(options.mode, None, "not-applicable", None, None, None)
    if options.mode == "external-certified-topology":
        return ZenohTopologyHandle(options.mode, options.topology_id, options.mode, None, None, None)
    if options.router is None or not options.router.is_file():
        raise ZenohTopologyError("ENVIRONMENT", "The requested Zenoh router or session config does not exist.")
    if options.mode == "external-session-config":
        env["ZENOH_SESSION_CONFIG_URI"] = str(options.router.resolve())
        return ZenohTopologyHandle(options.mode, options.topology_id, options.mode, None, None, None)

    log_path.parent.mkdir(parents=True, exist_ok=True)
    log_stream = log_path.open("w", encoding="utf-8", errors="replace")
    process: subprocess.Popen[str] | None = None
    try:
        popen_kwargs: dict[str, object] = {
            "cwd": str(cwd),
            "env": dict(env),
            "text": True,
            "stdout": log_stream,
            "stderr": subprocess.STDOUT,
        }
        if os.name != "nt":
            popen_kwargs["start_new_session"] = True
        process = subprocess.Popen([str(options.router.resolve())], **popen_kwargs)
        if not wait_for_marker(log_path, ready_marker, ready_timeout_seconds):
            terminate_owned_process(process)
            raise ZenohTopologyError("ROUTER_READY_TIMEOUT", "The helper-owned Zenoh router did not become ready before timeout.")
        if process.poll() is not None:
            raise ZenohTopologyError("ROUTER_EXITED", "The helper-owned Zenoh router exited immediately after its ready marker.")
        return ZenohTopologyHandle(
            options.mode,
            options.topology_id,
            "owned-router-ready",
            process,
            log_path,
            log_stream,
        )
    except Exception:
        if process is not None:
            terminate_owned_process(process)
        log_stream.close()
        raise


def close_topology(handle: ZenohTopologyHandle) -> None:
    """Release only the process and log stream owned by this helper."""

    if handle.process is not None:
        terminate_owned_process(handle.process)
    if handle._log_stream is not None:
        handle._log_stream.close()
