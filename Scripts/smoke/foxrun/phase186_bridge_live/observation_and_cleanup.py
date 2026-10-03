from __future__ import annotations
from .live_execution import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_live.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _build_observations(
    config: Mapping[str, Any],
    worker_results: Mapping[str, Mapping[str, Any]],
) -> dict[str, dict[str, Any]]:
    """Build observations only from evidence files that still exist."""
    result: dict[str, dict[str, Any]] = {}
    case_id = str(config["caseId"])
    for name in sorted(protocol.CASES[case_id].required_observations):
        path = _observation_path(config, worker_results, name).resolve()
        try:
            present = path.is_file() and path.stat().st_size > 0
        except OSError:
            present = False
        if not present:
            raise LiveFailure(
                "FAIL_EVIDENCE",
                f"live evidence file is absent for {name}",
            )
        result[name] = {
            "observed": True,
            "source": _observation_source(case_id, name),
            "path": str(path),
        }
    return result


def _observation_source(case_id: str, name: str) -> str:
    """Handle observation source for Phase186 acceptance."""
    if case_id == "bounds-hostile-peer" and name == "data":
        return "live-hostile-frame-rejection"
    return {
        "unity": "live-unity-editor",
        "bridge": "live-sidecar-health",
        "peer": "live-independent-peer",
        "graph": "live-rclpy-graph-api",
        "qos": "live-rclpy-endpoint-info",
        "data": "live-correlated-payload",
        "origin": "live-publisher-origin",
        "resources": "live-owned-cleanup",
        "packages": "current-package-composition",
    }[name]


def _wait_until_port_released(host: str, port: int) -> None:
    """Wait for until port released."""
    deadline = time.monotonic() + 15.0
    while time.monotonic() < deadline:
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
                if os.name == "nt":
                    probe.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
                probe.bind((host, port))
                return
        except OSError:
            time.sleep(0.05)
    raise LiveFailure("FAIL_CLEANUP", "sidecar port was not released for reconnect")


__all__ = [name for name in globals() if not name.startswith("__")]
