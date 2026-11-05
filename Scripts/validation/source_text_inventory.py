"""Inventory source-text assertions in runtime and unit validation files."""

from __future__ import annotations

import argparse
import csv
import re
from pathlib import Path


TOKEN_PATTERN = re.compile(r"\.Contains\(|\.IndexOf\(|TestSources\.Text\(|ReadRequiredRepoText\(")


def classify(target: str, line: str) -> str:
    """Classify one source-text assertion as boundary, wiring, or implementation."""
    if "!" in line and ".Contains(" in line:
        return "A"
    if target.startswith(".github/") or target.startswith("Scripts/release/run_ci"):
        return "B"
    if target.endswith(".md") or target.endswith("package.json") or "Registry" in target:
        return "B"
    return "C"


def resolve_target(lines: list[str], index: int) -> str:
    """Resolve a nearby quoted repository path when one is present."""
    window = "".join(lines[max(0, index - 3) : index + 1])
    match = re.search(r'"([^"\n]+(?:\.cs|\.py|\.md|\.json|\.yml))"', window)
    return match.group(1).replace("\\", "/") if match else "<unresolved>"


def scan_file(path: Path, root: Path) -> list[dict[str, object]]:
    """Collect source-text assertions from one validation file."""
    lines = path.read_text(encoding="utf-8").splitlines()
    rows: list[dict[str, object]] = []
    for index, line in enumerate(lines):
        if not TOKEN_PATTERN.search(line):
            continue
        target = resolve_target(lines, index)
        rows.append(
            {
                "file": path.relative_to(root).as_posix(),
                "line": index + 1,
                "class": classify(target, line),
                "target": target,
                "token": line.strip(),
                "comment_only": line.lstrip().startswith("//"),
            }
        )
    return rows


def inventory(root: Path) -> list[dict[str, object]]:
    """Scan runtime and unit C# validation files under every package."""
    rows: list[dict[str, object]] = []
    for pattern in ("Packages/*/Tests/Runtime/**/*.cs", "Packages/*/Tests/Unit/**/*.cs"):
        for path in sorted(root.glob(pattern)):
            rows.extend(scan_file(path, root))
    return rows


def write_report(rows: list[dict[str, object]], output: Path) -> None:
    """Write CSV and Markdown summary files for the inventory."""
    output.mkdir(parents=True, exist_ok=True)
    csv_path = output / "source_text_inventory.csv"
    with csv_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=("file", "line", "class", "target", "token", "comment_only"))
        writer.writeheader()
        writer.writerows(rows)
    counts = {label: sum(row["class"] == label for row in rows) for label in ("A", "B", "C")}
    comments = sum(bool(row["comment_only"]) for row in rows)
    (output / "SUMMARY.md").write_text(
        "# Source-text assertion inventory\n\n"
        f"- Total tokens: {len(rows)}\n"
        f"- Class A boundary: {counts['A']}\n"
        f"- Class B wiring: {counts['B']}\n"
        f"- Class C implementation: {counts['C']}\n"
        f"- Comment-only tokens: {comments}\n",
        encoding="utf-8",
    )


def main() -> int:
    """Run the source-text inventory command."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--output", type=Path, default=Path("build/review/source-text-inventory"))
    args = parser.parse_args()
    rows = inventory(args.root.resolve())
    write_report(rows, args.output)
    print(f"SOURCE_TEXT_INVENTORY_OK rows={len(rows)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
