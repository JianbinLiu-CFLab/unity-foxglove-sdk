from __future__ import annotations
from .qos_and_graph_evidence import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def _run_peer_graph_auditor(
    config: Mapping[str, object],
) -> Mapping[str, object]:
    """Independently validate a raw graph snapshot captured by the ROS peer."""

    write_actor_ready(
        config,
        "graph-observer",
        {"state": "peer-graph-auditor-ready", "topicCount": len(config["topics"])},
    )
    _wait_for_unity_context(config)
    peer_result = _wait_for_peer_result_document(config)
    evidence = peer_result.get("evidence")
    graph_evidence = (
        evidence.get("graphEvidence")
        if isinstance(evidence, Mapping)
        else None
    )
    if (
        not isinstance(graph_evidence, Mapping)
        or graph_evidence.get("source") != "ros2-peer-rclpy-graph-api"
    ):
        raise AcceptanceFailure(
            "FAIL_GRAPH",
            "The ROS peer graph evidence source is missing or invalid.",
        )
    raw_topics = graph_evidence.get("topics")
    expected_topics = [str(item) for item in config["topics"]]
    if not isinstance(raw_topics, Mapping) or set(raw_topics) != set(expected_topics):
        raise AcceptanceFailure(
            "FAIL_GRAPH",
            "The ROS peer graph snapshot topics are incomplete.",
        )
    graphs: dict[str, dict[str, list[dict[str, object]]]] = {}
    for topic in expected_topics:
        raw_graph = raw_topics.get(topic)
        if not isinstance(raw_graph, Mapping):
            raise AcceptanceFailure("FAIL_GRAPH", "A graph topic entry is malformed.")
        graph: dict[str, list[dict[str, object]]] = {}
        for direction in ("publishers", "subscriptions"):
            entries = raw_graph.get(direction)
            if (
                not isinstance(entries, list)
                or any(not isinstance(entry, Mapping) for entry in entries)
            ):
                raise AcceptanceFailure(
                    "FAIL_GRAPH",
                    "A graph endpoint collection is malformed.",
                )
            graph[direction] = [dict(entry) for entry in entries]
        graphs[topic] = graph
    result = _graph_evidence_from_topics(config, graphs)
    wait_for_terminal_marker(config, 30.0)
    return result


def _run_graph_observer(config: Mapping[str, object], rclpy_module, node) -> Mapping[str, object]:
    """Run graph observer."""

    case = str(config["case"])
    topics = [str(item) for item in config["topics"]]
    if case != "degraded-target":
        raise AcceptanceFailure(
            "FAIL_GRAPH",
            "Only the degraded case uses an independent rclpy graph node.",
        )
    write_actor_ready(
        config,
        "graph-observer",
        {"state": "graph-observer-ready", "topicCount": len(topics)},
    )
    _wait_for_unity_context(config)

    wait_for_log_marker(config, "PHASE184G_DEGRADED_WINDOW_STARTED", 60.0)
    deadline = (
        time.monotonic()
        + float(config["observationWindows"]["negativeSeconds"])
        + 0.25
    )
    final_graphs: dict[str, dict[str, list[dict[str, object]]]] = {}
    while time.monotonic() < deadline:
        rclpy_module.spin_once(node, timeout_sec=0.05)
        final_graphs = {topic: _graph_for_topic(node, topic) for topic in topics}
        if not _graph_ready(config, final_graphs):
            raise AcceptanceFailure(
                "FAIL_GRAPH",
                "Degraded case exposed a forbidden Native or Bridge publisher.",
            )
    wait_for_terminal_marker(config, 30.0)
    return {
        "endpointsObserved": True,
        "negativeWindowSeconds": config["observationWindows"]["negativeSeconds"],
        "noFallbackPublisher": True,
        "nodeIdentities": [],
        "publisherGids": [],
        "publishersByTopic": {topic: [] for topic in topics},
        "negativeObservationSeconds": config["observationWindows"]["negativeSeconds"],
        "topics": final_graphs,
        "requestedQos": {},
        "transportObservedQos": {},
        "qosMatches": True,
    }


def run_graph_observer_worker(config: Mapping[str, object]) -> int:
    """Run graph observer worker."""

    role = "graph-observer"
    try:
        if str(config["case"]) == "degraded-target":
            import rclpy

            _load_ros_message_types(config)
            rclpy.init(args=None)
            node = rclpy.create_node(_helper_node_name("graph", config))
            try:
                evidence = _run_graph_observer(config, rclpy, node)
            finally:
                node.destroy_node()
                rclpy.shutdown()
        else:
            evidence = _run_peer_graph_auditor(config)
        write_actor_result(config, role, verdict="PASS", evidence=evidence)
        return 0
    except AcceptanceFailure as exc:
        write_actor_result(
            config,
            role,
            verdict=exc.code,
            evidence={"diagnostic": str(exc)[: protocol.MAX_DIAGNOSTIC_CHARACTERS]},
        )
        return 1
    except Exception as exc:
        write_actor_result(
            config,
            role,
            verdict="FAIL_GRAPH",
            evidence={"diagnostic": type(exc).__name__},
        )
        return 1


@dataclass
class PreparedRosRuntime:
    """All selected ROS/runtime state retained for one owned acceptance run."""

    peer: Any
    toolchain: Any
    lock: Any
    ros2_root: pathlib.Path
    peer_workspace: pathlib.Path
    peer_runtime_workspace: pathlib.Path
    build_environment: dict[str, str]
    actor_environment: dict[str, str]
    unity_environment: dict[str, str]
    bridge_install: pathlib.Path | None
    bridge_runtime_workspace: pathlib.Path | None
    zenoh_router: pathlib.Path | None
    zenoh_router_environment: dict[str, str] | None
    zenoh_router_config: pathlib.Path | None
    zenoh_session_config: pathlib.Path | None
    zenoh_router_endpoint: UnityZenohRouterEndpoint | None
    subst_roots: tuple[pathlib.Path, ...]


def choose_owned_loopback_port(excluded: Iterable[int] = ()) -> int:
    """Reserve and release one currently bindable IPv4 loopback port."""

    excluded_ports = {int(value) for value in excluded}
    for _ in range(32):
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
            exclusive = getattr(socket, "SO_EXCLUSIVEADDRUSE", None)
            if exclusive is not None:
                probe.setsockopt(socket.SOL_SOCKET, exclusive, 1)
            probe.bind(("127.0.0.1", 0))
            port = int(probe.getsockname()[1])
        if port not in excluded_ports:
            return port
    raise AcceptanceFailure("FAIL_PREFLIGHT", "Could not allocate a distinct loopback port.")


def require_available_loopback_port(port: int, label: str) -> int:
    """Fail before launch when an explicitly selected loopback port is occupied."""

    selected = int(port)
    if not 1 <= selected <= 65535:
        raise AcceptanceFailure("FAIL_PREFLIGHT", f"{label} port is outside 1..65535.")
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
            exclusive = getattr(socket, "SO_EXCLUSIVEADDRUSE", None)
            if exclusive is not None:
                probe.setsockopt(socket.SOL_SOCKET, exclusive, 1)
            probe.bind(("127.0.0.1", selected))
    except OSError as exc:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            f"{label} loopback port is already in use.",
        ) from exc
    return selected


def load_unity_zenoh_router_endpoint(
    repository: pathlib.Path,
) -> UnityZenohRouterEndpoint:
    """Load the exact project setting that Unity applies before ROS starts."""

    settings_path = pathlib.Path(repository) / UNITY_ZENOH_SETTINGS_RELATIVE_PATH
    try:
        size = settings_path.stat().st_size
        if size <= 0 or size > MAX_UNITY_ZENOH_SETTINGS_BYTES:
            raise AcceptanceFailure(
                "FAIL_RUNTIME_SELECTION",
                "The Unity Zenoh router setting has an invalid size.",
            )
        document = json.loads(settings_path.read_text(encoding="utf-8"))
    except AcceptanceFailure:
        raise
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise AcceptanceFailure(
            "FAIL_RUNTIME_SELECTION",
            "The Unity Zenoh router setting is unavailable or malformed.",
        ) from exc

    expected_keys = {
        "schemaVersion",
        "routerAddress",
        "routerPort",
        "endpoint",
    }
    if (
        not isinstance(document, dict)
        or set(document) != expected_keys
        or not isinstance(document.get("schemaVersion"), int)
        or isinstance(document.get("schemaVersion"), bool)
        or document["schemaVersion"] != 1
    ):
        raise AcceptanceFailure(
            "FAIL_RUNTIME_SELECTION",
            "The Unity Zenoh router setting has an unsupported schema.",
        )

    address = document["routerAddress"]
    port = document["routerPort"]
    if (
        not isinstance(address, str)
        or address not in {"localhost", "127.0.0.1"}
        or not isinstance(port, int)
        or isinstance(port, bool)
        or not 1 <= port <= 65535
    ):
        raise AcceptanceFailure(
            "FAIL_RUNTIME_SELECTION",
            "The Unity Zenoh router setting is not a valid loopback endpoint.",
        )

    endpoint = f"tcp/{address}:{port}"
    if document["endpoint"] != endpoint:
        raise AcceptanceFailure(
            "FAIL_RUNTIME_SELECTION",
            "The Unity Zenoh router setting contains inconsistent endpoint fields.",
        )
    return UnityZenohRouterEndpoint(
        endpoint=endpoint,
        host="127.0.0.1",
        port=port,
    )


def wait_for_owned_zenoh_router(
    process,
    log_path: pathlib.Path,
    endpoint: UnityZenohRouterEndpoint,
    timeout_seconds: float = 60.0,
) -> dict[str, object]:
    """Require both the owned marker and a live loopback listener."""

    deadline = time.monotonic() + timeout_seconds
    marker_observed = False
    while True:
        if process.poll() is not None:
            raise AcceptanceFailure(
                "FAIL_RUNTIME_SELECTION",
                "The owned Zenoh router exited before listener readiness.",
            )
        if not marker_observed:
            marker_observed = any(
                "Started Zenoh router with id " in line
                for line in read_log_lines(log_path)
            )
        if marker_observed:
            try:
                connection = socket.create_connection(
                    (endpoint.host, endpoint.port),
                    timeout=0.25,
                )
            except OSError:
                connection = None
            if connection is not None:
                connection.close()
                return {
                    "state": "owned-router-ready",
                    "endpoint": endpoint.endpoint,
                }
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise AcceptanceFailure(
                "FAIL_RUNTIME_SELECTION",
                "The owned Zenoh router did not reach listener readiness.",
            )
        time.sleep(min(0.1, remaining))


def choose_domain_id(requested: int | None) -> int:
    """Return one explicit ROS domain without inheriting ambient process state."""

    if requested is not None:
        if not 0 <= int(requested) <= WINDOWS_SAFE_ROS_DOMAIN_ID_MAX:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "Windows ROS domain id must be in 0..166.",
            )
        return int(requested)
    return 64 + secrets.randbelow(96)


def choose_parent_domain_id(requested: int | None, execution_mode: str) -> int:
    """Select an isolated Batch domain or the user-owned Hub Editor domain."""

    if execution_mode == "manual":
        if requested not in (None, 0):
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "Phase184 manual Editor acceptance requires ROS domain 0 because "
                "the helper cannot change a user-owned Unity Hub process environment.",
            )
        return 0
    if execution_mode != "batch":
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "Parent execution mode must be batch or manual.",
        )
    return choose_domain_id(requested)


def manual_play_prompt(case: str) -> str:
    """Describe the one helper-authorized Play session without route-selection advice."""

    return (
        "[phase184] External endpoints are ready for helper-selected case "
        + str(case)
        + ". Enter Play Mode now for exactly one Play session; this helper run is single-use."
    )


def _require_file(path: pathlib.Path, code: str, description: str) -> pathlib.Path:
    """Require file."""

    candidate = pathlib.Path(path).resolve()
    if not candidate.is_file():
        raise AcceptanceFailure(code, f"{description} is unavailable.")
    return candidate


def _new_run_identity(requested_run_id: str | None) -> tuple[str, str]:
    """Handle the new run identity step."""

    now = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%d-%H%M%S")
    suffix = uuid.uuid4().hex[:10]
    run_id = requested_run_id or f"phase184g-{now}-{suffix}"
    if re.fullmatch(r"phase184g-[A-Za-z0-9][A-Za-z0-9._-]{7,79}", run_id) is None:
        raise AcceptanceFailure("FAIL_PREFLIGHT", "run id is unsafe or malformed.")
    return run_id, "p184g_" + uuid.uuid4().hex


def _prepare_run_directory(repository: pathlib.Path, run_id: str) -> pathlib.Path:
    """Prepare run directory."""

    output = (
        pathlib.Path(repository)
        / "build"
        / "phase184"
        / "acceptance"
        / run_id
    ).resolve()
    if output.exists():
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "The selected Phase184-G run directory already exists.",
        )
    try:
        output.mkdir(parents=True, exist_ok=False)
        (output / "ready").mkdir()
        (output / "results").mkdir()
        write_private_json_atomic(
            output / ".phase184g-owned.json",
            {"schemaVersion": 1, "runId": run_id, "ownerPid": os.getpid()},
        )
    except OSError as exc:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "The owned Phase184-G run directory could not be created.",
        ) from exc
    return output


def _progress_snapshot(paths: Iterable[pathlib.Path]) -> tuple[tuple[str, int, int], ...]:
    """Return bounded file progress identities for process and tool-owned logs."""

    snapshot: list[tuple[str, int, int]] = []
    for raw_path in paths:
        path = pathlib.Path(raw_path)
        try:
            stat = path.stat()
            snapshot.append((str(path), int(stat.st_size), int(stat.st_mtime_ns)))
        except OSError:
            snapshot.append((str(path), -1, -1))
    return tuple(snapshot)




__all__ = [name for name in globals() if not name.startswith("__")]
