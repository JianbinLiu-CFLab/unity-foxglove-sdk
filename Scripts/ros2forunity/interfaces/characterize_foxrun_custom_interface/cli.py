#!/usr/bin/env python3
"""Build and characterize the Phase181 static ROS2 interface out of tree.

This is deliberately a characterization gate, not package generation.  It
uses an explicitly selected ROS2 root, ros2cs overlay, and Visual Studio
installation to build the source-only package into ``build/phase181``.  The
only products are machine-local evidence and candidate files below that build
root; no Unity package is modified here.
"""

from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "characterize_foxrun_custom_interface.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import argparse
import sys
from pathlib import Path
from typing import Sequence

from .foundation import CharacterizationError, CharacterizationRequest
from .characterization_core import characterize

def _default_repo_root() -> Path:
    """Implement the internal default repo root step."""
    return Path(__file__).resolve().parents[3]


def parse_args(argv: Sequence[str]) -> tuple[CharacterizationRequest, bool]:
    """Parse command-line arguments."""
    root = _default_repo_root()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--distro", required=True, choices=("humble", "jazzy", "lyrical"))
    parser.add_argument("--static-package", type=Path, default=root / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces")
    parser.add_argument("--ros2-root", type=Path)
    parser.add_argument("--ros2cs-source", type=Path, required=True)
    parser.add_argument("--ros2cs-install", type=Path)
    parser.add_argument("--r2fu-source", type=Path, required=True)
    parser.add_argument("--build-root", type=Path, default=root / "build")
    parser.add_argument("--generator", default="Ninja")
    parser.add_argument("--dotnet", type=Path)
    parser.add_argument("--replace", action="store_true")
    args = parser.parse_args(argv)
    ros2_root = args.ros2_root or root / "ros2-windows" / ("ros2_" + args.distro)
    ros2cs_install = args.ros2cs_install or args.ros2cs_source / ("install-" + args.distro)
    return (
        CharacterizationRequest(
            distro=args.distro,
            static_package=args.static_package,
            ros2_root=ros2_root,
            ros2cs_source=args.ros2cs_source,
            ros2cs_install=ros2cs_install,
            r2fu_source=args.r2fu_source,
            build_root=args.build_root,
            generator=args.generator,
            dotnet=args.dotnet,
        ),
        args.replace,
    )


def main(argv: Sequence[str] | None = None) -> int:
    """Run the command-line entry point."""
    request, replace_existing = parse_args(argv or sys.argv[1:])
    try:
        result = characterize(request, replace_existing=replace_existing)
    except CharacterizationError as error:
        print(str(error), file=sys.stderr)
        return 1
    print("PASS:", result.distro, result.evidence_path)
    return 0


def cli_entry(argv: Sequence[str] | None = None) -> int:
    """Run the CLI while preserving the compatibility facade exit contract."""

    try:
        return main(argv)
    except KeyboardInterrupt:
        return 130
    except Exception as exc:
        print(f"[phase181-characterization] FAIL: {exc}", file=sys.stderr)
        return 1

__all__ = [name for name in globals() if not name.startswith("__")]
