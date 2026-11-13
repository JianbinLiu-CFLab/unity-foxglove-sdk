"""Run the Phase195 Unity acceptance audit and write reproducible evidence."""

from __future__ import annotations

import hashlib
import re
import subprocess
from pathlib import Path

from . import audit_lib
from . import bridge_history
from . import unity_coverage
from . import unity_lane

TOPIC = "unity_acceptance"
OUTPUT = audit_lib.BUILD_ROOT / TOPIC
PLAN = audit_lib.ROOT / "Plan" / "195_PHASE195_WORKSTREAM_E_UNITY_ACCEPTANCE_HARDENING_PLAN.md"
TRUSTED_AUDIT_MODULES = (
    "Scripts/validation/plan_audit/audit_lib.py",
    "Scripts/validation/plan_audit/bridge_history.py",
    "Scripts/validation/plan_audit/unity_acceptance.py",
    "Scripts/validation/plan_audit/unity_coverage.py",
    "Scripts/validation/plan_audit/unity_lane.py",
)


def _trusted_source_check(base_sha: str) -> audit_lib.Check:
    """Refuse to execute contributor-modified audit modules in a maintainer run."""
    mismatches: list[str] = []
    for relative in TRUSTED_AUDIT_MODULES:
        current = audit_lib.ROOT / relative
        result = subprocess.run(
            ["git", "show", f"{base_sha}:{relative}"],
            cwd=audit_lib.ROOT,
            capture_output=True,
            check=False,
        )
        if result.returncode != 0 or not current.is_file():
            mismatches.append(relative)
            continue
        expected = hashlib.sha256(result.stdout).hexdigest()
        actual = hashlib.sha256(current.read_bytes()).hexdigest()
        if expected != actual:
            mismatches.append(relative)
    status = "PASS" if not mismatches else "FAIL"
    detail = f"base={base_sha}; mismatched_modules={mismatches or 'none'}"
    return audit_lib.Check(
        "195-AUDIT-trust",
        "Plan/195... § trusted audit source",
        "security",
        status,
        "git show <base>:Scripts/validation/plan_audit/*.py; sha256 compare",
        "build/plan-audit/unity_acceptance/trusted-source.log",
        detail,
    )


def _plan_check(base_sha: str) -> audit_lib.Check:
    """Compare the plan status line with the delivered W1 state."""
    if not PLAN.is_file():
        return audit_lib.Check(
            "195-PLAN",
            "Plan/195_PHASE195_WORKSTREAM_E_UNITY_ACCEPTANCE_HARDENING_PLAN.md",
            "history",
            "MISSING",
            "Test-Path Plan/195_PHASE195_WORKSTREAM_E_UNITY_ACCEPTANCE_HARDENING_PLAN.md",
            "build/plan-audit/unity_acceptance/base.txt",
            "required Phase195 plan file is not present in the repository",
        )
    text = PLAN.read_text(encoding="utf-8")
    status_line = next((line for line in text.splitlines() if "Status:" in line), "")
    status_value = status_line.split("Status:", 1)[1].strip().lower() if "Status:" in status_line else ""
    stale = "plan only" in status_value
    negated = bool(re.search(r"\bnot\s+(?:implemented|complete|completed|delivered|accepted|verified)\b", status_value))
    recognized = bool(re.search(r"\b(?:implemented|complete|completed|delivered|accepted|verified)\b", status_value))
    status = "PASS" if recognized and not stale and not negated else "FAIL"
    detail = status_line if recognized and not stale and not negated else f"{status_line or 'Status line missing'}; status is missing, negated, or unrecognized"
    return audit_lib.Check(
        "195-PLAN",
        "Plan/195_PHASE195_WORKSTREAM_E_UNITY_ACCEPTANCE_HARDENING_PLAN.md",
        "history",
        status,
        "git rev-parse origin/main/main/HEAD; read Plan/195_PHASE195_WORKSTREAM_E_UNITY_ACCEPTANCE_HARDENING_PLAN.md",
        "build/plan-audit/unity_acceptance/base.txt",
        f"live base={base_sha}; {detail}",
    )


def _dod_check(checks: list[audit_lib.Check]) -> audit_lib.Check:
    """Map the plan definition of done to the produced check statuses."""
    by_id = {check.id: check for check in checks}
    required = (
        "195-PLAN",
        "195-W0-local100",
        "195-W1-assets",
        "195-W1-script",
        "195-W1-workflow",
        "195-W1-dispatch",
        "195-W1-zero-tests",
        "195-W1-red",
        "195-OBS-jazzy-meta",
        "195-W2",
        "195-W2-mutation",
        "195-W3",
        "195-W3-mutation",
        "195-W4",
    )
    missing = [check_id for check_id in required if check_id not in by_id]
    failed = [check_id for check_id in required if check_id in by_id and by_id[check_id].status != "PASS"]
    status = "PASS" if not missing and not failed else "FAIL"
    detail = f"missing checks={missing or 'none'}; unmet checks={failed or 'none'}"
    return audit_lib.Check(
        "195-DOD",
        "Plan/195_PHASE195_WORKSTREAM_E_UNITY_ACCEPTANCE_HARDENING_PLAN.md § 5",
        "auto",
        status,
        "python -B -m Scripts.validation.plan_audit.unity_acceptance",
        "build/plan-audit/unity_acceptance/verdict.json",
        detail,
    )


def _write_manual() -> None:
    """Write detailed manual steps for Unity and workflow-only checks."""
    target = OUTPUT / "manual.md"
    target.write_text(unity_coverage.manual_steps([]).rstrip() + "\n", encoding="utf-8")


def main() -> int:
    """Run the complete Phase195 audit without changing product files."""
    OUTPUT.mkdir(parents=True, exist_ok=True)
    before, before_sha = audit_lib.worktree_snapshot(exclude=("build/",))
    (OUTPUT / "state-before.txt").write_text(before, encoding="utf-8")
    base_sha = audit_lib.resolve_revision()
    (OUTPUT / "base.txt").write_text(base_sha + "\n", encoding="utf-8")
    checks = [_plan_check(base_sha)]
    trusted = _trusted_source_check(base_sha)
    (OUTPUT / "trusted-source.log").write_text(trusted.detail + "\n", encoding="utf-8")
    checks.append(trusted)
    if trusted.status == "PASS":
        checks.extend(bridge_history.bridge_checks(OUTPUT))
        checks.extend(unity_lane.lane_checks(OUTPUT))
        checks.extend(unity_coverage.coverage_checks(OUTPUT))
    checks.append(_dod_check(checks))
    after, after_sha = audit_lib.worktree_snapshot(exclude=("build/",))
    (OUTPUT / "state-after.txt").write_text(after, encoding="utf-8")
    unchanged = before_sha == after_sha
    audit_lib.write_verdict(TOPIC, "195", checks, base_sha, OUTPUT, unchanged)
    _write_manual()
    return 0 if unchanged and all(check.status == "PASS" for check in checks) else 1


if __name__ == "__main__":
    raise SystemExit(main())
