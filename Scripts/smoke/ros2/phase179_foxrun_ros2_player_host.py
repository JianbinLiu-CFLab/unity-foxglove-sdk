#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Launch one Phase179 Player with an explicit ROS2 environment and collect proof.

Run this helper on Windows before starting the Linux peer helper.  It owns only
the Player it launches and an explicitly supplied Zenoh router executable.  A
Player launch is not a pass: the final verdict also requires its READY marker,
all required copied-value APPLIED markers, a successful COMPLETE marker, and a
zero operating-system exit code.
"""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .phase179_foxrun_ros2_player_host import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".phase179_foxrun_ros2_player_host")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "phase179_foxrun_ros2_player_host" else None
    if _phase192_self is not None:
        _phase192_sys.modules.pop("phase179_foxrun_ros2_player_host", None)
    try:
        from phase179_foxrun_ros2_player_host import *
        _phase192_package_module = _phase192_sys.modules.get("phase179_foxrun_ros2_player_host")
    finally:
        if _phase192_self is not None:
            _phase192_sys.modules["phase179_foxrun_ros2_player_host"] = _phase192_self

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
    except Exception as exc:
        print(f"[phase179-player-host] FAIL: {exc}", file=_phase192_sys.stderr)
        print("[phase179-player-host] Check the Player log, stale ROS2/Zenoh processes, and the supplied runtime selection.", file=_phase192_sys.stderr)
        raise SystemExit(1)
