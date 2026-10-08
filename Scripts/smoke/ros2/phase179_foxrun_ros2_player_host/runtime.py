#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from .foundation import *
from .marker_protocol import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase179_foxrun_ros2_player_host.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def write_summary(path: pathlib.Path, summary: Mapping[str, object]) -> None:
    """Write only sanitized public evidence outside Unity package source directories."""

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(sanitize_summary(summary), indent=2, sort_keys=True) + "\n", encoding="utf-8")


def configure_zenoh_topology(
    args: argparse.Namespace,
    env: dict[str, str],
) -> zenoh_topology.ZenohTopologyHandle:
    """Start or select one explicit Zenoh topology before launching the Player."""

    try:
        options = zenoh_topology.validate_topology_options(
            args.rmw,
            router=args.zenoh_router,
            no_router=args.no_zenoh_router,
            topology_id=args.zenoh_topology_id,
        )
        return zenoh_topology.start_topology(
            options,
            env=env,
            cwd=inbound.workspace_root(),
            log_path=args.summary_json.with_name(args.summary_json.stem + "-zenoh-router.log"),
            ready_timeout_seconds=args.ready_timeout_seconds,
            ready_marker=args.zenoh_router_ready_marker,
        )
    except zenoh_topology.ZenohTopologyError as exc:
        raise PlayerHostFailure(exc.category, "Zenoh topology setup did not complete.") from exc
    except ValueError as exc:
        raise PlayerHostFailure("ENVIRONMENT", "Zenoh topology arguments were invalid.") from exc


def validate_player_path(player: pathlib.Path) -> pathlib.Path:
    """Reject relative or missing Player inputs before spawning any process."""

    if not player.is_absolute():
        raise PlayerHostFailure("ENVIRONMENT", "--player must be an absolute WindowsStandalone64 executable path.")
    if not player.is_file():
        raise PlayerHostFailure("ENVIRONMENT", "--player does not exist or is not a file.")
    return player.resolve()

__all__ = [name for name in globals() if not name.startswith("__")]
