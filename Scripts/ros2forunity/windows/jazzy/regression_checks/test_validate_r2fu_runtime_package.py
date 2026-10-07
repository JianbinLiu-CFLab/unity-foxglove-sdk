#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for R2FU runtime package validation gates.

from __future__ import annotations

import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[5]
VALIDATOR_PATH = ROOT / "Scripts" / "ros2forunity" / "windows" / "jazzy" / "validate_r2fu_runtime_package.py"


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

    def test_inventory_category_counts_must_match_file_entries(self) -> None:
        """A declared native count cannot be satisfied by DLLs from other categories."""
        files = [
            {"path": "Ros2ForUnity/native.dll", "category": "native_libraries"},
            {"path": "Ros2ForUnity/managed.dll", "category": "managed_assemblies"},
        ]

        self.assertFalse(
            self.validator.inventory_category_counts_match(
                {"native_libraries": 2},
                files,
            )
        )
        self.assertTrue(
            self.validator.inventory_category_counts_match(
                {"native_libraries": 1, "managed_assemblies": 1},
                files,
            )
        )

    def test_ros2cs_plugin_metadata_requires_portable_roots(self) -> None:
        """Both packaged plugin inventories must use a package-relative root."""
        with tempfile.TemporaryDirectory() as temp:
            runtime_root = Path(temp) / "Runtime" / "Ros2ForUnity"
            plugin_root = runtime_root / "Plugins" / "Windows" / "x86_64"
            metadata_files = (
                runtime_root / "Plugins" / "metadata_ros2cs.xml",
                plugin_root / "metadata_ros2cs.xml",
            )
            for path in metadata_files:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('<ros2cs><plugins root="D:\\producer\\plugins" /></ros2cs>', encoding="utf-8")
            self.validator.RUNTIME_ROOT = runtime_root
            self.validator.PLUGIN_ROOT = plugin_root
            results = []

            self.validator.check_ros2cs_metadata_portability(results)

            self.assertEqual(2, len(results))
            self.assertTrue(all(not result.ok for result in results))

    def test_release_gate_blocks_candidate_runtime_inventory(self) -> None:
        """Release gate fails while redistributionStatus is candidate_not_published."""
        exit_code = self.validator.main(["--release-gate"])

        self.assertEqual(self.validator.EXIT_FAILURE, exit_code)

    def test_release_gate_requires_fresh_project_acceptance(self) -> None:
        """Published runtime cannot pass while fresh-project acceptance is deferred."""
        self.assertFalse(self.validator.fresh_project_acceptance_passed({"freshProjectAcceptance": {"status": "deferred"}}))
        self.assertTrue(self.validator.fresh_project_acceptance_passed({"freshProjectAcceptance": {"status": "passed"}}))
        self.assertTrue(self.validator.fresh_project_acceptance_passed({"freshProjectAcceptance": "passed"}))

    def test_release_gate_requires_exact_candidate_acceptance_binding(self) -> None:
        """A passed acceptance record must identify the published candidate lineage."""
        manifest = {
            "runtimeId": "r2fu-jazzy-win64",
            "packageName": "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
            "packageVersion": "0.1.0-preview.1",
            "rosDistro": "jazzy",
            "artifactName": "Ros2ForUnity_jazzy_standalone_windows_x86_64.zip",
            "artifactSha256": "a" * 64,
            "artifactSize": 123,
            "inventoryFileCount": 7,
            "unityVersion": "6000.3.14f1",
            "freshProjectAcceptance": {
                "status": "passed",
                "runtimeId": "r2fu-jazzy-win64",
                "packageName": "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                "packageVersion": "0.1.0-preview.1",
                "rosDistro": "jazzy",
                "artifactName": "Ros2ForUnity_jazzy_standalone_windows_x86_64.zip",
                "artifactSha256": "a" * 64,
                "artifactSize": 123,
                "inventoryFileCount": 7,
                "unityVersion": "6000.3.14f1",
                "commitSha": "b" * 40,
                "workflowRunId": 42,
            },
        }
        self.assertTrue(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))
        self.assertTrue(self.validator.fresh_project_acceptance_record_is_valid(manifest))
        with mock.patch.dict(self.validator.os.environ, {"R2FU_EXPECTED_COMMIT_SHA": "c" * 40}, clear=False):
            self.assertFalse(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))
        with mock.patch.dict(self.validator.os.environ, {"R2FU_EXPECTED_COMMIT_SHA": "b" * 40}, clear=False):
            with mock.patch.object(self.validator, "acceptance_commit_is_ancestor_and_package_unchanged", return_value=True):
                self.assertTrue(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))
        manifest.pop("unityVersion")
        self.assertFalse(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))
        manifest["unityVersion"] = "6000.3.14f1"
        manifest["freshProjectAcceptance"]["artifactSha256"] = "c" * 64
        self.assertFalse(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))
        manifest["freshProjectAcceptance"]["artifactSha256"] = manifest["artifactSha256"]
        manifest["freshProjectAcceptance"]["inventoryFileCount"] = 8
        self.assertFalse(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))
        self.assertFalse(self.validator.fresh_project_acceptance_record_is_valid(manifest))

    def test_release_gate_accepts_unchanged_runtime_from_ancestor_acceptance(self) -> None:
        """A published package may reuse acceptance from an ancestor while its payload is unchanged."""
        manifest = {
            "runtimeId": "r2fu-jazzy-win64",
            "packageName": "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
            "packageVersion": "0.1.0-preview.1",
            "rosDistro": "jazzy",
            "artifactName": "Ros2ForUnity_jazzy_standalone_windows_x86_64.zip",
            "artifactSha256": "a" * 64,
            "artifactSize": 123,
            "inventoryFileCount": 7,
            "unityVersion": "6000.3.14f1",
            "freshProjectAcceptance": {
                "status": "passed",
                "runtimeId": "r2fu-jazzy-win64",
                "packageName": "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                "packageVersion": "0.1.0-preview.1",
                "rosDistro": "jazzy",
                "artifactName": "Ros2ForUnity_jazzy_standalone_windows_x86_64.zip",
                "artifactSha256": "a" * 64,
                "artifactSize": 123,
                "inventoryFileCount": 7,
                "unityVersion": "6000.3.14f1",
                "commitSha": "b" * 40,
                "workflowRunId": 42,
            },
        }
        with mock.patch.dict(self.validator.os.environ, {"R2FU_EXPECTED_COMMIT_SHA": "c" * 40}, clear=False):
            with mock.patch.object(self.validator, "acceptance_commit_is_ancestor_and_package_unchanged", return_value=True):
                self.assertTrue(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))
            with mock.patch.object(self.validator, "acceptance_commit_is_ancestor_and_package_unchanged", return_value=False):
                self.assertFalse(self.validator.fresh_project_acceptance_binding_matches_manifest(manifest))

    def test_ancestor_acceptance_helper_checks_real_git_history_and_package_delta(self) -> None:
        """The ancestor acceptance rule must reject a changed runtime package."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Packages" / "runtime"
            package.mkdir(parents=True)

            def git(*args: str) -> str:
                """Run a git command in the temporary repository."""
                result = subprocess.run(
                    ["git", *args],
                    cwd=root,
                    check=True,
                    text=True,
                    capture_output=True,
                )
                return result.stdout.strip()

            git("init", "-q")
            git("config", "user.name", "validator-test")
            git("config", "user.email", "validator-test@example.invalid")
            (package / "payload.txt").write_text("accepted\n", encoding="utf-8")
            (package / "RuntimeSupport").mkdir()
            (package / "RuntimeSupport/runtime-manifest.json").write_text(
                '{"payloadVersion":1}\n', encoding="utf-8"
            )
            git("add", ".")
            git("commit", "-q", "-m", "acceptance")
            acceptance_sha = git("rev-parse", "HEAD")
            (root / "README.md").write_text("unrelated\n", encoding="utf-8")
            git("add", ".")
            git("commit", "-q", "-m", "unrelated")
            unchanged_candidate_sha = git("rev-parse", "HEAD")

            self.validator.ROOT = root
            self.validator.PACKAGE = package
            self.assertTrue(
                self.validator.acceptance_commit_is_ancestor_and_package_unchanged(
                    acceptance_sha,
                    unchanged_candidate_sha,
                )
            )

            (package / "RuntimeSupport/runtime-manifest.json").write_text(
                '{"payloadVersion":1,"freshProjectAcceptance":{"status":"passed"}}\n',
                encoding="utf-8",
            )
            git("add", ".")
            git("commit", "-q", "-m", "record-acceptance")
            acceptance_record_candidate_sha = git("rev-parse", "HEAD")
            self.assertTrue(
                self.validator.acceptance_commit_is_ancestor_and_package_unchanged(
                    acceptance_sha,
                    acceptance_record_candidate_sha,
                )
            )

            (package / "payload.txt").write_text("changed\n", encoding="utf-8")
            git("add", ".")
            git("commit", "-q", "-m", "runtime-change")
            changed_candidate_sha = git("rev-parse", "HEAD")
            self.assertFalse(
                self.validator.acceptance_commit_is_ancestor_and_package_unchanged(
                    acceptance_sha,
                    changed_candidate_sha,
                )
            )

    def test_runtime_dll_metas_use_plugin_importer_in_both_plugin_roots(self) -> None:
        """Managed and native DLL metadata must be Unity plugin metadata."""
        metas = list((self.validator.RUNTIME_ROOT / "Plugins").glob("*.dll.meta"))
        metas.extend(self.validator.PLUGIN_ROOT.glob("*.dll.meta"))
        self.assertGreater(len(metas), 0)
        failures = [path for path in metas if "PluginImporter:" not in path.read_text(encoding="utf-8")]
        self.assertEqual([], failures)

    def test_player_runtime_paths_use_streaming_assets_and_plugin_root(self) -> None:
        """Player metadata and native plugins must resolve from their build outputs."""
        source = (self.validator.RUNTIME_ROOT / "Scripts" / "ROS2ForUnity.cs").read_text(encoding="utf-8")
        self.assertIn("Application.streamingAssetsPath", source)
        self.assertIn("Application.dataPath", source)
        self.assertIn("StreamingAssets", source)

    def test_release_gate_rejects_prototype_distribution(self) -> None:
        """Release gate rejects Prototype and records missing-field behavior."""
        self.assertFalse(self.validator.published_runtime_is_not_prototype({"distributionLevel": "Prototype"}))
        self.assertTrue(self.validator.published_runtime_is_not_prototype({"distributionLevel": "Release"}))
        self.assertTrue(self.validator.published_runtime_is_not_prototype({}))

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
                "runtime.jazzy.win64 adapter combined Unity2Foxglove workflow\n"
                "Install only one runtime.* package\n",
                encoding="utf-8",
            )
            notices.write_text(artifact_sha, encoding="utf-8")
            package_json.write_text("{}", encoding="utf-8")
            manifest.write_text(f'{{"artifactSha256":"{artifact_sha}"}}', encoding="utf-8")

            self.validator.PACKAGE = package
            self.validator.PUBLIC_DOCS = (readme, notices, package_json, manifest)
            results = []

            self.validator.check_public_docs(results, {"artifactSha256": artifact_sha})

        failed = [result.name for result in results if not result.ok]
        self.assertIn("README documents artifact SHA-256", failed)

    def test_public_docs_missing_manifest_does_not_pass_empty_hash(self) -> None:
        """Missing manifest cannot make artifact hash checks pass through empty-string containment."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            readme = package / "README.md"
            notices = package / "THIRD_PARTY_NOTICES.md"
            package_json = package / "package.json"
            missing_manifest = package / "missing-runtime-manifest.json"

            readme.write_text(
                "runtime.jazzy.win64 adapter combined Unity2Foxglove workflow\n"
                "Install only one ros2forunity.runtime. package\n",
                encoding="utf-8",
            )
            notices.write_text("notices without artifact hash", encoding="utf-8")
            package_json.write_text("{}", encoding="utf-8")

            self.validator.PACKAGE = package
            self.validator.PUBLIC_DOCS = (readme, notices, package_json, missing_manifest)
            self.validator.MANIFEST = missing_manifest
            results = []

            self.validator.check_public_docs(results, {})

        failed = [result.name for result in results if not result.ok]
        self.assertIn("README documents artifact SHA-256", failed)
        self.assertIn("THIRD_PARTY_NOTICES documents artifact SHA-256", failed)

    def test_public_docs_one_runtime_policy_accepts_plain_runtime_package_text(self) -> None:
        """The one-runtime policy check accepts stable wording, not only a glob literal."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            readme = package / "README.md"
            notices = package / "THIRD_PARTY_NOTICES.md"
            package_json = package / "package.json"
            manifest = package / "runtime-manifest.json"
            artifact_sha = "b" * 64

            readme.write_text(
                "runtime.jazzy.win64 adapter combined Unity2Foxglove workflow\n"
                f"Install only one runtime packages entry. {artifact_sha}\n",
                encoding="utf-8",
            )
            notices.write_text(artifact_sha, encoding="utf-8")
            package_json.write_text("{}", encoding="utf-8")
            manifest.write_text(f'{{"artifactSha256":"{artifact_sha}"}}', encoding="utf-8")

            self.validator.PACKAGE = package
            self.validator.PUBLIC_DOCS = (readme, notices, package_json, manifest)
            self.validator.MANIFEST = manifest
            results = []

            self.validator.check_public_docs(results, {"artifactSha256": artifact_sha})

        one_runtime = [result for result in results if result.name == "README documents one-runtime policy"]
        self.assertTrue(one_runtime)
        self.assertTrue(one_runtime[0].ok)

    def test_unity_editor_using_guard_allows_blank_lines_and_comments(self) -> None:
        """The UnityEditor using guard accepts normal formatting inside the UNITY_EDITOR block."""
        with tempfile.TemporaryDirectory() as temp:
            runtime_root = Path(temp) / "Runtime" / "Ros2ForUnity"
            scripts = runtime_root / "Scripts"
            scripts.mkdir(parents=True)
            (scripts / "ROS2ForUnity.cs").write_text(
                "#if UNITY_EDITOR\n"
                "// editor-only package lookup\n"
                "using UnityEditor;\n"
                "UnityEditor.PackageManager.PackageInfo packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(\"Packages/demo\");\n"
                "#endif\n"
                "// Unity2Foxglove package path support\n"
                "const string packageName = \"dev.unity2foxglove.ros2forunity.runtime.jazzy.win64\";\n"
                "var resolvedPath = unity2FoxgloveRuntimePackageAssetPath;\n"
                "SetProcessEnvironmentPathVariable(GetEnvPathVariableName(), resolvedPath, resolvedPath, Path.PathSeparator);\n"
                "Path.Combine(\"Packages\", \"Runtime\");\n"
                "Directory.Exists(packagePath);\n"
                "return assetPath;\n",
                encoding="utf-8",
            )

            self.validator.RUNTIME_ROOT = runtime_root
            results = []

            self.validator.check_package_path_patch(results)

        guard = [result for result in results if result.name == "UnityEditor using guarded"]
        self.assertTrue(guard)
        self.assertTrue(guard[0].ok)

    def test_runtime_source_declares_rmw_guard(self) -> None:
        """ROS2ForUnity startup path declares and enforces the expected RMW."""
        source = (
            self.validator.RUNTIME_ROOT
            / "Scripts"
            / "ROS2ForUnity.cs"
        ).read_text(encoding="utf-8", errors="replace")

        self.assertIn("expectedRmwImplementation", source)
        self.assertIn("ValidateRmwImplementation", source)
        self.assertIn("rmw_fastrtps_cpp", source)

    def test_runtime_asmdef_is_windows_constrained(self) -> None:
        """The packaged runtime assembly must not compile in unsupported editors."""
        data = self.validator.load_json(
            self.validator.RUNTIME_ROOT
            / "Scripts"
            / "Unity2Foxglove.Ros2ForUnity.Runtime.JazzyWin64.asmdef",
            [],
            "runtime asmdef",
        )
        self.assertEqual(
            ["UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN"],
            data.get("defineConstraints"),
        )

    def test_generator_alignment_reports_missing_generator_as_failed_check(self) -> None:
        """Missing generator source should produce a structured failed result."""
        with tempfile.TemporaryDirectory() as temp:
            self.validator.ROOT = Path(temp)
            results = []

            self.validator.check_generator_alignment(results)

        self.assertFalse(results[0].ok)
        self.assertIn("generator script readable", results[0].name)


if __name__ == "__main__":
    unittest.main()
