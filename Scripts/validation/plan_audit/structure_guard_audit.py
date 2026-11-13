"""Audit the Phase196 structural size and naming guards."""

from __future__ import annotations

import importlib.util
import json
import re
import subprocess
import tempfile
from pathlib import Path

from . import audit_lib

TOPIC = "structure_guard_audit"
OUTPUT = audit_lib.BUILD_ROOT / TOPIC
BASELINE_REL = "Scripts/architecture/structure_guard_baseline.json"
GUARD_REL = "Scripts/architecture/structure_guards.py"
PLAN = audit_lib.ROOT / "Plan" / "196_PHASE196_STRUCTURAL_SIZE_AND_NAMING_GUARDS_PLAN.md"
PLAN194 = audit_lib.ROOT / "Plan" / "194_PHASE194_ENGINEERING_HEALTH_AND_VERIFICATION_DEBT_PLAN.md"
PR716_BASE = "98e665a03d688aa26aac09650ae614214486d7c9"
PR716_MERGE = "bf1131db208c308529846f87c1d8c3c744b8c7f9"


def _check(identifier: str, plan_ref: str, kind: str, status: str, command: str, evidence: str, detail: str) -> audit_lib.Check:
    """Create one audit check record."""
    return audit_lib.Check(identifier, plan_ref, kind, status, command, evidence, detail)


def _log_name(identifier: str) -> Path:
    """Return the evidence log path for one check."""
    return OUTPUT / "logs" / f"{identifier}.log"


def _run(command: list[str], identifier: str, cwd: Path = audit_lib.ROOT, timeout: float = 120.0) -> dict[str, object]:
    """Run a command and store its evidence under the audit output."""
    return audit_lib.run(command, _log_name(identifier), timeout, cwd)


def _load_guard_module(root: Path = audit_lib.ROOT):
    """Load a structural guard implementation without changing its globals."""
    path = root / GUARD_REL
    spec = importlib.util.spec_from_file_location("phase196_structure_guards", path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"unable to load {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _tracked_add(root: Path) -> None:
    """Track all exported files in an isolated temporary repository."""
    subprocess.run(["git", "add", "-A"], cwd=root, check=True, capture_output=True)


def _temporary_tree(revision: str, name: str) -> Path:
    """Export a revision into the audit build directory and track its files."""
    target_parent = OUTPUT / "probe"
    target_parent.mkdir(parents=True, exist_ok=True)
    target = Path(tempfile.mkdtemp(prefix=f"{name}-", dir=target_parent))
    target.rmdir()
    tree = audit_lib.temp_git_tree(revision, target)
    listed = subprocess.run(
        ["git", "-C", str(tree), "ls-files"], capture_output=True, text=True, check=True
    ).stdout.splitlines()
    for relative in listed:
        path = tree / relative
        if path.is_file() and Path(relative).suffix not in {".cs", ".py", ".json"}:
            path.unlink()
    _tracked_add(tree)
    return tree


def _guard_result(root: Path, identifier: str) -> dict[str, object]:
    """Run the structure guard module in an isolated tree."""
    return _run(["python", "-B", "-m", "Scripts.architecture.structure_guards"], identifier, root, 180.0)


def _plan_check(base_sha: str) -> audit_lib.Check:
    """Check that the live Plan196 status and entry base describe current main."""
    text = PLAN.read_text(encoding="utf-8") if PLAN.is_file() else ""
    status_line = next((line for line in text.splitlines() if line.startswith("Status:")), "")
    entry_line = next((line for line in text.splitlines() if line.startswith("Entry base:")), "")
    status_ok = any(token in status_line.lower() for token in ("audit pending", "implemented", "completed"))
    base_ok = base_sha in entry_line
    status = "PASS" if status_ok and base_ok else "FAIL"
    detail = f"{entry_line}; {status_line}; live base={base_sha}"
    return _check("196-PLAN", "Plan/196... § header", "history", status, "git rev-parse origin/main; read Plan/196...", "build/plan-audit/structure_guard_audit/plan.log", detail)


def _guard_main() -> audit_lib.Check:
    """Verify the structural guard passes on the audited tree."""
    result = _run(["python", "-B", "-m", "Scripts.architecture.structure_guards"], "196-GUARD-main")
    status = "PASS" if result["exit_code"] == 0 and "STRUCTURE_GUARDS_OK" in str(result["output"]) else "FAIL"
    return _check("196-GUARD-main", "Plan/196... § 6 DoD", "auto", status, "python -B -m Scripts.architecture.structure_guards", result["log"], str(result["output"]).strip())


def _ci_placement() -> audit_lib.Check:
    """Verify the guard step is placed before dependency and .NET work."""
    workflow = audit_lib.ROOT / ".github" / "workflows" / "dotnet-tests.yml"
    text = workflow.read_text(encoding="utf-8")
    job_start = text.find("  linux-python-regressions:")
    next_job_match = re.search(r"\n  [A-Za-z0-9_-]+:\n", text[job_start + 3 :])
    next_job = job_start + 3 + next_job_match.start() if next_job_match else -1
    section = text[job_start: next_job if next_job >= 0 else len(text)]
    setup = section.find("actions/setup-python")
    guard = section.find("Scripts.architecture.structure_guards")
    install = section.find("Install pinned Python test dependencies")
    dotnet = min((index for index in (section.find("setup-dotnet"), section.find("dotnet ")) if index >= 0), default=-1)
    placement = job_start >= 0 and 0 <= setup < guard < install < dotnet
    required = _run(
        ["gh", "api", "repos/JianbinLiu-CFLab/unity-foxglove-sdk/branches/main/protection/required_status_checks"],
        "196-required-status",
        timeout=120.0,
    )
    required_payload = {}
    try:
        required_payload = json.loads(str(required["output"]))
    except json.JSONDecodeError:
        required_payload = {}
    contexts = required_payload.get("contexts", [])
    checks = required_payload.get("checks", [])
    required_names = {
        str(context) if isinstance(context, str) else str(context.get("context", ""))
        for context in contexts
    }
    required_names.update(
        str(check) if isinstance(check, str) else str(check.get("context", check.get("name", "")))
        for check in checks
    )
    expected = "Linux: Python regressions and package closure"
    job_required = expected in required_names
    status = "PASS" if placement and required["exit_code"] == 0 and job_required else "NOT_RUN" if placement and required["exit_code"] != 0 else "FAIL"
    detail = f"placement={placement}; required_api_exit={required['exit_code']}; required_match={job_required}; contexts={sorted(required_names)}"
    return _check("196-CI-placement", "Plan/196... § 3 item 4", "auto", status, "read .github/workflows/dotnet-tests.yml; gh api .../branches/main/protection/required_status_checks", "build/plan-audit/structure_guard_audit/ci-placement.log", detail)


def _red_runs(base_sha: str) -> list[audit_lib.Check]:
    """Run isolated G1, G2, and G3 mutations against an exported base tree."""
    guard = _load_guard_module()
    checks: list[audit_lib.Check] = []
    g1 = _temporary_tree(base_sha, "g1")
    g1_path = g1 / "Scripts" / "validation" / "plan_audit" / "g1_probe.py"
    g1_path.parent.mkdir(parents=True, exist_ok=True)
    g1_path.write_text("x\n" * 1001, encoding="utf-8")
    _tracked_add(g1)
    result = _guard_result(g1, "196-RED-G1")
    okay = result["exit_code"] != 0 and "g1_probe.py" in str(result["output"])
    checks.append(_check("196-RED-G1", "Plan/196... § 5 G1 red run", "mutation", "PASS" if okay else "FAIL", "python -B -m Scripts.architecture.structure_guards", result["log"], str(result["output"]).strip()))

    g2 = _temporary_tree(base_sha, "g2")
    baseline = guard.load_baseline(g2 / BASELINE_REL)
    paths = guard.tracked_paths(g2)
    partials = guard._partial_totals(g2, guard._source_paths(g2, paths))
    key, ceiling = next(iter(baseline["partial_types"].items()))
    target = next(
        g2 / path
        for path in guard._source_paths(g2, paths)
        if key in {
            guard._partial_key(path, guard._namespace((g2 / path).read_text(encoding="utf-8", errors="replace")), name)
            for name in guard._partial_names((g2 / path).read_text(encoding="utf-8", errors="replace"))
        }
    )
    target.write_bytes(target.read_bytes() + b"\n" * (int(ceiling) - partials[key] + 1))
    _tracked_add(g2)
    result = _guard_result(g2, "196-RED-G2")
    okay = result["exit_code"] != 0 and key in str(result["output"])
    checks.append(_check("196-RED-G2", "Plan/196... § 5 G2 red run", "mutation", "PASS" if okay else "FAIL", "python -B -m Scripts.architecture.structure_guards", result["log"], str(result["output"]).strip()))

    g3 = _temporary_tree(base_sha, "g3")
    g3_path = g3 / "Scripts" / "phase197_tool.py"
    g3_path.parent.mkdir(parents=True, exist_ok=True)
    g3_path.write_text("print('probe')\n", encoding="utf-8")
    _tracked_add(g3)
    result = _guard_result(g3, "196-RED-G3")
    okay = result["exit_code"] != 0 and "Scripts/phase197_tool.py" in str(result["output"])
    checks.append(_check("196-RED-G3", "Plan/196... § 5 G3 red run", "mutation", "PASS" if okay else "FAIL", "python -B -m Scripts.architecture.structure_guards", result["log"], str(result["output"]).strip()))
    return checks


def _baseline_exact() -> audit_lib.Check:
    """Compare the measured PR716 base tree with the merged baseline."""
    tree = _temporary_tree(PR716_BASE, "baseline-exact")
    historical_tree = _temporary_tree(PR716_MERGE, "baseline-guard")
    baseline_path = OUTPUT / "probe" / "baseline-pr716.json"
    baseline_path.write_bytes((historical_tree / BASELINE_REL).read_bytes())
    guard = _load_guard_module(historical_tree)
    measured = guard.measured_tree(tree, guard.tracked_paths(tree))
    expected = guard.load_baseline(baseline_path)
    status = "PASS" if measured == expected else "FAIL"
    detail = f"measured_matches_merged_baseline={measured == expected}; measured_keys={{files:{len(measured['files'])}, partial_types:{len(measured['partial_types'])}, phase_paths:{len(measured['phase_paths'])}}}"
    (_log_name("196-BASELINE-exact")).write_text(json.dumps({"measured": measured, "expected": expected}, indent=2) + "\n", encoding="utf-8")
    return _check("196-BASELINE-exact", "Plan/196... § 6 DoD", "auto", status, "measure PR716 base with the PR716 merge guard and baseline", "build/plan-audit/structure_guard_audit/logs/196-BASELINE-exact.log", detail)


def _baseline_history(base_sha: str) -> audit_lib.Check:
    """Check for baseline ceilings raised or entries added after PR716."""
    result = _run(["git", "log", "--format=%H %s", "--reverse", f"{PR716_MERGE}..{base_sha}", "--", BASELINE_REL], "196-BASELINE-history")
    changed = [line for line in str(result["output"]).splitlines() if line.strip()]
    status = "PASS" if result["exit_code"] == 0 and not changed else "FAIL"
    return _check("196-BASELINE-history", "Plan/196... § 6 baseline policy", "history", status, "git log -p origin/main -- Scripts/architecture/structure_guard_baseline.json", result["log"], f"post-PR716 baseline commits={changed or 'none'}")


def _baseline_slack(base_sha: str) -> audit_lib.Check:
    """Verify baseline additions, growth, and stale entries are rejected."""
    tree = _temporary_tree(base_sha, "baseline-slack")
    guard = _load_guard_module()
    baseline_path = tree / BASELINE_REL
    before_data = guard.load_baseline(baseline_path)
    paths = guard.tracked_paths(tree)
    base_phase_paths = guard._phase_prefixes(guard._source_paths(tree, paths))
    base_cli_data = json.loads(json.dumps(before_data))
    base_cli_data["files"]["Scripts/architecture/stale_baseline.py"] = 1001
    baseline_path.write_text(json.dumps(base_cli_data, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    _tracked_add(tree)
    base_write_result = _run(
        ["python", "-B", "-m", "Scripts.architecture.structure_guards", "--write-baseline"],
        "196-BASELINE-slack-base-cli",
        tree,
    )
    base_after_data = guard.load_baseline(baseline_path)
    base_guard_result = _run(
        ["python", "-B", "-m", "Scripts.architecture.structure_guards"],
        "196-BASELINE-slack-base-guard",
        tree,
    )
    base_cli_ok = (
        base_write_result["exit_code"] == 0
        and "STRUCTURE_GUARDS_BASELINE_WRITTEN" in str(base_write_result["output"])
        and "Scripts/architecture/stale_baseline.py" not in base_after_data["files"]
        and base_guard_result["exit_code"] == 0
        and "STRUCTURE_GUARDS_OK" in str(base_guard_result["output"])
    )
    head_tree = _temporary_tree("HEAD", "baseline-slack-head")
    head_guard = _load_guard_module(head_tree)
    head_baseline_path = head_tree / BASELINE_REL
    head_before_data = head_guard.load_baseline(head_baseline_path)
    head_cli_data = json.loads(json.dumps(head_before_data))
    head_cli_data["files"]["Scripts/architecture/stale_baseline.py"] = 1001
    head_baseline_path.write_text(json.dumps(head_cli_data, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    _tracked_add(head_tree)
    head_write_result = _run(
        ["python", "-B", "-m", "Scripts.architecture.structure_guards", "--write-baseline"],
        "196-BASELINE-slack-head-cli",
        head_tree,
    )
    head_after_data = head_guard.load_baseline(head_baseline_path)
    head_guard_result = _run(
        ["python", "-B", "-m", "Scripts.architecture.structure_guards"],
        "196-BASELINE-slack-head-guard",
        head_tree,
    )
    head_cli_ok = (
        head_write_result["exit_code"] == 0
        and "STRUCTURE_GUARDS_BASELINE_WRITTEN" in str(head_write_result["output"])
        and "Scripts/architecture/stale_baseline.py" not in head_after_data["files"]
        and head_guard_result["exit_code"] == 0
        and "STRUCTURE_GUARDS_OK" in str(head_guard_result["output"])
    )
    added = (
        set(base_after_data["files"]) - set(before_data["files"])
        | set(base_after_data["partial_types"]) - set(before_data["partial_types"])
        | set(base_after_data["phase_paths"]) - set(before_data["phase_paths"])
    )
    raised = any(
        base_after_data[section].get(key, 0) > value
        for section in ("files", "partial_types")
        for key, value in before_data[section].items()
    )
    widened = json.loads(json.dumps(before_data))
    file_key, file_limit = next(iter(widened["files"].items()))
    widened["files"][file_key] = int(file_limit) + 500
    widened["files"]["Scripts/architecture/nonexistent.py"] = 1001
    widened["phase_paths"].append("Scripts/phase999_not_real")
    integrity = guard.baseline_integrity_violations(widened, before_data, base_phase_paths)
    stale = guard.stale_baseline_violations(tree, paths, widened)
    valid = (
        base_cli_ok
        and head_cli_ok
        and not added
        and not raised
        and any("raised=" in error for error in integrity)
        and any("added=Scripts/architecture/nonexistent.py" in error for error in integrity)
        and any("added=Scripts/phase999_not_real" in error for error in integrity)
        and any("stale_file=" in error for error in stale)
    )
    status = "PASS" if valid else "FAIL"
    detail = (
        f"base_cli_ok={base_cli_ok}; head_cli_ok={head_cli_ok}; "
        f"base_write_output={str(base_write_result['output']).strip()}; "
        f"head_write_output={str(head_write_result['output']).strip()}; "
        f"added={sorted(added)}; raised={raised}; "
        f"integrity_errors={integrity}; stale_errors={stale}"
    )
    log = _log_name("196-BASELINE-slack")
    log.parent.mkdir(parents=True, exist_ok=True)
    log.write_text(detail + "\n", encoding="utf-8")
    return _check("196-BASELINE-slack", "Plan/196... § 2 baseline update policy", "probe", status, "base and head structure_guards --write-baseline plus baseline_integrity_violations + stale_baseline_violations", str(log), detail)


def _test_mutations() -> list[audit_lib.Check]:
    """Run regression tests and ensure weakened guard constants are detected."""
    checks: list[audit_lib.Check] = []
    result = _run(["python", "-B", "-m", "unittest", "Scripts.architecture.regression_checks.test_structure_guards"], "196-TESTS")
    checks.append(_check("196-TESTS", "Plan/196... § 3 tests", "auto", "PASS" if result["exit_code"] == 0 else "FAIL", "python -B -m unittest Scripts.architecture.regression_checks.test_structure_guards", result["log"], str(result["output"]).strip()))
    guard_path = audit_lib.ROOT / GUARD_REL
    for identifier, old, new, token in (
        ("196-TESTS-G3-mutation", 'r"(?i)phase\\d+"', 'r"(?i)phase\\d{2,3}"', "phase_path"),
        ("196-TESTS-G1-mutation", "FILE_LIMIT = 1000", "FILE_LIMIT = 1001", "1001"),
    ):
        try:
            with audit_lib.mutate(guard_path, old, new):
                mutated = _run(["python", "-B", "-m", "unittest", "Scripts.architecture.regression_checks.test_structure_guards"], identifier)
            okay = mutated["exit_code"] != 0 and token in str(mutated["output"])
            status = "PASS" if okay else "FAIL"
            detail = str(mutated["output"]).strip()
        except Exception as exc:
            status = "FAIL"
            detail = str(exc)
        checks.append(_check(identifier, "Plan/196... § 5 mutation", "mutation", status, "python -B -m unittest Scripts.architecture.regression_checks.test_structure_guards", f"build/plan-audit/structure_guard_audit/logs/{identifier}.log", detail))
    return checks


def _plan194_check() -> audit_lib.Check:
    """Verify the Phase194 status markers for W5.4, W6.1, and W6.2b."""
    text = PLAN194.read_text(encoding="utf-8") if PLAN194.is_file() else ""
    w54 = bool(re.search(r"W5\.4[^\n]*dropped", text, re.IGNORECASE))
    w61 = bool(re.search(r"W6\.1[^\n]*(?:moved to Phase196|implemented in Phase196)", text, re.IGNORECASE))
    w62 = bool(re.search(r"W6\.2b[^\n]*dropped", text, re.IGNORECASE))
    status = "PASS" if w54 and w61 and w62 else "FAIL"
    return _check("196-DOD-4", "Plan/196... § 6 DoD item 4", "history", status, "read Plan/194_PHASE194_ENGINEERING_HEALTH_AND_VERIFICATION_DEBT_PLAN.md", "build/plan-audit/structure_guard_audit/plan194.log", f"W5.4={w54}; W6.1={w61}; W6.2b={w62}")


def _agents_check() -> audit_lib.Check:
    """Verify the local ceiling and baseline approval rule."""
    path = audit_lib.ROOT / "AGENTS.md"
    if not path.is_file():
        return _check("196-AGENTS", "Plan/196... § 2 baseline update policy", "history", "MISSING", "read AGENTS.md", "build/plan-audit/structure_guard_audit/agents.log", "ignored AGENTS.md is missing")
    text = path.read_text(encoding="utf-8")
    found = "do not raise or add structure-guard baseline ceilings or entries without explicit user approval" in text.lower()
    return _check("196-AGENTS", "Plan/196... § 2 baseline update policy", "auto", "PASS" if found else "FAIL", "read AGENTS.md", "build/plan-audit/structure_guard_audit/agents.log", f"approval wording found={found}")


def main() -> int:
    """Run the complete Phase196 audit and write verdict artifacts."""
    OUTPUT.mkdir(parents=True, exist_ok=True)
    before, before_sha = audit_lib.worktree_snapshot(exclude=("build/",))
    (OUTPUT / "state-before.txt").write_text(before, encoding="utf-8")
    base_sha = audit_lib.resolve_revision()
    checks = [_plan_check(base_sha), _guard_main(), _ci_placement()]
    checks.extend(_red_runs(base_sha))
    checks.extend([_baseline_exact(), _baseline_history(base_sha), _baseline_slack(base_sha)])
    checks.extend(_test_mutations())
    checks.extend([_plan194_check(), _agents_check()])
    after, after_sha = audit_lib.worktree_snapshot(exclude=("build/",))
    (OUTPUT / "state-after.txt").write_text(after, encoding="utf-8")
    unchanged = before_sha == after_sha
    audit_lib.write_verdict(TOPIC, "196", checks, base_sha, OUTPUT, unchanged)
    return 0 if unchanged and all(check.status == "PASS" for check in checks) else 1


if __name__ == "__main__":
    raise SystemExit(main())
