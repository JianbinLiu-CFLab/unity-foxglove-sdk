#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression tests for the pure Phase186-H acceptance protocol."""

from __future__ import annotations

import pathlib
import tempfile
import unittest

from Scripts.smoke.foxrun import phase186_bridge_acceptance_protocol as protocol


HEAD = "a" * 40
TOKEN = "p186h_0123456789abcdef01234567"
RUN_ID = "phase186h-jazzy-0123456789ab"


class Phase186BridgeAcceptanceProtocolTests(unittest.TestCase):
    """Lock the evidence protocol without launching Unity or ROS."""

    def test_exact_rows_and_manual_cases_are_immutable(self) -> None:
        self.assertEqual(
            (
                "humble-fastrtps",
                "jazzy-fastrtps",
                "lyrical-fastrtps",
                "lyrical-zenoh",
            ),
            tuple(protocol.ROWS),
        )
        self.assertEqual(
            (
                "manual-jazzy-fastrtps-duplex",
                "manual-lyrical-zenoh-duplex",
            ),
            protocol.MANUAL_CASE_IDS,
        )
        self.assertEqual(
            {
                "humble-fastrtps": 160,
                "jazzy-fastrtps": 161,
                "lyrical-fastrtps": 162,
                "lyrical-zenoh": 163,
            },
            {row_id: row.domain_id for row_id, row in protocol.ROWS.items()},
        )
        self.assertTrue(
            all(
                0 <= row.domain_id <= protocol.WINDOWS_SAFE_ROS_DOMAIN_ID_MAX
                for row in protocol.ROWS.values()
            )
        )
        with self.assertRaises(TypeError):
            protocol.ROWS["other"] = protocol.ROWS["jazzy-fastrtps"]

    def test_automatic_cases_cover_every_locked_acceptance_family(self) -> None:
        self.assertEqual(
            {
                "frozen-v1",
                "bridge-source",
                "full-duplex",
                "fanout-fairness-health",
                "reconnect-degraded-recovery",
                "bounds-hostile-peer",
                "lifecycle",
                "slow-main-thread-640hz",
                "product-inspector",
            },
            set(protocol.AUTOMATIC_CASE_IDS),
        )
        for case_id in protocol.AUTOMATIC_CASE_IDS:
            self.assertFalse(protocol.CASES[case_id].manual)
            self.assertTrue(protocol.CASES[case_id].required_actors)

    def test_manual_cases_select_their_exact_rows_and_actors(self) -> None:
        jazzy = protocol.CASES["manual-jazzy-fastrtps-duplex"]
        lyrical = protocol.CASES["manual-lyrical-zenoh-duplex"]
        self.assertEqual("jazzy-fastrtps", jazzy.row_id)
        self.assertEqual("lyrical-zenoh", lyrical.row_id)
        self.assertTrue(jazzy.manual)
        self.assertTrue(lyrical.manual)
        self.assertEqual(
            frozenset({"sidecar", "ros-peer", "graph-observer"}),
            jazzy.required_actors,
        )
        self.assertIn("zenoh-router", lyrical.required_actors)

    def test_topics_are_token_scoped_unique_and_do_not_overlap_old_phases(self) -> None:
        topics = protocol.topics_for_case("full-duplex", TOKEN)
        self.assertEqual(len(topics), len(set(topics)))
        self.assertTrue(all(topic.startswith("/foxrun/phase186/") for topic in topics))
        self.assertTrue(all(TOKEN in topic for topic in topics))
        self.assertFalse(any("phase181" in topic or "phase184" in topic for topic in topics))

    def test_topics_reject_unsafe_or_foreign_tokens(self) -> None:
        for token in ("", "p184g_old", "p186h_slash/value", "p186h_short"):
            with self.subTest(token=token):
                with self.assertRaises(protocol.ProtocolFailure):
                    protocol.topics_for_case("full-duplex", token)

    def test_unknown_case_and_row_aliases_fail_closed(self) -> None:
        with self.assertRaises(protocol.ProtocolFailure):
            protocol.require_case("duplex")
        with self.assertRaises(protocol.ProtocolFailure):
            protocol.require_row("jazzy")

    def test_not_run_requires_a_named_prerequisite_and_is_never_pass(self) -> None:
        value = protocol.make_not_run_summary(
            run_id=RUN_ID,
            token=TOKEN,
            case_id="manual-jazzy-fastrtps-duplex",
            head=HEAD,
            prerequisite="ROS 2 Jazzy Windows root",
            evidence_root=r"D:\repo\build\phase186\acceptance\run",
        )
        validated = protocol.validate_terminal_summary(value)
        self.assertEqual("NOT RUN", validated["verdict"])
        self.assertNotEqual("PASS", validated["verdict"])
        value["missingPrerequisite"] = ""
        with self.assertRaises(protocol.ProtocolFailure):
            protocol.validate_terminal_summary(value)

    def test_pass_requires_exact_actor_evidence_and_complete_cleanup(self) -> None:
        value = protocol.make_pass_summary_for_tests(
            run_id=RUN_ID,
            token=TOKEN,
            case_id="manual-jazzy-fastrtps-duplex",
            head=HEAD,
            evidence_root=r"D:\repo\build\phase186\acceptance\run",
        )
        self.assertEqual("PASS", protocol.validate_terminal_summary(value)["verdict"])
        value["actors"].pop("sidecar")
        with self.assertRaises(protocol.ProtocolFailure):
            protocol.validate_terminal_summary(value)

    def test_pass_binds_the_graph_actor_to_its_ros_peer_process(self) -> None:
        value = protocol.make_pass_summary_for_tests(
            run_id=RUN_ID,
            token=TOKEN,
            case_id="manual-jazzy-fastrtps-duplex",
            head=HEAD,
            evidence_root=r"D:\repo\build\phase186\acceptance\run",
        )
        for actor_name, actor in value["actors"].items():
            actor["processRole"] = actor_name
            actor["cohosted"] = False
        peer = value["actors"]["ros-peer"]
        graph = value["actors"]["graph-observer"]
        for key in (
            "pid",
            "executable",
            "started",
            "ready",
            "identityVerified",
            "exited",
            "exitCode",
            "termination",
        ):
            graph[key] = peer[key]
        graph["processRole"] = "ros-peer"
        graph["cohosted"] = True

        try:
            verdict = protocol.validate_terminal_summary(value)["verdict"]
        except protocol.ProtocolFailure as exc:
            self.fail(f"cohosted graph evidence was rejected: {exc}")
        self.assertEqual("PASS", verdict)

        invalid = protocol.deep_copy_json(value)
        invalid["actors"]["sidecar"]["processRole"] = "ros-peer"
        invalid["actors"]["sidecar"]["cohosted"] = True
        with self.assertRaises(protocol.ProtocolFailure):
            protocol.validate_terminal_summary(invalid)

        mismatched = protocol.deep_copy_json(value)
        mismatched["actors"]["graph-observer"]["pid"] += 1
        with self.assertRaises(protocol.ProtocolFailure):
            protocol.validate_terminal_summary(mismatched)

    def test_owner_requested_windows_exit_codes_accept_signed_and_unsigned_forms(self) -> None:
        for exit_code in (-1073741510, 3221225786, -1073741515, 3221225781):
            with self.subTest(exit_code=exit_code):
                value = protocol.make_pass_summary_for_tests(
                    run_id=RUN_ID,
                    token=TOKEN,
                    case_id="manual-jazzy-fastrtps-duplex",
                    head=HEAD,
                    evidence_root=r"D:\repo\build\phase186\acceptance\run",
                )
                value["actors"]["sidecar"]["termination"] = "owner-requested"
                value["actors"]["sidecar"]["exitCode"] = exit_code
                self.assertEqual(
                    "PASS",
                    protocol.validate_terminal_summary(value)["verdict"],
                )

    def test_pass_rejects_cached_or_configuration_only_evidence(self) -> None:
        value = protocol.make_pass_summary_for_tests(
            run_id=RUN_ID,
            token=TOKEN,
            case_id="manual-jazzy-fastrtps-duplex",
            head=HEAD,
            evidence_root=r"D:\repo\build\phase186\acceptance\run",
        )
        for forbidden_source in ("cached", "configuration", "unit-test", "skipped"):
            with self.subTest(source=forbidden_source):
                candidate = protocol.deep_copy_json(value)
                candidate["observations"]["data"]["source"] = forbidden_source
                with self.assertRaises(protocol.ProtocolFailure):
                    protocol.validate_terminal_summary(candidate)

    def test_pass_rejects_stale_identity_or_incomplete_cleanup(self) -> None:
        value = protocol.make_pass_summary_for_tests(
            run_id=RUN_ID,
            token=TOKEN,
            case_id="manual-jazzy-fastrtps-duplex",
            head=HEAD,
            evidence_root=r"D:\repo\build\phase186\acceptance\run",
        )
        for path, replacement in (
            (("head",), "b" * 39),
            (("tokenHash",), "0" * 64),
            (("cleanup", "complete"), False),
            (("cleanup", "residualProcesses"), [123]),
            (("cleanup", "residualPorts"), [8767]),
        ):
            with self.subTest(path=path):
                candidate = protocol.deep_copy_json(value)
                target = candidate
                for key in path[:-1]:
                    target = target[key]
                target[path[-1]] = replacement
                with self.assertRaises(protocol.ProtocolFailure):
                    protocol.validate_terminal_summary(candidate)

    def test_fail_can_preserve_incomplete_cleanup_evidence(self) -> None:
        value = protocol.make_failure_summary(
            run_id=RUN_ID,
            token=TOKEN,
            case_id="manual-jazzy-fastrtps-duplex",
            head=HEAD,
            evidence_root=r"D:\repo\build\phase186\acceptance\run",
            failure_code="FAIL_CLEANUP",
            failure_message="owned sidecar remained live",
            cleanup={
                "complete": False,
                "cleanupErrors": ["owner close failed"],
                "residualProcesses": [1234],
                "residualPorts": [18767],
                "residualOverlays": [],
                "residualTemporaryProjects": [],
            },
        )
        validated = protocol.validate_terminal_summary(value)
        self.assertEqual("FAIL", validated["verdict"])
        self.assertEqual([1234], validated["cleanup"]["residualProcesses"])

    def test_terminal_line_round_trips_and_rejects_foreign_markers(self) -> None:
        value = protocol.make_not_run_summary(
            run_id=RUN_ID,
            token=TOKEN,
            case_id="manual-jazzy-fastrtps-duplex",
            head=HEAD,
            prerequisite="Unity license",
            evidence_root=r"D:\repo\build\phase186\acceptance\run",
        )
        line = protocol.format_terminal_line(value)
        parsed = protocol.parse_terminal_line(line, RUN_ID, TOKEN, HEAD)
        self.assertEqual("NOT RUN", parsed["verdict"])
        with self.assertRaises(protocol.ProtocolFailure):
            protocol.parse_terminal_line(line, RUN_ID + "x", TOKEN, HEAD)

    def test_manual_completion_marker_requires_exact_current_identity(self) -> None:
        marker = protocol.format_manual_completion_marker(
            case_id="manual-jazzy-fastrtps-duplex",
            run_id=RUN_ID,
            token=TOKEN,
            head=HEAD,
            verdict="PASS",
        )
        parsed = protocol.parse_manual_completion_marker(
            marker,
            case_id="manual-jazzy-fastrtps-duplex",
            run_id=RUN_ID,
            token=TOKEN,
            head=HEAD,
        )
        self.assertEqual("PASS", parsed["verdict"])
        with self.assertRaises(protocol.ProtocolFailure):
            protocol.parse_manual_completion_marker(
                marker.replace(HEAD, "b" * 40),
                case_id="manual-jazzy-fastrtps-duplex",
                run_id=RUN_ID,
                token=TOKEN,
                head=HEAD,
            )

    def test_run_config_is_bounded_to_owned_phase186_output(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            repo = pathlib.Path(temp).resolve()
            project = repo / "Unity2Foxglove"
            project.mkdir()
            output = repo / "build" / "phase186" / "acceptance" / RUN_ID
            output.mkdir(parents=True)
            config = protocol.make_run_config(
                repository=repo,
                project=project,
                output_root=output,
                run_id=RUN_ID,
                token=TOKEN,
                case_id="manual-jazzy-fastrtps-duplex",
                head=HEAD,
                bridge_port=18767,
                domain_id=161,
            )
            self.assertEqual(
                "jazzy-fastrtps",
                protocol.validate_run_config(config, repo)["rowId"],
            )
            config["outputRoot"] = str(repo / "outside")
            with self.assertRaises(protocol.ProtocolFailure):
                protocol.validate_run_config(config, repo)

    def test_run_config_accepts_only_the_exact_owned_bridge_only_project(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            repo = pathlib.Path(temp).resolve()
            (repo / "Unity2Foxglove").mkdir()
            output = repo / "build" / "phase186" / RUN_ID
            project = output / "bridge-only-unity"
            project.mkdir(parents=True)
            config = protocol.make_run_config(
                repository=repo,
                project=project,
                output_root=output,
                run_id=RUN_ID,
                token=TOKEN,
                case_id="full-duplex",
                head=HEAD,
                bridge_port=18767,
                domain_id=161,
            )
            self.assertEqual(
                project,
                pathlib.Path(
                    protocol.validate_run_config(config, repo)["projectPath"]
                ),
            )
            config["projectPath"] = str(output / "foreign-project")
            with self.assertRaises(protocol.ProtocolFailure):
                protocol.validate_run_config(config, repo)

    def test_row_independent_automatic_config_has_no_synthetic_ros_alias(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            repo = pathlib.Path(temp).resolve()
            project = repo / "Unity2Foxglove"
            project.mkdir()
            output = repo / "build" / "phase186" / "acceptance" / RUN_ID
            output.mkdir(parents=True)
            config = protocol.make_run_config(
                repository=repo,
                project=project,
                output_root=output,
                run_id=RUN_ID,
                token=TOKEN,
                case_id="bridge-source",
                head=HEAD,
                bridge_port=18767,
                domain_id=161,
            )
            validated = protocol.validate_run_config(config, repo)
            self.assertIsNone(validated["rowId"])
            self.assertIsNone(validated["distro"])
            self.assertIsNone(validated["rmw"])
            config["rowId"] = "jazzy-fastrtps"
            with self.assertRaises(protocol.ProtocolFailure):
                protocol.validate_run_config(config, repo)

    def test_run_config_rejects_invalid_ports_domains_and_extra_keys(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            repo = pathlib.Path(temp).resolve()
            project = repo / "Unity2Foxglove"
            project.mkdir()
            output = repo / "build" / "phase186" / "acceptance" / RUN_ID
            output.mkdir(parents=True)
            base = protocol.make_run_config(
                repository=repo,
                project=project,
                output_root=output,
                run_id=RUN_ID,
                token=TOKEN,
                case_id="manual-jazzy-fastrtps-duplex",
                head=HEAD,
                bridge_port=18767,
                domain_id=161,
            )
            for key, value in (
                ("bridgePort", 0),
                ("bridgePort", 65536),
                ("domainId", 167),
                ("domainId", 233),
            ):
                with self.subTest(key=key, value=value):
                    candidate = protocol.deep_copy_json(base)
                    candidate[key] = value
                    with self.assertRaises(protocol.ProtocolFailure):
                        protocol.validate_run_config(candidate, repo)
            base["unexpected"] = True
            with self.assertRaises(protocol.ProtocolFailure):
                protocol.validate_run_config(base, repo)


if __name__ == "__main__":
    unittest.main()
