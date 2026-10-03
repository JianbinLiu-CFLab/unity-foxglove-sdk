from __future__ import annotations
from .desktop_barrier_wait import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_foxglove_desktop_live_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
@dataclasses.dataclass(frozen=True, slots=True)
class TransportClientMarker:
    """One validated transport-count marker with token identity discarded."""

    overflow: bool
    active: int
    accepted: int

    def to_document(self) -> dict[str, int]:
        """Handle the to document step."""

        return {"active": self.active, "accepted": self.accepted}


def _validate_transport_identity(case: object, token: object) -> tuple[str, str]:
    """Validate transport identity."""

    if not isinstance(case, str) or _SAFE_CASE.fullmatch(case) is None:
        raise _evidence_failure("Transport marker case identity is invalid.")
    if not isinstance(token, str) or _SAFE_TOKEN.fullmatch(token) is None:
        raise _evidence_failure("Transport marker token identity is invalid.")
    return case, token


def parse_transport_client_marker(
    line: object,
    *,
    case: str,
    token: str,
) -> TransportClientMarker:
    """Parse one exact bounded token-correlated transport-count marker."""

    expected_case, expected_token = _validate_transport_identity(case, token)
    if not isinstance(line, str) or "\r" in line or "\n" in line:
        raise _evidence_failure("Transport client marker line is invalid.")
    try:
        encoded_size = len(line.encode("utf-8"))
    except UnicodeError as exc:
        raise _evidence_failure("Transport client marker line is invalid.") from exc
    if not line or encoded_size > MAX_TRANSPORT_CLIENT_MARKER_BYTES:
        raise _evidence_failure("Transport client marker line exceeds its fixed bound.")

    match = _TRANSPORT_CLIENT_MARKER.fullmatch(line)
    if match is None:
        raise _evidence_failure("Transport client marker envelope is malformed.")
    if (
        match.group("case") != expected_case
        or match.group("token") != expected_token
    ):
        raise _evidence_failure("Transport client marker identity is mismatched.")

    active = int(match.group("active"))
    accepted = int(match.group("accepted"))
    if active > MAX_TRANSPORT_CLIENT_COUNT or accepted > MAX_TRANSPORT_CLIENT_COUNT:
        raise _evidence_failure("Transport client marker count exceeds its fixed bound.")
    return TransportClientMarker(
        overflow=match.group("marker") == TRANSPORT_CLIENTS_OVERFLOW_MARKER,
        active=active,
        accepted=accepted,
    )


def validate_transport_client_transition_order(
    lines: Iterable[str],
    *,
    case: str,
    token: str,
) -> tuple[TransportClientMarker, TransportClientMarker, TransportClientMarker]:
    """Require the exact chronological 0/0 -> 1/1 -> 2/2+ live transition."""

    _validate_transport_identity(case, token)
    if isinstance(lines, (str, bytes)):
        raise _evidence_failure("Transport marker evidence must be a line sequence.")
    try:
        iterator = iter(lines)
    except TypeError as exc:
        raise _evidence_failure(
            "Transport marker evidence must be a line sequence."
        ) from exc

    required: list[TransportClientMarker] = []
    previous_line: str | None = None
    marker_line_count = 0
    collapsed_count = 0
    for line in iterator:
        if not isinstance(line, str):
            raise _evidence_failure("Transport marker evidence line is invalid.")
        if not line.startswith("PHASE184H_TRANSPORT_CLIENT"):
            continue
        marker = parse_transport_client_marker(line, case=case, token=token)
        if marker.overflow:
            raise _evidence_failure("Transport client marker overflow was observed.")

        marker_line_count += 1
        if marker_line_count > MAX_TRANSPORT_CLIENT_MARKERS:
            raise _evidence_failure(
                "Transport client marker evidence exceeds its fixed bound."
            )
        if line == previous_line:
            continue
        previous_line = line
        collapsed_count += 1
        if collapsed_count > MAX_TRANSPORT_CLIENT_MARKERS:
            raise _evidence_failure(
                "Transport client marker evidence exceeds its fixed bound."
            )

        pair = (marker.active, marker.accepted)
        stage = len(required)
        if stage == 0:
            matches = pair == (0, 0)
        elif stage == 1:
            matches = pair == (1, 1)
        elif stage == 2:
            matches = marker.active == 2 and marker.accepted >= 2
        else:
            matches = False
        if not matches:
            raise _evidence_failure(
                "Transport client markers are missing the required strict order."
            )
        required.append(marker)

    if len(required) != 3:
        raise _evidence_failure(
            "Transport client markers are missing the required strict order."
        )
    return required[0], required[1], required[2]


__all__ = [name for name in globals() if not name.startswith("__")]
