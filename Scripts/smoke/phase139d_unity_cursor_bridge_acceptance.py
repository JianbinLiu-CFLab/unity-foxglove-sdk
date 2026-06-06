#!/usr/bin/env python3
"""Validate the Phase139D Unity cursor bridge scaffold.

This helper does not drive the Foxglove UI.  It provides CI-safe checks for the
extension package and an optional loopback POST probe for a manually enabled
Unity cursor endpoint.  Endpoint mode also reads Unity replay state with GET so
the bidirectional follow mode has a direct state contract.  Remote Data Loader
`/v1/data` requests are deliberately excluded because they are cache/range
traffic, not playhead-control evidence.
"""

from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path


def repo_root() -> Path:
    """Return the repository root that contains this smoke helper."""
    current = Path(__file__).resolve()
    for parent in [current.parent, *current.parents]:
        if (parent / "Packages").exists() and (parent / "Scripts").exists():
            return parent
    raise RuntimeError("Could not locate repository root from smoke helper path.")


def read_text(path: Path) -> str:
    """Read UTF-8 text and surface the path in any failure."""
    try:
        return path.read_text(encoding="utf-8")
    except OSError as exc:
        raise RuntimeError(f"Could not read {path}: {exc}") from exc


def validate_extension_metadata(root: Path) -> dict:
    """Validate extension files and the currentTime watch contract."""
    extension_root = root / "Tools" / "foxglove-extensions" / "unity-cursor-bridge"
    package_json_path = extension_root / "package.json"
    source_path = extension_root / "src" / "index.ts"
    readme_path = extension_root / "README.md"

    package_json = json.loads(read_text(package_json_path))
    source = read_text(source_path)
    readme = read_text(readme_path)

    checks = {
        "package_named": package_json.get("name") == "unity-cursor-bridge",
        "package_has_build_script": "build" in package_json.get("scripts", {}),
        "watches_current_time": 'context.watch("currentTime")' in source,
        "reads_render_state_current_time": "renderState.currentTime" in source,
        "watches_bounds_and_seek": all(
            token in source
            for token in ['context.watch("startTime")', 'context.watch("endTime")', 'context.watch("didSeek")']
        ),
        "keeps_sec_nsec_split": "sec: currentTime.sec" in source and "nsec: currentTime.nsec" in source,
        "surfaces_forwarding_status": "Status:" in source and "Unity rejected sequence" in source,
        "supports_unity_to_foxglove_follow": "seekPlayback" in source and "Follow Unity replay" in source,
        "polls_unity_state_without_echo": "fetchUnityState" in source and "suppressForwardUntilMs" in source,
        "does_not_use_v1_data_as_cursor": "/v1/data" not in source,
        "documents_remote_data_loader_boundary": "/v1/data" in readme and "playhead signal" in readme,
    }

    failed = [name for name, ok in checks.items() if not ok]
    if failed:
        raise RuntimeError("Extension metadata checks failed: " + ", ".join(failed))

    return {
        "extension_root": str(extension_root),
        "package": package_json.get("name"),
        "checks": checks,
    }


def build_cursor_payload(sequence: int, sec: int, nsec: int) -> dict:
    """Build the explicit cursor payload expected by the future Unity endpoint."""
    return {
        "source": "foxglove-unity-cursor-bridge-smoke",
        "sequence": sequence,
        "time": {"sec": sec, "nsec": nsec},
        "mode": "seek",
    }


def post_cursor(url: str, token: str, payload: dict, timeout: float) -> dict:
    """POST one cursor payload to an explicitly enabled loopback endpoint."""
    body = json.dumps(payload, sort_keys=True).encode("utf-8")
    request = urllib.request.Request(url, data=body, method="POST")
    request.add_header("Content-Type", "application/json")
    if token:
        request.add_header("Authorization", "Bearer " + token)

    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            response_body = response.read().decode("utf-8", "replace")
            return {
                "status": response.status,
                "body": response_body,
            }
    except urllib.error.HTTPError as exc:
        return {
            "status": exc.code,
            "body": exc.read().decode("utf-8", "replace"),
        }


def get_unity_state(url: str, token: str, timeout: float) -> dict:
    """GET the current Unity replay cursor state from the loopback endpoint."""
    request = urllib.request.Request(url, method="GET")
    if token:
        request.add_header("Authorization", "Bearer " + token)

    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            body = response.read().decode("utf-8", "replace")
            parsed = json.loads(body)
            return {
                "status": response.status,
                "body": parsed,
            }
    except urllib.error.HTTPError as exc:
        return {
            "status": exc.code,
            "body": exc.read().decode("utf-8", "replace"),
        }


def validate_endpoint_loopback(args: argparse.Namespace) -> dict:
    """Send synthetic cursor updates and read replay state from a running Unity endpoint."""
    if not args.url:
        raise RuntimeError("--url is required for endpoint-loopback mode.")

    unity_state = get_unity_state(args.url, args.token, args.timeout)
    if unity_state["status"] < 200 or unity_state["status"] >= 300:
        raise RuntimeError(f"Cursor GET returned {unity_state['status']}: {unity_state['body']}")
    state_body = unity_state["body"]
    if not isinstance(state_body, dict) or "time" not in state_body:
        raise RuntimeError("Cursor GET did not return a JSON object with split time state.")
    time_body = state_body["time"]
    if not isinstance(time_body, dict) or "sec" not in time_body or "nsec" not in time_body:
        raise RuntimeError("Cursor GET time state must contain sec and nsec.")

    sent = []
    base_sec = args.sec
    for index in range(args.count):
        payload = build_cursor_payload(index + 1, base_sec, args.nsec + index)
        response = post_cursor(args.url, args.token, payload, args.timeout)
        sent.append({"payload": payload, "response": response})
        if response["status"] < 200 or response["status"] >= 300:
            raise RuntimeError(f"Cursor POST returned {response['status']}: {response['body']}")
        time.sleep(args.interval)

    return {
        "url": args.url,
        "unity_state": unity_state,
        "sent": sent,
    }


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse CLI arguments for Phase139D scaffold and endpoint acceptance."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", default="extension-metadata",
                        choices=["extension-metadata", "endpoint-loopback"])
    parser.add_argument("--json-out", default="build/phase139d/cursor-bridge-smoke.json")
    parser.add_argument("--url", help="Unity cursor endpoint URL for endpoint-loopback mode.")
    parser.add_argument("--token", default="")
    parser.add_argument("--sec", type=int, default=1780664658)
    parser.add_argument("--nsec", type=int, default=199413758)
    parser.add_argument("--count", type=int, default=3)
    parser.add_argument("--interval", type=float, default=0.05)
    parser.add_argument("--timeout", type=float, default=5.0)
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    """Run the selected Phase139D smoke mode and write JSON evidence."""
    args = parse_args(argv)
    root = repo_root()

    evidence = {
        "phase": "139D",
        "mode": args.mode,
        "generated_at_unix": time.time(),
        "status": "pass",
        "limitations": [
            "This smoke helper does not install or run Foxglove Desktop extensions.",
            "endpoint-loopback mode requires a manually enabled Unity cursor endpoint.",
        ],
    }

    evidence["extension"] = validate_extension_metadata(root)
    if args.mode == "endpoint-loopback":
        evidence["endpoint_loopback"] = validate_endpoint_loopback(args)

    json_out = (root / args.json_out).resolve()
    json_out.parent.mkdir(parents=True, exist_ok=True)
    json_out.write_text(json.dumps(evidence, indent=2, sort_keys=True), encoding="utf-8")
    print(json.dumps(evidence, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
