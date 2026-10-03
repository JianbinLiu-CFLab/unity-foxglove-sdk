from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase189_component_messagepack_probe.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
async def _wait_for_evidence(
    websocket: Any,
    sequence: int,
    value: int,
    timeout_seconds: float,
    *,
    reject_output_payload: bytes,
) -> None:
    """Wait for typed Unity apply evidence while rejecting input mirroring."""
    deadline = time.perf_counter() + timeout_seconds
    while time.perf_counter() < deadline:
        frame = await _receive_until(websocket, deadline)
        message = _decode_message_frame(frame)
        if message is None:
            continue
        subscription_id, payload = message
        if subscription_id == OUTPUT_SUBSCRIPTION_ID:
            if payload == reject_output_payload:
                raise ProbeFailure(
                    "Observed a same-topic output mirroring the remote input."
                )
            raise ProbeFailure(
                "Observed unexpected same-topic output before Unity apply evidence."
            )
        if subscription_id != EVIDENCE_SUBSCRIPTION_ID:
            continue
        evidence = _decode_json_object(payload, "Unity apply evidence")
        if (
            evidence.get("messagePackAppliedSequence") == sequence
            and evidence.get("messagePackAppliedValue") == value
        ):
            return
    raise ProbeFailure(
        f"Timed out waiting for typed Unity apply evidence sequence={sequence} value={value}."
    )
async def _require_no_output(
    websocket: Any,
    seconds: float,
    label: str,
) -> None:
    """Require a quiet same-topic output window."""
    deadline = time.perf_counter() + seconds
    while time.perf_counter() < deadline:
        frame = await _receive_optional(websocket, deadline)
        if frame is None:
            return
        message = _decode_message_frame(frame)
        if message is not None and message[0] == OUTPUT_SUBSCRIPTION_ID:
            decoded = _safe_decode(message[1])
            raise ProbeFailure(f"{label} observed same-topic output: {decoded!r}.")
async def _wait_for_canonical_output_b(
    websocket: Any,
    timeout_seconds: float,
) -> bytes:
    """Wait for and return the exact later-local canonical payload B."""
    deadline = time.perf_counter() + timeout_seconds
    expected = {
        "messagePackSequence": LOCAL_SEQUENCE_B,
        "messagePackValue": LOCAL_VALUE_B,
    }
    while time.perf_counter() < deadline:
        frame = await _receive_until(websocket, deadline)
        message = _decode_message_frame(frame)
        if message is None or message[0] != OUTPUT_SUBSCRIPTION_ID:
            continue
        payload = message[1]
        decoded = decode_complete_msgpack(payload)
        if decoded == {
            "messagePackSequence": REMOTE_SEQUENCE_A,
            "messagePackValue": REMOTE_VALUE_A,
        }:
            raise ProbeFailure("Inbound A was emitted as a same-topic mirror.")
        if decoded != expected:
            raise ProbeFailure(
                f"Expected canonical later-local B, got {decoded!r}."
            )
        return payload
    raise ProbeFailure("Timed out waiting for explicit later-local mutation B.")
async def _require_no_probe_activity(
    websocket: Any,
    seconds: float,
    label: str,
) -> None:
    """Require no controlled output or evidence activity for a bounded window."""
    deadline = time.perf_counter() + seconds
    while time.perf_counter() < deadline:
        frame = await _receive_optional(websocket, deadline)
        if frame is None:
            return
        message = _decode_message_frame(frame)
        if message is None:
            continue
        if message[0] in (OUTPUT_SUBSCRIPTION_ID, EVIDENCE_SUBSCRIPTION_ID):
            raise ProbeFailure(
                f"{label} unexpectedly changed controlled probe state."
            )
async def _drain(websocket: Any, seconds: float) -> None:
    """Drain pending protocol frames for a bounded startup interval."""
    deadline = time.perf_counter() + seconds
    while time.perf_counter() < deadline:
        if await _receive_optional(websocket, deadline) is None:
            return
async def _receive_until(websocket: Any, deadline: float) -> Any:
    """Receive one frame before an absolute monotonic deadline."""
    remaining = deadline - time.perf_counter()
    if remaining <= 0:
        raise ProbeFailure("Timed out waiting for Foxglove protocol evidence.")
    try:
        return await asyncio.wait_for(websocket.recv(), timeout=remaining)
    except asyncio.TimeoutError as exc:
        raise ProbeFailure("Timed out waiting for Foxglove protocol evidence.") from exc
async def _receive_optional(websocket: Any, deadline: float) -> Any | None:
    """Receive one frame before a deadline or return None on timeout."""
    remaining = deadline - time.perf_counter()
    if remaining <= 0:
        return None
    try:
        return await asyncio.wait_for(websocket.recv(), timeout=remaining)
    except asyncio.TimeoutError:
        return None
def _validate_output_channel(channel: ChannelInfo) -> None:
    """Validate the controlled live MessagePack output advertisement."""
    if channel.encoding != EXPECTED_ENCODING:
        raise ProbeFailure(
            f"Controlled output encoding must be msgpack, got {channel.encoding!r}."
        )
    if channel.schema_name or channel.schema_encoding or channel.schema:
        raise ProbeFailure("Controlled live MessagePack output must be schemaless.")
def _validate_evidence_channel(channel: ChannelInfo) -> None:
    """Validate the controlled JSON evidence advertisement."""
    if channel.encoding != "json":
        raise ProbeFailure("Unity apply evidence channel must use JSON.")
def _build_client_advertise(topic: str, channel_id: int) -> str:
    """Build a schemaless MessagePack client advertisement."""
    return json.dumps(
        {
            "op": "advertise",
            "channels": [
                {
                    "id": channel_id,
                    "topic": topic,
                    "encoding": EXPECTED_ENCODING,
                }
            ],
        },
        separators=(",", ":"),
    )
def _build_client_message(channel_id: int, payload: bytes) -> bytes:
    """Build a Foxglove client MessageData frame."""
    if not payload:
        raise ValueError("Client MessageData payload must not be empty.")
    return (
        bytes([CLIENT_MESSAGE_DATA_OPCODE])
        + struct.pack("<I", channel_id)
        + payload
    )
def _build_service_call(service_id: int, call_id: int, payload: bytes) -> bytes:
    """Build a JSON Foxglove client service-call frame."""
    encoding = b"json"
    return (
        bytes([CLIENT_SERVICE_CALL_OPCODE])
        + struct.pack("<III", service_id, call_id, len(encoding))
        + encoding
        + payload
    )
def _decode_service_response(
    frame: object,
) -> tuple[int, int, str, bytes] | None:
    """Decode a Foxglove service response when the frame matches."""
    if not isinstance(frame, bytes) or len(frame) < 13:
        return None
    if frame[0] != SERVER_SERVICE_RESPONSE_OPCODE:
        return None
    service_id, call_id, encoding_length = struct.unpack_from("<III", frame, 1)
    payload_offset = 13 + encoding_length
    if payload_offset > len(frame):
        raise ProbeFailure("Service response encoding length exceeds its frame.")
    try:
        encoding = frame[13:payload_offset].decode("utf-8")
    except UnicodeDecodeError as exc:
        raise ProbeFailure("Service response encoding is not UTF-8.") from exc
    return service_id, call_id, encoding, frame[payload_offset:]
def _decode_message_frame(frame: object) -> tuple[int, bytes] | None:
    """Decode a Foxglove server MessageData frame when present."""
    if not isinstance(frame, bytes) or len(frame) < MESSAGE_PAYLOAD_START:
        return None
    if frame[0] != SERVER_MESSAGE_DATA_OPCODE:
        return None
    subscription_id = struct.unpack_from("<I", frame, 1)[0]
    return subscription_id, frame[MESSAGE_PAYLOAD_START:]
def _decode_json_object(payload: bytes, label: str) -> dict[str, Any]:
    """Decode a required UTF-8 JSON object with a diagnostic label."""
    try:
        value = json.loads(payload.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ProbeFailure(f"{label} is not valid UTF-8 JSON.") from exc
    if not isinstance(value, dict):
        raise ProbeFailure(f"{label} is not a JSON object.")
    return value
def _malformed_payloads(valid_a: bytes) -> tuple[bytes, ...]:
    """Build duplicate-key, wrong-type, and truncated MessagePack cases."""
    key_sequence = _encode_string("messagePackSequence")
    key_value = _encode_string("messagePackValue")
    duplicate = (
        b"\x83"
        + key_sequence
        + _encode_integer(REMOTE_SEQUENCE_A)
        + key_sequence
        + _encode_integer(REMOTE_SEQUENCE_A + 1)
        + key_value
        + _encode_integer(REMOTE_VALUE_A)
    )
    wrong_type = (
        b"\x82"
        + key_sequence
        + _encode_string("not-an-int")
        + key_value
        + _encode_integer(REMOTE_VALUE_A)
    )
    truncated = valid_a[:-1]
    return duplicate, wrong_type, truncated
def _encode_string(value: str) -> bytes:
    """Encode a bounded UTF-8 MessagePack string."""
    encoded = value.encode("utf-8")
    if len(encoded) <= 31:
        return bytes([0xA0 | len(encoded)]) + encoded
    if len(encoded) <= 0xFF:
        return b"\xD9" + bytes([len(encoded)]) + encoded
    raise ValueError("Probe string exceeds its bounded encoder.")
def _encode_integer(value: int) -> bytes:
    """Encode one signed or unsigned 64-bit MessagePack integer."""
    if value >= 0:
        if value <= 0x7F:
            return bytes([value])
        if value <= 0xFF:
            return b"\xCC" + bytes([value])
        if value <= 0xFFFF:
            return b"\xCD" + struct.pack(">H", value)
        if value <= 0xFFFFFFFF:
            return b"\xCE" + struct.pack(">I", value)
        if value <= 0xFFFFFFFFFFFFFFFF:
            return b"\xCF" + struct.pack(">Q", value)
    else:
        if value >= -32:
            return bytes([256 + value])
        if value >= -128:
            return b"\xD0" + struct.pack(">b", value)
        if value >= -32768:
            return b"\xD1" + struct.pack(">h", value)
        if value >= -2147483648:
            return b"\xD2" + struct.pack(">i", value)
        if value >= -9223372036854775808:
            return b"\xD3" + struct.pack(">q", value)
    raise ValueError("Probe integer is outside the MessagePack 64-bit range.")
def _decode_msgpack_value(
    data: bytes,
    offset: int,
    depth: int,
) -> tuple[int, object]:
    """Decode one bounded MessagePack value from the supplied offset."""
    if depth > 16:
        raise ProbeFailure("MessagePack probe payload exceeds decoder depth.")
    marker, offset = _read_marker(data, offset)
    if marker <= 0x7F:
        return offset, marker
    if marker >= 0xE0:
        return offset, marker - 256
    if 0x80 <= marker <= 0x8F:
        return _decode_map(data, offset, marker & 0x0F, depth + 1)
    if 0x90 <= marker <= 0x9F:
        return _decode_array(data, offset, marker & 0x0F, depth + 1)
    if 0xA0 <= marker <= 0xBF:
        return _decode_string(data, offset, marker & 0x1F)
    if marker == 0xC0:
        return offset, None
    if marker == 0xC2:
        return offset, False
    if marker == 0xC3:
        return offset, True
    if marker == 0xCC:
        return _read_unsigned(data, offset, 1)
    if marker == 0xCD:
        return _read_unsigned(data, offset, 2)
    if marker == 0xCE:
        return _read_unsigned(data, offset, 4)
    if marker == 0xCF:
        return _read_unsigned(data, offset, 8)
    if marker == 0xD0:
        return _read_signed(data, offset, 1)
    if marker == 0xD1:
        return _read_signed(data, offset, 2)
    if marker == 0xD2:
        return _read_signed(data, offset, 4)
    if marker == 0xD3:
        return _read_signed(data, offset, 8)
    if marker == 0xD9:
        offset, length = _read_unsigned(data, offset, 1)
        return _decode_string(data, offset, length)
    if marker == 0xDA:
        offset, length = _read_unsigned(data, offset, 2)
        return _decode_string(data, offset, length)
    if marker == 0xDE:
        offset, count = _read_unsigned(data, offset, 2)
        return _decode_map(data, offset, count, depth + 1)
    if marker == 0xDC:
        offset, count = _read_unsigned(data, offset, 2)
        return _decode_array(data, offset, count, depth + 1)
    raise ProbeFailure(f"Unsupported MessagePack marker 0x{marker:02x}.")
def _decode_map(
    data: bytes,
    offset: int,
    count: int,
    depth: int,
) -> tuple[int, dict[object, object]]:
    """Decode a bounded MessagePack map and reject duplicate keys."""
    if count > 64:
        raise ProbeFailure("MessagePack probe map exceeds item limit.")
    result: dict[object, object] = {}
    for _ in range(count):
        offset, key = _decode_msgpack_value(data, offset, depth)
        if key in result:
            raise ProbeFailure(f"Duplicate MessagePack map key {key!r}.")
        offset, value = _decode_msgpack_value(data, offset, depth)
        result[key] = value
    return offset, result
def _decode_array(
    data: bytes,
    offset: int,
    count: int,
    depth: int,
) -> tuple[int, list[object]]:
    """Decode a bounded MessagePack array."""
    if count > 64:
        raise ProbeFailure("MessagePack probe array exceeds item limit.")
    result = []
    for _ in range(count):
        offset, value = _decode_msgpack_value(data, offset, depth)
        result.append(value)
    return offset, result
def _decode_string(data: bytes, offset: int, length: int) -> tuple[int, str]:
    """Decode an exact-length strict UTF-8 MessagePack string."""
    end = offset + length
    if end > len(data):
        raise ProbeFailure("MessagePack string ended early.")
    try:
        return end, data[offset:end].decode("utf-8")
    except UnicodeDecodeError as exc:
        raise ProbeFailure("MessagePack string is not valid UTF-8.") from exc
def _read_marker(data: bytes, offset: int) -> tuple[int, int]:
    """Read one MessagePack marker byte."""
    if offset >= len(data):
        raise ProbeFailure("MessagePack payload ended early.")
    return data[offset], offset + 1
def _read_unsigned(data: bytes, offset: int, width: int) -> tuple[int, int]:
    """Read one big-endian unsigned MessagePack integer payload."""
    end = offset + width
    if end > len(data):
        raise ProbeFailure("MessagePack integer ended early.")
    return end, int.from_bytes(data[offset:end], "big", signed=False)
def _read_signed(data: bytes, offset: int, width: int) -> tuple[int, int]:
    """Read one big-endian signed MessagePack integer payload."""
    end = offset + width
    if end > len(data):
        raise ProbeFailure("MessagePack integer ended early.")
    return end, int.from_bytes(data[offset:end], "big", signed=True)
def _safe_decode(payload: bytes) -> object:
    """Decode diagnostics best-effort without masking the original bytes."""
    try:
        return decode_complete_msgpack(payload)
    except ProbeFailure:
        return payload.hex()
def _build_url(args: argparse.Namespace) -> str:
    """Build the target WebSocket URL and optional token query."""
    url = args.url or f"ws://{args.host}:{args.port}"
    if args.token:
        parts = urlsplit(url)
        query = dict(parse_qsl(parts.query, keep_blank_values=True))
        query["token"] = args.token
        url = urlunsplit(
            (
                parts.scheme,
                parts.netloc,
                parts.path,
                urlencode(query),
                parts.fragment,
            )
        )
    return url
def _redacted_url(url: str) -> str:
    """Redact authentication tokens from a reportable URL."""
    parts = urlsplit(url)
    query = [
        (key, "REDACTED" if key == "token" else value)
        for key, value in parse_qsl(parts.query, keep_blank_values=True)
    ]
    return urlunsplit(
        (parts.scheme, parts.netloc, parts.path, urlencode(query), parts.fragment)
    )
def _build_ssl_context(url: str, insecure: bool) -> ssl.SSLContext | None:
    """Build the optional TLS context for a secure WebSocket URL."""
    if not url.lower().startswith("wss://"):
        return None
    context = ssl.create_default_context()
    if insecure:
        context.check_hostname = False
        context.verify_mode = ssl.CERT_NONE
    return context
def _positive_seconds(value: str) -> float:
    """Parse a strictly positive duration for an observation window."""
    try:
        seconds = float(value)
    except ValueError as exc:
        raise argparse.ArgumentTypeError("duration must be a finite positive number") from exc
    if not math.isfinite(seconds) or seconds <= 0:
        raise argparse.ArgumentTypeError("duration must be a finite positive number")
    return seconds


__all__ = [name for name in globals() if not name.startswith("__")]
