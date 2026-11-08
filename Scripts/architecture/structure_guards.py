"""Enforce source-size, partial-type, and phase-path growth guards."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Iterable


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
BASELINE_PATH = REPOSITORY_ROOT / "Scripts" / "architecture" / "structure_guard_baseline.json"
CODE_SUFFIXES = {".cs", ".py"}
FILE_LIMIT = 1000
PARTIAL_LIMIT = 2000
PARTIAL_PATTERN = re.compile(
    r"\bpartial\s+(?:(?:class|struct|interface)|record(?:\s+(?:class|struct))?)\s+([A-Za-z_]\w*)"
)
NAMESPACE_PATTERN = re.compile(r"\bnamespace\s+([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)")
PHASE_PATTERN = re.compile(r"(?i)phase\d+")


def _relative_path(path: str | Path) -> str:
    """Return a repository-style path with forward slashes."""
    return Path(path).as_posix().lstrip("./")


def _is_excluded(path: str) -> bool:
    """Return whether a tracked source path is outside the guard scope."""
    parts = Path(path).parts
    return "third-party" in parts or "Generated" in parts or path.endswith(".g.cs")


def line_count(path: Path) -> int:
    """Count physical lines, including a final unterminated line."""
    content = path.read_bytes()
    if not content:
        return 0
    return content.count(b"\n") + (0 if content.endswith(b"\n") else 1)


def _source_paths(root: Path, tracked_paths: Iterable[str]) -> list[str]:
    """Return in-scope tracked C# and Python paths."""
    return sorted(
        path
        for path in (_relative_path(item) for item in tracked_paths)
        if Path(path).suffix in CODE_SUFFIXES and not _is_excluded(path) and (root / path).is_file()
    )


def _namespace(source: str) -> str:
    """Return the first declared namespace or an empty namespace key."""
    match = NAMESPACE_PATTERN.search(source)
    return match.group(1) if match else ""


def _partial_names(source: str) -> set[str]:
    """Return partial type names declared in one source file."""
    return {match.group(1) for match in PARTIAL_PATTERN.finditer(source)}


def _package_root(path: str) -> str:
    """Return the package identity used in a partial-type key."""
    parts = Path(path).parts
    if len(parts) >= 2 and parts[0] == "Packages":
        return "/".join(parts[:2])
    return parts[0] if parts else ""


def _partial_key(path: str, namespace: str, name: str) -> str:
    """Build the stable serialized key for a partial type."""
    return "|".join((_package_root(path), namespace, name))


def _partial_totals(root: Path, paths: Iterable[str]) -> dict[str, int]:
    """Return total line counts grouped by package, namespace, and partial type."""
    totals: dict[str, int] = {}
    for path in paths:
        if Path(path).suffix != ".cs":
            continue
        source = (root / path).read_text(encoding="utf-8", errors="replace")
        names = _partial_names(source)
        if not names:
            continue
        namespace = _namespace(source)
        count = line_count(root / path)
        for name in names:
            key = _partial_key(path, namespace, name)
            totals[key] = totals.get(key, 0) + count
    return totals


def _phase_prefixes(paths: Iterable[str]) -> set[str]:
    """Return baseline prefixes for phase-numbered source path segments."""
    prefixes: set[str] = set()
    for path in paths:
        path_obj = Path(path)
        if path_obj.suffix not in CODE_SUFFIXES:
            continue
        parts = path_obj.parts
        in_scope = bool(parts and parts[0] == "Scripts") or (
            len(parts) >= 3 and parts[0] == "Packages" and parts[2] in {"Runtime", "Editor", "Tests"}
        )
        if not in_scope:
            continue
        for index, part in enumerate(parts):
            if PHASE_PATTERN.search(part):
                prefixes.add("/".join(parts[: index + 1]))
    return prefixes


def tracked_paths(root: Path) -> list[str]:
    """Return paths reported by git ls-files for a repository root."""
    completed = subprocess.run(
        ["git", "-C", str(root), "ls-files"],
        check=False,
        capture_output=True,
        text=True,
    )
    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.strip() or "git ls-files failed")
    return [line for line in completed.stdout.splitlines() if line]


def tracked_paths_at_revision(root: Path, revision: str) -> list[str]:
    """Return paths reported by git ls-tree for a trusted revision."""
    completed = subprocess.run(
        ["git", "-C", str(root), "ls-tree", "-r", "--name-only", revision],
        check=False,
        capture_output=True,
        text=True,
    )
    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.strip() or "git ls-tree failed")
    return [line for line in completed.stdout.splitlines() if line]


def measured_tree(root: Path, paths: Iterable[str]) -> dict[str, object]:
    """Measure current source ceilings and phase prefixes from tracked paths."""
    source_paths = _source_paths(root, paths)
    counts = {path: line_count(root / path) for path in source_paths}
    partials = _partial_totals(root, source_paths)
    return {
        "files": {path: count for path, count in counts.items() if count > FILE_LIMIT},
        "partial_types": {key: count for key, count in partials.items() if count > PARTIAL_LIMIT},
        "phase_paths": sorted(_phase_prefixes(source_paths)),
    }


def _normalise_baseline(value: object) -> dict[str, object]:
    """Return a baseline with the expected sections and scalar values."""
    if not isinstance(value, dict):
        raise ValueError("baseline must be a JSON object")
    files = value.get("files", {})
    partial_types = value.get("partial_types", {})
    phase_paths = value.get("phase_paths", [])
    if not isinstance(files, dict) or not isinstance(partial_types, dict) or not isinstance(phase_paths, list):
        raise ValueError("baseline sections have invalid types")
    return {
        "files": {str(path): int(limit) for path, limit in files.items()},
        "partial_types": {str(key): int(limit) for key, limit in partial_types.items()},
        "phase_paths": [str(path) for path in phase_paths],
    }


def load_baseline(path: Path = BASELINE_PATH) -> dict[str, object]:
    """Load and validate a structure-guard baseline JSON file."""
    return _normalise_baseline(json.loads(path.read_text(encoding="utf-8")))


def load_baseline_at_revision(root: Path, revision: str) -> dict[str, object] | None:
    """Load the baseline file from a git revision, if that revision has one."""
    relative = _relative_path(BASELINE_PATH.relative_to(root))
    completed = subprocess.run(
        ["git", "-C", str(root), "show", f"{revision}:{relative}"],
        check=False,
        capture_output=True,
        text=True,
    )
    if completed.returncode != 0:
        return None
    return _normalise_baseline(json.loads(completed.stdout))


def baseline_integrity_violations(
    baseline: dict[str, object],
    base_baseline: dict[str, object] | None,
    base_phase_paths: set[str] | None = None,
) -> list[str]:
    """Reject baseline additions and ceiling growth relative to the trusted base."""
    if base_baseline is None:
        return []

    errors: list[str] = []
    for section in ("files", "partial_types"):
        current = baseline[section]
        base = base_baseline[section]
        for key, limit in sorted(current.items()):
            if key not in base:
                errors.append(f"BASELINE {section} added={key} limit={limit}")
            elif int(limit) > int(base[key]):
                errors.append(
                    f"BASELINE {section} raised={key} base={base[key]} head={limit}"
                )

    added_phase_paths = set(baseline["phase_paths"]) - set(base_baseline["phase_paths"])
    if base_phase_paths is not None:
        added_phase_paths -= base_phase_paths
    added_phase_paths = sorted(added_phase_paths)
    errors.extend(f"BASELINE phase_paths added={path}" for path in added_phase_paths)
    return errors


def stale_baseline_violations(
    root: Path,
    paths: Iterable[str],
    baseline: dict[str, object],
) -> list[str]:
    """Reject baseline entries that no longer match the measured tree."""
    source_paths = _source_paths(root, paths)
    counts = {path: line_count(root / path) for path in source_paths}
    partials = _partial_totals(root, source_paths)
    phase_paths = _phase_prefixes(source_paths)
    errors: list[str] = []

    for path, ceiling in sorted(baseline["files"].items()):
        count = counts.get(path)
        if count is None:
            errors.append(f"BASELINE stale_file={path} reason=missing")
        elif count <= FILE_LIMIT:
            errors.append(f"BASELINE stale_file={path} reason=below-limit lines={count}")
        elif int(ceiling) > count:
            errors.append(f"BASELINE stale_file={path} ceiling={ceiling} measured={count}")

    for key, ceiling in sorted(baseline["partial_types"].items()):
        count = partials.get(key)
        if count is None:
            errors.append(f"BASELINE stale_partial_type={key} reason=missing")
        elif count <= PARTIAL_LIMIT:
            errors.append(f"BASELINE stale_partial_type={key} reason=below-limit lines={count}")
        elif int(ceiling) > count:
            errors.append(f"BASELINE stale_partial_type={key} ceiling={ceiling} measured={count}")

    for path in sorted(set(baseline["phase_paths"]) - phase_paths):
        errors.append(f"BASELINE stale_phase_path={path}")
    return errors


def violations(root: Path, paths: Iterable[str], baseline: dict[str, object]) -> list[str]:
    """Return all guard violations for a tracked source tree."""
    source_paths = _source_paths(root, paths)
    file_baseline = baseline["files"]
    partial_baseline = baseline["partial_types"]
    phase_baseline = set(baseline["phase_paths"])
    errors: list[str] = []
    for path in source_paths:
        count = line_count(root / path)
        ceiling = file_baseline.get(path, FILE_LIMIT)
        if count > ceiling:
            errors.append(f"G1 file={path} lines={count} limit={ceiling}")
    partials = _partial_totals(root, source_paths)
    for key, count in sorted(partials.items()):
        ceiling = partial_baseline.get(key, PARTIAL_LIMIT)
        if count > ceiling:
            errors.append(f"G2 partial_type={key} lines={count} limit={ceiling}")
    for prefix in sorted(_phase_prefixes(source_paths) - phase_baseline):
        errors.append(f"G3 phase_path={prefix}")
    return errors


def update_baseline(root: Path, paths: Iterable[str], baseline: dict[str, object]) -> dict[str, object]:
    """Lower existing ceilings and remove obsolete entries without adding entries."""
    source_paths = _source_paths(root, paths)
    counts = {path: line_count(root / path) for path in source_paths}
    partials = _partial_totals(root, source_paths)
    files = {
        path: min(int(limit), counts[path])
        for path, limit in baseline["files"].items()
        if path in counts and counts[path] > FILE_LIMIT
    }
    partial_types = {
        key: min(int(limit), partials[key])
        for key, limit in baseline["partial_types"].items()
        if key in partials and partials[key] > PARTIAL_LIMIT
    }
    phase_paths = sorted(set(baseline["phase_paths"]) & _phase_prefixes(source_paths))
    return {"files": files, "partial_types": partial_types, "phase_paths": phase_paths}


def write_baseline(root: Path, paths: Iterable[str], path: Path = BASELINE_PATH) -> None:
    """Write an initial measured baseline or lower an existing baseline."""
    if path.exists():
        baseline = update_baseline(root, paths, load_baseline(path))
    else:
        baseline = measured_tree(root, paths)
    path.write_text(json.dumps(baseline, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def main(argv: list[str] | None = None) -> int:
    """Run the structure guards or update their baseline."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--write-baseline", action="store_true")
    parser.add_argument("--base-ref", default=None)
    args = parser.parse_args(argv)
    paths = tracked_paths(REPOSITORY_ROOT)
    if args.write_baseline:
        write_baseline(REPOSITORY_ROOT, paths)
        print("STRUCTURE_GUARDS_BASELINE_WRITTEN")
        return 0
    baseline = load_baseline()
    base_baseline = load_baseline_at_revision(REPOSITORY_ROOT, args.base_ref) if args.base_ref else None
    base_phase_paths = None
    if args.base_ref:
        base_paths = tracked_paths_at_revision(REPOSITORY_ROOT, args.base_ref)
        base_phase_paths = _phase_prefixes(_source_paths(REPOSITORY_ROOT, base_paths))
    errors = stale_baseline_violations(REPOSITORY_ROOT, paths, baseline)
    errors.extend(
        baseline_integrity_violations(
            baseline,
            base_baseline,
            base_phase_paths,
        )
    )
    errors.extend(violations(REPOSITORY_ROOT, paths, baseline))
    if errors:
        print("\n".join(errors))
        return 1
    print("STRUCTURE_GUARDS_OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
