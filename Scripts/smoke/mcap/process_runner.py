"""Bounded subprocess execution for smoke helpers."""

from __future__ import annotations

import os
import signal
import subprocess
import threading
from dataclasses import dataclass
from typing import Mapping, Sequence


@dataclass(frozen=True)
class BoundedResult:
    """Small CompletedProcess-compatible result with bounded diagnostics."""

    returncode: int
    stdout: str
    stderr: str
    timed_out: bool = False
    output_truncated: bool = False


def _terminate_tree(process: subprocess.Popen[str]) -> None:
    """Terminate the owned process group and then the direct child."""

    if os.name == "nt":
        try:
            subprocess.run(
                ["taskkill", "/PID", str(process.pid), "/T", "/F"],
                check=False,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                timeout=10,
            )
        except (OSError, subprocess.TimeoutExpired):
            pass
    else:
        try:
            os.killpg(process.pid, signal.SIGTERM)
        except (OSError, ProcessLookupError):
            pass
    try:
        process.kill()
    except (OSError, ProcessLookupError):
        pass


def run_bounded(
    args: Sequence[str],
    *,
    cwd: str | os.PathLike[str],
    env: Mapping[str, str],
    timeout: float,
    max_output_bytes: int = 1 << 20,
) -> BoundedResult:
    """Run a child with a deadline, owned process group, and capped output."""

    if timeout <= 0:
        raise ValueError("timeout must be positive")
    if max_output_bytes <= 0:
        raise ValueError("max_output_bytes must be positive")

    creationflags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0) if os.name == "nt" else 0
    process = subprocess.Popen(
        list(args),
        cwd=cwd,
        env=dict(env),
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        errors="replace",
        start_new_session=(os.name != "nt"),
        creationflags=creationflags,
    )
    buffers: dict[str, list[str]] = {"stdout": [], "stderr": []}
    sizes = {"stdout": 0, "stderr": 0}
    truncated = False

    def drain(name: str, stream) -> None:
        nonlocal truncated
        while True:
            chunk = stream.read(8192)
            if not chunk:
                return
            remaining = max_output_bytes - sizes[name]
            if remaining > 0:
                buffers[name].append(chunk[:remaining])
                sizes[name] += min(len(chunk), remaining)
            if len(chunk) > max(remaining, 0):
                truncated = True

    threads = [
        threading.Thread(target=drain, args=("stdout", process.stdout), daemon=True),
        threading.Thread(target=drain, args=("stderr", process.stderr), daemon=True),
    ]
    for thread in threads:
        thread.start()

    timed_out = False
    try:
        process.wait(timeout=timeout)
    except subprocess.TimeoutExpired:
        timed_out = True
        _terminate_tree(process)
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            pass
    for thread in threads:
        thread.join(timeout=2)
    for stream in (process.stdout, process.stderr):
        if stream is not None:
            stream.close()
    return BoundedResult(
        process.returncode if process.returncode is not None else 1,
        "".join(buffers["stdout"]),
        "".join(buffers["stderr"]),
        timed_out=timed_out,
        output_truncated=truncated,
    )
