#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: ROS2-side acceptance helper for Phase138U raw + deskewed PointCloud2 DDS.

"""Validate raw and deskewed Phase138U PointCloud2 topics and launch RViz2.

Start Unity Play Mode first with ROS2 Native (R2FU), PointCloud2 Native mode,
and PointCloud Motion Compensation enabled in RawAndDeskewedTopic mode. Then run:

    python Scripts/smoke/ros2/phase138u_lidar_deskew_rviz2_acceptance.py

The script launches RViz2 by default for visual comparison and uses direct rclpy
subscribers as the hard DDS acceptance gate. Pass --no-rviz when only the DDS
subscription check is needed.
"""

from __future__ import annotations

import argparse
import json
import math
import random
import pathlib
import subprocess
import struct
import sys

import _ros2_windows_env as ros2env
import launch_phase138u_lidar_deskew_rviz2 as rviz2launch


POINTCLOUD2_MSG_TYPE = "sensor_msgs/msg/PointCloud2"


class InconclusiveError(RuntimeError):
    """Raised when DDS is healthy but the capture cannot prove motion deskew."""


POINTCLOUD2_METRIC_HELPERS = r'''
import math
import random
import struct

POINT_FIELD_INT8 = 1
POINT_FIELD_UINT8 = 2
POINT_FIELD_INT16 = 3
POINT_FIELD_UINT16 = 4
POINT_FIELD_INT32 = 5
POINT_FIELD_UINT32 = 6
POINT_FIELD_FLOAT32 = 7
POINT_FIELD_FLOAT64 = 8

POINT_FIELD_FORMATS = {
    POINT_FIELD_INT8: "b",
    POINT_FIELD_UINT8: "B",
    POINT_FIELD_INT16: "h",
    POINT_FIELD_UINT16: "H",
    POINT_FIELD_INT32: "i",
    POINT_FIELD_UINT32: "I",
    POINT_FIELD_FLOAT32: "f",
    POINT_FIELD_FLOAT64: "d",
}


def read_point_field(data, base, field, endian):
    """Read one scalar PointField value from a PointCloud2 data buffer."""

    fmt = POINT_FIELD_FORMATS.get(int(field.datatype))
    if fmt is None:
        return None
    return struct.unpack_from(endian + fmt, data, base + int(field.offset))[0]


def extract_pointcloud2_points(msg, max_points):
    """Extract XYZ, ring, and time fields from a PointCloud2 sample."""

    fields = {field.name: field for field in msg.fields}
    required = ("x", "y", "z")
    if not all(name in fields for name in required):
        return []

    endian = ">" if bool(msg.is_bigendian) else "<"
    point_step = int(msg.point_step)
    if point_step <= 0:
        return []

    count = int(msg.width) * int(msg.height)
    if count <= 0:
        return []

    stride = max(1, count // max(1, int(max_points)))
    points = []
    for point_index in range(0, count, stride):
        base = point_index * point_step
        if base + point_step > len(msg.data):
            break

        x = float(read_point_field(msg.data, base, fields["x"], endian))
        y = float(read_point_field(msg.data, base, fields["y"], endian))
        z = float(read_point_field(msg.data, base, fields["z"], endian))
        if not (math.isfinite(x) and math.isfinite(y) and math.isfinite(z)):
            continue

        ring = 0
        if "ring" in fields:
            ring_value = read_point_field(msg.data, base, fields["ring"], endian)
            if ring_value is not None:
                ring = int(ring_value)

        time_value = float(point_index)
        if "time_offset" in fields:
            time_value = float(read_point_field(msg.data, base, fields["time_offset"], endian))
        elif "t" in fields:
            time_value = float(read_point_field(msg.data, base, fields["t"], endian))

        points.append((x, y, z, ring, time_value, point_index))

    return points


def distance3(a, b):
    """Return Euclidean distance between two extracted PointCloud2 points."""

    dx = a[0] - b[0]
    dy = a[1] - b[1]
    dz = a[2] - b[2]
    return math.sqrt(dx * dx + dy * dy + dz * dz)


def ring_seam_metrics(points, min_ring_points):
    """Measure first/last seam distance for each LiDAR ring."""

    by_ring = {}
    for point in points:
        by_ring.setdefault(point[3], []).append(point)

    seams = []
    for ring, ring_points in by_ring.items():
        if len(ring_points) < int(min_ring_points):
            continue
        ordered = sorted(ring_points, key=lambda item: (item[4], item[5]))
        seams.append(
            {
                "ring": int(ring),
                "points": len(ordered),
                "distance_m": distance3(ordered[0], ordered[-1]),
                "first_time": float(ordered[0][4]),
                "last_time": float(ordered[-1][4]),
            }
        )

    if not seams:
        return {"ring_count": len(by_ring), "measured_rings": 0}

    distances = sorted(item["distance_m"] for item in seams)
    seams_sorted = sorted(seams, key=lambda item: item["distance_m"], reverse=True)
    return {
        "ring_count": len(by_ring),
        "measured_rings": len(seams),
        "median_distance_m": distances[len(distances) // 2],
        "max_distance_m": distances[-1],
        "largest": seams_sorted[:5],
    }


def coordinate_summary(points):
    """Return coordinate bounds and centroid for static/motion detection."""

    if not points:
        return {"available": False}

    min_x = min_y = min_z = float("inf")
    max_x = max_y = max_z = float("-inf")
    sum_x = sum_y = sum_z = 0.0
    min_time = float("inf")
    max_time = float("-inf")
    for point in points:
        x, y, z, _ring, time_value, _point_index = point
        min_x = min(min_x, x)
        min_y = min(min_y, y)
        min_z = min(min_z, z)
        max_x = max(max_x, x)
        max_y = max(max_y, y)
        max_z = max(max_z, z)
        sum_x += x
        sum_y += y
        sum_z += z
        min_time = min(min_time, time_value)
        max_time = max(max_time, time_value)

    count = float(len(points))
    return {
        "available": True,
        "bounds": {
            "x": [float(min_x), float(max_x)],
            "y": [float(min_y), float(max_y)],
            "z": [float(min_z), float(max_z)],
        },
        "centroid": [float(sum_x / count), float(sum_y / count), float(sum_z / count)],
        "time_span": [float(min_time), float(max_time)],
    }


def plane_from_points(a, b, c):
    """Return a normalized plane from three points, or None if degenerate."""

    ux = b[0] - a[0]
    uy = b[1] - a[1]
    uz = b[2] - a[2]
    vx = c[0] - a[0]
    vy = c[1] - a[1]
    vz = c[2] - a[2]
    nx = uy * vz - uz * vy
    ny = uz * vx - ux * vz
    nz = ux * vy - uy * vx
    norm = math.sqrt(nx * nx + ny * ny + nz * nz)
    if norm <= 1e-9:
        return None
    nx /= norm
    ny /= norm
    nz /= norm
    d = -(nx * a[0] + ny * a[1] + nz * a[2])
    return nx, ny, nz, d


def fit_plane_metrics(points, threshold_m, vertical_only):
    """Fit a dominant plane with deterministic RANSAC and return residuals."""

    if len(points) < 32:
        return {"available": False, "reason": "not enough points"}

    rng = random.Random(138)
    sample = points
    max_plane_points = 2500
    if len(sample) > max_plane_points:
        stride = max(1, len(sample) // max_plane_points)
        sample = sample[::stride][:max_plane_points]
    best = None
    iterations = 140
    threshold = float(threshold_m)
    for _ in range(iterations):
        a, b, c = rng.sample(sample, 3)
        plane = plane_from_points(a, b, c)
        if plane is None:
            continue
        nx, ny, nz, d = plane
        if vertical_only and abs(nz) > 0.45:
            continue

        inliers = 0
        sq_sum = 0.0
        for point in sample:
            residual = abs(nx * point[0] + ny * point[1] + nz * point[2] + d)
            if residual <= threshold:
                inliers += 1
                sq_sum += residual * residual
        if inliers == 0:
            continue
        rms = math.sqrt(sq_sum / inliers)
        candidate = (inliers, -rms, plane, rms)
        if best is None or candidate > best:
            best = candidate

    if best is None:
        return {"available": False, "reason": "no plane found"}

    inliers, neg_rms, plane, rms = best
    nx, ny, nz, d = plane
    return {
        "available": True,
        "inlier_count": int(inliers),
        "inlier_ratio": float(inliers / len(sample)),
        "rms_m": float(rms),
        "normal": [float(nx), float(ny), float(nz)],
        "d": float(d),
        "threshold_m": threshold,
    }


def compute_pointcloud2_metrics(msg, max_metric_points, plane_threshold_m, min_ring_points):
    """Compute motion-sensitive PointCloud2 geometry metrics."""

    points = extract_pointcloud2_points(msg, max_metric_points)
    metrics = {
        "parsed_point_count": len(points),
        "max_metric_points": int(max_metric_points),
        "coordinates": coordinate_summary(points),
        "ring_seam": ring_seam_metrics(points, min_ring_points),
    }
    metrics["best_vertical_plane"] = fit_plane_metrics(points, plane_threshold_m, vertical_only=True)
    metrics["best_any_plane"] = fit_plane_metrics(points, plane_threshold_m, vertical_only=False)
    return metrics
'''




# In-process metric implementation used by the no-ROS self-test.
POINT_FIELD_INT8 = 1

POINT_FIELD_UINT8 = 2

POINT_FIELD_INT16 = 3

POINT_FIELD_UINT16 = 4

POINT_FIELD_INT32 = 5

POINT_FIELD_UINT32 = 6

POINT_FIELD_FLOAT32 = 7

POINT_FIELD_FLOAT64 = 8

POINT_FIELD_FORMATS = {POINT_FIELD_INT8: 'b', POINT_FIELD_UINT8: 'B', POINT_FIELD_INT16: 'h', POINT_FIELD_UINT16: 'H', POINT_FIELD_INT32: 'i', POINT_FIELD_UINT32: 'I', POINT_FIELD_FLOAT32: 'f', POINT_FIELD_FLOAT64: 'd'}

def read_point_field(data, base, field, endian):
    """Read one scalar PointField value from a PointCloud2 data buffer."""
    fmt = POINT_FIELD_FORMATS.get(int(field.datatype))
    if fmt is None:
        return None
    return struct.unpack_from(endian + fmt, data, base + int(field.offset))[0]

def extract_pointcloud2_points(msg, max_points):
    """Extract XYZ, ring, and time fields from a PointCloud2 sample."""
    fields = {field.name: field for field in msg.fields}
    required = ('x', 'y', 'z')
    if not all((name in fields for name in required)):
        return []
    endian = '>' if bool(msg.is_bigendian) else '<'
    point_step = int(msg.point_step)
    if point_step <= 0:
        return []
    count = int(msg.width) * int(msg.height)
    if count <= 0:
        return []
    stride = max(1, count // max(1, int(max_points)))
    points = []
    for point_index in range(0, count, stride):
        base = point_index * point_step
        if base + point_step > len(msg.data):
            break
        x = float(read_point_field(msg.data, base, fields['x'], endian))
        y = float(read_point_field(msg.data, base, fields['y'], endian))
        z = float(read_point_field(msg.data, base, fields['z'], endian))
        if not (math.isfinite(x) and math.isfinite(y) and math.isfinite(z)):
            continue
        ring = 0
        if 'ring' in fields:
            ring_value = read_point_field(msg.data, base, fields['ring'], endian)
            if ring_value is not None:
                ring = int(ring_value)
        time_value = float(point_index)
        if 'time_offset' in fields:
            time_value = float(read_point_field(msg.data, base, fields['time_offset'], endian))
        elif 't' in fields:
            time_value = float(read_point_field(msg.data, base, fields['t'], endian))
        points.append((x, y, z, ring, time_value, point_index))
    return points

def distance3(a, b):
    """Return Euclidean distance between two extracted PointCloud2 points."""
    dx = a[0] - b[0]
    dy = a[1] - b[1]
    dz = a[2] - b[2]
    return math.sqrt(dx * dx + dy * dy + dz * dz)

def ring_seam_metrics(points, min_ring_points):
    """Measure first/last seam distance for each LiDAR ring."""
    by_ring = {}
    for point in points:
        by_ring.setdefault(point[3], []).append(point)
    seams = []
    for ring, ring_points in by_ring.items():
        if len(ring_points) < int(min_ring_points):
            continue
        ordered = sorted(ring_points, key=lambda item: (item[4], item[5]))
        seams.append({'ring': int(ring), 'points': len(ordered), 'distance_m': distance3(ordered[0], ordered[-1]), 'first_time': float(ordered[0][4]), 'last_time': float(ordered[-1][4])})
    if not seams:
        return {'ring_count': len(by_ring), 'measured_rings': 0}
    distances = sorted((item['distance_m'] for item in seams))
    seams_sorted = sorted(seams, key=lambda item: item['distance_m'], reverse=True)
    return {'ring_count': len(by_ring), 'measured_rings': len(seams), 'median_distance_m': distances[len(distances) // 2], 'max_distance_m': distances[-1], 'largest': seams_sorted[:5]}

def coordinate_summary(points):
    """Return coordinate bounds and centroid for static/motion detection."""
    if not points:
        return {'available': False}
    min_x = min_y = min_z = float('inf')
    max_x = max_y = max_z = float('-inf')
    sum_x = sum_y = sum_z = 0.0
    min_time = float('inf')
    max_time = float('-inf')
    for point in points:
        x, y, z, _ring, time_value, _point_index = point
        min_x = min(min_x, x)
        min_y = min(min_y, y)
        min_z = min(min_z, z)
        max_x = max(max_x, x)
        max_y = max(max_y, y)
        max_z = max(max_z, z)
        sum_x += x
        sum_y += y
        sum_z += z
        min_time = min(min_time, time_value)
        max_time = max(max_time, time_value)
    count = float(len(points))
    return {'available': True, 'bounds': {'x': [float(min_x), float(max_x)], 'y': [float(min_y), float(max_y)], 'z': [float(min_z), float(max_z)]}, 'centroid': [float(sum_x / count), float(sum_y / count), float(sum_z / count)], 'time_span': [float(min_time), float(max_time)]}

def plane_from_points(a, b, c):
    """Return a normalized plane from three points, or None if degenerate."""
    ux = b[0] - a[0]
    uy = b[1] - a[1]
    uz = b[2] - a[2]
    vx = c[0] - a[0]
    vy = c[1] - a[1]
    vz = c[2] - a[2]
    nx = uy * vz - uz * vy
    ny = uz * vx - ux * vz
    nz = ux * vy - uy * vx
    norm = math.sqrt(nx * nx + ny * ny + nz * nz)
    if norm <= 1e-09:
        return None
    nx /= norm
    ny /= norm
    nz /= norm
    d = -(nx * a[0] + ny * a[1] + nz * a[2])
    return (nx, ny, nz, d)

def fit_plane_metrics(points, threshold_m, vertical_only):
    """Fit a dominant plane with deterministic RANSAC and return residuals."""
    if len(points) < 32:
        return {'available': False, 'reason': 'not enough points'}
    rng = random.Random(138)
    sample = points
    max_plane_points = 2500
    if len(sample) > max_plane_points:
        stride = max(1, len(sample) // max_plane_points)
        sample = sample[::stride][:max_plane_points]
    best = None
    iterations = 140
    threshold = float(threshold_m)
    for _ in range(iterations):
        a, b, c = rng.sample(sample, 3)
        plane = plane_from_points(a, b, c)
        if plane is None:
            continue
        nx, ny, nz, d = plane
        if vertical_only and abs(nz) > 0.45:
            continue
        inliers = 0
        sq_sum = 0.0
        for point in sample:
            residual = abs(nx * point[0] + ny * point[1] + nz * point[2] + d)
            if residual <= threshold:
                inliers += 1
                sq_sum += residual * residual
        if inliers == 0:
            continue
        rms = math.sqrt(sq_sum / inliers)
        candidate = (inliers, -rms, plane, rms)
        if best is None or candidate > best:
            best = candidate
    if best is None:
        return {'available': False, 'reason': 'no plane found'}
    inliers, neg_rms, plane, rms = best
    nx, ny, nz, d = plane
    return {'available': True, 'inlier_count': int(inliers), 'inlier_ratio': float(inliers / len(sample)), 'rms_m': float(rms), 'normal': [float(nx), float(ny), float(nz)], 'd': float(d), 'threshold_m': threshold}

def compute_pointcloud2_metrics(msg, max_metric_points, plane_threshold_m, min_ring_points):
    """Compute motion-sensitive PointCloud2 geometry metrics."""
    points = extract_pointcloud2_points(msg, max_metric_points)
    metrics = {'parsed_point_count': len(points), 'max_metric_points': int(max_metric_points), 'coordinates': coordinate_summary(points), 'ring_seam': ring_seam_metrics(points, min_ring_points)}
    metrics['best_vertical_plane'] = fit_plane_metrics(points, plane_threshold_m, vertical_only=True)
    metrics['best_any_plane'] = fit_plane_metrics(points, plane_threshold_m, vertical_only=False)
    return metrics


__all__ = ['argparse', 'json', 'pathlib', 'subprocess', 'struct', 'sys', 'ros2env', 'rviz2launch', 'POINTCLOUD2_MSG_TYPE', 'InconclusiveError', 'POINTCLOUD2_METRIC_HELPERS']
