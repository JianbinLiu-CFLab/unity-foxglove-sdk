"""Report generation for source-text inventory results."""

from __future__ import annotations

import csv
from collections import Counter
from pathlib import Path


def write_report(rows: list[dict[str, object]], output: Path) -> None:
    """Write CSV and Markdown summary files for the inventory."""
    output.mkdir(parents=True, exist_ok=True)
    csv_path = output / "source_text_inventory.csv"
    fields = (
        "file",
        "line",
        "label",
        "validation_flag",
        "dispatch",
        "class",
        "target",
        "unresolved_reason",
        "xunit_coverage",
        "token",
        "assertion_signature",
        "reader_signature",
        "checked_expression",
        "assertion_context",
        "assertion_literal",
        "assertion_polarity",
        "harness_signature",
        "comment_only",
        "reachability",
        "call_path",
        "call_binding",
    )
    with csv_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)
    counts = {
        label: sum(row.get("class") == label for row in rows)
        for label in ("A", "B", "C", "UNKNOWN", "UNRESOLVED")
    }
    comments = sum(bool(row.get("comment_only")) for row in rows)
    reasons = Counter(
        str(row.get("unresolved_reason", "")) for row in rows if row.get("unresolved_reason")
    )
    supported = [
        row for row in rows if row.get("unresolved_reason") not in {"dynamic_path", "roslyn_syntax"}
    ]
    resolved_supported = sum(row.get("class") not in {"UNKNOWN", "UNRESOLVED"} for row in supported)
    rate = 100.0 * resolved_supported / len(supported) if supported else 100.0
    by_flag: dict[str, dict[str, int]] = {}
    for row in rows:
        flag = str(row.get("validation_flag") or "<unregistered>")
        summary = by_flag.setdefault(
            flag, {"total": 0, "class_c": 0, "comment_only": 0, "covered": 0}
        )
        summary["total"] += 1
        summary["class_c"] += int(row.get("class") == "C")
        summary["comment_only"] += int(bool(row.get("comment_only")))
        summary["covered"] += int(bool(row.get("xunit_coverage")))
    ranking = (
        "\n## Validation ranking\n\n"
        "| Flag | Tokens | Class C | xUnit-covered targets | Comment-only |\n"
        "|---|---:|---:|---:|---:|\n"
    )
    for flag, summary in sorted(by_flag.items(), key=lambda item: (-item[1]["class_c"], item[0])):
        ranking += (
            f"| `{flag}` | {summary['total']} | {summary['class_c']} | "
            f"{summary['covered']} | {summary['comment_only']} |\n"
        )
    file_targets = sum(
        bool(row.get("target"))
        and "<directory>" not in str(row.get("target"))
        and "<unresolved>" not in str(row.get("target"))
        for row in rows
    )
    directory_targets = sum("<directory>" in str(row.get("target")) for row in rows)
    reason_text = (
        "\n".join(f"- `{reason}`: {count}" for reason, count in sorted(reasons.items()))
        or "- None: 0"
    )
    summary = (
        "# Source-text assertion inventory\n\n"
        f"- Total tokens: {len(rows)}\n"
        f"- Class A boundary: {counts['A']}\n"
        f"- Class B wiring: {counts['B']}\n"
        f"- Class C implementation: {counts['C']}\n"
        f"- Unknown targets: {counts['UNKNOWN']}\n"
        f"- Unresolved targets: {counts['UNRESOLVED']}\n"
        f"- Comment-only tokens: {comments}\n"
        f"- File targets: {file_targets} / {len(rows)}\n"
        f"- Directory targets: {directory_targets}\n"
        "\n## Unresolved reasons\n\n"
        f"{reason_text}\n"
        "\n## Supported-form resolution\n\n"
        "Denominator excludes `dynamic_path` and `roslyn_syntax`.\n\n"
        f"- Resolved supported forms: {resolved_supported} / {len(supported)} ({rate:.2f}%)\n"
        + ranking
    )
    (output / "SUMMARY.md").write_text(summary, encoding="utf-8")
