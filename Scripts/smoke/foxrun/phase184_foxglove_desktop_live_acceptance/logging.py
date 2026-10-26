from __future__ import annotations
from .production import *
def _clock_value(dependencies: CoordinatorDependencies) -> float:
    """Handle the clock value step."""

    try:
        value = dependencies.clock()
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Coordinator monotonic clock failed.",
        )
    if (
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or not math.isfinite(float(value))
        or float(value) < 0
    ):
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Coordinator monotonic clock is invalid.",
        )
    return float(value)


def _sleep_poll(dependencies: CoordinatorDependencies) -> None:
    """Handle the sleep poll step."""

    try:
        dependencies.sleep(POLL_SECONDS)
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Coordinator bounded polling failed.",
        )


def _require_coordinator_log_bounds(
    dependencies: CoordinatorDependencies,
    paths: Sequence[pathlib.Path],
) -> None:
    """Require coordinator log bounds."""

    try:
        within_bound = dependencies.coordinator_logs_within_bound(
            paths,
            MAX_COORDINATOR_LOG_BYTES,
        )
    except Exception:
        within_bound = False
    if within_bound is not True:
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Coordinator-owned process log exceeded its fixed bound.",
        )


def _marker_tail(line: object, marker: str) -> str | None:
    """Handle the marker tail step."""

    if not isinstance(line, str):
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Unity log returned a non-text line.",
        )
    if marker not in line:
        return None
    if line.count(marker) != 1:
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Unity log marker envelope is ambiguous.",
        )
    tail = line[line.index(marker) :].strip()
    if (
        not tail
        or len(tail.encode("utf-8")) > protocol.MAX_TRANSPORT_CLIENT_MARKER_BYTES
    ):
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Unity log marker exceeds its fixed bound.",
        )
    return tail


def _redact_excerpt(line: str, token: str) -> str:
    """Handle the redact excerpt step."""

    redacted = line.replace(token, "<redacted>")
    if token in redacted or _RAW_TOKEN.search(redacted):
        _raise(
            protocol.FAIL_EVIDENCE,
            "Marker excerpt token redaction failed.",
        )
    if len(redacted) > protocol.MAX_TRANSPORT_CLIENT_MARKER_BYTES:
        _raise(
            protocol.FAIL_EVIDENCE,
            "Marker excerpt exceeds its fixed bound.",
        )
    return redacted


@dataclasses.dataclass(frozen=True)
class _LogEvidence:
    """Represent the log evidence contract."""

    context: str | None
    context_index: int | None
    transport_lines: tuple[str, ...]
    transport_markers: tuple[protocol.TransportClientMarker, ...]
    transport_indices: tuple[int, ...]


def _scan_unity_log(
    lines: Sequence[str],
    *,
    case: str,
    token: str,
) -> _LogEvidence:
    """Handle the scan unity log step."""

    if isinstance(lines, (str, bytes)) or len(lines) > 1_000_000:
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Unity log line collection exceeded its bound.",
        )
    context_line: str | None = None
    context_index: int | None = None
    transport_lines: list[str] = []
    transport_markers: list[protocol.TransportClientMarker] = []
    transport_indices: list[int] = []
    for raw_index, line in enumerate(lines):
        context = _marker_tail(line, "PHASE184G_CONTEXT_READY")
        if context is not None:
            match = _CONTEXT_MARKER.fullmatch(context)
            expected_digest = hashlib.sha256(
                token.encode("utf-8")
            ).hexdigest()[:12]
            if (
                match is None
                or match.group("case") != case
                or match.group("token") != token
                or match.group("digest").casefold() != expected_digest
            ):
                _raise(
                    protocol.FAIL_DESKTOP_CONNECTION,
                    "Unity context marker identity is mismatched.",
                )
            if context_line is None:
                context_line = context
                context_index = raw_index

        transport = _marker_tail(
            line,
            protocol.TRANSPORT_CLIENTS_OVERFLOW_MARKER,
        )
        if transport is None:
            transport = _marker_tail(
                line,
                protocol.TRANSPORT_CLIENTS_MARKER,
            )
        if transport is None:
            continue
        try:
            marker = protocol.parse_transport_client_marker(
                transport,
                case=case,
                token=token,
            )
        except protocol.AcceptanceFailure:
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Unity transport marker identity or envelope is invalid.",
            )
        if marker.overflow:
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Unity transport client marker overflow was observed.",
            )
        transport_lines.append(transport)
        transport_markers.append(marker)
        transport_indices.append(raw_index)
    return _LogEvidence(
        context=context_line,
        context_index=context_index,
        transport_lines=tuple(transport_lines),
        transport_markers=tuple(transport_markers),
        transport_indices=tuple(transport_indices),
    )


def _unique_transport_sequence(
    evidence: _LogEvidence,
) -> tuple[
    tuple[protocol.TransportClientMarker, str],
    ...,
]:
    """Handle the unique transport sequence step."""

    result: list[tuple[protocol.TransportClientMarker, str]] = []
    previous: tuple[protocol.TransportClientMarker, str] | None = None
    for marker, line in zip(
        evidence.transport_markers,
        evidence.transport_lines,
        strict=True,
    ):
        current = (marker, line)
        if current == previous:
            continue
        previous = current
        result.append(current)
        if len(result) > protocol.MAX_TRANSPORT_CLIENT_MARKERS:
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Transport marker sequence exceeds its fixed bound.",
            )
    return tuple(result)


def _validate_transport_prefix(
    sequence: tuple[
        tuple[protocol.TransportClientMarker, str],
        ...,
    ],
    *,
    maximum_stage: int,
) -> None:
    """Validate transport prefix."""

    if not sequence:
        return
    for index, (marker, _line) in enumerate(sequence):
        if index == 0:
            valid = (marker.active, marker.accepted) == (0, 0)
        elif index == 1:
            valid = (marker.active, marker.accepted) == (1, 1)
        elif index == 2:
            valid = marker.active == 2 and marker.accepted >= 2
        else:
            valid = False
        if not valid or index > maximum_stage:
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Transport clients did not follow exact 0/0 -> 1/1 -> 2/2+ order.",
            )



__all__ = [name for name in globals() if not name.startswith("__")]
