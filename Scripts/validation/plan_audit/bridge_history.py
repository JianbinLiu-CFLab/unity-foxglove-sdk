"""Audit the historical and local acceptance evidence for the bridge duplex test."""

from __future__ import annotations

import contextlib
import hashlib
import json
import os
import re
import subprocess
import time
import xml.etree.ElementTree as ElementTree
from pathlib import Path
from typing import Sequence

from . import audit_lib


PROJECT = audit_lib.ROOT / "Packages" / "dev.unity2foxglove.ros2bridge" / "Tests" / "Unit" / "Unity2Foxglove.Ros2Bridge.UnitTests.csproj"
TEST_NAME = "GeneratedSubscribersShareOnePhysicalLeaseAndApplyOnPumpThread"
FULL_TEST_NAME = f"Unity2Foxglove.Ros2Bridge.Tests.Ros2BridgeConnectionTests.{TEST_NAME}"
FILTER = f"FullyQualifiedName={FULL_TEST_NAME}"
PROJECT_DISPLAY = "Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Unity2Foxglove.Ros2Bridge.UnitTests.csproj"


def _evidence(path: Path) -> str:
    """Return a repository-relative evidence path."""
    return path.resolve().relative_to(audit_lib.ROOT).as_posix()


def _check(status: str, command: str, evidence: Path, detail: str, kind: str = "auto") -> audit_lib.Check:
    """Create the W0 check record."""
    return audit_lib.Check(
        "195-W0-local100" if "local100" in evidence.name else "195-W0-history",
        "Plan/195_PHASE195_WORKSTREAM_E_UNITY_ACCEPTANCE_HARDENING_PLAN.md § W0",
        kind,
        status,
        command,
        _evidence(evidence),
        detail,
    )


def _gh_environment() -> dict[str, str]:
    """Redirect GitHub CLI state and all temporary files into build."""
    root = audit_lib.ROOT / "build" / "tmp" / "gh"
    root.mkdir(parents=True, exist_ok=True)
    return {"GH_CACHE_DIR": str(root / "cache")}


@contextlib.contextmanager
def _dotnet_environment():
    """Redirect dotnet user state and NuGet packages into build."""
    values = {
        "DOTNET_CLI_HOME": audit_lib.ROOT / "build" / "tmp" / "dotnet-home",
        "NUGET_PACKAGES": audit_lib.ROOT / "build" / "tmp" / "nuget",
    }
    saved = {key: os.environ.get(key) for key in values}
    for key, value in values.items():
        value.mkdir(parents=True, exist_ok=True)
        os.environ[key] = str(value)
    try:
        yield
    finally:
        for key, value in saved.items():
            if value is None:
                os.environ.pop(key, None)
            else:
                os.environ[key] = value


def _history(output: Path) -> audit_lib.Check:
    """Search failed main-branch runs for the historical hello failure."""
    logs = output / "logs"
    list_log = logs / "w0-history-list.log"
    saved = {key: os.environ.get(key) for key in _gh_environment()}
    os.environ.update(_gh_environment())
    try:
        result = audit_lib.run(
            [
                "gh",
                "run",
                "list",
                "--workflow",
                "dotnet-tests.yml",
                "--branch",
                "main",
                "--limit",
                "50",
                "--json",
                "databaseId,conclusion,headSha,createdAt",
            ],
            list_log,
            120,
        )
        if result["exit_code"] != 0:
            return _check("NOT_RUN", "gh run list --workflow dotnet-tests.yml --branch main --limit 50 --json databaseId,conclusion,headSha,createdAt", list_log, "gh run list failed or authentication was unavailable", "history")
        try:
            runs = json.loads(str(result["output"]))
        except json.JSONDecodeError:
            return _check("NOT_RUN", "gh run list --workflow dotnet-tests.yml --branch main --limit 50 --json databaseId,conclusion,headSha,createdAt", list_log, "gh returned invalid JSON", "history")
        failures = [run for run in runs if run.get("conclusion") == "failure"]
        matches: list[str] = []
        retrieval_failures: list[str] = []
        for run in failures:
            raw_run_id = run.get("databaseId")
            run_id = str(raw_run_id) if raw_run_id else ""
            if not run_id:
                retrieval_failures.append("missing-run-id")
                continue
            run_log = logs / f"w0-history-{run_id}.log"
            detail = audit_lib.run(["gh", "run", "view", run_id, "--log-failed"], run_log, 180)
            if detail["exit_code"] != 0:
                retrieval_failures.append(run_id)
                continue
            text = str(detail["output"]).lower()
            if TEST_NAME.lower() in text or "did not complete hello" in text:
                matches.append(run_id)
        status = "NOT_RUN" if retrieval_failures else ("PASS" if not matches else "FAIL")
        detail = f"failed runs={len(failures)}; matching hello failures={matches or 'none'}; log retrieval failures={retrieval_failures or 'none'}"
        return _check(status, "gh run list --workflow dotnet-tests.yml --branch main --limit 50 --json databaseId,conclusion,headSha,createdAt", list_log, detail, "history")
    finally:
        for key, value in saved.items():
            if value is None:
                os.environ.pop(key, None)
            else:
                os.environ[key] = value


def _linux_sdk() -> tuple[bool, str]:
    """Return whether WSL has a usable SDK for this project's target framework."""
    try:
        result = subprocess.run(
            ["wsl.exe", "--exec", "dotnet", "--list-sdks"],
            cwd=audit_lib.ROOT,
            env=audit_lib._audit_environment(),
            capture_output=True,
            text=True,
            timeout=30,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        return False, "WSL is unavailable"
    versions = result.stdout + result.stderr
    usable = result.returncode == 0 and bool(re.search(r"(?:^|\n)10\.\d+\.\d+\s", versions))
    if usable:
        return True, "WSL Linux SDK 10.x is available"
    return False, f"WSL has no usable net10 SDK ({versions.strip() or 'no SDK output'})"


def _runner(use_wsl: bool) -> tuple[list[str], str]:
    """Return the command prefix and display label for the selected runtime."""
    if not use_wsl:
        return ["dotnet"], "Windows dotnet"
    root = "/mnt/" + str(audit_lib.ROOT)[0].lower() + str(audit_lib.ROOT)[2:].replace("\\", "/")
    tmp = root + "/build/tmp"
    return ["wsl.exe", "--cd", root, "--exec", "env", f"TEMP={tmp}", f"TMP={tmp}", f"DOTNET_CLI_HOME={tmp}/dotnet-home", f"NUGET_PACKAGES={tmp}/nuget", "dotnet"], "WSL Linux dotnet"


def _wsl_path(path: Path) -> str:
    """Convert a Windows repository path to the mounted WSL path."""
    absolute = str(path.resolve())
    return "/mnt/" + absolute[0].lower() + absolute[2:].replace("\\", "/")


def _one_result(trx: Path) -> tuple[bool, str]:
    """Validate that a TRX records exactly one selected test result."""
    if not trx.is_file():
        return False, "TRX file was not produced"
    try:
        root = ElementTree.parse(trx).getroot()
    except ElementTree.ParseError as exc:
        return False, f"invalid TRX: {exc}"
    results = [node for node in root.iter() if node.tag.endswith("UnitTestResult")]
    matching = [node for node in results if node.attrib.get("testName", "") == FULL_TEST_NAME]
    outcomes = [node.attrib.get("outcome", "").lower() for node in matching]
    if len(results) != 1 or len(matching) != 1:
        return False, f"TRX result count={len(results)}, matching={len(matching)}"
    if outcomes != ["passed"]:
        return False, f"selected test outcome={outcomes[0] if outcomes else 'missing'}"
    return True, "one matching UnitTestResult with outcome Passed"


def _discovery_has_exact_test(output: object) -> bool:
    """Return whether test discovery lists the exact fully qualified target."""
    return any(line.strip() == FULL_TEST_NAME for line in str(output).splitlines())


def _local100_impl(output: Path) -> audit_lib.Check:
    """Restore, build once, prove discovery, and execute one test one hundred times."""
    logs = output / "logs"
    trusted, trust_detail = _trusted_project_revision(output)
    if not trusted:
        evidence = logs / "w0-local100-trust.log"
        evidence.parent.mkdir(parents=True, exist_ok=True)
        evidence.write_text(trust_detail + "\n", encoding="utf-8")
        return _check("NOT_RUN", "git diff --quiet <trusted-base>; git status --porcelain", evidence, trust_detail, "security")
    base_sha, source_tree_sha = _cache_identity(output)
    use_wsl, runtime_detail = _linux_sdk()
    prefix, runtime = _runner(use_wsl)
    restore_cmd = prefix + ["restore", PROJECT_DISPLAY]
    build_cmd = prefix + ["build", "--no-restore", PROJECT_DISPLAY]
    restore = audit_lib.run(restore_cmd, logs / "w0-local100-restore.log", 900)
    if restore["exit_code"] != 0:
        return _check("NOT_RUN", " ".join(restore_cmd), logs / "w0-local100-restore.log", f"{runtime_detail}; restore failed", "auto")
    build = audit_lib.run(build_cmd, logs / "w0-local100-build.log", 900)
    if build["exit_code"] != 0:
        return _check("NOT_RUN", " ".join(build_cmd), logs / "w0-local100-build.log", f"{runtime_detail}; build failed", "auto")
    discovery_cmd = prefix + ["test", "--no-build", "--no-restore", PROJECT_DISPLAY, "--list-tests"]
    discovery = audit_lib.run(discovery_cmd, logs / "w0-local100-discovery.log", 180)
    if discovery["exit_code"] != 0 or not _discovery_has_exact_test(discovery["output"]):
        return _check("FAIL", " ".join(discovery_cmd), logs / "w0-local100-discovery.log", f"{runtime_detail}; target test was not discovered", "auto")
    outcomes: list[bool] = []
    details: list[str] = []
    durations: list[float] = []
    for index in range(1, 101):
        result_dir = output / "trx" / f"{index:03d}"
        trx_name = f"bridge-{index:03d}.trx"
        results_argument = _wsl_path(result_dir) if use_wsl else str(result_dir)
        command = prefix + ["test", "--no-build", "--no-restore", PROJECT_DISPLAY, "--filter", FILTER, "--logger", f"trx;LogFileName={trx_name}", "--results-directory", results_argument]
        started = time.monotonic()
        result = audit_lib.run(command, logs / f"w0-local100-{index:03d}.log", 180)
        durations.append(round(time.monotonic() - started, 3))
        trx = result_dir / trx_name
        passed, detail = _one_result(trx)
        ok = result["exit_code"] == 0 and passed
        outcomes.append(ok)
        details.append(f"{index:03d}: exit={result['exit_code']} {detail}")
    summary = output / "logs" / "w0-local100-summary.json"
    summary.write_text(json.dumps({"base_sha": base_sha, "source_tree_sha": source_tree_sha, "runtime": runtime, "passes": sum(outcomes), "runs": len(outcomes), "durations": durations, "details": details}, indent=2) + "\n", encoding="utf-8")
    return _check("PASS" if all(outcomes) else "FAIL", " ".join(prefix + ["test", "--no-build", "--no-restore", PROJECT_DISPLAY, "--filter", FILTER]), summary, f"{sum(outcomes)}/100 runs passed; {runtime_detail}; discovery and per-run TRX each recorded exactly one selected test", "auto")


def _local100(output: Path) -> audit_lib.Check:
    """Run the local acceptance loop with isolated dotnet caches."""
    with _dotnet_environment():
        return _local100_impl(output)


def _cache_identity(output: Path) -> tuple[str, str]:
    """Return the audited base SHA and bridge source tree SHA for cache validation."""
    marker = output / "base.txt"
    base_sha = marker.read_text(encoding="utf-8").strip() if marker.is_file() else audit_lib.resolve_revision()
    source_tree_sha = _worktree_tree_sha(audit_lib.ROOT / "Packages" / "dev.unity2foxglove.ros2bridge")
    return base_sha, source_tree_sha


def _worktree_tree_sha(root: Path) -> str:
    """Hash every file in the audited bridge worktree, including dirty changes."""
    digest = hashlib.sha256()
    for path in sorted(path for path in root.rglob("*") if path.is_file()):
        relative = path.relative_to(root).as_posix().encode("utf-8")
        content = path.read_bytes()
        digest.update(len(relative).to_bytes(8, "big"))
        digest.update(relative)
        digest.update(len(content).to_bytes(8, "big"))
        digest.update(content)
    return digest.hexdigest()


def _trusted_project_revision(output: Path) -> tuple[bool, str]:
    """Allow maintainer-owned dotnet execution only on the resolved trusted tree."""
    base_sha = _cache_identity(output)[0]
    diff = subprocess.run(
        ["git", "diff", "--quiet", base_sha],
        cwd=audit_lib.ROOT,
        env=audit_lib._audit_environment(),
        capture_output=True,
        check=False,
    )
    status = subprocess.run(
        ["git", "status", "--porcelain=v1", "--untracked-files=all"],
        cwd=audit_lib.ROOT,
        env=audit_lib._audit_environment(),
        capture_output=True,
        text=True,
        check=False,
    )
    dirty = [line for line in status.stdout.splitlines() if not line[3:].replace("\\", "/").startswith("build/")]
    if diff.returncode != 0 or dirty:
        return False, f"trusted checkout differs from {base_sha}; dotnet execution refused"
    return True, f"checkout matches trusted revision {base_sha}"


def bridge_checks(output: Path) -> list[audit_lib.Check]:
    """Return the W0 CI-history and local-100 acceptance checks."""
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    return [_history(output), _local100(output)]


__all__ = ["bridge_checks"]
