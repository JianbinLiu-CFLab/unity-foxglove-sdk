from __future__ import annotations
from .runtime_marker_evidence import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


@dataclass(frozen=True)
class _FoxgloveChannel:
    """Represent the foxglove channel contract."""

    channel_id: int
    topic: str
    encoding: str
    schema_name: str
    schema_encoding: str
    schema: str


async def _wait_for_foxglove_channels(
    websocket,
    topics: Sequence[str],
    timeout_seconds: float,
) -> dict[str, _FoxgloveChannel]:
    """Require exact current-run channel advertisements."""

    deadline = time.monotonic() + timeout_seconds
    channels: dict[str, _FoxgloveChannel] = {}
    expected = set(topics)
    while time.monotonic() < deadline:
        try:
            frame = await asyncio.wait_for(
                websocket.recv(),
                timeout=max(0.01, deadline - time.monotonic()),
            )
        except asyncio.TimeoutError:
            break
        if not isinstance(frame, str):
            continue
        try:
            message = json.loads(frame)
        except json.JSONDecodeError:
            continue
        if message.get("op") != "advertise":
            continue
        for item in message.get("channels", []):
            if not isinstance(item, Mapping):
                continue
            topic = str(item.get("topic", ""))
            if topic not in expected:
                continue
            channels[topic] = _FoxgloveChannel(
                int(item.get("id", 0)),
                topic,
                str(item.get("encoding", "")).lower(),
                str(item.get("schemaName", "")),
                str(item.get("schemaEncoding", "")).lower(),
                str(item.get("schema", "")),
            )
        if set(channels) == expected:
            return channels
    raise AcceptanceFailure("FAIL_CLIENT", "Foxglove did not advertise every exact case topic.")


def _dynamic_protobuf_class(channel: _FoxgloveChannel):
    """Build a dynamic message class from the advertised FileDescriptorSet."""

    if (
        channel.encoding != "protobuf"
        or channel.schema_encoding != "protobuf"
        or not channel.schema_name
        or not channel.schema
    ):
        raise AcceptanceFailure("FAIL_CLIENT", "Protobuf channel metadata is incomplete.")
    try:
        from google.protobuf import descriptor_pb2, descriptor_pool, message_factory

        descriptor_set = descriptor_pb2.FileDescriptorSet()
        descriptor_set.ParseFromString(base64.b64decode(channel.schema, validate=True))
        pool = descriptor_pool.DescriptorPool()
        pending = list(descriptor_set.file)
        while pending:
            progressed = False
            for item in tuple(pending):
                try:
                    pool.Add(item)
                except Exception:  # dependency may be later in the descriptor set
                    continue
                pending.remove(item)
                progressed = True
            if not progressed:
                raise ValueError("descriptor dependencies could not be resolved")
        descriptor = pool.FindMessageTypeByName(channel.schema_name)
        return message_factory.GetMessageClass(descriptor)
    except Exception as exc:
        raise AcceptanceFailure(
            "FAIL_CLIENT",
            "Advertised Protobuf schema could not create its dynamic message.",
        ) from exc


def _set_dynamic_dto(message, *, token: str, stage: str, count: int) -> None:
    """Populate the generated aggregate/DTO graph by semantic field name."""

    for field in message.DESCRIPTOR.fields:
        normalized = field.name.replace("_", "").lower()
        if field.label == field.LABEL_REPEATED:
            sequence = getattr(message, field.name)
            if field.type == field.TYPE_BYTES:
                setattr(message, field.name, bytes((0x18, 0x04, count & 0xFF)))
            elif field.type in {
                field.TYPE_INT32,
                field.TYPE_SINT32,
                field.TYPE_SFIXED32,
                field.TYPE_UINT32,
                field.TYPE_FIXED32,
                field.TYPE_INT64,
                field.TYPE_SINT64,
                field.TYPE_SFIXED64,
                field.TYPE_UINT64,
                field.TYPE_FIXED64,
            }:
                sequence.extend((count, count + 1, count + 2))
            continue
        if field.type == field.TYPE_MESSAGE:
            _set_dynamic_dto(
                getattr(message, field.name),
                token=token,
                stage=stage,
                count=count,
            )
        elif field.type == field.TYPE_STRING:
            setattr(message, field.name, token + "-" + stage)
        elif field.type == field.TYPE_BOOL:
            setattr(message, field.name, True)
        elif field.type == field.TYPE_ENUM:
            values = field.enum_type.values
            setattr(message, field.name, values[1].number if len(values) > 1 else values[0].number)
        elif field.type in {
            field.TYPE_INT32,
            field.TYPE_SINT32,
            field.TYPE_SFIXED32,
            field.TYPE_UINT32,
            field.TYPE_FIXED32,
            field.TYPE_INT64,
            field.TYPE_SINT64,
            field.TYPE_SFIXED64,
            field.TYPE_UINT64,
            field.TYPE_FIXED64,
        }:
            setattr(message, field.name, count)
        elif normalized.startswith("has"):
            setattr(message, field.name, True)


def _json_dto(token: str, stage: str, count: int) -> dict[str, object]:
    """Handle the JSON DTO step."""

    label = token + "-" + stage
    return {
        "Count": count,
        "Kind": 1,
        "Message": label,
        "Bytes": [0x18, 0x04, count & 0xFF],
        "Values": [count, count + 1, count + 2],
        "Nested": {"Enabled": True, "Label": label},
        "OptionalCount": count,
        "OptionalText": label,
    }


async def _foxglove_subscribe(websocket, channels: Mapping[str, _FoxgloveChannel]):
    """Handle the foxglove subscribe step."""

    subscription_to_topic: dict[int, str] = {}
    subscriptions: list[dict[str, int]] = []
    for index, topic in enumerate(channels, start=1):
        subscription_id = 184000 + index
        subscription_to_topic[subscription_id] = topic
        subscriptions.append(
            {"id": subscription_id, "channelId": channels[topic].channel_id}
        )
    await websocket.send(
        json.dumps(
            {"op": "subscribe", "subscriptions": subscriptions},
            separators=(",", ":"),
        )
    )
    return subscription_to_topic


async def _foxglove_advertise_and_send_json(
    websocket,
    topic: str,
    field_name: str,
    token: str,
    stage: str,
    count: int,
    channel_id: int,
    *,
    advertise: bool,
) -> None:
    """Handle the foxglove advertise and send JSON step."""

    if advertise:
        await websocket.send(
            json.dumps(
                {
                    "op": "advertise",
                    "channels": [{"id": channel_id, "topic": topic, "encoding": "json"}],
                },
                separators=(",", ":"),
            )
        )
    payload = json.dumps(
        {field_name: _json_dto(token, stage, count)},
        separators=(",", ":"),
    ).encode("utf-8")
    await websocket.send(
        bytes((FOXGLOVE_MESSAGE_OPCODE,))
        + struct.pack("<I", channel_id)
        + payload
    )


async def _receive_foxglove_stages(
    websocket,
    subscription_to_topic: Mapping[int, str],
    channels: Mapping[str, _FoxgloveChannel],
    expected: Mapping[str, set[str]],
    forbidden: Mapping[str, set[str]],
    timeout_seconds: float,
    minimum_observation_seconds: float = 0.0,
) -> tuple[dict[str, set[str]], list[str], float]:
    """Observe required stages and retain any forbidden remote republish."""

    observed = {topic: set() for topic in expected}
    forbidden_observed: list[str] = []
    protobuf_classes = {
        topic: _dynamic_protobuf_class(channel)
        for topic, channel in channels.items()
        if channel.encoding == "protobuf"
    }
    started = time.monotonic()
    deadline = started + timeout_seconds
    last_timestamp = 0.0
    while time.monotonic() < deadline:
        complete = all(
            observed[topic] >= stages
            for topic, stages in expected.items()
        )
        elapsed = time.monotonic() - started
        if complete and elapsed >= minimum_observation_seconds:
            return observed, forbidden_observed, last_timestamp
        remaining = deadline - time.monotonic()
        if complete:
            remaining = min(
                remaining,
                max(0.01, minimum_observation_seconds - elapsed),
            )
        try:
            frame = await asyncio.wait_for(
                websocket.recv(),
                timeout=max(0.01, min(0.1, remaining)),
            )
        except asyncio.TimeoutError:
            if (
                all(
                    observed[topic] >= stages
                    for topic, stages in expected.items()
                )
                and time.monotonic() - started >= minimum_observation_seconds
            ):
                return observed, forbidden_observed, last_timestamp
            if time.monotonic() >= deadline:
                break
            continue
        if not isinstance(frame, bytes) or len(frame) < 13 or frame[0] != FOXGLOVE_MESSAGE_OPCODE:
            continue
        subscription_id = struct.unpack_from("<I", frame, 1)[0]
        topic = subscription_to_topic.get(subscription_id)
        if topic is None:
            continue
        timestamp = struct.unpack_from("<Q", frame, 5)[0]
        payload = frame[13:]
        channel = channels[topic]
        try:
            if channel.encoding == "json":
                decoded: object = json.loads(payload.decode("utf-8"))
            elif channel.encoding == "protobuf":
                decoded = protobuf_classes[topic]()
                decoded.ParseFromString(payload)
            else:
                raise AcceptanceFailure("FAIL_CLIENT", "Unexpected Foxglove encoding.")
        except (UnicodeError, json.JSONDecodeError, ValueError) as exc:
            raise AcceptanceFailure("FAIL_CLIENT", "Foxglove payload could not be decoded.") from exc
        for stage in expected.get(topic, set()):
            if _message_contains_stage(decoded, stage):
                observed[topic].add(stage)
                last_timestamp = max(last_timestamp, float(timestamp))
        for stage in forbidden.get(topic, set()):
            if _message_contains_stage(decoded, stage):
                forbidden_observed.append(stage)
    missing = {
        topic: sorted(stages - observed.get(topic, set()))
        for topic, stages in expected.items()
        if stages - observed.get(topic, set())
    }
    raise AcceptanceFailure("FAIL_CLIENT", f"Foxglove delivery evidence is incomplete: {missing}.")




__all__ = [name for name in globals() if not name.startswith("__")]
