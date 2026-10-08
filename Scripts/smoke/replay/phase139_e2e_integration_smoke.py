#!/usr/bin/env python3
"""Phase139 end-to-end smoke helper for Unity2Foxglove.

This script is intentionally a helper for local acceptance, not a replacement
for the operator's Unity/RViz/Foxglove judgment. The default path checks the
Maze demo's WebSocket surface and emits a JSON evidence summary that can be
attached to a Developer report. Optional ROS2 Native evidence is kept as a
separate profile because DDS/RViz acceptance depends on Unity Play Mode and the
local Windows ROS2 environment.

The deskewed PointCloud2 topic is visualization output and not a SLAM input.
FAST-LIO2/LIVO2 style consumers should use the raw rolling PointCloud2 topic
with per-point timing and IMU.

The ROS2 Native profile deliberately references the project Windows helper
module _ros2_windows_env and direct rclpy subscribers, but it does not import
ROS2 packages during --self-test so a clean checkout can validate the harness
shape without a ROS2 installation.
"""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .phase139_e2e_integration_smoke import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".phase139_e2e_integration_smoke")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "phase139_e2e_integration_smoke" else None
    if _phase192_self is not None:
        _phase192_sys.modules.pop("phase139_e2e_integration_smoke", None)
    try:
        from phase139_e2e_integration_smoke import *
        _phase192_package_module = _phase192_sys.modules.get("phase139_e2e_integration_smoke")
    finally:
        if _phase192_self is not None:
            _phase192_sys.modules["phase139_e2e_integration_smoke"] = _phase192_self

class _Phase192FacadeModule(_phase192_types.ModuleType):
    """Mirror facade attribute updates into the decomposed implementation package."""
    def __setattr__(self, name, value):
        """Update this facade and its implementation package."""
        super().__setattr__(name, value)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            setattr(_phase192_package_module, name, value)

    def __delattr__(self, name):
        """Remove an attribute from this facade and its implementation package."""
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
    _phase192_main = globals().get("main")
    if callable(_phase192_main):
        raise SystemExit(_phase192_main(_phase192_sys.argv[1:]))
