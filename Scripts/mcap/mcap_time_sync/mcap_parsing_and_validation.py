from __future__ import annotations
from .mcap_resolution_and_statistics import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "mcap_time_sync.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

def parse_mcap(mcap_path: Path, imu_topic: str, pointcloud_topic: str) -> dict[str, MessageSamples]:
    """Extract topic timestamps from MCAP records."""

    topic_samples: dict[str, MessageSamples] = {}

    def get(topic: str) -> MessageSamples:
        """Get the sample container for ``topic`` (and create it if missing)."""

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

            samples.payload_times_ns.append(parse_payload_timestamp_ns(message.data))

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
        "imu_payload_parsed": sum(1 for value in imu_samples.payload_times_ns if value is not None),
        "pointcloud_payload_parsed": sum(1 for value in pointcloud_samples.payload_times_ns if value is not None),
    }

    pc_log_payload_ms = [
        abs(log - payload) / 1_000_000.0
        for log, payload in zip(pointcloud_samples.log_times_ns, pointcloud_samples.payload_times_ns)
        if payload is not None
    ]
    report["pointcloud_log_minus_payload_ms"] = describe_deltas(pc_log_payload_ms)

    imu_payload_ns = [value for value in imu_samples.payload_times_ns if value is not None]
    pointcloud_payload_ns = [value for value in pointcloud_samples.payload_times_ns if value is not None]

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


__all__ = [name for name in globals() if not name.startswith("__")]
