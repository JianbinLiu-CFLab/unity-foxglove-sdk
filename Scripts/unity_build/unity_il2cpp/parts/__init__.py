import sys as _phase192_sys
import importlib as _phase192_importlib

_phase192_parent_package = _phase192_sys.modules.get("Scripts.unity_build")
if _phase192_parent_package is None:
    _phase192_parent_package = _phase192_importlib.import_module("Scripts.unity_build")
_phase192_package_module = _phase192_sys.modules[__package__.rsplit(".parts", 1)[0]]
_phase192_sys.modules["unity_il2cpp"] = _phase192_package_module
_phase192_sys.modules["Scripts.unity_build.unity_il2cpp"] = _phase192_package_module
_phase192_sys.modules["Scripts.unity_build.unity_il2cpp.implementation"] = _phase192_sys.modules.get("unity_il2cpp.implementation", _phase192_sys.modules.get("Scripts.unity_build.unity_il2cpp.implementation"))

from . import foundation as _phase192_foundation
from . import il2cpp_logging as _phase192_il2cpp_logging
from . import process as _phase192_process
from . import cli as _phase192_cli

_phase192_parts_module = _phase192_sys.modules[__name__]
_phase192_sys.modules["unity_il2cpp.parts"] = _phase192_parts_module
_phase192_sys.modules["Scripts.unity_build.unity_il2cpp.parts"] = _phase192_parts_module
_phase192_sys.modules["unity_il2cpp.parts.foundation"] = _phase192_foundation
_phase192_sys.modules["Scripts.unity_build.unity_il2cpp.parts.foundation"] = _phase192_foundation
_phase192_sys.modules["unity_il2cpp.parts.il2cpp_logging"] = _phase192_il2cpp_logging
_phase192_sys.modules["Scripts.unity_build.unity_il2cpp.parts.il2cpp_logging"] = _phase192_il2cpp_logging
_phase192_sys.modules["unity_il2cpp.parts.process"] = _phase192_process
_phase192_sys.modules["Scripts.unity_build.unity_il2cpp.parts.process"] = _phase192_process
_phase192_sys.modules["unity_il2cpp.parts.cli"] = _phase192_cli
_phase192_sys.modules["Scripts.unity_build.unity_il2cpp.parts.cli"] = _phase192_cli
