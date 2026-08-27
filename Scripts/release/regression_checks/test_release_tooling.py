#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for release helper correctness and diagnostics.

from __future__ import annotations

import ast
import ctypes
import importlib.util
import io
import json
import os
import re
import signal
import subprocess
import sys
import tempfile
import threading
import time
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
PHASE186_WINDOWS_LIVE_WORKFLOW_PATH = (
    ROOT / ".github" / "workflows" / "phase186-bridge-windows-live.yml"
)
DOCS_WORKFLOW_PATH = ROOT / ".github" / "workflows" / "docs-check.yml"
DOTNET_WORKFLOW_PATH = ROOT / ".github" / "workflows" / "dotnet-tests.yml"
PACKAGE_WORKFLOW_PATH = ROOT / ".github" / "workflows" / "package-check.yml"
REPOSITORY_BOUNDARY_WORKFLOW_PATH = (
    ROOT / ".github" / "workflows" / "repository-boundary-check.yml"
)
PHASE16_VALIDATION_PATH = (
    ROOT
    / "Packages"
    / "dev.unity2foxglove.sdk"
    / "Tests"
    / "Runtime"
    / "Phase16Validation.cs"
)
WORKFLOW_PATHS = (
    DOCS_WORKFLOW_PATH,
    DOTNET_WORKFLOW_PATH,
    PACKAGE_WORKFLOW_PATH,
    PHASE186_WINDOWS_LIVE_WORKFLOW_PATH,
    REPOSITORY_BOUNDARY_WORKFLOW_PATH,
)


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


def active_workflow_line_index(
    workflow: str,
    exact_line: str,
    start: int = 0,
) -> int:
    """Return the line index of one active exact YAML line."""
    lines = workflow.splitlines()
    for index in range(start, len(lines)):
        line = lines[index]
        if line.lstrip().startswith("#"):
            continue
        if line.strip() == exact_line:
            return index
    raise ValueError(f"active workflow line is missing: {exact_line}")


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

    def test_update_adapter_dependency_syncs_all_optional_packages(self) -> None:
        """Every optional package should depend on the released SDK version."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            paths = [
                root / relative
                for relative in (
                    "Packages/dev.unity2foxglove.ros2forunity/package.json",
                    "Packages/dev.unity2foxglove.ros2bridge/package.json",
                    "Packages/dev.unity2foxglove.remotegateway.win64/package.json",
                )
            ]
            for path in paths:
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

            for path in paths:
                text = path.read_text(encoding="utf-8")
                self.assertIn('"dev.unity2foxglove.sdk": "9.9.9"', text)
                self.assertNotIn('"dev.unity2foxglove.sdk": "1.2.3"', text)

    def test_invalid_release_date_is_rejected_before_repository_writes(self) -> None:
        """Release dates must be canonical, real ISO calendar dates."""
        with mock.patch.object(
            sys,
            "argv",
            ["bump_version.py", "2.0.0", "--date", "2026-02-31"],
        ), mock.patch("sys.stderr", new_callable=io.StringIO) as stderr:
            with self.assertRaises(SystemExit) as context:
                self.bump_module.main()

        self.assertEqual(2, context.exception.code)
        self.assertIn("valid YYYY-MM-DD", stderr.getvalue())

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
    PHASE186_BRIDGE_TOOLING_SUITES = (
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_acceptance_protocol",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_acceptance",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_live",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_certification",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_build",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_capability_probe",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_provenance",
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

    def test_boundary_check_rejects_root_developer_meta(self) -> None:
        """A root Unity folder companion must not bypass the private boundary."""

        def fake_run(cmd, **_kwargs):
            """Expose one tracked root Developer.meta file."""
            if cmd == ["git", "ls-files", "--", "Plan/**", "Developer/**"]:
                return subprocess.CompletedProcess(cmd, 0, "", "")
            if cmd == ["git", "ls-files"]:
                return subprocess.CompletedProcess(cmd, 0, "Developer.meta\n", "")
            raise AssertionError(cmd)

        with mock.patch.object(self.run_ci.subprocess, "run", side_effect=fake_run):
            self.assertFalse(self.run_ci._check_boundary())

    def test_repository_boundary_workflow_triggers_for_deep_private_paths(self) -> None:
        """GitHub must schedule the boundary job for private paths at any depth."""
        workflow = REPOSITORY_BOUNDARY_WORKFLOW_PATH.read_text(encoding="utf-8")
        event_blocks: dict[str, str] = {}
        for event in ("push", "pull_request"):
            match = re.search(
                rf"(?ms)^  {event}:\n(?P<body>.*?)(?=^  [a-z_]+:|^jobs:)",
                workflow,
            )
            self.assertIsNotNone(match, f"missing {event} event block")
            event_blocks[event] = match.group("body")

        required_patterns = {
            "Developer.meta": '      - "Developer.meta"',
            "Unity2Foxglove/Assets/Developer/private.md": '      - "**/Developer/**"',
            "Packages/A/B/Developer.meta": '      - "**/Developer.meta"',
        }
        for event, block in event_blocks.items():
            for private_path, required_pattern in required_patterns.items():
                self.assertIn(
                    required_pattern,
                    block,
                    f"{event} does not schedule the boundary job for {private_path}",
                )

        self.assertRegex(
            workflow,
            r"git ls-files -- [^\n]*'Developer\.meta'",
            "the remote boundary command does not inspect root Developer.meta",
        )
        phase16 = PHASE16_VALIDATION_PATH.read_text(encoding="utf-8")
        self.assertIn(
            '"Developer.meta"',
            phase16,
            "the default Phase16 boundary check does not inspect root Developer.meta",
        )

    def test_workflow_checkouts_never_persist_repository_credentials(self) -> None:
        """Read-only CI jobs must remove checkout credentials from every workspace."""

        for path in WORKFLOW_PATHS:
            workflow = path.read_text(encoding="utf-8")
            checkout_count = workflow.count("uses: actions/checkout@v4")
            hardened_count = workflow.count("persist-credentials: false")
            self.assertGreater(checkout_count, 0, path.name)
            self.assertEqual(
                checkout_count,
                hardened_count,
                f"every checkout in {path.name} must disable credential persistence",
            )

    def test_ci_dotnet_restore_fails_when_the_only_feed_is_unavailable(self) -> None:
        """CI restore must not suppress failure of the repository's sole NuGet feed."""

        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertNotIn("--ignore-failed-sources", workflow)

    def test_heavy_pull_request_workflows_cancel_superseded_runs(self) -> None:
        """Superseded dotnet and package runs should release hosted CI capacity."""

        for path in (DOTNET_WORKFLOW_PATH, PACKAGE_WORKFLOW_PATH):
            workflow = path.read_text(encoding="utf-8")
            self.assertRegex(workflow, r"(?m)^concurrency:\s*$")
            self.assertIn(
                "group: ${{ github.workflow }}-${{ github.ref }}",
                workflow,
            )
            self.assertIn("cancel-in-progress: true", workflow)

    def test_run_ci_includes_schema_generated_output_freshness(self) -> None:
        """Local CI should reject stale committed schema generator outputs."""
        self.assertEqual(
            "Scripts/schema/validate_schema_generated_outputs.py",
            self.run_ci.SCHEMA_GENERATED_OUTPUT_VALIDATOR,
        )

    def test_dotnet_workflow_runs_schema_generated_output_freshness(self) -> None:
        """Remote CI must compare outputs against its pinned Foxglove checkout."""
        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        checkout = active_workflow_line_index(
            workflow,
            "repository: foxglove/foxglove-sdk",
        )
        pinned_ref = active_workflow_line_index(
            workflow,
            "ref: b298c3d1649e6e5dfd77a53b12ab7c27f97c7aba",
            checkout + 1,
        )
        checkout_path = active_workflow_line_index(
            workflow,
            "path: third-party/foxglove-sdk",
            pinned_ref + 1,
        )
        validation = active_workflow_line_index(
            workflow,
            "run: python3 Scripts/schema/validate_schema_generated_outputs.py",
            checkout_path + 1,
        )

        self.assertLess(checkout, pinned_ref)
        self.assertLess(pinned_ref, checkout_path)
        self.assertLess(checkout_path, validation)

    def test_workflow_line_lookup_rejects_commented_steps(self) -> None:
        """A commented workflow step must not satisfy an active CI contract."""
        workflow = (
            "# repository: foxglove/foxglove-sdk\n"
            "  # ref: b298c3d1649e6e5dfd77a53b12ab7c27f97c7aba\n"
        )

        with self.assertRaisesRegex(ValueError, "active workflow line is missing"):
            active_workflow_line_index(
                workflow,
                "repository: foxglove/foxglove-sdk",
            )

    def test_windows_workflow_executes_editor_restart_relay_process_tests(self) -> None:
        """The Windows-only restart behavior must not silently pass in an Ubuntu lane."""
        workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        windows_job = workflow.index("runs-on: windows-latest")
        adapter_property = workflow.index(
            "-p:IncludeRos2ForUnityAdapter=true",
            windows_job,
        )
        relay_filter = workflow.index(
            "FullyQualifiedName~Ros2ForUnityEditorRestartRelayTests",
            adapter_property,
        )

        self.assertLess(windows_job, adapter_property)
        self.assertLess(adapter_property, relay_filter)

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

    def test_packages_lane_executes_every_r2fu_package_validator(self) -> None:
        """Local package CI must cover runtime and adapter artifacts for every distro."""
        calls: list[list[str]] = []

        def fake_run_parallel(commands: list[tuple[str, list[str]]]) -> dict[str, bool]:
            """Capture package subprocess commands without executing them."""
            calls.extend(command for _label, command in commands)
            return {label: True for label, _command in commands}

        with mock.patch.object(self.run_ci, "run_parallel", side_effect=fake_run_parallel):
            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", "packages"]):
                self.assertEqual(0, self.run_ci.main())

        expected = {
            f"Scripts/ros2forunity/windows/{distro}/{validator}.py"
            for distro in ("humble", "jazzy", "lyrical")
            for validator in (
                "validate_r2fu_runtime_package",
                "validate_ros2forunity_package",
            )
        }
        actual = {
            command[1]
            for command in calls
            if len(command) == 2 and command[1] in expected
        }
        self.assertEqual(expected, actual)

    def test_package_workflow_executes_every_r2fu_package_validator(self) -> None:
        """Remote package CI must cover the same six distro and artifact validators."""
        workflow = PACKAGE_WORKFLOW_PATH.read_text(encoding="utf-8")

        for distro in ("humble", "jazzy", "lyrical"):
            for validator in (
                "validate_r2fu_runtime_package",
                "validate_ros2forunity_package",
            ):
                command = (
                    "python3 Scripts/ros2forunity/windows/"
                    f"{distro}/{validator}.py"
                )
                self.assertEqual(1, workflow.count(command), command)

    def test_packages_lane_executes_ros2_bridge_sample_regressions_and_drift_gate(self) -> None:
        """The package lane must execute both the sync helper tests and byte drift check."""
        calls: list[list[str]] = []

        def fake_run_parallel(commands: list[tuple[str, list[str]]]) -> dict[str, bool]:
            """Capture package subprocess commands without executing them."""
            calls.extend(command for _label, command in commands)
            return {label: True for label, _command in commands}

        with mock.patch.object(
            self.run_ci,
            "run_parallel",
            side_effect=fake_run_parallel,
        ):
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "packages"],
            ):
                self.assertEqual(0, self.run_ci.main())

        self.assertIn(
            [
                sys.executable,
                "-m",
                "unittest",
                "Scripts.samples.regression_checks.test_sample_sync_tooling",
            ],
            calls,
        )
        self.assertIn(
            [
                sys.executable,
                "-m",
                "unittest",
                "Scripts.remotegateway.regression_checks.test_remote_gateway_tooling",
            ],
            calls,
        )
        self.assertIn(
            [
                sys.executable,
                "Scripts/samples/sync_ros2_bridge_sample.py",
                "--dry-run",
            ],
            calls,
        )

    def test_packages_lane_executes_all_maintained_python_regression_modules(self) -> None:
        """Default package CI must execute maintained regression modules, not only validators."""
        expected = (
            "Scripts.native.regression_checks.test_native_sources",
            "Scripts.package.regression_checks.test_validate_local_entrypoints",
            "Scripts.package.regression_checks.test_validate_phase186_package_matrix",
            "Scripts.package.regression_checks.test_validate_unity_package",
            "Scripts.schema.regression_checks.test_schema_tooling",
            "Scripts.smoke.test_core_smoke_scripts",
            "Scripts.smoke.ros2.regression_checks.test_phase162_lyrical_zenoh_player_smoke",
            "Scripts.smoke.ros2.regression_checks.test_ros2_windows_env",
        )
        calls: list[list[str]] = []

        def fake_run_parallel(commands: list[tuple[str, list[str]]]) -> dict[str, bool]:
            """Capture package subprocess commands without executing them."""
            calls.extend(command for _label, command in commands)
            return {label: True for label, _command in commands}

        with mock.patch.object(
            self.run_ci,
            "run_parallel",
            side_effect=fake_run_parallel,
        ):
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "packages"],
            ):
                self.assertEqual(0, self.run_ci.main())

        self.assertIn(
            [sys.executable, "-m", "unittest", *expected],
            calls,
        )

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
                "phase186-bridge-tooling",
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
            {
                "mcap-conformance",
                "phase184-acceptance-tooling",
                "phase186-bridge-tooling",
            },
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

    def test_phase186_bridge_tooling_selector_is_default_but_live_is_not(self) -> None:
        """Ordinary CI runs static tooling; provisioned live work stays explicit."""

        jobs = self.run_ci.build_default_ci_jobs(
            types.SimpleNamespace(skip_analyzer=False)
        )
        names = [job.name for job in jobs]
        self.assertIn("phase186-bridge-tooling", names)
        self.assertNotIn("phase186-bridge-windows-live", names)
        tooling = next(job for job in jobs if job.name == "phase186-bridge-tooling")
        self.assertTrue(tooling.disable_timeout)

    def test_phase186_bridge_tooling_selector_runs_exact_static_suites_and_matrix(self) -> None:
        """The tooling selector must never launch Unity, a sidecar, or a ROS peer."""

        with mock.patch.object(self.run_ci, "run", return_value=True) as run:
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "phase186-bridge-tooling"],
            ):
                self.assertEqual(0, self.run_ci.main())

        commands = [call.args[0] for call in run.call_args_list]
        self.assertEqual(
            [
                [sys.executable, "-m", "unittest", module]
                for module in self.PHASE186_BRIDGE_TOOLING_SUITES
            ]
            + [
                [sys.executable, "Scripts/package/validate_phase186_package_matrix.py"],
                [sys.executable, "-m", "Scripts.smoke.foxrun.phase186_provenance"],
            ],
            commands,
        )
        flattened = " ".join(part for command in commands for part in command)
        self.assertNotIn(
            [
                sys.executable,
                "-m",
                "Scripts.smoke.foxrun.phase186_bridge_certification",
            ],
            commands,
        )
        self.assertNotIn("phase186_bridge_acceptance --case", flattened)

    def test_phase186_bridge_windows_live_selector_runs_exact_certification(self) -> None:
        """The live selector must bind certification identity to the current SHA."""

        head = "a" * 40
        with mock.patch.object(self.run_ci, "current_git_head", return_value=head):
            with mock.patch.object(self.run_ci, "run", return_value=True) as run:
                with mock.patch.object(
                    sys,
                    "argv",
                    ["run_ci.py", "--only", "phase186-bridge-windows-live"],
                ):
                    self.assertEqual(0, self.run_ci.main())

        self.assertEqual(1, run.call_count)
        command = run.call_args.args[0]
        self.assertEqual(
            [
                sys.executable,
                "-m",
                "Scripts.smoke.foxrun.phase186_bridge_certification",
                "--expected-head",
                head,
                "--output-root",
                "build/phase186/windows-live",
                "--run-id",
                self.run_ci.phase186_certification_run_id(head),
            ],
            command,
        )
        self.assertTrue(run.call_args.kwargs["disable_timeout"])

    def test_phase186_bridge_windows_live_not_run_is_not_promoted_to_pass(self) -> None:
        """Any nonzero certification result, including NOT RUN, fails the selector."""

        with mock.patch.object(self.run_ci, "current_git_head", return_value="b" * 40):
            with mock.patch.object(self.run_ci, "run", return_value=False):
                with mock.patch.object(
                    sys,
                    "argv",
                    ["run_ci.py", "--only", "phase186-bridge-windows-live"],
                ):
                    self.assertEqual(1, self.run_ci.main())

    def test_phase186_selectors_are_named_in_help(self) -> None:
        """Both honest Phase186 lanes must be discoverable without reading the plan."""

        with mock.patch.object(sys, "argv", ["run_ci.py", "--help"]):
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                with self.assertRaises(SystemExit) as context:
                    self.run_ci.main()

        self.assertEqual(0, context.exception.code)
        rendered = stdout.getvalue()
        self.assertIn("phase186-bridge-tooling", rendered)
        self.assertIn("phase186-bridge-windows-live", rendered)

    def test_phase186_workflows_keep_tooling_and_provisioned_live_separate(self) -> None:
        """Hosted CI runs tooling while live certification requires a labeled owner."""

        dotnet_workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertIn(
            "run_ci.py --only phase186-bridge-tooling",
            dotnet_workflow,
        )
        live_workflow = PHASE186_WINDOWS_LIVE_WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertIn("workflow_dispatch:", live_workflow)
        self.assertIn("unity2foxglove-phase186-live", live_workflow)
        self.assertIn("run_ci.py --only phase186-bridge-windows-live", live_workflow)
        self.assertIn("build/phase186/windows-live", live_workflow)
        self.assertNotRegex(live_workflow, r"(?m)^\s+push:")
        self.assertNotRegex(live_workflow, r"(?m)^\s+pull_request:")

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

    def test_unknown_only_selector_is_a_usage_error(self) -> None:
        """A misspelled lane must never become a zero-work success."""
        with mock.patch.object(
            sys,
            "argv",
            ["run_ci.py", "--only", "pacakges"],
        ), mock.patch("sys.stderr", new_callable=io.StringIO) as stderr:
            with self.assertRaises(SystemExit) as context:
                self.run_ci.main()

        self.assertEqual(2, context.exception.code)
        self.assertIn("invalid choice", stderr.getvalue())

    def test_direct_analyzer_skip_is_a_usage_error(self) -> None:
        """The analyzer-only selector cannot claim success when its only lane is skipped."""
        with mock.patch.object(
            sys,
            "argv",
            ["run_ci.py", "--only", "analyzer", "--skip-analyzer"],
        ), mock.patch("sys.stderr", new_callable=io.StringIO) as stderr:
            with self.assertRaises(SystemExit) as context:
                self.run_ci.main()

        self.assertEqual(2, context.exception.code)
        self.assertIn("--skip-analyzer cannot be combined with --only analyzer", stderr.getvalue())

    def test_empty_ci_result_summary_is_non_pass(self) -> None:
        """An aggregate with no executed lanes must fail instead of vacuously passing."""
        with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
            result = self.run_ci.report_ci_job_results({})

        self.assertEqual(1, result)
        self.assertIn("No CI checks were executed.", stdout.getvalue())

    def test_default_analyzer_skip_exposes_machine_readable_lane(self) -> None:
        """A permitted partial default run must identify its skipped analyzer lane explicitly."""
        with mock.patch.object(self.run_ci, "run_ci_jobs", return_value={"other": True}):
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--skip-analyzer"],
            ), mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                result = self.run_ci.main()

        rendered = stdout.getvalue()
        self.assertEqual(0, result)
        self.assertIn("[SKIP] analyzer", rendered)
        self.assertIn("SKIPPED_LANES=analyzer", rendered)
        self.assertIn("All executed CI checks passed.", rendered)
        self.assertNotIn("All CI checks passed.", rendered)

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
                "phase186-bridge-tooling": None,
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
                "phase186-bridge-tooling",
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

    def test_runtime_adoption_json_replace_failure_preserves_previous_file(self) -> None:
        """A failed final replace must not truncate verified adoption evidence."""
        from Scripts.ros2forunity.windows import runtime_adoption_manifest

        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "adoption.json"
            path.write_text('{"verified":true}\n', encoding="utf-8")
            with mock.patch.object(
                runtime_adoption_manifest.os,
                "replace",
                side_effect=OSError("replace failed"),
            ):
                with self.assertRaisesRegex(OSError, "replace failed"):
                    runtime_adoption_manifest.write_json(
                        path,
                        {"verified": False},
                    )

            self.assertEqual(
                '{"verified":true}\n',
                path.read_text(encoding="utf-8"),
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

    def test_negative_build_timeout_is_rejected(self) -> None:
        """Only zero, not an arbitrary negative value, may disable the build timeout."""
        with mock.patch.object(sys, "argv", ["unity_il2cpp.py", "--timeout-minutes", "-1"]):
            with self.assertRaises(SystemExit) as raised:
                self.unity_il2cpp.parse_args()

        self.assertEqual(self.unity_il2cpp.EXIT_USAGE_ERROR, raised.exception.code)

    def test_posix_process_group_enumeration_fails_closed_without_ps(self) -> None:
        """A missing ps binary must not disguise an extant owned process group as empty."""
        with mock.patch.object(self.unity_il2cpp.subprocess, "run", side_effect=FileNotFoundError("ps")):
            with mock.patch.object(self.unity_il2cpp.os, "killpg", return_value=None, create=True):
                pids = self.unity_il2cpp._posix_process_group_pids(4321)

        self.assertEqual([4321], pids)

    def test_posix_termination_poll_avoids_repeated_ps_processes(self) -> None:
        """Quiescence polling should use the process-group primitive, not shell out on every pass."""
        process = mock.Mock()
        process.wait.return_value = 0

        kill_signal = 9

        def inspect_or_kill_group(_process_group_id, signal_value):
            """Model an extant group until the final kill signal is sent."""
            if signal_value == kill_signal:
                return None
            raise ProcessLookupError

        tree = self.unity_il2cpp.OwnedProcessTree(process, posix_process_group_id=4321)
        with mock.patch.object(self.unity_il2cpp.signal, "SIGKILL", kill_signal, create=True):
            with mock.patch.object(
                self.unity_il2cpp.os,
                "killpg",
                side_effect=inspect_or_kill_group,
                create=True,
            ):
                with mock.patch.object(self.unity_il2cpp, "_posix_process_group_pids", return_value=[]) as enumerate_pids:
                    residual = tree.terminate()

        self.assertEqual([], residual)
        enumerate_pids.assert_not_called()

    def test_windows_job_uses_a_dedicated_child_termination_code(self) -> None:
        """A killed child must not inherit the build CLI's own timeout exit code."""
        kernel32 = mock.Mock()
        kernel32.TerminateJobObject.return_value = True
        job = object.__new__(self.unity_il2cpp._WindowsKillOnCloseJob)
        job._handle = 123
        job._kernel32 = kernel32

        job.terminate()

        self.assertNotEqual(
            self.unity_il2cpp.EXIT_TIMEOUT,
            self.unity_il2cpp.WINDOWS_JOB_TERMINATE_EXIT_CODE,
        )
        kernel32.TerminateJobObject.assert_called_once_with(
            123,
            self.unity_il2cpp.WINDOWS_JOB_TERMINATE_EXIT_CODE,
        )

    def test_timeout_terminates_owned_descendant_tree(self) -> None:
        """A timed-out Unity stand-in must not leave its compiler child alive."""
        parent_pid = None
        child_pid = None
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            parent_pid_path = root / "parent.pid"
            child_pid_path = root / "child.pid"
            parent_code = (
                "import os, pathlib, subprocess, sys, time; "
                f"pathlib.Path({str(parent_pid_path)!r}).write_text(str(os.getpid()), encoding='utf-8'); "
                "child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)']); "
                f"pathlib.Path({str(child_pid_path)!r}).write_text(str(child.pid), encoding='utf-8'); "
                "time.sleep(60)"
            )

            try:
                with mock.patch.object(self.unity_il2cpp, "SECONDS_PER_MINUTE", 5):
                    with mock.patch.object(self.unity_il2cpp, "LOG_POLL_SLEEP_SECONDS", 0.01):
                        with mock.patch.object(self.unity_il2cpp, "UNITY_TERMINATION_WAIT_SECONDS", 2):
                            result = self.unity_il2cpp.run_with_progress(
                                [sys.executable, "-c", parent_code],
                                root,
                                root / "unity.log",
                                interval=1,
                                timeout_minutes=1,
                            )

                self.assertEqual(self.unity_il2cpp.EXIT_TIMEOUT, result)
                self.assertTrue(parent_pid_path.is_file(), "fake Unity did not publish its PID")
                self.assertTrue(child_pid_path.is_file(), "fake compiler did not publish its PID")
                parent_pid = int(parent_pid_path.read_text(encoding="utf-8"))
                child_pid = int(child_pid_path.read_text(encoding="utf-8"))
                self.assertTrue(self._wait_for_pid_exit(parent_pid))
                self.assertTrue(
                    self._wait_for_pid_exit(child_pid),
                    f"timed-out compiler child remained alive: pid={child_pid}",
                )
            finally:
                if child_pid is None:
                    child_pid = self._read_pid_if_present(child_pid_path)
                if parent_pid is None:
                    parent_pid = self._read_pid_if_present(parent_pid_path)
                self._kill_if_running(child_pid)
                self._kill_if_running(parent_pid)

    def test_owned_process_tree_preserves_normal_exit(self) -> None:
        """Owned launch plumbing must preserve an ordinary successful exit."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            with mock.patch.object(self.unity_il2cpp, "LOG_POLL_SLEEP_SECONDS", 0.01):
                result = self.unity_il2cpp.run_with_progress(
                    [sys.executable, "-c", "raise SystemExit(0)"],
                    root,
                    root / "unity.log",
                    interval=1,
                    timeout_minutes=0,
                )

        self.assertEqual(self.unity_il2cpp.EXIT_SUCCESS, result)

    @staticmethod
    def _read_pid_if_present(path: Path) -> int | None:
        """Read a test-owned PID file when startup reached that boundary."""
        if not path.is_file():
            return None
        return int(path.read_text(encoding="utf-8"))

    @staticmethod
    def _wait_for_pid_exit(pid: int, timeout_seconds: float = 2.0) -> bool:
        """Return whether a concrete PID disappears within a bounded wait."""
        deadline = time.monotonic() + timeout_seconds
        while time.monotonic() < deadline:
            if not UnityIl2CppBuildTests._pid_is_running(pid):
                return True
            time.sleep(0.02)
        return not UnityIl2CppBuildTests._pid_is_running(pid)

    @staticmethod
    def _pid_is_running(pid: int) -> bool:
        """Check one PID with only standard-library platform primitives."""
        if os.name != "nt":
            try:
                os.kill(pid, 0)
                return True
            except ProcessLookupError:
                return False
            except PermissionError:
                return True

        from ctypes import wintypes

        process_query_limited_information = 0x1000
        still_active = 259
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel32.OpenProcess.restype = wintypes.HANDLE
        kernel32.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
        kernel32.GetExitCodeProcess.restype = wintypes.BOOL
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.CloseHandle.restype = wintypes.BOOL
        handle = kernel32.OpenProcess(process_query_limited_information, False, pid)
        if not handle:
            return False
        try:
            exit_code = wintypes.DWORD()
            return bool(kernel32.GetExitCodeProcess(handle, ctypes.byref(exit_code))) and exit_code.value == still_active
        finally:
            kernel32.CloseHandle(handle)

    @staticmethod
    def _kill_if_running(pid: int | None) -> None:
        """Best-effort cleanup for a failed process-tree regression."""
        if pid is None:
            return
        try:
            os.kill(pid, signal.SIGTERM if os.name == "nt" else signal.SIGKILL)
        except (OSError, ProcessLookupError):
            pass
        UnityIl2CppBuildTests._wait_for_pid_exit(pid)


if __name__ == "__main__":
    unittest.main()
