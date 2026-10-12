"""Task 0 entry-boundary evidence helpers."""
from __future__ import annotations

import csv
import hashlib
import re
import subprocess
from pathlib import Path
from typing import Callable, Iterable


CI_PASS_MARKER = "All CI checks passed."


def command_snapshot(
    root: Path,
    output: Path,
    name: str,
    argv: list[str],
    *,
    required: bool = True,
    timeout_seconds: int = 30,
) -> int:
    """Record one Task 0 command and its exit status."""
    try:
        completed = subprocess.run(
            argv,
            cwd=root,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=timeout_seconds,
            check=False,
        )
        output_text = completed.stdout
        returncode = completed.returncode
    except subprocess.TimeoutExpired as error:
        output_text = f"UNAVAILABLE: timeout after {timeout_seconds}s\n{error}\n"
        returncode = 124
    except OSError as error:
        output_text = f"UNAVAILABLE: {error}\n"
        returncode = 127
    payload = (
        f"COMMAND={subprocess.list2cmdline(argv)}\n"
        "INPUT=working tree at Task 0 invocation\n"
        "LITERAL_OUTPUT_BEGIN\n"
        f"{output_text}"
        "LITERAL_OUTPUT_END\n"
        f"EXIT_STATUS={returncode}\n"
    )
    (output / f"{name}.log").write_text(payload, encoding="utf-8")
    return returncode if required else 0


def record_entry_commands(root: Path, output: Path) -> None:
    """Record the live repository boundary commands."""
    required_commands = {
        "status": ["git", "status", "-sb", "--untracked-files=all"],
        "refs": ["git", "rev-parse", "HEAD"],
        "worktrees": ["git", "worktree", "list", "--porcelain"],
    }
    failures = [name for name, argv in required_commands.items() if command_snapshot(root, output, name, argv)]
    if failures:
        raise RuntimeError("Task 0 command failed: " + ", ".join(failures))
    command_snapshot(
        root,
        output,
        "remote-refs",
        ["git", "rev-parse", "main", "origin/main"],
        required=False,
    )
    command_snapshot(
        root,
        output,
        "remote-heads",
        ["git", "ls-remote", "--heads", "origin"],
        required=False,
        timeout_seconds=15,
    )


def write_plan_entry(output: Path, rows: Iterable[Iterable[str]]) -> None:
    """Write the human-readable plan hash table."""
    review = output / "../review"
    rows = [list(row) for row in rows]
    lines = [
        "# Phase192 Task 0 plan-entry",
        "",
        "The hashes below are captured from the live checkout at baseline freeze.",
        "",
        "| Path | Exists | SHA-256 |",
        "| --- | --- | --- |",
    ]
    lines.extend(f"| `{path}` | `{exists}` | `{digest}` |" for path, exists, digest in rows)
    (review / "plan-entry.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


def record_plan_hashes(root: Path, output: Path, plans: Iterable[Path]) -> list[str]:
    """Record the hashes of the governing phase plans."""
    rows = []
    missing: list[str] = []
    for plan in plans:
        absolute = plan if plan.is_absolute() else root / plan
        if not absolute.is_file() and plan.name.startswith("191_"):
            matches = sorted((root / "Plan").glob("191_*.md"))
            absolute = matches[0] if len(matches) == 1 else absolute
        relative = absolute.relative_to(root).as_posix() if absolute.is_file() else plan.as_posix()
        digest = hashlib.sha256(absolute.read_bytes()).hexdigest().upper() if absolute.is_file() else ""
        if not absolute.is_file():
            missing.append(relative)
        rows.append([relative, str(absolute.is_file()).lower(), digest])
    review = output / "../review"
    review.mkdir(parents=True, exist_ok=True)
    with (review / "plan-entry.tsv").open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle, delimiter="\t", lineterminator="\n")
        writer.writerow(["path", "exists", "sha256"])
        writer.writerows(rows)
    write_plan_entry(output, rows)
    return missing


def validate_classification(
    output: Path,
    paths: Iterable[str],
    source_extensions: frozenset[str],
    is_generated: Callable[[str], bool],
    is_vendor: Callable[[str], bool],
) -> None:
    """Fail closed when source paths are not classified by a manifest."""
    def manifest_paths(name: str) -> set[str]:
        """Return manifest paths from a Task 0 inventory file."""
        with (output / name).open(encoding="utf-8", newline="") as handle:
            return {row[0] for row in csv.reader(handle, delimiter="\t") if row and row[0] != "path"}

    generated = manifest_paths("generated-manifest.tsv")
    vendor = manifest_paths("vendor-manifest.tsv")
    errors = []
    for relative in paths:
        if Path(relative).suffix.casefold() not in source_extensions:
            continue
        generated_flag = is_generated(relative)
        vendor_flag = is_vendor(relative)
        if generated_flag and relative not in generated or vendor_flag and relative not in vendor or generated_flag and vendor_flag:
            errors.append([relative, str(generated_flag).lower(), str(vendor_flag).lower(), "ambiguous-or-unmanifested"])
    with (output / "classification-errors.tsv").open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle, delimiter="\t", lineterminator="\n")
        writer.writerow(["path", "generated_excluded", "vendor_excluded", "error"])
        writer.writerows(errors)
    if errors:
        raise RuntimeError(f"Task 0 classification failed closed for {len(errors)} source paths")


def measured_lane_result(lane_runs: Path, name: str) -> tuple[str, str]:
    """Return a fail-closed status and measurement for one recorded CI lane."""
    meta = lane_runs / f"{name}.meta"
    if not meta.is_file():
        return "NOT_RUN", "meta=missing"
    metadata = meta.read_text(encoding="utf-8", errors="replace")
    exit_match = re.search(r"(?:^|[|\r\n]\s*)EXIT_STATUS=(-?\d+)", metadata)
    logs = [lane_runs / f"{name}.stdout.log", lane_runs / f"{name}.stderr.log"]
    payload = "\n".join(path.read_text(encoding="utf-8", errors="replace") for path in logs if path.is_file())
    marker = CI_PASS_MARKER in payload
    if exit_match is None:
        pid = re.search(r"(?:^|[|\r\n]\s*)PID=(\d+)", metadata)
        return "RUNNING", f"pid={pid.group(1) if pid else 'unknown'}; exit_status=missing; marker={str(marker).lower()}"
    exit_status = int(exit_match.group(1))
    if exit_status == 0 and marker:
        status = "PASS"
    elif exit_status == 0:
        status = "FAIL"
    else:
        status = "FAIL"
    return status, f"exit_status={exit_status}; marker={str(marker).lower()}"
