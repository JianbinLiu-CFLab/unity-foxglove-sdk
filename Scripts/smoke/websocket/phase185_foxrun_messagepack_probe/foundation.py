#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Fail-closed typed FoxRun MessagePack full-duplex acceptance probe.
# Usage: python Scripts/smoke/websocket/phase185_foxrun_messagepack_probe.py --url ws://127.0.0.1:8765 --output build/phase185/probe/report.json

"""Verify the controlled Full Demo typed MessagePack full-duplex contract."""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase185_foxrun_messagepack_probe.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
try:
    from Scripts.smoke.atomic_output import atomic_write_bytes, atomic_write_text
except ModuleNotFoundError:
    import sys as _sys
    from pathlib import Path as _Path
    _sys.path.insert(0, str(_Path(__file__).resolve().parents[3]))
    from Scripts.smoke.atomic_output import atomic_write_bytes, atomic_write_text
import argparse
import asyncio
import json
import math
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


__all__ = [name for name in globals() if not name.startswith("__")]
