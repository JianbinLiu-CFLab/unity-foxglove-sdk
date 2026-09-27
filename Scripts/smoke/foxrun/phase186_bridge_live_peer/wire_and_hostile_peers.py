from __future__ import annotations
from .graph_observer_runtime import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_live_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
async def _run_foxglove_async(config: Mapping[str, Any]) -> Mapping[str, Any]:
    """Run foxglove async."""
    try:
        import websockets
    except ImportError as exc:
        raise LiveActorFailure("FAIL_CLIENT", "Python websockets is unavailable") from exc
    _write_actor_document(
        config,
        "foxglove-client",
        "ready",
        {"state": "loopback-client-ready", "port": config["foxglovePort"]},
    )
    await asyncio.to_thread(_wait_for_unity_ready, config)
    url = f"ws://{config['foxgloveHost']}:{config['foxglovePort']}"
    deadline = time.monotonic() + LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS
    websocket = None
    while websocket is None:
        try:
            websocket = await websockets.connect(url, subprotocols=[FOXGLOVE_SUBPROTOCOL])
        except OSError:
            if time.monotonic() >= deadline:
                raise LiveActorFailure("FAIL_CLIENT", "Foxglove listener did not become ready")
            await asyncio.sleep(0.1)
    try:
        expected = set(str(topic) for topic in config["topics"])
        channels: dict[str, dict[str, Any]] = {}
        while set(channels) != expected and time.monotonic() < deadline:
            frame = await asyncio.wait_for(websocket.recv(), timeout=5.0)
            if not isinstance(frame, str):
                continue
            try:
                value = json.loads(frame)
            except json.JSONDecodeError:
                continue
            if value.get("op") != "advertise":
                continue
            for item in value.get("channels", []):
                if isinstance(item, Mapping) and item.get("topic") in expected:
                    channels[str(item["topic"])] = dict(item)
        if set(channels) != expected:
            raise LiveActorFailure("FAIL_CLIENT", "Foxglove channel set is incomplete")
        subscriptions = []
        ids: dict[int, str] = {}
        for index, topic in enumerate(sorted(channels), start=1):
            subscription_id = 186000 + index
            ids[subscription_id] = topic
            subscriptions.append(
                {"id": subscription_id, "channelId": int(channels[topic]["id"])}
            )
        await websocket.send(
            json.dumps({"op": "subscribe", "subscriptions": subscriptions}, separators=(",", ":"))
        )
        delivered: dict[str, int] = {topic: 0 for topic in expected}
        required_count = 2 if config["caseId"] == "fanout-fairness-health" else 1
        while (
            any(count < required_count for count in delivered.values())
            and time.monotonic() < deadline
        ):
            frame = await asyncio.wait_for(websocket.recv(), timeout=5.0)
            if not isinstance(frame, bytes) or len(frame) < 13 or frame[0] != FOXGLOVE_MESSAGE_OPCODE:
                continue
            topic = ids.get(struct.unpack_from("<I", frame, 1)[0])
            if topic is not None and frame[13:]:
                delivered[topic] += 1
        if any(count < required_count for count in delivered.values()):
            raise LiveActorFailure("FAIL_CLIENT", "Foxglove did not deliver every fanout topic")
        return {
            "url": url,
            "deliveredTopics": sorted(delivered),
            "messageCounts": dict(sorted(delivered.items())),
            "channels": {
                topic: {
                    "encoding": str(value.get("encoding", "")),
                    "schemaName": str(value.get("schemaName", "")),
                }
                for topic, value in channels.items()
            },
        }
    finally:
        await websocket.close()


def _read_frame(connection: socket.socket, initial: bytes = b"") -> bytes:
    """Read frame."""
    fixed = bytearray(initial)
    if len(fixed) > 16:
        raise LiveActorFailure("FAIL_BRIDGE", "sidecar response prefix is oversized")
    while len(fixed) < 16:
        chunk = connection.recv(16 - len(fixed))
        if not chunk:
            raise LiveActorFailure("FAIL_BRIDGE", "sidecar closed during frame")
        fixed.extend(chunk)
    if fixed[:4] != b"U2R2":
        raise LiveActorFailure("FAIL_BRIDGE", "sidecar response magic differs")
    version, flags, header_size, payload_size = struct.unpack("<HHII", fixed[4:16])
    if (
        version != 1
        or flags != 0
        or not 0 < header_size <= MAX_FRAME_HEADER_BYTES
        or payload_size > MAX_FRAME_PAYLOAD_BYTES
    ):
        raise LiveActorFailure("FAIL_BRIDGE", "sidecar response header differs")
    total = 16 + header_size + payload_size
    while len(fixed) < total:
        chunk = connection.recv(total - len(fixed))
        if not chunk:
            raise LiveActorFailure("FAIL_BRIDGE", "sidecar response is truncated")
        fixed.extend(chunk)
    return bytes(fixed)


def _read_optional_frame(connection: socket.socket) -> bytes | None:
    """Read one optional response without discarding a fragmented prefix."""

    try:
        first = connection.recv(1)
    except socket.timeout as exc:
        raise LiveActorFailure(
            "FAIL_BRIDGE", "hostile connection did not reject within the bound"
        ) from exc
    except OSError:
        return None
    if not first:
        return None
    return _read_frame(connection, first)


def _decode_frame(frame: bytes) -> tuple[Mapping[str, Any], bytes]:
    """Handle decode frame for Phase186 acceptance."""
    if len(frame) < 16 or frame[:4] != b"U2R2":
        raise LiveActorFailure("FAIL_BRIDGE", "sidecar response framing differs")
    _version, _flags, header_size, payload_size = struct.unpack("<HHII", frame[4:16])
    if len(frame) != 16 + header_size + payload_size:
        raise LiveActorFailure("FAIL_BRIDGE", "sidecar response length differs")
    try:
        header = json.loads(frame[16 : 16 + header_size].decode("utf-8"))
    except (UnicodeError, json.JSONDecodeError) as exc:
        raise LiveActorFailure("FAIL_BRIDGE", "sidecar response JSON differs") from exc
    if not isinstance(header, Mapping):
        raise LiveActorFailure("FAIL_BRIDGE", "sidecar response is not an object")
    return header, frame[16 + header_size : 16 + header_size + payload_size]


def _encode_frame(header: Mapping[str, Any], payload: bytes = b"") -> bytes:
    """Handle encode frame for Phase186 acceptance."""
    encoded = json.dumps(
        dict(header), separators=(",", ":"), ensure_ascii=True
    ).encode("ascii")
    return b"U2R2" + struct.pack("<HHII", 1, 0, len(encoded), len(payload)) + encoded + payload


def _fixture(config: Mapping[str, Any]) -> Mapping[str, Any]:
    """Handle fixture for Phase186 acceptance."""
    path = (
        pathlib.Path(str(config["repository"]))
        / "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/fixtures/u2r2_protocol_vectors.json"
    )
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, Mapping):
        raise LiveActorFailure("FAIL_EVIDENCE", "U2R2 fixture is malformed")
    return value


def _health(config: Mapping[str, Any], request_id: str) -> Mapping[str, Any]:
    """Handle health for Phase186 acceptance."""
    header = {"op": "health_ping", "requestId": request_id, "protocolVersion": 1}
    frame = _encode_frame(header)
    with socket.create_connection(
        (str(config["bridgeHost"]), int(config["bridgePort"])), timeout=2.0
    ) as connection:
        connection.settimeout(3.0)
        connection.sendall(frame)
        response, payload = _decode_frame(_read_frame(connection))
    if (
        response.get("op") != "health_pong"
        or response.get("requestId") != request_id
        or response.get("status") != "ok"
        or payload
    ):
        raise LiveActorFailure("FAIL_BRIDGE", "health response is uncorrelated")
    return response


def run_wire_peer(config: Mapping[str, Any]) -> Mapping[str, Any]:
    """Run wire peer."""
    fixture = _fixture(config)
    _write_actor_document(config, "wire-peer", "ready", {"state": "fixture-loaded"})
    health = fixture["health"]
    request = bytes.fromhex(health["request"]["frameHex"])
    with socket.create_connection(
        (str(config["bridgeHost"]), int(config["bridgePort"])), timeout=5.0
    ) as connection:
        connection.settimeout(5.0)
        connection.sendall(request)
        health_response, payload = _decode_frame(_read_frame(connection))
    if health_response != health["response"]["header"] or payload:
        raise LiveActorFailure("FAIL_BRIDGE", "frozen v1 health vector drifted")
    prepare = fixture["preparePublisher"]
    publish = fixture["publish"]
    with socket.create_connection(
        (str(config["bridgeHost"]), int(config["bridgePort"])), timeout=5.0
    ) as connection:
        connection.settimeout(5.0)
        connection.sendall(bytes.fromhex(prepare["request"]["frameHex"]))
        response, payload = _decode_frame(_read_frame(connection))
        if response != prepare["response"]["header"] or payload:
            raise LiveActorFailure("FAIL_BRIDGE", "frozen v1 prepare vector drifted")
        connection.sendall(bytes.fromhex(publish["frame"]["frameHex"]))
        time.sleep(0.25)
    return {
        "health": health_response.get("status") == "ok",
        "publishTopic": publish["topic"],
        "publishSequence": publish["sequence"],
        "fixtureVersion": fixture["fixtureVersion"],
    }


def _expect_rejection(config: Mapping[str, Any], frame: bytes) -> None:
    """Handle expect rejection for Phase186 acceptance."""
    with socket.create_connection(
        (str(config["bridgeHost"]), int(config["bridgePort"])), timeout=2.0
    ) as connection:
        connection.settimeout(2.0)
        connection.sendall(frame)
        with contextlib.suppress(OSError):
            connection.shutdown(socket.SHUT_WR)
        response = _read_optional_frame(connection)
        if response is not None:
            header, _payload = _decode_frame(response)
            if header.get("status") != "error":
                raise LiveActorFailure("FAIL_BRIDGE", "hostile frame was accepted")


def _hostile_mutations() -> Mapping[str, bytes]:
    """Handle hostile mutations for Phase186 acceptance."""
    unknown = _encode_frame({"op": "phase186_unknown_op"})
    return {
        "bad-magic": b"X2R2" + struct.pack("<HHII", 1, 0, 2, 0) + b"{}",
        "bad-version": b"U2R2" + struct.pack("<HHII", 2, 0, 2, 0) + b"{}",
        "oversized-header": b"U2R2" + struct.pack("<HHII", 1, 0, 65_537, 0),
        "oversized-payload": b"U2R2" + struct.pack("<HHII", 1, 0, 2, 67_108_865) + b"{}",
        "invalid-utf8": b"U2R2" + struct.pack("<HHII", 1, 0, 1, 0) + b"\xff",
        "trailing-root": b"U2R2" + struct.pack("<HHII", 1, 0, 4, 0) + b"{}{}",
        "unknown-op": unknown,
        "truncated-fixed": b"U2R2" + b"\x01" * 10,
    }


def _is_busy_response(header: Mapping[str, Any]) -> bool:
    """Return whether busy response."""
    return (
        header.get("op") == "busy"
        and header.get("status") == "error"
        and header.get("errorCode") == "busy"
        and header.get("terminal") is True
    )


def run_hostile_peer(config: Mapping[str, Any]) -> Mapping[str, Any]:
    """Run hostile peer."""
    _write_actor_document(
        config,
        "hostile-peer",
        "ready",
        {"state": "hostile-peer-ready"},
    )
    _wait_for_unity_ready(config)
    mutations = _hostile_mutations()
    passed: list[str] = []
    for index, (name, frame) in enumerate(mutations.items(), start=1):
        _expect_rejection(config, frame)
        _health(config, f"{config['token']}-hostile-{index}")
        passed.append(name)
    hello = next(
        item for item in _fixture(config)["v2"]["operations"] if item["id"] == "hello_request"
    )
    with socket.create_connection(
        (str(config["bridgeHost"]), int(config["bridgePort"])), timeout=3.0
    ) as connection:
        connection.settimeout(3.0)
        connection.sendall(bytes.fromhex(hello["frameHex"]))
        busy, _payload = _decode_frame(_read_frame(connection))
    if not _is_busy_response(busy):
        raise LiveActorFailure("FAIL_BRIDGE", "second data client did not receive busy")
    _health(config, str(config["token"]) + "-post-busy")
    return {
        "rejectedFamilies": passed,
        "secondClientCode": "busy",
        "healthAfterHostile": True,
    }




__all__ = [name for name in globals() if not name.startswith("__")]
