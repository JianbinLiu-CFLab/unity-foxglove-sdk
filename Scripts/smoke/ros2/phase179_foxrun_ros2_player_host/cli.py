#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from .foundation import *
from .marker_protocol import *
from .runtime import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase179_foxrun_ros2_player_host.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def main(argv: Sequence[str] | None = None) -> int:
    """Launch the Player, wait for proof, then emit a correlated portable summary."""

    args = parse_args(argv)
    summary: dict[str, object] = {
        "phase": 179,
        "role": "windows-player-host",
        "distro": args.distro,
        "rmwImplementation": args.rmw,
        "domainId": args.domain_id,
        "discoveryRange": args.discovery_range,
        "token": args.token,
        "topicPrefix": args.topic_prefix,
        "messageSet": list(args.message_set),
        "ready": False,
        "allRequiredApplied": False,
    }
    if args.profile_id is not None:
        summary["profileId"] = args.profile_id
        summary["surface"] = args.surface
    if args.zenoh_topology_id is not None:
        summary["zenohTopologyId"] = args.zenoh_topology_id
    configured_topology: zenoh_topology.ZenohTopologyHandle | str | None = None
    owned_processes: list[subprocess.Popen[str]] = []
    player: subprocess.Popen[str] | None = None
    failure: PlayerHostFailure | None = None
    exit_code: int | None = None
    try:
        player_path = validate_player_path(args.player)
        args.player_log.parent.mkdir(parents=True, exist_ok=True)
        env = build_player_environment(args)
        configured_topology = configure_zenoh_topology(args, env)
        summary["zenohTopology"] = inbound.topology_summary(configured_topology)
        player = launch_owned_process(
            build_player_command(
                player_path,
                args.player_log,
                args.token,
                string_burst_final_sequence=args.string_burst_final_sequence,
            ),
            player_path.parent,
            env,
        )
        owned_processes.append(player)

        wait_for_ready_marker(args.player_log, args.distro, args.rmw, args.token, args.ready_timeout_seconds)
        summary["ready"] = True
        print(
            "[phase179-player-host] Player READY with requested runtime/RMW; waiting for Linux peer token " + args.token,
            flush=True,
        )
        player_result = wait_for_owned_process(player, args.exit_timeout_seconds)
        exit_code = player_result.return_code
        if player_result.timed_out:
            raise PlayerHostFailure("EXIT_TIMEOUT", "Player did not exit before the bounded deadline.")
        if exit_code != 0:
            raise PlayerHostFailure("EXIT_CODE", "Player exited with a non-zero operating-system exit code.")

        log_text = args.player_log.read_text(encoding="utf-8", errors="replace") if args.player_log.is_file() else ""
        applied_evidence = verify_required_applied_markers(
            log_text,
            args.token,
            string_burst_final_sequence=args.string_burst_final_sequence,
        )
        summary["allRequiredApplied"] = True
        if "stringBurst" in applied_evidence:
            summary["stringBurst"] = applied_evidence["stringBurst"]
        find_completion_marker(log_text, args.token)
    except PlayerHostFailure as exc:
        failure = exc
        summary["failureCategory"] = exc.category
    except KeyboardInterrupt:
        failure = PlayerHostFailure("INTERRUPTED", "Player host was interrupted by the operator.")
        summary["failureCategory"] = failure.category
    except (OSError, subprocess.SubprocessError):
        failure = PlayerHostFailure("ENVIRONMENT", "A helper-owned Player or router process could not be started or completed.")
        summary["failureCategory"] = failure.category
    finally:
        for process in reversed(owned_processes):
            terminate_owned_process(process)
        summary["playerExitCode"] = exit_code
        summary["verdict"] = classify_player_verdict(
            ready=bool(summary["ready"]),
            all_applied=bool(summary["allRequiredApplied"]),
            exit_code=exit_code,
            failure=failure,
        )
        try:
            write_summary(args.summary_json, summary)
            print(f"Summary: {args.summary_json}")
            print(f"Verdict: {summary['verdict']}")
        finally:
            inbound.close_configured_topology(configured_topology)
    return 2 if summary["verdict"] == "PLAYER_PROOF_COMPLETE_LINUX_PEER_CORRELATION_PENDING" else 1


__all__ = [name for name in globals() if not name.startswith("__")]
