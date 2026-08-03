#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Fail-closed typed FoxRun MessagePack full-duplex acceptance probe.
# Usage: python Scripts/smoke/websocket/phase185_foxrun_messagepack_probe.py --url ws://127.0.0.1:8765 --output build/phase185/probe/report.json

"""Verify the controlled Full Demo typed MessagePack full-duplex contract."""

from __future__ import annotations

import argparse
import asyncio
import json
import ssl
import struct
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit

import websockets


FOXGLOVE_SUBPROTOCOL = "foxglove.sdk.v1"
CATALOG_SERVICE = "/foxrun/subscription-contracts"
PROBE_TOPIC = "/phase185/messagepack/full-duplex"
APPLY_EVIDENCE_TOPIC = "/phase185/messagepack/apply-evidence"
EXPECTED_ENCODING = "msgpack"

REMOTE_SEQUENCE_A = 185_001
REMOTE_VALUE_A = 41
LOCAL_SEQUENCE_B = 185_002
LOCAL_VALUE_B = 82
RECOVERY_SEQUENCE = 185_003
RECOVERY_VALUE = 123

NO_OUTPUT_WINDOW_SECONDS = 1.0
LOCAL_OUTPUT_TIMEOUT_SECONDS = 4.0
EXACTLY_ONCE_QUIET_SECONDS = 0.6
MALFORMED_SETTLE_SECONDS = 0.2
RECOVERY_TIMEOUT_SECONDS = 2.0
DISCOVERY_TIMEOUT_SECONDS = 15.0
SERVICE_TIMEOUT_SECONDS = 5.0
STARTUP_DRAIN_SECONDS = 0.25

CLIENT_CHANNEL_ID = 185_001
OUTPUT_SUBSCRIPTION_ID = 185_101
EVIDENCE_SUBSCRIPTION_ID = 185_102
CATALOG_CALL_ID = 185_201

CLIENT_MESSAGE_DATA_OPCODE = 1
CLIENT_SERVICE_CALL_OPCODE = 2
SERVER_MESSAGE_DATA_OPCODE = 1
SERVER_SERVICE_RESPONSE_OPCODE = 3
MESSAGE_PAYLOAD_START = 13

EXIT_SUCCESS = 0
EXIT_FAILURE = 1


class ProbeFailure(RuntimeError):
    """Raised when live evidence cannot satisfy the Phase185 contract."""


@dataclass(frozen=True)
class ChannelInfo:
    """One advertised server channel."""

    channel_id: int
    topic: str
    encoding: str
    schema_name: str
    schema_encoding: str
    schema: str


@dataclass(frozen=True)
class ServiceInfo:
    """One advertised server service."""

    service_id: int
    name: str


@dataclass(frozen=True)
class Discovery:
    """Required controlled channels and catalog service."""

    output: ChannelInfo
    evidence: ChannelInfo
    catalog: ServiceInfo


def encode_probe_payload(sequence: int, value: int) -> bytes:
    """Encode the canonical deterministic two-field MessagePack map."""
    return (
        b"\x82"
        + _encode_string("messagePackSequence")
        + _encode_integer(sequence)
        + _encode_string("messagePackValue")
        + _encode_integer(value)
    )


def decode_complete_msgpack(payload: bytes) -> object:
    """Independently decode one complete bounded MessagePack value."""
    offset, value = _decode_msgpack_value(payload, 0, 0)
    if offset != len(payload):
        raise ProbeFailure(
            f"MessagePack payload has {len(payload) - offset} trailing byte(s)."
        )
    return value


def select_catalog_contract(catalog: object) -> dict[str, Any]:
    """Select exactly one available schemaless Subscribe row for the probe."""
    if not isinstance(catalog, dict):
        raise ProbeFailure("Catalog response is not an object.")
    if catalog.get("subscriptionsEnabled") is not True:
        raise ProbeFailure("FoxRun subscriptions are disabled.")
    contracts = catalog.get("contracts")
    if not isinstance(contracts, list):
        raise ProbeFailure("Catalog response has no contracts array.")
    matches = [
        contract
        for contract in contracts
        if isinstance(contract, dict)
        and contract.get("topic") == PROBE_TOPIC
        and contract.get("flow") == "Subscribe"
    ]
    if len(matches) != 1:
        raise ProbeFailure(
            f"Expected exactly one Subscribe catalog row for {PROBE_TOPIC}, got {len(matches)}."
        )
    selected = matches[0]
    if selected.get("encoding") != EXPECTED_ENCODING:
        raise ProbeFailure(
            "Controlled input row is not resolved to MessagePack."
        )
    if selected.get("subscribeAvailable") is not True:
        diagnostic = str(selected.get("unavailableDiagnosticId", ""))
        reason = str(selected.get("unavailableReason", ""))
        raise ProbeFailure(
            f"Controlled MessagePack input is unavailable: {diagnostic} {reason}".strip()
        )
    if selected.get("schemaName", "") != "" or selected.get("wireSchemaName", "") != "":
        raise ProbeFailure("MessagePack catalog wire schema fields must be empty.")
    logical = selected.get("logicalSchemaName")
    if not isinstance(logical, str) or not logical:
        raise ProbeFailure("MessagePack catalog logical schema identity is missing.")
    return selected


def build_pass_report(
    *,
    contract: dict[str, Any],
    payload_a: bytes,
    payload_b: bytes,
    decoded_b: object,
    no_output_seconds: float,
    malformed_rejections: int,
    recovery_applied: bool,
) -> dict[str, Any]:
    """Build a bounded terminal report only after all evidence is complete."""
    expected_b = {
        "messagePackSequence": LOCAL_SEQUENCE_B,
        "messagePackValue": LOCAL_VALUE_B,
    }
    if contract.get("topic") != PROBE_TOPIC:
        raise ProbeFailure("PASS report topic does not match the controlled probe.")
    if contract.get("encoding") != EXPECTED_ENCODING:
        raise ProbeFailure("PASS report encoding is not msgpack.")
    if contract.get("schemaName", "") != "" or contract.get("wireSchemaName", "") != "":
        raise ProbeFailure("PASS report wire schema must remain empty.")
    if payload_a == payload_b:
        raise ProbeFailure("Later local B payload must differ from inbound A.")
    if decoded_b != expected_b:
        raise ProbeFailure("Independent decoder did not recover canonical local B.")
    if no_output_seconds < NO_OUTPUT_WINDOW_SECONDS:
        raise ProbeFailure("No-output evidence window is incomplete.")
    if malformed_rejections < 3 or not recovery_applied:
        raise ProbeFailure("Malformed rejection and recovery evidence is incomplete.")

    return {
        "version": 1,
        "verdict": "PASS",
        "selectedContract": {
            "topic": PROBE_TOPIC,
            "flow": "Subscribe",
            "messageEncoding": EXPECTED_ENCODING,
            "schemaName": "",
            "wireSchemaName": "",
            "logicalSchemaName": contract.get("logicalSchemaName", ""),
        },
        "remoteInput": {
            "identity": "A",
            "sequence": REMOTE_SEQUENCE_A,
            "value": REMOTE_VALUE_A,
            "payloadHex": payload_a.hex(),
        },
        "unityApply": {
            "observed": True,
            "sequence": REMOTE_SEQUENCE_A,
            "value": REMOTE_VALUE_A,
            "evidenceTopic": APPLY_EVIDENCE_TOPIC,
        },
        "noImmediateMirror": {
            "complete": True,
            "seconds": no_output_seconds,
            "sameTopicOutputCount": 0,
        },
        "canonicalOutput": {
            "identity": "B",
            "topic": PROBE_TOPIC,
            "directionMetadataKey": "unity2foxglove.direction",
            "direction": "output",
            "messageEncoding": EXPECTED_ENCODING,
            "schemaName": "",
            "expectedSchemaId": 0,
            "payloadHex": payload_b.hex(),
            "decoded": decoded_b,
            "count": 1,
            "remoteEcho": False,
        },
        "malformedInput": {
            "rejectionsObserved": malformed_rejections,
            "observation": "no state-application evidence and later valid recovery",
            "recoveryApplied": recovery_applied,
            "recoverySequence": RECOVERY_SEQUENCE,
            "recoveryValue": RECOVERY_VALUE,
        },
    }


async def run_live(args: argparse.Namespace) -> dict[str, Any]:
    """Collect the complete live acceptance report."""
    url = _build_url(args)
    ssl_context = _build_ssl_context(url, args.insecure)
    async with websockets.connect(
        url,
        subprotocols=[FOXGLOVE_SUBPROTOCOL],
        ssl=ssl_context,
        max_size=4 * 1024 * 1024,
    ) as websocket:
        discovery = await _discover(websocket, args.discovery_timeout_seconds)
        _validate_output_channel(discovery.output)
        _validate_evidence_channel(discovery.evidence)

        catalog = await _call_catalog(
            websocket,
            discovery.catalog,
            args.service_timeout_seconds,
        )
        contract = select_catalog_contract(catalog)

        await websocket.send(
            json.dumps(
                {
                    "op": "subscribe",
                    "subscriptions": [
                        {
                            "id": OUTPUT_SUBSCRIPTION_ID,
                            "channelId": discovery.output.channel_id,
                        },
                        {
                            "id": EVIDENCE_SUBSCRIPTION_ID,
                            "channelId": discovery.evidence.channel_id,
                        },
                    ],
                },
                separators=(",", ":"),
            )
        )
        await _drain(websocket, args.startup_drain_seconds)
        await websocket.send(_build_client_advertise(PROBE_TOPIC, CLIENT_CHANNEL_ID))

        payload_a = encode_probe_payload(REMOTE_SEQUENCE_A, REMOTE_VALUE_A)
        await websocket.send(_build_client_message(CLIENT_CHANNEL_ID, payload_a))
        await _wait_for_evidence(
            websocket,
            REMOTE_SEQUENCE_A,
            REMOTE_VALUE_A,
            args.apply_timeout_seconds,
            reject_output_payload=payload_a,
        )

        no_output_started = time.perf_counter()
        await _require_no_output(
            websocket,
            args.no_output_window_seconds,
            "bounded no-output window after remote A apply",
        )
        no_output_elapsed = time.perf_counter() - no_output_started

        payload_b = await _wait_for_canonical_output_b(
            websocket,
            args.local_output_timeout_seconds,
        )
        decoded_b = decode_complete_msgpack(payload_b)
        await _require_no_output(
            websocket,
            args.exactly_once_quiet_seconds,
            "exactly-one quiet window after local B",
        )

        malformed_payloads = _malformed_payloads(payload_a)
        for index, malformed in enumerate(malformed_payloads, start=1):
            await websocket.send(
                _build_client_message(CLIENT_CHANNEL_ID, malformed)
            )
            await _require_no_probe_activity(
                websocket,
                args.malformed_settle_seconds,
                f"malformed case {index}",
            )

        recovery = encode_probe_payload(RECOVERY_SEQUENCE, RECOVERY_VALUE)
        await websocket.send(_build_client_message(CLIENT_CHANNEL_ID, recovery))
        await _wait_for_evidence(
            websocket,
            RECOVERY_SEQUENCE,
            RECOVERY_VALUE,
            args.recovery_timeout_seconds,
            reject_output_payload=recovery,
        )
        await _require_no_output(
            websocket,
            args.malformed_settle_seconds,
            "origin suppression after valid recovery",
        )

    return build_pass_report(
        contract=contract,
        payload_a=payload_a,
        payload_b=payload_b,
        decoded_b=decoded_b,
        no_output_seconds=no_output_elapsed,
        malformed_rejections=len(malformed_payloads),
        recovery_applied=True,
    )


async def _discover(websocket: Any, timeout_seconds: float) -> Discovery:
    """Collect the bounded Foxglove channel and service advertisements."""
    channels: dict[str, ChannelInfo] = {}
    services: dict[str, ServiceInfo] = {}
    deadline = time.perf_counter() + timeout_seconds
    while time.perf_counter() < deadline:
        frame = await _receive_until(websocket, deadline)
        if not isinstance(frame, str):
            continue
        try:
            message = json.loads(frame)
        except json.JSONDecodeError:
            continue
        if message.get("op") == "advertise":
            for raw in message.get("channels", []):
                if not isinstance(raw, dict):
                    continue
                channel = ChannelInfo(
                    channel_id=int(raw.get("id", 0)),
                    topic=str(raw.get("topic", "")),
                    encoding=str(raw.get("encoding", "")),
                    schema_name=str(raw.get("schemaName", "")),
                    schema_encoding=str(raw.get("schemaEncoding", "")),
                    schema=str(raw.get("schema", "")),
                )
                channels[channel.topic] = channel
        elif message.get("op") == "advertiseServices":
            for raw in message.get("services", []):
                if not isinstance(raw, dict):
                    continue
                service = ServiceInfo(
                    service_id=int(raw.get("id", 0)),
                    name=str(raw.get("name", "")),
                )
                services[service.name] = service

        if (
            PROBE_TOPIC in channels
            and APPLY_EVIDENCE_TOPIC in channels
            and CATALOG_SERVICE in services
        ):
            return Discovery(
                output=channels[PROBE_TOPIC],
                evidence=channels[APPLY_EVIDENCE_TOPIC],
                catalog=services[CATALOG_SERVICE],
            )
    raise ProbeFailure(
        "Timed out discovering controlled MessagePack channels and catalog service. "
        f"channels={sorted(channels)} services={sorted(services)}"
    )


async def _call_catalog(
    websocket: Any,
    service: ServiceInfo,
    timeout_seconds: float,
) -> object:
    """Call the catalog service and return its decoded JSON payload."""
    payload = json.dumps({}, separators=(",", ":")).encode("utf-8")
    await websocket.send(
        _build_service_call(service.service_id, CATALOG_CALL_ID, payload)
    )
    deadline = time.perf_counter() + timeout_seconds
    while time.perf_counter() < deadline:
        frame = await _receive_until(websocket, deadline)
        if isinstance(frame, str):
            try:
                message = json.loads(frame)
            except json.JSONDecodeError:
                continue
            if (
                message.get("op") == "serviceCallFailure"
                and int(message.get("callId", 0)) == CATALOG_CALL_ID
            ):
                raise ProbeFailure(
                    "Catalog service failed: " + str(message.get("message", ""))
                )
            continue
        decoded = _decode_service_response(frame)
        if decoded is None:
            continue
        service_id, call_id, encoding, response = decoded
        if service_id != service.service_id or call_id != CATALOG_CALL_ID:
            continue
        if encoding != "json":
            raise ProbeFailure(
                f"Catalog response encoding must be json, got {encoding!r}."
            )
        try:
            return json.loads(response.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise ProbeFailure("Catalog response is not valid UTF-8 JSON.") from exc
    raise ProbeFailure("Timed out waiting for the catalog service response.")


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


def parse_args() -> argparse.Namespace:
    """Parse bounded live-probe command-line options."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="ws://127.0.0.1:8765")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--token", default="")
    parser.add_argument("--insecure", action="store_true")
    parser.add_argument(
        "--output",
        default="build/phase185/probe/report.json",
    )
    parser.add_argument(
        "--discovery-timeout-seconds",
        type=float,
        default=DISCOVERY_TIMEOUT_SECONDS,
    )
    parser.add_argument(
        "--service-timeout-seconds",
        type=float,
        default=SERVICE_TIMEOUT_SECONDS,
    )
    parser.add_argument("--apply-timeout-seconds", type=float, default=3.0)
    parser.add_argument(
        "--no-output-window-seconds",
        type=float,
        default=NO_OUTPUT_WINDOW_SECONDS,
    )
    parser.add_argument(
        "--local-output-timeout-seconds",
        type=float,
        default=LOCAL_OUTPUT_TIMEOUT_SECONDS,
    )
    parser.add_argument(
        "--exactly-once-quiet-seconds",
        type=float,
        default=EXACTLY_ONCE_QUIET_SECONDS,
    )
    parser.add_argument(
        "--malformed-settle-seconds",
        type=float,
        default=MALFORMED_SETTLE_SECONDS,
    )
    parser.add_argument(
        "--recovery-timeout-seconds",
        type=float,
        default=RECOVERY_TIMEOUT_SECONDS,
    )
    parser.add_argument(
        "--startup-drain-seconds",
        type=float,
        default=STARTUP_DRAIN_SECONDS,
    )
    return parser.parse_args()


def main() -> int:
    """Run the live probe and write a bounded PASS or FAIL report."""
    args = parse_args()
    url = _build_url(args)
    output = Path(args.output)
    try:
        report = asyncio.run(run_live(args))
    except (ProbeFailure, OSError, ValueError) as exc:
        failure = {
            "version": 1,
            "verdict": "FAIL",
            "endpoint": _redacted_url(url),
            "reason": str(exc),
        }
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(
            json.dumps(failure, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        print("Verdict: FAIL")
        print(str(exc))
        return EXIT_FAILURE

    report["endpoint"] = _redacted_url(url)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(f"Report: {output}")
    print("Verdict: PASS")
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main())
