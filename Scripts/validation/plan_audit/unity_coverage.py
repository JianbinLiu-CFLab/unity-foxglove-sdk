"""Evidence-aware coverage checks for the Unity acceptance plan."""

from __future__ import annotations

import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ElementTree
from pathlib import Path

from . import audit_lib

PLAN_PATH = "Plan/195_PHASE195_WORKSTREAM_E_UNITY_ACCEPTANCE_HARDENING_PLAN.md"
EXPECTED_CASES = {
    "W2": frozenset(
        {
            "GraphicsAcceptance_CameraInfoUsesImageSourceCamera",
            "GraphicsAcceptance_ResizeDuringReadbackProducesNoErrors",
            "GraphicsAcceptance_ResizeFramesKeepDimensionsAndPayloadsConsistent",
        }
    ),
    "W3": frozenset(
        {
            "LidarAcceptance_ConfigurationChangesApplyAfterReenable",
            "LidarAcceptance_ElapsedPhysicsTimeSpansMultipleTicks",
            "LidarAcceptance_SensorTransformDefinesPointFrame",
        }
    ),
}


def _record(checks: list[audit_lib.Check], check_id: str, kind: str, status: str, command: str, evidence: Path, detail: str) -> None:
    """Append a check with a repository-relative evidence path."""
    evidence = evidence.resolve()
    section = {"195-W2": "§ W2", "195-W3": "§ W3", "195-W4": "§ W4", "195-OBS-jazzy-meta": "§ OBS"}.get(check_id, "§ 5")
    checks.append(audit_lib.Check(check_id, f"{PLAN_PATH} {section}", kind, status, command, str(evidence.relative_to(audit_lib.ROOT)).replace("\\", "/"), detail))


def _source_files() -> list[Path]:
    """Return C# files under the Unity test tree."""
    tests = audit_lib.ROOT / "Unity2Foxglove" / "Assets" / "Tests"
    return sorted(tests.rglob("*.cs")) if tests.is_dir() else []


def _unity_acceptance_result(results: Path, category: str, expected_names: frozenset[str]) -> tuple[str, str]:
    """Read a Unity NUnit result file and require every selected case to pass."""
    if not results.is_file():
        return "NOT_RUN", f"results file is missing: {results}"
    try:
        root = ElementTree.parse(results).getroot()
    except (ElementTree.ParseError, OSError) as exc:
        return "FAIL", f"unable to parse Unity results: {exc}"
    cases = []
    for case in root.iter("test-case"):
        categories = {node.attrib.get("value") for node in case.findall("./properties/property") if node.attrib.get("name") == "Category"}
        if category in categories:
            cases.append(case)
    names = [case.attrib.get("name", "<unnamed>") for case in cases]
    unexpected = sorted(set(names) ^ expected_names)
    duplicates = sorted(name for name in expected_names if names.count(name) != 1)
    if unexpected or duplicates:
        return "FAIL", f"category={category}; unexpected={unexpected or 'none'}; duplicate-or-missing={duplicates or 'none'}"
    failed = [case.attrib.get("fullname") or case.attrib.get("name") or "<unnamed>" for case in cases if case.attrib.get("result", "").lower() != "passed"]
    if not cases:
        return "FAIL", f"no Unity test cases selected by category {category}"
    if all(case.attrib.get("result", "").lower() in {"skipped", "ignored", "inconclusive"} for case in cases):
        return "MANUAL", f"category={category}; cases={len(cases)}; graphics device unavailable"
    return ("PASS" if not failed else "FAIL"), f"category={category}; cases={len(cases)}; failed={failed or 'none'}"


def _mutation_failure_observed(results: Path, category: str, expected_name: str) -> bool:
    """Require the deliberate mutation to fail the exact behavioral case."""
    if not results.is_file():
        return False
    try:
        root = ElementTree.parse(results).getroot()
    except (ElementTree.ParseError, OSError):
        return False
    matches = []
    for case in root.iter("test-case"):
        categories = {node.attrib.get("value") for node in case.findall("./properties/property") if node.attrib.get("name") == "Category"}
        if category in categories and case.attrib.get("name") == expected_name:
            matches.append(case)
    return len(matches) == 1 and matches[0].attrib.get("result", "").lower() in {"failed", "error"}


def _coverage(checks: list[audit_lib.Check], output: Path, workstream: str, terms: tuple[str, ...], platform: str) -> None:
    """Classify workstream artifacts without treating tokens as acceptance evidence."""
    named = [path for path in _source_files() if any(term.lower() in path.name.lower() for term in terms)]
    evidence = output / "logs" / f"{workstream.lower()}.log"
    evidence.parent.mkdir(parents=True, exist_ok=True)
    evidence.write_text("\n".join(str(path.relative_to(audit_lib.ROOT)) for path in named) + "\n", encoding="utf-8")
    command = "rg --files Unity2Foxglove/Assets/Tests | rg '(Camera|Readback)'" if workstream == "W2" else "rg --files Unity2Foxglove/Assets/Tests | rg '(Lidar|LiDAR)'"
    if not named:
        _record(checks, f"195-{workstream}", "auto", "MISSING", command, evidence, f"no named {workstream} acceptance test artifact exists")
    elif workstream == "W3":
        results = audit_lib.BUILD_ROOT / "unity_acceptance" / "w3" / "playmode.xml"
        status, detail = _unity_acceptance_result(results, "LidarAcceptance", EXPECTED_CASES["W3"])
        _record(checks, "195-W3", "probe", status, "python -B Scripts/release/run_unity_tests.py --test-platform PlayMode", results, detail)
    elif workstream == "W2":
        results = audit_lib.BUILD_ROOT / "unity_acceptance" / "w2" / "playmode.xml"
        status, detail = _unity_acceptance_result(results, "GraphicsAcceptance", EXPECTED_CASES["W2"])
        _record(checks, "195-W2", "probe", status, "python -B Scripts/release/run_unity_tests.py --test-platform PlayMode --test-category GraphicsAcceptance", results, detail)
    else:
        _record(checks, f"195-{workstream}", "probe", "NOT_RUN", f"python -B -m Scripts.validation.plan_audit.unity_acceptance --platform {platform}", evidence, f"candidate files={len(named)}; Unity execution and defect mutation are required")


def _w2_mutation(checks: list[audit_lib.Check], output: Path) -> None:
    """Require camera resize acceptance to fail when retired render textures release immediately."""
    source = audit_lib.ROOT / "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraCaptureResources.cs"
    evidence = output / "logs" / "w2-mutation.log"
    command = [
        sys.executable,
        "-B",
        "Scripts/release/run_unity_tests.py",
        "--project-path",
        "Unity2Foxglove",
        "--test-platform",
        "PlayMode",
        "--test-category",
        "GraphicsAcceptance",
        "--test-results",
        "build/plan-audit/unity_acceptance/w2/red/playmode.xml",
        "--log-file",
        "build/plan-audit/unity_acceptance/w2/red/playmode.log",
    ]
    old = "                RetireRenderTexture();"
    new = "                DestroyRenderTexture();"
    try:
        with audit_lib.mutate(source, old, new):
            result = audit_lib.run(command, evidence, 3600)
        payload_path = audit_lib.BUILD_ROOT / "unity_acceptance" / "w2" / "red" / "playmode.json"
        try:
            payload = json.loads(payload_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            payload = {}
        results_path = audit_lib.BUILD_ROOT / "unity_acceptance" / "w2" / "red" / "playmode.xml"
        observed = _mutation_failure_observed(results_path, "GraphicsAcceptance", "GraphicsAcceptance_ResizeDuringReadbackProducesNoErrors")
        status = "PASS" if result["exit_code"] != 0 and payload.get("verdict") == "FAIL" and observed else "FAIL"
        detail = f"mutated retired render texture release; exit={result['exit_code']}; verdict={payload.get('verdict', 'missing')}; named_failure={observed}"
    except (OSError, ValueError, RuntimeError) as exc:
        status = "NOT_RUN"
        detail = f"mutation not run: {exc}"
        evidence.write_text(detail + "\n", encoding="utf-8")
    _record(checks, "195-W2-mutation", "mutation", status, "mutate CameraCaptureResources retired texture release and run GraphicsAcceptance", evidence, detail)


def _w3_mutation(checks: list[audit_lib.Check], output: Path) -> None:
    """Require the elapsed-physics-time test to fail under nominal-tick mutation."""
    source = audit_lib.ROOT / "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs"
    evidence = output / "logs" / "w3-mutation.log"
    command = [
        sys.executable,
        "-B",
        "Scripts/release/run_unity_tests.py",
        "--project-path",
        "Unity2Foxglove",
        "--test-platform",
        "PlayMode",
        "--test-category",
        "LidarAcceptance",
        "--test-results",
        "build/plan-audit/unity_acceptance/w3/red/playmode.xml",
        "--log-file",
        "build/plan-audit/unity_acceptance/w3/red/playmode.log",
        "--nographics",
    ]
    old = "                    dt,\n                    _scanBuffers.ScanColumnCount,"
    new = "                    0.02,\n                    _scanBuffers.ScanColumnCount,"
    try:
        with audit_lib.mutate(source, old, new):
            result = audit_lib.run(command, evidence, 3600)
        payload_path = audit_lib.BUILD_ROOT / "unity_acceptance" / "w3" / "red" / "playmode.json"
        try:
            payload = json.loads(payload_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            payload = {}
        results_path = audit_lib.BUILD_ROOT / "unity_acceptance" / "w3" / "red" / "playmode.xml"
        observed = _mutation_failure_observed(results_path, "LidarAcceptance", "LidarAcceptance_ElapsedPhysicsTimeSpansMultipleTicks")
        status = "PASS" if result["exit_code"] != 0 and payload.get("verdict") == "FAIL" and observed else "FAIL"
        detail = f"mutated elapsed-time argument; exit={result['exit_code']}; verdict={payload.get('verdict', 'missing')}; named_failure={observed}"
    except (OSError, ValueError, RuntimeError) as exc:
        status = "NOT_RUN"
        detail = f"mutation not run: {exc}"
        evidence.write_text(detail + "\n", encoding="utf-8")
    _record(checks, "195-W3-mutation", "mutation", status, "mutate VirtualLidar elapsed-time argument and run PlayMode", evidence, detail)


def _workflow_w4(checks: list[audit_lib.Check], output: Path) -> None:
    """Check explicit Humble and Lyrical workflow obligations and fresh runs."""
    workflow = audit_lib.ROOT / ".github" / "workflows" / "unity-compile.yml"
    text = workflow.read_text(encoding="utf-8", errors="replace") if workflow.is_file() else ""
    required = {"workflow_dispatch": "workflow_dispatch", "humble": "humble", "lyrical": "lyrical", "always": "if: always()", "library": "Library"}
    evidence = output / "logs" / "w4.log"
    evidence.parent.mkdir(parents=True, exist_ok=True)
    lines = [f"{name}={needle in text}" for name, needle in required.items()]
    distro_path_markers = (
        r"Packages/dev\.unity2foxglove\.(ros2forunity\.runtime|foxrun\.ros2\.interfaces\.typesupport)\.(humble|lyrical)\.win64",
        "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
        "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64",
    )
    distro_paths = any(marker in text for marker in distro_path_markers)
    lines.append(f"distro_paths={distro_paths}")
    evidence.write_text("\n".join(lines) + "\n", encoding="utf-8")
    missing = [name for name, needle in required.items() if needle not in text]
    if not distro_paths:
        missing.append("distro_paths")
    if missing:
        _record(checks, "195-W4", "auto", "MISSING", "Get-Content .github/workflows/unity-compile.yml", evidence, f"missing workflow requirements={missing}")
        return

    branch = subprocess.run(["git", "branch", "--show-current"], cwd=audit_lib.ROOT, capture_output=True, text=True, check=False).stdout.strip()
    command = ["gh", "run", "list", "--workflow", "unity-compile.yml", "--branch", branch, "--limit", "20", "--json", "databaseId,headSha,event,conclusion"]
    result = audit_lib.run(command, output / "logs" / "w4-history.log", 120)
    if result["exit_code"] != 0:
        _record(checks, "195-W4", "history", "NOT_RUN", " ".join(command), output / "logs" / "w4-history.log", "workflow history unavailable")
        return
    try:
        runs = json.loads(str(result["output"]))
    except json.JSONDecodeError:
        _record(checks, "195-W4", "history", "NOT_RUN", " ".join(command), output / "logs" / "w4-history.log", "workflow history was not valid JSON")
        return
    head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=audit_lib.ROOT, capture_output=True, text=True, check=False).stdout.strip()
    verified: dict[str, str] = {}
    for run in runs:
        if run.get("conclusion") != "success" or run.get("headSha") != head:
            continue
        run_id = str(run.get("databaseId", ""))
        if not run_id:
            continue
        view = audit_lib.run(["gh", "run", "view", run_id, "--json", "jobs"], output / "logs" / f"w4-history-{run_id}.log", 120)
        try:
            jobs = json.loads(str(view["output"])).get("jobs", [])
        except json.JSONDecodeError:
            continue
        for distro in ("humble", "lyrical"):
            name = f"unity-distro-compile ({distro})"
            if any(job.get("name") == name and job.get("conclusion") == "success" for job in jobs):
                verified[distro] = run_id
    status = "PASS" if set(verified) == {"humble", "lyrical"} else "MANUAL"
    detail = f"head={head}; verified distro runs={verified or 'none'}; compile-error mutation is a separate manual acceptance"
    _record(checks, "195-W4", "history", status, " ".join(command), output / "logs" / "w4-history.log", detail)


def _w4_mutation(checks: list[audit_lib.Check], output: Path, head_sha: str) -> None:
    """Require revision-bound evidence that the deliberate Lyrical compile failure was observed."""
    evidence = output / "w4-mutation.json"
    log = output / "logs" / "w4-mutation.log"
    log.parent.mkdir(parents=True, exist_ok=True)
    if not head_sha:
        status = "FAIL"
        detail = "current HEAD revision could not be resolved"
    elif not evidence.is_file():
        status = "NOT_RUN"
        detail = f"required mutation evidence is missing: {evidence}"
    else:
        try:
            payload = json.loads(evidence.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            payload = {}
            detail = f"mutation evidence is not valid JSON: {exc}"
            status = "FAIL"
        else:
            if not isinstance(payload, dict):
                detail = "mutation evidence must be a JSON object"
                status = "FAIL"
            else:
                expected = {
                    "base_sha": head_sha,
                    "distro": "lyrical",
                    "mutation": "compile-error",
                    "job_name": "unity-distro-compile (lyrical)",
                    "job_conclusion": "failure",
                    "restored": True,
                }
                mismatches = [key for key, value in expected.items() if payload.get(key) != value]
                if not payload.get("run_id"):
                    mismatches.append("run_id")
                status = "PASS" if not mismatches else "FAIL"
                detail = f"base={payload.get('base_sha', 'missing')}; run_id={payload.get('run_id', 'missing')}; mismatches={mismatches or 'none'}"
    log.write_text(detail + "\n", encoding="utf-8")
    _record(checks, "195-W4-mutation", "mutation", status, "read build/plan-audit/unity_acceptance/w4-mutation.json", log, detail)


def _manifest_roots() -> list[Path]:
    """Resolve manifest-enabled local packages and the Unity Assets tree."""
    project = audit_lib.ROOT / "Unity2Foxglove"
    manifest = project / "Packages" / "manifest.json"
    roots = [project / "Assets"]
    try:
        dependencies = json.loads(manifest.read_text(encoding="utf-8")).get("dependencies", {})
    except (OSError, json.JSONDecodeError):
        dependencies = {}
    for value in dependencies.values():
        if isinstance(value, str) and value.startswith("file:"):
            roots.append((manifest.parent / value[5:]).resolve())
    return [path for path in roots if path.is_dir()]


def _guid_audit(checks: list[audit_lib.Check], output: Path, base_sha: str, head_sha: str) -> None:
    """Classify dirty metadata, enabled-scope GUID duplicates, and Editor.log conflicts."""
    package = "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64"
    diff_command = ["git", "diff", f"{base_sha}...{head_sha}", "--", package]
    diff = subprocess.run(diff_command, cwd=audit_lib.ROOT, capture_output=True, text=True, check=False)
    if diff.returncode != 0:
        evidence = output / "logs" / "jazzy-meta.log"
        evidence.parent.mkdir(parents=True, exist_ok=True)
        detail = f"unable to read committed metadata diff base={base_sha} head={head_sha}; exit={diff.returncode}"
        evidence.write_text(detail + "\n", encoding="utf-8")
        _record(checks, "195-OBS-jazzy-meta", "auto", "FAIL", " ".join(diff_command), evidence, detail)
        return
    changed_meta = sorted({line[6:] for line in diff.stdout.splitlines() if line.startswith("+++ b/") and line.endswith(".meta")})
    guid_changes = len(re.findall(r"^[+-]guid:\s*\S+", diff.stdout, re.MULTILINE))
    importer_changes = len([line for line in diff.stdout.splitlines() if (line.startswith("+") or line.startswith("-")) and not line.startswith(("+++", "---")) and not re.match(r"^[+-]guid:", line) and not line.startswith(("diff ", "index "))])
    counts: dict[str, list[str]] = {}
    for root in _manifest_roots():
        for meta in root.rglob("*.meta"):
            if any(part.endswith("~") for part in meta.parts):
                continue
            match = re.search(r"^guid:\s*([0-9a-fA-F]+)\s*$", meta.read_text(encoding="utf-8", errors="replace"), re.MULTILINE)
            if match:
                counts.setdefault(match.group(1).lower(), []).append(str(meta.relative_to(audit_lib.ROOT)).replace("\\", "/"))
    duplicates = {guid: paths for guid, paths in counts.items() if len(paths) > 1}
    editor_log = Path.home() / "AppData" / "Local" / "Unity" / "Editor" / "Editor.log"
    log_text = editor_log.read_text(encoding="utf-8", errors="replace") if editor_log.is_file() else ""
    conflict_lines = [line.strip() for line in log_text.splitlines() if re.search(r"guid", line, re.IGNORECASE) and re.search(r"conflict|duplicat|already", line, re.IGNORECASE)]
    evidence = output / "logs" / "jazzy-meta.log"
    evidence.parent.mkdir(parents=True, exist_ok=True)
    evidence.write_text(json.dumps({"base_sha": base_sha, "head_sha": head_sha, "changed_meta": changed_meta, "guid_changes": guid_changes, "importer_changes": importer_changes, "duplicates": duplicates, "editor_log": str(editor_log), "conflict_lines": conflict_lines[:20]}, indent=2) + "\n", encoding="utf-8")
    status = "FAIL" if duplicates or conflict_lines else "PASS"
    detail = f"base={base_sha}; head={head_sha}; changed_meta={len(changed_meta)}; guid_changes={guid_changes}; importer_changes={importer_changes}; enabled-scope duplicates={len(duplicates)}; Editor.log conflict lines={len(conflict_lines)}"
    _record(checks, "195-OBS-jazzy-meta", "auto", status, " ".join(diff_command) + "; scan manifest-enabled package and Assets .meta GUIDs", evidence, detail)


def coverage_checks(output: Path, base_sha: str | None = None, head_sha: str | None = None) -> list[audit_lib.Check]:
    """Return honest W2, W3, W4, and metadata coverage checks."""
    output.mkdir(parents=True, exist_ok=True)
    base_sha = base_sha or audit_lib.resolve_revision()
    if head_sha is None:
        result = subprocess.run(["git", "rev-parse", "HEAD"], cwd=audit_lib.ROOT, capture_output=True, text=True, check=False)
        head_sha = result.stdout.strip() if result.returncode == 0 else ""
    checks: list[audit_lib.Check] = []
    _coverage(checks, output, "W2", ("Camera", "Readback"), "PlayMode")
    _coverage(checks, output, "W3", ("Lidar", "LiDAR"), "PlayMode")
    if any(check.id == "195-W2" and check.status == "PASS" for check in checks):
        _w2_mutation(checks, output)
    if any(check.id == "195-W3" and check.status == "PASS" for check in checks):
        _w3_mutation(checks, output)
    _workflow_w4(checks, output)
    _w4_mutation(checks, output, head_sha)
    _guid_audit(checks, output, base_sha, head_sha)
    return checks


def manual_steps(checks: list[audit_lib.Check]) -> str:
    """Render step-by-step human acceptance instructions."""
    return """# Phase 195 manual acceptance

## W1 red run and dispatch
1. Confirm no Unity Editor process owns the project and, if `Unity2Foxglove/Temp/UnityLockfile` exists, verify it can be opened exclusively. A readable residual lockfile does not block the run.
2. Save the target EditMode smoke test bytes and insert `Assert.Fail(\"plan audit red run\")` in exactly one test.
3. Run `python -B Scripts/release/run_unity_tests.py --project-path Unity2Foxglove --test-platform EditMode --test-results build/plan-audit/unity_acceptance/red/editmode.xml --log-file build/plan-audit/unity_acceptance/red/editmode.log --nographics`.
4. Record non-zero exit code, `FAIL` verdict, and failing test name. Restore bytes and verify the hash.
5. Run green EditMode and PlayMode commands and retain XML and JSON artifacts.
6. With approval, trigger `unity-compile.yml` via `workflow_dispatch` on `main`; record run id, smoke names, JSON, and XML.

## W2 camera acceptance
1. Open `Unity2Foxglove` in Unity 6000 with a graphics device and no competing Editor.
2. Create a PlayMode scene with `FoxgloveCameraPublisher` and `FoxgloveCameraInfoPublisher` using different source cameras.
3. Start capture, resize output while `AsyncGPUReadback` is pending, and collect Console plus published frames.
4. Verify no exception, retired resources survive until generation drain, then release, and post-resize dimensions and intrinsics match the image source.
5. Mutate `CameraCaptureResources` to release immediately, rerun, record the expected failure, restore bytes, and hash-check.

## W3 LiDAR acceptance
1. Run a PlayMode scene with `Physics.simulationMode = SimulationMode.Script`, colliders, and a `VirtualLidar` scan spanning four ticks.
2. Advance with `Physics.Simulate` and `WaitForFixedUpdate`, calling `Physics.SyncTransforms` after collider moves.
3. Record point offsets, elapsed physics time, activated configuration, and sensor-transform deskew output.
4. Mutate elapsed-time progress to fixed nominal tick progress, require the timing assertion to fail, then restore and hash-check.

## W4 distro compile acceptance
1. Obtain approval to dispatch one run for Humble and one for Lyrical on `main`.
2. Record selected package, runner path on `D:`, independent `Library` directory, compile command, and run id.
3. Verify the manifest is restored in an `if: always()` step and `git status --porcelain` is clean.
4. In a disposable test branch, inject one compile error into the Lyrical copy and confirm batch compile fails. Record `build/plan-audit/unity_acceptance/w4-mutation.json` with `base_sha`, `run_id`, `distro=lyrical`, `mutation=compile-error`, `job_name=unity-distro-compile (lyrical)`, `job_conclusion=failure`, and `restored=true`.
"""
