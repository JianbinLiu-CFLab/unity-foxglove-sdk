#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for release helper correctness and diagnostics.

from __future__ import annotations

import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
BUMP_VERSION_PATH = ROOT / "Scripts" / "release" / "bump_version.py"
RUN_CI_PATH = ROOT / "Scripts" / "release" / "run_ci.py"
UNITY_IL2CPP_PATH = ROOT / "Scripts" / "unity_build" / "unity_il2cpp.py"


def load_module(name: str, path: Path):
    """Load a Python script as an isolated module."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class VersionBumpTests(unittest.TestCase):
    """Regression coverage for release version synchronization."""

    def setUp(self) -> None:
        """Load a fresh bump_version module for each test."""
        self.bump_module = load_module("bump_version_under_test", BUMP_VERSION_PATH)

    def test_update_readme_keeps_two_newest_release_notes_without_corruption(self) -> None:
        """Trim old release notes without stale-offset deletion."""
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
            self.assertIn("- [v1.9.4 release notes](docs/releases/RELEASE_NOTES_v1.9.4.md)", text)
            self.assertNotIn("RELEASE_NOTES_v1.9.3", text)
            self.assertNotIn("RELEASE_NOTES_v1.9.2", text)
            self.assertNotIn("RELEASE_NOTES_v1.9.1", text)
            self.assertIn("- [Release notes archive](docs/releases/)", text)
            self.assertIn("footer", text)

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
