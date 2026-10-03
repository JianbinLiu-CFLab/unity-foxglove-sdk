from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase179_foxrun_ros2_inbound_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def build_publish_command(ros2_executable: pathlib.Path, spec: MessageSpec, token: str, topic_prefix: str = "/foxrun/phase179") -> list[str]:
    """Build a bounded ``ros2 topic pub --once`` argv with no shell interpolation."""

    return _build_publish_command(
        ros2_executable,
        topic_for_spec(topic_prefix, spec),
        spec.message_type,
        spec.qos_reliability,
        spec.qos_history,
        spec.qos_depth,
        spec.qos_durability,
        spec.publish_payload(token),
    )
def build_negative_publish_command(
    ros2_executable: pathlib.Path,
    spec: MessageSpec,
    token: str,
    negative_case: str,
    topic_prefix: str = "/foxrun/phase179",
) -> list[str]:
    """Build a deliberate wrong-type or wrong-QoS publication without changing the selected RMW."""

    topic = topic_for_spec(topic_prefix, spec)
    if negative_case == "type-mismatch":
        mismatch = next(candidate for candidate in MESSAGE_SPECS.values() if candidate.message_type != spec.message_type)
        return _build_publish_command(
            ros2_executable,
            topic,
            mismatch.message_type,
            mismatch.qos_reliability,
            mismatch.qos_history,
            mismatch.qos_depth,
            mismatch.qos_durability,
            mismatch.publish_payload(token),
        )
    if negative_case == "qos-incompatible":
        if spec.qos_reliability != "reliable":
            raise ValueError("qos-incompatible negative probes require a Reliable contract")
        return _build_publish_command(
            ros2_executable,
            topic,
            spec.message_type,
            "best_effort",
            spec.qos_history,
            spec.qos_depth,
            spec.qos_durability,
            spec.publish_payload(token),
        )
    raise ValueError(f"{negative_case} does not use a ROS2 publication command")
def expected_string_burst_value(token: str, final_sequence: int) -> dict[str, object]:
    """Return the exact bounded Unity value required after a latest-wins burst."""

    total = final_sequence + 1
    return {"type": "String", "data": f"{token}|seq={final_sequence}|total={total}"}
_STRING_BURST_PUBLISHER_CODE = r'''
import sys
import time

import rclpy
from rclpy.qos import DurabilityPolicy, HistoryPolicy, QoSProfile, ReliabilityPolicy
from std_msgs.msg import String

topic = sys.argv[1]
token = sys.argv[2]
final_sequence = int(sys.argv[3])
rate_hz = float(sys.argv[4])
total = final_sequence + 1
interval = 1.0 / rate_hz

rclpy.init(args=None)
node = rclpy.create_node("u2f_phase179_string_burst")
qos = QoSProfile(
    history=HistoryPolicy.KEEP_LAST,
    depth=10,
    reliability=ReliabilityPolicy.RELIABLE,
    durability=DurabilityPolicy.VOLATILE,
)
publisher = node.create_publisher(String, topic, qos)
try:
    discovery_deadline = time.monotonic() + 5.0
    while publisher.get_subscription_count() <= 0 and time.monotonic() < discovery_deadline:
        time.sleep(0.02)
    if publisher.get_subscription_count() <= 0:
        raise RuntimeError("No matching Unity subscription was discovered by the burst publisher")
    for sequence in range(total):
        message = String()
        message.data = f"{token}|seq={sequence}|total={total}"
        publisher.publish(message)
        if sequence != final_sequence:
            time.sleep(interval)
    time.sleep(0.1)
finally:
    node.destroy_publisher(publisher)
    node.destroy_node()
    rclpy.shutdown()
'''
def build_string_burst_command(
    python_executable: pathlib.Path,
    topic: str,
    token: str,
    final_sequence: int,
    rate_hz: float,
) -> list[str]:
    """Build one shell-free rclpy publisher process for a deterministic String burst."""

    if final_sequence < 1:
        raise ValueError("final_sequence must be at least 1 to prove latest-wins replacement")
    if not math.isfinite(rate_hz) or rate_hz <= 0.0:
        raise ValueError("rate_hz must be a finite positive number")
    return [
        str(python_executable),
        "-c",
        _STRING_BURST_PUBLISHER_CODE,
        topic,
        token,
        str(final_sequence),
        str(rate_hz),
    ]
def run_string_burst(
    env: Mapping[str, str],
    topic: str,
    token: str,
    final_sequence: int,
    rate_hz: float,
    timeout_seconds: float,
) -> None:
    """Publish one bounded latest-wins String burst from the caller's sourced ROS Python."""

    expected_duration = (final_sequence + 1) / rate_hz
    burst_timeout = min(timeout_seconds, max(2.0, expected_duration + 5.0))
    result = run_bounded_command(
        build_string_burst_command(pathlib.Path(sys.executable), topic, token, final_sequence, rate_hz),
        env,
        burst_timeout,
        "rclpy String burst",
    )
    require_command_success(result, "PUBLISH", "rclpy String burst")
def validate_string_burst_marker(
    baseline: UnityMarker,
    final: UnityMarker,
    token: str,
    final_sequence: int,
) -> dict[str, int]:
    """Prove latest-wins behavior without requiring every intermediate sample to apply."""

    if final.session != baseline.session or final.topic != baseline.topic or final.token != token:
        raise AcceptanceFailure("BURST", "Burst final marker did not belong to the active String subscription session.")
    if final.value != expected_string_burst_value(token, final_sequence):
        raise AcceptanceFailure("BURST", "Burst final marker did not contain the final deterministic String sequence.")
    if final.received <= baseline.received:
        raise AcceptanceFailure("BURST", "Burst did not increase the Unity received counter.")
    if final.replaced <= baseline.replaced:
        raise AcceptanceFailure("BURST", "Burst did not exercise latest-wins replacement.")
    if final.applied > final.received:
        raise AcceptanceFailure("BURST", "Burst marker violated applied <= received.")
    return {
        "finalSequence": final_sequence,
        "total": final_sequence + 1,
        "received": final.received,
        "applied": final.applied,
        "replaced": final.replaced,
    }
def terminate_owned_process(process: subprocess.Popen[str]) -> None:
    """Terminate precisely one helper-launched process tree, never global ROS state."""

    if process.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(
            ["taskkill", "/PID", str(process.pid), "/T", "/F"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        return

    try:
        os.killpg(process.pid, signal.SIGTERM)
    except (OSError, ProcessLookupError):
        try:
            process.terminate()
        except OSError:
            return
    try:
        process.wait(timeout=3.0)
    except subprocess.TimeoutExpired:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except (OSError, ProcessLookupError):
            try:
                process.kill()
            except OSError:
                return
def run_bounded_command(
    command: Sequence[str],
    env: Mapping[str, str],
    timeout_seconds: float,
    label: str,
) -> CommandResult:
    """Run one helper-owned argv and clean it up on timeout or interruption."""

    if not command:
        raise ValueError(f"{label} command must not be empty")
    popen_kwargs: dict[str, object] = {
        "env": dict(env),
        "text": True,
        "stdout": subprocess.PIPE,
        "stderr": subprocess.STDOUT,
    }
    if os.name != "nt":
        popen_kwargs["start_new_session"] = True
    process = subprocess.Popen(list(command), **popen_kwargs)
    try:
        output, _ = process.communicate(timeout=timeout_seconds)
        return CommandResult(tuple(command), process.returncode, output or "", False)
    except subprocess.TimeoutExpired:
        terminate_owned_process(process)
        try:
            output, _ = process.communicate(timeout=5.0)
        except subprocess.TimeoutExpired:
            output = ""
        return CommandResult(tuple(command), process.returncode, output or "", True)
    except KeyboardInterrupt:
        terminate_owned_process(process)
        raise
def find_ros2_executable(env: Mapping[str, str]) -> pathlib.Path:
    """Resolve the already-sourced ROS2 command without choosing an install root."""

    ros2 = shutil.which("ros2", path=env.get("PATH"))
    if not ros2:
        raise AcceptanceFailure("ENVIRONMENT", "ros2 was not found on PATH after sourcing the selected environment.")
    return pathlib.Path(ros2)
def require_command_success(result: CommandResult, category: str, label: str) -> None:
    """Convert a bounded command result into a stable failure without leaking output."""

    if result.timed_out:
        raise AcceptanceFailure(category, f"{label} timed out.")
    if result.return_code != 0:
        raise AcceptanceFailure(category, f"{label} exited with code {result.return_code}.")
def topic_list_has_type(output: str, topic: str, message_type: str) -> bool:
    """Return whether ``ros2 topic list -t`` reports the expected typed topic."""

    return f"{topic} [{message_type}]" in output
def wait_for_unity_subscription_topic(
    ros2_executable: pathlib.Path,
    env: Mapping[str, str],
    topic: str,
    message_type: str,
    timeout_seconds: float,
) -> str:
    """Poll the ROS graph until Unity exposes the expected typed subscription topic."""

    deadline = time.monotonic() + timeout_seconds
    last_output = ""
    while True:
        remaining = deadline - time.monotonic()
        if remaining < 0.0:
            break
        result = run_bounded_command(
            [str(ros2_executable), "topic", "list", "-t", "--no-daemon"],
            env,
            max(0.25, min(5.0, remaining or 0.25)),
            "ros2 topic list",
        )
        if not result.timed_out and result.return_code == 0:
            last_output = result.output
            if topic_list_has_type(last_output, topic, message_type):
                return last_output
        if time.monotonic() >= deadline:
            break
        time.sleep(min(0.5, max(0.0, deadline - time.monotonic())))
    raise AcceptanceFailure("DISCOVERY", "Unity did not expose the expected typed ROS2 subscription before timeout.")
def _parse_subscription_qos(section: str) -> tuple[str, str, int, str] | None:
    """Parse the portable subset of a verbose ROS2 subscription QoS block."""

    reliability_match = re.search(r"(?im)^\s*Reliability\s*:\s*([A-Z_]+)\s*$", section)
    history_match = re.search(
        r"(?im)^\s*History(?:\s*\(\s*Depth\s*\))?\s*:\s*([A-Z_]+)(?:\s*\(\s*([0-9]+)\s*\))?\s*$",
        section,
    )
    depth_match = re.search(r"(?im)^\s*Depth\s*:\s*([0-9]+)\s*$", section)
    durability_match = re.search(r"(?im)^\s*Durability\s*:\s*([A-Z_]+)\s*$", section)
    if reliability_match is None or history_match is None or durability_match is None:
        return None
    depth_text = history_match.group(2) or (depth_match.group(1) if depth_match is not None else None)
    if depth_text is None:
        return None
    return (
        reliability_match.group(1).lower(),
        history_match.group(1).lower(),
        int(depth_text),
        durability_match.group(1).lower(),
    )
def validate_unity_subscription_endpoint(topic_info: str, spec: MessageSpec) -> EndpointEvidence:
    """Require the topic type, a subscription endpoint, and the complete Phase179 QoS contract."""

    if spec.message_type not in topic_info:
        raise AcceptanceFailure("ENDPOINT", "ROS2 topic info did not report the expected message type.")
    count_match = re.search(r"Subscription count:\s*([0-9]+)", topic_info, re.IGNORECASE)
    subscription_count = int(count_match.group(1)) if count_match is not None else 0
    if subscription_count <= 0:
        raise AcceptanceFailure("ENDPOINT", "ROS2 topic info did not report a Unity subscription endpoint.")
    subscriptions = re.split(r"(?im)^\s*Subscription\s*#\d+\s*:\s*$", topic_info)[1:]
    if not subscriptions:
        raise AcceptanceFailure("ENDPOINT", "ROS2 topic info did not expose subscription QoS details.")
    expected = (spec.qos_reliability, spec.qos_history, spec.qos_depth, spec.qos_durability)
    for section in subscriptions:
        observed = _parse_subscription_qos(section)
        if observed == expected:
            return EndpointEvidence(spec.message_type, subscription_count, *observed)
    raise AcceptanceFailure("ENDPOINT", "Unity subscription QoS did not match the complete Phase179 contract.")
def query_unity_subscription_endpoint(
    ros2_executable: pathlib.Path,
    env: Mapping[str, str],
    topic: str,
    spec: MessageSpec,
    timeout_seconds: float,
) -> EndpointEvidence:
    """Capture and validate verbose endpoint evidence for one native topic."""

    deadline = time.monotonic() + timeout_seconds
    last_failure: AcceptanceFailure | None = None
    while True:
        remaining = deadline - time.monotonic()
        if remaining < 0.0:
            break
        result = run_bounded_command(
            [str(ros2_executable), "topic", "info", topic, "-v", "--no-daemon"],
            env,
            max(0.25, min(5.0, remaining or 0.25)),
            "ros2 topic info",
        )
        try:
            require_command_success(result, "ENDPOINT", "ros2 topic info")
            return validate_unity_subscription_endpoint(result.output, spec)
        except AcceptanceFailure as exc:
            last_failure = exc
        if time.monotonic() >= deadline:
            break
        time.sleep(min(0.5, max(0.0, deadline - time.monotonic())))
    if last_failure is not None:
        raise last_failure
    raise AcceptanceFailure("ENDPOINT", "ROS2 topic info did not produce endpoint evidence before timeout.")
def _parse_marker_fields(text: str) -> dict[str, str]:
    """Parse one bounded key=value marker line generated by the Unity sample."""

    return dict(re.findall(r"\b([A-Za-z][A-Za-z0-9_]*)=([^\s]+)", text))
def parse_unity_markers(text: str) -> list[UnityMarker]:
    """Parse valid applied markers, retaining only copied bounded JSON values."""

    markers: list[UnityMarker] = []
    lines = text.splitlines()
    for index, line in enumerate(lines):
        marker_index = line.find(UNITY_APPLIED_MARKER)
        if marker_index < 0:
            continue
        fields_text = line[marker_index + len(UNITY_APPLIED_MARKER) :].strip()
        if not fields_text and index + 1 < len(lines):
            fields_text = lines[index + 1].strip()
        fields = _parse_marker_fields(fields_text)
        try:
            value = json.loads(fields["value"]) if "value" in fields else None
            if value is not None and not isinstance(value, dict):
                continue
            markers.append(
                UnityMarker(
                    session=int(fields["session"]),
                    topic=fields["topic"],
                    token=fields["token"],
                    received=int(fields["received"]),
                    applied=int(fields["applied"]),
                    replaced=int(fields["replaced"]),
                    value=value,
                )
            )
        except (KeyError, TypeError, ValueError, json.JSONDecodeError):
            continue
    return markers
def parse_unity_ready_markers(text: str) -> list[UnityReadyMarker]:
    """Parse bounded native-runtime identity markers emitted by the Unity acceptance receiver."""

    markers: list[UnityReadyMarker] = []
    lines = text.splitlines()
    for index, line in enumerate(lines):
        marker_index = line.find(UNITY_READY_MARKER)
        if marker_index < 0:
            continue
        fields_text = line[marker_index + len(UNITY_READY_MARKER) :].strip()
        if not fields_text and index + 1 < len(lines):
            fields_text = lines[index + 1].strip()
        fields = _parse_marker_fields(fields_text)
        try:
            markers.append(UnityReadyMarker(fields["runtime"], fields["rmw"], fields["token"]))
        except KeyError:
            continue
    return markers
def find_matching_unity_ready_marker(
    text: str,
    runtime: str,
    rmw: str,
    token: str | None,
    excluded_tokens: Sequence[str] = (),
) -> UnityReadyMarker:
    """Return the current Unity runtime identity, rejecting any READY token captured before a local run."""

    markers = parse_unity_ready_markers(text)
    if not markers:
        raise AcceptanceFailure("READY_TIMEOUT", "Unity did not yet emit a native runtime READY marker.")
    matching_identity = [
        marker
        for marker in markers
        if marker.runtime == runtime and marker.rmw == rmw and (token is None or marker.token == token)
    ]
    if not matching_identity:
        raise AcceptanceFailure("READY_MISMATCH", "Unity READY marker did not match the requested runtime, RMW, and optional token identity.")
    excluded = frozenset(excluded_tokens)
    for marker in reversed(matching_identity):
        if marker.token not in excluded:
            return marker
    raise AcceptanceFailure("READY_STALE", "Unity READY marker was already present before this local acceptance run.")


__all__ = [name for name in globals() if not name.startswith("__")]
