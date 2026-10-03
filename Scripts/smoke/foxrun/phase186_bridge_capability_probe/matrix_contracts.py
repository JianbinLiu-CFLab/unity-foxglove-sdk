#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke/foxrun
# Purpose: Prove one portable local-origin primitive across the Phase186 ROS/RMW matrix.

"""Run a real generic publisher/subscriber origin-classification probe.

The probe uses ``GenericSubscription.take_serialized`` plus the returned
``rmw_message_info.publisher_gid``.  It compares that GID with the
process-owned generic publisher GID and separately observes an independently
owned publisher on the same topic/type.  All four maintained rows must select
this exact mechanism before Phase186 may claim portable loop suppression.
"""

from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_capability_probe.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

import argparse
import json
import os
import pathlib
import platform
import signal
import subprocess
import sys
import tempfile
import time
import uuid
from collections.abc import Mapping, Sequence
from types import MappingProxyType


SCRIPT_DIRECTORY = pathlib.Path(__file__).resolve().parent
if str(SCRIPT_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIRECTORY))

import phase186_bridge_build as build
import phase186_bridge_acceptance_protocol as acceptance_protocol


SELECTED_MECHANISM = "publisher_gid_take_serialized"
SUMMARY_SCHEMA_VERSION = 1
DOMAIN_IDS: Mapping[str, int] = MappingProxyType(
    {row_id: row.domain_id for row_id, row in acceptance_protocol.ROWS.items()}
)


class ProbeFailure(RuntimeError):
    """Stable fail-closed capability-probe error."""


def validate_row_result(
    value: Mapping[str, object],
    row: build.BridgeRow,
) -> None:
    """Require direct ROS observations for one exact row."""

    if not isinstance(value, Mapping):
        raise ProbeFailure("row result is not an object")
    expected = {
        "schemaVersion": SUMMARY_SCHEMA_VERSION,
        "rowId": row.row_id,
        "distro": row.distro,
        "requestedRmw": row.rmw,
        "observedRmw": row.rmw,
        "verdict": "PASS",
        "platform": "Windows",
        "domainOwned": True,
        "ambientDomainRejected": True,
        "canonicalType": build.INTERFACE_TYPE,
        "interfaceDigest": build.INTERFACE_DIGEST,
        "mechanism": SELECTED_MECHANISM,
    }
    for key, expected_value in expected.items():
        if value.get(key) != expected_value:
            raise ProbeFailure("row result mismatch for " + key)
    domain = value.get("domainId")
    if not isinstance(domain, int) or domain != DOMAIN_IDS[row.row_id]:
        raise ProbeFailure("row result did not use its owned domain")
    overlay = value.get("overlayAuthority")
    if (
        not isinstance(overlay, Mapping)
        or overlay.get("validated") is not True
        or overlay.get("rowId") != row.row_id
    ):
        raise ProbeFailure("row result lacks exact overlay authority")
    observations = value.get("rosObservations")
    expected_observations = {
        "localSeen": True,
        "localGidMatched": True,
        "ignoreLocalSawLocal": False,
        "externalSeen": True,
        "externalGidMatched": False,
        "ignoreLocalSawExternal": True,
    }
    if not isinstance(observations, Mapping):
        raise ProbeFailure("row result lacks direct ROS observations")
    for key, expected_value in expected_observations.items():
        if observations.get(key) is not expected_value:
            raise ProbeFailure("ROS observation mismatch for " + key)
    owned = value.get("ownedProcesses")
    if (
        not isinstance(owned, Mapping)
        or not isinstance(owned.get("subscriberPid"), int)
        or not isinstance(owned.get("publisherPid"), int)
        or owned.get("cleanupComplete") is not True
    ):
        raise ProbeFailure("row result lacks owned-process cleanup proof")
    if row.rmw == "rmw_zenoh_cpp":
        topology = value.get("zenohTopology")
        if (
            not isinstance(topology, Mapping)
            or topology.get("owned") is not True
            or not topology.get("topologyId")
            or not isinstance(topology.get("routerPid"), int)
            or not topology.get("sessionConfig")
        ):
            raise ProbeFailure("Zenoh row lacks owned topology evidence")


def validate_matrix(
    rows: Mapping[str, Mapping[str, object]],
) -> dict[str, object]:
    """Require the exact four-row matrix and one identical mechanism."""

    if not isinstance(rows, Mapping) or tuple(rows) != tuple(build.ROWS):
        raise ProbeFailure(
            "capability matrix must contain the exact maintained rows in order"
        )
    mechanisms: set[str] = set()
    for row_id, row in build.ROWS.items():
        value = rows.get(row_id)
        if not isinstance(value, Mapping):
            raise ProbeFailure("capability matrix row is missing: " + row_id)
        validate_row_result(value, row)
        mechanisms.add(str(value.get("mechanism")))
    if mechanisms != {SELECTED_MECHANISM}:
        raise ProbeFailure("capability matrix selected different mechanisms")
    return {
        "schemaVersion": SUMMARY_SCHEMA_VERSION,
        "verdict": "PASS",
        "rows": list(build.ROWS),
        "selectedMechanism": SELECTED_MECHANISM,
        "canonicalType": build.INTERFACE_TYPE,
        "interfaceDigest": build.INTERFACE_DIGEST,
    }


__all__ = [name for name in globals() if not name.startswith("__")]
