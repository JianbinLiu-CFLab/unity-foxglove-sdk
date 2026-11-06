"""Run the Phase195 Unity acceptance audit and write reproducible evidence."""

from __future__ import annotations

import subprocess
from pathlib import Path

from . import audit_lib
from . import bridge_history
from . import unity_coverage
from . import unity_lane

TOPIC = "unity_acceptance"
OUTPUT = audit_lib.BUILD_ROOT / TOPIC
PLAN = audit_lib.ROOT / "Plan" / "195_PHASE195_WORKSTREAM_E_UNITY_ACCEPTANCE_HARDENING_PLAN.md"


def _plan_check(base_sha: str) -> audit_lib.Check:
    """Compare the plan's recorded entry base with live origin/main."""
    text = PLAN.read_text(encoding="utf-8")
    entry = next((line for line in text.splitlines() if line.startswith("Entry base:")), "")
    status = "PASS" if base_sha in entry else "FAIL"
    return audit_lib.Check(
        "195-PLAN",
        "Plan/195_PHASE195_WORKSTREAM_E_UNITY_ACCEPTANCE_HARDENING_PLAN.md",
        "history",
        status,
        "git rev-parse origin/main; read Plan/195_PHASE195_WORKSTREAM_E_UNITY_ACCEPTANCE_HARDENING_PLAN.md",
        "build/plan-audit/unity_acceptance/base.txt",
        f"live origin/main={base_sha}; {entry or 'Entry base line missing'}",
    )


def _dod_check(checks: list[audit_lib.Check]) -> audit_lib.Check:
    """Map the plan definition of done to the produced check statuses."""
    by_id = {check.id: check for check in checks}
    required = ("195-W0-local100", "195-W1-assets", "195-W1-script", "195-W1-workflow", "195-W2", "195-W3", "195-W4")
    missing = [check_id for check_id in required if check_id not in by_id]
    failed = [check_id for check_id in required if check_id in by_id and by_id[check_id].status in {"FAIL", "MISSING"}]
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
    base_sha = subprocess.check_output(["git", "rev-parse", "origin/main"], cwd=audit_lib.ROOT, text=True).strip()
    (OUTPUT / "base.txt").write_text(base_sha + "\n", encoding="utf-8")
    checks = [_plan_check(base_sha)]
    checks.extend(bridge_history.bridge_checks(OUTPUT))
    checks.extend(unity_lane.lane_checks(OUTPUT))
    checks.extend(unity_coverage.coverage_checks(OUTPUT))
    checks.append(_dod_check(checks))
    after, after_sha = audit_lib.worktree_snapshot(exclude=("build/",))
    (OUTPUT / "state-after.txt").write_text(after, encoding="utf-8")
    unchanged = before_sha == after_sha
    audit_lib.write_verdict(TOPIC, "195", checks, base_sha, OUTPUT, unchanged)
    _write_manual()
    return 0 if unchanged and all(check.status not in {"FAIL"} for check in checks) else 1


if __name__ == "__main__":
    raise SystemExit(main())
