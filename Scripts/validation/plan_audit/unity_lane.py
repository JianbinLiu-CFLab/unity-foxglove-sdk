"""Evidence checks for the Phase 195 Unity Test Runner lane."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ElementTree
from pathlib import Path
from types import ModuleType

from . import audit_lib

PLAN_REF = "Plan/195_PHASE195_WORKSTREAM_E_UNITY_ACCEPTANCE_HARDENING_PLAN.md § W1"
WORKFLOW = Path(".github/workflows/unity-compile.yml")
RUNNER = Path("Scripts/release/run_unity_tests.py")
EDIT = Path("Unity2Foxglove/Assets/Tests/EditMode/Unity2FoxgloveSensorsEditModeSmokeTests.cs")
ASSETS = (Path("Unity2Foxglove/Assets/Tests/EditMode/Unity2Foxglove.Sensors.EditModeTests.asmdef"), Path("Unity2Foxglove/Assets/Tests/PlayMode/Unity2Foxglove.Sensors.PlayModeTests.asmdef"), EDIT, Path("Unity2Foxglove/Assets/Tests/PlayMode/Unity2FoxgloveSensorsPlayModeSmokeTests.cs"))


def _record(checks: list[audit_lib.Check], check_id: str, kind: str, status: str, command: str, evidence: Path, detail: str) -> None:
    """Append one check with a repository-relative evidence path."""
    checks.append(audit_lib.Check(check_id, PLAN_REF, kind, status, command, str(evidence.resolve().relative_to(audit_lib.ROOT)).replace("\\", "/"), detail))


def _base(output: Path) -> str:
    """Read the base revision selected by the parent audit."""
    marker = output / "base.txt"
    if marker.is_file():
        for line in marker.read_text(encoding="utf-8", errors="replace").splitlines():
            value = line.strip()
            if len(value) == 40 and all(char in "0123456789abcdefABCDEF" for char in value):
                return value
    return subprocess.check_output(["git", "rev-parse", "origin/main"], cwd=audit_lib.ROOT, text=True).strip()


def _git_show(revision: str, path: Path) -> str | None:
    """Read a path from a selected revision without changing the worktree."""
    result = subprocess.run(["git", "show", f"{revision}:{path.as_posix()}"], cwd=audit_lib.ROOT, capture_output=True, text=True, check=False)
    return result.stdout if result.returncode == 0 else None


def _load(path: Path, name: str) -> ModuleType:
    """Load a runner module by absolute path."""
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ImportError(str(path))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _runner(revision: str, output: Path, name: str) -> ModuleType | None:
    """Load the branch runner or export a base revision into build."""
    if revision == "WORKTREE":
        return _load(audit_lib.ROOT / RUNNER, name)
    tree = output / "probe" / f"tree-{revision[:12]}"
    try:
        if not (tree / RUNNER).is_file():
            audit_lib.temp_git_tree(revision, tree, (RUNNER.as_posix(),))
        return _load(tree / RUNNER, name)
    except (OSError, ImportError, subprocess.CalledProcessError):
        return None


def _assets(checks: list[audit_lib.Check], output: Path, base: str) -> None:
    """Check EditMode and PlayMode assemblies in base and branch."""
    base_missing = [path.as_posix() for path in ASSETS if _git_show(base, path) is None]
    branch_missing = [path.as_posix() for path in ASSETS if not (audit_lib.ROOT / path).is_file()]
    evidence = output / "logs" / "w1-assets.log"
    evidence.parent.mkdir(parents=True, exist_ok=True)
    evidence.write_text(json.dumps({"base_missing": base_missing, "branch_missing": branch_missing}, indent=2) + "\n", encoding="utf-8")
    _record(checks, "195-W1-assets", "auto", "PASS" if not branch_missing else "MISSING", "git show <base>:Unity2Foxglove/Assets/Tests; inspect branch paths", evidence, f"base missing={base_missing or 'none'}; branch missing={branch_missing or 'none'}")


def _script(checks: list[audit_lib.Check], output: Path) -> None:
    """Run focused runner tests and preserve their actual output."""
    command = [sys.executable, "-m", "unittest", "Scripts.release.regression_checks.test_release_tooling.unity_batch_compile_gate_tests.UnityTestRunnerTests"]
    result = audit_lib.run(command, output / "logs" / "w1-script.log", 180)
    _record(checks, "195-W1-script", "auto", "PASS" if result["exit_code"] == 0 else "FAIL", " ".join(command), output / "logs" / "w1-script.log", "focused tests passed" if result["exit_code"] == 0 else "focused tests failed")


def _workflow(checks: list[audit_lib.Check], output: Path, base: str) -> None:
    """Check branch job ordering, triggers, artifacts, runner, and required policy."""
    branch = audit_lib.ROOT / WORKFLOW
    text = branch.read_text(encoding="utf-8", errors="replace") if branch.is_file() else ""
    required = ("workflow_dispatch:", "pull_request:", "unity-tests:", "needs: unity_compile", "run_unity_tests.py", "--test-results", "upload-artifact")
    missing = [item for item in required if item not in text]
    tests_at = text.find("\n  unity-tests:")
    required_at = text.find("\n  unity_required:")
    tests_block = text[tests_at:required_at] if tests_at >= 0 and required_at > tests_at else ""
    if "runs-on: [self-hosted, Windows, X64, unity2foxglove-phase186-live]" not in tests_block:
        missing.append("same runner")
    required_block = text[required_at:] if required_at >= 0 else ""
    needs_at = required_block.find("\n    needs:")
    needs_block = required_block[needs_at:] if needs_at >= 0 else ""
    needs_block = needs_block.split("\n    if:", 1)[0] if needs_block else ""
    if required_at < 0 or needs_at < 0 or "unity-tests" in needs_block:
        missing.append("unity-tests must remain non-required")
    evidence = output / "logs" / "w1-workflow.log"
    evidence.parent.mkdir(parents=True, exist_ok=True)
    evidence.write_text(json.dumps({"base_has_workflow": _git_show(base, WORKFLOW) is not None, "missing": missing}, indent=2) + "\n", encoding="utf-8")
    _record(checks, "195-W1-workflow", "auto", "PASS" if not missing else "FAIL", "read .github/workflows/unity-compile.yml", evidence, f"branch missing={missing or 'none'}; base was audited at {base}")


def _dispatch(checks: list[audit_lib.Check], output: Path) -> None:
    """Inspect workflow dispatch history without triggering a run."""
    command = ["gh", "run", "list", "--workflow", "unity-compile.yml", "--event", "workflow_dispatch", "--branch", "main", "--limit", "20", "--json", "databaseId,conclusion"]
    result = audit_lib.run(command, output / "logs" / "w1-dispatch.log", 120)
    if result["exit_code"] != 0:
        status, detail = "NOT_RUN", "GitHub history unavailable"
    else:
        try:
            runs = json.loads(str(result["output"]))
        except json.JSONDecodeError:
            runs = []
        successful = [run for run in runs if run.get("conclusion") == "success"]
        verified = []
        for run in successful[:5]:
            view = audit_lib.run(["gh", "run", "view", str(run["databaseId"]), "--json", "jobs"], output / "logs" / f"w1-dispatch-{run['databaseId']}.log", 120)
            try:
                jobs = json.loads(str(view["output"])).get("jobs", [])
            except json.JSONDecodeError:
                jobs = []
            if any(job.get("name") == "unity-tests" and job.get("conclusion") == "success" for job in jobs):
                verified.append(run["databaseId"])
        status = "PASS" if verified else "MANUAL"
        detail = f"verified runs={verified}" if verified else "no successful smoke run; user trigger required"
    _record(checks, "195-W1-dispatch", "history", status, " ".join(command), output / "logs" / "w1-dispatch.log", detail)


def _zero_fixtures(module: ModuleType, directory: Path) -> dict[str, str]:
    """Run three NUnit fixtures through one runner parser."""
    fixtures = (("total-zero", (0, 0, 0, 0, 0)), ("all-skipped", (1, 0, 0, 0, 1)), ("all-inconclusive", (1, 0, 0, 1, 0)))
    results = {}
    for name, values in fixtures:
        path = directory / f"{name}.xml"
        keys = ("total", "passed", "failed", "inconclusive", "skipped")
        path.write_text("<test-run " + " ".join(f'{key}=\"{value}\"' for key, value in zip(keys, values)) + " />", encoding="utf-8")
        results[name] = module.parse_nunit_results(path)[0]
    return results


def _zero_tests(checks: list[audit_lib.Check], output: Path, base: str) -> None:
    """Compare zero-effective-test behavior in base and branch modules."""
    base_module = _runner(base, output, "base_unity_runner")
    branch_module = _runner("WORKTREE", output, "branch_unity_runner")
    evidence = output / "logs" / "w1-zero-tests.log"
    if base_module is None or branch_module is None:
        evidence.write_text("unable to load selected runner\n", encoding="utf-8")
        _record(checks, "195-W1-zero-tests", "probe", "NOT_RUN", "parse_nunit_results against base and branch fixtures", evidence, "runner import failed")
        return
    with tempfile.TemporaryDirectory(dir=output / "probe") as temp:
        root = Path(temp)
        base_dir, branch_dir = root / "base", root / "branch"
        base_dir.mkdir()
        branch_dir.mkdir()
        base_outcomes = _zero_fixtures(base_module, base_dir)
        branch_outcomes = _zero_fixtures(branch_module, branch_dir)
    evidence.write_text(json.dumps({"base": base_outcomes, "branch": branch_outcomes}, indent=2) + "\n", encoding="utf-8")
    status = "PASS" if all(value != "PASS" for value in branch_outcomes.values()) else "FAIL"
    _record(checks, "195-W1-zero-tests", "probe", status, "parse_nunit_results total=0, all skipped, all inconclusive", evidence, f"base={base_outcomes}; branch={branch_outcomes}")


def _preflight() -> tuple[bool, str]:
    """Refuse Unity launch when lock, ownership, or C: writes are possible."""
    unity = Path(os.environ.get("UNITY_EXECUTABLE", r"C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe"))
    project = audit_lib.ROOT / "Unity2Foxglove"
    if not unity.is_file():
        return False, f"Unity executable not found: {unity}"
    if (project / "Temp" / "UnityLockfile").exists():
        return False, "Unity2Foxglove/Temp/UnityLockfile exists"
    processes = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Unity.exe"], capture_output=True, text=True, check=False)
    if "Unity.exe" in processes.stdout:
        return False, "Unity.exe is active and ownership is not exclusive"
    return True, "preflight passed"


def _red_failure_observed(results_path: Path) -> bool:
    """Return whether NUnit recorded the deliberate named red-run failure."""
    if not results_path.is_file():
        return False
    try:
        root = ElementTree.parse(results_path).getroot()
    except (ElementTree.ParseError, OSError):
        return False
    for testcase in root.iter("test-case"):
        result = testcase.attrib.get("result", "").lower()
        message = " ".join((testcase.attrib.get("message", ""), *testcase.itertext())).lower()
        if result in {"failed", "error"} and "plan audit red run" in message:
            return True
    return False


def _red(checks: list[audit_lib.Check], output: Path) -> None:
    """Use audit_lib.mutate for one red run, restoring bytes in all cases."""
    target = audit_lib.ROOT / EDIT
    evidence = output / "logs" / "w1-red.log"
    if not target.is_file():
        evidence.write_text(f"missing target: {EDIT.as_posix()}\n", encoding="utf-8")
        _record(checks, "195-W1-red", "mutation", "MISSING", "mutate EditMode smoke test and run Unity EditMode", evidence, f"missing {EDIT.as_posix()}")
        return
    original = target.read_bytes() if target.is_file() else b""
    before = hashlib.sha256(original).hexdigest()
    _, snapshot_before = audit_lib.worktree_snapshot(exclude=("build/",))
    backup = audit_lib.BUILD_ROOT / ".backup" / "unity-lane-editmode-smoke.cs"
    backup.parent.mkdir(parents=True, exist_ok=True)
    backup.write_bytes(original)
    old = "        {\n            Assert.That("
    new = '        {\n            Assert.Fail("plan audit red run");\n            Assert.That('
    safe = False
    red_exit: int | None = None
    green_detail = "green EditMode and PlayMode not run"
    try:
        with audit_lib.mutate(target, old, new):
            mutated = hashlib.sha256(target.read_bytes()).hexdigest()
            safe, reason = _preflight()
            if safe:
                command = [sys.executable, "-B", "Scripts/release/run_unity_tests.py", "--project-path", "Unity2Foxglove", "--test-platform", "EditMode", "--test-results", "build/plan-audit/unity_acceptance/red/editmode.xml", "--log-file", "build/plan-audit/unity_acceptance/red/editmode.log", "--nographics"]
                result = audit_lib.run(command, evidence, 3600)
                red_exit = int(result["exit_code"]) if result["exit_code"] is not None else None
                results_path = audit_lib.ROOT / "build/plan-audit/unity_acceptance/red/editmode.xml"
                payload_path = results_path.with_suffix(".json")
                try:
                    payload = json.loads(payload_path.read_text(encoding="utf-8"))
                except (OSError, json.JSONDecodeError):
                    payload = {}
                observed = _red_failure_observed(results_path)
                if red_exit is None:
                    status, detail = "NOT_RUN", f"mutated hash={mutated}; timeout or launch failure; exit={red_exit}"
                else:
                    status = "PASS" if red_exit != 0 and payload.get("verdict") == "FAIL" and observed else "FAIL"
                    detail = f"mutated hash={mutated}; exit={red_exit}; verdict={payload.get('verdict')}; named_failure={observed}"
            else:
                evidence.write_text(f"mutated hash={mutated}\nNOT_RUN: {reason}\n", encoding="utf-8")
                status, detail = "NOT_RUN", f"mutated hash={mutated}; NOT_RUN: {reason}"
    except (ValueError, FileNotFoundError) as exc:
        evidence.write_text(f"NOT_RUN: {exc}\n", encoding="utf-8")
        status, detail = "NOT_RUN", str(exc)
    if safe and red_exit not in (None, 0):
        green_results = []
        for platform in ("EditMode", "PlayMode"):
            green_command = [sys.executable, "-B", "Scripts/release/run_unity_tests.py", "--project-path", "Unity2Foxglove", "--test-platform", platform, "--test-results", f"build/plan-audit/unity_acceptance/green/{platform.lower()}.xml", "--log-file", f"build/plan-audit/unity_acceptance/green/{platform.lower()}.log", "--nographics"]
            green = audit_lib.run(green_command, output / "logs" / f"w1-green-{platform.lower()}.log", 3600)
            green_results.append((platform, green["exit_code"]))
        if any(exit_code != 0 for _, exit_code in green_results):
            status = "FAIL"
        green_detail = f"green results={green_results}"
    restored = hashlib.sha256(target.read_bytes()).hexdigest()
    if restored != before:
        raise RuntimeError(f"mutation restore changed bytes: {target}")
    _, snapshot_after = audit_lib.worktree_snapshot(exclude=("build/",))
    if snapshot_after != snapshot_before:
        raise RuntimeError("mutation changed the worktree snapshot")
    _record(checks, "195-W1-red", "mutation", status, "python -B Scripts/release/run_unity_tests.py --project-path Unity2Foxglove --test-platform EditMode --test-results build/plan-audit/unity_acceptance/red/editmode.xml --log-file build/plan-audit/unity_acceptance/red/editmode.log --nographics", evidence, detail + f"; {green_detail}; restored hash={restored}")


def lane_checks(output: Path) -> list[audit_lib.Check]:
    """Return W1 checks and write actual logs under the supplied output directory."""
    output = output.resolve()
    (output / "logs").mkdir(parents=True, exist_ok=True)
    base = _base(output)
    checks: list[audit_lib.Check] = []
    _assets(checks, output, base)
    _script(checks, output)
    _workflow(checks, output, base)
    _dispatch(checks, output)
    _zero_tests(checks, output, base)
    _red(checks, output)
    return checks
