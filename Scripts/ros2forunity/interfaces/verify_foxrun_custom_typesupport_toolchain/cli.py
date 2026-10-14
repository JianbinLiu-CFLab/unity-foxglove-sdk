#!/usr/bin/env python3
"""Non-mutating Phase181 Win64 custom-typesupport toolchain preflight.

The tool accepts explicit ROS2 and source roots, builds a minimal child
environment, and probes only the selected toolchain. It never downloads a
tool, sources a user shell profile, or writes below ``Packages/``. Successful
results are recorded as redacted provenance below ``build/phase181``.
"""

from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "verify_foxrun_custom_typesupport_toolchain.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import argparse
import subprocess
import sys
from pathlib import Path
from typing import Sequence

from .models import (
    ProcessResult,
    SUPPORTED_DISTROS,
    DEFAULT_GENERATOR,
    ToolchainPreflightError,
    ToolchainPreflightRequest,
)
from .probes import preflight_toolchain

def _default_runner(argv: Sequence[str], environment: Mapping[str, str]) -> ProcessResult:
    """Implement the internal default runner step."""
    completed = subprocess.run(
        tuple(str(value) for value in argv),
        capture_output=True,
        check=False,
        cwd=None,
        env=dict(environment),
        shell=False,
        text=True,
    )
    return ProcessResult(completed.returncode, completed.stdout or "", completed.stderr or "")


def _repository_root() -> Path:
    """Implement the internal repository root step."""
    return Path(__file__).resolve().parents[3]


def parse_args(argv: Sequence[str] | None = None) -> ToolchainPreflightRequest:
    """Parse command-line arguments."""
    root = _repository_root()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--distro", choices=SUPPORTED_DISTROS, required=True)
    parser.add_argument("--ros2-root", type=Path)
    parser.add_argument("--ros2cs-source", type=Path, required=True)
    parser.add_argument("--ros2cs-install", type=Path)
    parser.add_argument("--r2fu-source", type=Path, required=True)
    parser.add_argument("--build-root", type=Path, default=root / "build")
    parser.add_argument("--generator", default=DEFAULT_GENERATOR)
    parser.add_argument("--vswhere", type=Path)
    parser.add_argument("--dotnet", type=Path)
    args = parser.parse_args(argv)
    ros2_root = args.ros2_root or root / "ros2-windows" / ("ros2_" + args.distro)
    return ToolchainPreflightRequest(
        distro=args.distro,
        ros2_root=ros2_root,
        ros2cs_source=args.ros2cs_source,
        ros2cs_install=args.ros2cs_install,
        r2fu_source=args.r2fu_source,
        build_root=args.build_root,
        generator=args.generator,
        vswhere=args.vswhere,
        dotnet=args.dotnet,
    )


def main(argv: Sequence[str] | None = None) -> int:
    """Run the command-line entry point."""
    try:
        result = preflight_toolchain(parse_args(argv))
    except ToolchainPreflightError as error:
        print(error.code + ": " + error.remediation, file=sys.stderr)
        return 1
    print("PASS: " + result.distro + " / " + result.generator)
    return 0

__all__ = [name for name in globals() if not name.startswith("__")]
