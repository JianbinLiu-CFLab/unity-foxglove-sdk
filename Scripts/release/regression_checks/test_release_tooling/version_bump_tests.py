from .foundation import *

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

__all__ = ["VersionBumpTests"]
