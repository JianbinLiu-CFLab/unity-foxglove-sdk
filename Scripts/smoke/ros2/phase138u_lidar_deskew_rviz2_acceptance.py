#!/usr/bin/env python3
"""Validate raw and deskewed Phase138U PointCloud2 topics and launch RViz2.

Start Unity Play Mode first with ROS2 Native (R2FU), PointCloud2 Native mode,
and PointCloud Motion Compensation enabled in RawAndDeskewedTopic mode. Then run:

    python Scripts/smoke/ros2/phase138u_lidar_deskew_rviz2_acceptance.py

The script launches RViz2 by default for visual comparison and uses direct rclpy
subscribers as the hard DDS acceptance gate. Pass --no-rviz when only the DDS
subscription check is needed.
"""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .phase138u_lidar_deskew_rviz2_acceptance import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".phase138u_lidar_deskew_rviz2_acceptance")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "phase138u_lidar_deskew_rviz2_acceptance" else None
    if _phase192_self is not None:
        _phase192_sys.modules.pop("phase138u_lidar_deskew_rviz2_acceptance", None)
    try:
        from phase138u_lidar_deskew_rviz2_acceptance import *
        _phase192_package_module = _phase192_sys.modules.get("phase138u_lidar_deskew_rviz2_acceptance")
    finally:
        if _phase192_self is not None:
            _phase192_sys.modules["phase138u_lidar_deskew_rviz2_acceptance"] = _phase192_self


class _Phase192FacadeModule(_phase192_types.ModuleType):
    """Mirror facade attribute updates into the decomposed package."""

    def __setattr__(self, name, value):
        """Update this facade and the decomposed implementation module."""
        super().__setattr__(name, value)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            setattr(_phase192_package_module, name, value)

    def __delattr__(self, name):
        """Remove an attribute from this facade and the implementation module."""
        super().__delattr__(name)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            try:
                delattr(_phase192_package_module, name)
            except AttributeError:
                pass


_phase192_module = _phase192_sys.modules.get(__name__)
if _phase192_module is not None:
    _phase192_module.__class__ = _Phase192FacadeModule

if __name__ == "__main__":
    try:
        raise SystemExit(main(_phase192_sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except InconclusiveError as exc:
        print(f"[phase138u-lidar-deskew] INCONCLUSIVE: {exc}", file=sys.stderr)
        raise SystemExit(2)
    except Exception as exc:
        print(f"[phase138u-lidar-deskew] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
