#!/usr/bin/env python3
# Keep this release entrypoint under CI coverage for documentation-only ref updates.
# Release checks also cover post-merge checkpoint alignment commits.
# Keep final-reference checkpoint updates on the same required-check path.
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Run local CI checks that match the GitHub Actions workflows.
# Usage:   python Scripts/release/run_ci.py
#          python Scripts/release/run_ci.py --skip-analyzer
#          python Scripts/release/run_ci.py --only dotnet

"""Run local CI checks (dotnet tests + package validators)."""

# Package closure regression module: Scripts.smoke.ros2.regression_checks.test_i10_native_cleanup

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .run_ci import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".run_ci")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    _phase192_repo_root = _Phase192Path(__file__).resolve().parents[2]
    if str(_phase192_repo_root) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_repo_root))
    _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "run_ci" else None
    if _phase192_self is not None:
        _phase192_sys.modules.pop("run_ci", None)
    try:
        from run_ci import *
        _phase192_package_module = _phase192_sys.modules.get("run_ci")
    finally:
        if _phase192_self is not None:
            _phase192_sys.modules["run_ci"] = _phase192_self


class _Phase192FacadeModule(_phase192_types.ModuleType):
    """Forward facade mutations to the implementation package."""

    def __setattr__(self, name, value):
        """Propagate exported state changes to the package module."""
        super().__setattr__(name, value)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            setattr(_phase192_package_module, name, value)

    def __delattr__(self, name):
        """Propagate exported state removals to the package module."""
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
    raise SystemExit(main())
