"""Trusted pins and source URLs for third-party repository restoration."""

from __future__ import annotations

from collections.abc import Mapping
from urllib.parse import urlsplit

from Scripts.smoke.foxrun.phase186_provenance.foundation import _REFERENCE_REMOTE
from Scripts.validation.source_decomposition.freeze_baseline import VENDOR_ROOTS


SOURCE_URLS = {
    "third-party/foxglove-sdk": "https://github.com/foxglove/foxglove-sdk.git",
    "third-party/mcap": "https://github.com/foxglove/mcap.git",
    "third-party/ROS-TCP-Connector": _REFERENCE_REMOTE,
}


def vendor_pins(vendor_roots: Mapping[str, str] = VENDOR_ROOTS) -> dict[str, str]:
    """Return exactly the third-party pins supplied by the trusted baseline."""
    pins = {
        relative: revision
        for relative, revision in vendor_roots.items()
        if relative.startswith("third-party/")
    }
    if len(pins) != 3:
        raise RuntimeError("trusted baseline must expose exactly three third-party pins")
    return pins


def _canonical_github_url(value: str) -> tuple[str, str, str] | None:
    """Normalize an official GitHub HTTPS URL when its authority is safe."""
    try:
        parsed = urlsplit(value)
        if (
            parsed.scheme.lower() != "https"
            or parsed.hostname is None
            or parsed.hostname.lower() != "github.com"
            or parsed.username is not None
            or parsed.password is not None
            or parsed.port is not None
            or parsed.query
            or parsed.fragment
        ):
            return None
    except ValueError:
        return None
    path = parsed.path.rstrip("/")
    if path.endswith(".git"):
        path = path[:-4]
    parts = path.split("/")
    if len(parts) != 3 or not parts[1] or not parts[2]:
        return None
    return parsed.scheme.lower(), parsed.hostname.lower(), path


def source_url_matches(expected: str, actual: str) -> bool:
    """Accept only the expected source or its official optional .git spelling."""
    expected_canonical = _canonical_github_url(expected)
    actual_canonical = _canonical_github_url(actual)
    if expected_canonical is None:
        return expected == actual
    return actual_canonical == expected_canonical
