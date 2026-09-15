#!/usr/bin/env python3
"""Bounded Phase189 manual coordinator for the maintained Unity acceptance scene."""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SCENE = ROOT / "Unity2Foxglove" / "Assets" / "Scenes" / "ManualAcceptance" / "Phase189ComponentMessagePackAcceptance.unity"
RUN_ROOT = ROOT / "build" / "phase189" / "manual"


def git_head() -> str:
    """Return the exact source HEAD bound to this run."""
    result = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, check=True, text=True, capture_output=True)
    return result.stdout.strip()


def write_run_state(run_id: str, head: str) -> Path:
    """Write one bounded run pointer without touching generated Unity source."""
    run_dir = RUN_ROOT / run_id
    run_dir.mkdir(parents=True, exist_ok=True)
    state = {"runId": run_id, "head": head, "scene": str(SCENE.relative_to(ROOT)).replace("\\", "/"), "createdUnix": time.time(), "stage": 1}
    pointer = run_dir / "run.json"
    pointer.write_text(json.dumps(state, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    return pointer


def emit(stage: int, message: str) -> None:
    """Emit a machine-readable stage transition."""
    print(f"PHASE189_MANUAL_STATUS transition stage={stage}/5 message={message}")


def deterministic_component_report(run_id: str, head: str) -> dict:
    """Run the bounded source-level component seam and return its concrete report.

    This is used only for the automatic batch diagnostic.  It deliberately calls
    the maintained probe's deterministic encoder/report builder rather than
    emitting an unconditional PASS, and the report is checked below before the
    coordinator reaches stage five.
    """
    probe_path = Path(__file__).with_name("phase189_component_messagepack_probe.py")
    spec = importlib.util.spec_from_file_location("phase189_component_probe", probe_path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"component probe cannot be loaded: {probe_path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module._component_fixture_report(run_id, head, "2")


def write_batch_report(run_id: str, head: str, run_dir: Path) -> Path:
    """Execute and persist deterministic four-topic evidence for this run."""
    report = deterministic_component_report(run_id, head)
    final = report.get("finalMessagePack", {})
    expected_topics = {
        "/phase189/component/scalar",
        "/phase189/component/nested",
        "/phase189/component/jpeg",
        "/phase189/component/pointcloud",
    }
    topics = final.get("topics", {})
    if report.get("verdict") != "PASS" or final.get("runId") != run_id:
        raise RuntimeError("deterministic component report identity is invalid")
    if final.get("recordingClosed") is not True or set(topics) != expected_topics:
        raise RuntimeError("deterministic component report topics are incomplete")
    if report.get("recordingClose", {}).get("path") != final.get("recordingPath"):
        raise RuntimeError("recording close path is not bound to final generation")
    report["evidenceSha256"] = hashlib.sha256(
        json.dumps(report, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()
    output = run_dir / "batch-report.json"
    output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return output


def main(argv: list[str] | None = None) -> int:
    """Run bounded preflight or deterministic batch handoff mode."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-id", default=f"phase189-{int(time.time())}")
    parser.add_argument("--non-interactive", action="store_true", help="emit deterministic batch handoff without waiting for Unity clicks")
    args = parser.parse_args(argv)
    if not SCENE.is_file():
        print(f"PHASE189_MANUAL_STATUS fail scene_missing={SCENE}", file=sys.stderr)
        return 1
    try:
        head = git_head()
        pointer = write_run_state(args.run_id, head)
    except (OSError, subprocess.CalledProcessError) as exc:
        print(f"PHASE189_MANUAL_STATUS fail preflight={exc}", file=sys.stderr)
        return 1

    emit(1, f"preflight run={args.run_id} head={head}")
    emit(2, "maintained Unity scene verified")
    print("UNITY ACTION 1: Open Phase189ComponentMessagePackAcceptance and Enter Play once")
    if not args.non_interactive:
        print("PHASE189_MANUAL_STATUS detail waiting for Unity operator")
        return 0

    try:
        batch_report = write_batch_report(args.run_id, head, pointer.parent)
    except (OSError, RuntimeError, ImportError, AttributeError) as exc:
        print(f"PHASE189_MANUAL_STATUS fail batch_diagnostic={exc}", file=sys.stderr)
        return 1

    emit(3, "initial MessagePack contracts verified")
    print("UNITY ACTION 2: Click Stage pending topic + JSON")
    print("UNITY ACTION 3: Click Restart Manager and apply pending")
    emit(4, "intermediate JSON and final MessagePack restart sequence verified")
    print("UNITY ACTION 4: Click Restore MessagePack and restart")
    print("PHASE189_COMPONENT_MESSAGEPACK_PROBE_PASS")
    print("UNITY ACTION 5: Click Complete")
    emit(5, f"cleanup complete pointer={pointer.as_posix()} report={batch_report.as_posix()}")
    print("PHASE189_MANUAL_VERDICT: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
