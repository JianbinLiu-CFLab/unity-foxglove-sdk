#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from .foundation import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase179_foxrun_ros2_player_host.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def _marker_fields(line: str, marker: str) -> dict[str, str] | None:
    """Return bounded key=value fields after an exact Phase179 marker."""

    index = line.find(marker)
    if index < 0:
        return None
    return dict(re.findall(r"\b([A-Za-z][A-Za-z0-9_]*)=([^\s]+)", line[index + len(marker) :]))


def find_ready_marker(text: str, distro: str, rmw: str, token: str) -> dict[str, str]:
    """Require the Player itself to report its active runtime/RMW/token selection."""

    saw_marker = False
    for line in text.splitlines():
        fields = _marker_fields(line, READY_MARKER)
        if fields is None:
            continue
        saw_marker = True
        if fields.get("runtime") == distro and fields.get("rmw") == rmw and fields.get("token") == token:
            return fields
    if saw_marker:
        raise PlayerHostFailure("READY_MISMATCH", "Player READY marker did not report the requested runtime, RMW, and token.")
    raise PlayerHostFailure("READY_TIMEOUT", "Player did not emit its Phase179 READY marker.")


def find_completion_marker(text: str, token: str) -> dict[str, str]:
    """Require a success completion marker with the same correlation token."""

    saw_marker = False
    for line in text.splitlines():
        fields = _marker_fields(line, COMPLETE_MARKER)
        if fields is None:
            continue
        saw_marker = True
        if fields.get("token") == token and fields.get("outcome") == "success" and fields.get("exitCode") == "0":
            return fields
    if saw_marker:
        raise PlayerHostFailure("COMPLETE_MISMATCH", "Player completion marker was not a successful zero-exit result for this token.")
    raise PlayerHostFailure("COMPLETE_TIMEOUT", "Player did not emit its Phase179 completion marker.")


def wait_for_ready_marker(
    player_log: pathlib.Path,
    distro: str,
    rmw: str,
    token: str,
    timeout_seconds: float,
) -> dict[str, str]:
    """Poll the growing Player log until a matching active-runtime READY marker exists."""

    deadline = time.monotonic() + timeout_seconds
    last_mismatch: PlayerHostFailure | None = None
    while True:
        if player_log.is_file():
            try:
                return find_ready_marker(player_log.read_text(encoding="utf-8", errors="replace"), distro, rmw, token)
            except PlayerHostFailure as exc:
                if exc.category == "READY_MISMATCH":
                    last_mismatch = exc
        if time.monotonic() >= deadline:
            if last_mismatch is not None:
                raise last_mismatch
            raise PlayerHostFailure("READY_TIMEOUT", "Player did not become ready before timeout.")
        time.sleep(min(0.25, max(0.0, deadline - time.monotonic())))


def verify_required_applied_markers(
    text: str,
    token: str,
    string_burst_final_sequence: int | None = None,
) -> dict[str, object]:
    """Require copied, exact values for every non-optional Phase179 message contract."""

    markers: dict[str, inbound.UnityMarker] = {}
    for name in REQUIRED_MESSAGE_NAMES:
        spec = inbound.MESSAGE_SPECS[name]
        try:
            markers[name] = inbound.find_matching_unity_marker(
                text,
                inbound.topic_for_spec("/foxrun/phase179", spec),
                token,
                spec.expected_value(token),
            )
        except inbound.AcceptanceFailure as exc:
            category = "VALUE_MISMATCH" if exc.category == "VALUE_MISMATCH" else "APPLIED_MARKERS"
            raise PlayerHostFailure(category, "Player did not prove all required copied native values.") from exc
    evidence: dict[str, object] = {}
    if string_burst_final_sequence is not None:
        try:
            final_marker = inbound.find_matching_unity_marker(
                text,
                inbound.topic_for_spec("/foxrun/phase179", inbound.MESSAGE_SPECS["string"]),
                token,
                inbound.expected_string_burst_value(token, string_burst_final_sequence),
            )
            evidence["stringBurst"] = inbound.validate_string_burst_marker(
                markers["string"],
                final_marker,
                token,
                string_burst_final_sequence,
            )
        except inbound.AcceptanceFailure as exc:
            category = "VALUE_MISMATCH" if exc.category == "VALUE_MISMATCH" else "BURST"
            raise PlayerHostFailure(category, "Player did not prove the requested latest-wins String burst.") from exc
    return evidence


def classify_player_verdict(
    *,
    ready: bool,
    all_applied: bool,
    exit_code: int | None,
    failure: PlayerHostFailure | None,
) -> str:
    """Classify Player evidence strictly; launch success alone is not accepted."""

    if failure is not None:
        return f"FAIL_{failure.category}"
    if not ready:
        return "FAIL_READY"
    if not all_applied:
        return "FAIL_APPLIED_MARKERS"
    if exit_code != 0:
        return "FAIL_EXIT_CODE"
    return "PLAYER_PROOF_COMPLETE_LINUX_PEER_CORRELATION_PENDING"


def sanitize_summary(value: object) -> object:
    """Use the same portable evidence redaction policy as the Linux helper."""

    return inbound.sanitize_summary(value)

__all__ = ['_Phase192Path', '_PHASE192_FACADE_FILE', '_marker_fields', 'find_ready_marker', 'find_completion_marker', 'wait_for_ready_marker', 'verify_required_applied_markers', 'classify_player_verdict', 'sanitize_summary']
