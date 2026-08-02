#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

import hashlib
import json
import pathlib
import sys
import tempfile
import unittest
from unittest import mock


SCRIPT_DIR = pathlib.Path(__file__).resolve().parents[1]
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

import phase186_bridge_build as build


class Phase186BridgeBuildTests(unittest.TestCase):
    """Regression coverage for the Phase186 Bridge build evidence contract."""

    def test_exact_maintained_rows_and_no_fastdds_alias(self) -> None:
        """Keep the maintained row IDs exact and reject the FastDDS alias."""

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

    def test_run_logged_interrupt_terminates_owned_tree_and_retains_partial_log(self) -> None:
        process = mock.Mock()
        process.pid = 18602
        process.communicate.side_effect = [KeyboardInterrupt(), ("partial output\n", None)]
        owner = mock.Mock()
        job = mock.Mock()
        with tempfile.TemporaryDirectory() as temp, \
                mock.patch.object(build.subprocess, "Popen", return_value=process), \
                mock.patch.object(build, "_new_process_owner", return_value=(job, owner)):
            log = pathlib.Path(temp) / "owned.log"
            with self.assertRaises(KeyboardInterrupt):
                build.run_logged(
                    ["fake.exe", "--work"],
                    cwd=pathlib.Path(temp),
                    env={},
                    log_path=log,
                    timeout_seconds=30.0,
                )
            retained = log.read_text(encoding="utf-8")

        owner.register.assert_called_once_with("command", process)
        owner.close.assert_called_once_with()
        self.assertEqual("partial output\n", retained)

    def test_direct_script_bootstrap_can_import_process_owner(self) -> None:
        probe = """
import importlib
import pathlib
import sys

script_directory = pathlib.Path(sys.argv[1]).resolve()
sys.path.insert(0, str(script_directory))
importlib.import_module("phase186_bridge_build")
"""
        with tempfile.TemporaryDirectory() as temp:
            completed = build.subprocess.run(
                [sys.executable, "-I", "-c", probe, str(SCRIPT_DIR)],
                cwd=temp,
                capture_output=True,
                text=True,
                check=False,
            )

        self.assertEqual(0, completed.returncode, completed.stderr)

    def test_run_logged_interrupt_during_registration_still_retains_output(self) -> None:
        process = mock.Mock()
        process.pid = 18603
        process.communicate.return_value = ("registration output\n", None)
        owner = mock.Mock()
        owner.register.side_effect = KeyboardInterrupt
        with tempfile.TemporaryDirectory() as temp, \
                mock.patch.object(build.subprocess, "Popen", return_value=process), \
                mock.patch.object(build, "_new_process_owner", return_value=(mock.Mock(), owner)):
            log = pathlib.Path(temp) / "owned.log"
            with self.assertRaises(KeyboardInterrupt):
                build.run_logged(
                    ["fake.exe"],
                    cwd=pathlib.Path(temp),
                    env={},
                    log_path=log,
                    timeout_seconds=30.0,
                )

            self.assertEqual(
                "registration output\n",
                log.read_text(encoding="utf-8"),
            )
        owner.close.assert_called_once_with()

    def test_generated_duplex_uses_modern_rosidl_targets_with_legacy_fallback(self) -> None:
        """Keep Lyrical target exports and older ament dependency macros buildable."""

        repository = pathlib.Path(__file__).resolve().parents[4]
        cmake = (
            repository
            / "Tools"
            / "ros2_bridge"
            / "unity2foxglove_ros2_bridge"
            / "CMakeLists.txt"
        ).read_text(encoding="utf-8")

        self.assertIn(
            "if(TARGET foxglove_msgs::foxglove_msgs AND TARGET "
            "unity2foxglove_foxrun_interfaces_v1::"
            "unity2foxglove_foxrun_interfaces_v1)",
            cmake,
        )
        self.assertIn("elseif(COMMAND ament_target_dependencies)", cmake)
        self.assertIn(
            'message(FATAL_ERROR "No supported ROS interface dependency target API")',
            cmake,
        )

    def test_tracked_phase181_lock_is_exact(self) -> None:
        """Require the tracked Phase181 interface authority to match exactly."""

        repository = pathlib.Path(__file__).resolve().parents[4]
        authority = build.load_interface_authority(repository)
        self.assertEqual(build.INTERFACE_TYPE, authority["canonicalType"])
        self.assertEqual(build.INTERFACE_DIGEST, authority["interfaceDigest"])
        self.assertEqual(
            "unity2foxglove_foxrun_interfaces_v1",
            authority["rosPackageName"],
        )

    def test_generated_standard_schema_authority_and_overlay_source_are_exact(self) -> None:
        """Build the live standard leg only from the tracked generated-schema authority."""

        repository = pathlib.Path(__file__).resolve().parents[4]
        authority = build.load_standard_schema_authority(repository)
        self.assertEqual(
            "foxglove_msgs/msg/Log",
            authority["canonicalType"],
        )
        self.assertEqual(
            "1cacf4b47ef1c6306f00c673ed283837f80c9f1b67ffa8ecf3a0929f62e6c5fd",
            authority["sourceDigest"],
        )

        with tempfile.TemporaryDirectory() as temp:
            destination = build.stage_standard_schema_package(
                repository,
                pathlib.Path(temp),
            )
            self.assertEqual("foxglove_msgs", destination.name)
            self.assertEqual(
                authority["sourceBytes"],
                (destination / "msg" / "Log.msg").read_bytes(),
            )
            self.assertTrue((destination / "package.xml").is_file())
            self.assertTrue((destination / "CMakeLists.txt").is_file())

    def test_overlay_colcon_command_selects_phase181_and_generated_standard(self) -> None:
        """The exact external overlay must build both live duplex schema families."""

        command = build.build_overlay_colcon_command(
            pathlib.Path("colcon.exe"),
            pathlib.Path("python.exe"),
        )
        selected = command.index("--packages-select")
        self.assertEqual(
            [
                build.ROS_PACKAGE_NAME,
                build.STANDARD_ROS_PACKAGE_NAME,
            ],
            command[selected + 1 : selected + 3],
        )

    def test_overlay_cache_and_installed_schema_are_bound_to_standard_bytes(self) -> None:
        """A stale standard-schema overlay must neither reuse nor validate."""

        peer_key = "a" * 64
        first_digest = "b" * 64
        second_digest = "c" * 64
        self.assertNotEqual(
            build.overlay_build_cache_key(peer_key, first_digest),
            build.overlay_build_cache_key(peer_key, second_digest),
        )

        with tempfile.TemporaryDirectory() as temp:
            install = pathlib.Path(temp)
            package = install / "share" / build.STANDARD_ROS_PACKAGE_NAME
            message = package / "msg" / "Log.msg"
            message.parent.mkdir(parents=True)
            (package / "package.xml").write_text("<package/>", encoding="utf-8")
            source = b"builtin_interfaces/Time timestamp\n"
            message.write_bytes(source)
            digest = hashlib.sha256(source).hexdigest()
            build.validate_installed_standard_schema(install, digest)
            message.write_bytes(source + b"uint8 level\n")
            with self.assertRaises(build.BridgeBuildFailure):
                build.validate_installed_standard_schema(install, digest)

    def test_cpp_runtime_paths_keep_evidence_physical_and_compile_through_alias(self) -> None:
        """Use the short row alias for CMake and overlay includes, not evidence paths."""

        physical = pathlib.Path(r"D:\deep\phase186\jazzy-fastrtps")
        runtime = pathlib.Path("Z:/")
        physical_build, runtime_build, runtime_install = build.cpp_runtime_paths(
            physical,
            runtime,
        )
        self.assertEqual(physical / "cpp-build", physical_build)
        self.assertEqual(runtime / "cpp-build", runtime_build)
        self.assertEqual(
            runtime / "peer-workspace" / "install",
            runtime_install,
        )

    def test_cmake_build_is_reset_when_subst_alias_changes(self) -> None:
        """A reusable CMake tree must not retain a different temporary drive."""

        with tempfile.TemporaryDirectory() as temp:
            physical_build = pathlib.Path(temp) / "row" / "cpp-build"
            physical_build.mkdir(parents=True)
            (physical_build / "CMakeCache.txt").write_text(
                "CMAKE_CACHEFILE_DIR:INTERNAL=Y:/cpp-build\n",
                encoding="utf-8",
            )
            (physical_build / "stale.obj").write_bytes(b"stale")

            changed = build.reset_cmake_build_for_runtime_alias(
                physical_build,
                pathlib.Path("Z:/cpp-build"),
            )

            self.assertTrue(changed)
            self.assertFalse(physical_build.exists())

    def test_cmake_build_is_reused_for_same_alias_ignoring_case_and_slashes(self) -> None:
        """Do not rebuild an already valid row merely for path spelling."""

        with tempfile.TemporaryDirectory() as temp:
            physical_build = pathlib.Path(temp) / "row" / "cpp-build"
            physical_build.mkdir(parents=True)
            (physical_build / "CMakeCache.txt").write_text(
                "CMAKE_CACHEFILE_DIR:INTERNAL=z:/cpp-build\n",
                encoding="utf-8",
            )
            sentinel = physical_build / "compiled.exe"
            sentinel.write_bytes(b"compiled")

            changed = build.reset_cmake_build_for_runtime_alias(
                physical_build,
                pathlib.Path(r"Z:\cpp-build"),
            )

            self.assertFalse(changed)
            self.assertEqual(b"compiled", sentinel.read_bytes())

    def test_overlay_authority_rejects_cross_row_and_digest_drift(self) -> None:
        """Reject overlay evidence copied across rows or carrying digest drift."""

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
                standard_source_digest=build.STANDARD_SCHEMA_DIGEST,
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
        """Classify missing live prerequisites as blocking NOT RUN evidence."""

        result = build.not_run_summary(
            build.require_row("jazzy-fastrtps"),
            "missing Visual Studio C++ toolchain",
        )
        self.assertEqual("NOT RUN", result["verdict"])
        self.assertEqual("missing Visual Studio C++ toolchain", result["missingPrerequisite"])
        self.assertNotEqual(0, build.verdict_exit_code(result["verdict"]))

    def test_result_validator_requires_real_build_and_ctest_evidence(self) -> None:
        """Require successful configure, build, and CTest evidence for PASS."""

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
            "standardCanonicalType": build.STANDARD_SCHEMA_TYPE,
            "standardSchemaDigest": build.STANDARD_SCHEMA_DIGEST,
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
            "generatedDuplexProbe": {"sha256": "b" * 64},
        }
        build.validate_build_summary(valid, row)
        invalid = json.loads(json.dumps(valid))
        invalid["commands"]["ctest"]["exitCode"] = 1
        with self.assertRaises(build.BridgeBuildFailure):
            build.validate_build_summary(invalid, row)


if __name__ == "__main__":
    unittest.main()
