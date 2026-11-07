"""Inventory source-text assertions in runtime and unit validation files."""

from __future__ import annotations

import argparse
import csv
import re
from pathlib import Path


TOKEN_PATTERN = re.compile(r"\.Contains\(|\.IndexOf\(")
PATH_LITERAL_PATTERN = re.compile(
    r'"([^"\r\n]+(?:\.cs|\.py|\.md|\.json|\.yml|\.yaml|\.csproj|\.props|\.targets|\.asmdef|\.meta|\.txt|\.xml|\.cpp|\.h|\.dll|\.so))"'
)
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
    if re.search(r"(?<![=!])!\s*[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*\s*\.\s*(?:Contains|IndexOf)\s*\(", line):
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
            target = target or "<unresolved>"
            if paths.get(match.group("name")) != target:
                paths[match.group("name")] = target
                changed = True
        for match in re.finditer(r"(?:var|string)\s+(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<expression>[^;]+)", text):
            expression = match.group("expression")
            expression_without_literals = re.sub(r'@?"(?:""|\\.|[^"\\])*"', '""', expression)
            referenced = re.findall(r"\b[A-Za-z_]\w*\b", expression_without_literals)
            targets = []
            for name in referenced:
                if name not in paths:
                    continue
                suffix_match = re.search(rf"\b{re.escape(name)}\s*\+\s*\"([^\"]*)\"", expression)
                if suffix_match:
                    suffix = suffix_match.group(1).replace("\\", "/")
                    targets.extend(
                        value.rstrip("/") + "/" + suffix.lstrip("/")
                        for value in paths[name].split(";")
                    )
                else:
                    targets.extend(paths[name].split(";"))
            target = ";".join(dict.fromkeys(";".join(targets).split(";"))) if targets else None
            if target and paths.get(match.group("name")) != target:
                paths[match.group("name")] = target
                changed = True
        if not changed:
            break
    return paths


def resolve_target(lines: list[str], index: int, paths: dict[str, str]) -> str:
    """Resolve the receiver or reader on an assertion line to a repository path."""
    direct = _extract_argument_path(lines[index], paths)
    if direct:
        return direct
    receiver = RECEIVER_PATTERN.search(lines[index])
    if receiver:
        name = receiver.group("receiver").split(".", 1)[0]
        if name in paths:
            return paths[name]
    referenced = []
    for name, target in paths.items():
        if name != "__last_reader__" and re.search(rf"\b{re.escape(name)}\b", lines[index]):
            referenced.extend(target.split(";"))
    if referenced:
        return ";".join(dict.fromkeys(referenced))
    return "<unresolved>"


def _comment_only(lines: list[str], index: int) -> bool:
    """Return whether a token line is inside a C# line or block comment."""
    return _comment_only_flags(lines)[index]


def _comment_only_flags(lines: list[str]) -> list[bool]:
    """Classify every line in one pass without rescanning prior lines."""
    flags: list[bool] = []
    block_comment = False
    string_mode = ""
    for line in lines:
        code = False
        line_comment = False
        cursor = 0
        while cursor < len(line):
            char = line[cursor]
            next_char = line[cursor + 1] if cursor + 1 < len(line) else ""
            if block_comment:
                end = line.find("*/", cursor)
                if end < 0:
                    cursor = len(line)
                    continue
                block_comment = False
                cursor = end + 2
                continue
            if string_mode:
                if string_mode == 'verbatim' and char == '"':
                    if next_char == '"':
                        cursor += 2
                        continue
                    string_mode = ""
                elif string_mode in {'normal', 'char'} and char == "\\":
                    cursor += 2
                    continue
                elif (string_mode == 'normal' and char == '"') or (string_mode == 'char' and char == "'"):
                    string_mode = ""
                cursor += 1
                continue
            if char == '@' and next_char == '"':
                string_mode = 'verbatim'
                cursor += 2
                continue
            if char == '"':
                string_mode = 'normal'
                cursor += 1
                continue
            if char == "'":
                string_mode = 'char'
                cursor += 1
                continue
            if char == '/' and next_char == '/':
                line_comment = True
                break
            if char == '/' and next_char == '*':
                block_comment = True
                cursor += 2
                continue
            if not char.isspace():
                code = True
            cursor += 1
        flags.append(not code and (line_comment or block_comment))
    return flags


def _shared_scope_paths(files: list[Path]) -> dict[Path, dict[str, str]]:
    """Build shared const and reader symbols for partial validation classes."""
    groups: dict[str, list[Path]] = {}
    class_pattern = re.compile(r"\b(?:partial\s+)?(?:class|struct)\s+([A-Za-z_]\w*)")
    for path in files:
        text = path.read_text(encoding="utf-8", errors="replace")
        names = class_pattern.findall(text)
        key = names[0] if names else path.stem
        groups.setdefault(key, []).append(path)
    result: dict[Path, dict[str, str]] = {}
    for group in groups.values():
        lines = []
        for path in group:
            lines.extend(path.read_text(encoding="utf-8", errors="replace").splitlines())
        shared = _source_targets(lines)
        for path in group:
            result[path] = shared
    return result


def _build_xunit_index(root: Path) -> dict[str, list[str]]:
    """Index unit-test files by the type names they reference."""
    index: dict[str, list[str]] = {}
    unit_files = sorted(root.glob("Packages/*/Tests/Unit/**/*.cs"))
    texts = [(path, _strip_csharp_comments_and_strings(path.read_text(encoding="utf-8", errors="replace"))) for path in unit_files]
    product_types: set[str] = set()
    declaration = re.compile(r"\b(?:class|struct|record|interface|enum)\s+([A-Za-z_]\w*)")
    for path in root.glob("Packages/**/*.cs"):
        if "\\Tests\\" in str(path) or "/Tests/" in path.as_posix():
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        product_types.update(declaration.findall(text))
    for path, text in texts:
        referenced = set(re.findall(r"\b[A-Za-z_]\w*\b", text)) & product_types
        for stem in referenced:
            index.setdefault(stem, []).append(path.relative_to(root).as_posix())
    return index


def _strip_csharp_comments_and_strings(text: str) -> str:
    """Remove C# comments and string literals before symbol matching."""
    output: list[str] = []
    index = 0
    mode = ""
    while index < len(text):
        char = text[index]
        next_char = text[index + 1] if index + 1 < len(text) else ""
        if mode == "line":
            if char == "\n":
                output.append(char)
                mode = ""
            else:
                output.append(" ")
            index += 1
            continue
        if mode == "block":
            if char == "*" and next_char == "/":
                output.extend((" ", " "))
                index += 2
                mode = ""
            else:
                output.append("\n" if char == "\n" else " ")
                index += 1
            continue
        if mode in {"string", "verbatim", "char"}:
            if mode == "verbatim" and char == '"':
                if next_char == '"':
                    output.extend((" ", " "))
                    index += 2
                    continue
                mode = ""
            elif mode in {"string", "char"} and char == "\\":
                output.extend((" ", " "))
                index += 2
                continue
            elif (mode == "string" and char == '"') or (mode == "char" and char == "'"):
                mode = ""
            output.append("\n" if char == "\n" else " ")
            index += 1
            continue
        if char == "/" and next_char == "/":
            output.extend((" ", " "))
            index += 2
            mode = "line"
            continue
        if char == "/" and next_char == "*":
            output.extend((" ", " "))
            index += 2
            mode = "block"
            continue
        if char == '@' and next_char == '"':
            output.extend((" ", " "))
            index += 2
            mode = "verbatim"
            continue
        if char == '"':
            output.append(" ")
            index += 1
            mode = "string"
            continue
        if char == "'":
            output.append(" ")
            index += 1
            mode = "char"
            continue
        output.append(char)
        index += 1
    return "".join(output)


def _xunit_coverage(target: str, xunit_index: dict[str, list[str]]) -> str:
    """Return indexed unit-test files whose text references the target type name."""
    targets = [Path(value).stem for value in target.split(";") if value]
    matches = sorted({match for name in targets for match in xunit_index.get(name, [])})
    return ";".join(matches)


def scan_file(path: Path, root: Path, xunit_index: dict[str, list[str]], shared_paths: dict[str, str] | None = None) -> list[dict[str, object]]:
    """Collect source-text assertions from one validation file."""
    lines = path.read_text(encoding="utf-8").splitlines()
    comment_only_flags = _comment_only_flags(lines)
    paths = dict(shared_paths or {})
    for name, target in _source_targets(lines).items():
        if target != "<unresolved>" or name not in paths:
            paths[name] = target
    rows: list[dict[str, object]] = []
    for index, line in enumerate(lines):
        if not TOKEN_PATTERN.search(line):
            continue
        target = resolve_target(lines, index, paths)
        target_parts = []
        for part in target.split(";"):
            candidate = (root / part).resolve() if part and part != "<unresolved>" else None
            if candidate is not None and candidate.is_dir():
                target_parts.append("<directory>")
            elif part:
                target_parts.append(part)
        target = ";".join(dict.fromkeys(target_parts)) or "<unresolved>"
        receiver = RECEIVER_PATTERN.search(line)
        receiver_name = receiver.group("receiver").split(".", 1)[0] if receiver else ""
        referenced_source = next((name for name in paths if name != "__last_reader__" and re.search(rf"\b{re.escape(name)}\b", line)), "")
        source_line = receiver_name in paths or bool(referenced_source) or target != "<unresolved>"
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
                "comment_only": comment_only_flags[index],
            }
        )
    return rows


def inventory(root: Path) -> list[dict[str, object]]:
    """Scan runtime and unit C# validation files under every package."""
    rows: list[dict[str, object]] = []
    xunit_index = _build_xunit_index(root)
    files = sorted({path for pattern in ("Packages/*/Tests/Runtime/**/*.cs", "Packages/*/Tests/Unit/**/*.cs") for path in root.glob(pattern)})
    scopes = _shared_scope_paths(files)
    for path in files:
        rows.extend(scan_file(path, root, xunit_index, scopes.get(path)))
    return rows


def _validation_flag(root: Path, source_file: str) -> str:
    """Resolve a runtime validation file to its PhaseValidationRegistry flag."""
    stem = Path(source_file).stem
    registry = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
    if not registry.is_file():
        return ""
    text = registry.read_text(encoding="utf-8", errors="replace")
    entry_start = re.compile(r"^\s*(?:Ci|Local|DefaultOnly)\(\"(?P<flag>--[A-Za-z0-9-]+)\"", re.MULTILINE)
    matches = list(entry_start.finditer(text))
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        if re.search(rf"\b{re.escape(stem)}\.Validate\w*\b", text[match.start() : end]):
            return match.group("flag")
    return ""


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
    file_targets = sum(
        bool(row["target"])
        and "<directory>" not in str(row["target"])
        and "<unresolved>" not in str(row["target"])
        for row in rows
    )
    directory_targets = sum("<directory>" in str(row["target"]) for row in rows)
    unresolved_targets = sum("<unresolved>" in str(row["target"]) for row in rows)
    (output / "SUMMARY.md").write_text(
        "# Source-text assertion inventory\n\n"
        f"- Total tokens: {len(rows)}\n"
        f"- Class A boundary: {counts['A']}\n"
        f"- Class B wiring: {counts['B']}\n"
        f"- Class C implementation: {counts['C']}\n"
        f"- Comment-only tokens: {comments}\n"
        f"- File targets: {file_targets} / {len(rows)}\n"
        f"- Directory targets: {directory_targets}\n"
        f"- Unresolved targets: {unresolved_targets}\n"
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
