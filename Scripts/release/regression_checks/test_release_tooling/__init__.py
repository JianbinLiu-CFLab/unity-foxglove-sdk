"""Import-compatible package for decomposed release-tooling regression tests."""

from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types
from pathlib import Path as _Phase192Path

_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "test_release_tooling.py"
__file__ = str(_PHASE192_FACADE_FILE)
_PHASE192_PARENT = _PHASE192_FACADE_FILE.parent
if str(_PHASE192_PARENT) not in _phase192_sys.path:
    _phase192_sys.path.insert(0, str(_PHASE192_PARENT))

from . import foundation as _phase192_foundation
from . import version_bump_tests as _phase192_version_bump_tests
from . import run_ci_tests_part_1 as _phase192_run_ci_tests_part_1
from . import run_ci_tests_part_2 as _phase192_run_ci_tests_part_2
from . import run_ci_tests_part_3 as _phase192_run_ci_tests_part_3
from . import run_ci_tests_part_4 as _phase192_run_ci_tests_part_4
from . import run_ci_tests_part_5 as _phase192_run_ci_tests_part_5
from . import local_entrypoint_tests as _phase192_local_entrypoint_tests
from . import mcap_conformance_tests as _phase192_mcap_conformance_tests
from . import r2fu_artifact_handoff_tests as _phase192_r2fu_artifact_handoff_tests
from . import unity_il2cpp_tests_part_1 as _phase192_unity_il2cpp_tests_part_1
from . import unity_il2cpp_tests_part_2 as _phase192_unity_il2cpp_tests_part_2
from . import unity_batch_compile_gate_tests as _phase192_unity_batch_compile_gate_tests
for _phase192_name, _phase192_value in vars(_phase192_foundation).items():
    if not _phase192_name.startswith("_"):
        globals()[_phase192_name] = _phase192_value
VersionBumpTests = _phase192_version_bump_tests.VersionBumpTests
LocalEntrypointValidatorTests = _phase192_local_entrypoint_tests.LocalEntrypointValidatorTests
McapConformanceToolTests = _phase192_mcap_conformance_tests.McapConformanceToolTests
R2fuArtifactHandoffTests = _phase192_r2fu_artifact_handoff_tests.R2fuArtifactHandoffTests
UnityBatchCompileGateTests = _phase192_unity_batch_compile_gate_tests.UnityBatchCompileGateTests
class RunCiTests(_phase192_run_ci_tests_part_1._RunCiTestsPart1, _phase192_run_ci_tests_part_2._RunCiTestsPart2, _phase192_run_ci_tests_part_3._RunCiTestsPart3, _phase192_run_ci_tests_part_4._RunCiTestsPart4, _phase192_run_ci_tests_part_5._RunCiTestsPart5, unittest.TestCase):
    """Regression coverage for local CI runner reliability."""
    pass

class UnityIl2CppBuildTests(_phase192_unity_il2cpp_tests_part_1._UnityIl2CppBuildTestsPart1, _phase192_unity_il2cpp_tests_part_2._UnityIl2CppBuildTestsPart2, unittest.TestCase):
    """Regression coverage for Unity IL2CPP build tooling."""
    pass


_PHASE192_SECTION_MODULES = (_phase192_foundation, _phase192_version_bump_tests, _phase192_run_ci_tests_part_1, _phase192_run_ci_tests_part_2, _phase192_run_ci_tests_part_3, _phase192_run_ci_tests_part_4, _phase192_run_ci_tests_part_5, _phase192_local_entrypoint_tests, _phase192_mcap_conformance_tests, _phase192_r2fu_artifact_handoff_tests, _phase192_unity_il2cpp_tests_part_1, _phase192_unity_il2cpp_tests_part_2, _phase192_unity_batch_compile_gate_tests,)
_PHASE192_EXPORT_NAMES = {
    _phase192_name
    for _phase192_name in globals()
    if not _phase192_name.startswith("_")
}
for _phase192_section in _PHASE192_SECTION_MODULES:
    for _phase192_name in _PHASE192_EXPORT_NAMES:
        _phase192_section.__dict__[_phase192_name] = globals()[_phase192_name]

for _phase192_class in (VersionBumpTests, RunCiTests, LocalEntrypointValidatorTests, McapConformanceToolTests, R2fuArtifactHandoffTests, UnityIl2CppBuildTests, UnityBatchCompileGateTests,):
    _phase192_class.__module__ = __name__


class _Phase192PackageModule(_phase192_types.ModuleType):
    """Mirror package state into all decomposed release-tooling test modules."""

    def __setattr__(self, name, value):
        """Mirror facade assignments into decomposed test modules."""
        super().__setattr__(name, value)
        if not name.startswith("__") and not name.startswith("_phase192_"):
            for _phase192_section in _PHASE192_SECTION_MODULES:
                _phase192_section.__dict__[name] = value

    def __delattr__(self, name):
        """Mirror facade deletions into decomposed test modules."""
        super().__delattr__(name)
        if not name.startswith("__") and not name.startswith("_phase192_"):
            for _phase192_section in _PHASE192_SECTION_MODULES:
                _phase192_section.__dict__.pop(name, None)


__all__ = sorted(_PHASE192_EXPORT_NAMES)
_phase192_module = _phase192_sys.modules.get(__name__)
if _phase192_module is not None:
    _phase192_module.__class__ = _Phase192PackageModule
del _phase192_sys, _phase192_types, _Phase192Path, _PHASE192_FACADE_FILE, _PHASE192_PARENT
del _phase192_module, _phase192_section, _phase192_name, _phase192_class
