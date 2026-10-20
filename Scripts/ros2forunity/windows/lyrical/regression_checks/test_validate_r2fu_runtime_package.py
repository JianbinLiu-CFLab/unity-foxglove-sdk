#!/usr/bin/env python3
"""Compatibility facade for decomposed Lyrical runtime validator tests."""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .test_validate_r2fu_runtime_package import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".test_validate_r2fu_runtime_package")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    _phase192_repo_root = _Phase192Path(__file__).resolve().parents[5]
    if str(_phase192_repo_root) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_repo_root))
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "test_validate_r2fu_runtime_package" else None
    if _phase192_self is not None:
        _phase192_sys.modules.pop("test_validate_r2fu_runtime_package", None)
    try:
        from test_validate_r2fu_runtime_package import *
        _phase192_package_module = _phase192_sys.modules.get("test_validate_r2fu_runtime_package")
    finally:
        if _phase192_self is not None:
            _phase192_sys.modules["test_validate_r2fu_runtime_package"] = _phase192_self




_PHASE192_DISCOVERY_SUPPRESS = "discover" in _phase192_sys.argv[1:]
if _PHASE192_DISCOVERY_SUPPRESS:
    import unittest as _phase192_unittest
    for _phase192_name in tuple(globals()):
        _phase192_value = globals().get(_phase192_name)
        if (
            isinstance(_phase192_value, type)
            and issubclass(_phase192_value, _phase192_unittest.TestCase)
            and _phase192_value is not _phase192_unittest.TestCase
        ):
            globals().pop(_phase192_name, None)

class _Phase192FacadeModule(_phase192_types.ModuleType):
    """Mirror facade attribute updates into the test package."""

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
    import unittest as _phase192_unittest
    raise SystemExit(_phase192_unittest.main(module=__name__))
