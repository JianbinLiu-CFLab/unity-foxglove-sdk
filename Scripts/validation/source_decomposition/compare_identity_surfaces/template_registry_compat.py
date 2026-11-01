from __future__ import annotations

import importlib as _phase192_importlib
import sys as _phase192_sys
import types as _phase192_types
from pathlib import Path as _Phase192Path


def _phase192_install_template_registry_facade(module_name: str) -> None:
    """Install the legacy facade state bridge after the package import resolves."""
    module = _phase192_sys.modules.get(module_name)
    if module is None:
        return
    facade_path = getattr(module, "__file__", None)
    if not facade_path:
        return
    parent = str(_Phase192Path(facade_path).resolve().parent)
    if parent not in _phase192_sys.path:
        _phase192_sys.path.insert(0, parent)
    _phase192_sys.modules.pop(module_name, None)
    previous_module = _phase192_sys.modules.pop("template_registry", None)
    try:
        package_module = _phase192_importlib.import_module("template_registry")
        for name in getattr(package_module, "__all__", ()):
            setattr(module, name, getattr(package_module, name))
    finally:
        _phase192_sys.modules[module_name] = module
        if module_name == "template_registry":
            _phase192_sys.modules["template_registry"] = module
        elif previous_module is not None and previous_module is not module:
            _phase192_sys.modules["template_registry"] = previous_module
    class _Phase192FacadeModule(_phase192_types.ModuleType):
        """Mirror facade assignments into the decomposed template registry package."""

        def __setattr__(self, name, value):
            """Update this facade and the decomposed package."""
            super().__setattr__(name, value)
            if package_module is not None and package_module is not self:
                setattr(package_module, name, value)

        def __delattr__(self, name):
            """Remove this facade value from the decomposed package."""
            super().__delattr__(name)
            if package_module is not None and package_module is not self:
                try:
                    delattr(package_module, name)
                except AttributeError:
                    pass

    module.__class__ = _Phase192FacadeModule
