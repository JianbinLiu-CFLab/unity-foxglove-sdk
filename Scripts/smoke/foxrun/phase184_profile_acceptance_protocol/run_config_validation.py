from __future__ import annotations
from .configuration_and_contracts import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def failure_code(stage: str, *, blocked: bool = False) -> str:
    """Return the stable public code for one planned failure domain."""

    if stage not in FAILURE_CODES:
        raise ValueError(f"Unknown Phase184-G failure stage: {stage}")
    prefix = "BLOCKED" if blocked else "FAIL"
    return f"{prefix}_{stage.replace('-', '_').upper()}"


def _fail(stage: str, message: str, *, blocked: bool = False) -> ProtocolFailure:
    """Handle the fail step."""

    return ProtocolFailure(failure_code(stage, blocked=blocked), message)


def validate_case_profile(case: str, profile: str | None) -> CaseContract:
    """Resolve a case's locked profile and reject contradictory overrides."""

    contract = CASE_CONTRACTS.get(case)
    if contract is None:
        raise _fail("preflight", f"Unknown Phase184-G case {case!r}.")
    if profile is not None and profile != contract.profile:
        raise _fail(
            "runtime-selection",
            f"Case {case!r} requires profile {contract.profile!r}, not {profile!r}.",
        )
    return contract


def validate_execution_mode(*, batch: bool, manual_editor: bool) -> str:
    """Require exactly one supported Unity execution mode."""

    if batch == manual_editor:
        raise _fail(
            "preflight",
            "Exactly one of Batch mode and manual Editor mode must be selected.",
        )
    return "batch" if batch else "manual"


def token_sha256(token: str) -> str:
    """Hash a correlation token without retaining it in durable evidence."""

    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def _require_mapping(value: object, label: str, stage: str = "preflight") -> Mapping[str, Any]:
    """Require mapping."""

    if not isinstance(value, Mapping):
        raise _fail(stage, f"{label} must be an object.")
    return value


def _require_string(value: object, label: str, stage: str = "preflight") -> str:
    """Require string."""

    if not isinstance(value, str) or not value:
        raise _fail(stage, f"{label} must be a non-empty string.")
    return value


def _require_bounded_int(
    value: object,
    label: str,
    minimum: int,
    maximum: int,
    stage: str = "preflight",
) -> int:
    """Require bounded int."""

    if isinstance(value, bool) or not isinstance(value, int):
        raise _fail(stage, f"{label} must be an integer.")
    if value < minimum or value > maximum:
        raise _fail(stage, f"{label} must be in [{minimum}, {maximum}].")
    return value


def _resolved_absolute_path(value: object, label: str) -> pathlib.Path:
    """Handle the resolved absolute path step."""

    text = _require_string(value, label)
    candidate = pathlib.Path(text)
    if not candidate.is_absolute():
        raise _fail("preflight", f"{label} must be absolute.")
    try:
        return candidate.resolve(strict=False)
    except OSError as exc:
        raise _fail("preflight", f"{label} cannot be resolved: {exc}") from exc


def _is_below(candidate: pathlib.Path, parent: pathlib.Path) -> bool:
    """Return whether below."""

    return candidate == parent or parent in candidate.parents


def _require_exact_keys(
    value: Mapping[str, Any],
    expected: set[str],
    label: str,
    stage: str = "preflight",
) -> None:
    """Require exact keys."""

    actual = set(value)
    if actual != expected:
        missing = sorted(expected - actual)
        unexpected = sorted(actual - expected)
        raise _fail(
            stage,
            f"{label} keys differ; missing={missing}, unexpected={unexpected}.",
        )


def validate_run_config(
    config: Mapping[str, Any],
    repo_root: os.PathLike[str] | str,
) -> CaseContract:
    """Validate the immutable coordination authority before any actor starts."""

    config = _require_mapping(config, "run-config")
    required_keys = {
        "schemaVersion",
        "executionMode",
        "runId",
        "token",
        "case",
        "profile",
        "projectPath",
        "outputRoot",
        "rosDistro",
        "rmw",
        "domainId",
        "discoveryRange",
        "zenohTopologyId",
        "phase181Workspace",
        "phase181Install",
        "bridgeOverlayInstall",
        "foxgloveHost",
        "foxglovePort",
        "bridgeHost",
        "bridgePort",
        "interfacePackage",
        "interfaceType",
        "interfaceDigest",
        "topics",
        "observationWindows",
        "readyFiles",
        "resultFiles",
        "unityLog",
    }
    _require_exact_keys(config, required_keys, "run-config")

    if config["schemaVersion"] != RUN_CONFIG_SCHEMA_VERSION:
        raise _fail("preflight", "Unsupported run-config schemaVersion.")
    if config["executionMode"] not in {"batch", "manual"}:
        raise _fail("preflight", "executionMode must be batch or manual.")

    run_id = _require_string(config["runId"], "runId")
    token = _require_string(config["token"], "token")
    if _SAFE_RUN_ID.fullmatch(run_id) is None:
        raise _fail("preflight", "runId contains unsafe characters or length.")
    if _SAFE_TOKEN.fullmatch(token) is None:
        raise _fail("preflight", "token contains unsafe characters or length.")

    case = _require_string(config["case"], "case")
    profile = _require_string(config["profile"], "profile")
    contract = validate_case_profile(case, profile)
    profile_contract = PROFILE_CONTRACTS[profile]
    if config["rosDistro"] != profile_contract.runtime or config["rmw"] != profile_contract.rmw:
        raise _fail(
            "runtime-selection",
            f"Profile {profile!r} requires runtime/RMW "
            f"{profile_contract.runtime!r}/{profile_contract.rmw!r}.",
        )

    repo = pathlib.Path(repo_root).resolve(strict=False)
    project = _resolved_absolute_path(config["projectPath"], "projectPath")
    output = _resolved_absolute_path(config["outputRoot"], "outputRoot")
    expected_project = (repo / "Unity2Foxglove").resolve(strict=False)
    acceptance_root = (repo / "build" / "phase184" / "acceptance").resolve(strict=False)
    if project != expected_project:
        raise _fail("preflight", "projectPath must select the repository Unity project.")
    if not _is_below(output, acceptance_root) or output == acceptance_root:
        raise _fail("preflight", "outputRoot must be an owned Phase184 acceptance run.")
    if output.name != run_id:
        raise _fail("preflight", "outputRoot leaf must equal runId.")

    phase181_root = (repo / "build" / "phase181").resolve(strict=False)
    phase181_workspace = _resolved_absolute_path(
        config["phase181Workspace"], "phase181Workspace"
    )
    phase181_install = _resolved_absolute_path(
        config["phase181Install"], "phase181Install"
    )
    bridge_overlay = _resolved_absolute_path(
        config["bridgeOverlayInstall"], "bridgeOverlayInstall"
    )
    if not _is_below(phase181_workspace, phase181_root):
        raise _fail("preflight", "phase181Workspace escaped build/phase181.")
    if not _is_below(phase181_install, phase181_workspace):
        raise _fail("preflight", "phase181Install escaped phase181Workspace.")
    if "bridge" in contract.required_actors:
        expected_bridge_overlay = (
            repo
            / "build"
            / "phase184"
            / "bridge-cache"
            / profile
            / "bridge-overlay"
            / "install"
        ).resolve(strict=False)
        if bridge_overlay != expected_bridge_overlay:
            raise _fail(
                "preflight",
                "bridgeOverlayInstall must select the exact profile-stable Bridge cache.",
            )
    elif not _is_below(bridge_overlay, output):
        raise _fail("preflight", "Unused bridgeOverlayInstall escaped outputRoot.")

    loopback_hosts = {"127.0.0.1", "localhost", "::1"}
    for key in ("foxgloveHost", "bridgeHost"):
        if config[key] not in loopback_hosts:
            raise _fail("preflight", f"{key} must be loopback.")
    foxglove_port = _require_bounded_int(
        config["foxglovePort"], "foxglovePort", 1, 65535
    )
    bridge_port = _require_bounded_int(config["bridgePort"], "bridgePort", 1, 65535)
    if foxglove_port == bridge_port:
        raise _fail("preflight", "Foxglove and Bridge ports must be distinct.")
    _require_bounded_int(config["domainId"], "domainId", 0, 232)
    if config["discoveryRange"] != profile_contract.discovery_range:
        raise _fail(
            "preflight",
            f"Profile {profile!r} requires discoveryRange "
            f"{profile_contract.discovery_range!r}.",
        )

    topology_id = config["zenohTopologyId"]
    if profile == "lyrical-zenoh":
        if not isinstance(topology_id, str) or _SAFE_TOPOLOGY_ID.fullmatch(topology_id) is None:
            raise _fail("runtime-selection", "Zenoh profile requires a safe topology id.")
    elif topology_id != "":
        raise _fail("runtime-selection", "Non-Zenoh profiles must not select a topology.")

    package = _require_string(config["interfacePackage"], "interfacePackage")
    interface_type = _require_string(config["interfaceType"], "interfaceType")
    digest = _require_string(config["interfaceDigest"], "interfaceDigest")
    if _SAFE_INTERFACE_PACKAGE.fullmatch(package) is None:
        raise _fail("preflight", "interfacePackage is malformed.")
    if (
        _SAFE_INTERFACE_TYPE.fullmatch(interface_type) is None
        or not interface_type.startswith(f"{package}/msg/")
    ):
        raise _fail("preflight", "interfaceType is malformed or has another package.")
    if _LOWER_SHA256.fullmatch(digest) is None:
        raise _fail("preflight", "interfaceDigest must be a lowercase SHA-256.")

    topics = config["topics"]
    if not isinstance(topics, list) or tuple(topics) != contract.topics:
        raise _fail("preflight", f"topics do not match case {case!r}.")
    if any(not is_valid_ros_topic_name(topic) for topic in topics):
        raise _fail(
            "preflight",
            f"Case {case!r} contains a non-concrete ROS2 topic name.",
        )

    windows = _require_mapping(config["observationWindows"], "observationWindows")
    expected_windows = {
        "positiveSeconds",
        "negativeSeconds",
        "streamProductionSeconds",
        "terminalSeconds",
        "teardownSeconds",
    }
    _require_exact_keys(windows, expected_windows, "observationWindows")
    for key in expected_windows:
        _require_bounded_int(windows[key], f"observationWindows.{key}", 1, 3600)

    actors = contract.required_actors | frozenset(contract.deliberately_absent_actors)
    expected_actor_keys = set(actors)
    for map_name, directory in (("readyFiles", "ready"), ("resultFiles", "results")):
        paths = _require_mapping(config[map_name], map_name)
        _require_exact_keys(paths, expected_actor_keys, map_name)
        for actor in actors:
            actual = _resolved_absolute_path(paths[actor], f"{map_name}.{actor}")
            expected = (output / directory / f"{actor}.json").resolve(strict=False)
            if actual != expected:
                raise _fail(
                    "preflight",
                    f"{map_name}.{actor} must use its immutable owned path.",
                )

    unity_log = _resolved_absolute_path(config["unityLog"], "unityLog")
    if unity_log != (output / "unity-editor.log").resolve(strict=False):
        raise _fail("preflight", "unityLog must use the owned run log path.")

    return contract


_REQUIRED_SECTION_FIELDS: Mapping[str, set[str]] = {
    "foxglove": {
        "deliveryObserved",
        "channelEncodings",
        "sampleToken",
        "sampleStages",
        "timestamp",
    },
    "rosGraph": {
        "endpointsObserved",
        "nodeIdentities",
        "publisherGids",
        "publishersByTopic",
        "samplePublisherGids",
        "negativeObservationSeconds",
    },
    "qos": {"requested", "transportObserved", "matches"},
    "targets": {
        "states",
        "diagnosticCounts",
        "healthyDelivery",
        "statusEvidence",
    },
    "origin": {"remoteApplied", "sameOriginDropped", "laterLocalPublished"},
    "stream": {
        "offered",
        "received",
        "accepted",
        "replaced",
        "rateDropped",
        "transportDropped",
        "dropped",
        "drained",
        "disposed",
        "maximumQueueDepth",
        "lastSequence",
        "retainedOrdered",
        "ownershipBalanced",
    },
}

_SECTION_BOOLEAN_FIELDS: Mapping[str, set[str]] = {
    "foxglove": {"deliveryObserved"},
    "rosGraph": {"endpointsObserved"},
    "qos": {"matches"},
    "targets": {"healthyDelivery"},
    "origin": {"remoteApplied", "sameOriginDropped", "laterLocalPublished"},
    "stream": {"retainedOrdered", "ownershipBalanced"},
}

_SECTION_FAILURE_STAGE = {
    "foxglove": "client",
    "rosGraph": "graph",
    "qos": "qos",
    "targets": "fanout",
    "origin": "origin",
    "stream": "stream",
}



__all__ = [name for name in globals() if not name.startswith("__")]
