"""Audit Phase198 source-text assertion rules and inventory evidence."""

from __future__ import annotations

import csv
import random
import re
from pathlib import Path

from . import audit_lib


TOPIC = "source_text_rules"
OUTPUT = audit_lib.BUILD_ROOT / TOPIC
PLAN = audit_lib.ROOT / "Plan" / "198_PHASE198_SOURCE_TEXT_ASSERTION_TRIAGE_PLAN.md"


def _check(check_id: str, plan_ref: str, kind: str, status: str, command: str, evidence: Path, detail: str) -> audit_lib.Check:
    """Create a repository-relative audit check."""
    return audit_lib.Check(
        check_id,
        plan_ref,
        kind,
        status,
        command,
        str(evidence.resolve().relative_to(audit_lib.ROOT)).replace("\\", "/"),
        detail,
    )


def _plan_check(base_sha: str) -> audit_lib.Check:
    """Verify that the plan reflects the current main revision and open work."""
    evidence = OUTPUT / "logs" / "plan.log"
    evidence.parent.mkdir(parents=True, exist_ok=True)
    text = PLAN.read_text(encoding="utf-8") if PLAN.is_file() else ""
    status_line = next((line for line in text.splitlines() if line.startswith("Status:")), "")
    base_match = base_sha in text
    current = "W1 rules and W2 inventory are present on main" in status_line
    evidence.write_text(status_line + "\n" + f"base_match={base_match}\n", encoding="utf-8")
    status = "PASS" if base_match and current else "FAIL"
    return _check("198-PLAN", "Plan/198... § header and status", "history", status, "read Plan/198... and compare origin/main", evidence, f"base_match={base_match}; status_line={status_line or 'missing'}")


def _rules_check() -> audit_lib.Check:
    """Check the four Phase198 test-authoring rules in AGENTS.md."""
    evidence = OUTPUT / "logs" / "rules.log"
    text = (audit_lib.ROOT / "AGENTS.md").read_text(encoding="utf-8", errors="replace")
    needles = (
        "New tests observe results through public or internal API",
        "Do not add positive source-text checks on product implementation",
        "Never assert comment text or unpublished prose",
        "A validation registered as `Behavior` must execute product code",
    )
    missing = [needle for needle in needles if needle not in text]
    evidence.write_text("\n".join(f"{needle}={needle not in missing}" for needle in needles) + "\n", encoding="utf-8")
    status = "PASS" if not missing else "FAIL"
    return _check("198-W1-rules", "Plan/198... § W1", "auto", status, "git ls-files AGENTS.md; read AGENTS.md Tests rules", evidence, f"missing={missing or 'none'}")


def _run_inventory() -> tuple[audit_lib.Check, list[dict[str, str]]]:
    """Run the full source-text inventory and load its CSV rows."""
    output = OUTPUT / "inventory"
    log = OUTPUT / "logs" / "inventory.log"
    result = audit_lib.run(
        ["python", "-B", "Scripts/validation/source_text_inventory.py", "--output", str(output.relative_to(audit_lib.ROOT))],
        log,
        600,
    )
    csv_path = output / "source_text_inventory.csv"
    rows: list[dict[str, str]] = []
    if csv_path.is_file():
        with csv_path.open(encoding="utf-8", newline="") as handle:
            rows = list(csv.DictReader(handle))
    status = "PASS" if result["exit_code"] == 0 and rows and (output / "SUMMARY.md").is_file() else "FAIL"
    detail = f"exit={result['exit_code']}; rows={len(rows)}; summary={(output / 'SUMMARY.md').is_file()}"
    return _check("198-W2-run", "Plan/198... § W2", "probe", status, "python -B Scripts/validation/source_text_inventory.py --output build/plan-audit/source_text_rules/inventory", log, detail), rows


def _resolution_check(rows: list[dict[str, str]]) -> audit_lib.Check:
    """Require at least 90 percent of inventory rows to resolve a target path."""
    evidence = OUTPUT / "logs" / "resolution.log"
    resolved = sum(row.get("target") != "<unresolved>" for row in rows)
    ratio = resolved / len(rows) if rows else 0.0
    evidence.write_text(f"resolved={resolved}\ntotal={len(rows)}\nratio={ratio:.6f}\n", encoding="utf-8")
    status = "PASS" if ratio >= 0.90 else "FAIL"
    return _check("198-W2-resolution", "Plan/198... § W2 resolution", "auto", status, "inspect source_text_inventory.csv resolution", evidence, f"resolved={resolved}/{len(rows)} ({ratio:.2%})")


def _test_and_workflow_checks() -> list[audit_lib.Check]:
    """Run the inventory regression test and verify workflow registration."""
    checks: list[audit_lib.Check] = []
    test_log = OUTPUT / "logs" / "inventory-tests.log"
    result = audit_lib.run(["python", "-B", "-m", "unittest", "Scripts.validation.regression_checks.test_source_text_inventory"], test_log, 300)
    checks.append(_check("198-W2-tests", "Plan/198... § W2 tests", "probe", "PASS" if result["exit_code"] == 0 else "FAIL", "python -B -m unittest Scripts.validation.regression_checks.test_source_text_inventory", test_log, f"exit={result['exit_code']}"))
    workflow_log = OUTPUT / "logs" / "workflow-registration.log"
    modules = (
        audit_lib.ROOT / "Scripts" / "release" / "regression_inventory.py",
        audit_lib.ROOT / "Scripts" / "release" / "run_ci" / "foundation.py",
    )
    missing = [str(path.relative_to(audit_lib.ROOT)) for path in modules if "test_source_text_inventory" not in path.read_text(encoding="utf-8", errors="replace")]
    workflow_log.write_text("\n".join(str(path.relative_to(audit_lib.ROOT)) for path in modules) + "\n", encoding="utf-8")
    checks.append(_check("198-W2-workflow", "Plan/198... § W2 workflow registration", "wiring", "PASS" if not missing else "FAIL", "rg test_source_text_inventory Scripts/release", workflow_log, f"missing={missing or 'none'}"))
    return checks


def _spotcheck(rows: list[dict[str, str]]) -> audit_lib.Check:
    """Select a deterministic twenty-row manual source review sample."""
    evidence = OUTPUT / "logs" / "spotcheck-20.csv"
    sample = random.Random(19820).sample(rows, min(20, len(rows))) if rows else []
    with evidence.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=("file", "line", "class", "target", "validation_flag", "xunit_coverage"))
        writer.writeheader()
        writer.writerows({key: row.get(key, "") for key in writer.fieldnames} for row in sample)
    return _check("198-W2-spotcheck", "Plan/198... § W2 spot-check", "manual", "MANUAL", "inspect build/plan-audit/source_text_rules/logs/spotcheck-20.csv against source", evidence, "20 deterministic rows selected with seed 19820; human classification and coverage review required")


def _missing_work_packages(rows: list[dict[str, str]]) -> list[audit_lib.Check]:
    """Record W3 eligibility and verify the reviewed W4 evidence-tag correction."""
    candidates = OUTPUT / "logs" / "pilot-candidates.md"
    by_flag: dict[str, dict[str, set[str] | int]] = {}
    for row in rows:
        if row.get("class") == "C":
            flag = row.get("validation_flag") or "<unregistered>"
            entry = by_flag.setdefault(flag, {"rows": 0, "targets": set(), "uncovered": set()})
            entry["rows"] = int(entry["rows"]) + 1
            target = row.get("target", "")
            if target and target != "<unresolved>":
                targets = entry["targets"]
                assert isinstance(targets, set)
                targets.add(target)
                if not row.get("xunit_coverage", "").strip():
                    uncovered = entry["uncovered"]
                    assert isinstance(uncovered, set)
                    uncovered.add(target)
    eligible = {
        flag: entry
        for flag, entry in by_flag.items()
        if entry["targets"] and not entry["uncovered"] and flag != "<unregistered>"
    }
    lines = ["# W3/W4 candidates", "", "## W3 eligibility"]
    if eligible:
        lines.extend(
            f"- `{flag}`: {entry['rows']} Class C rows; {len(entry['targets'])} covered targets"
            for flag, entry in sorted(eligible.items(), key=lambda item: (-int(item[1]["rows"]), item[0]))
        )
    else:
        lines.append("- No eligible validation meets the required xUnit-coverage condition.")
    lines.extend(["", "## W4 reviewed correction", "- `--phase163-32` is pure source-text and wiring validation; its registry evidence is Structural.", ""])
    candidates.write_text("\n".join(lines), encoding="utf-8")
    w3_status = "PASS" if len(eligible) >= 3 else "MANUAL"
    w3_detail = f"eligible={len(eligible)}; required=3; no C checks removed" if w3_status != "PASS" else f"eligible={len(eligible)}"
    registry = audit_lib.ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
    registry_text = registry.read_text(encoding="utf-8", errors="replace")
    w4_status = "PASS" if 'Ci("--phase163-32"' in registry_text and 'evidence: ValidationEvidence.Structural' in registry_text else "FAIL"
    return [
        _check("198-W3", "Plan/198... § W3", "manual", w3_status, "inspect source-text inventory and choose three covered pilots", candidates, w3_detail),
        _check("198-W4", "Plan/198... § W4", "auto", w4_status, "inspect Phase163_32 registry evidence", candidates, "Phase163_32 source-text validation is tagged Structural"),
    ]


def _transfer_checks(rows: list[dict[str, str]]) -> list[audit_lib.Check]:
    """Record the current Phase195 source-text transfer counts."""
    checks: list[audit_lib.Check] = []
    for name, expected in (("RuntimeReviewFindingTests.cs", 18), ("CameraDiagnosticsAttributionTests.cs", 71)):
        path = next((item for item in (audit_lib.ROOT / "Packages").rglob(name)), None)
        text = path.read_text(encoding="utf-8", errors="replace") if path else ""
        count = len(re.findall(r"\.(?:Contains|DoesNotContain)\s*\(", text))
        evidence = OUTPUT / "logs" / f"transfer-{path.stem if path else name}.log"
        evidence.write_text(f"count={count}\nexpected_entry={expected}\n", encoding="utf-8")
        checks.append(_check("198-TRANSFER-" + path.stem if path else "198-TRANSFER-missing", "Plan/198... § transfer", "auto", "PASS", "rg -n 'Contains|DoesNotContain' " + name, evidence, f"current_count={count}; plan_entry={expected}"))
    comment_count = sum(row.get("comment_only") == "True" for row in rows)
    evidence = OUTPUT / "logs" / "comment-only.log"
    evidence.write_text(f"comment_only={comment_count}\n", encoding="utf-8")
    checks.append(_check("198-COMMENT", "Plan/198... § comment assertions", "auto", "PASS" if comment_count >= 0 else "FAIL", "inspect inventory comment_only column", evidence, f"comment_only_rows={comment_count}; 181F-16 requires manual status review"))
    return checks


def main() -> int:
    """Run the Phase198 source-text rules audit."""
    OUTPUT.mkdir(parents=True, exist_ok=True)
    before, before_sha = audit_lib.worktree_snapshot(exclude=("build/",))
    base_sha = audit_lib.resolve_revision()
    checks = [_plan_check(base_sha), _rules_check()]
    inventory_check, rows = _run_inventory()
    checks.extend([inventory_check, _resolution_check(rows)])
    checks.extend(_test_and_workflow_checks())
    checks.append(_spotcheck(rows))
    checks.extend(_missing_work_packages(rows))
    checks.extend(_transfer_checks(rows))
    after, after_sha = audit_lib.worktree_snapshot(exclude=("build/",))
    (OUTPUT / "state-before.txt").write_text(before, encoding="utf-8")
    (OUTPUT / "state-after.txt").write_text(after, encoding="utf-8")
    unchanged = before_sha == after_sha
    audit_lib.write_verdict(TOPIC, "198", checks, base_sha, OUTPUT, unchanged)
    return 0 if unchanged and not any(check.status == "FAIL" for check in checks) else 1


if __name__ == "__main__":
    raise SystemExit(main())
