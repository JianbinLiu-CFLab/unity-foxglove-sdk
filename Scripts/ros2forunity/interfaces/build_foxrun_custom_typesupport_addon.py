#!/usr/bin/env python3
"""Build one Phase181 custom ROS2 typesupport add-on below ``build/phase181``.

The script deliberately has two materialization boundaries.  rosidl output is
first characterized in an out-of-tree candidate workspace.  A Unity batch
importer then generates PluginImporter metadata for every managed and native
candidate DLL.  Only the sibling ``sync_...`` command may copy a fully
validated candidate to ``Packages/``.
"""


from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

if __package__:
    from .build_foxrun_custom_typesupport_addon import *
    _phase192_package_module = _phase192_sys.modules.get(__package__ + ".build_foxrun_custom_typesupport_addon")
else:
    from pathlib import Path as _Phase192Path
    _phase192_parent = _Phase192Path(__file__).resolve().parent
    if str(_phase192_parent) not in _phase192_sys.path:
        _phase192_sys.path.insert(0, str(_phase192_parent))
    _phase192_self = _phase192_sys.modules.get(__name__) if __name__ == "build_foxrun_custom_typesupport_addon" else None
    if _phase192_self is not None:
        _phase192_sys.modules.pop("build_foxrun_custom_typesupport_addon", None)
    try:
        from build_foxrun_custom_typesupport_addon import *
        _phase192_package_module = _phase192_sys.modules.get("build_foxrun_custom_typesupport_addon")
    finally:
        if _phase192_self is not None:
            _phase192_sys.modules["build_foxrun_custom_typesupport_addon"] = _phase192_self

class _Phase192FacadeModule(_phase192_types.ModuleType):
    """Mirror facade attribute updates into the decomposed package."""

    def __setattr__(self, name, value):
        """Mirror facade attribute writes into the decomposed package."""
        super().__setattr__(name, value)
        if _phase192_package_module is not None and _phase192_package_module is not self:
            setattr(_phase192_package_module, name, value)

    def __delattr__(self, name):
        """Mirror facade attribute deletions into the decomposed package."""
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
    raise SystemExit(cli_entry(_phase192_sys.argv[1:]))
