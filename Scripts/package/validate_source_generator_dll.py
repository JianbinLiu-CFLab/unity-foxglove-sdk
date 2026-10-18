#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Rebuild the checked-in Roslyn source generator DLL and verify freshness.
# Usage: python Scripts/package/validate_source_generator_dll.py
#        python Scripts/package/validate_source_generator_dll.py --update

"""Validate that the checked-in source generator DLL matches a fresh Release build.

The facade retains the path-coupled Phase163-58 contract markers
``--build-output-dir``, ``build_output_dir: Path``, ``build_output_dir / name``,
and ``args.build_output_dir`` while forwarding implementation to the package.
"""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

_phase192_module = _phase192_sys.modules.get(__name__)
_phase192_canonical_self = (
    _phase192_sys.modules.get(__name__)
    if __package__ and _phase192_sys.modules.get(__name__) is not None
    else None
)
_phase192_previous_self = _phase192_sys.modules.get("validate_source_generator_dll")
if _phase192_canonical_self is not None:
    _phase192_canonical_self.__package__ = ""
    _phase192_canonical_self.__name__ = "validate_source_generator_dll"
    _phase192_sys.modules["validate_source_generator_dll"] = _phase192_canonical_self

if __package__:
    from .validate_source_generator_dll import *
    _phase192_package_module = _phase192_sys.modules.get(
        __package__ + ".validate_source_generator_dll"
    )
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = (
        _phase192_sys.modules.get(__name__)
        if __name__ == "validate_source_generator_dll"
        else None
    )
    if _phase192_self is not None:
        _phase192_sys.modules.pop("validate_source_generator_dll", None)
    try:
        from validate_source_generator_dll import *
        _phase192_package_module = _phase192_sys.modules.get(
            "validate_source_generator_dll"
        )
    finally:
        if _phase192_self is not None:
            _phase192_sys.modules["validate_source_generator_dll"] = _phase192_self

if _phase192_canonical_self is not None and _phase192_previous_self is not None:
    _phase192_sys.modules["validate_source_generator_dll"] = _phase192_previous_self

__package__ = __spec__.parent if __spec__ is not None else __package__
__name__ = __spec__.name if __spec__ is not None else __name__


class _Phase192FacadeModule(_phase192_types.ModuleType):
    """Mirror facade attribute updates into the decomposed validator package."""

    def __setattr__(self, name, value):
        """Mirror assignments into the decomposed implementation."""
        super().__setattr__(name, value)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            setattr(_phase192_package_module, name, value)

    def __delattr__(self, name):
        """Mirror deletions into the decomposed implementation."""
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
