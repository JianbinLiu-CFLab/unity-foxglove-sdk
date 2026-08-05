#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

import copy
import pathlib
import sys
import unittest


SCRIPT_DIR = pathlib.Path(__file__).resolve().parents[1]
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

import phase186_bridge_build as build
import phase186_bridge_acceptance_protocol as acceptance_protocol
import phase186_bridge_capability_probe as probe


def passing_row(row_id: str) -> dict[str, object]:
    """Build a complete passing capability result for one maintained row."""

    row = build.require_row(row_id)
    result: dict[str, object] = {
        "schemaVersion": 1,
        "rowId": row.row_id,
        "distro": row.distro,
        "requestedRmw": row.rmw,
        "observedRmw": row.rmw,
        "verdict": "PASS",
        "platform": "Windows",
        "domainId": probe.DOMAIN_IDS[row_id],
        "domainOwned": True,
        "ambientDomainRejected": True,
        "canonicalType": build.INTERFACE_TYPE,
        "interfaceDigest": build.INTERFACE_DIGEST,
        "overlayAuthority": {
            "validated": True,
            "rowId": row.row_id,
        },
        "mechanism": probe.SELECTED_MECHANISM,
        "rosObservations": {
            "localSeen": True,
            "localGidMatched": True,
            "ignoreLocalSawLocal": False,
            "externalSeen": True,
            "externalGidMatched": False,
            "ignoreLocalSawExternal": True,
        },
        "ownedProcesses": {
            "subscriberPid": 100,
            "publisherPid": 101,
            "cleanupComplete": True,
        },
    }
    if row.rmw == "rmw_zenoh_cpp":
        result["zenohTopology"] = {
            "owned": True,
            "topologyId": "phase186-test-topology",
            "routerPid": 102,
            "sessionConfig": "session.json5",
        }
    return result


class Phase186BridgeCapabilityProbeTests(unittest.TestCase):
    """Regression coverage for the Phase186 Bridge capability matrix."""

    def test_exact_good_matrix_selects_one_mechanism(self) -> None:
        """Accept the exact maintained matrix using one selected mechanism."""

        self.assertEqual(
            {
                row_id: row.domain_id
                for row_id, row in acceptance_protocol.ROWS.items()
            },
            probe.DOMAIN_IDS,
        )
        matrix = {row_id: passing_row(row_id) for row_id in build.ROWS}
        validated = probe.validate_matrix(matrix)
        self.assertEqual(probe.SELECTED_MECHANISM, validated["selectedMechanism"])
        self.assertEqual(list(build.ROWS), validated["rows"])

    def test_missing_or_renamed_row_is_rejected(self) -> None:
        """Reject matrices with a missing row or an aliased row identifier."""

        matrix = {
            row_id: passing_row(row_id)
            for row_id in tuple(build.ROWS)[:-1]
        }
        matrix["lyrical-fastdds"] = passing_row("lyrical-fastrtps")
        with self.assertRaises(probe.ProbeFailure):
            probe.validate_matrix(matrix)

    def test_self_report_without_ros_observations_is_rejected(self) -> None:
        """Reject a passing self-report that lacks observed ROS evidence."""

        result = passing_row("jazzy-fastrtps")
        result.pop("rosObservations")
        with self.assertRaises(probe.ProbeFailure):
            probe.validate_row_result(result, build.require_row("jazzy-fastrtps"))

    def test_requested_and_observed_rmw_must_match(self) -> None:
        """Require the observed RMW to equal the requested matrix RMW."""

        result = passing_row("jazzy-fastrtps")
        result["observedRmw"] = "rmw_zenoh_cpp"
        with self.assertRaises(probe.ProbeFailure):
            probe.validate_row_result(result, build.require_row("jazzy-fastrtps"))

    def test_zenoh_requires_owned_topology(self) -> None:
        """Require owned Zenoh topology evidence for the Zenoh row."""

        result = passing_row("lyrical-zenoh")
        result.pop("zenohTopology")
        with self.assertRaises(probe.ProbeFailure):
            probe.validate_row_result(result, build.require_row("lyrical-zenoh"))

    def test_ambient_domain_is_rejected(self) -> None:
        """Reject evidence collected from an ambient unowned ROS domain."""

        result = passing_row("humble-fastrtps")
        result["domainOwned"] = False
        with self.assertRaises(probe.ProbeFailure):
            probe.validate_row_result(result, build.require_row("humble-fastrtps"))

    def test_matrix_cannot_change_mechanism_by_rmw(self) -> None:
        """Reject per-RMW drift from the matrix-wide selected mechanism."""

        matrix = {row_id: passing_row(row_id) for row_id in build.ROWS}
        matrix["lyrical-zenoh"]["mechanism"] = "ignore_local_publications_only"
        with self.assertRaises(probe.ProbeFailure):
            probe.validate_matrix(matrix)

    def test_not_run_blocks_matrix_completion(self) -> None:
        """Require every row to pass before the matrix can complete."""

        matrix = {row_id: passing_row(row_id) for row_id in build.ROWS}
        blocked = copy.deepcopy(matrix["humble-fastrtps"])
        blocked["verdict"] = "NOT RUN"
        blocked["missingPrerequisite"] = "ROS root"
        matrix["humble-fastrtps"] = blocked
        with self.assertRaises(probe.ProbeFailure):
            probe.validate_matrix(matrix)

    def test_owned_process_cleanup_failure_is_recorded_in_the_row_result(self) -> None:
        """Never silently discard failure to terminate or reap an owned child."""

        class UnreapableProcess:
            """Emulate one owned child that cannot be terminated."""

            pid = 4242

            @staticmethod
            def poll():
                """Report that the injected child is still running."""

                return None

            @staticmethod
            def kill() -> None:
                """Fail without exposing the injected operating-system detail."""

                raise OSError(5, "sensitive injected detail")

        diagnostic = probe._terminate_owned(UnreapableProcess())
        self.assertIsNotNone(diagnostic)
        self.assertNotIn("sensitive", diagnostic)

        result = passing_row("jazzy-fastrtps")
        probe._record_cleanup_failures(result, (diagnostic,))

        self.assertEqual("FAIL", result["verdict"])
        self.assertFalse(result["ownedProcesses"]["cleanupComplete"])
        self.assertIn("owned process 4242", result["failure"])


if __name__ == "__main__":
    unittest.main()
