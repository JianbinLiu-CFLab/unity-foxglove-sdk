from __future__ import annotations
from .wait_for_evidence_and_require_no_output import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase185_foxrun_messagepack_probe.py"
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
        default="build/phase185/probe/report.json",
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
    return parser.parse_args()
def main() -> int:
    """Run the live probe and write a bounded PASS or FAIL report."""
    args = parse_args()
    url = _build_url(args)
    output = Path(args.output)
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
