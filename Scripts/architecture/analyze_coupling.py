#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Report conservative architecture coupling, fan-in/fan-out,
# file-size, asmdef dependency, and private-boundary metrics.
# Usage: python Scripts/architecture/analyze_coupling.py --format text
# Inputs: Git-tracked repository files, C# sources, Unity asmdefs, and tests.
# Outputs: Text or JSON architecture report, optionally written under build/architecture/.

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable


EXIT_SUCCESS = 0
EXIT_FAILURE = 1
DEFAULT_HOTSPOT_LIMIT = 20
HOTSPOT_WARN_LINES = 500
HOTSPOT_HIGH_LINES = 800
NAMESPACE_RE = re.compile(r"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)", re.MULTILINE)
USING_RE = re.compile(r"^\s*using\s+(?:static\s+)?([A-Za-z_][A-Za-z0-9_.]*)\s*;", re.MULTILINE)
QUALIFIED_RE = re.compile(r"\b([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*){1,})\b")


@dataclass
class CSharpFileMetric:
    """Stores per-file C# line count and namespace dependency metrics."""

    path: str
    namespace: str
    line_count: int
    fan_out: list[str] = field(default_factory=list)
    fan_in: list[str] = field(default_factory=list)


@dataclass
class AsmdefMetric:
    """Stores one Unity asmdef name and normalized reference list."""

    path: str
    name: str
    references: list[str]


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description="Analyze Unity2Foxglove architecture coupling.")
    parser.add_argument("--repo-root", help="Repository root. Defaults to auto-detection from this script.")
    parser.add_argument("--format", choices=("text", "json"), default="text", help="Report format.")
    parser.add_argument("--output", help="Optional report path, for example build/architecture/phase126-coupling-report.txt.")
    parser.add_argument("--include-generated", action="store_true", help="Include generated C# sources in line metrics.")
    parser.add_argument("--hotspot-limit", type=int, default=DEFAULT_HOTSPOT_LIMIT, help="Maximum hotspots to print.")
    return parser.parse_args()


def repo_root_from(start: Path) -> Path:
    """Find the repository root by walking upward until .git is found."""
    current = start.resolve()
    for candidate in (current, *current.parents):
        if (candidate / ".git").exists():
            return candidate
    raise FileNotFoundError(f"Could not locate repository root from {start}")


def run_git_ls_files(repo_root: Path, *pathspecs: str) -> list[str]:
    """Return git-tracked paths for optional pathspecs."""
    command = ["git", "ls-files", *pathspecs]
    result = subprocess.run(command, cwd=repo_root, text=True, capture_output=True, check=False)
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or "git ls-files failed")
    return [line.strip().replace("\\", "/") for line in result.stdout.splitlines() if line.strip()]


def is_generated_source(path: str) -> bool:
    """Return true for generated C# files that should be hidden by default."""
    normalized = path.replace("\\", "/")
    name = Path(normalized).name
    return (
        name.endswith(".g.cs")
        or "/Generated/" in normalized
        or "/Runtime/Schemas/Proto/Generated/" in normalized
        or "/obj/" in normalized
        or "/bin/" in normalized
    )


def is_text_test_file(path: str) -> bool:
    """Return true for tracked default-test source files that can reference private paths."""
    normalized = path.replace("\\", "/")
    return normalized.startswith("Packages/dev.unity2foxglove.sdk/Tests/Runtime/") and normalized.endswith(".cs")


def read_text(repo_root: Path, relative_path: str) -> str:
    """Read a tracked text file with UTF-8 fallback behavior."""
    path = repo_root / relative_path
    try:
        return path.read_text(encoding="utf-8")
    except UnicodeDecodeError:
        return path.read_text(encoding="utf-8", errors="replace")


def find_namespace(text: str) -> str:
    """Find the first declared namespace, or return the global namespace label."""
    match = NAMESPACE_RE.search(text)
    return match.group(1) if match else "<global>"


def namespace_candidates(known_namespaces: set[str], text: str) -> set[str]:
    """Find using and fully qualified namespace references in source text."""
    references: set[str] = set()
    for match in USING_RE.finditer(text):
        namespace = match.group(1)
        if namespace in known_namespaces:
            references.add(namespace)
        else:
            parts = namespace.split(".")
            for index in range(len(parts), 0, -1):
                candidate = ".".join(parts[:index])
                if candidate in known_namespaces:
                    references.add(candidate)
                    break

    for match in QUALIFIED_RE.finditer(text):
        qualified = match.group(1)
        parts = qualified.split(".")
        for index in range(len(parts), 1, -1):
            candidate = ".".join(parts[:index])
            if candidate in known_namespaces:
                references.add(candidate)
                break
    return references


def collect_csharp_metrics(repo_root: Path, tracked_files: list[str], include_generated: bool) -> list[CSharpFileMetric]:
    """Collect C# line counts plus namespace fan-in and fan-out metrics."""
    csharp_files = [
        path
        for path in tracked_files
        if path.endswith(".cs") and (include_generated or not is_generated_source(path))
        and (repo_root / path).exists()
    ]
    namespace_by_file: dict[str, str] = {}
    text_by_file: dict[str, str] = {}
    for path in csharp_files:
        text = read_text(repo_root, path)
        text_by_file[path] = text
        namespace_by_file[path] = find_namespace(text)

    known_namespaces = {namespace for namespace in namespace_by_file.values() if namespace != "<global>"}
    fan_out_by_file: dict[str, set[str]] = {}
    fan_in_by_namespace: dict[str, set[str]] = defaultdict(set)

    for path, text in text_by_file.items():
        own_namespace = namespace_by_file[path]
        references = namespace_candidates(known_namespaces, text)
        references.discard(own_namespace)
        fan_out_by_file[path] = references
        for namespace in references:
            fan_in_by_namespace[namespace].add(path)

    metrics = []
    for path in csharp_files:
        namespace = namespace_by_file[path]
        line_count = len(text_by_file[path].splitlines())
        metrics.append(
            CSharpFileMetric(
                path=path,
                namespace=namespace,
                line_count=line_count,
                fan_out=sorted(fan_out_by_file[path]),
                fan_in=sorted(fan_in_by_namespace.get(namespace, set())),
            )
        )
    return sorted(metrics, key=lambda item: item.path)


def normalize_asmdef_reference(reference: str) -> str:
    """Normalize Unity asmdef references such as GUID or assembly-name references."""
    if reference.startswith("GUID:"):
        return reference
    return reference.split("/")[-1]


def collect_asmdef_metrics(repo_root: Path, tracked_files: list[str]) -> list[AsmdefMetric]:
    """Collect Unity asmdef reference metrics."""
    metrics = []
    for path in tracked_files:
        if not path.endswith(".asmdef"):
            continue
        if not (repo_root / path).exists():
            continue
        try:
            payload = json.loads(read_text(repo_root, path))
        except json.JSONDecodeError:
            metrics.append(AsmdefMetric(path, "<invalid-json>", []))
            continue
        name = str(payload.get("name", ""))
        references = [normalize_asmdef_reference(str(item)) for item in payload.get("references", [])]
        metrics.append(AsmdefMetric(path, name, sorted(references)))
    return sorted(metrics, key=lambda item: item.path)


def find_asmdef_cycles(metrics: list[AsmdefMetric]) -> list[list[str]]:
    """Find simple asmdef dependency cycles by assembly name."""
    graph = {metric.name: [ref for ref in metric.references if not ref.startswith("GUID:")] for metric in metrics if metric.name}
    cycles: list[list[str]] = []

    def visit(node: str, stack: list[str]) -> None:
        """Depth-first cycle walk for one asmdef node."""
        if node in stack:
            cycle = stack[stack.index(node) :] + [node]
            if cycle not in cycles:
                cycles.append(cycle)
            return
        for child in graph.get(node, []):
            visit(child, stack + [node])

    for node in graph:
        visit(node, [])
    return cycles


def find_default_test_private_references(repo_root: Path, tracked_files: list[str]) -> list[str]:
    """Find test sources that mention ignored Plan/ or Developer/ paths."""
    default_files = find_registry_default_test_files(repo_root)
    offenders = []
    for path in tracked_files:
        if not is_text_test_file(path):
            continue
        if default_files and path not in default_files:
            continue
        if not (repo_root / path).exists():
            continue
        text = read_text(repo_root, path)
        if depends_on_private_workspace(text):
            offenders.append(path)
    return sorted(offenders)


def depends_on_private_workspace(text: str) -> bool:
    """Return true when code appears to read ignored Plan/ or Developer/ content."""
    private_path = r"(?:Plan|Developer)[/\\]"
    patterns = (
        rf"ReadRepoText\(\s*\"{private_path}",
        rf"RepoFileExists\(\s*\"{private_path}",
        rf"RepoPath\(\s*\"{private_path}",
        rf"File\.(?:ReadAllText|ReadAllLines|Exists)\(\s*[^;\n]*\"{private_path}",
    )
    return any(re.search(pattern, text) for pattern in patterns)


def find_registry_default_test_files(repo_root: Path) -> set[str]:
    """Infer default validation source files from the C# phase registry."""
    registry_path = "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs"
    full_path = repo_root / registry_path
    if not full_path.exists():
        return set()

    files: set[str] = set()
    for line in read_text(repo_root, registry_path).splitlines():
        if "includeInDefault: false" in line:
            continue
        if "DefaultOnly(" not in line and "Ci(" not in line:
            continue
        for class_name in re.findall(r"\b([A-Za-z0-9_]+Validation)\.", line):
            files.add(f"Packages/dev.unity2foxglove.sdk/Tests/Runtime/{class_name}.cs")
    return files


def top_hotspots(metrics: Iterable[CSharpFileMetric], limit: int) -> list[CSharpFileMetric]:
    """Return the largest C# file metrics."""
    return sorted(metrics, key=lambda item: item.line_count, reverse=True)[:limit]


def build_report(repo_root: Path, args: argparse.Namespace) -> dict[str, object]:
    """Build the architecture report model."""
    tracked_files = run_git_ls_files(repo_root)
    csharp_metrics = collect_csharp_metrics(repo_root, tracked_files, args.include_generated)
    asmdef_metrics = collect_asmdef_metrics(repo_root, tracked_files)
    tracked_root_private = run_git_ls_files(repo_root, "Plan/**", "Developer/**")
    tracked_nested_developer_paths = sorted(
        path
        for path in tracked_files
        if "/Developer/" in path or path.endswith("/Developer.meta")
        if (repo_root / path).exists()
    )
    high_line_files = [
        metric
        for metric in csharp_metrics
        if metric.line_count >= HOTSPOT_WARN_LINES
    ]

    return {
        "repo_root": str(repo_root),
        "tracked_file_count": len(tracked_files),
        "csharp_file_count": len(csharp_metrics),
        "hotspot_warn_lines": HOTSPOT_WARN_LINES,
        "hotspot_high_lines": HOTSPOT_HIGH_LINES,
        "largest_csharp_files": [metric.__dict__ for metric in top_hotspots(csharp_metrics, args.hotspot_limit)],
        "hotspot_files": [metric.__dict__ for metric in sorted(high_line_files, key=lambda item: item.line_count, reverse=True)],
        "asmdefs": [metric.__dict__ for metric in asmdef_metrics],
        "asmdef_cycles": find_asmdef_cycles(asmdef_metrics),
        "tracked_root_private_paths": tracked_root_private,
        "tracked_nested_developer_paths": tracked_nested_developer_paths,
        "default_test_private_references": find_default_test_private_references(repo_root, tracked_files),
    }


def render_text(report: dict[str, object]) -> str:
    """Render a human-readable architecture report."""
    lines = [
        "Unity2Foxglove Phase126 Architecture Coupling Report",
        "====================================================",
        f"repo_root: {report['repo_root']}",
        f"tracked_file_count: {report['tracked_file_count']}",
        f"csharp_file_count: {report['csharp_file_count']}",
        "",
        "Largest C# files:",
    ]

    for item in report["largest_csharp_files"]:
        severity = "HIGH" if item["line_count"] >= HOTSPOT_HIGH_LINES else "WARN" if item["line_count"] >= HOTSPOT_WARN_LINES else "OK"
        lines.append(
            f"- {severity} {item['line_count']:>5} lines {item['path']} "
            f"(namespace={item['namespace']}, fan-in={len(item['fan_in'])}, fan-out={len(item['fan_out'])})"
        )

    lines.extend(["", "Asmdef references:"])
    for item in report["asmdefs"]:
        references = ", ".join(item["references"]) if item["references"] else "(none)"
        lines.append(f"- {item['name']} <- {item['path']} references: {references}")

    lines.extend(["", "Asmdef cycles:"])
    cycles = report["asmdef_cycles"]
    if cycles:
        for cycle in cycles:
            lines.append("- " + " -> ".join(cycle))
    else:
        lines.append("- none")

    lines.extend(["", "Private boundary findings:"])
    append_list(lines, "tracked_root_private_paths", report["tracked_root_private_paths"])
    append_list(lines, "tracked_nested_developer_paths", report["tracked_nested_developer_paths"])
    append_list(lines, "default_test_private_references", report["default_test_private_references"])
    return "\n".join(lines) + "\n"


def append_list(lines: list[str], label: str, values: object) -> None:
    """Append a labeled list to text report lines."""
    items = list(values)
    if not items:
        lines.append(f"- {label}: none")
        return
    lines.append(f"- {label}:")
    for item in items:
        lines.append(f"  - {item}")


def write_output(payload: str, output: str | None) -> None:
    """Write report payload to stdout or the requested output path."""
    if output is None:
        sys.stdout.write(payload)
        return
    output_path = Path(output)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(payload, encoding="utf-8")
    print(f"wrote {output_path}")


def main() -> int:
    """Run the architecture report command."""
    args = parse_args()
    try:
        repo_root = Path(args.repo_root).resolve() if args.repo_root else repo_root_from(Path(__file__))
        report = build_report(repo_root, args)
        payload = json.dumps(report, indent=2, sort_keys=True) + "\n" if args.format == "json" else render_text(report)
        write_output(payload, args.output)
        return EXIT_SUCCESS
    except Exception as exc:
        print(f"analyze_coupling: {exc}", file=sys.stderr)
        return EXIT_FAILURE


if __name__ == "__main__":
    raise SystemExit(main())
