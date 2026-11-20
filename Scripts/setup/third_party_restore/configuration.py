"""Trusted pins and source URLs for third-party repository restoration."""

from __future__ import annotations

from collections.abc import Mapping

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
