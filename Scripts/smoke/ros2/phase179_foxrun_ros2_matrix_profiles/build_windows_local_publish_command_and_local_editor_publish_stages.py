from __future__ import annotations
from .validate_windows_subscription_endpoints_and_append_zenoh_topology_argv import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase179_foxrun_ros2_matrix_profiles.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def build_windows_local_publish_command(
    profile: MatrixProfile,
    spec: inbound.MessageSpec,
    args: argparse.Namespace,
) -> list[str]:
    """Build a bounded local publisher command without passing unavailable ROS CLI switches to Humble."""

    command = inbound.build_publish_command(
        pathlib.Path("ros2"),
        spec,
        args.token,
        profile.topic_prefix,
    )[1:]
    once_index = command.index("--once")
    wait_arguments = ["--wait-matching-subscriptions", "1"]
    if profile.distro in WINDOWS_LOCAL_PUBLISH_MAX_WAIT_DISTROS:
        wait_arguments.extend(["--max-wait-time-secs", str(args.ready_timeout_seconds)])
    command[once_index + 1 : once_index + 1] = wait_arguments
    return command
def local_editor_publish_stages(profile: MatrixProfile) -> tuple[tuple[str, ...], ...]:
    """Stage String before tokenless contracts so their Unity proof has this run's correlation token."""

    if LOCAL_EDITOR_CORRELATION_MESSAGE not in profile.message_set:
        raise MatrixFailure(
            "LOCAL_PROFILE",
            "Windows-local acceptance requires the String correlation contract in every fixed profile.",
        )
    first_stage = (LOCAL_EDITOR_CORRELATION_MESSAGE,)
    remaining_stage = tuple(name for name in profile.message_set if name != LOCAL_EDITOR_CORRELATION_MESSAGE)
    return (first_stage, remaining_stage) if remaining_stage else (first_stage,)
@contextlib.contextmanager
def managed_windows_local_publishers(
    python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    profile: MatrixProfile,
    args: argparse.Namespace,
    *,
    message_names: Sequence[str],
) -> object:
    """Keep one staged set of repo-local publishers alive until Unity registers their subscriptions."""

    processes: list[tuple[str, subprocess.Popen[str]]] = []
    try:
        if not message_names or any(name not in profile.message_set for name in message_names):
            raise MatrixFailure("LOCAL_PROFILE", "Windows-local publisher stage did not match the fixed profile contracts.")
        for name in message_names:
            spec = inbound.MESSAGE_SPECS[name]
            command = build_windows_local_publish_command(profile, spec, args)
            popen_kwargs: dict[str, object] = {
                "env": dict(env),
                "text": True,
                "stdout": subprocess.PIPE,
                "stderr": subprocess.STDOUT,
            }
            if os.name != "nt":
                popen_kwargs["start_new_session"] = True
            process = subprocess.Popen(
                [str(python), str(ros2_script), *command],
                **popen_kwargs,
            )
            processes.append((name, process))

        yield

        completion_deadline = time.monotonic() + args.apply_timeout_seconds
        for name, process in processes:
            remaining = completion_deadline - time.monotonic()
            if remaining <= 0.0:
                raise MatrixFailure("WINDOWS_PUBLISH", "Repo-local Windows ROS2 did not complete the fixed Phase179 publication.")
            try:
                process.communicate(timeout=remaining)
            except subprocess.TimeoutExpired as exc:
                raise MatrixFailure("WINDOWS_PUBLISH", "Repo-local Windows ROS2 did not complete the fixed Phase179 publication.") from exc
            if process.returncode != 0:
                raise MatrixFailure("WINDOWS_PUBLISH", "Repo-local Windows ROS2 could not publish the fixed Phase179 contract.")
    finally:
        for _, process in processes:
            inbound.terminate_owned_process(process)
def _wait_for_windows_local_apply_markers(
    profile: MatrixProfile,
    args: argparse.Namespace,
    message_names: Sequence[str],
) -> dict[str, inbound.UnityMarker]:
    """Require copied-value evidence for this run's generated token without assuming Editor.log appends at EOF."""

    markers: dict[str, inbound.UnityMarker] = {}
    for name in message_names:
        spec = inbound.MESSAGE_SPECS[name]
        markers[name] = inbound.wait_for_unity_marker(
            args.unity_log,
            inbound.topic_for_spec(profile.topic_prefix, spec),
            args.token,
            spec.expected_value(args.token),
            args.apply_timeout_seconds,
            start_offset=None,
        )
    return markers
def run_windows_local_editor(profile: MatrixProfile, args: argparse.Namespace) -> int:
    """Turn one Unity Play click into a complete local Windows ROS2-to-Unity acceptance result."""

    summary: dict[str, object] = {
        "phase": 179,
        "role": "windows-local-editor",
        "transportScope": "windows-local-loopback",
        "profileId": profile.profile_id,
        "surface": "editor",
        "distro": profile.distro,
        "rmwImplementation": profile.rmw,
        "domainId": args.domain_id,
        "discoveryRange": args.discovery_range,
        "token": args.token,
        "topicPrefix": profile.topic_prefix,
        "messageSet": list(profile.message_set),
        "ready": False,
        "allRequiredApplied": False,
        "messageResults": [],
    }
    if profile.rmw == zenoh_topology.ZENOH_RMW:
        summary["zenohTopologyId"] = args.zenoh_topology_id
    topology_handle: zenoh_topology.ZenohTopologyHandle | None = None
    failure_category: str | None = None
    try:
        ros2_root, python, ros2_script = resolve_windows_ros2_root(profile, args)
        env = ros2env.build_ros_env(
            ros2_root,
            profile.rmw,
            args.discovery_range,
            str(args.domain_id),
            profile.distro,
        )
        if profile.rmw == zenoh_topology.ZENOH_RMW:
            topology_handle = zenoh_topology.start_topology(
                _topology_options(profile, args),
                env=env,
                cwd=workspace_root(),
                log_path=args.summary_json.with_name(args.summary_json.stem + "-zenoh-router.log"),
                ready_timeout_seconds=args.ready_timeout_seconds,
                ready_marker=args.zenoh_router_ready_marker,
            )
            summary["zenohTopology"] = inbound.topology_summary(topology_handle)
        ready_tokens_before_launch = inbound.capture_unity_ready_marker_tokens(
            args.unity_log,
            profile.distro,
            profile.rmw,
        )
        publish_stages = local_editor_publish_stages(profile)
        first_stage = publish_stages[0]
        print(
            f"[phase179:{profile.profile_id}] Repo-local String publisher is waiting for its Unity subscription "
            f"for up to {args.ready_timeout_seconds:g} seconds; enter Play Mode now.",
            flush=True,
        )
        markers: dict[str, inbound.UnityMarker] = {}
        with managed_windows_local_publishers(
            python,
            ros2_script,
            env,
            profile,
            args,
            message_names=first_stage,
        ):
            inbound.wait_for_unity_ready_marker(
                args.unity_log,
                profile.distro,
                profile.rmw,
                None,
                args.ready_timeout_seconds,
                start_offset=None,
                excluded_tokens=ready_tokens_before_launch,
            )
            summary["ready"] = True
            markers.update(_wait_for_windows_local_apply_markers(profile, args, first_stage))
        for stage in publish_stages[1:]:
            print(
                f"[phase179:{profile.profile_id}] String correlation proof observed; starting delayed publishers: "
                + ", ".join(stage)
                + ".",
                flush=True,
            )
            with managed_windows_local_publishers(
                python,
                ros2_script,
                env,
                profile,
                args,
                message_names=stage,
            ):
                markers.update(_wait_for_windows_local_apply_markers(profile, args, stage))
        summary["messageResults"] = _editor_marker_evidence(profile, args.token, markers)
        summary["allRequiredApplied"] = True
        # The preceding context verifies every repo-local publisher exited successfully
        # after it waited for Unity and each unique copied-value marker was observed.
        # A new post-publication rclpy graph observer is deliberately not a local hard
        # gate: on Windows/FastDDS it can time out even after the actual DDS data path
        # has completed. The cross-host Editor role retains its separate graph proof.
        summary["windowsLocalDataPathEvidence"] = "publisher-complete-and-unity-applied"
        summary["verdict"] = _local_editor_pass_verdict(profile)
        exit_code = 0
    except MatrixFailure as exc:
        failure_category = exc.category
        exit_code = 1
    except inbound.AcceptanceFailure as exc:
        failure_category = exc.category
        exit_code = 1
    except zenoh_topology.ZenohTopologyError as exc:
        failure_category = exc.category
        exit_code = 1
    except KeyboardInterrupt:
        failure_category = "INTERRUPTED"
        exit_code = 1
    except (OSError, RuntimeError, subprocess.SubprocessError):
        failure_category = "ENVIRONMENT"
        exit_code = 1
    finally:
        if failure_category is not None:
            summary["failureCategory"] = failure_category
            summary["verdict"] = f"FAIL_{failure_category}"
        elif "verdict" not in summary:
            summary["verdict"] = "FAIL_UNKNOWN"
        try:
            _write_summary(args.summary_json, summary)
            print(f"Summary: {args.summary_json}")
            print(f"Verdict: {summary['verdict']}")
        finally:
            if topology_handle is not None:
                zenoh_topology.close_topology(topology_handle)
    return exit_code
def run_correlation(profile: MatrixProfile, args: argparse.Namespace) -> int:
    """Read two half-summaries and write the only final matrix PASS artifact."""

    label = profile.profile_id.upper().replace("-", "_")
    output: dict[str, object]
    exit_code: int
    try:
        output = correlate_summaries(
            profile,
            args.surface,
            _read_summary(args.linux_summary_json),
            _read_summary(args.windows_summary_json),
            consumed_receipts_path=args.summary_json.with_name("correlation-consumed.json"),
        )
        exit_code = 0
    except MatrixFailure as exc:
        output = {
            "phase": 179,
            "profileId": profile.profile_id,
            "surface": args.surface,
            "distro": profile.distro,
            "rmwImplementation": profile.rmw,
            "verdict": f"PHASE179_{label}_{args.surface.upper()}_FAIL_{exc.category}",
            "failureCategory": exc.category,
        }
        exit_code = 1
    _write_summary(args.summary_json, output)
    print(f"Summary: {args.summary_json}")
    print(f"Verdict: {output['verdict']}")
    return exit_code
def run_profile(profile_id: str, argv: Sequence[str] | None = None) -> int:
    """Run exactly one named profile role; the wrapper never permits a distro/RMW reinterpretation."""

    try:
        profile = PROFILES[profile_id]
    except KeyError:
        print(f"Unknown Phase179 profile: {profile_id}", file=sys.stderr)
        return 1
    args = parse_profile_args(profile, list(argv or ()))
    if args.role == "linux-peer":
        return run_linux_peer(profile, args)
    if args.role == "windows-local-editor":
        return run_windows_local_editor(profile, args)
    if args.role == "windows-player":
        return run_windows_player(profile, args)
    if args.role == "windows-editor":
        return run_windows_editor_host(profile, args)
    return run_correlation(profile, args)


__all__ = [name for name in globals() if not name.startswith("__")]
