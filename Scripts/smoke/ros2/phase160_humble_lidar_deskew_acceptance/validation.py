#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from .foundation import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase160_humble_lidar_deskew_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def required_tf_keys(fixed_frame: str, base_frame: str, sensor_frame: str, point_frame: str) -> list[str]:
    """Return the expected dynamic TF chain."""

    return [
        f"{fixed_frame}->{base_frame}",
        f"{base_frame}->{sensor_frame}",
        f"{sensor_frame}->{point_frame}",
    ]


def validate_summary(
    summary: dict[str, Any],
    raw_topic: str,
    deskewed_topic: str,
    required_clouds: str,
    fixed_frame: str,
    base_frame: str,
    sensor_frame: str,
    point_frame: str,
    allow_static: bool,
    motion_threshold_m: float,
) -> None:
    """Validate direct PointCloud2 + TF evidence."""

    point_clouds = summary.get("pointClouds", {})
    if required_clouds == "raw":
        required_topics = (raw_topic,)
    elif required_clouds == "deskewed":
        required_topics = (deskewed_topic,)
    elif required_clouds == "both":
        required_topics = (raw_topic, deskewed_topic)
    else:
        raw_count = int((point_clouds.get(raw_topic) or {}).get("count") or 0)
        deskewed_count = int((point_clouds.get(deskewed_topic) or {}).get("count") or 0)
        if raw_count <= 0 and deskewed_count <= 0:
            raise AcceptanceError(
                f"Neither PointCloud2 topic produced samples: {raw_topic}, {deskewed_topic}."
            )
        required_topics = tuple(
            topic for topic in (raw_topic, deskewed_topic)
            if int((point_clouds.get(topic) or {}).get("count") or 0) > 0
        )

    for topic in required_topics:
        record = point_clouds.get(topic) or {}
        if int(record.get("count") or 0) <= 0:
            raise AcceptanceError(f"PointCloud2 topic {topic} produced no samples.")
        last = record.get("last") or {}
        frame_id = (last.get("frame_id") or "").strip("/")
        if frame_id != point_frame:
            raise AcceptanceError(
                f"PointCloud2 topic {topic} frame_id is {frame_id!r}, expected {point_frame!r}."
            )
        data_len = int(last.get("data_len") or 0)
        width = int(last.get("width") or 0)
        if data_len <= 0 or width <= 0:
            raise AcceptanceError(
                f"PointCloud2 topic {topic} has empty payload width={width} data_len={data_len}."
            )

    transforms = summary.get("transforms", {})
    for key in required_tf_keys(fixed_frame, base_frame, sensor_frame, point_frame):
        if key not in transforms:
            available = ", ".join(sorted(transforms)) or "<none>"
            raise AcceptanceError(f"Missing TF edge {key}; available TF edges: {available}")

    base_key = f"{fixed_frame}->{base_frame}"
    base_record = transforms[base_key]
    base_delta = float(base_record.get("translationDeltaM") or 0.0)
    if not allow_static and base_delta < motion_threshold_m:
        raise AcceptanceError(
            f"TF edge {base_key} did not move enough: delta={base_delta:.4f} m "
            f"< threshold={motion_threshold_m:.4f} m. Move the vehicle for motion acceptance, "
            "or omit --require-motion for static runtime wiring smoke."
        )


def print_summary(summary: dict[str, Any]) -> None:
    """Print a compact human-readable evidence summary."""

    print("[phase160-humble] Direct subscriber evidence:")
    for topic, record in sorted((summary.get("pointClouds") or {}).items()):
        last = record.get("last") or {}
        print(
            f"  PointCloud2 {topic}: samples={record.get('count')} "
            f"frame={last.get('frame_id')} width={last.get('width')} data_len={last.get('data_len')} "
            f"stamp={last.get('stamp')}"
        )
    for key, record in sorted((summary.get("transforms") or {}).items()):
        print(
            f"  TF {key}: samples={record.get('count')} "
            f"translationDeltaM={float(record.get('translationDeltaM') or 0.0):.4f} "
            f"stampDeltaSeconds={float(record.get('stampDeltaSeconds') or 0.0):.3f}"
        )


def synthetic_summary() -> dict[str, Any]:
    """Return a representative moving vehicle summary for self-test."""

    fields = [
        {"name": "x", "offset": 0, "datatype": 7, "count": 1},
        {"name": "y", "offset": 4, "datatype": 7, "count": 1},
        {"name": "z", "offset": 8, "datatype": 7, "count": 1},
    ]
    point = {
        "frame_id": DEFAULT_POINT_FRAME,
        "stamp": {"sec": 10, "nanosec": 0},
        "height": 1,
        "width": 128,
        "point_step": 16,
        "row_step": 2048,
        "is_dense": True,
        "data_len": 2048,
        "fields": fields,
    }
    edge = {
        "count": 2,
        "first": {"translation": {"x": 0, "y": 0, "z": 0}},
        "last": {"translation": {"x": 0.2, "y": 0, "z": 0}},
        "translationDeltaM": 0.2,
        "stampDeltaSeconds": 1.0,
    }
    static_edge = {
        "count": 2,
        "first": {"translation": {"x": 0, "y": 0, "z": 0}},
        "last": {"translation": {"x": 0, "y": 0, "z": 0}},
        "translationDeltaM": 0.0,
        "stampDeltaSeconds": 1.0,
    }
    return {
        "pointClouds": {
            DEFAULT_RAW_TOPIC: {"count": 1, "first": point, "last": point},
            DEFAULT_DESKEWED_TOPIC: {"count": 1, "first": point, "last": point},
        },
        "transforms": {
            f"{DEFAULT_FIXED_FRAME}->{DEFAULT_BASE_FRAME}": edge,
            f"{DEFAULT_BASE_FRAME}->{DEFAULT_SENSOR_FRAME}": static_edge,
            f"{DEFAULT_SENSOR_FRAME}->{DEFAULT_POINT_FRAME}": static_edge,
        },
    }


def run_self_test() -> int:
    """Run validation logic without ROS2."""

    summary = synthetic_summary()
    validate_summary(
        summary,
        DEFAULT_RAW_TOPIC,
        DEFAULT_DESKEWED_TOPIC,
        "both",
        DEFAULT_FIXED_FRAME,
        DEFAULT_BASE_FRAME,
        DEFAULT_SENSOR_FRAME,
        DEFAULT_POINT_FRAME,
        allow_static=False,
        motion_threshold_m=0.02,
    )
    print_summary(summary)
    print("[phase160-humble] Self-test passed.")
    return 0

__all__ = [name for name in globals() if not name.startswith("__")]
