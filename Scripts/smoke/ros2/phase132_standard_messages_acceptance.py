#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: ROS2 CLI acceptance helper for the Phase132 standard message sample.

"""Validate the ROS2 standard message expansion sample.

Start Unity manually first. Import the ROS2 Standard Message Expansion sample,
add Phase132StandardMessagesSmoke to a scene object, then enter Play Mode. This
helper uses the pinned Windows ROS2 Jazzy Python entry point to check the six
standard ROS2 topics and representative echo payloads, then launches RViz2 by
default for a lightweight visual sanity check.
"""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .phase132_standard_messages_acceptance import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".phase132_standard_messages_acceptance")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "phase132_standard_messages_acceptance" else None
    if _phase192_self is not None: _phase192_sys.modules.pop("phase132_standard_messages_acceptance", None)
    try:
        from phase132_standard_messages_acceptance import *
        _phase192_package_module = _phase192_sys.modules.get("phase132_standard_messages_acceptance")
    finally:
        if _phase192_self is not None: _phase192_sys.modules["phase132_standard_messages_acceptance"] = _phase192_self

class _Phase192FacadeModule(_phase192_types.ModuleType):
    """Mirror facade attribute updates into the decomposed implementation module."""
    def __setattr__(self, name, value):
        """Update this facade and its implementation module."""
        super().__setattr__(name, value)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            setattr(_phase192_package_module, name, value)
    def __delattr__(self, name):
        """Remove an attribute from this facade and its implementation module."""
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
        try:
            raise SystemExit(_phase192_main(_phase192_sys.argv[1:]))
        except KeyboardInterrupt:
            raise SystemExit(130)
        except Exception as exc:
            print(f"[phase132-standard-messages] FAIL: {exc}", file=sys.stderr)
            raise SystemExit(1)
