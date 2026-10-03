from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase179_foxrun_ros2_matrix_profiles.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def validate_windows_subscription_endpoints(
    python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    profile: MatrixProfile,
    *,
    timeout_seconds: float,
) -> list[dict[str, object]]:
    """Validate all fixed Unity subscription contracts using the selected repo-local Windows ROS Python."""

    _ = ros2_script
    deadline = time.monotonic() + timeout_seconds
    evidence: list[dict[str, object]] = []
    for name in profile.message_set:
        spec = inbound.MESSAGE_SPECS[name]
        topic = inbound.topic_for_spec(profile.topic_prefix, spec)
        remaining = deadline - time.monotonic()
        if remaining <= 0.0:
            raise MatrixFailure("WINDOWS_ENDPOINT", "Windows ROS2 endpoint evidence timed out.")
        try:
            probe = subprocess.run(
                [
                    str(python),
                    str(WINDOWS_RCLPY_ENDPOINT_PROBE),
                    "--topic",
                    topic,
                    "--timeout-seconds",
                    str(remaining),
                ],
                env=dict(env),
                text=True,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                check=False,
                timeout=remaining + 1.0,
            )
        except (OSError, subprocess.TimeoutExpired) as exc:
            raise MatrixFailure("WINDOWS_ENDPOINT", "Windows ROS2 endpoint evidence command did not complete.") from exc
        if probe.returncode != 0:
            raise MatrixFailure("WINDOWS_ENDPOINT", "Windows ROS2 endpoint evidence probe did not complete.")
        try:
            snapshot = json.loads(probe.stdout)
        except json.JSONDecodeError as exc:
            raise MatrixFailure("WINDOWS_ENDPOINT", "Windows ROS2 endpoint evidence probe returned invalid output.") from exc
        if not isinstance(snapshot, Mapping) or snapshot.get("topic") != topic:
            raise MatrixFailure("WINDOWS_ENDPOINT", "Windows ROS2 endpoint evidence did not identify the required topic.")
        subscription_count = snapshot.get("subscriptionCount")
        endpoints = snapshot.get("endpoints")
        if not isinstance(subscription_count, int) or subscription_count < 1 or not isinstance(endpoints, list):
            raise MatrixFailure("WINDOWS_ENDPOINT", "Windows ROS2 endpoint evidence did not observe a Unity subscription.")
        matching_endpoint = next(
            (
                endpoint
                for endpoint in endpoints
                if isinstance(endpoint, Mapping)
                and endpoint.get("messageType") == spec.message_type
                and endpoint.get("qosReliability") == spec.qos_reliability
                and endpoint.get("qosHistory") == spec.qos_history
                and endpoint.get("qosDepth") == spec.qos_depth
                and endpoint.get("qosDurability") == spec.qos_durability
            ),
            None,
        )
        if matching_endpoint is None:
            raise MatrixFailure("WINDOWS_ENDPOINT", "Windows ROS2 endpoint QoS evidence did not match the fixed contract.")
        evidence.append(
            {
                "name": name,
                "topic": topic,
                "messageType": spec.message_type,
                "subscriptionCount": subscription_count,
                "qosReliability": spec.qos_reliability,
                "qosHistory": spec.qos_history,
                "qosDepth": spec.qos_depth,
                "qosDurability": spec.qos_durability,
            }
        )
    return evidence
def _append_zenoh_topology_argv(argv: list[str], profile: MatrixProfile, args: argparse.Namespace) -> None:
    """Append the explicit Zenoh topology selection only for the Lyrical/Zenoh row."""

    if profile.rmw != zenoh_topology.ZENOH_RMW:
        return
    argv.extend(
        [
            "--zenoh-topology-id",
            args.zenoh_topology_id,
            "--zenoh-router-ready-marker",
            args.zenoh_router_ready_marker,
        ]
    )
    if args.zenoh_router is not None:
        argv.extend(["--zenoh-router", str(args.zenoh_router)])
    else:
        argv.append("--no-zenoh-router")
def _topology_options(profile: MatrixProfile, args: argparse.Namespace) -> zenoh_topology.ZenohTopologyOptions:
    """Resolve the profile's topology once without starting a process."""

    try:
        return zenoh_topology.validate_topology_options(
            profile.rmw,
            router=args.zenoh_router,
            no_router=args.no_zenoh_router,
            topology_id=args.zenoh_topology_id,
        )
    except zenoh_topology.ZenohTopologyError as exc:
        raise MatrixFailure(exc.category, "The selected Phase179 profile requires an explicit Zenoh topology.") from exc
    except ValueError as exc:
        raise MatrixFailure("ZENOH_TOPOLOGY", "Zenoh topology arguments do not match the selected profile.") from exc
def _read_summary(path: pathlib.Path) -> Mapping[str, object]:
    """Read one helper-written portable JSON summary without exposing raw parse diagnostics."""

    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise MatrixFailure("SUMMARY", "A required Phase179 evidence summary could not be read.") from exc
    if not isinstance(value, Mapping):
        raise MatrixFailure("SUMMARY", "A required Phase179 evidence summary was not a JSON object.")
    return value
def _write_summary(path: pathlib.Path, summary: Mapping[str, object]) -> None:
    """Persist only the common sanitized evidence schema outside package source directories."""

    inbound.write_summary(path, summary)
def _parser(profile: MatrixProfile) -> argparse.ArgumentParser:
    """Create the common named-profile parser without ever exposing a distro/RMW override."""

    parser = argparse.ArgumentParser(
        description=(
            f"Phase179 fixed profile {profile.profile_id} ({profile.distro}/{profile.rmw}). "
            "Use one role at a time; the local Editor role emits a Windows-local PASS."
        )
    )
    parser.add_argument(
        "--role",
        choices=("linux-peer", "windows-editor", "windows-local-editor", "windows-player", "correlate"),
        required=True,
    )
    parser.add_argument("--surface", choices=("editor", "player"), default=None)
    parser.add_argument("--domain-id", type=inbound.parse_domain_id, default=0)
    parser.add_argument("--discovery-range", default="SUBNET")
    parser.add_argument("--token", type=inbound.parse_token, default=None)
    parser.add_argument("--summary-json", type=pathlib.Path, default=None)
    parser.add_argument("--linux-summary-json", type=pathlib.Path, default=None)
    parser.add_argument("--windows-summary-json", type=pathlib.Path, default=None)
    parser.add_argument("--timeout-seconds", type=inbound.positive_seconds, default=45.0)
    parser.add_argument("--ready-timeout-seconds", type=inbound.positive_seconds, default=45.0)
    parser.add_argument("--apply-timeout-seconds", type=inbound.positive_seconds, default=45.0)
    parser.add_argument("--exit-timeout-seconds", type=inbound.positive_seconds, default=120.0)
    parser.add_argument("--string-burst-final-sequence", type=inbound.nonnegative_sequence, default=None)
    parser.add_argument("--string-burst-rate-hz", type=inbound.positive_seconds, default=500.0)
    parser.add_argument("--unity-log", type=pathlib.Path, default=None)
    parser.add_argument("--player", type=pathlib.Path, default=None)
    parser.add_argument("--player-log", type=pathlib.Path, default=None)
    parser.add_argument("--ros2-root", type=pathlib.Path, default=None)
    parser.add_argument("--zenoh-router", type=pathlib.Path, default=None)
    parser.add_argument("--no-zenoh-router", action="store_true")
    parser.add_argument("--zenoh-topology-id", type=inbound.parse_token, default=None)
    parser.add_argument("--zenoh-router-ready-marker", type=inbound.parse_ready_marker, default="Started")
    return parser
def _default_unity_editor_log_path() -> pathlib.Path:
    """Return Unity's standard Windows Editor log path for the one-command local acceptance flow."""

    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        return pathlib.Path(local_app_data) / "Unity" / "Editor" / "Editor.log"
    return pathlib.Path.home() / "AppData" / "Local" / "Unity" / "Editor" / "Editor.log"
def _generated_local_token(profile: MatrixProfile) -> str:
    """Create one bounded opaque token for a local Editor run without exposing it as operator work."""

    return f"phase179-{profile.profile_id}-local-{uuid.uuid4().hex}"
def parse_profile_args(profile: MatrixProfile, argv: Sequence[str]) -> argparse.Namespace:
    """Parse one fixed-profile command and reject role combinations that cannot yield sound evidence."""

    parser = _parser(profile)
    try:
        validate_no_profile_overrides(argv)
    except ValueError as exc:
        parser.error(str(exc))
    args = parser.parse_args(argv)

    if args.role == "windows-player":
        if args.surface not in (None, "player"):
            parser.error("--role windows-player requires --surface player when a surface is supplied")
        args.surface = "player"
    elif args.role == "windows-local-editor":
        if args.surface not in (None, "editor"):
            parser.error("--role windows-local-editor requires --surface editor when a surface is supplied")
        args.surface = "editor"
        if args.token is None:
            args.token = _generated_local_token(profile)
        if args.unity_log is None:
            args.unity_log = _default_unity_editor_log_path()
    elif args.role in ("linux-peer", "windows-editor", "correlate") and args.surface is None:
        parser.error(f"--role {args.role} requires --surface editor or player")

    if args.role in ("linux-peer", "windows-editor", "windows-local-editor", "windows-player") and args.token is None:
        parser.error(f"--role {args.role} requires --token for two-sided evidence correlation")
    if args.role == "windows-editor" and args.surface != "editor":
        parser.error("--role windows-editor requires --surface editor")
    if args.role == "windows-local-editor" and args.surface != "editor":
        parser.error("--role windows-local-editor requires --surface editor")
    if args.role == "linux-peer" and args.surface not in ("editor", "player"):
        parser.error("--role linux-peer requires --surface editor or player")
    if args.role in ("windows-editor", "windows-local-editor") and args.unity_log is None:
        parser.error("--role windows-editor requires --unity-log")
    if args.role == "windows-player" and (args.player is None or args.player_log is None):
        parser.error("--role windows-player requires --player and --player-log")
    if args.role == "correlate" and (args.linux_summary_json is None or args.windows_summary_json is None):
        parser.error("--role correlate requires --linux-summary-json and --windows-summary-json")
    if args.role != "correlate" and (args.linux_summary_json is not None or args.windows_summary_json is not None):
        parser.error("--linux-summary-json and --windows-summary-json are valid only with --role correlate")
    if args.role == "correlate" and (args.zenoh_router is not None or args.no_zenoh_router or args.zenoh_topology_id is not None):
        parser.error("--role correlate reads Zenoh topology identity from its two summaries; do not supply topology ownership arguments")

    if args.string_burst_final_sequence is not None and args.role not in ("linux-peer", "windows-player"):
        parser.error("--string-burst-final-sequence is supported only by linux-peer and windows-player roles")
    if args.role != "linux-peer" and args.string_burst_rate_hz != 500.0:
        parser.error("--string-burst-rate-hz is valid only with --role linux-peer")

    if args.role != "correlate":
        try:
            _topology_options(profile, args)
        except MatrixFailure as exc:
            parser.error(str(exc))

    if args.summary_json is None:
        args.summary_json = profile_evidence_path(profile, role=args.role, surface=args.surface)
    return args
def run_linux_peer(profile: MatrixProfile, args: argparse.Namespace) -> int:
    """Delegate one Linux half-evidence run while explicitly retaining its documented pending exit code."""

    child_argv = build_linux_peer_argv(
        profile,
        surface=args.surface,
        token=args.token,
        domain_id=args.domain_id,
        discovery_range=args.discovery_range,
        summary_json=args.summary_json,
    )
    child_argv.extend(["--timeout-seconds", str(args.timeout_seconds)])
    if args.string_burst_final_sequence is not None:
        child_argv.extend(
            [
                "--string-burst-final-sequence",
                str(args.string_burst_final_sequence),
                "--string-burst-rate-hz",
                str(args.string_burst_rate_hz),
            ]
        )
    _append_zenoh_topology_argv(child_argv, profile, args)
    exit_code = inbound.main(child_argv)
    try:
        validate_linux_peer_result(exit_code, _read_summary(args.summary_json), profile, surface=args.surface, token=args.token)
    except MatrixFailure as exc:
        print(f"[phase179:{profile.profile_id}] Linux half-evidence rejected: {exc.category}", file=sys.stderr, flush=True)
        return 1
    print(
        f"[phase179:{profile.profile_id}] Linux publication half-evidence complete for {args.surface}; correlation pending.",
        flush=True,
    )
    return 2
def run_windows_player(profile: MatrixProfile, args: argparse.Namespace) -> int:
    """Delegate one Player half-evidence run without injecting Windows ROS2 CLI DLL paths into the Player."""

    child_argv = build_windows_player_argv(
        profile,
        player=args.player,
        player_log=args.player_log,
        token=args.token,
        domain_id=args.domain_id,
        discovery_range=args.discovery_range,
        summary_json=args.summary_json,
    )
    child_argv.extend(
        [
            "--ready-timeout-seconds",
            str(args.ready_timeout_seconds),
            "--exit-timeout-seconds",
            str(args.exit_timeout_seconds),
        ]
    )
    if args.string_burst_final_sequence is not None:
        child_argv.extend(["--string-burst-final-sequence", str(args.string_burst_final_sequence)])
    _append_zenoh_topology_argv(child_argv, profile, args)
    exit_code = player_host.main(child_argv)
    try:
        validate_windows_player_result(exit_code, _read_summary(args.summary_json), profile, token=args.token)
    except MatrixFailure as exc:
        print(f"[phase179:{profile.profile_id}] Player half-evidence rejected: {exc.category}", file=sys.stderr, flush=True)
        return 1
    print(f"[phase179:{profile.profile_id}] Player copied-value half-evidence complete; correlation pending.", flush=True)
    return 2
def _editor_marker_evidence(profile: MatrixProfile, token: str, markers: Mapping[str, inbound.UnityMarker]) -> list[dict[str, object]]:
    """Serialize bounded copied values that were already checked against the fixed profile contract."""

    result: list[dict[str, object]] = []
    for name in profile.message_set:
        marker = markers[name]
        spec = inbound.MESSAGE_SPECS[name]
        if marker.value != spec.expected_value(token):
            raise MatrixFailure("EDITOR_APPLIED", "Editor copied-value evidence did not match the fixed profile payload.")
        result.append(
            {
                "name": name,
                "topic": marker.topic,
                "received": marker.received,
                "applied": marker.applied,
                "replaced": marker.replaced,
                "value": marker.value,
            }
        )
    return result
def run_windows_editor_host(profile: MatrixProfile, args: argparse.Namespace) -> int:
    """Collect the Windows Editor half-evidence using fresh log offsets and repo-local ROS2 CLI preflight."""

    summary: dict[str, object] = {
        "phase": 179,
        "role": "windows-editor-host",
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
    exit_code = 1
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
        options = _topology_options(profile, args)
        topology_handle = zenoh_topology.start_topology(
            options,
            env=env,
            cwd=workspace_root(),
            log_path=args.summary_json.with_name(args.summary_json.stem + "-zenoh-router.log"),
            ready_timeout_seconds=args.ready_timeout_seconds,
            ready_marker=args.zenoh_router_ready_marker,
        )
        summary["zenohTopology"] = inbound.topology_summary(topology_handle)

        ready_offset = inbound.unity_log_offset(args.unity_log)
        inbound.wait_for_unity_ready_marker(
            args.unity_log,
            profile.distro,
            profile.rmw,
            None,
            args.ready_timeout_seconds,
            start_offset=ready_offset,
        )
        summary["ready"] = True
        apply_offset = inbound.unity_log_offset(args.unity_log)
        summary["endpointEvidence"] = validate_windows_subscription_endpoints(
            python,
            ros2_script,
            env,
            profile,
            timeout_seconds=args.ready_timeout_seconds,
        )
        summary["windowsRos2Preflight"] = "passed"
        print(
            f"[phase179:{profile.profile_id}] Editor READY and Windows ROS2 subscription preflight complete; "
            f"run the matching Linux peer with token {args.token}.",
            flush=True,
        )
        markers: dict[str, inbound.UnityMarker] = {}
        for name in profile.message_set:
            spec = inbound.MESSAGE_SPECS[name]
            markers[name] = inbound.wait_for_unity_marker(
                args.unity_log,
                inbound.topic_for_spec(profile.topic_prefix, spec),
                args.token,
                spec.expected_value(args.token),
                args.apply_timeout_seconds,
                start_offset=apply_offset,
            )
        summary["messageResults"] = _editor_marker_evidence(profile, args.token, markers)
        summary["allRequiredApplied"] = True
        summary["verdict"] = "WINDOWS_EDITOR_PROOF_COMPLETE_LINUX_PEER_CORRELATION_PENDING"
        exit_code = 2
    except MatrixFailure as exc:
        failure_category = exc.category
    except inbound.AcceptanceFailure as exc:
        failure_category = exc.category
    except zenoh_topology.ZenohTopologyError as exc:
        failure_category = exc.category
    except KeyboardInterrupt:
        failure_category = "INTERRUPTED"
    except (OSError, RuntimeError, subprocess.SubprocessError):
        failure_category = "ENVIRONMENT"
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
def _local_editor_pass_verdict(profile: MatrixProfile) -> str:
    """Return the explicit Windows-loopback success label without claiming a Linux peer result."""

    label = profile.profile_id.upper().replace("-", "_")
    return f"PHASE179_{label}_WINDOWS_LOCAL_EDITOR_PASS"


__all__ = [name for name in globals() if not name.startswith("__")]
