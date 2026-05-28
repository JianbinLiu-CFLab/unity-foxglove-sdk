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

    def package_version(self) -> str:
        """Read the current semantic version from package.json."""
        package_json = self.root / "Packages/dev.unity2foxglove.sdk/package.json"
        data = json.loads(self.read(package_json))
        version = data.get("version")
        if not isinstance(version, str) or not VERSION_RE.match(version):
            raise ValueError(f"Cannot read semantic version from {self.rel(package_json)}")
        return version

    def replace_version_property(self, old_version: str) -> None:
        """Replace the canonical package.json version property."""
        path = self.root / "Packages/dev.unity2foxglove.sdk/package.json"
        text = self.read(path)
        pattern = re.compile(r'("version"\s*:\s*")(\d+\.\d+\.\d+)(")')
        updated, count = pattern.subn(
            lambda m: f"{m.group(VERSION_PROPERTY_PREFIX_GROUP)}{self.version}{m.group(VERSION_PROPERTY_SUFFIX_GROUP)}",
            text,
            count=SINGLE_REPLACEMENT,
        )
        if count != SINGLE_REPLACEMENT:
            raise ValueError(f"Expected one version property in {self.rel(path)}")
        self.write_if_changed(path, updated, f"set package version {old_version} -> {self.version}")

    def update_phase16_assertion(self) -> None:
        """Keep the Phase16 package-version assertion aligned with package.json."""
        path = self.root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase16Validation.cs"
        text = self.read(path)
        text = re.sub(r'"\\"version\\": \\"\d+\.\d+\.\d+\\""', f'"\\"version\\": \\"{self.version}\\""', text)
        text = re.sub(r"package\.json version is \d+\.\d+\.\d+", f"package.json version is {self.version}", text)
        self.write_if_changed(path, text, f"update Phase16 package version assertion to {self.version}")

    # Maximum number of old release-note links kept in README.
    KEEP_RELEASE_NOTES = 2

    def update_readme(self, old_version: str) -> None:
        """Update root README badges and release-note links for the target version."""
        path = self.root / "README.md"
        text = self.read(path)
        text = text.replace(f"release-v{old_version}", f"release-v{self.version}")
        text = text.replace(f"verified for v{old_version}", f"verified for v{self.version}")

        release_note_line = (
            r"^- \[v(?P<ver>\d+\.\d+\.\d+) release notes\]"
            r"\(docs/releases/RELEASE_NOTES_v(?P=ver)\.md\)$"
        )
        release_note_re = re.compile(release_note_line, re.MULTILINE)

        release_note = f"- [v{self.version} release notes](docs/releases/RELEASE_NOTES_v{self.version}.md)"
        if release_note not in text:
            match = release_note_re.search(text)
            if match:
                text = text[: match.start()] + release_note + "\n" + text[match.start() :]
            else:
                archive = "- [Release notes archive](docs/releases/)"
                idx = text.find(archive)
                if idx >= 0:
                    text = text[:idx] + release_note + "\n" + text[idx:]
                else:
                    text += f"\n{release_note}\n"

        # Trim old entries to keep only the latest KEEP_RELEASE_NOTES.
        hits = list(release_note_re.finditer(text))
        if len(hits) > self.KEEP_RELEASE_NOTES:
            for hit in hits[: -self.KEEP_RELEASE_NOTES]:
                start = hit.start()
                end = hit.end()
                if end < len(text) and text[end] == "\n":
                    end += 1
                text = text[:start] + text[end:]

        self.write_if_changed(path, text, f"update README version references to {self.version}")

    def update_package_readme(self, old_version: str) -> None:
        """Update the package README verified-version note."""
        path = self.root / "Packages/dev.unity2foxglove.sdk/README.md"
        text = self.read(path)
        text = text.replace(f"verified for v{old_version}", f"verified for v{self.version}")
        self.write_if_changed(path, text, f"update package README verified version to {self.version}")

    def update_changelog(self) -> None:
        """Insert a changelog section for the target version when it is absent."""
        path = self.root / "CHANGELOG.md"
        text = self.read(path)
        heading = f"## {self.version} - "
        if heading in text:
            return

        entry = (
            f"## {self.version} - {self.release_date}\n\n"
            "### Added\n\n"
            "- Version prepared for the next Unity2Foxglove package release.\n\n"
            "### Changed\n\n"
            "- Release notes and package metadata are synchronized for this version.\n\n"
            "### Verified\n\n"
            "- Runtime validation suite should be run before tagging this release.\n"
            "- Release package validation should be run before tagging this release.\n\n"
        )

        delimiter = "---\n\n"
        if delimiter not in text:
            raise ValueError(f"Cannot find changelog insertion point in {self.rel(path)}")
        text = text.replace(delimiter, delimiter + entry, SINGLE_REPLACEMENT)
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
            "python Scripts/release/validate_package.py\n"
            "python Scripts/performance/run_baseline.py --quick --output build/performance/release\n"
            "```\n"
        )
        self.write_if_changed(path, content, f"create release notes for {self.version}")

    def run(self) -> int:
        """Apply or report every version-bump edit."""
        old_version = self.package_version()
        self.replace_version_property(old_version)
        self.update_phase16_assertion()
        self.update_readme(old_version)
        self.update_package_readme(old_version)
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

    root = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
    return VersionBump(root, args.version, args.date, args.dry_run).run()


if __name__ == "__main__":
    raise SystemExit(main())
