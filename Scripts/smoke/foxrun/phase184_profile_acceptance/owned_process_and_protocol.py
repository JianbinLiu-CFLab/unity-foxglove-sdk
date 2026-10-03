from __future__ import annotations
from .process_lifecycle import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


class OwnedProcessSet:
    """Single owner for named children and their retained exit evidence."""

    def __init__(self, job: WindowsKillOnCloseJob | None) -> None:
        """Initialize the owned process set."""

        self._job = job
        self._processes: dict[str, Any] = {}
        self._exit_codes: dict[str, int] = {}
        self._owner_stopped_roles: set[str] = set()
        self._closed = False

    def register(self, role: str, process):
        """Register one owned process."""

        try:
            if self._closed or role in self._processes:
                raise AcceptanceFailure(
                    "FAIL_PREFLIGHT",
                    "Duplicate or late process registration.",
                )
            if self._job is not None:
                self._job.assign(process)
            self._processes[role] = process
            return process
        except BaseException:
            terminate_owned_process(process)
            raise

    def process(self, role: str):
        """Return one registered owned process."""

        return self._processes.get(role)

    def stop(self, role: str) -> int:
        """Stop one registered child while retaining exact owner evidence."""

        if self._closed:
            raise AcceptanceFailure(
                "FAIL_CLEANUP",
                "A closed process owner cannot stop another child.",
            )
        process = self._processes.get(role)
        if process is None:
            raise AcceptanceFailure(
                "FAIL_CLEANUP",
                "The requested process role is not owned.",
            )
        if process.poll() is None:
            self._owner_stopped_roles.add(role)
        exit_code = terminate_owned_process(process)
        self._exit_codes[role] = exit_code
        return exit_code

    def close(self) -> None:
        """Close all resources owned by this helper."""

        if self._closed:
            return
        self._closed = True
        for role, process in reversed(tuple(self._processes.items())):
            if process.poll() is None:
                self._owner_stopped_roles.add(role)
            self._exit_codes[role] = terminate_owned_process(process)
        if self._job is not None:
            self._job.close()

    def exit_codes(self) -> dict[str, int]:
        """Handle the exit codes step."""

        result = dict(self._exit_codes)
        for role, process in self._processes.items():
            exit_code = process.poll()
            if exit_code is not None:
                result[role] = int(exit_code)
        return result

    def all_stopped(self) -> bool:
        """Return cleanup state without exposing the owner's process registry."""

        return all(process.poll() is not None for process in self._processes.values())

    def owner_stopped_roles(self) -> frozenset[str]:
        """Return actors whose termination was initiated by this exact owner."""

        return frozenset(self._owner_stopped_roles)

    def __enter__(self):
        """Enter the owned process set context."""

        return self

    def __exit__(self, _type, _value, _traceback):
        """Exit the owned process set context without suppressing failures."""

        self.close()


def encode_u2r2_frame(header: Mapping[str, object], payload: bytes) -> bytes:
    """Encode one bounded U2R2 v1 frame."""

    header_bytes = json.dumps(
        dict(header),
        sort_keys=True,
        separators=(",", ":"),
        ensure_ascii=True,
    ).encode("utf-8")
    payload = bytes(payload)
    if not 0 < len(header_bytes) <= MAX_FRAME_HEADER_BYTES:
        raise AcceptanceFailure("FAIL_BRIDGE", "U2R2 header length is invalid.")
    if len(payload) > MAX_FRAME_PAYLOAD_BYTES:
        raise AcceptanceFailure("FAIL_BRIDGE", "U2R2 payload length is invalid.")
    return (
        U2R2_MAGIC
        + struct.pack("<HHII", U2R2_VERSION, 0, len(header_bytes), len(payload))
        + header_bytes
        + payload
    )


def decode_u2r2_frame(frame: bytes) -> tuple[dict[str, object], bytes]:
    """Decode one complete bounded U2R2 v1 frame."""

    data = bytes(frame)
    if len(data) < 16 or data[:4] != U2R2_MAGIC:
        raise AcceptanceFailure("FAIL_BRIDGE", "U2R2 response magic or length is invalid.")
    version, flags, header_size, payload_size = struct.unpack("<HHII", data[4:16])
    if version != U2R2_VERSION or flags != 0:
        raise AcceptanceFailure("FAIL_BRIDGE", "U2R2 response version or flags are invalid.")
    if (
        header_size <= 0
        or header_size > MAX_FRAME_HEADER_BYTES
        or payload_size > MAX_FRAME_PAYLOAD_BYTES
        or len(data) != 16 + header_size + payload_size
    ):
        raise AcceptanceFailure("FAIL_BRIDGE", "U2R2 response lengths are invalid.")
    try:
        header = json.loads(data[16 : 16 + header_size].decode("utf-8"))
    except (UnicodeError, json.JSONDecodeError) as exc:
        raise AcceptanceFailure("FAIL_BRIDGE", "U2R2 response JSON is invalid.") from exc
    if not isinstance(header, dict):
        raise AcceptanceFailure("FAIL_BRIDGE", "U2R2 response header must be an object.")
    return header, data[16 + header_size :]


def build_u2r2_health_frame(request_id: str) -> bytes:
    """Build one correlated Bridge health request."""

    if not _SAFE_MARKER_FIELD.fullmatch(request_id):
        raise AcceptanceFailure("FAIL_BRIDGE", "Bridge health request id is unsafe.")
    return encode_u2r2_frame(
        {"op": "health_ping", "requestId": request_id, "protocolVersion": 1},
        b"",
    )


def validate_bridge_health_response(
    header: Mapping[str, object],
    payload: bytes,
    request_id: str,
) -> None:
    """Require the exact sidecar identity and current health correlation."""

    expected = {
        "op": "health_pong",
        "requestId": request_id,
        "protocolVersion": 1,
        "status": "ok",
        "sidecarName": "unity2foxglove_ros2_bridge",
        "sidecarVersion": "0.1.0",
    }
    if dict(header) != expected or payload:
        raise AcceptanceFailure(
            "FAIL_BRIDGE",
            "Bridge health response was stale, incomplete, or from another sidecar.",
        )


def _parse_marker_fields(line: str) -> dict[str, str]:
    """Parse marker fields."""

    fields: dict[str, str] = {}
    for part in line.split()[1:]:
        if "=" not in part:
            continue
        key, value = part.split("=", 1)
        fields[key] = value
    return fields


def find_terminal_marker(
    lines: Iterable[str],
    case: str,
    token: str,
) -> TerminalMarker | None:
    """Find only the exact current case/token terminal marker."""

    result: TerminalMarker | None = None
    for raw in lines:
        line = raw.strip()
        if not (
            line.startswith("PHASE184G_CASE_PASS ")
            or line.startswith("PHASE184G_CASE_FAIL ")
        ):
            continue
        fields = _parse_marker_fields(line)
        if fields.get("case") != case or fields.get("token") != token:
            continue
        verdict = "PASS" if line.startswith("PHASE184G_CASE_PASS ") else "FAIL"
        result = TerminalMarker(verdict, line[:2048], fields)
    return result


def _correlated_runtime_markers(
    config: Mapping[str, object],
    marker: str,
) -> list[TerminalMarker]:
    """Return current case/token runtime markers from the dedicated Unity log."""

    case = str(config["case"])
    token = str(config["token"])
    prefix = marker + " "
    result: list[TerminalMarker] = []
    for raw in read_log_lines(pathlib.Path(str(config["unityLog"]))):
        line = raw.strip()
        if not line.startswith(prefix):
            continue
        fields = _parse_marker_fields(line)
        if fields.get("case") == case and fields.get("token") == token:
            result.append(TerminalMarker(marker, line[:2048], fields))
    return result


def _require_latest_runtime_marker(
    config: Mapping[str, object],
    marker: str,
) -> TerminalMarker:
    """Require the latest correlated runtime marker."""

    markers = _correlated_runtime_markers(config, marker)
    if not markers:
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            f"Unity emitted no correlated {marker} evidence.",
        )
    return markers[-1]


def _runtime_marker_int(marker: TerminalMarker, name: str) -> int:
    """Parse one non-negative runtime marker counter."""

    value = marker.fields.get(name)
    try:
        parsed = int(value) if value is not None else -1
    except ValueError as exc:
        raise AcceptanceFailure(
            "FAIL_FANOUT",
            f"Unity runtime field {name!r} is not an integer.",
        ) from exc
    if parsed < 0:
        raise AcceptanceFailure(
            "FAIL_FANOUT",
            f"Unity runtime field {name!r} is missing or negative.",
        )
    return parsed


def _split_runtime_endpoints(value: str | None) -> list[str]:
    """Parse the stable endpoint list emitted by the Unity harness."""

    if value == "None":
        return []
    endpoints = [] if value is None else value.split(",")
    allowed = {"Foxglove", "Ros2Native", "Ros2Bridge"}
    if (
        not endpoints
        or len(endpoints) != len(set(endpoints))
        or any(endpoint not in allowed for endpoint in endpoints)
    ):
        raise AcceptanceFailure(
            "FAIL_FANOUT",
            "Unity runtime target evidence contains an invalid endpoint list.",
        )
    return endpoints


def _endpoint_state_key(endpoint: str) -> str:
    """Map one runtime endpoint name to the summary schema key."""

    return {
        "Foxglove": "foxglove",
        "Ros2Native": "ros2Native",
        "Ros2Bridge": "ros2Bridge",
    }[endpoint]


def _observed_profile_evidence(
    config: Mapping[str, object],
) -> dict[str, object]:
    """Consume Source/Targets/Encoding resolved by the active Unity route."""

    markers = _correlated_runtime_markers(config, "PHASE184G_PROFILE_EVIDENCE")
    if not markers:
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            "Unity emitted no correlated runtime profile evidence.",
        )
    observed = {
        (
            marker.fields.get("source"),
            marker.fields.get("targets"),
            marker.fields.get("publishEncoding"),
            marker.fields.get("subscribeEncoding"),
        )
        for marker in markers
    }
    if len(observed) != 1:
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            "Unity emitted conflicting runtime profile evidence.",
        )
    source, raw_targets, publish_encoding, subscribe_encoding = observed.pop()
    if source not in {"None", "Foxglove", "Ros2Native", "Ros2Bridge"}:
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            "Unity runtime profile source is invalid.",
        )
    if publish_encoding not in {"protobuf", "json", "protobuf,json", "not_applicable"}:
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            "Unity runtime publish encoding evidence is invalid.",
        )
    if subscribe_encoding not in {
        "protobuf",
        "json",
        "protobuf,json",
        "not_applicable",
    }:
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            "Unity runtime subscribe encoding evidence is invalid.",
        )
    return {
        "source": source,
        "targets": _split_runtime_endpoints(raw_targets),
        "publishEncoding": publish_encoding,
        "subscribeEncoding": subscribe_encoding,
    }


__all__ = [name for name in globals() if not name.startswith("__")]
