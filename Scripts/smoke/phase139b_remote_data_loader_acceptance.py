#!/usr/bin/env python3
"""Probe the Phase139B Remote Data Loader backend and save evidence.

The helper can either attach to an already-running backend or launch the
test-runner-hosted loopback server, then verifies the manifest and downloads a
small MCAP range.  It is intended for manual acceptance evidence, not as a
replacement for the repository's C# validation suite.
"""

from __future__ import annotations

import argparse
import calendar
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import socket
from pathlib import Path


MCAP_MAGIC = b"\x89MCAP0\r\n"
NANOSECONDS_PER_SECOND = 1_000_000_000


def repo_root() -> Path:
    """Return the repository root from this script location."""
    return Path(__file__).resolve().parents[2]


def read_url(url: str, token: str, timeout: float) -> tuple[int, str, bytes]:
    """Read one HTTP URL and return status, content type, and body."""
    request = urllib.request.Request(url)
    if token:
        request.add_header("Authorization", "Bearer " + token)

    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return response.status, response.headers.get("Content-Type", ""), response.read()
    except urllib.error.HTTPError as exc:
        return exc.code, exc.headers.get("Content-Type", ""), exc.read()


def parse_iso_utc_ns(value: str) -> int:
    """Parse the backend's UTC ISO timestamp without losing nanoseconds."""
    if not value.endswith("Z"):
        raise ValueError(f"Expected UTC timestamp ending with Z: {value}")
    body = value[:-1]
    seconds, _, fraction = body.partition(".")
    parsed = time.strptime(seconds, "%Y-%m-%dT%H:%M:%S")
    unix_seconds = int(calendar.timegm(parsed))
    fraction_ns = int((fraction + "000000000")[:9]) if fraction else 0
    return unix_seconds * NANOSECONDS_PER_SECOND + fraction_ns


def format_iso_utc_ns(value: int) -> str:
    """Format a Unix nanosecond timestamp as compact UTC ISO 8601."""
    seconds, nanos = divmod(int(value), NANOSECONDS_PER_SECOND)
    base = time.strftime("%Y-%m-%dT%H:%M:%S", time.gmtime(seconds))
    if nanos == 0:
        return base + "Z"
    fraction = f"{nanos:09d}".rstrip("0")
    return f"{base}.{fraction}Z"


def build_data_url(base_url: str, source: dict, range_seconds: float | None) -> str:
    """Build a concrete /v1/data URL from a manifest source entry."""
    source_url = source.get("url") or "/v1/data"
    absolute = urllib.parse.urljoin(base_url.rstrip("/") + "/", source_url)
    parsed = urllib.parse.urlparse(absolute)
    query = dict(urllib.parse.parse_qsl(parsed.query, keep_blank_values=True))

    source_start = source.get("startTime")
    source_end = source.get("endTime")
    if source_start and "startTime" not in query:
        query["startTime"] = source_start
    if source_end and "endTime" not in query:
        query["endTime"] = source_end

    if range_seconds is not None and source_start and source_end:
        start_ns = parse_iso_utc_ns(source_start)
        end_ns = parse_iso_utc_ns(source_end)
        requested_end = start_ns + max(0, int(range_seconds * NANOSECONDS_PER_SECOND))
        query["startTime"] = source_start
        query["endTime"] = format_iso_utc_ns(min(end_ns, requested_end))

    encoded = urllib.parse.urlencode(query)
    return urllib.parse.urlunparse(parsed._replace(query=encoded))


def resolve_mcap_path(value: str, root: Path) -> Path:
    """Resolve MCAP paths from either repo-root or Unity-project-relative input."""
    candidate = Path(value)
    if candidate.is_absolute():
        return candidate

    rooted = (root / candidate).resolve()
    if rooted.exists():
        return rooted

    unity_rooted = (root / "Unity2Foxglove" / candidate).resolve()
    if unity_rooted.exists():
        return unity_rooted

    return rooted


def launch_backend(args: argparse.Namespace, root: Path) -> tuple[subprocess.Popen, str, list[str]]:
    """Launch the test-runner-hosted 139B backend and return process/base URL/logs."""
    # Keep dotnet outputs under the ignored repo-level build tree so package
    # source folders never receive bin/obj artifacts during smoke testing.
    build_root = root / "build" / "phase139b" / "dotnet" / ("run-" + str(os.getpid()) + "-" + str(int(time.time() * 1000)))
    out_dir = build_root / "out"
    obj_dir = build_root / "obj"
    out_dir.mkdir(parents=True, exist_ok=True)
    obj_dir.mkdir(parents=True, exist_ok=True)

    project = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "FoxgloveSdk.Tests.csproj"
    command = [
        "dotnet",
        "run",
        "--project",
        str(project),
        "-p:BaseOutputPath=" + str(out_dir) + os.sep,
        "-p:BaseIntermediateOutputPath=" + str(obj_dir) + os.sep,
        "--",
        "--phase139b-remote-data-loader-server",
        "--mcap",
        str(resolve_mcap_path(args.mcap, root)),
        "--host",
        args.host,
        "--port",
        str(args.port if args.port > 0 else find_free_loopback_port()),
        "--source-id",
        args.source_id,
        "--name",
        args.name,
    ]
    if args.max_data_bytes is not None:
        command.extend(["--max-data-bytes", str(args.max_data_bytes)])
    if args.token:
        command.extend(["--token", args.token])

    process = subprocess.Popen(
        command,
        cwd=str(root),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        bufsize=1,
    )

    logs: list[str] = []
    deadline = time.monotonic() + args.startup_timeout
    while time.monotonic() < deadline:
        line = process.stdout.readline() if process.stdout else ""
        if line:
            line = line.rstrip()
            logs.append(line)
            if line.startswith("PHASE139B_SERVER_READY="):
                payload = json.loads(line.split("=", 1)[1])
                return process, payload["baseUrl"], logs

        if process.poll() is not None:
            break

    if process.poll() is None:
        process.terminate()
    raise RuntimeError("Phase139B backend did not become ready. Logs:\n" + "\n".join(logs[-40:]))


def stop_backend(process: subprocess.Popen | None) -> None:
    """Stop a backend process started by this script."""
    if process is None or process.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(
            ["taskkill", "/PID", str(process.pid), "/T", "/F"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        process.wait(timeout=5)
        return

    process.terminate()
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=5)


def find_free_loopback_port() -> int:
    """Reserve and release one loopback TCP port for the temporary backend."""
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
        probe.bind(("127.0.0.1", 0))
        return int(probe.getsockname()[1])


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mcap", help="MCAP file to serve through a temporary backend.")
    parser.add_argument("--base-url", help="Probe an already-running Phase139B backend instead of starting one.")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=0, help="Backend port; 0 selects a free loopback port.")
    parser.add_argument("--source-id", default="local-mcap")
    parser.add_argument("--name", default="Unity2Foxglove MCAP")
    parser.add_argument("--token", default="")
    parser.add_argument("--json-out", default="build/phase139b/remote-data-loader.json")
    parser.add_argument("--range-out", default="build/phase139b/range.mcap")
    parser.add_argument("--range-seconds", type=float, default=1.0,
                        help="Data window to request from the manifest start; use --full-range to request the whole recording.")
    parser.add_argument("--full-range", action="store_true", help="Request the full manifest time range.")
    parser.add_argument("--max-data-bytes", type=int, default=None,
                        help="Override the temporary backend's in-memory response cap.")
    parser.add_argument("--timeout", type=float, default=10.0)
    parser.add_argument("--startup-timeout", type=float, default=90.0)
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    """Run the Phase139B HTTP acceptance probe."""
    args = parse_args(argv)
    if not args.base_url and not args.mcap:
        raise SystemExit("--mcap is required unless --base-url is provided.")

    root = repo_root()
    process: subprocess.Popen | None = None
    backend_logs: list[str] = []
    try:
        if args.base_url:
            base_url = args.base_url.rstrip("/")
        else:
            process, base_url, backend_logs = launch_backend(args, root)

        manifest_url = base_url + "/v1/manifest"
        status, content_type, body = read_url(manifest_url, args.token, args.timeout)
        if status != 200:
            raise RuntimeError(f"GET /v1/manifest returned {status}: {body.decode('utf-8', 'replace')}")
        manifest = json.loads(body.decode("utf-8"))
        sources = manifest.get("sources") or []
        if not sources:
            raise RuntimeError("Manifest contained no sources.")

        range_seconds = None if args.full_range else args.range_seconds
        data_url = build_data_url(base_url, sources[0], range_seconds)
        data_status, data_content_type, data_body = read_url(data_url, args.token, args.timeout)
        if data_status != 200:
            raise RuntimeError(f"GET /v1/data returned {data_status}: {data_body.decode('utf-8', 'replace')}")
        if not (data_body.startswith(MCAP_MAGIC) and data_body.endswith(MCAP_MAGIC)):
            raise RuntimeError("Downloaded data response is not a finalized MCAP stream.")

        json_out = (root / args.json_out).resolve()
        range_out = (root / args.range_out).resolve()
        json_out.parent.mkdir(parents=True, exist_ok=True)
        range_out.parent.mkdir(parents=True, exist_ok=True)
        range_out.write_bytes(data_body)

        relevant_backend_logs = [
            line for line in backend_logs
            if line.startswith("PHASE139B_SERVER_READY=")
            or "error " in line.lower()
            or "unhandled exception" in line.lower()
        ]

        evidence = {
            "phase": "139B",
            "status": "pass",
            "base_url": base_url,
            "manifest": {
                "url": manifest_url,
                "content_type": content_type,
                "source_count": len(sources),
                "first_source_id": sources[0].get("id"),
                "first_source_topics": [topic.get("name") for topic in sources[0].get("topics", [])],
                "first_source_start_time": sources[0].get("startTime"),
                "first_source_end_time": sources[0].get("endTime"),
            },
            "data": {
                "url": data_url,
                "content_type": data_content_type,
                "bytes": len(data_body),
                "range_out": str(range_out),
                "requested_range_seconds": range_seconds,
                "mcap_magic_ok": True,
            },
            "backend_logs_tail": relevant_backend_logs[-20:],
        }
        json_out.write_text(json.dumps(evidence, indent=2, sort_keys=True), encoding="utf-8")
        print(json.dumps(evidence, indent=2, sort_keys=True))
        return 0
    finally:
        stop_backend(process)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
