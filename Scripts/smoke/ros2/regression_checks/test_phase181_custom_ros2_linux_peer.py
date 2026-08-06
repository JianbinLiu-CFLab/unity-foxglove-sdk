#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the Phase181 caller-owned Linux peer path."""

from __future__ import annotations

import importlib.util
import os
import pathlib
import sys
import unittest
from types import SimpleNamespace

from Scripts.test_support.phase181_scratch import temporary_directory


ROOT = pathlib.Path(__file__).resolve().parents[4]
MODULE_PATH = ROOT / "Scripts" / "smoke" / "ros2" / "phase181_custom_ros2_linux_peer.py"


def load_linux_peer_module():
    """Load the Phase181 module under test."""
    script_directory = str(MODULE_PATH.parent)
    if script_directory not in sys.path:
        sys.path.insert(0, script_directory)
    spec = importlib.util.spec_from_file_location("phase181_custom_ros2_linux_peer", MODULE_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("Could not load the Phase181 Linux custom ROS2 peer module.")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class Phase181CustomRos2LinuxPeerTests(unittest.TestCase):
    """Keep Linux proof caller-owned, exact-source, and role-specific."""

    def test_linux_environment_requires_the_already_sourced_profile_without_fallback(self):
        """Verify Phase181 behavior: linux environment requires the already sourced profile without fallback."""
        linux = load_linux_peer_module()
        args = SimpleNamespace(distro="lyrical", rmw="rmw_zenoh_cpp", domain_id=19, discovery_range="SUBNET")

        environment = linux.build_linux_environment(
            args,
            {"ROS_VERSION": "2", "ROS_DISTRO": "lyrical", "RMW_IMPLEMENTATION": "rmw_zenoh_cpp", "TOKEN": "not-persisted"},
        )

        self.assertEqual("19", environment["ROS_DOMAIN_ID"])
        self.assertEqual("SUBNET", environment["ROS_AUTOMATIC_DISCOVERY_RANGE"])
        self.assertEqual("lyrical", environment["ROS_DISTRO"])
        self.assertNotIn("TOKEN", environment)
        with self.assertRaisesRegex(linux.LinuxPeerFailure, "FAIL_ENVIRONMENT"):
            linux.build_linux_environment(
                args,
                {"ROS_VERSION": "2", "ROS_DISTRO": "jazzy", "RMW_IMPLEMENTATION": "rmw_zenoh_cpp"},
            )

    def test_linux_stage_preserves_a_matching_caller_owned_source_and_rejects_drift(self):
        """Verify Phase181 behavior: linux stage preserves a matching caller owned source and rejects drift."""
        linux = load_linux_peer_module()
        with temporary_directory("linux-peer-") as temporary:
            root = pathlib.Path(temporary)
            static_package = root / "static"
            source = static_package / "Ros2Package~"
            (source / "msg").mkdir(parents=True)
            (source / "msg" / "State.msg").write_text("int32 value\n", encoding="utf-8")
            (source / "package.xml").write_text("<package/>\n", encoding="utf-8")
            (source / "CMakeLists.txt").write_text("cmake_minimum_required(VERSION 3.8)\n", encoding="utf-8")
            workspace = root / "caller-owned-workspace"
            workspace.mkdir()

            staged = linux.stage_or_verify_locked_ros_source(static_package, workspace, "example_interfaces")

            self.assertEqual(workspace / "src" / "example_interfaces", staged)
            self.assertEqual("int32 value\n", (staged / "msg" / "State.msg").read_text(encoding="utf-8"))
            self.assertEqual(staged, linux.stage_or_verify_locked_ros_source(static_package, workspace, "example_interfaces"))
            (staged / "msg" / "State.msg").write_text("int32 drift\n", encoding="utf-8")
            with self.assertRaisesRegex(linux.LinuxPeerFailure, "FAIL_PEER_SOURCE"):
                linux.stage_or_verify_locked_ros_source(static_package, workspace, "example_interfaces")

    def test_linux_role_command_uses_the_same_generated_worker_protocol(self):
        """Verify Phase181 behavior: linux role command uses the same generated worker protocol."""
        linux = load_linux_peer_module()
        command = linux.build_linux_worker_command(
            pathlib.Path("/opt/ros/lyrical/bin/python3"),
            workspace=pathlib.Path("/tmp/phase181-workspace"),
            interface_digest="a" * 64,
            role="bidirectional",
            unity_log=pathlib.Path("/tmp/unity.log"),
            result_json=pathlib.Path("/tmp/result.json"),
            distro="lyrical",
            rmw="rmw_zenoh_cpp",
            domain_id=19,
            surface="player",
        )

        self.assertIn("--worker", command)
        self.assertEqual("linux-peer", command[command.index("--role") + 1])
        self.assertEqual("bidirectional", command[command.index("--probe-role") + 1])
        self.assertEqual("player", command[command.index("--surface") + 1])
        self.assertNotIn("ros2", command)

    def test_linux_worker_environment_prepends_the_merged_install_python_and_prefix_paths(self):
        """Verify Phase181 behavior: linux worker environment prepends the merged install python and prefix paths."""
        linux = load_linux_peer_module()
        with temporary_directory("linux-peer-") as temporary:
            install = pathlib.Path(temporary) / "install"
            site_packages = install / "lib" / "python3.12" / "site-packages"
            (install / "bin").mkdir(parents=True)
            site_packages.mkdir(parents=True)

            environment = linux.build_linux_worker_environment(
                {
                    "AMENT_PREFIX_PATH": "/opt/ros/lyrical",
                    "CMAKE_PREFIX_PATH": "/opt/ros/lyrical",
                    "COLCON_PREFIX_PATH": "/opt/ros/lyrical",
                    "PYTHONPATH": "/existing/python",
                    "PATH": "/existing/bin",
                },
                install,
            )

            self.assertEqual(
                os.pathsep.join((str(install), "/opt/ros/lyrical")),
                environment["AMENT_PREFIX_PATH"],
            )
            self.assertEqual(
                os.pathsep.join((str(site_packages), "/existing/python")),
                environment["PYTHONPATH"],
            )
            self.assertEqual(
                os.pathsep.join((str(install / "bin"), "/existing/bin")),
                environment["PATH"],
            )

    def test_linux_parser_exposes_distinct_publisher_subscriber_bidirectional_and_orchestrate_roles(self):
        """Verify Phase181 behavior: linux parser exposes distinct publisher subscriber bidirectional and orchestrate roles."""
        linux = load_linux_peer_module()
        with temporary_directory("linux-peer-") as temporary:
            workspace = pathlib.Path(temporary)
            for role in ("correlate", "publisher", "subscriber", "bidirectional", "orchestrate"):
                args = linux.parse_args(
                    [
                        "--role",
                        role,
                        "--profile-id",
                        "linux-test",
                        "--surface",
                        "editor",
                        "--distro",
                        "lyrical",
                        "--rmw",
                        "rmw_fastrtps_cpp",
                        "--workspace",
                        str(workspace),
                        "--unity-log",
                        str(workspace / "unity.log"),
                    ]
                )
                self.assertEqual(role, args.role)


if __name__ == "__main__":
    unittest.main()
