#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Refresh verified Phase181 custom ROS2 typesupport add-ons without operator-memory steps.

The command deliberately orchestrates the existing materialization boundaries:

* the candidate builder writes only below ``build/phase181/<distro>/candidate``;
* the sync command copies only a validated candidate inventory into ``Packages``;
* the final validator proves the tracked add-on matches its static interface and
  current base runtime.

Without ``--apply`` this is a read-only check.  With ``--apply`` it rebuilds
only add-ons whose static-interface or base-runtime manifest identity changed.
The caller must provide explicit external source and Unity paths; this script
never invents machine-specific toolchain locations.
"""

from __future__ import annotations

from . import foundation as _foundation
from . import refresh as _refresh
from .foundation import *
from .refresh import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "refresh_phase181_custom_typesupport_addons.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def parse_args(argv: Sequence[str] | None = None) -> AddonRefreshRequest:
    """Parse a safe read-only check or explicit real refresh request."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--distro", action="append", choices=SUPPORTED_DISTROS, help="Limit work to one distro; repeatable.")
    parser.add_argument("--apply", action="store_true", help="Build and sync only stale add-ons; the default is read-only.")
    parser.add_argument("--ros2cs-source", type=Path)
    parser.add_argument("--r2fu-source", type=Path)
    parser.add_argument("--unity", type=Path)
    parser.add_argument(
        "--ros2-root",
        action="append",
        default=[],
        metavar="DISTRO=PATH",
        help="Explicit ROS root for an isolated worktree; repeat once per selected distro.",
    )
    args = parser.parse_args(argv)
    ros2_roots: list[tuple[str, Path]] = []
    for assignment in args.ros2_root:
        distro, separator, path = assignment.partition("=")
        if not separator or distro not in SUPPORTED_DISTROS or not path:
            parser.error("--ros2-root must use DISTRO=PATH with a supported distro")
        ros2_roots.append((distro, Path(path)))
    return AddonRefreshRequest(
        root=repository_root(),
        distros=tuple(args.distro or SUPPORTED_DISTROS),
        apply=args.apply,
        ros2cs_source=args.ros2cs_source,
        r2fu_source=args.r2fu_source,
        unity=args.unity,
        ros2_roots=tuple(ros2_roots),
    )


def main(argv: Sequence[str] | None = None) -> int:
    """Run the command-line entry point."""

    request = parse_args(argv)
    try:
        states = _refresh.run_refresh(request)
    except AddonRefreshError as exc:
        print(str(exc), file=sys.stderr)
        return 1
    stale = [state.distro for state in states if not state.current]
    if stale and not request.apply:
        print("NEEDS_REBUILD: " + ", ".join(stale), file=sys.stderr)
        return 2
    print("PASS: Phase181 custom typesupport add-ons are current and validated.")
    return 0

__all__ = ['_foundation', '_refresh', '_Phase192Path', '_PHASE192_FACADE_FILE', 'parse_args', 'main']
