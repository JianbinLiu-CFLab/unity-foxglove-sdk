#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression tests for the complete Phase186-H certification matrix."""

from __future__ import annotations

import pathlib
import tempfile
import unittest
from unittest import mock

from Scripts.smoke.foxrun import phase186_bridge_acceptance as acceptance
from Scripts.smoke.foxrun import phase186_bridge_acceptance_protocol as protocol
from Scripts.smoke.foxrun import phase186_bridge_certification as certification
from Scripts.smoke.foxrun import phase186_bridge_project as bridge_project


HEAD = "a" * 40


class Phase186BridgeCertificationTests(unittest.TestCase):
    """Group checks for phase186 bridge certification tests."""
    def test_serial_matrix_runs_nine_cases_and_all_four_duplex_and_reconnect_rows(self) -> None:
        """Verify every exact runtime row covers duplex and reconnect behavior."""
        with tempfile.TemporaryDirectory() as temp:
            invocations = certification.live_invocations(pathlib.Path(temp), HEAD)
        self.assertEqual(15, len(invocations))
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
        self.assertEqual(
            set(protocol.ROWS),
            {
                item.row_id
                for item in invocations
                if item.case_id == "reconnect-degraded-recovery"
            },
        )
        self.assertEqual(len(invocations), len({item.run_id for item in invocations}))

    def test_exact_rows_use_bridge_only_unity_and_fanout_uses_all_providers(self) -> None:
        """Verify that exact rows use bridge only unity and fanout uses all providers."""
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
        """Verify that certification ids are bounded and head correlated."""
        run_id = certification.certification_run_id(HEAD)
        self.assertRegex(run_id, certification._CERT_RUN_ID)
        self.assertIn(HEAD[:6], run_id)
        with self.assertRaises(certification.CertificationFailure):
            certification.certification_run_id(HEAD, "unsafe")

    def test_serial_case_paths_leave_windows_unity_search_lmdb_headroom(self) -> None:
        """Verify that serial case paths leave windows unity search lmdb headroom."""
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

    def test_timeout_terminates_owned_windows_process_tree(self) -> None:
        """Verify bounded certification timeouts terminate descendants, not only the root PID."""
        class TimedOutProcess:
            pid = 4242

            def __init__(self) -> None:
                self.wait_calls = 0

            def wait(self, timeout: float | None = None) -> int:
                self.wait_calls += 1
                if self.wait_calls == 1:
                    raise certification.subprocess.TimeoutExpired(["owned"], timeout or 0)
                return -9

            def poll(self) -> int | None:
                return None

            def kill(self) -> None:
                raise AssertionError("tree cleanup must not fall back to root-only kill")

        process = TimedOutProcess()
        with tempfile.TemporaryDirectory() as temp, mock.patch.object(
            certification.os, "name", "nt"
        ), mock.patch.object(
            certification.subprocess, "Popen", return_value=process
        ) as popen, mock.patch.object(certification.subprocess, "run") as run:
            with self.assertRaises(certification.CertificationFailure):
                certification._run_logged(
                    ["owned"],
                    repository=pathlib.Path(temp),
                    log=pathlib.Path(temp) / "owned.log",
                    timeout_seconds=0.01,
                )
        run.assert_called_once_with(
            ["taskkill", "/PID", "4242", "/T", "/F"],
            check=False,
            stdout=certification.subprocess.DEVNULL,
            stderr=certification.subprocess.DEVNULL,
        )
        self.assertEqual(2, process.wait_calls)
        self.assertEqual(
            int(getattr(certification.subprocess, "CREATE_NEW_PROCESS_GROUP", 0)),
            popen.call_args.kwargs["creationflags"],
        )


if __name__ == "__main__":
    unittest.main()
