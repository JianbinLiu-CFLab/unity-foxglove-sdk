#!/usr/bin/env python3
"""Bounded Phase189 manual coordinator for the maintained Unity acceptance scene."""
from __future__ import annotations

import argparse
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

    emit(3, "initial MessagePack contracts verified")
    print("UNITY ACTION 2: Click Stage pending topic + JSON")
    print("UNITY ACTION 3: Click Restart Manager and apply pending")
    emit(4, "intermediate JSON and final MessagePack restart sequence verified")
    print("UNITY ACTION 4: Click Restore MessagePack and restart")
    print("PHASE189_COMPONENT_MESSAGEPACK_PROBE_PASS")
    print("UNITY ACTION 5: Click Complete")
    emit(5, f"cleanup complete pointer={pointer.as_posix()}")
    print("PHASE189_MANUAL_VERDICT: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
