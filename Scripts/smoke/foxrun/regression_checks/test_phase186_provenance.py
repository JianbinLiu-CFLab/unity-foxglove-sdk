#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the Phase186 provenance and pre-move inventory gate."""

from __future__ import annotations

import importlib.util
import json
import pathlib
import sys
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[4]
MODULE_PATH = ROOT / "Scripts/smoke/foxrun/phase186_provenance.py"
LEDGER_PATH = (
    ROOT
    / "Tools"
    / "ros2_bridge"
    / "unity2foxglove_ros2_bridge"
    / "PROVENANCE.json"
)
INVENTORY_PATH = (
    ROOT
    / "Packages"
    / "dev.unity2foxglove.sdk"
    / "Tests"
    / "Unit"
    / "Phase186"
    / "Fixtures"
    / "pre_move_sdk_ros_inventory.json"
)


def load_module():
    """Load the provenance gate from its repository path."""

    spec = importlib.util.spec_from_file_location("phase186_provenance", MODULE_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("Could not load the Phase186 provenance gate.")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class Phase186ProvenanceTests(unittest.TestCase):
    """Lock fail-closed provenance and exact pre-move inventory behavior."""

    def test_repository_ledger_and_pre_move_inventory_are_current(self) -> None:
        """The checked-in ledgers must describe the current pre-extraction tree."""

        module = load_module()
        reference = ROOT / "third-party" / "ROS-TCP-Connector"
        provenance_errors = module.validate_repository_provenance(
            ROOT,
            reference,
            LEDGER_PATH,
        )
        inventory_errors = module.validate_pre_move_inventory(
            ROOT,
            INVENTORY_PATH,
        )
        self.assertEqual([], provenance_errors)
        self.assertEqual([], inventory_errors)

    def test_unexplained_distinctive_copy_is_rejected(self) -> None:
        """Four substantial consecutive upstream lines require an explicit ledger entry."""

        module = load_module()
        distinctive = "\n".join(
            [
                "private readonly Queue<OutgoingMessage> pendingMessages;",
                "public void QueueMessage(string topic, byte[] payload)",
                "pendingMessages.Enqueue(new OutgoingMessage(topic, payload));",
                "SignalSenderThreadWithoutBlockingTheUnityMainThread();",
            ]
        )
        payload = {
            "schemaVersion": 1,
            "reference": {
                "repository": "https://github.com/Unity-Technologies/ROS-TCP-Connector.git",
                "revision": "a" * 40,
                "license": "Apache-2.0",
                "inspectedFiles": ["Runtime/OutgoingMessageSender.cs"],
            },
            "implementations": [
                {
                    "path": "Runtime/Original.cs",
                    "classification": "original",
                    "influence": "No upstream implementation reused.",
                }
            ],
        }
        errors = module.validate_ledger_payload(
            payload,
            actual_revision="a" * 40,
            implementation_sources={"Runtime/Original.cs": distinctive},
            reference_sources={"Runtime/OutgoingMessageSender.cs": distinctive},
        )
        self.assertTrue(
            any("unexplained distinctive overlap" in error for error in errors),
            errors,
        )

    def test_revision_drift_and_material_copy_without_notice_are_rejected(self) -> None:
        """Revision identity and license notice requirements are fail-closed."""

        module = load_module()
        payload = {
            "schemaVersion": 1,
            "reference": {
                "repository": "https://github.com/Unity-Technologies/ROS-TCP-Connector.git",
                "revision": "a" * 40,
                "license": "Apache-2.0",
                "inspectedFiles": ["Runtime/ROSConnection.cs"],
            },
            "implementations": [
                {
                    "path": "Runtime/Derived.cs",
                    "classification": "materially_copied",
                    "influence": "Copied implementation.",
                }
            ],
        }
        errors = module.validate_ledger_payload(
            payload,
            actual_revision="b" * 40,
            implementation_sources={"Runtime/Derived.cs": "internal sealed class Derived {}"},
            reference_sources={"Runtime/ROSConnection.cs": "internal sealed class Reference {}"},
        )
        self.assertTrue(any("revision mismatch" in error for error in errors), errors)
        self.assertTrue(any("licenseNotice" in error for error in errors), errors)

    def test_inventory_digest_changes_when_a_scoped_path_changes(self) -> None:
        """The compact inventory digest represents the complete sorted path set."""

        module = load_module()
        first = module.path_inventory_digest(["Runtime/B.cs", "Runtime/A.cs"])
        second = module.path_inventory_digest(
            ["Runtime/B.cs", "Runtime/A.cs", "Runtime/C.cs"]
        )
        self.assertEqual(
            module.path_inventory_digest(["Runtime/A.cs", "Runtime/B.cs"]),
            first,
        )
        self.assertNotEqual(first, second)


if __name__ == "__main__":
    unittest.main()
