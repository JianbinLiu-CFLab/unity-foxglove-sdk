#!/usr/bin/env python3
"""Validate the Phase139C Remote Data Loader curve-only workflow.

This helper intentionally treats the Unity cursor bridge as a separate optional
channel.  Foxglove Remote Data Loader range requests prove file-backed curve
inspection, but they are not a reliable current-playhead signal for driving the
Unity scene.
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import subprocess
import sys
from pathlib import Path


def load_phase139b_helper():
    """Load the sibling Phase139B helper without depending on ambient sys.path."""

    helper_path = Path(__file__).resolve().with_name("phase139b_remote_data_loader_acceptance.py")
    spec = importlib.util.spec_from_file_location("phase139b_remote_data_loader_acceptance_for_phase139c", helper_path)
    if spec is None or spec.loader is None:
        raise ImportError(f"Could not load Phase139B helper from {helper_path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


phase139b = load_phase139b_helper()


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command-line arguments for curve-only DataLoader acceptance."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", default="curve-only", choices=["curve-only"],
                        help="Acceptance mode; cursor bridge control is intentionally out of scope.")
    parser.add_argument("--mcap", help="MCAP file to serve through a temporary Phase139B backend.")
    parser.add_argument("--base-url", help="Probe an already-running Remote Data Loader backend.")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=0, help="Backend port; 0 selects a free loopback port.")
    parser.add_argument("--source-id", default="local-mcap")
    parser.add_argument("--name", default="Unity2Foxglove MCAP")
    parser.add_argument("--token", default="")
    parser.add_argument("--json-out", default="build/phase139c/dataloader-curve-only.json")
    parser.add_argument("--range-out", default="build/phase139c/dataloader-range.mcap")
    parser.add_argument("--range-seconds", type=float, default=1.0,
                        help="Small data window requested from the manifest start.")
    parser.add_argument("--full-range", action="store_true",
                        help="Request the full manifest time range instead of a bounded window.")
    parser.add_argument("--max-data-bytes", type=int, default=None,
                        help="Override the temporary backend's in-memory response cap.")
    parser.add_argument("--timeout", type=float, default=10.0)
    parser.add_argument("--startup-timeout", type=float, default=90.0)
    return parser.parse_args(argv)


def probe_remote_data_loader(args: argparse.Namespace, root: Path) -> dict:
    """Launch or attach to the Phase139B backend and verify DataLoader endpoints."""
    process: subprocess.Popen | None = None
    backend_logs: list[str] = []
    try:
        if args.base_url:
            base_url = args.base_url.rstrip("/")
        else:
            process, base_url, backend_logs = phase139b.launch_backend(args, root)

        manifest_url = base_url + "/v1/manifest"
        status, content_type, body = phase139b.read_url(manifest_url, args.token, args.timeout)
        if status != 200:
            raise RuntimeError(f"GET /v1/manifest returned {status}: {body.decode('utf-8', 'replace')}")

        manifest = json.loads(body.decode("utf-8"))
        sources = manifest.get("sources") or []
        if not sources:
            raise RuntimeError("Manifest contained no sources.")

        range_seconds = None if args.full_range else args.range_seconds
        data_url = phase139b.build_data_url(base_url, sources[0], range_seconds)
        data_status, data_content_type, data_body = phase139b.read_url(data_url, args.token, args.timeout)
        if data_status != 200:
            raise RuntimeError(f"GET /v1/data returned {data_status}: {data_body.decode('utf-8', 'replace')}")
        if not (data_body.startswith(phase139b.MCAP_MAGIC) and data_body.endswith(phase139b.MCAP_MAGIC)):
            raise RuntimeError("Downloaded data response is not a finalized MCAP stream.")

        remote_file_url = phase139b.build_remote_file_url(base_url, sources[0], args.source_id)
        file_status, file_content_type, file_body = phase139b.read_url(
            remote_file_url,
            args.token,
            args.timeout,
            headers={"Range": "bytes=0-7"},
        )
        if file_status != 206:
            raise RuntimeError(f"GET direct .mcap range returned {file_status}: {file_body.decode('utf-8', 'replace')}")
        if file_body != phase139b.MCAP_MAGIC:
            raise RuntimeError("Direct .mcap endpoint did not return MCAP magic for bytes=0-7.")

        range_out = (root / args.range_out).resolve()
        range_out.parent.mkdir(parents=True, exist_ok=True)
        range_out.write_bytes(data_body)

        relevant_logs = [
            line for line in backend_logs
            if line.startswith("PHASE139B_SERVER_READY=")
            or "error " in line.lower()
            or "unhandled exception" in line.lower()
        ]

        return {
            "base_url": base_url,
            "manifest": {
                "url": manifest_url,
                "content_type": content_type,
                "source_count": len(sources),
                "first_source_id": sources[0].get("id"),
                "first_source_start_time": sources[0].get("startTime"),
                "first_source_end_time": sources[0].get("endTime"),
                "first_source_topics": [topic.get("name") for topic in sources[0].get("topics", [])],
            },
            "data": {
                "url": data_url,
                "content_type": data_content_type,
                "bytes": len(data_body),
                "range_out": str(range_out),
                "requested_range_seconds": range_seconds,
                "mcap_magic_ok": True,
            },
            "remote_file": {
                "url": remote_file_url,
                "content_type": file_content_type,
                "range_status": file_status,
                "range_mcap_magic_ok": True,
                "foxglove_source": "Remote files",
            },
            "backend_logs_tail": relevant_logs[-20:],
        }
    finally:
        phase139b.stop_backend(process)


def build_manual_workflow(remote: dict) -> dict:
    """Describe the Foxglove-side evidence expected from the probed backend."""
    manifest_url = remote["manifest"]["url"]
    remote_file_url = remote["remote_file"]["url"]
    topics = remote["manifest"]["first_source_topics"]
    return {
        "connect_to": {
            "foxglove_source": "Remote files",
            "remote_file_url": remote_file_url,
        },
        "backend_contract": {
            "manifest_url": manifest_url,
            "note": "/v1/manifest remains the backend contract endpoint; Foxglove's stock Remote files dialog expects the direct .mcap URL.",
        },
        "expected_observations": [
            "Foxglove accepts the direct .mcap URL in the Remote files dialog.",
            "Plot shows a continuous curve from file-backed history after loading.",
            "Scrubbing the Foxglove timeline localizes the Plot cursor within the loaded range.",
            "3D and image panels render from file data without requiring Unity Play Mode.",
        ],
        "topics_seen_by_backend": topics,
    }


def main(argv: list[str]) -> int:
    """Run Phase139C curve-only acceptance and write JSON evidence."""
    args = parse_args(argv)
    if not args.base_url and not args.mcap:
        raise SystemExit("--mcap is required unless --base-url is provided.")

    root = phase139b.repo_root()
    remote = probe_remote_data_loader(args, root)

    evidence = {
        "phase": "139C",
        "status": "pass",
        "mode": args.mode,
        "remote_data_loader": remote,
        "foxglove_manual_acceptance": build_manual_workflow(remote),
        "cursor_bridge": {
            "status": "optional_not_exercised",
            "note": (
                "Cursor bridge remains a separate optional control channel. "
                "Remote Data Loader /v1/data requests are cache/range requests, "
                "not a dependable Unity playhead signal."
            ),
        },
        "limitations": [
            "This smoke script does not drive the Foxglove UI.",
            "A human should confirm the continuous Plot curve and scrub-localized cursor in Foxglove.",
        ],
    }

    json_out = (root / args.json_out).resolve()
    json_out.parent.mkdir(parents=True, exist_ok=True)
    json_out.write_text(json.dumps(evidence, indent=2, sort_keys=True), encoding="utf-8")
    print(json.dumps(evidence, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
