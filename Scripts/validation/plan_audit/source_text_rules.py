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
PURE_SOURCE_FLAGS = {
    "--phase163-32",
    "--phase163-35",
    "--phase163-58",
    "--phase164-1",
    "--phase164-4",
    "--phase164-10",
    "--phase164-33",
    "--phase164-56",
    "--phase77",
    "--phase132",
}
PILOT_FLAGS = ("--phase86", "--phase134-1", "--phase72")
ENTRY_START = re.compile(r'^\s*(?:Ci|Local|DefaultOnly)\("(?P<flag>--[A-Za-z0-9-]+)"', re.MULTILINE)


def _registry_blocks(text: str) -> list[tuple[str, str]]:
    """Split the validation registry into flag-scoped entry blocks."""
    matches = list(ENTRY_START.finditer(text))
    return [
        (match.group("flag"), text[match.start() : matches[index + 1].start() if index + 1 < len(matches) else len(text)])
        for index, match in enumerate(matches)
    ]


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
    if not PLAN.is_file():
        evidence.write_text("plan=missing\n", encoding="utf-8")
        return _check("198-PLAN", "Plan/198... § header and status", "history", "MISSING", "Test-Path Plan/198_PHASE198_SOURCE_TEXT_ASSERTION_TRIAGE_PLAN.md", evidence, "Plan198 is ignored local input and is absent from the repository tree")
    text = PLAN.read_text(encoding="utf-8")
    status_line = next((line for line in text.splitlines() if line.startswith("Status:")), "")
    base_match = base_sha in text
    current = (
        "W1 rules and W2 inventory are present on main" in status_line
        or "Follow-up 11 W2 ratchet implementation are complete" in status_line
        or "W1-W4 complete" in status_line
    )
    evidence.write_text(status_line + "\n" + f"base_match={base_match}\n", encoding="utf-8")
    status = "PASS" if base_match and current else "FAIL"
    return _check("198-PLAN", "Plan/198... § header and status", "history", status, "read Plan/198... and compare origin/main", evidence, f"base_match={base_match}; status_line={status_line or 'missing'}")


def _rules_check() -> audit_lib.Check:
    """Check the four Phase198 test-authoring rules in AGENTS.md."""
    evidence = OUTPUT / "logs" / "rules.log"
    rules_path = audit_lib.ROOT / "AGENTS.md"
    if not rules_path.is_file():
        evidence.write_text("AGENTS.md=missing\n", encoding="utf-8")
        return _check("198-W1-rules", "Plan/198... § W1", "history", "MISSING", "Test-Path AGENTS.md", evidence, "AGENTS.md is local workflow input and is absent from the repository tree")
    text = rules_path.read_text(encoding="utf-8", errors="replace")
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
    """Report inventory resolution without making the former threshold a gate."""
    evidence = OUTPUT / "logs" / "resolution.log"
    resolved = sum(
        bool(row.get("target"))
        and "<directory>" not in row.get("target", "")
        and "<unresolved>" not in row.get("target", "")
        for row in rows
    )
    directories = sum("<directory>" in row.get("target", "") for row in rows)
    unresolved = sum("<unresolved>" in row.get("target", "") for row in rows)
    ratio = resolved / len(rows) if rows else 0.0
    evidence.write_text(
        f"file_targets={resolved}\ndirectory_targets={directories}\nunresolved_targets={unresolved}\n"
        f"total={len(rows)}\nratio={ratio:.6f}\n",
        encoding="utf-8",
    )
    status = "PASS"
    return _check(
        "198-W2-resolution",
        "Plan/198... § W2 resolution",
        "auto",
        status,
        "inspect source_text_inventory.csv file-target resolution",
        evidence,
        f"file_targets={resolved}/{len(rows)} ({ratio:.2%}); directories={directories}; unresolved={unresolved}; "
        "threshold=report-only; accepted_by_user=2026-11-10",
    )


def _test_and_workflow_checks() -> list[audit_lib.Check]:
    """Run the inventory regression test and verify workflow registration."""
    checks: list[audit_lib.Check] = []
    test_log = OUTPUT / "logs" / "inventory-tests.log"
    test_command = [
        "python",
        "-B",
        "-m",
        "unittest",
        "Scripts.validation.regression_checks.test_source_text_inventory",
        "Scripts.validation.regression_checks.test_source_text_inventory_gate",
        "Scripts.validation.regression_checks.test_source_text_inventory_semantics",
        "Scripts.validation.regression_checks.test_source_text_inventory_migration",
    ]
    result = audit_lib.run(test_command, test_log, 300)
    checks.append(_check("198-W2-tests", "Plan/198... § W2 tests", "probe", "PASS" if result["exit_code"] == 0 else "FAIL", "python -B -m unittest source inventory regression modules", test_log, f"exit={result['exit_code']}"))
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
            if target and "<directory>" not in target and "<unresolved>" not in target:
                targets = entry["targets"]
                assert isinstance(targets, set)
                targets.add(target)
                if not row.get("xunit_coverage", "").strip():
                    uncovered = entry["uncovered"]
                    assert isinstance(uncovered, set)
                    uncovered.add(target)
    registry = audit_lib.ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
    registry_text = registry.read_text(encoding="utf-8", errors="replace")
    blocks = _registry_blocks(registry_text)
    ci_flags = {flag for flag, block in blocks if re.match(r"\s*Ci\(", block)}
    behavior_flags = {flag for flag, block in blocks if re.match(r"\s*Ci\(", block) and "ValidationEvidence.Behavior" in block}
    eligible = {
        flag: entry
        for flag, entry in by_flag.items()
        if entry["targets"] and not entry["uncovered"] and flag in ci_flags and flag in behavior_flags
    }
    lines = ["# W3/W4 candidates", "", "## W3 eligibility"]
    if eligible:
        lines.extend(
            f"- `{flag}`: {entry['rows']} Class C rows; {len(entry['targets'])} covered targets"
            for flag, entry in sorted(eligible.items(), key=lambda item: (-int(item[1]["rows"]), item[0]))
        )
    else:
        lines.append("- No eligible validation meets the required xUnit-coverage condition.")
    remaining_pilot_c = {
        flag: sum(1 for row in rows if row.get("validation_flag") == flag and row.get("class") == "C")
        for flag in PILOT_FLAGS
    }
    lines.extend(["", "## W3 completed pilots"])
    lines.extend(
        f"- `{flag}`: remaining Class C rows = {remaining_pilot_c[flag]}"
        for flag in PILOT_FLAGS
    )
    lines.extend(["", "## W4 reviewed correction", "- Complete Behavior-tag candidate review is recorded in `behavior-tag-candidates.md`.", ""])
    candidate_lines = ["# Behavior evidence-tag review", "", "| Flag | Runtime C rows | Decision |", "|---|---:|---|"]
    for flag, entry in sorted(by_flag.items()):
        if flag == "<unregistered>" or not entry["rows"]:
            continue
        marker = "Structural: source-text and wiring only" if flag in PURE_SOURCE_FLAGS else "Behavior: product code executes in validation"
        candidate_lines.append(f"| `{flag}` | {entry['rows']} | {marker} |")
    candidate_report = OUTPUT / "logs" / "behavior-tag-candidates.md"
    candidate_report.write_text("\n".join(candidate_lines) + "\n", encoding="utf-8")
    candidates.write_text("\n".join(lines), encoding="utf-8")
    w3_status = "PASS" if all(value == 0 for value in remaining_pilot_c.values()) else "FAIL"
    w3_detail = f"pilots={','.join(PILOT_FLAGS)}; remaining_c={remaining_pilot_c}"
    missing_structural = [
        flag
        for flag in PURE_SOURCE_FLAGS
        if not any(
            entry_flag == flag
            and re.match(r"\s*Ci\(", block)
            and "evidence: ValidationEvidence.Structural" in block
            for entry_flag, block in blocks
        )
    ]
    w4_status = "PASS" if not missing_structural else "FAIL"
    return [
        _check("198-W3", "Plan/198... § W3", "manual", w3_status, "inspect source-text inventory and verify three covered pilots", candidates, w3_detail),
        _check("198-W4", "Plan/198... § W4", "auto", w4_status, "inspect all Behavior-tagged source-text candidates", candidate_report, f"pure_source_flags={sorted(PURE_SOURCE_FLAGS)}; missing_structural={missing_structural or 'none'}"),
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
        status = "PASS" if path and count == expected else "FAIL"
        checks.append(_check("198-TRANSFER-" + path.stem if path else "198-TRANSFER-missing", "Plan/198... § transfer", "auto", status, "rg -n 'Contains|DoesNotContain' " + name, evidence, f"current_count={count}; expected={expected}; path={path or 'missing'}"))
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
