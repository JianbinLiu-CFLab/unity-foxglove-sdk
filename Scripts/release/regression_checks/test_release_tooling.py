#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for release helper correctness and diagnostics.

from __future__ import annotations

import ast
import importlib.util
import io
import json
import os
import re
import subprocess
import sys
import tempfile
import threading
import types
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
BUMP_VERSION_PATH = ROOT / "Scripts" / "release" / "bump_version.py"
RUN_CI_PATH = ROOT / "Scripts" / "release" / "run_ci.py"
MCAP_CONFORMANCE_PATH = ROOT / "Scripts" / "mcap" / "conformance" / "run_phase121_conformance.py"
UNITY_IL2CPP_PATH = ROOT / "Scripts" / "unity_build" / "unity_il2cpp.py"
LOCAL_ENTRYPOINT_VALIDATOR_PATH = ROOT / "Scripts" / "package" / "validate_local_entrypoints.py"


def load_module(name: str, path: Path):
    """Load a Python script as an isolated module."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules.pop(spec.name, None)
    sys.modules[spec.name] = module
    try:
        spec.loader.exec_module(module)
    except Exception:
        sys.modules.pop(spec.name, None)
        raise
    return module


class VersionBumpTests(unittest.TestCase):
    """Regression coverage for release version synchronization."""

    def setUp(self) -> None:
        """Load a fresh bump_version module for each test."""
        self.bump_module = load_module("bump_version_under_test", BUMP_VERSION_PATH)

    def test_load_module_cleans_partial_module_after_exec_failure(self) -> None:
        """Syntax/import failures should not leave a partial module registered."""
        with tempfile.TemporaryDirectory() as temp:
            broken = Path(temp) / "broken.py"
            broken.write_text("raise RuntimeError('boom')\n", encoding="utf-8")

            with self.assertRaises(RuntimeError):
                load_module("broken_release_tool_under_test", broken)

        self.assertNotIn("broken_release_tool_under_test", sys.modules)

    def test_update_readme_keeps_only_current_release_note_without_corruption(self) -> None:
        """Keep one current release note and remove archive navigation from README."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            readme = root / "README.md"
            readme.write_text(
                "\n".join(
                    [
                        "header",
                        "[![Release](https://img.shields.io/badge/release-v1.9.4-green)](https://example.test/releases)",
                        "- A pure C# WebSocket server for Unity Editor and Standalone Player. Windows is verified for v1.9.4; macOS/Linux are intended targets but not yet verified.",
                        "Historical note: upgraded from release-v1.9.4 after verified for v1.9.4 acceptance.",
                        "- [v1.9.4 release notes](docs/releases/RELEASE_NOTES_v1.9.4.md)",
                        "- [v1.9.3 release notes](docs/releases/RELEASE_NOTES_v1.9.3.md)",
                        "- [v1.9.2 release notes](docs/releases/RELEASE_NOTES_v1.9.2.md)",
                        "- [v1.9.1 release notes](docs/releases/RELEASE_NOTES_v1.9.1.md)",
                        "- [Release notes archive](docs/releases/)",
                        "footer",
                    ]
                )
                + "\n",
                encoding="utf-8",
            )

            bump = self.bump_module.VersionBump(root, "2.0.0", "2026-06-08", False)
            bump.update_readme("1.9.4")

            text = readme.read_text(encoding="utf-8")
            self.assertIn("release-v2.0.0-green", text)
            self.assertIn("Windows is verified for v2.0.0;", text)
            self.assertIn("Historical note: upgraded from release-v1.9.4 after verified for v1.9.4 acceptance.", text)
            self.assertIn("- [v2.0.0 release notes](docs/releases/RELEASE_NOTES_v2.0.0.md)", text)
            self.assertNotIn("RELEASE_NOTES_v1.9.4", text)
            self.assertNotIn("RELEASE_NOTES_v1.9.3", text)
            self.assertNotIn("RELEASE_NOTES_v1.9.2", text)
            self.assertNotIn("RELEASE_NOTES_v1.9.1", text)
            self.assertNotIn("- [Release notes archive](docs/releases/)", text)
            self.assertEqual(text.count("release notes](docs/releases/RELEASE_NOTES_v"), 1)
            self.assertIn("footer", text)

    def test_root_readme_links_only_the_package_release(self) -> None:
        """The public README should expose exactly the current package release note."""
        package = json.loads((ROOT / "Packages/dev.unity2foxglove.sdk/package.json").read_text(encoding="utf-8"))
        version = package["version"]
        text = (ROOT / "README.md").read_text(encoding="utf-8")
        links = re.findall(
            r"docs/releases/RELEASE_NOTES_v\d+\.\d+\.\d+\.md",
            text,
        )

        self.assertEqual(links, [f"docs/releases/RELEASE_NOTES_v{version}.md"])
        self.assertNotIn("[Release notes archive]", text)

    def test_update_readme_preserves_concise_release_navigation(self) -> None:
        """Update the single inline release link without appending a legacy list."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            readme = root / "README.md"
            readme.write_text(
                "\n".join(
                    [
                        "[![Release](https://img.shields.io/badge/release-v1.9.6-green)](https://example.test/releases)",
                        "Core WebSocket/MCAP: Windows is verified for v1.9.6; other platforms remain pending.",
                        "Release and compliance: [v1.9.6 release notes](docs/releases/RELEASE_NOTES_v1.9.6.md) "
                        "· [Changelog](CHANGELOG.md) · [Third-party notices](THIRD_PARTY_NOTICES.md)",
                    ]
                )
                + "\n",
                encoding="utf-8",
            )

            bump = self.bump_module.VersionBump(root, "1.9.7", "2026-07-20", False)
            bump.update_readme("1.9.6")

            text = readme.read_text(encoding="utf-8")
            self.assertIn("release-v1.9.7-green", text)
            self.assertIn("Windows is verified for v1.9.7;", text)
            self.assertIn(
                "Release and compliance: [v1.9.7 release notes]"
                "(docs/releases/RELEASE_NOTES_v1.9.7.md) · [Changelog]",
                text,
            )
            self.assertNotIn("RELEASE_NOTES_v1.9.6", text)
            self.assertEqual(text.count("release notes](docs/releases/RELEASE_NOTES_v"), 1)
            self.assertFalse(text.rstrip().endswith("release notes](docs/releases/RELEASE_NOTES_v1.9.7.md)"))

    def test_replace_version_property_updates_package_json(self) -> None:
        """The package.json version property should be synchronized."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "Packages/dev.unity2foxglove.sdk/package.json"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(
                '{\n'
                '  "name": "dev.unity2foxglove.sdk",\n'
                '  "version": "1.2.3"\n'
                '}\n',
                encoding="utf-8",
            )

            bump = self.bump_module.VersionBump(root, "9.9.9", "2026-06-08", False)
            bump.replace_version_property("1.2.3", path.read_text(encoding="utf-8"), path)

            text = path.read_text(encoding="utf-8")
            self.assertIn('"version": "9.9.9"', text)
            self.assertNotIn('"version": "1.2.3"', text)

    def test_update_adapter_dependency_syncs_core_sdk_version(self) -> None:
        """The optional ROS2 adapter should depend on the released SDK version."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "Packages/dev.unity2foxglove.ros2forunity/package.json"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(
                '{\n'
                '  "dependencies": {\n'
                '    "dev.unity2foxglove.sdk": "1.2.3"\n'
                '  }\n'
                '}\n',
                encoding="utf-8",
            )

            bump = self.bump_module.VersionBump(root, "9.9.9", "2026-06-08", False)
            bump.update_adapter_dependency()

            text = path.read_text(encoding="utf-8")
            self.assertIn('"dev.unity2foxglove.sdk": "9.9.9"', text)
            self.assertNotIn('"dev.unity2foxglove.sdk": "1.2.3"', text)

    def test_update_phase16_assertions_syncs_release_metadata(self) -> None:
        """Runtime release metadata assertions should move with each release."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase16Validation.cs"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(
                'Assert(adapterPackageJson.Contains("\\"dev.unity2foxglove.sdk\\": \\"1.2.3\\""));\n'
                'Assert(citation.Contains("version: \\"1.2.3\\"")\n'
                '       && citation.Contains("date-released: \\"2026-01-02\\""));\n',
                encoding="utf-8",
            )

            bump = self.bump_module.VersionBump(root, "9.9.9", "2026-06-08", False)
            bump.update_phase16_assertions()

            text = path.read_text(encoding="utf-8")
            self.assertIn('\\"dev.unity2foxglove.sdk\\": \\"9.9.9\\"', text)
            self.assertIn('version: \\"9.9.9\\"', text)
            self.assertIn('date-released: \\"2026-06-08\\"', text)
            self.assertNotIn("1.2.3", text)
            self.assertNotIn("2026-01-02", text)

    def test_update_core_sdk_dependency_assertions_syncs_validators(self) -> None:
        """Runtime and script validators should assert the released SDK version."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            bracket_paths = [
                root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase107Validation.cs",
                root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase108Validation.cs",
            ]
            escaped_literal_path = root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase163_29Validation.cs"
            literal_paths = [
                root / "Scripts/ros2forunity/windows/humble/validate_ros2forunity_package.py",
                root / "Scripts/ros2forunity/windows/jazzy/validate_ros2forunity_package.py",
                root / "Scripts/ros2forunity/windows/lyrical/validate_ros2forunity_package.py",
            ]
            for path in bracket_paths:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(
                    'dependencies["dev.unity2foxglove.sdk"] == "1.2.3"\n',
                    encoding="utf-8",
                )
            escaped_literal_path.parent.mkdir(parents=True, exist_ok=True)
            escaped_literal_path.write_text(
                'Check(packageJson.Contains("\\"dev.unity2foxglove.sdk\\": \\"1.2.3\\""));\n',
                encoding="utf-8",
            )
            for path in literal_paths:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(
                    'dependencies == {"dev.unity2foxglove.sdk": "1.2.3"}\n',
                    encoding="utf-8",
                )

            bump = self.bump_module.VersionBump(root, "9.9.9", "2026-06-08", False)
            bump.update_core_sdk_dependency_assertions()

            for path in [*bracket_paths, escaped_literal_path, *literal_paths]:
                text = path.read_text(encoding="utf-8")
                self.assertIn("9.9.9", text)
                self.assertNotIn("1.2.3", text)

    def test_update_changelog_inserts_at_version_heading_not_first_rule(self) -> None:
        """Horizontal rules before version history must not receive new entries."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            changelog = root / "CHANGELOG.md"
            changelog.write_text(
                "# Changelog\n\n"
                "Intro text.\n\n"
                "---\n"
                "Non-version separator.\n\n"
                "---\n\n"
                "## 1.2.3 - 2026-01-02\n\n"
                "- Existing entry.\n",
                encoding="utf-8",
            )

            bump = self.bump_module.VersionBump(root, "2.0.0", "2026-06-08", False)
            bump.update_changelog()

            text = changelog.read_text(encoding="utf-8")
            self.assertIn("---\nNon-version separator.\n\n---\n\n## 2.0.0 - 2026-06-08", text)
            self.assertIn("## 1.2.3 - 2026-01-02", text)

    def test_update_changelog_promotes_nonempty_unreleased_section(self) -> None:
        """Move curated Unreleased notes into the new version without stub text."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            changelog = root / "CHANGELOG.md"
            changelog.write_text(
                "# Changelog\n\n"
                "---\n\n"
                "## Unreleased\n\n"
                "### Added\n\n"
                "- Bidirectional FoxRun bindings.\n\n"
                "### Verified\n\n"
                "- Focused gates passed.\n\n"
                "## 1.9.6 - 2026-07-06\n\n"
                "- Existing release.\n",
                encoding="utf-8",
            )

            bump = self.bump_module.VersionBump(root, "1.9.7", "2026-07-20", False)
            bump.update_changelog()

            text = changelog.read_text(encoding="utf-8")
            self.assertIn(
                "## Unreleased\n\n## 1.9.7 - 2026-07-20\n\n"
                "### Added\n\n- Bidirectional FoxRun bindings.",
                text,
            )
            self.assertIn("### Verified\n\n- Focused gates passed.", text)
            self.assertIn("## 1.9.6 - 2026-07-06", text)
            self.assertNotIn("should be run before tagging", text)

    def test_update_citation_updates_version_and_release_date(self) -> None:
        """CITATION.cff should carry exact release metadata."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            citation = root / "CITATION.cff"
            citation.write_text(
                "\n".join(
                    [
                        "cff-version: 1.2.0",
                        'title: "Unity2Foxglove"',
                        "type: software",
                        'version: "1.2.3"',
                        'date-released: "2026-01-02"',
                    ]
                )
                + "\n",
                encoding="utf-8",
            )

            bump = self.bump_module.VersionBump(root, "2.0.0", "2026-06-08", False)
            bump.update_citation()

            text = citation.read_text(encoding="utf-8")
            self.assertIn('version: "2.0.0"', text)
            self.assertIn('date-released: "2026-06-08"', text)


class RunCiTests(unittest.TestCase):
    """Regression coverage for local CI runner reliability."""

    PHASE184_ACCEPTANCE_TOOLING_SUITES = (
        (
            "PHASE184_PROFILE_ACCEPTANCE_PROTOCOL_REGRESSION",
            "Scripts.smoke.foxrun.regression_checks.test_phase184_profile_acceptance_protocol",
            "Phase184 acceptance protocol tooling regressions",
        ),
        (
            "PHASE184_PROFILE_ACCEPTANCE_ORCHESTRATOR_REGRESSION",
            "Scripts.smoke.foxrun.regression_checks.test_phase184_profile_acceptance",
            "Phase184 acceptance orchestrator tooling regressions",
        ),
        (
            "PHASE184_FOXGLOVE_DESKTOP_LIVE_PROTOCOL_REGRESSION",
            "Scripts.smoke.foxrun.regression_checks.test_phase184_foxglove_desktop_live_protocol",
            "Phase184 Foxglove Desktop live protocol regressions",
        ),
        (
            "PHASE184_FOXGLOVE_CLI_INSTALL_REGRESSION",
            "Scripts.smoke.foxrun.regression_checks.test_phase184_foxglove_cli_install",
            "Phase184 Foxglove CLI installer regressions",
        ),
        (
            "PHASE184_WINDOWS_JOB_OWNER_REGRESSION",
            "Scripts.smoke.foxrun.regression_checks.test_phase184_windows_job_owner",
            "Phase184 Windows Job owner regressions",
        ),
        (
            "PHASE184_FOXGLOVE_DESKTOP_LIVE_ACCEPTANCE_REGRESSION",
            "Scripts.smoke.foxrun.regression_checks.test_phase184_foxglove_desktop_live_acceptance",
            "Phase184 Foxglove Desktop live coordinator regressions",
        ),
    )

    def setUp(self) -> None:
        """Load a fresh run_ci module for each test."""
        self.run_ci = load_module("run_ci_under_test", RUN_CI_PATH)

    def test_run_ci_imports_under_current_python(self) -> None:
        """The CI runner module loads without f-string syntax failures."""
        self.assertTrue(hasattr(self.run_ci, "main"))

    def test_boundary_check_fails_when_git_command_fails(self) -> None:
        """Boundary checks must not pass when git itself fails."""
        failed = subprocess.CompletedProcess(args=["git"], returncode=128, stdout="", stderr="fatal")
        with mock.patch.object(self.run_ci.subprocess, "run", return_value=failed):
            self.assertFalse(self.run_ci._check_boundary())

    def test_changelog_verified_stub_check_fails_on_unreplaced_stub(self) -> None:
        """Release CI should fail if changelog verified entries still contain generated stubs."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "CHANGELOG.md").write_text(
                "### Verified\n\n"
                "- Runtime validation suite should be run before tagging this release.\n",
                encoding="utf-8",
            )
            with mock.patch.object(self.run_ci, "REPO_ROOT", root):
                self.assertFalse(self.run_ci._check_changelog_verified_stubs())

            (root / "CHANGELOG.md").write_text(
                "### Verified\n\n"
                "- Runtime validation suite passed before tagging this release.\n",
                encoding="utf-8",
            )
            with mock.patch.object(self.run_ci, "REPO_ROOT", root):
                self.assertTrue(self.run_ci._check_changelog_verified_stubs())

    def test_boundary_check_uses_plain_ls_files_for_nested_developer(self) -> None:
        """Nested Developer checks should not depend on git pathspec glob support."""
        calls: list[list[str]] = []

        def fake_run(cmd, **_kwargs):
            """Return canned git outputs for the boundary check."""
            calls.append(cmd)
            if cmd == ["git", "ls-files", "--", "Plan/**", "Developer/**"]:
                return subprocess.CompletedProcess(cmd, 0, "", "")
            if cmd == ["git", "ls-files"]:
                return subprocess.CompletedProcess(cmd, 0, "Packages/Demo/Developer/meta.txt\n", "")
            raise AssertionError(cmd)

        with mock.patch.object(self.run_ci.subprocess, "run", side_effect=fake_run):
            self.assertFalse(self.run_ci._check_boundary())

        self.assertIn(["git", "ls-files"], calls)
        self.assertFalse(any(":(glob)" in " ".join(call) for call in calls))

    def test_run_ci_includes_schema_generated_output_freshness(self) -> None:
        """Local CI should reject stale committed schema generator outputs."""
        self.assertEqual(
            "Scripts/schema/validate_schema_generated_outputs.py",
            self.run_ci.SCHEMA_GENERATED_OUTPUT_VALIDATOR,
        )

    def test_validator_msbuild_args_keep_dash_prefixed_property_attached(self) -> None:
        """Argparse must receive dash-prefixed MSBuild properties as option values."""
        self.assertEqual(
            ["--msbuild-prop=-p:BaseOutputPath=C:/ci/bin/"],
            self.run_ci.validator_msbuild_args(["-p:BaseOutputPath=C:/ci/bin/"]),
        )

    def test_package_validators_use_current_python_executable(self) -> None:
        """Local CI should not depend on a bare python command existing."""
        calls: list[list[str]] = []

        def fake_run_parallel(commands: list[tuple[str, list[str]]]) -> dict[str, bool]:
            """Capture CI subprocess commands without executing them."""
            for _label, cmd in commands:
                calls.append(cmd)
            return {label: True for label, _cmd in commands}

        with mock.patch.object(self.run_ci, "run_parallel", side_effect=fake_run_parallel):
            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", "packages"]):
                self.assertEqual(0, self.run_ci.main())

        python_calls = [cmd for cmd in calls if cmd and cmd[1].endswith(".py")]
        self.assertTrue(python_calls)
        self.assertTrue(all(cmd[0] == sys.executable for cmd in python_calls))

    def test_fatal_run_raises_after_printing_failure(self) -> None:
        """Fatal subprocess failures should abort at the point of failure."""
        failed = subprocess.CompletedProcess(args=["tool"], returncode=7, stdout="", stderr="")
        with mock.patch.object(self.run_ci.subprocess, "run", return_value=failed):
            self.assertFalse(self.run_ci.run(["tool"], "nonfatal", fatal=False))
            with self.assertRaises(SystemExit) as context:
                self.run_ci.run(["tool"], "fatal", fatal=True)
        self.assertEqual(7, context.exception.code)

    def test_run_reports_elapsed_seconds_on_success(self) -> None:
        """Direct command success output should include stable one-decimal elapsed time."""
        completed = subprocess.CompletedProcess(args=["tool"], returncode=0, stdout="", stderr="")

        with mock.patch.object(self.run_ci.time, "monotonic", side_effect=[10.0, 11.24]):
            with mock.patch.object(self.run_ci.subprocess, "run", return_value=completed):
                with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                    self.assertTrue(self.run_ci.run(["tool"], "timed success"))

        self.assertIn(f"{self.run_ci.PASS} timed success (1.2s)", stdout.getvalue())

    def test_run_reports_elapsed_seconds_on_nonzero_exit(self) -> None:
        """Direct command failures should retain their exit code and include elapsed time."""
        failed = subprocess.CompletedProcess(args=["tool"], returncode=7, stdout="", stderr="")

        with mock.patch.object(self.run_ci.time, "monotonic", side_effect=[15.0, 16.24]):
            with mock.patch.object(self.run_ci.subprocess, "run", return_value=failed):
                with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                    self.assertFalse(self.run_ci.run(["tool"], "timed failure"))

        self.assertIn(
            f"{self.run_ci.FAIL} timed failure (exit 7) (1.2s)",
            stdout.getvalue(),
        )

    def test_run_captured_returns_elapsed_seconds_on_success(self) -> None:
        """Captured package-validator results should carry elapsed time for ordered replay."""
        completed = subprocess.CompletedProcess(
            args=["tool"],
            returncode=0,
            stdout="validator output\n",
            stderr="",
        )

        with mock.patch.object(self.run_ci.time, "monotonic", side_effect=[20.0, 21.26]):
            with mock.patch.object(self.run_ci.subprocess, "run", return_value=completed):
                result = self.run_ci.run_captured(
                    ["tool"],
                    "captured validator",
                )

        self.assertIsInstance(result, self.run_ci.CapturedCommandResult)
        self.assertEqual("captured validator", result.label)
        self.assertTrue(result.ok)
        self.assertEqual(0, result.returncode)
        self.assertAlmostEqual(1.26, result.elapsed_seconds)
        self.assertEqual("validator output\n", result.stdout)
        self.assertEqual("", result.stderr)
        self.assertIsNone(result.timeout_seconds)

    def test_run_captured_preserves_nonzero_result_without_timeout(self) -> None:
        """Captured non-timeout failures should preserve their process result and elapsed time."""
        failed = subprocess.CompletedProcess(
            args=["tool"],
            returncode=9,
            stdout="validator stdout\n",
            stderr="validator stderr\n",
        )

        with mock.patch.object(self.run_ci.time, "monotonic", side_effect=[25.0, 26.24]):
            with mock.patch.object(self.run_ci.subprocess, "run", return_value=failed):
                result = self.run_ci.run_captured(["tool"], "captured failure")

        self.assertIsInstance(result, self.run_ci.CapturedCommandResult)
        self.assertEqual("captured failure", result.label)
        self.assertFalse(result.ok)
        self.assertEqual(9, result.returncode)
        self.assertAlmostEqual(1.24, result.elapsed_seconds)
        self.assertEqual("validator stdout\n", result.stdout)
        self.assertEqual("validator stderr\n", result.stderr)
        self.assertIsNone(result.timeout_seconds)

    def test_run_captured_timeout_caches_its_effective_timeout(self) -> None:
        """Captured timeout diagnostics should use the one timeout value enforced by subprocess."""
        timeout = subprocess.TimeoutExpired(
            ["tool"],
            7,
            output="partial stdout\n",
            stderr="partial stderr\n",
        )

        with mock.patch.object(self.run_ci.time, "monotonic", side_effect=[30.0, 31.24]):
            with mock.patch.object(
                self.run_ci,
                "command_timeout_seconds",
                side_effect=[7, 99],
            ) as command_timeout:
                with mock.patch.object(self.run_ci.subprocess, "run", side_effect=timeout) as run_process:
                    result = self.run_ci.run_captured(["tool"], "captured timeout")

        command_timeout.assert_called_once_with()
        self.assertEqual(7, run_process.call_args.kwargs["timeout"])
        self.assertIsInstance(result, self.run_ci.CapturedCommandResult)
        self.assertFalse(result.ok)
        self.assertEqual(124, result.returncode)
        self.assertAlmostEqual(1.24, result.elapsed_seconds)
        self.assertEqual(7, result.timeout_seconds)
        self.assertEqual("partial stdout\n", result.stdout)
        self.assertEqual("partial stderr\n", result.stderr)

    def test_run_parallel_replays_ordered_command_elapsed_time(self) -> None:
        """Parallel validator replay should retain labels, output order, return codes, and elapsed time."""
        captured_results = {
            "first validator": self.run_ci.CapturedCommandResult(
                label="first validator",
                ok=True,
                returncode=0,
                elapsed_seconds=1.2,
                stdout="first output\n",
                stderr="",
            ),
            "second validator": self.run_ci.CapturedCommandResult(
                label="second validator",
                ok=False,
                returncode=9,
                elapsed_seconds=4.6,
                stdout="",
                stderr="second error\n",
            ),
        }

        def fake_run_captured(_cmd: list[str], label: str):
            """Return complete captured command results without starting a subprocess."""
            return captured_results[label]

        with mock.patch.object(self.run_ci, "run_captured", side_effect=fake_run_captured):
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                with mock.patch("sys.stderr", new_callable=io.StringIO) as stderr:
                    results = self.run_ci.run_parallel(
                        [
                            ("first validator", ["first"]),
                            ("second validator", ["second"]),
                        ]
                    )

        rendered_stdout = stdout.getvalue()
        self.assertEqual({"first validator": True, "second validator": False}, results)
        self.assertIn("--- first validator ---", rendered_stdout)
        self.assertIn("first output", rendered_stdout)
        self.assertIn(f"{self.run_ci.PASS} first validator (1.2s)", rendered_stdout)
        self.assertIn("--- second validator ---", rendered_stdout)
        self.assertIn(
            f"{self.run_ci.FAIL} second validator (exit 9) (4.6s)",
            rendered_stdout,
        )
        self.assertLess(rendered_stdout.index("--- first validator ---"), rendered_stdout.index("--- second validator ---"))
        self.assertLess(rendered_stdout.index("first output"), rendered_stdout.index("--- second validator ---"))
        self.assertIn("second error", stderr.getvalue())

    def test_run_parallel_replays_captured_timeout_once_in_declaration_order(self) -> None:
        """Captured timeouts should retain partial output and use one timeout-specific replay diagnostic."""
        captured_results = {
            "first validator": self.run_ci.CapturedCommandResult(
                label="first validator",
                ok=True,
                returncode=0,
                elapsed_seconds=1.2,
                stdout="first output\n",
                stderr="",
            ),
            "timeout validator": self.run_ci.CapturedCommandResult(
                label="timeout validator",
                ok=False,
                returncode=124,
                elapsed_seconds=4.6,
                stdout="partial timeout stdout\n",
                stderr="partial timeout stderr\n",
                timeout_seconds=7,
            ),
        }

        def fake_run_captured(_cmd: list[str], label: str):
            """Return captured results without starting a subprocess."""
            return captured_results[label]

        with mock.patch.object(self.run_ci, "run_captured", side_effect=fake_run_captured):
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                with mock.patch("sys.stderr", new_callable=io.StringIO) as stderr:
                    results = self.run_ci.run_parallel(
                        [
                            ("first validator", ["first"]),
                            ("timeout validator", ["timeout"]),
                        ]
                    )

        rendered_stdout = stdout.getvalue()
        rendered_stderr = stderr.getvalue()
        rendered = rendered_stdout + rendered_stderr
        self.assertEqual({"first validator": True, "timeout validator": False}, results)
        self.assertLess(rendered_stdout.index("--- first validator ---"), rendered_stdout.index("--- timeout validator ---"))
        self.assertLess(rendered_stdout.index("first output"), rendered_stdout.index("--- timeout validator ---"))
        self.assertIn("partial timeout stdout", rendered_stdout)
        self.assertIn("partial timeout stderr", rendered_stderr)
        self.assertIn(
            f"{self.run_ci.FAIL} timeout validator timed out after 7s (4.6s)",
            rendered_stdout,
        )
        self.assertEqual(1, rendered.count(f"{self.run_ci.FAIL} timeout validator"))
        self.assertNotIn(f"{self.run_ci.FAIL} timeout validator (exit 124)", rendered)

    def test_run_timeout_reports_reason_and_elapsed_seconds(self) -> None:
        """Timed-out direct commands should retain their limit and show elapsed time."""
        with mock.patch.object(self.run_ci.time, "monotonic", side_effect=[30.0, 31.28]):
            with mock.patch.object(
                self.run_ci.subprocess,
                "run",
                side_effect=subprocess.TimeoutExpired(["tool"], 7),
            ):
                with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                    self.assertFalse(self.run_ci.run(["tool"], "timeout", timeout_seconds=7))

        rendered = stdout.getvalue()
        self.assertIn(f"{self.run_ci.FAIL} timeout timed out after 7s", rendered)
        self.assertIn("(1.3s)", rendered)

    def test_run_ci_reports_timeout_without_hanging(self) -> None:
        """Subprocess timeouts should fail the command instead of hanging local CI."""
        with mock.patch.dict(os.environ, {"UNITY2FOXGLOVE_CI_TIMEOUT": "1"}):
            with mock.patch.object(self.run_ci.subprocess, "run", side_effect=subprocess.TimeoutExpired(["tool"], 1)):
                self.assertFalse(self.run_ci.run(["tool"], "timeout", fatal=False))
                with self.assertRaises(SystemExit) as context:
                    self.run_ci.run(["tool"], "fatal-timeout", fatal=True)
        self.assertEqual(124, context.exception.code)

    def test_run_can_disable_wall_clock_timeout(self) -> None:
        """Finite gates should be allowed to finish without a machine-specific deadline."""
        completed = subprocess.CompletedProcess(args=["tool"], returncode=0, stdout="", stderr="")

        with mock.patch.object(self.run_ci.subprocess, "run", return_value=completed) as run_process:
            self.assertTrue(self.run_ci.run(["tool"], "finite gate", disable_timeout=True))

        self.assertIsNone(run_process.call_args.kwargs["timeout"])

    def test_default_ci_builds_independent_subcommand_jobs(self) -> None:
        """Default local CI should enqueue every dotnet lane as a self-subcommand."""
        args = types.SimpleNamespace(skip_analyzer=False)

        jobs = self.run_ci.build_default_ci_jobs(args)

        self.assertEqual(
            [
                "analyzer",
                "dotnet-runtime",
                "xunit",
                "xunit-adapter",
                "xunit-native",
                "foxrun-publish-panel",
                "phase179-ros2-regression",
                "phase181-ros2-regression",
                "phase184-acceptance-tooling",
                "mcap-conformance",
                "packages",
                "boundary",
            ],
            [job.name for job in jobs],
        )
        for job in jobs:
            self.assertEqual(
                [sys.executable, str(RUN_CI_PATH.resolve()), "--only", job.name],
                job.command,
        )
        self.assertEqual(
            {"mcap-conformance", "phase184-acceptance-tooling"},
            {job.name for job in jobs if job.disable_timeout},
        )

    def test_phase184_acceptance_regression_module_constants_are_exact(self) -> None:
        """The tooling lane must name only the six maintained pure unittest modules."""
        expected = {
            name: module
            for name, module, _label in self.PHASE184_ACCEPTANCE_TOOLING_SUITES
        }

        self.assertEqual(
            expected,
            {name: getattr(self.run_ci, name, None) for name in expected},
        )

    def test_phase184_acceptance_regressions_have_a_truthful_dedicated_lane(self) -> None:
        """The selector must execute exactly six pure unittest suites in locked order."""
        with mock.patch.object(self.run_ci, "run", return_value=True) as run:
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "phase184-acceptance-tooling"],
            ):
                self.assertEqual(0, self.run_ci.main())

        observed = [(call.args[0], call.args[1]) for call in run.call_args_list]
        self.assertEqual(
            [
                ([sys.executable, "-m", "unittest", module], label)
                for _name, module, label in self.PHASE184_ACCEPTANCE_TOOLING_SUITES
            ],
            observed,
        )
        self.assertTrue(
            all(
                call.kwargs.get("disable_timeout", False) is False
                for call in run.call_args_list
            )
        )

        phase184_modules = [
            module
            for _name, module, _label in self.PHASE184_ACCEPTANCE_TOOLING_SUITES
        ]
        self.assertTrue(
            all(
                command[:3] == [sys.executable, "-m", "unittest"]
                and len(command) == 4
                for command, _label in observed
            )
        )
        self.assertTrue(
            all(
                module.startswith("Scripts.smoke.foxrun.regression_checks.test_")
                for module in phase184_modules
            )
        )
        self.assertTrue(
            {
                "Scripts.smoke.foxrun.phase184_profile_acceptance",
                "Scripts.smoke.foxrun.phase184_foxglove_cli_install",
                "Scripts.smoke.foxrun.phase184_foxglove_desktop_live_acceptance",
            }.isdisjoint(phase184_modules)
        )

        with mock.patch.object(self.run_ci, "run", return_value=True) as phase181_run:
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "phase181-ros2-regression"],
            ):
                self.assertEqual(0, self.run_ci.main())

        phase181_commands = {
            tuple(call.args[0])
            for call in phase181_run.call_args_list
        }
        self.assertTrue(
            all(
                (sys.executable, "-m", "unittest", module)
                not in phase181_commands
                for module in phase184_modules
            )
        )

    def test_phase184_acceptance_tooling_lane_propagates_failure(self) -> None:
        """A failing Phase184 tooling suite must fail its dedicated selector."""

        with mock.patch.object(
            self.run_ci,
            "run",
            side_effect=(False, True, True, True, True, True),
        ):
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "phase184-acceptance-tooling"],
            ):
                self.assertEqual(1, self.run_ci.main())

    def test_only_help_lists_phase184_acceptance_tooling_selector(self) -> None:
        """CLI help must name the dedicated Phase184 tooling lane honestly."""

        with mock.patch.object(sys, "argv", ["run_ci.py", "--help"]):
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                with self.assertRaises(SystemExit) as context:
                    self.run_ci.main()

        self.assertEqual(0, context.exception.code)
        self.assertIn("phase184-acceptance-tooling", stdout.getvalue())

    def test_phase184_acceptance_tooling_command_is_exact(self) -> None:
        """The dedicated lane must remain a tooling test, not claim runtime execution."""

        jobs = self.run_ci.build_default_ci_jobs(
            types.SimpleNamespace(skip_analyzer=False)
        )
        job = next(
            candidate
            for candidate in jobs
            if candidate.name == "phase184-acceptance-tooling"
        )
        self.assertEqual(
            (
                sys.executable,
                str(RUN_CI_PATH.resolve()),
                "--only",
                "phase184-acceptance-tooling",
            ),
            tuple(job.command),
        )

    def test_only_help_lists_dotnet_lane_selectors(self) -> None:
        """CLI help should expose each direct dotnet lane selector."""
        with mock.patch.object(sys, "argv", ["run_ci.py", "--help"]):
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                with self.assertRaises(SystemExit) as context:
                    self.run_ci.main()

        self.assertEqual(0, context.exception.code)
        for selector in ("dotnet-runtime", "xunit", "xunit-adapter", "xunit-native"):
            help_pattern = re.escape(selector).replace(r"\-", r"-\s*")
            self.assertRegex(stdout.getvalue(), help_pattern)

    def test_default_ci_marks_only_analyzer_and_dotnet_lanes_exclusive(self) -> None:
        """Resource-heavy analyzer and dotnet jobs should serialize without blocking unrelated work."""
        jobs = self.run_ci.build_default_ci_jobs(types.SimpleNamespace(skip_analyzer=False))

        self.assertEqual("dotnet", self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP)
        self.assertEqual(
            {
                "analyzer": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "dotnet-runtime": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "xunit": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "xunit-adapter": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "xunit-native": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "foxrun-publish-panel": None,
                "phase179-ros2-regression": None,
                "phase181-ros2-regression": None,
                "phase184-acceptance-tooling": None,
                "mcap-conformance": None,
                "packages": None,
                "boundary": None,
            },
            {job.name: job.exclusive_group for job in jobs},
        )

    def test_main_dispatches_dotnet_parent_through_flat_parallel_jobs(self) -> None:
        """The dotnet parent selection should use the top-level lane scheduler."""
        observed: dict[str, object] = {}

        def fake_run_ci_jobs(jobs, max_workers):
            """Capture the flattened lane jobs without executing subprocesses."""
            observed["names"] = [job.name for job in jobs]
            observed["max_workers"] = max_workers
            return {job.name: True for job in jobs}

        with mock.patch.object(self.run_ci, "run_ci_jobs", side_effect=fake_run_ci_jobs):
            with mock.patch.object(self.run_ci, "restore_with_ignoring_failed_sources", return_value=True):
                with mock.patch.object(self.run_ci, "run_with_restore_fallback", return_value=True):
                    with mock.patch.object(sys, "argv", ["run_ci.py", "--only", "dotnet", "--jobs", "2"]):
                        self.assertEqual(0, self.run_ci.main())

        self.assertEqual(
            ["dotnet-runtime", "xunit", "xunit-adapter", "xunit-native"],
            observed.get("names"),
        )
        self.assertEqual(2, observed.get("max_workers"))

    def _flat_dotnet_lane_cases(self):
        """Return the exact restore and command contracts for every flat dotnet lane."""
        return [
            (
                "dotnet-runtime",
                self.run_ci.RUNTIME_TESTS_PROJ,
                self.run_ci.RUNTIME_TEST_PROPS,
                "Restore runtime test project",
                [
                    "dotnet",
                    "run",
                    "--no-restore",
                    "--project",
                    self.run_ci.RUNTIME_TESTS_PROJ,
                    *self.run_ci.RUNTIME_TEST_PROPS,
                ],
                "Dotnet validation suite (default CI)",
                None,
                None,
            ),
            (
                "xunit",
                self.run_ci.UNIT_TESTS_PROJ,
                self.run_ci.UNIT_TEST_PROPS,
                "Restore xUnit unit test project",
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    self.run_ci.UNIT_TESTS_PROJ,
                    *self.run_ci.UNIT_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests.trx",
                    "--results-directory",
                    str(self.run_ci.UNIT_TEST_RESULTS_DIR),
                ],
                "xUnit unit tests",
                "unit-tests.trx",
                self.run_ci.CI_ROOT / "test-results" / "unit",
            ),
            (
                "xunit-adapter",
                self.run_ci.UNIT_TESTS_PROJ,
                self.run_ci.UNIT_ADAPTER_TEST_PROPS,
                "Restore xUnit optional ROS2 adapter lane",
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    self.run_ci.UNIT_TESTS_PROJ,
                    *self.run_ci.UNIT_ADAPTER_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests-adapter.trx",
                    "--results-directory",
                    str(self.run_ci.UNIT_ADAPTER_TEST_RESULTS_DIR),
                ],
                "xUnit optional ROS2 adapter unit tests",
                "unit-tests-adapter.trx",
                self.run_ci.CI_ROOT / "test-results" / "unit-adapter",
            ),
            (
                "xunit-native",
                self.run_ci.UNIT_TESTS_PROJ,
                self.run_ci.UNIT_NATIVE_TEST_PROPS,
                "Restore xUnit Native ROS2 compilation lane",
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    self.run_ci.UNIT_TESTS_PROJ,
                    *self.run_ci.UNIT_NATIVE_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests-native.trx",
                    "--results-directory",
                    str(self.run_ci.UNIT_NATIVE_TEST_RESULTS_DIR),
                ],
                "xUnit Native ROS2 compilation unit tests",
                "unit-tests-native.trx",
                self.run_ci.CI_ROOT / "test-results" / "unit-native",
            ),
        ]

    def test_flat_dotnet_selectors_restore_and_run_isolated_lanes(self) -> None:
        """Each flat selector should restore its isolated lane then run it once without restore."""
        cases = self._flat_dotnet_lane_cases()
        xunit_artifacts: list[tuple[str, str]] = []
        lane_msbuild_roots: list[tuple[str, ...]] = []

        for (
            selector,
            project,
            props,
            restore_label,
            command,
            run_label,
            trx_name,
            results_dir,
        ) in cases:
            with self.subTest(selector=selector):
                with mock.patch.object(
                    self.run_ci,
                    "restore_with_ignoring_failed_sources",
                    return_value=True,
                ) as restore:
                    with mock.patch.object(self.run_ci, "run", return_value=True) as run:
                        with mock.patch.object(self.run_ci, "run_with_restore_fallback") as fallback:
                            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", selector]):
                                self.assertEqual(0, self.run_ci.main())

                restore.assert_called_once_with(project, restore_label, props, fatal=False)
                run.assert_called_once_with(command, run_label)
                fallback.assert_not_called()
                self.assertIn("--no-restore", command)
                isolated_roots = [
                    prop.split("=", 1)[1]
                    for prop in props
                    if prop.startswith(
                        (
                            "-p:BaseOutputPath=",
                            "-p:BaseIntermediateOutputPath=",
                            "-p:MSBuildProjectExtensionsPath=",
                            "-p:RestoreOutputPath=",
                        )
                    )
                ]
                self.assertEqual(4, len(isolated_roots))
                isolated_root = str(self.run_ci.ISOLATED_DOTNET_ROOT.resolve()).replace("\\", "/") + "/"
                self.assertTrue(all(root.startswith(isolated_root) for root in isolated_roots))
                lane_msbuild_roots.append(
                    tuple(root.replace("\\", "/").rstrip("/") for root in isolated_roots)
                )

                if selector == "xunit-adapter":
                    self.assertIn("-p:IncludeRos2ForUnityAdapter=true", props)
                if selector == "xunit-native":
                    self.assertIn("-p:IncludeRos2ForUnityNative=true", props)
                if trx_name is not None:
                    self.assertIsNotNone(results_dir)
                    self.assertIn(f"trx;LogFileName={trx_name}", command)
                    self.assertIn(str(results_dir), command)
                    xunit_artifacts.append((trx_name, str(results_dir)))

        self.assertEqual(3, len({trx_name for trx_name, _ in xunit_artifacts}))
        self.assertEqual(3, len({results_dir for _, results_dir in xunit_artifacts}))
        self.assertEqual(4, len(lane_msbuild_roots))
        self.assertEqual(4, len(set(lane_msbuild_roots)))

    def test_flat_dotnet_lane_failure_does_not_retry_with_restore(self) -> None:
        """Every failed lane should report failure after one no-restore command."""
        for (
            selector,
            project,
            props,
            restore_label,
            command,
            run_label,
            _trx_name,
            _results_dir,
        ) in self._flat_dotnet_lane_cases():
            with self.subTest(selector=selector):
                with mock.patch.object(
                    self.run_ci,
                    "restore_with_ignoring_failed_sources",
                    return_value=True,
                ) as restore:
                    with mock.patch.object(self.run_ci, "run", return_value=False) as run:
                        with mock.patch.object(self.run_ci, "run_with_restore_fallback") as fallback:
                            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", selector]):
                                self.assertEqual(1, self.run_ci.main())

                restore.assert_called_once_with(project, restore_label, props, fatal=False)
                run.assert_called_once_with(command, run_label)
                self.assertIn("--no-restore", command)
                fallback.assert_not_called()

    def test_flat_dotnet_lane_restore_failure_skips_the_no_restore_command(self) -> None:
        """Every failed explicit restore should fail its lane without running its test command."""
        for (
            selector,
            project,
            props,
            restore_label,
            _command,
            _run_label,
            _trx_name,
            _results_dir,
        ) in self._flat_dotnet_lane_cases():
            with self.subTest(selector=selector):
                with mock.patch.object(
                    self.run_ci,
                    "restore_with_ignoring_failed_sources",
                    return_value=False,
                ) as restore:
                    with mock.patch.object(self.run_ci, "run") as run:
                        with mock.patch.object(self.run_ci, "run_with_restore_fallback") as fallback:
                            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", selector]):
                                self.assertEqual(1, self.run_ci.main())

                restore.assert_called_once_with(project, restore_label, props, fatal=False)
                run.assert_not_called()
                fallback.assert_not_called()

    def test_run_ci_jobs_limits_active_jobs_until_release(self) -> None:
        """The real top-level scheduler should not start a third job before release."""
        jobs = [
            self.run_ci.CiJob("first", ["first"]),
            self.run_ci.CiJob("second", ["second"]),
            self.run_ci.CiJob("third", ["third"]),
        ]
        release = threading.Event()
        two_jobs_started = threading.Event()
        third_job_started = threading.Event()
        state_lock = threading.Lock()
        state = {"active": 0, "max_active": 0, "started_before_release": []}
        outcome: dict[str, object] = {}
        worker_errors: list[BaseException] = []

        def fake_run_ci_job(job, log_dir):
            """Hold worker slots until the test releases the synthetic jobs."""
            with state_lock:
                state["active"] += 1
                state["max_active"] = max(state["max_active"], state["active"])
                if not release.is_set():
                    state["started_before_release"].append(job.name)
                    if len(state["started_before_release"]) == 2:
                        two_jobs_started.set()
                    elif len(state["started_before_release"]) == 3:
                        third_job_started.set()
            try:
                release.wait()
                return self.run_ci.CiJobResult(job.name, True, 0, 0.0, log_dir / f"{job.name}.log")
            finally:
                with state_lock:
                    state["active"] -= 1

        def run_scheduler() -> None:
            """Run the real scheduler without blocking the test thread."""
            try:
                outcome["results"] = self.run_ci.run_ci_jobs(jobs, max_workers=2)
            except BaseException as exc:  # pragma: no cover - asserted by the test thread.
                worker_errors.append(exc)

        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(self.run_ci, "CI_ROOT", Path(temp)):
                with mock.patch.object(self.run_ci, "_run_ci_job", side_effect=fake_run_ci_job):
                    scheduler_thread = threading.Thread(target=run_scheduler)
                    scheduler_thread.start()
                    try:
                        self.assertTrue(two_jobs_started.wait(timeout=2), "two workers did not start")
                        self.assertFalse(
                            third_job_started.wait(timeout=0.5),
                            "a third job started before a worker slot was released",
                        )
                        with state_lock:
                            self.assertEqual(2, len(state["started_before_release"]))
                            self.assertLessEqual(state["max_active"], 2)
                    finally:
                        release.set()
                        scheduler_thread.join(timeout=5)

        self.assertFalse(scheduler_thread.is_alive(), "scheduler did not finish after release")
        self.assertEqual([], worker_errors)
        self.assertEqual({job.name: True for job in jobs}, outcome["results"])

    def test_run_ci_jobs_admits_compatible_work_while_dotnet_group_is_active(self) -> None:
        """A blocked dotnet job should leave a worker free for an unrelated pending job."""
        jobs = [
            types.SimpleNamespace(
                name="dotnet-a",
                command=["dotnet-a"],
                disable_timeout=False,
                exclusive_group="dotnet",
            ),
            types.SimpleNamespace(
                name="dotnet-b",
                command=["dotnet-b"],
                disable_timeout=False,
                exclusive_group="dotnet",
            ),
            types.SimpleNamespace(
                name="other",
                command=["other"],
                disable_timeout=False,
                exclusive_group=None,
            ),
        ]
        dotnet_a_started = threading.Event()
        dotnet_a_release = threading.Event()
        dotnet_b_started = threading.Event()
        other_finished = threading.Event()
        started_names: list[str] = []
        state_lock = threading.Lock()
        outcome: dict[str, object] = {}
        worker_errors: list[BaseException] = []

        def fake_run_ci_job(job, log_dir):
            """Control only worker completion while retaining the real admission scheduler."""
            with state_lock:
                started_names.append(job.name)
            if job.name == "dotnet-a":
                dotnet_a_started.set()
                dotnet_a_release.wait()
            elif job.name == "dotnet-b":
                dotnet_b_started.set()
            elif job.name == "other":
                other_finished.set()
            return self.run_ci.CiJobResult(job.name, True, 0, 0.0, log_dir / f"{job.name}.log")

        def run_scheduler() -> None:
            """Run the real scheduler off the test thread so it can await controlled workers."""
            try:
                outcome["results"] = self.run_ci.run_ci_jobs(jobs, max_workers=2)
            except BaseException as exc:  # pragma: no cover - asserted by the test thread.
                worker_errors.append(exc)

        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(self.run_ci, "CI_ROOT", Path(temp)):
                with mock.patch.object(self.run_ci, "_run_ci_job", side_effect=fake_run_ci_job):
                    scheduler_thread = threading.Thread(target=run_scheduler)
                    scheduler_thread.start()
                    try:
                        self.assertTrue(dotnet_a_started.wait(timeout=2), "dotnet-a did not start")
                        self.assertTrue(other_finished.wait(timeout=2), "compatible job did not start")
                        self.assertFalse(
                            dotnet_b_started.wait(timeout=0.5),
                            "second dotnet job started before the active group released",
                        )
                        with state_lock:
                            self.assertEqual({"dotnet-a", "other"}, set(started_names))
                        dotnet_a_release.set()
                        self.assertTrue(dotnet_b_started.wait(timeout=2), "dotnet-b did not start after release")
                    finally:
                        dotnet_a_release.set()
                        scheduler_thread.join(timeout=5)

        self.assertFalse(scheduler_thread.is_alive(), "scheduler did not finish after release")
        self.assertEqual([], worker_errors)
        self.assertEqual(
            [("dotnet-a", True), ("dotnet-b", True), ("other", True)],
            list(outcome["results"].items()),
        )

    def test_run_ci_jobs_releases_dotnet_group_after_normal_failure(self) -> None:
        """A normal failed grouped job should release its group for the next grouped job."""
        jobs = [
            types.SimpleNamespace(
                name="dotnet-a",
                command=["dotnet-a"],
                disable_timeout=False,
                exclusive_group="dotnet",
            ),
            types.SimpleNamespace(
                name="dotnet-b",
                command=["dotnet-b"],
                disable_timeout=False,
                exclusive_group="dotnet",
            ),
        ]
        dotnet_a_started = threading.Event()
        dotnet_a_release = threading.Event()
        dotnet_b_started = threading.Event()
        outcome: dict[str, object] = {}
        worker_errors: list[BaseException] = []

        def fake_run_ci_job(job, log_dir):
            """Return an ordinary failed result for the group owner after controlled release."""
            if job.name == "dotnet-a":
                dotnet_a_started.set()
                dotnet_a_release.wait()
                log_path = log_dir / f"{job.name}.log"
                log_path.write_text("synthetic normal failure\n", encoding="utf-8")
                return self.run_ci.CiJobResult(
                    job.name,
                    False,
                    1,
                    0.0,
                    log_path,
                )
            dotnet_b_started.set()
            return self.run_ci.CiJobResult(job.name, True, 0, 0.0, log_dir / f"{job.name}.log")

        def run_scheduler() -> None:
            """Run the real scheduler off the test thread so it can await controlled workers."""
            try:
                outcome["results"] = self.run_ci.run_ci_jobs(jobs, max_workers=2)
            except BaseException as exc:  # pragma: no cover - asserted by the test thread.
                worker_errors.append(exc)

        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(self.run_ci, "CI_ROOT", Path(temp)):
                with mock.patch.object(self.run_ci, "_run_ci_job", side_effect=fake_run_ci_job):
                    scheduler_thread = threading.Thread(target=run_scheduler)
                    scheduler_thread.start()
                    try:
                        self.assertTrue(dotnet_a_started.wait(timeout=2), "dotnet-a did not start")
                        self.assertFalse(
                            dotnet_b_started.wait(timeout=0.5),
                            "dotnet-b started before the failed owner released its group",
                        )
                        dotnet_a_release.set()
                        self.assertTrue(dotnet_b_started.wait(timeout=2), "dotnet-b did not start after failure")
                    finally:
                        dotnet_a_release.set()
                        scheduler_thread.join(timeout=5)

        self.assertFalse(scheduler_thread.is_alive(), "scheduler did not finish after failure")
        self.assertEqual([], worker_errors)
        self.assertEqual(
            [("dotnet-a", False), ("dotnet-b", True)],
            list(outcome["results"].items()),
        )

    def test_mcap_conformance_disables_wall_clock_timeout(self) -> None:
        """The external differential gate should not assume how fast the host machine is."""
        observed: dict[str, object] = {}

        def fake_run(cmd, label, **kwargs):
            """Capture the dedicated conformance command without executing it."""
            observed["cmd"] = cmd
            observed["label"] = label
            observed["disable_timeout"] = kwargs.get("disable_timeout")
            return True

        with mock.patch.object(self.run_ci, "run", side_effect=fake_run):
            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", "mcap-conformance"]):
                self.assertEqual(0, self.run_ci.main())

        self.assertIn("run_phase121_conformance.py", " ".join(observed["cmd"]))
        self.assertEqual("Official MCAP differential conformance", observed["label"])
        self.assertIs(True, observed["disable_timeout"])

    def test_parallel_mcap_job_disables_wall_clock_timeout(self) -> None:
        """The parent CI process must not reintroduce a deadline around the MCAP child."""
        completed = subprocess.CompletedProcess(args=["tool"], returncode=0, stdout="", stderr="")

        with tempfile.TemporaryDirectory() as temp:
            job = self.run_ci.CiJob("mcap-conformance", ["tool"], disable_timeout=True)
            with mock.patch.object(self.run_ci.subprocess, "run", return_value=completed) as run_process:
                result = self.run_ci._run_ci_job(job, Path(temp))

        self.assertTrue(result.ok)
        self.assertIsNone(run_process.call_args.kwargs["timeout"])

    def test_main_dispatches_default_ci_through_parallel_jobs(self) -> None:
        """Without --only, CI should use the parallel job runner and aggregate job results."""
        observed: dict[str, object] = {}

        def fake_run_ci_jobs(jobs, max_workers):
            """Capture default CI jobs without executing subprocesses."""
            observed["names"] = [job.name for job in jobs]
            observed["max_workers"] = max_workers
            return {job.name: True for job in jobs}

        with mock.patch.object(self.run_ci, "run_ci_jobs", side_effect=fake_run_ci_jobs):
            with mock.patch.object(sys, "argv", ["run_ci.py", "--jobs", "2"]):
                self.assertEqual(0, self.run_ci.main())

        self.assertEqual(
            [
                "analyzer",
                "dotnet-runtime",
                "xunit",
                "xunit-adapter",
                "xunit-native",
                "foxrun-publish-panel",
                "phase179-ros2-regression",
                "phase181-ros2-regression",
                "phase184-acceptance-tooling",
                "mcap-conformance",
                "packages",
                "boundary",
            ],
            observed["names"],
        )
        self.assertEqual(2, observed["max_workers"])


class LocalEntrypointValidatorTests(unittest.TestCase):
    """Regression coverage for machine-local path detection boundaries."""

    def setUp(self) -> None:
        """Load a fresh local-entrypoint validator for each test."""
        self.validator = load_module(
            "validate_local_entrypoints_under_test",
            LOCAL_ENTRYPOINT_VALIDATOR_PATH,
        )

    def test_release_asset_rule_allows_host_allowlists_but_rejects_concrete_urls(self) -> None:
        """A trusted host constant is not itself a temporary signed asset URL."""
        pattern = next(
            pattern
            for label, pattern in self.validator.FORBIDDEN_PATTERNS
            if label == "temporary GitHub signed release asset URL"
        )

        self.assertIsNone(pattern.search('"release-assets.githubusercontent.com",'))
        self.assertIsNotNone(
            pattern.search(
                "https://release-assets.githubusercontent.com/"
                "github-production-release-asset/431693744/object?sig=opaque"
            )
        )

    def test_git_grep_excludes_regression_fixtures(self) -> None:
        """Intentional invalid-path fixtures must not be treated as production defaults."""
        completed = subprocess.CompletedProcess(args=[], returncode=1, stdout="", stderr="")
        with mock.patch.object(self.validator.subprocess, "run", return_value=completed) as run:
            self.assertEqual(
                [],
                self.validator.git_grep_failures(
                    "temporary GitHub signed release asset URL",
                    self.validator.FORBIDDEN_PATTERNS[-1][1],
                ),
            )

        command = run.call_args.args[0]
        self.assertIn(
            ":(exclude,glob)Scripts/**/regression_checks/**/*.py",
            command,
        )


class McapConformanceToolTests(unittest.TestCase):
    """Regression coverage for the release-blocking official MCAP differential."""

    def setUp(self) -> None:
        """Load a fresh conformance wrapper for each test."""
        self.conformance = load_module("mcap_conformance_under_test", MCAP_CONFORMANCE_PATH)

    def test_official_conformance_stages_have_no_hardcoded_deadlines(self) -> None:
        """Official finite stages should run to completion independent of host speed."""
        tree = ast.parse(MCAP_CONFORMANCE_PATH.read_text(encoding="utf-8"))
        timeout_calls = []
        for node in ast.walk(tree):
            if not isinstance(node, ast.Call):
                continue
            if not any(keyword.arg == "timeout_seconds" for keyword in node.keywords):
                continue
            function_name = node.func.id if isinstance(node.func, ast.Name) else ""
            if function_name in {"invoke_command_capture", "run_package_manager"}:
                timeout_calls.append(node.lineno)

        self.assertEqual([], timeout_calls)

    def test_timeout_failure_details_preserve_the_timeout_reason(self) -> None:
        """Large runner output must not truncate away the actionable timeout diagnosis."""
        stdout = "running csharp-streamed-reader\n" + "\n".join(
            f"  testing fixture-{index}.mcap" for index in range(600)
        )
        result = self.conformance.CommandResult(
            -1,
            stdout,
            "Timed out after 180 second(s).",
            "runner command",
            timed_out=True,
        )

        report = self.conformance.measure_runner_output(
            "csharp-streamed-reader",
            "streamed-reader",
            result,
        )

        failure = report["failures"][0]
        self.assertTrue(failure["timedOut"])
        self.assertIn("Timed out after 180 second(s).", failure["details"])


class R2fuArtifactHandoffTests(unittest.TestCase):
    """Keep source gates aligned with the current verified R2FU artifact handoff."""

    def test_v090_runtime_artifact_pins_are_consistent(self) -> None:
        """Every source gate must name the three verified v0.9.0 artifact digests."""
        expected_by_path = {
            "Scripts/ros2forunity/windows/humble/sync_r2fu_artifact_to_unity2foxglove.py": "6937f348b2abdf40614379173bb81ba55090dc1541cab616d1a0f1e248ceb5b0",
            "Scripts/ros2forunity/windows/humble/validate_ros2forunity_package.py": "6937f348b2abdf40614379173bb81ba55090dc1541cab616d1a0f1e248ceb5b0",
            "Scripts/ros2forunity/windows/jazzy/sync_r2fu_artifact_to_unity2foxglove.py": "4e5cb8b0073d4a34d194b9a6ce0b3449220085f3cfd041b2fd33622e6442ff5d",
            "Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py": "4e5cb8b0073d4a34d194b9a6ce0b3449220085f3cfd041b2fd33622e6442ff5d",
            "Scripts/ros2forunity/windows/jazzy/build_r2fu_runtime_package.py": "4e5cb8b0073d4a34d194b9a6ce0b3449220085f3cfd041b2fd33622e6442ff5d",
            "Scripts/ros2forunity/windows/lyrical/lyrical_artifact_config.py": "b31f12cccd2c702ec18c5f5ededce9239d8a2bbe244d54b5526606a96a3a5b71",
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/R2fuHumbleRuntimePackageValidation.cs": "6937f348b2abdf40614379173bb81ba55090dc1541cab616d1a0f1e248ceb5b0",
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/R2fuJazzyRuntimeRefreshValidation.cs": "4e5cb8b0073d4a34d194b9a6ce0b3449220085f3cfd041b2fd33622e6442ff5d",
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/R2fuLyricalRuntimePackageValidation.cs": "b31f12cccd2c702ec18c5f5ededce9239d8a2bbe244d54b5526606a96a3a5b71",
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase107Validation.cs": "4e5cb8b0073d4a34d194b9a6ce0b3449220085f3cfd041b2fd33622e6442ff5d",
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase128Validation.cs": "4e5cb8b0073d4a34d194b9a6ce0b3449220085f3cfd041b2fd33622e6442ff5d",
        }

        for relative_path, expected_sha256 in expected_by_path.items():
            with self.subTest(path=relative_path):
                source = (ROOT / relative_path).read_text(encoding="utf-8")
                self.assertIn(expected_sha256, source)

    def test_runtime_adoption_sync_updates_only_the_selected_distro(self) -> None:
        """Serial runtime refreshes must preserve every other distro's verified metadata."""
        from Scripts.ros2forunity.windows.runtime_adoption_manifest import (
            sync_runtime_adoption_manifest,
        )

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package_name = "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64"
            compliance = root / "Packages/dev.unity2foxglove.ros2forunity/Compliance"
            package = root / "Packages" / package_name
            (package / "RuntimeSupport").mkdir(parents=True)
            compliance.mkdir(parents=True)
            adoption = {
                "currentRecommendedRuntime": {
                    "packageName": package_name,
                    "artifactSha256": "old-jazzy",
                    "artifactSize": 1,
                    "inventoryFileCount": 2,
                    "criticalRuntimeFiles": ["keep-current.dll"],
                },
                "supportedRuntimePackages": [
                    {
                        "packageName": "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
                        "artifactSha256": "keep-humble",
                        "artifactSize": 10,
                    },
                    {
                        "packageName": package_name,
                        "artifactSha256": "old-jazzy",
                        "artifactSize": 1,
                        "inventoryFileCount": 2,
                        "criticalRuntimeFiles": ["keep-supported.dll"],
                    },
                ],
            }
            (compliance / "ros2-for-unity-adoption-manifest.json").write_text(
                json.dumps(adoption), encoding="utf-8"
            )
            (package / "RuntimeSupport/runtime-manifest.json").write_text(
                json.dumps(
                    {
                        "artifactSha256": "new-jazzy",
                        "artifactSize": 123,
                        "inventoryFileCount": 456,
                        "criticalRuntimeFiles": ["runtime-only.dll"],
                    }
                ),
                encoding="utf-8",
            )

            sync_runtime_adoption_manifest(
                root,
                package,
                package_name,
                update_current_recommended=True,
            )

            updated = json.loads(
                (compliance / "ros2-for-unity-adoption-manifest.json").read_text(encoding="utf-8")
            )
            humble, jazzy = updated["supportedRuntimePackages"]
            self.assertEqual("keep-humble", humble["artifactSha256"])
            self.assertEqual("new-jazzy", jazzy["artifactSha256"])
            self.assertEqual(123, jazzy["artifactSize"])
            self.assertEqual(456, jazzy["inventoryFileCount"])
            self.assertEqual(["keep-supported.dll"], jazzy["criticalRuntimeFiles"])
            self.assertEqual("new-jazzy", updated["currentRecommendedRuntime"]["artifactSha256"])
            self.assertEqual(["keep-current.dll"], updated["currentRecommendedRuntime"]["criticalRuntimeFiles"])

    def test_runtime_adoption_sync_rejects_missing_core_artifact_metadata_before_write(self) -> None:
        """A partial runtime manifest must not leave stale adoption fields behind."""
        from Scripts.ros2forunity.windows.runtime_adoption_manifest import (
            sync_runtime_adoption_manifest,
        )

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package_name = "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64"
            compliance = root / "Packages/dev.unity2foxglove.ros2forunity/Compliance"
            package = root / "Packages" / package_name
            (package / "RuntimeSupport").mkdir(parents=True)
            compliance.mkdir(parents=True)
            adoption_path = compliance / "ros2-for-unity-adoption-manifest.json"
            original = {
                "currentRecommendedRuntime": {
                    "packageName": package_name,
                    "artifactSha256": "verified-old",
                    "artifactSize": 123,
                    "inventoryFileCount": 456,
                },
                "supportedRuntimePackages": [
                    {
                        "packageName": package_name,
                        "artifactSha256": "verified-old",
                        "artifactSize": 123,
                        "inventoryFileCount": 456,
                    }
                ],
            }
            adoption_path.write_text(json.dumps(original), encoding="utf-8")
            (package / "RuntimeSupport/runtime-manifest.json").write_text(
                json.dumps(
                    {
                        "artifactSize": 999,
                        "inventoryFileCount": 1000,
                    }
                ),
                encoding="utf-8",
            )

            with self.assertRaisesRegex(RuntimeError, "artifactSha256"):
                sync_runtime_adoption_manifest(
                    root,
                    package,
                    package_name,
                    update_current_recommended=True,
                )

            self.assertEqual(
                original,
                json.loads(adoption_path.read_text(encoding="utf-8")),
            )

    def test_inactive_runtime_syncs_can_preserve_the_selected_runtime(self) -> None:
        """Refreshing a non-selected payload must not rewrite the Unity runtime selection."""
        scripts = {
            "humble": ROOT / "Scripts/ros2forunity/windows/humble/sync_r2fu_artifact_to_unity2foxglove.py",
            "jazzy": ROOT / "Scripts/ros2forunity/windows/jazzy/sync_r2fu_artifact_to_unity2foxglove.py",
        }

        for distro, path in scripts.items():
            with self.subTest(distro=distro), tempfile.TemporaryDirectory() as temp:
                project_root = Path(temp)
                manifest_path = project_root / "Unity2Foxglove/Packages/manifest.json"
                manifest_path.parent.mkdir(parents=True)
                manifest = {
                    "dependencies": {
                        "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64": (
                            "file:../../Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64"
                        )
                    }
                }
                manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
                sync = load_module(f"{distro}_r2fu_sync_under_test", path)

                result = sync.ensure_project_uses_runtime_package(
                    project_root,
                    update=False,
                    require_runtime_dependency=False,
                )

                self.assertFalse(result["manifestUpdated"])
                self.assertFalse(result["runtimeDependencyRequired"])
                self.assertEqual(json.loads(manifest_path.read_text(encoding="utf-8")), manifest)
                source = path.read_text(encoding="utf-8")
                self.assertIn("--skip-project-manifest-check", source)
                self.assertIn("require_runtime_dependency=not args.skip_project_manifest_check", source)

    def test_jazzy_v083_excludes_only_test_typesupport_payload(self) -> None:
        """The current Jazzy gate must not reject real geometry message support as stale debris."""
        paths = (
            ROOT / "Scripts/ros2forunity/windows/jazzy/build_r2fu_runtime_package.py",
            ROOT / "Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py",
        )
        expected_test_only = (
            "test_msgs_complex_nested_key__rosidl_typesupport_c_native.dll",
            "test_msgs_keyed_long__rosidl_typesupport_c_native.dll",
            "test_msgs_keyed_string__rosidl_typesupport_c_native.dll",
            "test_msgs_non_keyed_with_nested_key__rosidl_typesupport_c_native.dll",
        )

        for path in paths:
            with self.subTest(path=path):
                source = path.read_text(encoding="utf-8")
                self.assertIn("V083_EXCLUDED_TEST_TYPESUPPORT_DLLS", source)
                self.assertNotIn("geometry_msgs_velocity_with_covariance_stamped", source)
                for filename in expected_test_only:
                    self.assertIn(filename, source)


class UnityIl2CppBuildTests(unittest.TestCase):
    """Regression coverage for Unity build preflight checks."""

    def setUp(self) -> None:
        """Load a fresh unity_il2cpp module for each test."""
        self.unity_il2cpp = load_module("unity_il2cpp_under_test", UNITY_IL2CPP_PATH)

    def test_generated_artifact_preflight_reports_missing_files(self) -> None:
        """Missing generated files should fail before Unity is started."""
        with tempfile.TemporaryDirectory() as temp:
            failures = self.unity_il2cpp.validate_generated_artifacts(Path(temp))

        self.assertTrue(failures)
        self.assertTrue(any("missing generated artifact" in failure for failure in failures))

    def test_missing_project_pinned_unity_falls_back_to_hub_discovery(self) -> None:
        """Missing ProjectVersion editor should not block newer Hub editor discovery."""
        with tempfile.TemporaryDirectory() as temp:
            project = Path(temp) / "UnityProject"
            settings = project / "ProjectSettings"
            settings.mkdir(parents=True)
            (settings / "ProjectVersion.txt").write_text("m_EditorVersion: 6000.3.14f1\n", encoding="utf-8")

            with mock.patch.object(self.unity_il2cpp.platform, "system", return_value="Windows"):
                with mock.patch.dict(self.unity_il2cpp.os.environ, {"PROGRAMFILES": str(Path(temp) / "ProgramFiles")}, clear=False):
                    self.assertIsNone(self.unity_il2cpp.find_unity_from_project_version(project))

    def test_unity_version_key_ignores_patch_suffix_letters(self) -> None:
        """Unity Hub sorting should compare numeric version components only."""
        path = Path("C:/Program Files/Unity/Hub/Editor/6000.3.14f1/Editor/Unity.exe")

        self.assertEqual((6000, 3, 14, 1), self.unity_il2cpp.unity_version_key(path))

    def test_default_build_dir_uses_utc_stamp(self) -> None:
        """Generated build directories should be timezone-stable in CI logs."""
        build_dir = self.unity_il2cpp.default_build_dir(Path("repo"), "win64")

        self.assertRegex(str(build_dir), r"win64-il2cpp-\d{8}-\d{6}Z$")


if __name__ == "__main__":
    unittest.main()
