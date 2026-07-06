#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for R2FU runtime package validation gates.

from __future__ import annotations

import importlib.util
import hashlib
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[5]
VALIDATOR_PATH = ROOT / "Scripts" / "ros2forunity" / "windows" / "lyrical" / "validate_r2fu_runtime_package.py"


def load_validator_module():
    """Load the runtime package validator module under test."""
    spec = importlib.util.spec_from_file_location("validate_r2fu_runtime_package", VALIDATOR_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class RuntimePackageValidatorTests(unittest.TestCase):
    """Regression coverage for release and public-doc validation."""

    def setUp(self) -> None:
        """Load a fresh validator module for each test."""
        self.validator = load_validator_module()

    def test_release_gate_blocks_candidate_runtime_inventory(self) -> None:
        """Release gate fails while redistributionStatus is candidate_not_published."""
        exit_code = self.validator.main(["--release-gate"])

        self.assertEqual(self.validator.EXIT_FAILURE, exit_code)

    def test_public_docs_must_include_artifact_hash(self) -> None:
        """Public docs validation rejects README/notices that omit the artifact hash."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            readme = package / "README.md"
            notices = package / "THIRD_PARTY_NOTICES.md"
            package_json = package / "package.json"
            manifest = package / "runtime-manifest.json"
            artifact_sha = "a" * 64

            readme.write_text(
                "runtime.lyrical.win64 adapter combined Unity2Foxglove workflow\n"
                "Install only one dev.unity2foxglove.ros2forunity.runtime.* package\n",
                encoding="utf-8",
            )
            notices.write_text(artifact_sha, encoding="utf-8")
            package_json.write_text("{}", encoding="utf-8")
            manifest.write_text(f'{{"artifactSha256":"{artifact_sha}"}}', encoding="utf-8")

            self.validator.PACKAGE = package
            self.validator.PUBLIC_DOCS = (readme, notices, package_json, manifest)
            results = []

            self.validator.check_public_docs(results)

        failed = [result.name for result in results if not result.ok]
        self.assertIn("README documents artifact SHA-256", failed)

    def test_runtime_source_declares_rmw_guard(self) -> None:
        """ROS2ForUnity startup path declares and enforces supported RMWs."""
        source = (
            self.validator.RUNTIME_ROOT
            / "Scripts"
            / "ROS2ForUnity.cs"
        ).read_text(encoding="utf-8", errors="replace")

        self.assertIn("defaultRmwImplementation", source)
        self.assertIn("zenohRmwImplementation", source)
        self.assertIn("IsSupportedRmwImplementation", source)
        self.assertIn("ValidateRmwImplementation", source)
        self.assertIn("rmw_fastrtps_cpp", source)
        self.assertIn("rmw_zenoh_cpp", source)

    def test_package_metadata_requires_explicit_empty_dependencies(self) -> None:
        """Runtime package metadata should declare that it has no external package dependencies."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            (package / "package.json").write_text(
                '{"name":"dev.unity2foxglove.ros2forunity.runtime.lyrical.win64",'
                '"version":"0.1.0-preview.1",'
                '"displayName":"Unity2Foxglove ROS2 For Unity Runtime - Lyrical Win64",'
                '"license":"Apache-2.0",'
                '"unity":"6000.0",'
                '"description":"Optional Lyrical Windows x64 runtime package for Unity2Foxglove ROS2 For Unity integration.",'
                '"keywords":["ros2","ros2-for-unity","lyrical","win64"]}',
                encoding="utf-8",
            )
            self.validator.PACKAGE = package
            results = []

            self.validator.check_package_metadata(results)

        by_name = {result.name: result for result in results}
        self.assertFalse(by_name["package declares no external dependencies"].ok)

    def test_public_docs_must_explain_facade_independent_runtime(self) -> None:
        """Runtime docs must make the no-facade-dependency package role explicit."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            readme = package / "README.md"
            notices = package / "THIRD_PARTY_NOTICES.md"
            package_json = package / "package.json"
            manifest = package / "runtime-manifest.json"
            artifact_sha = "b" * 64

            readme.write_text(
                "runtime.lyrical.win64 adapter combined Unity2Foxglove workflow\n"
                "Install only one dev.unity2foxglove.ros2forunity.runtime.* package\n"
                f"{artifact_sha}\n"
                "WSL2 NAT diagnostic-only Windows Defender Firewall\n",
                encoding="utf-8",
            )
            notices.write_text(artifact_sha, encoding="utf-8")
            package_json.write_text("{}", encoding="utf-8")
            manifest.write_text(f'{{"artifactSha256":"{artifact_sha}"}}', encoding="utf-8")

            self.validator.PACKAGE = package
            self.validator.PUBLIC_DOCS = (readme, notices, package_json, manifest)
            results = []

            self.validator.check_public_docs(results)

        by_name = {result.name: result for result in results}
        self.assertFalse(by_name["README documents runtime package has no facade dependency"].ok)

    def test_generator_alignment_reports_missing_generator_as_failed_check(self) -> None:
        """Missing generator source should produce a structured failed result."""
        with tempfile.TemporaryDirectory() as temp:
            self.validator.ROOT = Path(temp)
            results = []

            self.validator.check_generator_alignment(results)

        self.assertFalse(results[0].ok)
        self.assertIn("generator script readable", results[0].name)

    def test_runtime_files_fail_when_fastdds_import_dependency_is_missing(self) -> None:
        """Runtime validation rejects an RMW DLL whose transitive imports are absent."""
        source = (
            self.validator.PLUGIN_ROOT
            / "rmw_fastrtps_cpp.dll"
        )
        with tempfile.TemporaryDirectory() as temp:
            plugin_root = Path(temp)
            shutil.copyfile(source, plugin_root / source.name)
            self.validator.PLUGIN_ROOT = plugin_root
            self.validator.RUNTIME_ROOT = plugin_root.parent
            self.validator.CRITICAL_PLUGIN_DLLS = ()
            self.validator.ZENOH_CONFIG_FILES = ()
            results = []

            self.validator.check_runtime_files(results)

        closure = [
            result for result in results
            if result.name == "native DLL dependency closure: rmw_fastrtps_cpp.dll"
        ]
        self.assertEqual(1, len(closure))
        self.assertFalse(closure[0].ok)
        self.assertIn("rosidl_dynamic_typesupport_fastrtps.dll", closure[0].detail)

    def test_runtime_files_reject_unsafe_zenoh_session_defaults(self) -> None:
        """Unity-facing Zenoh session configs must not enable hard exit, 1GiB buffers, or adminspace."""
        unsafe_config = (
            "listen: { exit_on_failure: true }\n"
            "rx: { max_message_size: 1073741824 }\n"
            "adminspace: { enabled: true, permissions: { read: true, write: false } }\n"
        )
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            plugin_config = (
                root
                / "Runtime"
                / "Ros2ForUnity"
                / "Plugins"
                / "Windows"
                / "x86_64"
                / "share"
                / "rmw_zenoh_cpp"
                / "config"
                / "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5"
            )
            streaming_config = (
                root
                / "Runtime"
                / "Ros2ForUnity"
                / "StreamingAssets"
                / "Ros2ForUnity"
                / "share"
                / "rmw_zenoh_cpp"
                / "config"
                / "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5"
            )
            plugin_config.parent.mkdir(parents=True)
            streaming_config.parent.mkdir(parents=True)
            plugin_config.write_text(unsafe_config, encoding="utf-8")
            streaming_config.write_text(unsafe_config, encoding="utf-8")

            self.validator.RUNTIME_ROOT = root / "Runtime" / "Ros2ForUnity"
            self.validator.PLUGIN_ROOT = root / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64"
            self.validator.CRITICAL_PLUGIN_DLLS = ()
            self.validator.RMW_DEPENDENCY_CLOSURE_SEEDS = ()
            self.validator.LEAKY_UPSTREAM_EXAMPLES = ()
            self.validator.ZENOH_CONFIG_FILES = (plugin_config, streaming_config)
            self.validator.ZENOH_CONFIG_MIRRORS = ((plugin_config, streaming_config),)
            results = []

            self.validator.check_runtime_files(results)

        failed_names = {result.name for result in results if not result.ok}
        self.assertTrue(any("listen failure is non-fatal" in name for name in failed_names))
        self.assertTrue(any("defragmentation buffer is bounded" in name for name in failed_names))
        self.assertTrue(any("adminspace disabled" in name for name in failed_names))

    def test_runtime_files_require_zenoh_router_security_notes(self) -> None:
        """Open Zenoh router configs must document the trusted-lab boundary."""
        unsafe_router_config = (
            "listen: { endpoints: [\"tcp/[::]:7447\"] }\n"
            "transport: { unicast: { accept_pending: 10000, max_sessions: 10000 } }\n"
        )
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            plugin_config = (
                root
                / "Runtime"
                / "Ros2ForUnity"
                / "Plugins"
                / "Windows"
                / "x86_64"
                / "share"
                / "rmw_zenoh_cpp"
                / "config"
                / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"
            )
            streaming_config = (
                root
                / "Runtime"
                / "Ros2ForUnity"
                / "StreamingAssets"
                / "Ros2ForUnity"
                / "share"
                / "rmw_zenoh_cpp"
                / "config"
                / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"
            )
            plugin_config.parent.mkdir(parents=True)
            streaming_config.parent.mkdir(parents=True)
            plugin_config.write_text(unsafe_router_config, encoding="utf-8")
            streaming_config.write_text(unsafe_router_config, encoding="utf-8")

            self.validator.RUNTIME_ROOT = root / "Runtime" / "Ros2ForUnity"
            self.validator.PLUGIN_ROOT = root / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64"
            self.validator.CRITICAL_PLUGIN_DLLS = ()
            self.validator.RMW_DEPENDENCY_CLOSURE_SEEDS = ()
            self.validator.LEAKY_UPSTREAM_EXAMPLES = ()
            self.validator.ZENOH_CONFIG_FILES = (plugin_config, streaming_config)
            self.validator.ZENOH_CONFIG_MIRRORS = ((plugin_config, streaming_config),)
            results = []

            self.validator.check_runtime_files(results)

        failed_names = {result.name for result in results if not result.ok}
        self.assertTrue(any("open-listen profile is documented" in name for name in failed_names))
        self.assertTrue(any("high connection limits are documented" in name for name in failed_names))

    def test_inventory_rejects_stale_zenoh_config_hash(self) -> None:
        """Runtime inventory should hash-check patched Zenoh configs even in fast mode."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            runtime_root = package / "Runtime" / "Ros2ForUnity"
            config = (
                runtime_root
                / "Plugins"
                / "Windows"
                / "x86_64"
                / "share"
                / "rmw_zenoh_cpp"
                / "config"
                / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"
            )
            inventory = package / "RuntimeSupport" / "r2fu-lyrical-win64-runtime-inventory.json"
            config.parent.mkdir(parents=True)
            inventory.parent.mkdir(parents=True)
            config.write_text("router config\n", encoding="utf-8")
            inventory.write_text(
                json.dumps(
                    {
                        "runtimeId": "r2fu-lyrical-win64",
                        "artifactName": "Ros2ForUnity_lyrical_standalone_windows_x86_64.zip",
                        "rosDistro": "lyrical",
                        "rmw": "rmw_fastrtps_cpp",
                        "defaultRmwImplementation": "rmw_fastrtps_cpp",
                        "platform": "win64",
                        "buildType": "standalone",
                        "supportedRmwImplementations": ["rmw_fastrtps_cpp", "rmw_zenoh_cpp"],
                        "sha256": self.validator.EXPECTED_ARTIFACT_SHA256,
                        "artifactSize": 1,
                        "fileCount": 1,
                        "redistributionStatus": "candidate_not_published",
                        "categoryCounts": {"native_libraries": 700},
                        "knownCriticalFiles": [],
                        "files": [
                            {
                                "path": "Ros2ForUnity/Plugins/Windows/x86_64/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
                                "sha256": "0" * 64,
                                "size": config.stat().st_size,
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )
            manifest = {"artifactSha256": self.validator.EXPECTED_ARTIFACT_SHA256, "artifactSize": 1, "inventoryFileCount": 1}

            self.validator.PACKAGE = package
            self.validator.RUNTIME_ROOT = runtime_root
            self.validator.INVENTORY = inventory
            self.validator.MANIFEST = package / "manifest.json"
            self.validator.MANIFEST.write_text(json.dumps(manifest), encoding="utf-8")
            actual_hash = hashlib.sha256(config.read_bytes()).hexdigest()
            results = []

            self.validator.check_inventory(results, release_gate=False, skip_dll_hash=True)

        by_name = {result.name: result for result in results}
        self.assertFalse(by_name["runtime inventory Zenoh config hashes match disk"].ok)
        self.assertNotEqual("0" * 64, actual_hash)

    def test_expected_artifact_hash_is_full_sha256(self) -> None:
        """Pinned artifact hash is not accidentally truncated."""
        self.assertEqual(64, len(self.validator.EXPECTED_ARTIFACT_SHA256))

    def test_core_runtime_missing_reports_failed_boundary_check(self) -> None:
        """A missing core Runtime folder must fail instead of silently passing."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            core = root / "core"
            adapter = root / "adapter"
            core.mkdir()
            adapter.mkdir()
            (core / "package.json").write_text("{}", encoding="utf-8")
            (adapter / "package.json").write_text("{}", encoding="utf-8")
            self.validator.CORE_PACKAGE = core
            self.validator.ADAPTER_PACKAGE = adapter
            results = []

            self.validator.check_package_boundaries(results)

        by_name = {result.name: result for result in results}
        self.assertFalse(by_name["core Runtime folder exists"].ok)
        self.assertFalse(by_name["core SDK runtime remains ROS2 For Unity free"].ok)

    def test_unity_editor_using_guard_removes_guarded_occurrence(self) -> None:
        """The UnityEditor using check should inspect the post-substitution text."""
        self.assertTrue(self.validator.guarded_unity_editor_using("#if UNITY_EDITOR\nusing UnityEditor;\n#endif\n"))
        self.assertFalse(
            self.validator.guarded_unity_editor_using(
                "#if UNITY_EDITOR\nusing UnityEditor;\n#endif\nusing UnityEditor;\n"
            )
        )

    def test_public_docs_accept_specific_runtime_package_token(self) -> None:
        """One-runtime docs check should not depend on a literal runtime.* token."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            readme = package / "README.md"
            notices = package / "THIRD_PARTY_NOTICES.md"
            package_json = package / "package.json"
            manifest = package / "runtime-manifest.json"
            artifact_sha = "b" * 64

            readme.write_text(
                "runtime.lyrical.win64 adapter combined Unity2Foxglove workflow\n"
                "Install only one dev.unity2foxglove.ros2forunity.runtime.lyrical.win64 package\n"
                "WSL2 NAT diagnostic-only Windows Defender Firewall\n"
                + artifact_sha,
                encoding="utf-8",
            )
            notices.write_text(artifact_sha, encoding="utf-8")
            package_json.write_text("{}", encoding="utf-8")
            manifest.write_text(f'{{"artifactSha256":"{artifact_sha}"}}', encoding="utf-8")
            self.validator.PACKAGE = package
            self.validator.PUBLIC_DOCS = (readme, notices, package_json, manifest)
            self.validator.MANIFEST = manifest
            results = []

            self.validator.check_public_docs(results)

        one_runtime = [result for result in results if result.name == "README documents one-runtime policy"]
        self.assertEqual(1, len(one_runtime))
        self.assertTrue(one_runtime[0].ok)


if __name__ == "__main__":
    unittest.main()
