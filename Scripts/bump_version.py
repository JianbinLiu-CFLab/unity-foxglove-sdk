#!/usr/bin/env python3
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


VERSION_RE = re.compile(r"^\d+\.\d+\.\d+$")


@dataclass
class PlannedChange:
    path: Path
    action: str


class VersionBump:
    def __init__(self, root: Path, version: str, release_date: str, dry_run: bool) -> None:
        self.root = root
        self.version = version
        self.release_date = release_date
        self.dry_run = dry_run
        self.changes: list[PlannedChange] = []

    def rel(self, path: Path) -> str:
        return path.relative_to(self.root).as_posix()

    def read(self, path: Path) -> str:
        return path.read_text(encoding="utf-8")

    def write_if_changed(self, path: Path, content: str, action: str) -> None:
        original = self.read(path) if path.exists() else None
        if original == content:
            return

        self.changes.append(PlannedChange(path, action))
        if not self.dry_run:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8", newline="\n")

    def package_version(self) -> str:
        package_json = self.root / "Packages/dev.unity2foxglove.sdk/package.json"
        data = json.loads(self.read(package_json))
        version = data.get("version")
        if not isinstance(version, str) or not VERSION_RE.match(version):
            raise ValueError(f"Cannot read semantic version from {self.rel(package_json)}")
        return version

    def replace_version_property(self, old_version: str) -> None:
        path = self.root / "Packages/dev.unity2foxglove.sdk/package.json"
        text = self.read(path)
        pattern = re.compile(r'("version"\s*:\s*")(\d+\.\d+\.\d+)(")')
        updated, count = pattern.subn(lambda m: f"{m.group(1)}{self.version}{m.group(3)}", text, count=1)
        if count != 1:
            raise ValueError(f"Expected one version property in {self.rel(path)}")
        self.write_if_changed(path, updated, f"set package version {old_version} -> {self.version}")

    def update_phase16_assertion(self) -> None:
        path = self.root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase16Validation.cs"
        text = self.read(path)
        text = re.sub(r'"\\"version\\": \\"\d+\.\d+\.\d+\\""', f'"\\"version\\": \\"{self.version}\\""', text)
        text = re.sub(r"package\.json version is \d+\.\d+\.\d+", f"package.json version is {self.version}", text)
        self.write_if_changed(path, text, f"update Phase16 package version assertion to {self.version}")

    def update_readme(self, old_version: str) -> None:
        path = self.root / "README.md"
        text = self.read(path)
        text = text.replace(f"release-v{old_version}", f"release-v{self.version}")
        text = text.replace(f"verified for v{old_version}", f"verified for v{self.version}")

        release_note = f"- [v{self.version} release notes](RELEASE_NOTES_v{self.version}.md)"
        if release_note not in text:
            marker = re.compile(r"(^- \[v\d+\.\d+\.\d+ release notes\]\(RELEASE_NOTES_v\d+\.\d+\.\d+\.md\)$)", re.MULTILINE)
            match = marker.search(text)
            if match:
                text = text[: match.start()] + release_note + "\n" + text[match.start() :]
            else:
                text += f"\n{release_note}\n"

        self.write_if_changed(path, text, f"update README version references to {self.version}")

    def update_package_readme(self, old_version: str) -> None:
        path = self.root / "Packages/dev.unity2foxglove.sdk/README.md"
        text = self.read(path)
        text = text.replace(f"verified for v{old_version}", f"verified for v{self.version}")
        self.write_if_changed(path, text, f"update package README verified version to {self.version}")

    def update_changelog(self) -> None:
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
        text = text.replace(delimiter, delimiter + entry, 1)
        self.write_if_changed(path, text, f"insert changelog section for {self.version}")

    def create_release_notes(self) -> None:
        path = self.root / f"RELEASE_NOTES_v{self.version}.md"
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
            "```powershell\n"
            "dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj\n"
            "python Scripts/validate_release_package.py\n"
            "python Scripts/run_performance_baseline.py --quick --output build/performance/release\n"
            "```\n"
        )
        self.write_if_changed(path, content, f"create release notes for {self.version}")

    def run(self) -> int:
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
            return 0

        print(f"{prefix} planned changes:" if self.dry_run else f"{prefix} updated files:")
        for change in self.changes:
            print(f"  - {self.rel(change.path)}: {change.action}")
        return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Synchronize Unity2Foxglove package version references.")
    parser.add_argument("version", help="Target semantic version, for example 1.2.0.")
    parser.add_argument("--date", default=date.today().isoformat(), help="Release date for new changelog/release notes.")
    parser.add_argument("--dry-run", action="store_true", help="Print planned changes without writing files.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not VERSION_RE.match(args.version):
        raise SystemExit(f"Invalid version '{args.version}'. Expected MAJOR.MINOR.PATCH.")

    root = Path(__file__).resolve().parents[1]
    return VersionBump(root, args.version, args.date, args.dry_run).run()


if __name__ == "__main__":
    raise SystemExit(main())
