#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

import json
import pathlib
import sys
import tempfile
import unittest


SCRIPT_DIR = pathlib.Path(__file__).resolve().parents[1]
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

import phase186_bridge_build as build


class Phase186BridgeBuildTests(unittest.TestCase):
    def test_exact_maintained_rows_and_no_fastdds_alias(self) -> None:
        self.assertEqual(
            (
                "humble-fastrtps",
                "jazzy-fastrtps",
                "lyrical-fastrtps",
                "lyrical-zenoh",
            ),
            tuple(build.ROWS),
        )
        with self.assertRaises(build.BridgeBuildFailure):
            build.require_row("jazzy-fastdds")

    def test_tracked_phase181_lock_is_exact(self) -> None:
        repository = pathlib.Path(__file__).resolve().parents[4]
        authority = build.load_interface_authority(repository)
        self.assertEqual(build.INTERFACE_TYPE, authority["canonicalType"])
        self.assertEqual(build.INTERFACE_DIGEST, authority["interfaceDigest"])
        self.assertEqual(
            "unity2foxglove_foxrun_interfaces_v1",
            authority["rosPackageName"],
        )

    def test_overlay_authority_rejects_cross_row_and_digest_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            row_root = root / "jazzy-fastrtps"
            install = row_root / "peer-workspace" / "install"
            install.mkdir(parents=True)
            (install / "local_setup.bat").write_text("@echo off\n", encoding="utf-8")
            authority = build.expected_overlay_authority(
                build.require_row("jazzy-fastrtps"),
                row_root,
                install,
                source_digest=build.INTERFACE_DIGEST,
            )
            build.validate_overlay_authority(
                authority,
                build.require_row("jazzy-fastrtps"),
                row_root,
            )

            cross_row = dict(authority, rowId="humble-fastrtps")
            with self.assertRaises(build.BridgeBuildFailure):
                build.validate_overlay_authority(
                    cross_row,
                    build.require_row("jazzy-fastrtps"),
                    row_root,
                )
            drifted = dict(authority, interfaceDigest="0" * 64)
            with self.assertRaises(build.BridgeBuildFailure):
                build.validate_overlay_authority(
                    drifted,
                    build.require_row("jazzy-fastrtps"),
                    row_root,
                )

    def test_missing_live_prerequisite_is_not_run_and_never_pass(self) -> None:
        result = build.not_run_summary(
            build.require_row("jazzy-fastrtps"),
            "missing Visual Studio C++ toolchain",
        )
        self.assertEqual("NOT RUN", result["verdict"])
        self.assertEqual("missing Visual Studio C++ toolchain", result["missingPrerequisite"])
        self.assertNotEqual(0, build.verdict_exit_code(result["verdict"]))

    def test_result_validator_requires_real_build_and_ctest_evidence(self) -> None:
        row = build.require_row("jazzy-fastrtps")
        valid = {
            "schemaVersion": 1,
            "rowId": row.row_id,
            "distro": row.distro,
            "requestedRmw": row.rmw,
            "selectedRmw": row.rmw,
            "verdict": "PASS",
            "platform": "Windows",
            "interfaceDigest": build.INTERFACE_DIGEST,
            "canonicalType": build.INTERFACE_TYPE,
            "overlayAuthority": {"validated": True},
            "commands": {
                "colcon": {"exitCode": 0, "log": "colcon-build.log"},
                "cmakeConfigure": {"exitCode": 0, "log": "cmake-configure.log"},
                "cmakeBuild": {"exitCode": 0, "log": "cmake-build.log"},
                "ctest": {"exitCode": 0, "log": "ctest.log"},
            },
            "ctest": {"tests": 1, "passed": 1},
            "compiler": {"identity": "MSVC"},
            "probeExecutable": {"sha256": "a" * 64},
        }
        build.validate_build_summary(valid, row)
        invalid = json.loads(json.dumps(valid))
        invalid["commands"]["ctest"]["exitCode"] = 1
        with self.assertRaises(build.BridgeBuildFailure):
            build.validate_build_summary(invalid, row)


if __name__ == "__main__":
    unittest.main()
