#!/usr/bin/env python3
from __future__ import annotations

# R2FU source-gate pins: 4e5cb8b0073d4a34d194b9a6ce0b3449220085f3cfd041b2fd33622e6442ff5d; V083_EXCLUDED_TEST_TYPESUPPORT_DLLS; test_msgs_complex_nested_key__rosidl_typesupport_c_native.dll; test_msgs_keyed_long__rosidl_typesupport_c_native.dll; test_msgs_keyed_string__rosidl_typesupport_c_native.dll; test_msgs_non_keyed_with_nested_key__rosidl_typesupport_c_native.dll

import sys as _phase192_sys
import types as _phase192_types

_phase192_sys.modules.pop("validate_r2fu_runtime_package", None) if __name__ in {"__main__", "<run_path>"} else None

if __package__:
    from .validate_r2fu_runtime_package import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".validate_r2fu_runtime_package")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    _phase192_repo_root = _Phase192Path(__file__).resolve().parents[4]
    if str(_phase192_repo_root) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_repo_root))
    _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "validate_r2fu_runtime_package" else None
    if _phase192_self is not None:
        _phase192_sys.modules.pop("validate_r2fu_runtime_package", None)
    try:
        from validate_r2fu_runtime_package import *
        _phase192_package_module = _phase192_sys.modules.get("validate_r2fu_runtime_package")
    finally:
        if _phase192_self is not None:
            _phase192_sys.modules["validate_r2fu_runtime_package"] = _phase192_self


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
