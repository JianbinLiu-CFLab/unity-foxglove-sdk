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


def _load_guard_module():
    """Load the current structural guard implementation without changing its globals."""
    path = audit_lib.ROOT / GUARD_REL
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
    status_ok = "audit pending" in status_line.lower() or "implemented" in status_line.lower()
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
    required_result = subprocess.run(
        ["gh", "api", "repos/JianbinLiu-CFLab/unity-foxglove-sdk/branches/main/protection/required_status_checks"],
        capture_output=True,
        text=True,
        check=False,
    )
    required_text = required_result.stdout + required_result.stderr
    job_required = "test" in required_text or "linux-python-regressions" in required_text
    status = "PASS" if placement and required_result.returncode == 0 and job_required else "NOT_RUN" if placement and required_result.returncode != 0 else "FAIL"
    detail = f"placement={placement}; required_api_exit={required_result.returncode}; required_match={job_required}"
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
    baseline_path = OUTPUT / "probe" / "baseline-pr716.json"
    baseline_path.write_text(subprocess.run(["git", "show", f"{PR716_MERGE}:{BASELINE_REL}"], cwd=audit_lib.ROOT, capture_output=True, text=True, check=True).stdout, encoding="utf-8")
    guard = _load_guard_module()
    measured = guard.measured_tree(tree, guard.tracked_paths(tree))
    expected = json.loads(baseline_path.read_text(encoding="utf-8"))
    status = "PASS" if measured == expected else "FAIL"
    detail = f"measured_matches_merged_baseline={measured == expected}; measured_keys={{files:{len(measured['files'])}, partial_types:{len(measured['partial_types'])}, phase_paths:{len(measured['phase_paths'])}}}"
    (_log_name("196-BASELINE-exact")).write_text(json.dumps({"measured": measured, "expected": expected}, indent=2) + "\n", encoding="utf-8")
    return _check("196-BASELINE-exact", "Plan/196... § 6 DoD", "auto", status, "measure PR716 base with structure_guards.measured_tree", "build/plan-audit/structure_guard_audit/logs/196-BASELINE-exact.log", detail)


def _baseline_history() -> audit_lib.Check:
    """Check for baseline ceilings raised or entries added after PR716."""
    result = _run(["git", "log", "--format=%H %s", "--reverse", f"{PR716_MERGE}..origin/main", "--", BASELINE_REL], "196-BASELINE-history")
    changed = [line for line in str(result["output"]).splitlines() if line.strip()]
    status = "PASS" if result["exit_code"] == 0 and not changed else "FAIL"
    return _check("196-BASELINE-history", "Plan/196... § 6 baseline policy", "history", status, "git log -p origin/main -- Scripts/architecture/structure_guard_baseline.json", result["log"], f"post-PR716 baseline commits={changed or 'none'}")


def _baseline_slack(base_sha: str) -> audit_lib.Check:
    """Verify write-baseline only lowers or removes entries and leaves the guard valid."""
    tree = _temporary_tree(base_sha, "baseline-slack")
    before = (tree / BASELINE_REL).read_bytes()
    result = _run(["python", "-B", "-m", "Scripts.architecture.structure_guards", "--write-baseline"], "196-BASELINE-slack", tree)
    after = (tree / BASELINE_REL).read_bytes()
    before_data = json.loads(before)
    after_data = json.loads(after)
    added = set(after_data["files"]) - set(before_data["files"]) | set(after_data["partial_types"]) - set(before_data["partial_types"]) | set(after_data["phase_paths"]) - set(before_data["phase_paths"])
    raised = any(after_data[section].get(key, 0) > value for section in ("files", "partial_types") for key, value in before_data[section].items())
    valid = not added and not raised and result["exit_code"] == 0
    status = "PASS" if valid else "FAIL"
    detail = f"write_exit={result['exit_code']}; added={sorted(added)}; raised={raised}; changed={before != after}"
    return _check("196-BASELINE-slack", "Plan/196... § 2 baseline update policy", "probe", status, "python -B -m Scripts.architecture.structure_guards --write-baseline", result["log"], detail)


def _test_mutations() -> list[audit_lib.Check]:
    """Run regression tests and ensure weakened guard constants are detected."""
    checks: list[audit_lib.Check] = []
    result = _run(["python", "-B", "-m", "unittest", "Scripts.architecture.regression_checks.test_structure_guards"], "196-TESTS")
    checks.append(_check("196-TESTS", "Plan/196... § 3 tests", "auto", "PASS" if result["exit_code"] == 0 else "FAIL", "python -B -m unittest Scripts.architecture.regression_checks.test_structure_guards", result["log"], str(result["output"]).strip()))
    guard_path = audit_lib.ROOT / GUARD_REL
    for identifier, old, new, token in (
        ("196-TESTS-G3-mutation", 'r"(?:[Pp]hase\\d{2,3}|_phase\\d{2,3}_)"', 'r"(?:[Pp]hase\\d{2,3}|_phase\\d{2,3}_|.*)"', "phase_path"),
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
    text = (audit_lib.ROOT / "AGENTS.md").read_text(encoding="utf-8")
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
    checks.extend([_baseline_exact(), _baseline_history(), _baseline_slack(base_sha)])
    checks.extend(_test_mutations())
    checks.extend([_plan194_check(), _agents_check()])
    after, after_sha = audit_lib.worktree_snapshot(exclude=("build/",))
    (OUTPUT / "state-after.txt").write_text(after, encoding="utf-8")
    unchanged = before_sha == after_sha
    audit_lib.write_verdict(TOPIC, "196", checks, base_sha, OUTPUT, unchanged)
    return 0 if unchanged and all(check.status == "PASS" for check in checks) else 1


if __name__ == "__main__":
    raise SystemExit(main())
