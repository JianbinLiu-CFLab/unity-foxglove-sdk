#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Synchronize Unity2Foxglove package version references.
# Usage: python Scripts/release/bump_version.py 1.3.0 --date 2026-05-12
# Inputs: Target semantic version, optional --date, optional --dry-run.
# Outputs: Updates package metadata, changelog, README, and release-note stubs unless --dry-run.

"""Synchronize Unity2Foxglove package version references.

This script updates the package version, the runtime package-metadata
validation assertion, README badges/notes, and release document stubs.
It intentionally does not create git commits, tags, or GitHub releases.
"""

from __future__ import annotations

import argparse
import json
import re
from dataclasses import dataclass
from datetime import date
from pathlib import Path


# Semantic version grammar accepted by the release helper.
VERSION_RE = re.compile(r"^\d+\.\d+\.\d+$")

# Process exit code for a successful synchronization or dry run.
EXIT_SUCCESS = 0

# Number of parent directories between this file and the repository root.
REPO_ROOT_PARENT_DEPTH = 2

# Text replacements that update a single canonical occurrence.
SINGLE_REPLACEMENT = 1

# Regex capture groups for the package.json version replacement pattern.
VERSION_PROPERTY_PREFIX_GROUP = 1
VERSION_PROPERTY_SUFFIX_GROUP = 3


def resolve_repo_root() -> Path:
    """Return the repository root and fail loudly if the script is moved."""
    root = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
    package_json = root / "Packages/dev.unity2foxglove.sdk/package.json"
    changelog = root / "CHANGELOG.md"
    if not package_json.exists() or not changelog.exists():
        raise SystemExit(f"Unexpected repository root for bump_version.py: {root}")
    return root


@dataclass
class PlannedChange:
    """Records one file that would be changed, or was changed, by the bump."""

    path: Path
    action: str


class VersionBump:
    """Coordinates all package-version edits for one target release."""

    def __init__(self, root: Path, version: str, release_date: str, dry_run: bool) -> None:
        """Store the release context and initialize the change log."""
        self.root = root
        self.version = version
        self.release_date = release_date
        self.dry_run = dry_run
        self.changes: list[PlannedChange] = []

    def rel(self, path: Path) -> str:
        """Format a path relative to the repository root for console output."""
        return path.relative_to(self.root).as_posix()

    def read(self, path: Path) -> str:
        """Read a UTF-8 text file."""
        return path.read_text(encoding="utf-8")

    def write_if_changed(self, path: Path, content: str, action: str) -> None:
        """Record and optionally write a file when the generated content differs."""
        original = self.read(path) if path.exists() else None
        if original == content:
            return

        self.changes.append(PlannedChange(path, action))
        if not self.dry_run:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8", newline="\n")

    def sub_exactly_once(self, path: Path, text: str, pattern: str, replacement: str, label: str) -> str:
        """Apply one regex replacement and fail loudly when the target is ambiguous."""
        hits = list(re.finditer(pattern, text))
        if len(hits) != SINGLE_REPLACEMENT:
            raise ValueError(f"Expected one {label} in {self.rel(path)}, found {len(hits)}.")
        return re.sub(pattern, replacement, text, count=SINGLE_REPLACEMENT)

    def package_json_path(self) -> Path:
        """Return the canonical package.json path."""
        return self.root / "Packages/dev.unity2foxglove.sdk/package.json"

    def package_version(self, text: str | None = None, path: Path | None = None) -> str:
        """Read the current semantic version from package.json."""
        path = path or self.package_json_path()
        text = self.read(path) if text is None else text
        data = json.loads(text)
        version = data.get("version")
        if not isinstance(version, str) or not VERSION_RE.match(version):
            raise ValueError(f"Cannot read semantic version from {self.rel(path)}")
        return version

    def replace_version_property(self, old_version: str, text: str | None = None, path: Path | None = None) -> None:
        """Replace the canonical package.json version property."""
        path = path or self.package_json_path()
        text = self.read(path) if text is None else text
        pattern = re.compile(r'("version"\s*:\s*")(\d+\.\d+\.\d+)(")')
        updated, count = pattern.subn(
            lambda m: f"{m.group(VERSION_PROPERTY_PREFIX_GROUP)}{self.version}{m.group(VERSION_PROPERTY_SUFFIX_GROUP)}",
            text,
            count=SINGLE_REPLACEMENT,
        )
        if count != SINGLE_REPLACEMENT:
            raise ValueError(f"Expected one version property in {self.rel(path)}")
        self.write_if_changed(path, updated, f"set package version {old_version} -> {self.version}")

    def update_adapter_dependency(self) -> None:
        """Update the optional ROS2 adapter dependency on the core SDK package."""
        path = self.root / "Packages/dev.unity2foxglove.ros2forunity/package.json"
        text = self.read(path)
        text = self.sub_exactly_once(
            path,
            text,
            r'("dev\.unity2foxglove\.sdk"\s*:\s*")(\d+\.\d+\.\d+)(")',
            rf"\g<1>{self.version}\g<3>",
            "ROS2 adapter SDK dependency version",
        )
        self.write_if_changed(path, text, f"update ROS2 adapter SDK dependency to {self.version}")

    def update_phase16_assertions(self) -> None:
        """Update release metadata assertions used by the runtime package validator."""
        path = self.root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase16Validation.cs"
        text = self.read(path)
        text = self.sub_exactly_once(
            path,
            text,
            r'(\\\"dev\.unity2foxglove\.sdk\\\": \\\")(\d+\.\d+\.\d+)(\\\")',
            rf"\g<1>{self.version}\g<3>",
            "Phase16 ROS2 adapter dependency assertion",
        )
        text = self.sub_exactly_once(
            path,
            text,
            r'(version: \\\")(\d+\.\d+\.\d+)(\\\")',
            rf"\g<1>{self.version}\g<3>",
            "Phase16 CITATION version assertion",
        )
        text = self.sub_exactly_once(
            path,
            text,
            r'(date-released: \\\")(\d{4}-\d{2}-\d{2})(\\\")',
            rf"\g<1>{self.release_date}\g<3>",
            "Phase16 CITATION release date assertion",
        )
        self.write_if_changed(path, text, f"update Phase16 release assertions to {self.version}")

    def update_core_sdk_dependency_assertions(self) -> None:
        """Update validation anchors that assert the adapter's SDK dependency."""
        bracket_assertion_paths = [
            self.root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase107Validation.cs",
            self.root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase108Validation.cs",
        ]
        for path in bracket_assertion_paths:
            text = self.read(path)
            text = self.sub_exactly_once(
                path,
                text,
                r'(\["dev\.unity2foxglove\.sdk"\]\s*==\s*")(\d+\.\d+\.\d+)(")',
                rf"\g<1>{self.version}\g<3>",
                "core SDK dependency assertion",
            )
            self.write_if_changed(path, text, f"update core SDK dependency assertion to {self.version}")

        escaped_literal_path = self.root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase163_29Validation.cs"
        text = self.read(escaped_literal_path)
        text = self.sub_exactly_once(
            escaped_literal_path,
            text,
            r'(\\\"dev\.unity2foxglove\.sdk\\\": \\\")(\d+\.\d+\.\d+)(\\\")',
            rf"\g<1>{self.version}\g<3>",
            "escaped core SDK dependency literal assertion",
        )
        self.write_if_changed(
            escaped_literal_path,
            text,
            f"update escaped core SDK dependency literal to {self.version}",
        )

        literal_assertion_paths = [
            self.root / "Scripts/ros2forunity/windows/humble/validate_ros2forunity_package.py",
            self.root / "Scripts/ros2forunity/windows/jazzy/validate_ros2forunity_package.py",
            self.root / "Scripts/ros2forunity/windows/lyrical/validate_ros2forunity_package.py",
        ]
        for path in literal_assertion_paths:
            text = self.read(path)
            text = self.sub_exactly_once(
                path,
                text,
                r'("dev\.unity2foxglove\.sdk": ")(\d+\.\d+\.\d+)(")',
                rf"\g<1>{self.version}\g<3>",
                "core SDK dependency literal assertion",
            )
            self.write_if_changed(path, text, f"update core SDK dependency literal to {self.version}")

    # README exposes only the current release; historical notes remain in docs/releases.
    KEEP_RELEASE_NOTES = 1

    def update_readme(self, old_version: str) -> None:
        """Update root README badges and release-note links for the target version."""
        path = self.root / "README.md"
        text = self.read(path)
        old = re.escape(old_version)
        text = self.sub_exactly_once(
            path,
            text,
            rf"(?m)^(\[!\[Release\]\(https://img\.shields\.io/badge/release-)v{old}(-green\)\]\([^)]+\))$",
            rf"\g<1>v{self.version}\g<2>",
            "README release badge",
        )
        text = self.sub_exactly_once(
            path,
            text,
            rf"(?m)^(.*Windows is verified for )v{old}(;.*)$",
            rf"\g<1>v{self.version}\g<2>",
            "README verified Windows version note",
        )

        release_note_link = (
            r"\[v(?P<ver>\d+\.\d+\.\d+) release notes\]"
            r"\(docs/releases/RELEASE_NOTES_v(?P=ver)\.md\)"
        )
        release_note_re = re.compile(release_note_link)
        current_release_note = (
            f"[v{self.version} release notes]"
            f"(docs/releases/RELEASE_NOTES_v{self.version}.md)"
        )
        text = self.sub_exactly_once(
            path,
            text,
            rf"\[v{old} release notes\]"
            rf"\(docs/releases/RELEASE_NOTES_v{old}\.md\)",
            current_release_note,
            "README current release-note link",
        )

        # Keep the first link in place (inline navigation or a legacy bullet) and
        # remove only historical standalone bullets. Ambiguous inline duplicates
        # fail closed instead of silently corrupting prose.
        hits = list(release_note_re.finditer(text))
        if len(hits) > self.KEEP_RELEASE_NOTES:
            for hit in reversed(hits[self.KEEP_RELEASE_NOTES :]):
                line_start = text.rfind("\n", 0, hit.start()) + 1
                line_end = text.find("\n", hit.end())
                if line_end < 0:
                    line_end = len(text)
                line = text[line_start:line_end].rstrip("\r")
                if line != "- " + hit.group(0):
                    raise ValueError(
                        "Historical README release-note links must be standalone bullets "
                        f"in {self.rel(path)}."
                    )
                remove_end = line_end + (1 if line_end < len(text) else 0)
                text = text[:line_start] + text[remove_end:]

        text = re.sub(
            r"(?m)^- \[Release notes archive\]\(docs/releases/\)\r?\n?",
            "",
            text,
        )

        self.write_if_changed(path, text, f"update README version references to {self.version}")

    def update_package_readme(self, old_version: str) -> None:
        """Update the package README verified-version note."""
        path = self.root / "Packages/dev.unity2foxglove.sdk/README.md"
        text = self.read(path)
        text = self.sub_exactly_once(
            path,
            text,
            rf"(?m)^(- Editor \+ Standalone Player\. Windows is verified for )v{re.escape(old_version)}(;.*)$",
            rf"\g<1>v{self.version}\g<2>",
            "package README verified Windows version note",
        )
        self.write_if_changed(path, text, f"update package README verified version to {self.version}")

    def update_citation(self) -> None:
        """Update software citation release metadata."""
        path = self.root / "CITATION.cff"
        text = self.read(path)
        text = self.sub_exactly_once(
            path,
            text,
            r'(?m)^version:\s*"[0-9]+\.[0-9]+\.[0-9]+"\s*$',
            f'version: "{self.version}"',
            "CITATION.cff version",
        )
        text = self.sub_exactly_once(
            path,
            text,
            r'(?m)^date-released:\s*"[0-9]{4}-[0-9]{2}-[0-9]{2}"\s*$',
            f'date-released: "{self.release_date}"',
            "CITATION.cff release date",
        )
        self.write_if_changed(path, text, f"update CITATION.cff metadata to {self.version}")

    def update_changelog(self) -> None:
        """Promote Unreleased notes or insert a stub for the target version."""
        path = self.root / "CHANGELOG.md"
        text = self.read(path)
        heading = f"## {self.version} - "
        if heading in text:
            return

        stub_entry = (
            f"## {self.version} - {self.release_date}\n\n"
            "### Added\n\n"
            "- Version prepared for the next Unity2Foxglove package release.\n\n"
            "### Changed\n\n"
            "- Release notes and package metadata are synchronized for this version.\n\n"
            "### Verified\n\n"
            "- Runtime validation suite should be run before tagging this release.\n"
            "- Release package validation should be run before tagging this release.\n\n"
        )

        unreleased = re.search(
            r"(?ms)^## Unreleased[ \t]*\r?\n(?P<body>.*?)(?=^## \d+\.\d+\.\d+ - )",
            text,
        )
        if unreleased is not None:
            body = unreleased.group("body").strip()
            entry = (
                f"## {self.version} - {self.release_date}\n\n{body}\n\n"
                if body
                else stub_entry
            )
            replacement = "## Unreleased\n\n" + entry
            text = text[: unreleased.start()] + replacement + text[unreleased.end() :]
            self.write_if_changed(path, text, f"promote Unreleased notes to {self.version}")
            return

        insertion = re.search(r"(?m)^---\n\n(?=## \d+\.\d+\.\d+ - )", text)
        if insertion is None:
            raise ValueError(f"Cannot find changelog insertion point in {self.rel(path)}")
        text = text[: insertion.end()] + stub_entry + text[insertion.end() :]
        self.write_if_changed(path, text, f"insert changelog section for {self.version}")

    def create_release_notes(self) -> None:
        """Create a release-note stub for the target version when missing."""
        path = self.root / "docs/releases" / f"RELEASE_NOTES_v{self.version}.md"
        if path.exists():
            return

        content = (
            f"# Unity2Foxglove v{self.version} Release Notes\n\n"
            f"Release date: {self.release_date}\n\n"
            f"Unity2Foxglove v{self.version} prepares the next package release. Replace this summary "
            "with the final user-facing release description before publishing.\n\n"
            "## Highlights\n\n"
            "- Version metadata and release documents have been prepared.\n\n"
            "## Compatibility Notes\n\n"
            "- Existing Unity scenes keep serialized Inspector values unless changed manually.\n\n"
            "## Verification\n\n"
            "Run before publishing the release:\n\n"
            "```bash\n"
            "dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj\n"
            "python Scripts/package/validate_unity_package.py\n"
            "python Scripts/performance/run_baseline.py --quick --output build/performance/release\n"
            "```\n"
        )
        self.write_if_changed(path, content, f"create release notes for {self.version}")

    def run(self) -> int:
        """Apply or report every version-bump edit."""
        package_json = self.package_json_path()
        package_json_text = self.read(package_json)
        old_version = self.package_version(package_json_text, package_json)
        self.replace_version_property(old_version, package_json_text, package_json)
        self.update_adapter_dependency()
        self.update_phase16_assertions()
        self.update_core_sdk_dependency_assertions()
        self.update_readme(old_version)
        self.update_package_readme(old_version)
        self.update_citation()
        self.update_changelog()
        self.create_release_notes()

        prefix = "[DRY-RUN]" if self.dry_run else "[bump_version]"
        if not self.changes:
            print(f"{prefix} version references are already synchronized for {self.version}.")
            return EXIT_SUCCESS

        print(f"{prefix} planned changes:" if self.dry_run else f"{prefix} updated files:")
        for change in self.changes:
            print(f"  - {self.rel(change.path)}: {change.action}")
        return EXIT_SUCCESS


def parse_args() -> argparse.Namespace:
    """Parse CLI arguments for the version-bump workflow."""
    parser = argparse.ArgumentParser(description="Synchronize Unity2Foxglove package version references.")
    parser.add_argument("version", help="Target semantic version, for example 1.2.0.")
    parser.add_argument("--date", default=date.today().isoformat(), help="Release date for new changelog/release notes.")
    parser.add_argument("--dry-run", action="store_true", help="Print planned changes without writing files.")
    return parser.parse_args()


def main() -> int:
    """Validate CLI input and run the package-version synchronization."""
    args = parse_args()
    if not VERSION_RE.match(args.version):
        raise SystemExit(f"Invalid version '{args.version}'. Expected MAJOR.MINOR.PATCH.")

    root = resolve_repo_root()
    return VersionBump(root, args.version, args.date, args.dry_run).run()


if __name__ == "__main__":
    raise SystemExit(main())
