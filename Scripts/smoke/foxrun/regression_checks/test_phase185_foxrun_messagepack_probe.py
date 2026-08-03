#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for the Phase185 typed MessagePack duplex probe.

from __future__ import annotations

import importlib.util
import re
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[4]
PROBE_PATH = (
    ROOT
    / "Scripts"
    / "smoke"
    / "websocket"
    / "phase185_foxrun_messagepack_probe.py"
)
ACCEPTANCE_SOURCES = (
    ROOT
    / "Unity2Foxglove"
    / "Assets"
    / "Scripts"
    / "FullDemoVisualization"
    / "TestLog.MessagePack.cs",
    ROOT
    / "Packages"
    / "dev.unity2foxglove.sdk"
    / "Samples~"
    / "FullDemoVisualization"
    / "Scripts"
    / "TestLog.MessagePack.cs",
)


def load_probe():
    """Load the maintained probe from its CLI path."""
    spec = importlib.util.spec_from_file_location(
        "phase185_foxrun_messagepack_probe_under_test",
        PROBE_PATH,
    )
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class Phase185FoxRunMessagePackProbeTests(unittest.TestCase):
    """Fail-closed protocol and report coverage."""

    def test_canonical_a_and_b_payloads_are_distinct_and_independently_decodable(self) -> None:
        """Canonical A and B remain distinct complete MessagePack payloads."""
        module = load_probe()

        payload_a = module.encode_probe_payload(
            module.REMOTE_SEQUENCE_A,
            module.REMOTE_VALUE_A,
        )
        payload_b = module.encode_probe_payload(
            module.LOCAL_SEQUENCE_B,
            module.LOCAL_VALUE_B,
        )

        self.assertNotEqual(payload_a, payload_b)
        self.assertEqual(
            {
                "messagePackSequence": module.REMOTE_SEQUENCE_A,
                "messagePackValue": module.REMOTE_VALUE_A,
            },
            module.decode_complete_msgpack(payload_a),
        )
        self.assertEqual(
            {
                "messagePackSequence": module.LOCAL_SEQUENCE_B,
                "messagePackValue": module.LOCAL_VALUE_B,
            },
            module.decode_complete_msgpack(payload_b),
        )

    def test_catalog_selection_is_subscribe_only_and_requires_empty_wire_schema(self) -> None:
        """Catalog selection requires Subscribe availability and no wire schema."""
        module = load_probe()
        output_row = {
            "topic": module.PROBE_TOPIC,
            "flow": "Publish",
            "encoding": "protobuf",
            "schemaName": "wrong.output",
            "wireSchemaName": "wrong.output",
            "logicalSchemaName": "Wrong.Output",
            "subscribeAvailable": True,
        }
        input_row = {
            "topic": module.PROBE_TOPIC,
            "flow": "Subscribe",
            "encoding": "msgpack",
            "schemaName": "",
            "wireSchemaName": "",
            "logicalSchemaName": "TestLog",
            "subscribeAvailable": True,
        }

        selected = module.select_catalog_contract(
            {"subscriptionsEnabled": True, "contracts": [output_row, input_row]}
        )

        self.assertIs(input_row, selected)
        with self.assertRaises(module.ProbeFailure):
            module.select_catalog_contract(
                {
                    "subscriptionsEnabled": True,
                    "contracts": [{**input_row, "schemaName": "not-empty"}],
                }
            )

    def test_terminal_report_requires_apply_no_output_and_distinct_single_b(self) -> None:
        """The PASS report proves apply, quiet input, and exactly one later B."""
        module = load_probe()
        payload_a = module.encode_probe_payload(
            module.REMOTE_SEQUENCE_A,
            module.REMOTE_VALUE_A,
        )
        payload_b = module.encode_probe_payload(
            module.LOCAL_SEQUENCE_B,
            module.LOCAL_VALUE_B,
        )

        report = module.build_pass_report(
            contract={
                "topic": module.PROBE_TOPIC,
                "encoding": "msgpack",
                "schemaName": "",
                "wireSchemaName": "",
                "logicalSchemaName": "TestLog",
            },
            payload_a=payload_a,
            payload_b=payload_b,
            decoded_b=module.decode_complete_msgpack(payload_b),
            no_output_seconds=module.NO_OUTPUT_WINDOW_SECONDS,
            malformed_rejections=3,
            recovery_applied=True,
        )

        self.assertEqual("PASS", report["verdict"])
        self.assertEqual("output", report["canonicalOutput"]["direction"])
        self.assertEqual(payload_b.hex(), report["canonicalOutput"]["payloadHex"])
        self.assertNotEqual(
            report["remoteInput"]["payloadHex"],
            report["canonicalOutput"]["payloadHex"],
        )
        self.assertTrue(report["noImmediateMirror"]["complete"])

    def test_exactly_once_acceptance_topics_do_not_enable_change_heartbeats(self) -> None:
        """The controlled B and evidence values stay quiet after their change."""
        declaration_pattern = re.compile(
            r"\[FoxRun\((?P<arguments>.*?)\)\]\s*"
            r"private int _messagePack(?:Sequence|Value|AppliedSequence|AppliedValue);",
            re.DOTALL,
        )
        heartbeat_pattern = re.compile(
            r"\bHz\s*=\s*(?P<hz>[+-]?(?:\d+(?:\.\d*)?|\.\d+))f?\b"
        )

        for source_path in ACCEPTANCE_SOURCES:
            source = source_path.read_text(encoding="utf-8")
            declarations = declaration_pattern.findall(source)
            self.assertEqual(4, len(declarations), source_path)
            for arguments in declarations:
                heartbeat = heartbeat_pattern.search(arguments)
                if heartbeat is not None:
                    self.assertLessEqual(
                        float(heartbeat.group("hz")),
                        0.0,
                        f"{source_path} enables a Change heartbeat incompatible "
                        "with the probe's exactly-once quiet windows.",
                    )


if __name__ == "__main__":
    unittest.main()
