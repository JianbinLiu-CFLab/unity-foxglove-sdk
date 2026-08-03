#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Launch one focused Phase186-H Unity manual acceptance suite."""

from __future__ import annotations

import argparse
import pathlib
import sys
from collections.abc import Sequence


SCRIPT_DIRECTORY = pathlib.Path(__file__).resolve().parent
REPOSITORY_ROOT = SCRIPT_DIRECTORY.parents[2]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))
if str(SCRIPT_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIRECTORY))

try:
    from Scripts.smoke.foxrun import phase186_bridge_acceptance as acceptance
    from Scripts.smoke.foxrun import phase186_bridge_manual_status as status
except ImportError:  # Direct script execution from outside the repository root.
    import phase186_bridge_acceptance as acceptance
    import phase186_bridge_manual_status as status


EXIT_USAGE = 2
MANUAL_ALIASES = {
    "jazzy": (
        "manual-jazzy-fastrtps-duplex",
        pathlib.Path("build/phase186/manual/jazzy-fastrtps"),
    ),
    "zenoh": (
        "manual-lyrical-zenoh-duplex",
        pathlib.Path("build/phase186/manual/lyrical-zenoh"),
    ),
}


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Accept exactly one short immutable suite alias."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("alias", choices=tuple(MANUAL_ALIASES))
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    """Delegate a selected manual suite without inspecting Git ourselves."""

    args = parse_args(argv)
    case_id, output_root = MANUAL_ALIASES[args.alias]
    reporter = status.ManualStatusReporter()
    coordinator_args = [
        "--case",
        case_id,
        "--manual",
        "--manual-timeout-seconds",
        "1800",
        "--output-root",
        output_root.as_posix(),
    ]
    try:
        return acceptance.main(
            coordinator_args,
            status=reporter,
            resolve_current_head=True,
        )
    finally:
        reporter.close()


if __name__ == "__main__":
    raise SystemExit(main())
