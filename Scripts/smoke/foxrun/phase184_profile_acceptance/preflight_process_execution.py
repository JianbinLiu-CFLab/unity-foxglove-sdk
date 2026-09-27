from __future__ import annotations
from .preflight_process_setup import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

def parse_bridge_publisher_evidence(
    config: Mapping[str, object],
    log_path: pathlib.Path,
) -> dict[str, object]:
    """Parse only the dedicated current-run sidecar log."""

    expected = _expected_qos_by_topic(config)
    observed: dict[str, dict[str, object]] = {}
    for line in read_log_lines(log_path):
        match = _BRIDGE_PUBLISHER_LINE.search(line)
        if match is None:
            continue
        topic = match.group("topic")
        if topic not in expected:
            continue
        observed[topic] = {
            "profile": match.group("profile"),
            "reliability": match.group("reliability"),
            "durability": match.group("durability"),
            "history": match.group("history"),
            "depth": int(match.group("depth")),
        }
    if set(observed) != set(expected):
        raise AcceptanceFailure(
            "FAIL_BRIDGE",
            "Bridge did not log every expected publisher contract.",
        )
    for topic, actual in observed.items():
        requested = expected[topic]
        if _normalized_policy(actual["profile"]) != requested["profile"]:
            raise AcceptanceFailure(
                "FAIL_QOS",
                f"Bridge parsed QoS profile drifted for {topic}.",
            )
        comparable = {
            "reliability": actual["reliability"],
            "durability": actual["durability"],
            "history": actual["history"],
            "depth": actual["depth"],
        }
        if not _qos_equals(comparable, requested):
            raise AcceptanceFailure(
                "FAIL_QOS",
                f"Bridge parsed QoS drifted for {topic}.",
            )
    return {
        "nodeIdentity": "unity2foxglove_ros2_bridge",
        "publishers": observed,
    }
def _unity_version_from_log(log_path: pathlib.Path) -> str:
    """Handle the unity version from log step."""

    for line in read_log_lines(log_path):
        match = _UNITY_VERSION.search(line)
        if match is not None and match.group(1).strip():
            return match.group(1).strip()
    raise AcceptanceFailure("FAIL_TERMINAL", "Unity log has no exact Editor version.")
def _marker_int(marker: TerminalMarker, name: str) -> int:
    """Handle the marker int step."""

    value = marker.fields.get(name)
    try:
        parsed = int(value) if value is not None else -1
    except ValueError as exc:
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            f"Unity terminal field {name!r} is not an integer.",
        ) from exc
    if parsed < 0:
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            f"Unity terminal field {name!r} is missing or negative.",
        )
    return parsed
def _validated_stream_evidence(
    terminal: TerminalMarker,
    peer: Mapping[str, object],
) -> dict[str, object]:
    """Cross-check Unity ownership counters against the independent ROS producer."""

    received = _marker_int(terminal, "received")
    accepted = _marker_int(terminal, "accepted")
    drained = _marker_int(terminal, "drained")
    replaced = _marker_int(terminal, "replaced")
    rate_dropped = _marker_int(terminal, "rateDropped")
    high_water = _marker_int(terminal, "highWater")
    disposal_failures = _marker_int(terminal, "disposalFailures")
    last_sequence = _marker_int(terminal, "lastSequence")
    try:
        peer_offered = int(peer.get("offered", -1))
        nominal_hz = int(peer.get("nominalHz", -1))
        elapsed = float(peer.get("productionElapsedSeconds", -1.0))
    except (TypeError, ValueError) as exc:
        raise AcceptanceFailure(
            "FAIL_STREAM",
            "ROS stream producer evidence is malformed.",
        ) from exc
    if peer_offered != 1280:
        raise AcceptanceFailure(
            "FAIL_STREAM",
            "The ROS stream producer did not offer the locked 1280-sample run.",
        )
    if nominal_hz != 640:
        raise AcceptanceFailure(
            "FAIL_STREAM",
            "ROS stream production did not prove the nominal 640 Hz interval.",
        )
    elapsed = _validated_stream_production_elapsed(elapsed)
    if disposal_failures != 0:
        raise AcceptanceFailure(
            "FAIL_STREAM",
            "Unity reported a stream disposal failure.",
        )
    if (
        received <= protocol.STREAM_CAPACITY
        or received > peer_offered
        or accepted + rate_dropped != received
        or drained + replaced != accepted
        or high_water != protocol.STREAM_CAPACITY
        or replaced <= 0
        or (last_sequence + 1) * 1000
        < peer_offered * protocol.MIN_STREAM_LAST_SEQUENCE_PERMILLE
    ):
        raise AcceptanceFailure(
            "FAIL_STREAM",
            "Unity stream counters do not prove bounded retained delivery.",
        )
    transport_dropped = peer_offered - received
    return {
        "offered": peer_offered,
        "received": received,
        "accepted": accepted,
        "replaced": replaced,
        "rateDropped": rate_dropped,
        "transportDropped": transport_dropped,
        "dropped": transport_dropped + rate_dropped,
        "drained": drained,
        "disposed": drained + replaced,
        "maximumQueueDepth": high_water,
        "lastSequence": last_sequence,
        "retainedOrdered": terminal.fields.get("ordered") == "True",
        "ownershipBalanced": terminal.fields.get("ownershipBalanced") == "True",
    }
def _ensure_acceptance_scene(
    editor: pathlib.Path,
    repository: pathlib.Path,
    output: pathlib.Path,
    job: WindowsKillOnCloseJob | None,
) -> pathlib.Path:
    """Handle the ensure acceptance scene step."""

    scene = (
        pathlib.Path(repository)
        / "Unity2Foxglove"
        / "Assets"
        / "Scenes"
        / "ManualAcceptance"
        / "Phase184FoxRunProfileAcceptance.unity"
    )
    command = [
        str(editor),
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath",
        str(pathlib.Path(repository) / "Unity2Foxglove"),
        "-executeMethod",
        "Unity2Foxglove.Phase184FoxRunProfileAcceptanceBuilder.CreateOrRefreshAcceptanceScene",
        "-logFile",
        str(output / "scene-builder.log"),
    ]
    _run_logged_preflight(
        command,
        cwd=repository,
        environment=_clean_environment(os.environ),
        log_path=output / "scene-builder-process.log",
        job=job,
        failure_code="FAIL_UNITY_STARTUP",
        operation="unity-startup",
        progress_paths=(output / "scene-builder.log",),
    )
    if not scene.is_file() or "PHASE184G_SCENE_BUILDER_PASS" not in "\n".join(
        read_log_lines(output / "scene-builder.log")
    ):
        raise AcceptanceFailure(
            "FAIL_UNITY_STARTUP",
            "Unity did not create and validate the Phase184 acceptance scene.",
        )
    return scene
def _select_unity_runtime(
    *,
    peer,
    editor: pathlib.Path,
    repository: pathlib.Path,
    output: pathlib.Path,
    distro: str,
    rmw: str,
    job: WindowsKillOnCloseJob | None,
) -> None:
    """Handle the select unity runtime step."""

    selection_log = output / "runtime-selection.log"
    current_selection = _current_unity_runtime_selection_evidence(
        repository,
        distro,
        rmw,
    )
    if current_selection is not None:
        try:
            selection_log.write_text(
                "PHASE184G_RUNTIME_SELECTION_REUSED "
                f"distro={current_selection['rosDistro']} "
                f"rmw={current_selection['rmwImplementation']} "
                f"runtime={current_selection['runtimePackage']} "
                f"typesupport={current_selection['typesupportPackage']}\n",
                encoding="utf-8",
            )
        except OSError as exc:
            raise AcceptanceFailure(
                "FAIL_RUNTIME_SELECTION",
                "The reused Unity runtime selection evidence could not be persisted.",
            ) from exc
        return

    command = peer.build_runtime_selection_batch_command(
        editor,
        repository / "Unity2Foxglove",
        selection_log,
        distro,
        rmw,
    )
    _run_logged_preflight(
        command,
        cwd=repository,
        environment=peer.ros2env.sanitized_subprocess_env(os.environ),
        log_path=output / "runtime-selection-process.log",
        job=job,
        failure_code="FAIL_RUNTIME_SELECTION",
        operation="runtime-selection",
        progress_paths=(selection_log,),
    )
    if peer._RUNTIME_SELECTION_READY_MARKER not in "\n".join(
        read_log_lines(selection_log)
    ):
        raise AcceptanceFailure(
            "FAIL_RUNTIME_SELECTION",
            "Unity runtime selection exited without its validated readiness marker.",
        )
__all__ = [name for name in globals() if not name.startswith("__")]


__all__ = [name for name in globals() if not name.startswith("__")]
