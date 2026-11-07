"""Inventory source-text assertions in runtime and unit validation files."""

from __future__ import annotations

import argparse
import csv
import re
from pathlib import Path


TOKEN_PATTERN = re.compile(r"\.Contains\(|\.IndexOf\(|TestSources\.Text\(|ReadRepoText\(|ReadRequiredRepoText\(")
PATH_LITERAL_PATTERN = re.compile(r'"([^"\r\n]+(?:\.cs|\.py|\.md|\.json|\.yml|\.yaml))"')
READER_PATTERN = re.compile(
    r"(?:ReadRepoText|ReadRequiredRepoText|PhaseValidationSourceHelpers\.ReadRequiredRepoText|TestSources\.Text)\s*\(\s*(?P<argument>[^)]*)\)"
)
VARIABLE_READER_PATTERN = re.compile(
    r"(?:var|string)\s+(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<reader>ReadRepoText|ReadRequiredRepoText|PhaseValidationSourceHelpers\.ReadRequiredRepoText|TestSources\.Text)\s*\(\s*(?P<argument>[^)]*?)\)",
    re.DOTALL,
)
ARRAY_PATTERN = re.compile(r"(?:var|string\[\])\s+(?P<name>[A-Za-z_]\w*)\s*=\s*new\[\]\s*\{(?P<body>[^}]*)\}", re.DOTALL)
CONSTANT_PATH_PATTERN = re.compile(r"(?:const\s+string|private\s+const\s+string)\s+(?P<name>[A-Za-z_]\w*)\s*=\s*\"(?P<path>[^\"]+)\"\s*;?")
RECEIVER_PATTERN = re.compile(r"(?P<receiver>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\.(?:Contains|IndexOf)\s*\(")
PHASE_LABEL_PATTERN = re.compile(r"\"(?P<label>\d+[A-Za-z]?[-A-Za-z0-9]*-[A-Za-z0-9]+:[^\"]+)\"")


def classify(target: str, line: str) -> str:
    """Classify one source-text assertion as boundary, wiring, or implementation."""
    if "!" in line and ".Contains(" in line:
        return "A"
    if target.startswith(".github/") or target.startswith("Scripts/release/run_ci"):
        return "B"
    if target.endswith(".md") or target.endswith("package.json") or "Registry" in target:
        return "B"
    return "C"


def _normalise_path(value: str) -> str:
    """Normalise a repository-relative path for stable reports."""
    return value.strip().strip('"').replace("\\", "/")


def _extract_argument_path(argument: str, paths: dict[str, str]) -> str | None:
    """Resolve a reader argument to a repository path or a documented path set."""
    literal = PATH_LITERAL_PATTERN.search(argument)
    if literal:
        return _normalise_path(literal.group(1))
    argument = argument.strip()
    return paths.get(argument)


def _source_targets(lines: list[str]) -> dict[str, str]:
    """Build a local symbol-to-source-path map from reader calls and path arrays."""
    paths: dict[str, str] = {}
    text = "\n".join(lines)
    for match in ARRAY_PATTERN.finditer(text):
        values = [_normalise_path(value) for value in PATH_LITERAL_PATTERN.findall(match.group("body"))]
        if values:
            paths[match.group("name")] = ";".join(values)
    for match in CONSTANT_PATH_PATTERN.finditer(text):
        value = _normalise_path(match.group("path"))
        if "/" in value or "\\" in value:
            paths[match.group("name")] = value
    for _ in range(3):
        changed = False
        for match in VARIABLE_READER_PATTERN.finditer(text):
            target = _extract_argument_path(match.group("argument"), paths)
            if target and paths.get(match.group("name")) != target:
                paths[match.group("name")] = target
                changed = True
        for match in re.finditer(r"(?:var|string)\s+(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<expression>[^;]+)", text):
            expression = match.group("expression")
            referenced = re.findall(r"\b[A-Za-z_]\w*\b", expression)
            target = next((paths[name] for name in referenced if name in paths), None)
            if target and paths.get(match.group("name")) != target:
                paths[match.group("name")] = target
                changed = True
        if not changed:
            break
    return paths


def resolve_target(lines: list[str], index: int, paths: dict[str, str]) -> str:
    """Resolve the receiver or reader on an assertion line to a repository path."""
    window = "".join(lines[max(0, index - 4) : index + 1])
    direct = _extract_argument_path(window, paths)
    if direct:
        return direct
    receiver = RECEIVER_PATTERN.search(lines[index])
    if receiver:
        name = receiver.group("receiver").split(".", 1)[0]
        if name in paths:
            return paths[name]
    for name, target in paths.items():
        if name != "__last_reader__" and re.search(rf"\b{re.escape(name)}\b", lines[index]):
            return target
    if paths.get("__last_reader__"):
        return paths["__last_reader__"]
    return "<unresolved>"


def _comment_only(lines: list[str], index: int) -> bool:
    """Return whether a token line is inside a C# line or block comment."""
    in_block = False
    for line_index, line in enumerate(lines[: index + 1]):
        cursor = 0
        while cursor < len(line):
            if in_block:
                end = line.find("*/", cursor)
                if end < 0:
                    break
                in_block = False
                cursor = end + 2
                continue
            start = line.find("/*", cursor)
            line_comment = line.find("//", cursor)
            if line_comment >= 0 and (start < 0 or line_comment < start):
                if line_index == index:
                    return not line[:line_comment].strip()
                break
            if start < 0:
                break
            in_block = True
            cursor = start + 2
    return in_block


def _build_xunit_index(root: Path) -> dict[str, list[str]]:
    """Index unit-test files by the type names they reference."""
    index: dict[str, list[str]] = {}
    unit_files = sorted(root.glob("Packages/*/Tests/Unit/**/*.cs"))
    texts = [(path, path.read_text(encoding="utf-8", errors="replace")) for path in unit_files]
    stems = {path.stem for path in root.glob("Packages/*/Tests/Runtime/**/*.cs")}
    for path, text in texts:
        referenced = set(re.findall(r"\b[A-Za-z_]\w*\b", text)) & stems
        for stem in referenced:
            index.setdefault(stem, []).append(path.relative_to(root).as_posix())
    return index


def _xunit_coverage(target: str, xunit_index: dict[str, list[str]]) -> str:
    """Return indexed unit-test files whose text references the target type name."""
    targets = [Path(value).stem for value in target.split(";") if value]
    matches = sorted({match for name in targets for match in xunit_index.get(name, [])})
    return ";".join(matches)


def scan_file(path: Path, root: Path, xunit_index: dict[str, list[str]]) -> list[dict[str, object]]:
    """Collect source-text assertions from one validation file."""
    lines = path.read_text(encoding="utf-8").splitlines()
    paths = _source_targets(lines)
    rows: list[dict[str, object]] = []
    for index, line in enumerate(lines):
        if not TOKEN_PATTERN.search(line):
            continue
        target = resolve_target(lines, index, paths)
        receiver = RECEIVER_PATTERN.search(line)
        receiver_name = receiver.group("receiver").split(".", 1)[0] if receiver else ""
        referenced_source = next((name for name in paths if name != "__last_reader__" and re.search(rf"\b{re.escape(name)}\b", line)), "")
        source_line = (
            "TestSources.Text(" in line
            or "ReadRepoText(" in line
            or "ReadRequiredRepoText(" in line
            or receiver_name in paths
            or bool(referenced_source)
        )
        if not source_line:
            continue
        label_match = PHASE_LABEL_PATTERN.search("".join(lines[max(0, index - 5) : index + 1]))
        rows.append(
            {
                "file": path.relative_to(root).as_posix(),
                "line": index + 1,
                "label": label_match.group("label") if label_match else "",
                "validation_flag": _validation_flag(root, path.relative_to(root).as_posix()),
                "class": classify(target, line),
                "target": target,
                "xunit_coverage": _xunit_coverage(target, xunit_index),
                "token": line.strip(),
                "comment_only": _comment_only(lines, index),
            }
        )
    return rows


def inventory(root: Path) -> list[dict[str, object]]:
    """Scan runtime and unit C# validation files under every package."""
    rows: list[dict[str, object]] = []
    xunit_index = _build_xunit_index(root)
    for pattern in ("Packages/*/Tests/Runtime/**/*.cs", "Packages/*/Tests/Unit/**/*.cs"):
        for path in sorted(root.glob(pattern)):
            rows.extend(scan_file(path, root, xunit_index))
    return rows


def _validation_flag(root: Path, source_file: str) -> str:
    """Resolve a runtime validation file to its PhaseValidationRegistry flag."""
    stem = Path(source_file).stem
    registry = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
    if not registry.is_file():
        return ""
    text = registry.read_text(encoding="utf-8", errors="replace")
    match = re.search(rf'(?P<flag>--[A-Za-z0-9-]+)"[^\n]*\b{re.escape(stem)}\.Validate\b', text)
    return match.group("flag") if match else ""


def write_report(rows: list[dict[str, object]], output: Path) -> None:
    """Write CSV and Markdown summary files for the inventory."""
    output.mkdir(parents=True, exist_ok=True)
    csv_path = output / "source_text_inventory.csv"
    with csv_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=("file", "line", "label", "validation_flag", "class", "target", "xunit_coverage", "token", "comment_only"))
        writer.writeheader()
        writer.writerows(rows)
    counts = {label: sum(row["class"] == label for row in rows) for label in ("A", "B", "C")}
    comments = sum(bool(row["comment_only"]) for row in rows)
    by_flag: dict[str, dict[str, int]] = {}
    for row in rows:
        flag = str(row["validation_flag"] or "<unregistered>")
        summary = by_flag.setdefault(flag, {"total": 0, "class_c": 0, "comment_only": 0, "covered": 0})
        summary["total"] += 1
        summary["class_c"] += int(row["class"] == "C")
        summary["comment_only"] += int(bool(row["comment_only"]))
        summary["covered"] += int(bool(row["xunit_coverage"]))
    ranking = "\n## Validation ranking\n\n| Flag | Tokens | Class C | xUnit-covered targets | Comment-only |\n|---|---:|---:|---:|---:|\n"
    for flag, summary in sorted(by_flag.items(), key=lambda item: (-item[1]["class_c"], item[0])):
        ranking += f"| `{flag}` | {summary['total']} | {summary['class_c']} | {summary['covered']} | {summary['comment_only']} |\n"
    (output / "SUMMARY.md").write_text(
        "# Source-text assertion inventory\n\n"
        f"- Total tokens: {len(rows)}\n"
        f"- Class A boundary: {counts['A']}\n"
        f"- Class B wiring: {counts['B']}\n"
        f"- Class C implementation: {counts['C']}\n"
        f"- Comment-only tokens: {comments}\n"
        f"- Resolved targets: {sum(row['target'] != '<unresolved>' for row in rows)} / {len(rows)}\n"
        + ranking,
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
