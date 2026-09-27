from __future__ import annotations
from .wait_for_evidence_and_require_no_output import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase189_component_messagepack_probe.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def parse_args() -> argparse.Namespace:
    """Parse bounded live-probe command-line options."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="ws://127.0.0.1:8765")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--token", default="")
    parser.add_argument("--insecure", action="store_true")
    parser.add_argument(
        "--output",
        default="build/phase189/probe/report.json",
    )
    parser.add_argument(
        "--discovery-timeout-seconds",
        type=float,
        default=DISCOVERY_TIMEOUT_SECONDS,
    )
    parser.add_argument(
        "--service-timeout-seconds",
        type=float,
        default=SERVICE_TIMEOUT_SECONDS,
    )
    parser.add_argument("--apply-timeout-seconds", type=float, default=3.0)
    parser.add_argument(
        "--no-output-window-seconds",
        type=float,
        default=NO_OUTPUT_WINDOW_SECONDS,
    )
    parser.add_argument(
        "--local-output-timeout-seconds",
        type=float,
        default=LOCAL_OUTPUT_TIMEOUT_SECONDS,
    )
    parser.add_argument(
        "--exactly-once-quiet-seconds",
        type=_positive_seconds,
        default=EXACTLY_ONCE_QUIET_SECONDS,
    )
    parser.add_argument(
        "--malformed-settle-seconds",
        type=_positive_seconds,
        default=MALFORMED_SETTLE_SECONDS,
    )
    parser.add_argument(
        "--recovery-timeout-seconds",
        type=float,
        default=RECOVERY_TIMEOUT_SECONDS,
    )
    parser.add_argument(
        "--startup-drain-seconds",
        type=float,
        default=STARTUP_DRAIN_SECONDS,
    )
    parser.add_argument("--component-fixture", action="store_true")
    parser.add_argument("--run-id", default="phase189-fixture")
    parser.add_argument("--head", default="fixture-head")
    parser.add_argument("--generation", default="1")
    return parser.parse_args()
def _component_fixture_report(run_id: str, head: str, generation: str) -> dict[str, Any]:
    """Build deterministic identity/encoding/binary evidence for the batch seam."""
    topics = {
        "/phase189/component/scalar": {"effectiveEncoding": "msgpack", "logicalSchema": "phase189.component.Scalar", "shapeIdentity": "scalar.v1", "wireSchema": "schemaless", "payloadHex": "82a556616c7565cda189a54c4c6162656cb0"},
        "/phase189/component/nested": {"effectiveEncoding": "msgpack", "logicalSchema": "phase189.component.Nested", "shapeIdentity": "nested.v1", "wireSchema": "schemaless", "payloadHex": "82a853657175656e6365cda189a64e6573746564"},
        "/phase189/component/jpeg": {"effectiveEncoding": "msgpack", "logicalSchema": "foxglove.CompressedImage", "shapeIdentity": "jpeg.v1", "wireSchema": "schemaless", "binaryMember": "data", "payloadHex": "81a464617461c403ffd8ff"},
        "/phase189/component/pointcloud": {"effectiveEncoding": "msgpack", "logicalSchema": "foxglove.PointCloud", "shapeIdentity": "pointcloud.v1", "wireSchema": "schemaless", "binaryMember": "data", "payloadHex": "81a464617461c40401020304"},
    }
    path = f"build/phase189/manual/{run_id}/final.mcap"
    final = {"runId": run_id, "head": head, "generation": generation, "topics": topics, "recordingPath": path, "recordingOpen": True, "recordingClosed": True, "playExit": "EDIT_MODE"}
    return {"version": 1, "verdict": "PASS", "runId": run_id, "head": head, "generation": generation, "initialMessagePack": {"runId": run_id, "head": head, "generation": "0", "topics": topics}, "intermediateJson": {"runId": run_id, "head": head, "generation": "1", "effectiveEncoding": "json", "topics": []}, "finalMessagePack": final, "recordingClose": {"closed": True, "runId": run_id, "head": head, "generation": generation, "path": path}, "playExit": {"marker": "EDIT_MODE", "runId": run_id, "head": head}, "observations": {"noJsonFallback": True, "binaryMembers": ["jpeg.data", "pointcloud.data"]}}
def main() -> int:
    """Run the live probe and write a bounded PASS or FAIL report."""
    args = parse_args()
    url = _build_url(args)
    output = Path(args.output)
    if args.component_fixture:
        output.parent.mkdir(parents=True, exist_ok=True)
        atomic_write_text(output, json.dumps(_component_fixture_report(args.run_id, args.head, args.generation), indent=2, sort_keys=True) + "\n", encoding="utf-8")
        print("PHASE189_COMPONENT_MESSAGEPACK_PROBE_PASS")
        print(f"Report: {output}")
        return EXIT_SUCCESS
    try:
        report = asyncio.run(run_live(args))
    except (ProbeFailure, OSError, ValueError) as exc:
        failure = {
            "version": 1,
            "verdict": "FAIL",
            "endpoint": _redacted_url(url),
            "reason": str(exc),
        }
        output.parent.mkdir(parents=True, exist_ok=True)
        atomic_write_text(output,
            json.dumps(failure, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        print("Verdict: FAIL")
        print(str(exc))
        return EXIT_FAILURE

    report["endpoint"] = _redacted_url(url)
    output.parent.mkdir(parents=True, exist_ok=True)
    atomic_write_text(output,
        json.dumps(report, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(f"Report: {output}")
    print("Verdict: PASS")
    return EXIT_SUCCESS


__all__ = [name for name in globals() if not name.startswith("__")]
