from __future__ import annotations
from .run_windows_surface import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase181_custom_ros2_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse generic peer/worker options; profile wrappers supply all normal defaults."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--worker", action="store_true", help="Run inside the selected ROS2 Python environment.")
    parser.add_argument("--role", choices=("windows-local-editor", "linux-peer", "windows-player"), required=True)
    parser.add_argument("--probe-role", choices=PROBE_ROLES, default="orchestrate")
    parser.add_argument("--profile-id", default="")
    parser.add_argument("--surface", choices=("editor", "player"), default="editor")
    parser.add_argument("--distro", choices=("humble", "jazzy", "lyrical"), default="jazzy")
    parser.add_argument("--rmw", default="rmw_fastrtps_cpp")
    parser.add_argument("--domain-id", type=int, default=0)
    parser.add_argument("--discovery-range", choices=("LOCALHOST", "SUBNET", "SYSTEM_DEFAULT", "OFF"), default="SUBNET")
    parser.add_argument("--token", default="")
    parser.add_argument("--workspace", type=pathlib.Path)
    parser.add_argument("--ros2-root", type=pathlib.Path)
    parser.add_argument("--ros2-python", type=pathlib.Path)
    parser.add_argument("--colcon", type=pathlib.Path)
    parser.add_argument("--static-interface-package", type=pathlib.Path)
    parser.add_argument("--unity-log", type=pathlib.Path)
    parser.add_argument("--unity-batch", action="store_true", help="Launch an owned Unity Editor Batch probe for the Editor surface.")
    parser.add_argument("--unity-editor", type=pathlib.Path, help="Absolute Unity.exe path required with --unity-batch.")
    parser.add_argument("--player", type=pathlib.Path)
    parser.add_argument("--player-log", type=pathlib.Path)
    parser.add_argument("--unity-log-offset", type=int, default=0)
    parser.add_argument("--worker-result-json", type=pathlib.Path)
    parser.add_argument("--worker-ready-json", type=pathlib.Path)
    parser.add_argument("--summary-json", type=pathlib.Path)
    parser.add_argument("--interface-digest", default="")
    parser.add_argument("--zenoh-topology-id", default="")
    parser.add_argument("--ready-timeout-seconds", type=float, default=900.0)
    parser.add_argument("--apply-timeout-seconds", type=float, default=120.0)
    return parser.parse_args(argv)
def main(argv: Sequence[str] | None = None) -> int:
    """Run advanced profile arguments; ordinary operators use one named thin wrapper."""

    args = parse_args(argv)
    if args.worker:
        return worker_main(args)
    if args.role == "windows-local-editor":
        return run_windows_local_editor(args)
    if args.role == "windows-player":
        return run_windows_player(args)
    print("Phase181 Linux and Player modes require their dedicated role helpers.", file=sys.stderr)
    return 2
def worker_main(args: argparse.Namespace) -> int:
    """Run the generated-envelope worker and return only one bounded terminal code."""

    try:
        return run_typed_worker(args)
    except protocol.ProtocolFailure as exc:
        if args.worker_result_json is not None:
            write_worker_result(args.worker_result_json, {"phase": 181, "verdict": exc.code, "error": str(exc)})
        print(exc.code, file=sys.stderr)
        return 1
    except Exception:  # noqa: BLE001 - native initialization must never leak an unbounded worker failure.
        if args.worker_result_json is not None:
            write_worker_result(
                args.worker_result_json,
                {"phase": 181, "verdict": "FAIL_PEER_RUNTIME", "error": "unhandled worker runtime failure"},
            )
        print("FAIL_PEER_RUNTIME", file=sys.stderr)
        return 1


__all__ = [name for name in globals() if not name.startswith("__")]
