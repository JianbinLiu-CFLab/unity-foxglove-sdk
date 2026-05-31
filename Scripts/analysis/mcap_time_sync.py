#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Offline MCAP timestamp sync validation helper.

This script checks timestamp coherence for point-cloud and IMU streams:

* Point-cloud topic logTime vs payload timestamp
* IMU payload timestamp vs point-cloud payload timestamp (nearest-neighbor)

Common usage:

    python Scripts/analysis/mcap_time_sync.py
    python Scripts/analysis/mcap_time_sync.py --mcap Unity2Foxglove/Recordings/my_run.mcap
    python Scripts/analysis/mcap_time_sync.py --skip-frames 80
"""

from __future__ import annotations

import argparse
import bisect
import json
import math
from dataclasses import dataclass
from pathlib import Path
from statistics import median

from mcap.reader import make_reader


REPO_ROOT_PARENT_DEPTH = 2
REPO_ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
DEFAULT_RECORDING_DIR = REPO_ROOT / "Unity2Foxglove" / "Recordings"

DEFAULT_IMU_TOPIC = "/imu/data"
DEFAULT_POINT_CLOUD_TOPIC = "/unity/point_cloud_draco"

EXIT_SUCCESS = 0
EXIT_FAILURE = 1


WIRE_VARINT = 0
WIRE_FIXED64 = 1
WIRE_LENGTH_DELIMITED = 2
WIRE_START_GROUP = 3
WIRE_END_GROUP = 4
WIRE_FIXED32 = 5

TIMESTAMP_THRESHOLDS_MS = [0.5, 1, 5, 10, 20, 50, 100, 200, 500, 1000]


@dataclass(frozen=True)
class MessageSamples:
    """Timestamps collected for one topic."""

    topic: str
    log_times_ns: list[int]
    publish_times_ns: list[int]
    payload_times_ns: list[int]


def percent_if(values: list[float], limit_ms: float) -> float:
    """Return percentage of samples whose value is <= limit."""

    if not values:
        return math.nan
    return 100.0 * sum(1 for x in values if x <= limit_ms) / len(values)


def parse_varint(data: bytes, offset: int) -> tuple[int, int]:
    """Parse one protobuf varint from ``data`` at ``offset``."""

    value = 0
    shift = 0

    while offset < len(data):
        byte = data[offset]
        offset += 1
        value |= (byte & 0x7F) << shift
        if byte < 0x80:
            return value, offset
        shift += 7
        if shift >= 64:
            break

    raise ValueError("malformed protobuf varint")


def read_length_prefixed(data: bytes, offset: int) -> tuple[bytes, int]:
    """Read protobuf length-prefixed bytes."""

    length, offset = parse_varint(data, offset)
    end = offset + length
    if length < 0 or end > len(data):
        raise ValueError("malformed protobuf length field")
    return data[offset:end], end


def skip_field(data: bytes, offset: int, wire_type: int) -> int:
    """Skip one protobuf field payload by wire type."""

    if wire_type == WIRE_VARINT:
        _, offset = parse_varint(data, offset)
        return offset

    if wire_type == WIRE_FIXED64:
        return offset + 8

    if wire_type == WIRE_LENGTH_DELIMITED:
        length, offset = parse_varint(data, offset)
        return offset + length

    if wire_type == WIRE_FIXED32:
        return offset + 4

    if wire_type in (WIRE_START_GROUP, WIRE_END_GROUP):
        raise ValueError("unsupported protobuf group wire type")

    raise ValueError(f"unsupported protobuf wire type {wire_type}")


def parse_timestamp_message(timestamp_payload: bytes) -> int | None:
    """Parse a Timestamp-like payload with sec/nsec fields into nanoseconds."""

    sec = None
    nsec = None
    offset = 0

    while offset < len(timestamp_payload):
        tag, offset = parse_varint(timestamp_payload, offset)
        field_number = tag >> 3
        wire_type = tag & 0x07

        if field_number == 1 and wire_type == WIRE_VARINT:
            sec, offset = parse_varint(timestamp_payload, offset)
            continue

        if field_number == 2 and wire_type == WIRE_VARINT:
            nsec, offset = parse_varint(timestamp_payload, offset)
            continue

        if wire_type == WIRE_LENGTH_DELIMITED:
            nested, new_offset = read_length_prefixed(timestamp_payload, offset)
            offset = new_offset
            nested_sec = None
            nested_nsec = None
            n_offset = 0
            while n_offset < len(nested):
                n_tag, n_offset = parse_varint(nested, n_offset)
                n_field = n_tag >> 3
                n_wire = n_tag & 0x07
                if n_field == 1 and n_wire == WIRE_VARINT:
                    nested_sec, n_offset = parse_varint(nested, n_offset)
                elif n_field == 2 and n_wire == WIRE_VARINT:
                    nested_nsec, n_offset = parse_varint(nested, n_offset)
                else:
                    n_offset = skip_field(nested, n_offset, n_wire)
            if nested_sec is not None and nested_nsec is not None:
                sec = nested_sec
                nsec = nested_nsec
            continue

        offset = skip_field(timestamp_payload, offset, wire_type)

    if sec is None or nsec is None:
        return None
    return int(sec) * 1_000_000_000 + int(nsec)


def parse_payload_timestamp_ns(payload: bytes) -> int | None:
    """Extract the first timestamp-like field from a Protobuf payload."""

    offset = 0
    sec = None
    nsec = None

    while offset < len(payload):
        tag, offset = parse_varint(payload, offset)
        field_number = tag >> 3
        wire_type = tag & 0x07

        # Timestamp usually sits in field #1 as a nested length-delimited message.
        if field_number == 1 and wire_type == WIRE_LENGTH_DELIMITED:
            nested, offset = read_length_prefixed(payload, offset)
            parsed = parse_timestamp_message(nested)
            if parsed is not None:
                return parsed
            continue

        # Fallback if a caller sends a raw sec/nsec-style flat payload.
        if field_number == 1 and wire_type == WIRE_VARINT:
            sec, offset = parse_varint(payload, offset)
            continue

        if field_number == 2 and wire_type == WIRE_VARINT:
            nsec, offset = parse_varint(payload, offset)
            continue

        if sec is not None and nsec is not None:
            return int(sec) * 1_000_000_000 + int(nsec)

        offset = skip_field(payload, offset, wire_type)

    if sec is not None and nsec is not None:
        return int(sec) * 1_000_000_000 + int(nsec)

    return None


def latest_mcap_from_default_dir() -> Path:
    """Pick the newest readable MCAP under the default recording directory."""

    matches = list(DEFAULT_RECORDING_DIR.rglob("*.mcap"))
    if not matches:
        raise FileNotFoundError(f"No .mcap files found under {DEFAULT_RECORDING_DIR}")

    latest = max(matches, key=lambda p: p.stat().st_mtime)
    if latest.stat().st_size <= 0:
        raise FileNotFoundError(f"The selected MCAP file is empty: {latest}")
    return latest.resolve()


def resolve_mcap_path(path_or_glob: str | None) -> Path:
    """Resolve explicit path or choose latest MCAP by default."""

    if path_or_glob:
        candidate = Path(path_or_glob)
        if not candidate.is_absolute():
            candidate = REPO_ROOT / candidate
        candidate = candidate.resolve()

        if any(ch in path_or_glob for ch in ("*", "?", "[")):
            pattern = str(candidate)
            parent = candidate.parent
            matches = list(parent.glob(candidate.name))
            if not matches:
                raise FileNotFoundError(f"No MCAP files match: {pattern}")
            return max(matches, key=lambda p: p.stat().st_mtime)

        if not candidate.exists() or candidate.stat().st_size <= 0:
            raise FileNotFoundError(f"MCAP not found or empty: {candidate}")
        return candidate

    return latest_mcap_from_default_dir()


def percentile(values: list[float], ratio: float) -> float:
    """Get a deterministic percentile value for a sorted float list."""

    if not values:
        return math.nan
    idx = int((ratio / 100.0) * (len(values) - 1))
    return values[idx]


def describe_deltas(values: list[float]) -> dict[str, float]:
    """Build a concise statistics dictionary from absolute millisecond deltas."""

    if not values:
        return {
            "count": 0,
            "mean_ms": math.nan,
            "min_ms": math.nan,
            "median_ms": math.nan,
            "p95_ms": math.nan,
            "p99_ms": math.nan,
            "max_ms": math.nan,
            "pct_le": {str(t): math.nan for t in TIMESTAMP_THRESHOLDS_MS},
        }

    sorted_values = sorted(values)
    return {
        "count": len(sorted_values),
        "mean_ms": sum(sorted_values) / len(sorted_values),
        "min_ms": sorted_values[0],
        "median_ms": median(sorted_values),
        "p95_ms": percentile(sorted_values, 95),
        "p99_ms": percentile(sorted_values, 99),
        "max_ms": sorted_values[-1],
        "pct_le": {str(t): percent_if(sorted_values, t) for t in TIMESTAMP_THRESHOLDS_MS},
    }


def nearest_neighbor_deltas_ms(a_ns: list[int], b_ns: list[int]) -> list[float]:
    """Distance from each sample in ``a_ns`` to nearest sample in sorted ``b_ns``."""

    if not b_ns:
        return []

    b_sorted = sorted(b_ns)
    deltas_ms: list[float] = []

    for target in a_ns:
        idx = bisect.bisect_left(b_sorted, target)
        candidates = []
        if idx < len(b_sorted):
            candidates.append(abs(b_sorted[idx] - target))
        if idx > 0:
            candidates.append(abs(b_sorted[idx - 1] - target))

        if not candidates:
            continue

        deltas_ms.append(min(candidates) / 1_000_000.0)

    return deltas_ms


def parse_mcap(mcap_path: Path, imu_topic: str, pointcloud_topic: str) -> dict[str, MessageSamples]:
    """Extract topic timestamps from MCAP records."""

    topic_samples: dict[str, MessageSamples] = {}

    def get(topic: str) -> MessageSamples:
        existing = topic_samples.get(topic)
        if existing is None:
            fresh = MessageSamples(topic=topic, log_times_ns=[], publish_times_ns=[], payload_times_ns=[])
            topic_samples[topic] = fresh
            return fresh
        return existing

    with open(mcap_path, "rb") as f:
        for _schema, channel, message in make_reader(f).iter_messages():
            topic = channel.topic
            if topic not in (imu_topic, pointcloud_topic):
                continue

            samples = get(topic)
            samples.log_times_ns.append(message.log_time)
            samples.publish_times_ns.append(message.publish_time)

            payload_ns = parse_payload_timestamp_ns(message.data)
            if payload_ns is not None:
                samples.payload_times_ns.append(payload_ns)

    return topic_samples


def validate_topics(
    parsed: dict[str, MessageSamples],
    imu_topic: str,
    pointcloud_topic: str,
    skip_frames: int,
) -> dict[str, object]:
    """Build an execution report from parsed topic samples."""

    imu_samples = parsed.get(imu_topic)
    pointcloud_samples = parsed.get(pointcloud_topic)

    report: dict[str, object] = {
        "imu_topic": imu_topic,
        "pointcloud_topic": pointcloud_topic,
    }

    if imu_samples is None:
        report["error"] = f"IMU topic {imu_topic!r} not found"
        return report

    if pointcloud_samples is None:
        report["error"] = f"Point-cloud topic {pointcloud_topic!r} not found"
        return report

    report["counts"] = {
        "imu_messages": len(imu_samples.log_times_ns),
        "pointcloud_messages": len(pointcloud_samples.log_times_ns),
        "imu_payload_parsed": len(imu_samples.payload_times_ns),
        "pointcloud_payload_parsed": len(pointcloud_samples.payload_times_ns),
    }

    pc_log_payload_ms = [
        abs(log - payload) / 1_000_000.0
        for log, payload in zip(pointcloud_samples.log_times_ns, pointcloud_samples.payload_times_ns)
        if payload is not None
    ]
    report["pointcloud_log_minus_payload_ms"] = describe_deltas(pc_log_payload_ms)

    imu_payload_ns = imu_samples.payload_times_ns
    pointcloud_payload_ns = pointcloud_samples.payload_times_ns

    if len(imu_payload_ns) >= 2 and len(pointcloud_payload_ns) >= 2:
        align_input = pointcloud_payload_ns[skip_frames:]
        deltas_ms = nearest_neighbor_deltas_ms(align_input, imu_payload_ns)
        report["imu_vs_pointcloud_payload_delta_ms"] = {
            "skipped_frames": skip_frames,
            "stats": describe_deltas(deltas_ms),
        }
    else:
        report["imu_vs_pointcloud_payload_delta_ms"] = {
            "skipped_frames": skip_frames,
            "stats": describe_deltas([]),
        }

    return report


def print_report(report: dict[str, object], mcap_path: Path, write_json: str | None = None) -> None:
    """Print human-readable report and optional JSON section."""

    print(f"mcap: {mcap_path}")
    if "error" in report:
        print(f"error: {report['error']}")
        return

    counts = report["counts"]
    print("---")
    print(f"imu topic: {report['imu_topic']}")
    print(f"pointcloud topic: {report['pointcloud_topic']}")
    print(f"imu messages: {counts['imu_messages']} (payload parsed={counts['imu_payload_parsed']})")
    print(
        f"pointcloud messages: {counts['pointcloud_messages']} (payload parsed={counts['pointcloud_payload_parsed']})"
    )

    print("---")
    stats_pc = report["pointcloud_log_minus_payload_ms"]
    print("pointcloud log_time - payload_time (ms):")
    print(
        "  count: {count}, mean/min/median/p95/p99/max: "
        "{mean_ms:.3f}/{min_ms:.3f}/{median_ms:.3f}/{p95_ms:.3f}/{p99_ms:.3f}/{max_ms:.3f}".format(
            **stats_pc
        )
    )

    stats_sync = report["imu_vs_pointcloud_payload_delta_ms"]["stats"]
    print("imu payload time vs pointcloud payload time (nearest IMU, nearest-neighbor):")
    print(f"  skipped_frames: {report['imu_vs_pointcloud_payload_delta_ms']['skipped_frames']}")
    print(
        "  count: {count}, mean/min/median/p95/p99/max: "
        "{mean_ms:.3f}/{min_ms:.3f}/{median_ms:.3f}/{p95_ms:.3f}/{p99_ms:.3f}/{max_ms:.3f}".format(
            **stats_sync
        )
    )
    print("  thresholds (<= ms):")
    for k, v in sorted(stats_sync["pct_le"].items(), key=lambda x: float(x[0])):
        print(f"    {k}: {v:.1f}%")

    if write_json:
        with open(write_json, "w", encoding="utf-8") as file:
            json.dump(report, file, ensure_ascii=False, indent=2)
        print(f"json report: {write_json}")


def parse_args() -> argparse.Namespace:
    """Parse CLI arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "mcap",
        nargs="?",
        help="MCAP file path (or glob). Defaults to latest under Unity2Foxglove/Recordings.",
    )
    parser.add_argument("--imu-topic", default=DEFAULT_IMU_TOPIC, help=f"Default: {DEFAULT_IMU_TOPIC}")
    parser.add_argument(
        "--pointcloud-topic",
        default=DEFAULT_POINT_CLOUD_TOPIC,
        help=f"Default: {DEFAULT_POINT_CLOUD_TOPIC}",
    )
    parser.add_argument(
        "--skip-frames",
        type=int,
        default=0,
        help="Skip first N pointcloud frames for IMU-vs-pointcloud alignment stats.",
    )
    parser.add_argument(
        "--json",
        dest="json_out",
        help="Optional path to store a JSON copy of the same report.",
    )
    return parser.parse_args()


def main() -> int:
    """CLI entrypoint."""

    args = parse_args()

    try:
        mcap_path = resolve_mcap_path(args.mcap)
    except FileNotFoundError as exc:
        print(f"error: {exc}")
        return EXIT_FAILURE

    parsed = parse_mcap(mcap_path, args.imu_topic, args.pointcloud_topic)
    report = validate_topics(parsed, args.imu_topic, args.pointcloud_topic, args.skip_frames)
    print_report(report, mcap_path, args.json_out)

    if "error" in report:
        return EXIT_FAILURE
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main())

