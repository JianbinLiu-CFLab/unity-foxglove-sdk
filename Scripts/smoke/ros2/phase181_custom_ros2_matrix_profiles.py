#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Fixed Windows-local Editor rows for Phase181 custom ROS2 interfaces.

"""Expose one obvious no-argument command for every supported Phase181 row.

The profile module pins distribution, RMW, static source identity, readiness
window, and summary location.  It deliberately keeps all real peer behavior in
``phase181_custom_ros2_peer`` so Windows-local, Player, and Linux paths share
the same lock/digest/marker protocol rather than drifting into four copies.
"""

from __future__ import annotations

import pathlib
import sys
from dataclasses import dataclass
from typing import Sequence

import _ros2_windows_env as ros2env
import phase179_zenoh_topology as zenoh_topology
import phase181_custom_ros2_peer as peer
import phase181_custom_ros2_peer_protocol as protocol


DEFAULT_READY_TIMEOUT_SECONDS = 300
DEFAULT_APPLY_TIMEOUT_SECONDS = 120
DEFAULT_ZENOH_TOPOLOGY_ID = "phase181-lyrical-zenoh-local-router"
_FIXED_PROFILE_OPTIONS = ("--role", "--profile-id", "--distro", "--rmw", "--surface")
_REPOSITORY_LOCAL_OPTIONS = ("--ros2-root", "--workspace", "--static-interface-package")


@dataclass(frozen=True)
class MatrixProfile:
    """One immutable Phase181 Windows-local custom-interface row."""

    profile_id: str
    distro: str
    rmw: str


PROFILES = {
    "humble-fastrtps": MatrixProfile("humble-fastrtps", "humble", "rmw_fastrtps_cpp"),
    "jazzy-fastrtps": MatrixProfile("jazzy-fastrtps", "jazzy", "rmw_fastrtps_cpp"),
    "lyrical-fastrtps": MatrixProfile("lyrical-fastrtps", "lyrical", "rmw_fastrtps_cpp"),
    "lyrical-zenoh": MatrixProfile("lyrical-zenoh", "lyrical", "rmw_zenoh_cpp"),
}

_PROFILE_SUCCESS_VERDICTS = {
    "humble-fastrtps": "PHASE181_HUMBLE_FASTRTPS_WINDOWS_LOCAL_EDITOR_PASS",
    "jazzy-fastrtps": "PHASE181_JAZZY_FASTRTPS_WINDOWS_LOCAL_EDITOR_PASS",
    "lyrical-fastrtps": "PHASE181_LYRICAL_FASTRTPS_WINDOWS_LOCAL_EDITOR_PASS",
    "lyrical-zenoh": "PHASE181_LYRICAL_ZENOH_WINDOWS_LOCAL_EDITOR_PASS",
}


def profile_success_verdict(profile_id: str) -> str:
    """Return the stable positive verdict name for one visible matrix row."""

    try:
        return _PROFILE_SUCCESS_VERDICTS[profile_id]
    except KeyError as exc:
        raise ValueError("Unknown Phase181 profile: " + profile_id) from exc


def profile_owns_default_router(profile_id: str) -> bool:
    """Return whether this named row owns an explicit Zenoh router by default."""

    profile = PROFILES.get(profile_id)
    if profile is None:
        raise ValueError("Unknown Phase181 profile: " + profile_id)
    return profile.rmw == zenoh_topology.ZENOH_RMW


def _reject_profile_overrides(argv: Sequence[str]) -> None:
    """Reject arguments that could turn a named wrapper into another matrix row."""

    for argument in argv:
        for option in (*_FIXED_PROFILE_OPTIONS, *_REPOSITORY_LOCAL_OPTIONS):
            if argument == option or argument.startswith(option + "="):
                raise ValueError(option + " is fixed by the selected Phase181 matrix profile.")


def _has_option(argv: Sequence[str], option: str) -> bool:
    """Recognize both split and ``--option=value`` forms without parsing values."""

    return any(argument == option or argument.startswith(option + "=") for argument in argv)


def profile_wrapper_argv(profile_id: str, argv: Sequence[str]) -> list[str]:
    """Return fixed local-Editor arguments plus optional non-identity refinements."""

    profile = PROFILES.get(profile_id)
    if profile is None:
        raise ValueError("Unknown Phase181 profile: " + profile_id)
    supplied = list(argv)
    _reject_profile_overrides(supplied)
    fixed = [
        "--role",
        "windows-local-editor",
        "--profile-id",
        profile.profile_id,
        "--surface",
        "editor",
        "--distro",
        profile.distro,
        "--rmw",
        profile.rmw,
        "--ready-timeout-seconds",
        str(DEFAULT_READY_TIMEOUT_SECONDS),
        "--apply-timeout-seconds",
        str(DEFAULT_APPLY_TIMEOUT_SECONDS),
    ]
    if profile_owns_default_router(profile_id) and not _has_option(supplied, "--zenoh-topology-id"):
        fixed.extend(["--zenoh-topology-id", DEFAULT_ZENOH_TOPOLOGY_ID])
    return [*fixed, *supplied]


def _extract_zenoh_router_options(argv: Sequence[str]) -> tuple[list[str], pathlib.Path | None, bool]:
    """Consume the two wrapper-only router ownership controls without leaking them to the peer parser."""

    remaining: list[str] = []
    router: pathlib.Path | None = None
    no_router = False
    index = 0
    while index < len(argv):
        argument = argv[index]
        if argument == "--no-zenoh-router":
            no_router = True
            index += 1
            continue
        if argument == "--zenoh-router":
            if index + 1 >= len(argv):
                raise ValueError("--zenoh-router requires an explicit owned router executable path.")
            router = pathlib.Path(argv[index + 1])
            index += 2
            continue
        remaining.append(argument)
        index += 1
    if router is not None and no_router:
        raise ValueError("--zenoh-router and --no-zenoh-router are mutually exclusive.")
    return remaining, router, no_router


def _default_zenoh_router(ros2_root: pathlib.Path) -> pathlib.Path:
    """Resolve only the repository-local Lyrical Zenoh router executable."""

    return ros2_root / "Lib" / "rmw_zenoh_cpp" / "rmw_zenohd.exe"


def _default_zenoh_config_templates(ros2_root: pathlib.Path) -> tuple[pathlib.Path, pathlib.Path]:
    """Resolve the packaged Lyrical defaults that seed one owned local router configuration."""

    config_directory = ros2_root / "share" / "rmw_zenoh_cpp" / "config"
    return (
        config_directory / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
        config_directory / "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
    )


def profile_summary_path(profile: MatrixProfile) -> pathlib.Path:
    """Return the durable, profile-scoped Windows-local summary path."""

    return peer.workspace_root() / "build" / "phase181" / profile.profile_id / "windows-local-editor.json"


def write_profile_failure_summary(profile: MatrixProfile, failure_code: str) -> pathlib.Path:
    """Persist a bounded wrapper-only failure before the shared peer can run."""

    if not failure_code.startswith("FAIL_"):
        raise ValueError("Phase181 wrapper failures must use the stable FAIL_* grammar.")
    summary_path = profile_summary_path(profile)
    protocol.write_summary_atomic(
        summary_path,
        {
            "phase": 181,
            "role": "windows-local-editor",
            "surface": "editor",
            "transportScope": "windows-local-editor",
            "profileId": profile.profile_id,
            "distro": profile.distro,
            "rmwImplementation": profile.rmw,
            "commandLabels": {},
            "processOwnership": {},
            "failureCode": failure_code,
            "verdict": failure_code,
        },
    )
    return summary_path


def run_profile(profile_id: str, argv: Sequence[str] | None = None) -> int:
    """Run exactly one named local profile, owning only a default Zenoh router when applicable."""

    profile = PROFILES.get(profile_id)
    if profile is None:
        print("Unknown Phase181 profile: " + profile_id, file=sys.stderr)
        return 1
    try:
        supplied, requested_router, no_router = _extract_zenoh_router_options(list(argv or ()))
        if (requested_router is not None or no_router) and not profile_owns_default_router(profile_id):
            raise ValueError("Zenoh router options are valid only for the Lyrical/Zenoh Phase181 wrapper.")
        if no_router and not _has_option(supplied, "--zenoh-topology-id"):
            raise ValueError("--no-zenoh-router requires an explicit externally owned --zenoh-topology-id.")
        args = peer.parse_args(profile_wrapper_argv(profile_id, supplied))
        args.success_verdict = profile_success_verdict(profile_id)
    except (ValueError, SystemExit) as exc:
        if isinstance(exc, SystemExit):
            write_profile_failure_summary(profile, "FAIL_ARGUMENTS")
            return int(exc.code)
        write_profile_failure_summary(profile, "FAIL_ARGUMENTS")
        print("FAIL_ARGUMENTS", file=sys.stderr)
        return 1

    topology_handle: zenoh_topology.ZenohTopologyHandle | None = None
    try:
        if args.unity_batch:
            peer.prepare_unity_batch_profile_selection(args)
        if profile_owns_default_router(profile_id):
            ros2_root = ros2env.default_ros2_root(profile.distro, peer.workspace_root())
            router = requested_router if requested_router is not None else (None if no_router else _default_zenoh_router(ros2_root))
            environment = ros2env.build_ros_env(
                ros2_root,
                profile.rmw,
                None,
                str(args.domain_id),
                profile.distro,
            )
            options = zenoh_topology.validate_topology_options(
                profile.rmw,
                router=router,
                no_router=no_router,
                topology_id=args.zenoh_topology_id,
            )
            owned_config: zenoh_topology.OwnedZenohRouterConfig | None = None
            if options.mode == "owned-router":
                router_template, session_template = _default_zenoh_config_templates(ros2_root)
                owned_config = zenoh_topology.create_owned_local_router_config(
                    router_template=router_template,
                    session_template=session_template,
                    output_directory=peer.workspace_root() / "build" / "phase181" / profile.profile_id,
                )
            topology_handle = zenoh_topology.start_topology(
                options,
                env=environment,
                cwd=peer.workspace_root(),
                log_path=peer.workspace_root() / "build" / "phase181" / profile.profile_id / "owned-zenoh-router.log",
                ready_timeout_seconds=min(60.0, args.ready_timeout_seconds),
                owned_config=owned_config,
            )
        if topology_handle is None:
            return peer.run_windows_local_editor(args)
        return peer.run_windows_local_editor(args, zenoh_session_config=topology_handle.session_config)
    except peer.PeerFailure as exc:
        write_profile_failure_summary(profile, exc.code)
        print(exc.code, file=sys.stderr)
        return 1
    except zenoh_topology.ZenohTopologyError:
        write_profile_failure_summary(profile, "FAIL_ZENOH_TOPOLOGY")
        print("FAIL_ZENOH_TOPOLOGY", file=sys.stderr)
        return 1
    finally:
        if topology_handle is not None:
            zenoh_topology.close_topology(topology_handle)
