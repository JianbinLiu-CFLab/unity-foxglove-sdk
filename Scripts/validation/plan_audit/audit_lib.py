"""Shared utilities for repeatable repository plan audits."""

from __future__ import annotations

import contextlib
import hashlib
import json
import os
import subprocess
import tarfile
import tempfile
import time
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import Iterator, Sequence

ROOT = Path(__file__).resolve().parents[3]
BUILD_ROOT = ROOT / "build" / "plan-audit"
VALID_STATUSES = frozenset({"PASS", "FAIL", "MISSING", "NOT_RUN", "MANUAL"})


def resolve_revision() -> str:
    """Resolve the first available repository revision for audit evidence."""
    for reference in ("origin/main", "main", "HEAD"):
        result = subprocess.run(
            ["git", "rev-parse", "--verify", reference],
            cwd=ROOT,
            env=_audit_environment(),
            capture_output=True,
            text=True,
            check=False,
        )
        revision = result.stdout.strip()
        if result.returncode == 0 and revision:
            return revision
    raise RuntimeError("no usable audit revision found: origin/main, main, and HEAD are unavailable")


@dataclass(frozen=True)
class Check:
    """Represent one audit result."""

    id: str
    plan_ref: str
    kind: str
    status: str
    command: str
    evidence: str
    detail: str


def _audit_environment() -> dict[str, str]:
    """Return the environment with temporary files redirected under build."""
    environment = os.environ.copy()
    temporary = ROOT / "build" / "tmp"
    temporary.mkdir(parents=True, exist_ok=True)
    for name in ("TMPDIR", "TMP", "TEMP"):
        environment[name] = str(temporary)
    environment["PYTHONDONTWRITEBYTECODE"] = "1"
    environment["GH_NO_UPDATE_NOTIFIER"] = "1"
    environment.setdefault("GH_CACHE_DIR", str(temporary / "gh-cache"))
    return environment


def run(
    cmd: Sequence[str],
    log_path: Path,
    timeout: float,
    cwd: Path = ROOT,
) -> dict[str, object]:
    """Run one command, capture its output, exit code, and elapsed time."""
    log_path.parent.mkdir(parents=True, exist_ok=True)
    started = time.monotonic()
    try:
        completed = subprocess.run(
            list(cmd),
            cwd=cwd,
            env=_audit_environment(),
            capture_output=True,
            timeout=timeout,
            check=False,
        )
        output = completed.stdout + completed.stderr
        exit_code = completed.returncode
        timed_out = False
    except subprocess.TimeoutExpired as exc:
        output = (exc.stdout or b"") + (exc.stderr or b"")
        exit_code = None
        timed_out = True
    except OSError as exc:
        output = str(exc).encode()
        exit_code = None
        timed_out = False
    output = output.decode("utf-8", errors="replace")
    elapsed = time.monotonic() - started
    log_path.write_text(output, encoding="utf-8", errors="replace")
    return {
        "command": subprocess.list2cmdline([str(part) for part in cmd]),
        "exit_code": exit_code,
        "elapsed_seconds": round(elapsed, 3),
        "timed_out": timed_out,
        "log": os.path.relpath(log_path, ROOT).replace("\\", "/"),
        "output": output,
    }


@contextlib.contextmanager
def mutate(path: Path, old: str, new: str) -> Iterator[None]:
    """Temporarily replace one exact source occurrence and restore its bytes."""
    original = path.read_bytes()
    needle = old.encode("utf-8")
    if original.count(needle) != 1:
        raise ValueError(f"mutation target occurs {original.count(needle)} times: {path}")
    snapshot = worktree_snapshot(exclude=("build/",))
    digest = hashlib.sha256(original).digest()
    backups = BUILD_ROOT / ".backup"
    backups.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=backups) as temporary:
        backup = Path(temporary) / path.name
        backup.write_bytes(original)
        try:
            path.write_bytes(original.replace(needle, new.encode("utf-8")))
            yield
        finally:
            path.write_bytes(backup.read_bytes())
            if hashlib.sha256(path.read_bytes()).digest() != digest or worktree_snapshot(exclude=("build/",)) != snapshot:
                raise RuntimeError(f"mutation changed the worktree after restoration: {path}")


def worktree_snapshot(exclude: Sequence[str] = ()) -> tuple[str, str]:
    """Return porcelain status text and its SHA-256 digest."""
    result = subprocess.run(
        ["git", "-c", "core.quotepath=false", "status", "--porcelain=v1", "--untracked-files=normal"],
        cwd=ROOT,
        env=_audit_environment(),
        capture_output=True,
        text=True,
        check=True,
    )
    lines = []
    prefixes = tuple(prefix.replace("\\", "/") for prefix in exclude)
    for line in result.stdout.splitlines():
        path = line[3:].replace("\\", "/") if len(line) >= 4 else line
        if not any(path.startswith(prefix) for prefix in prefixes):
            lines.append(line)
    text = "\n".join(lines) + ("\n" if lines else "")
    return text, hashlib.sha256(text.encode("utf-8")).hexdigest()


def temp_git_tree(rev: str, dest: Path, paths: Sequence[str] = ()) -> Path:
    """Export a revision into build and initialize it as an independent git tree."""
    dest = dest.resolve()
    if not dest.is_relative_to((ROOT / "build").resolve()) or dest == (ROOT / "build").resolve():
        raise ValueError("audit tree must be created inside build/")
    dest.mkdir(parents=True, exist_ok=False)
    with tempfile.TemporaryFile(dir=dest.parent) as handle:
        command = ["git", "archive", "--format=tar", rev, *paths]
        subprocess.run(command, cwd=ROOT, stdout=handle, check=True)
        handle.seek(0)
        with tarfile.open(fileobj=handle) as archive:
            archive.extractall(dest, filter="data")
    subprocess.run(["git", "init", "--initial-branch=main"], cwd=dest, check=True, capture_output=True)
    subprocess.run(["git", "config", "core.longpaths", "true"], cwd=dest, check=True)
    return dest


def write_verdict(
    topic: str,
    phase: str,
    checks: Sequence[Check],
    base_sha: str,
    output_dir: Path,
    worktree_unchanged: bool,
) -> None:
    """Write verdict JSON, Markdown report, and manual acceptance notes."""
    invalid = [check.status for check in checks if check.status not in VALID_STATUSES]
    if invalid:
        raise ValueError(f"invalid statuses: {invalid}")
    output_dir.mkdir(parents=True, exist_ok=True)
    payload = {
        "phase": phase,
        "topic": topic,
        "base_sha": base_sha,
        "generated_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "worktree_unchanged": worktree_unchanged,
        "checks": [asdict(check) for check in checks],
    }
    (output_dir / "verdict.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    report_lines = [f"# Phase {phase} {topic} audit", "", f"Base SHA: `{base_sha}`", ""]
    for check in checks:
        report_lines.extend(
            [
                f"## {check.id} [{check.status}]",
                f"- Plan: {check.plan_ref}",
                f"- Kind: {check.kind}",
                f"- Command: `{check.command}`",
                f"- Evidence: `{check.evidence}`",
                f"- Detail: {check.detail}",
                "",
            ]
        )
    (output_dir / "report.md").write_text("\n".join(report_lines), encoding="utf-8")
    manual = [
        f"# Phase {phase} manual acceptance",
        "",
        "Run each MANUAL or NOT_RUN item only when its stated environment is available.",
        "",
    ]
    for check in checks:
        if check.status in {"MANUAL", "NOT_RUN"}:
            manual.extend([f"## {check.id}", f"- Command: `{check.command}`", f"- Reason: {check.detail}", ""])
    (output_dir / "manual.md").write_text("\n".join(manual), encoding="utf-8")
