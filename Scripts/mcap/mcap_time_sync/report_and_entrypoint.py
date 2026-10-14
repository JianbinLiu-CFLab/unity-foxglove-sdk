"""Offline MCAP timestamp sync validation helper.

This script checks timestamp coherence for point-cloud and IMU streams:

* Point-cloud topic logTime vs payload timestamp
* IMU payload timestamp vs point-cloud payload timestamp (nearest-neighbor)

Common usage:

    python Scripts/mcap/mcap_time_sync.py
    python Scripts/mcap/mcap_time_sync.py --mcap Unity2Foxglove/Recordings/my_run.mcap
    python Scripts/mcap/mcap_time_sync.py --skip-frames 80
"""

from __future__ import annotations
from .mcap_parsing_and_validation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "mcap_time_sync.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

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


__all__ = [name for name in globals() if not name.startswith("__")]
