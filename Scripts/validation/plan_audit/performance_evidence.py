"""Audit Plan199 performance evidence, thresholds, CI pinning, and documentation."""

from __future__ import annotations

import json
import contextlib
import importlib.util
import io
import math
import re
import tempfile
import unittest
from pathlib import Path

from . import audit_lib


TOPIC = "performance_evidence"
OUTPUT = audit_lib.BUILD_ROOT / TOPIC
PLAN = audit_lib.ROOT / "Plan" / "199_PHASE199_PERFORMANCE_EVIDENCE_AND_CI_PLAN.md"
WORKFLOW = audit_lib.ROOT / ".github" / "workflows" / "performance.yml"
THRESHOLDS = audit_lib.ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Performance" / "performance-thresholds.ci.json"
GUIDE = audit_lib.ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Documentation~" / "en" / "07_FoxRun_Zero_Code_Publishing.md"
RESEARCH = audit_lib.ROOT / "docs" / "research-shared-emitter-architecture.md"


def _evidence(path: Path) -> str:
    """Return a repository-relative evidence path."""
    return path.resolve().relative_to(audit_lib.ROOT).as_posix()


def _check(check_id: str, plan_ref: str, kind: str, status: str, command: str, evidence: Path, detail: str) -> audit_lib.Check:
    """Create one audit result."""
    return audit_lib.Check(check_id, plan_ref, kind, status, command, _evidence(evidence), detail)


def _plan_check(base_sha: str) -> audit_lib.Check:
    """Verify the local Plan199 status and current base reference."""
    evidence = OUTPUT / "logs" / "plan.log"
    evidence.parent.mkdir(parents=True, exist_ok=True)
    if not PLAN.is_file():
        evidence.write_text("plan=missing\n", encoding="utf-8")
        return _check("199-PLAN", "Plan/199... § 8 execution notes", "history", "MISSING", "Test-Path Plan/199_PHASE199_PERFORMANCE_EVIDENCE_AND_CI_PLAN.md", evidence, "Plan199 is ignored local input and is unavailable in this checkout")
    text = PLAN.read_text(encoding="utf-8")
    status_line = next((line for line in text.splitlines() if line.startswith("Status:")), "")
    valid_status = "performance evidence implementation complete" in status_line.lower()
    base_match = base_sha in text
    evidence.write_text(f"{status_line}\nbase_match={base_match}\n", encoding="utf-8")
    status = "PASS" if valid_status and base_match else "FAIL"
    return _check("199-PLAN", "Plan/199... § 8 execution notes", "history", status, "read Plan199 status and compare origin/main", evidence, f"status={valid_status}; base_match={base_match}")


def _load_thresholds() -> dict[str, object]:
    """Load the CI threshold configuration."""
    return json.loads(THRESHOLDS.read_text(encoding="utf-8"))


def _threshold_check() -> audit_lib.Check:
    """Require finite default and large-payload allocation/GC ceilings."""
    evidence = OUTPUT / "logs" / "thresholds.log"
    try:
        data = _load_thresholds()
        defaults = data["defaults"]
        large = data["scenarios"]["LargePayloadFanout"]
        quick_large = data["modes"]["quick"]["scenarios"]["LargePayloadFanout"]
        required_scenarios = {
            "FoxRunStreamIngest",
            "FoxRunPublishPayload",
            "WsSendQueueLargeFrame",
            "LargePayloadFanout",
            "FoxRunStreamIngest.Default",
            "FoxRunStreamIngest.Unbounded",
            "FoxRunPublishPayload.Json.1",
            "FoxRunPublishPayload.Json.4",
            "FoxRunPublishPayload.Protobuf.1",
            "FoxRunPublishPayload.Protobuf.4",
            "FoxRunPublishPayload.MessagePack.1",
            "FoxRunPublishPayload.MessagePack.4",
            "LargePayloadFanout.WebSocket.1",
            "LargePayloadFanout.WebSocket.4",
            "LargePayloadFanout.Mcap",
        }
        explicit_modes = all(
            required_scenarios.issubset(data.get("modes", {}).get(mode, {}).get("scenarios", {}))
            for mode in ("quick", "full")
        )
        explicit_scenarios = required_scenarios.issubset(data.get("scenarios", {}))
        finite = all(
            isinstance(defaults.get(key), (int, float))
            and math.isfinite(defaults[key])
            and defaults[key] < 10_000_000_000
            for key in ("maxAllocatedBytesPerMessage", "maxAllocatedBytesTotal")
        )
        large_bounded = (
            0 < large.get("maxAllocatedBytesPerMessage", 0) <= 4 * 1024 * 1024
            and 0 < large.get("maxAllocatedBytesTotal", 0) <= 16 * 1024 * 1024 * 1024
            and 0 <= large.get("maxGen1Collections", 0) <= 2000
            and 0 <= large.get("maxGen2Collections", 0) <= 2000
            and all(quick_large.get(key, 0) <= large.get(key, 0) for key in (
                "maxAllocatedBytesPerMessage",
                "maxAllocatedBytesTotal",
                "maxGen1Collections",
                "maxGen2Collections",
            ))
        )
        def valid_threshold(value: dict[str, object]) -> bool:
            """Validate one scenario threshold map."""
            numeric = (
                "maxElapsedMs",
                "maxAllocatedBytesPerMessage",
                "maxAllocatedBytesTotal",
                "maxGen0Collections",
                "maxGen1Collections",
                "maxGen2Collections",
            )
            finite_nonnegative = all(
                isinstance(value.get(key), (int, float))
                and math.isfinite(value[key])
                and value[key] >= 0
                for key in numeric
            )
            min_messages = value.get("minMessagesPerSecond", 0)
            return (
                finite_nonnegative
                and value["maxElapsedMs"] > 0
                and value["maxAllocatedBytesPerMessage"] > 0
                and value["maxAllocatedBytesTotal"] > 0
                and isinstance(min_messages, (int, float))
                and math.isfinite(min_messages)
                and min_messages >= 0
            )

        values_valid = all(valid_threshold(data["scenarios"][name]) for name in required_scenarios)
        mode_values_valid = all(
            valid_threshold(data["modes"][mode]["scenarios"][name])
            for mode in ("quick", "full")
            for name in required_scenarios
        )
        detail = f"defaults={defaults}; large={large}; quick_large={quick_large}; finite_defaults={finite}; bounded_large={large_bounded}; explicit_scenarios={explicit_scenarios}; explicit_modes={explicit_modes}; values_valid={values_valid}; mode_values_valid={mode_values_valid}"
        status = "PASS" if finite and large_bounded and explicit_scenarios and explicit_modes and values_valid and mode_values_valid else "FAIL"
    except (OSError, KeyError, TypeError, json.JSONDecodeError) as exc:
        detail = str(exc)
        status = "FAIL"
    evidence.write_text(detail + "\n", encoding="utf-8")
    return _check("199-THRESHOLDS", "Plan/199... § 5 allocation and GC gates", "auto", status, "read performance-thresholds.ci.json", evidence, detail)


def _run_baseline(output: Path, thresholds: Path, log_name: str) -> dict[str, object]:
    """Run the quick performance baseline and capture its report."""
    return audit_lib.run(
        ["python", "-B", "Scripts/performance/run_baseline.py", "--quick", "--thresholds", str(thresholds.relative_to(audit_lib.ROOT)), "--output", str(output.relative_to(audit_lib.ROOT))],
        OUTPUT / "logs" / log_name,
        900,
    )


def _red_alloc_check() -> audit_lib.Check:
    """Lower the large-payload ceiling and require the harness to fail."""
    evidence = OUTPUT / "logs" / "red-alloc.log"
    baseline_dir = OUTPUT / "red-baseline"
    baseline = _run_baseline(baseline_dir, THRESHOLDS, "red-baseline.log")
    report_paths = sorted(baseline_dir.glob("*.json"))
    if baseline["exit_code"] != 0 or not report_paths:
        evidence.write_text(str(baseline["output"]), encoding="utf-8")
        return _check("199-RED-alloc", "Plan/199... § 5 allocation red run", "mutation", "FAIL", "run_baseline.py --quick --thresholds performance-thresholds.ci.json", evidence, "baseline did not produce a passing report")
    report = json.loads(report_paths[-1].read_text(encoding="utf-8"))
    row = next((item for item in report.get("scenarios", []) if item.get("name") == "LargePayloadFanout"), None)
    if not row:
        evidence.write_text("LargePayloadFanout missing\n", encoding="utf-8")
        return _check("199-RED-alloc", "Plan/199... § 5 allocation red run", "mutation", "FAIL", "inspect LargePayloadFanout baseline", evidence, "baseline row missing")
    mutated = json.loads(THRESHOLDS.read_text(encoding="utf-8"))
    target = mutated["scenarios"]["LargePayloadFanout"]
    target["maxAllocatedBytesPerMessage"] = max(1, int(float(row["allocatedBytesPerMessage"]) * 0.5))
    target["maxAllocatedBytesTotal"] = max(1, int(float(row["allocatedBytesTotal"]) * 0.5))
    target["maxGen2Collections"] = max(0, int(int(row["gen2Collections"]) * 0.5))
    mutated.setdefault("modes", {}).setdefault("quick", {}).setdefault("scenarios", {})["LargePayloadFanout"] = dict(target)
    mutated_path = OUTPUT / "performance-thresholds.red.json"
    mutated_path.write_text(json.dumps(mutated, indent=2) + "\n", encoding="utf-8")
    red = _run_baseline(OUTPUT / "red-run", mutated_path, "red-alloc.log")
    output = str(red["output"])
    passed = red["exit_code"] != 0 and ("LargePayloadFanout" in output or "allocatedBytes" in output or "gen2Collections" in output)
    evidence.write_text(output, encoding="utf-8")
    detail = f"baseline_alloc_per_message={row['allocatedBytesPerMessage']}; baseline_total={row['allocatedBytesTotal']}; baseline_gen2={row['gen2Collections']}; red_exit={red['exit_code']}"
    return _check("199-RED-alloc", "Plan/199... § 5 allocation red run", "mutation", "PASS" if passed else "FAIL", "run_baseline.py --quick with lowered LargePayloadFanout ceilings", evidence, detail)


def _workflow_contract(text: str) -> bool:
    """Return whether the fixed CI lane remains quick and path-pinned."""
    required = (
        "workflow_dispatch:",
        "pull_request:",
        "Packages/dev.unity2foxglove.sdk/Runtime/Core/**",
        "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/**",
        "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/**",
        "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/**",
        "Packages/dev.unity2foxglove.sdk/Tests/Performance/**",
        "Scripts/performance/**",
        "--quick",
        "performance-thresholds.ci.json",
        "permissions:",
        "contents: read",
        "persist-credentials: false",
    )
    return all(token in text for token in required)


def _workflow_check() -> audit_lib.Check:
    """Run the repository workflow-pin regression against current and mutated fixtures."""
    evidence = OUTPUT / "logs" / "workflow.log"
    text = WORKFLOW.read_text(encoding="utf-8") if WORKFLOW.is_file() else ""
    original_contract = _workflow_contract(text)

    def run_existing_pin_test(workflow_text: str, root: Path) -> tuple[bool, str]:
        """Run the existing performance pin test against one workflow fixture."""
        fixture = root / ".github" / "workflows" / "performance.yml"
        fixture.parent.mkdir(parents=True, exist_ok=True)
        fixture.write_text(workflow_text, encoding="utf-8")
        test_path = audit_lib.ROOT / "Scripts" / "performance" / "regression_checks" / "test_performance_tooling.py"
        module_name = f"performance_tooling_pin_{abs(hash(workflow_text))}"
        spec = importlib.util.spec_from_file_location(module_name, test_path)
        if spec is None or spec.loader is None:
            return False, "unable to load repository pin test"
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        module.ROOT = root
        suite = unittest.defaultTestLoader.loadTestsFromName(
            "PerformanceToolingTests.test_performance_workflow_pins_ci_thresholds_and_baseline_command",
            module,
        )
        output = io.StringIO()
        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            result = unittest.TextTestRunner(stream=output, verbosity=0).run(suite)
        return result.wasSuccessful(), output.getvalue()

    with tempfile.TemporaryDirectory(dir=audit_lib.BUILD_ROOT) as temp:
        fixture_root = Path(temp)
        original_test, original_output = run_existing_pin_test(text, fixture_root / "original")
        mutated_test, mutated_output = run_existing_pin_test(text.replace("--quick ", "", 1), fixture_root / "mutated")
    detail = (
        f"workflow_contract={original_contract}; existing_pin_test={original_test}; "
        f"mutated_pin_test_rejected={not mutated_test}"
    )
    evidence.write_text(detail + "\n" + original_output + mutated_output, encoding="utf-8")
    status = "PASS" if original_contract and original_test and not mutated_test else "FAIL"
    return _check("199-PIN", "Plan/199... § 4 W2", "mutation", status, "run repository performance workflow pin test on current and mutated workflow fixtures", evidence, detail)


def _history_check() -> audit_lib.Check:
    """Verify recent main performance workflow history through GitHub CLI."""
    evidence = OUTPUT / "logs" / "ci-history.log"
    result = audit_lib.run(["gh", "run", "list", "--workflow", "performance.yml", "--branch", "main", "--limit", "10", "--json", "databaseId,conclusion,headSha,createdAt"], evidence, 120)
    if result["exit_code"] != 0:
        return _check("199-CI-history", "Plan/199... § 5 CI history", "history", "NOT_RUN", "gh run list --workflow performance.yml --branch main --limit 10", evidence, "GitHub CLI history unavailable")
    try:
        runs = json.loads(str(result["output"]))
    except json.JSONDecodeError:
        return _check("199-CI-history", "Plan/199... § 5 CI history", "history", "NOT_RUN", "gh run list --workflow performance.yml --branch main --limit 10", evidence, "GitHub CLI returned invalid JSON")
    successes = [run for run in runs if run.get("conclusion") == "success"]
    status = "PASS" if successes else "FAIL"
    return _check("199-CI-history", "Plan/199... § 5 CI history", "history", status, "gh run list --workflow performance.yml --branch main --limit 10", evidence, f"runs={len(runs)}; successful_runs={len(successes)}")


def _docs_check() -> audit_lib.Check:
    """Require benchmark and backpressure evidence tied to the fresh baseline."""
    evidence = OUTPUT / "logs" / "docs.log"
    guide = GUIDE.read_text(encoding="utf-8") if GUIDE.is_file() else ""
    research = RESEARCH.read_text(encoding="utf-8") if RESEARCH.is_file() else ""
    baseline_paths = sorted((OUTPUT / "red-baseline").glob("*.json"))
    observed = {}
    if baseline_paths:
        try:
            report = json.loads(baseline_paths[-1].read_text(encoding="utf-8"))
            observed = next((item for item in report.get("scenarios", []) if item.get("name") == "LargePayloadFanout"), {})
        except (OSError, json.JSONDecodeError):
            observed = {}
    observed_mb = float(observed.get("allocatedBytesPerMessage", 0)) / 1_000_000
    observed_gen2 = int(observed.get("gen2Collections", 0))
    expected_mb = f"{observed_mb:.1f} MB"
    expected_gen2 = "1,300-1,400"
    guide_ok = all(token in guide for token in ("1000 Hz", "1 MB", "backpressure", "benchmark"))
    research_ok = all(token in research for token in ("LargePayloadFanout", expected_mb, expected_gen2, "gen2", "backpressure")) and 1300 <= observed_gen2 <= 1400
    detail = f"guide={guide_ok}; research={research_ok}; observed_mb={expected_mb}; observed_gen2={expected_gen2}"
    evidence.write_text(detail + "\n", encoding="utf-8")
    return _check("199-DOCS", "Plan/199... § 5 documentation", "auto", "PASS" if guide_ok and research_ok else "FAIL", "read FoxRun guide and shared-emitter research note", evidence, detail)


def _tests_check() -> audit_lib.Check:
    """Run the plan-audit regression tests."""
    evidence = OUTPUT / "logs" / "tests.log"
    result = audit_lib.run(["python", "-B", "-m", "unittest", "Scripts.validation.regression_checks.test_plan_audit_lib"], evidence, 300)
    return _check("199-TESTS", "Plan/199... § 5 validation", "auto", "PASS" if result["exit_code"] == 0 else "FAIL", "python -B -m unittest Scripts.validation.regression_checks.test_plan_audit_lib", evidence, f"exit={result['exit_code']}")


def main() -> int:
    """Run the complete Plan199 performance evidence audit."""
    OUTPUT.mkdir(parents=True, exist_ok=True)
    before, before_sha = audit_lib.worktree_snapshot(exclude=("build/",))
    (OUTPUT / "state-before.txt").write_text(before, encoding="utf-8")
    base_sha = audit_lib.resolve_revision()
    checks = [_plan_check(base_sha), _threshold_check(), _workflow_check(), _red_alloc_check(), _history_check(), _docs_check(), _tests_check()]
    after, after_sha = audit_lib.worktree_snapshot(exclude=("build/",))
    (OUTPUT / "state-after.txt").write_text(after, encoding="utf-8")
    unchanged = before_sha == after_sha
    audit_lib.write_verdict(TOPIC, "199", checks, base_sha, OUTPUT, unchanged)
    return 0 if unchanged and all(check.status == "PASS" for check in checks) else 1


if __name__ == "__main__":
    raise SystemExit(main())
