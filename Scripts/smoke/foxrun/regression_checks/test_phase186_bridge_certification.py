#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression tests for the complete Phase186-H certification matrix."""

from __future__ import annotations

import pathlib
import tempfile
import unittest

from Scripts.smoke.foxrun import phase186_bridge_acceptance as acceptance
from Scripts.smoke.foxrun import phase186_bridge_acceptance_protocol as protocol
from Scripts.smoke.foxrun import phase186_bridge_certification as certification
from Scripts.smoke.foxrun import phase186_bridge_project as bridge_project


HEAD = "a" * 40


class Phase186BridgeCertificationTests(unittest.TestCase):
    def test_serial_matrix_runs_nine_cases_and_all_four_full_duplex_rows(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            invocations = certification.live_invocations(pathlib.Path(temp), HEAD)
        self.assertEqual(12, len(invocations))
        self.assertEqual(
            protocol.AUTOMATIC_CASE_IDS,
            tuple(item.case_id for item in invocations[:9]),
        )
        self.assertEqual(
            set(protocol.ROWS),
            {
                item.row_id
                for item in invocations
                if item.case_id == "full-duplex"
            },
        )
        self.assertEqual(len(invocations), len({item.run_id for item in invocations}))

    def test_exact_rows_use_bridge_only_unity_and_fanout_uses_all_providers(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            invocations = certification.live_invocations(pathlib.Path(temp), HEAD)
        for item in invocations:
            expected = acceptance.unity_composition_for_case(item.case_id)
            if item.case_id == "full-duplex":
                self.assertEqual("bridge-only", expected)
        fanout = next(
            item for item in invocations if item.case_id == "fanout-fairness-health"
        )
        self.assertEqual(
            "repository-all-providers",
            acceptance.unity_composition_for_case(fanout.case_id),
        )

    def test_certification_ids_are_bounded_and_head_correlated(self) -> None:
        run_id = certification.certification_run_id(HEAD)
        self.assertRegex(run_id, certification._CERT_RUN_ID)
        self.assertIn(HEAD[:6], run_id)
        with self.assertRaises(certification.CertificationFailure):
            certification.certification_run_id(HEAD, "unsafe")

    def test_serial_case_paths_leave_windows_unity_search_lmdb_headroom(self) -> None:
        repository = pathlib.Path(
            r"D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox"
        )
        certification_root = (
            repository
            / "build"
            / "phase186"
            / "windows-live"
            / "phase186h-cert-aaaaaaaaaaaa"
        )
        invocations = certification.live_invocations(certification_root, HEAD)
        for item in invocations:
            search_asset_db = (
                protocol.owned_unity_project_path(repository, item.run_id)
                / bridge_project._UNITY_LMDB_RELATIVE_PATH
            )
            self.assertLessEqual(
                len(str(search_asset_db)),
                bridge_project.MAX_WINDOWS_UNITY_LMDB_PATH,
                str(search_asset_db),
            )


if __name__ == "__main__":
    unittest.main()
