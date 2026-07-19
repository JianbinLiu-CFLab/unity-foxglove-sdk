#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the four no-argument Phase181 Windows-local wrappers."""

from __future__ import annotations

import importlib.util
import json
import pathlib
import sys
import unittest
from unittest import mock

from Scripts.test_support.phase181_scratch import temporary_directory


ROOT = pathlib.Path(__file__).resolve().parents[4]
MODULE_PATH = ROOT / "Scripts" / "smoke" / "ros2" / "phase181_custom_ros2_matrix_profiles.py"


def load_profiles_module():
    """Load the Phase181 module under test."""
    script_directory = str(MODULE_PATH.parent)
    if script_directory not in sys.path:
        sys.path.insert(0, script_directory)
    spec = importlib.util.spec_from_file_location("phase181_custom_ros2_matrix_profiles", MODULE_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("Could not load the Phase181 profile module.")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class Phase181CustomRos2MatrixProfileTests(unittest.TestCase):
    """Keep each convenience wrapper pinned to exactly one real acceptance row."""

    def test_no_argument_wrappers_pin_the_four_supported_distribution_rmw_rows(self):
        """Verify Phase181 behavior: no argument wrappers pin the four supported distribution rmw rows."""
        profiles = load_profiles_module()
        expected = {
            "humble-fastrtps": ("humble", "rmw_fastrtps_cpp"),
            "jazzy-fastrtps": ("jazzy", "rmw_fastrtps_cpp"),
            "lyrical-fastrtps": ("lyrical", "rmw_fastrtps_cpp"),
            "lyrical-zenoh": ("lyrical", "rmw_zenoh_cpp"),
        }

        for profile_id, (distro, rmw) in expected.items():
            argv = profiles.profile_wrapper_argv(profile_id, [])
            self.assertEqual("windows-local-editor", argv[argv.index("--role") + 1])
            self.assertEqual(profile_id, argv[argv.index("--profile-id") + 1])
            self.assertEqual(distro, argv[argv.index("--distro") + 1])
            self.assertEqual(rmw, argv[argv.index("--rmw") + 1])
            self.assertEqual("300", argv[argv.index("--ready-timeout-seconds") + 1])

    def test_only_the_lyrical_zenoh_profile_owns_a_default_router(self):
        """Verify Phase181 behavior: only the lyrical zenoh profile owns a default router."""
        profiles = load_profiles_module()

        self.assertFalse(profiles.profile_owns_default_router("humble-fastrtps"))
        self.assertFalse(profiles.profile_owns_default_router("jazzy-fastrtps"))
        self.assertFalse(profiles.profile_owns_default_router("lyrical-fastrtps"))
        self.assertTrue(profiles.profile_owns_default_router("lyrical-zenoh"))

    def test_each_profile_has_one_stable_windows_local_editor_pass_name(self):
        """Verify Phase181 behavior: each profile has one stable windows local editor pass name."""
        profiles = load_profiles_module()

        self.assertEqual(
            "PHASE181_HUMBLE_FASTRTPS_WINDOWS_LOCAL_EDITOR_PASS",
            profiles.profile_success_verdict("humble-fastrtps"),
        )
        self.assertEqual(
            "PHASE181_JAZZY_FASTRTPS_WINDOWS_LOCAL_EDITOR_PASS",
            profiles.profile_success_verdict("jazzy-fastrtps"),
        )
        self.assertEqual(
            "PHASE181_LYRICAL_FASTRTPS_WINDOWS_LOCAL_EDITOR_PASS",
            profiles.profile_success_verdict("lyrical-fastrtps"),
        )
        self.assertEqual(
            "PHASE181_LYRICAL_ZENOH_WINDOWS_LOCAL_EDITOR_PASS",
            profiles.profile_success_verdict("lyrical-zenoh"),
        )

    def test_wrapper_rejects_attempts_to_override_its_fixed_distro_or_rmw(self):
        """Verify Phase181 behavior: wrapper rejects attempts to override its fixed distro or rmw."""
        profiles = load_profiles_module()

        with self.assertRaisesRegex(ValueError, "--distro"):
            profiles.profile_wrapper_argv("humble-fastrtps", ["--distro", "jazzy"])
        with self.assertRaisesRegex(ValueError, "--rmw"):
            profiles.profile_wrapper_argv("lyrical-zenoh", ["--rmw", "rmw_fastrtps_cpp"])

    def test_zenoh_wrapper_preserves_an_explicit_external_topology_value(self):
        """Verify Phase181 behavior: zenoh wrapper preserves an explicit external topology value."""
        profiles = load_profiles_module()

        argv = profiles.profile_wrapper_argv(
            "lyrical-zenoh",
            ["--zenoh-topology-id=operator-owned-topology"],
        )

        self.assertIn("--zenoh-topology-id=operator-owned-topology", argv)
        self.assertNotIn(profiles.DEFAULT_ZENOH_TOPOLOGY_ID, argv)

    def test_router_start_failure_persists_a_redacted_profile_summary(self):
        """Verify Phase181 behavior: router start failure persists a redacted profile summary."""
        profiles = load_profiles_module()
        with temporary_directory("matrix-profiles-") as temporary:
            root = pathlib.Path(temporary)
            with mock.patch.object(profiles.peer, "workspace_root", return_value=root):
                summary_path = profiles.write_profile_failure_summary(
                    profiles.PROFILES["lyrical-zenoh"],
                    "FAIL_ZENOH_TOPOLOGY",
                )

            summary = json.loads(summary_path.read_text(encoding="utf-8"))
            self.assertEqual("FAIL_ZENOH_TOPOLOGY", summary["verdict"])
            self.assertEqual("lyrical-zenoh", summary["profileId"])
            self.assertEqual("rmw_zenoh_cpp", summary["rmwImplementation"])
            self.assertNotIn("error", summary)
            self.assertEqual(root / "build" / "phase181" / "lyrical-zenoh" / "windows-local-editor.json", summary_path)


if __name__ == "__main__":
    unittest.main()
