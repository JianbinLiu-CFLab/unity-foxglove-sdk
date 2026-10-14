#!/usr/bin/env python3
"""Build one Phase181 custom ROS2 typesupport add-on below ``build/phase181``.

The script deliberately has two materialization boundaries.  rosidl output is
first characterized in an out-of-tree candidate workspace.  A Unity batch
importer then generates PluginImporter metadata for every managed and native
candidate DLL.  Only the sibling ``sync_...`` command may copy a fully
validated candidate to ``Packages/``.
"""


from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "build_foxrun_custom_typesupport_addon.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import json
import shutil
from pathlib import Path

from .models import *
from .serialization import *
from .repair import *
from .unity import *


def _require_explicit_toolchain_sources(request: CandidateBuildRequest) -> tuple[Path, Path, Path]:
    """Return explicit build-toolchain paths without machine-local defaults."""

    if request.ros2cs_source is None:
        raise CandidateBuildError("provide-ros2cs-source")
    if request.r2fu_source is None:
        raise CandidateBuildError("provide-r2fu-source")
    ros2cs_install = request.ros2cs_install or request.ros2cs_source / ("install-" + request.distro)
    return request.ros2cs_source, ros2cs_install, request.r2fu_source

def build_candidate(request: CandidateBuildRequest, *, check_source_only: bool = False) -> CandidateBuildResult:
    """Build, Unity-import, and validate one private candidate add-on."""

    interface_digest = verify_candidate_source_lock(request)
    if check_source_only:
        return CandidateBuildResult(request.distro, candidate_package_root(request), interface_digest)

    ros2cs_source, ros2cs_install, r2fu_source = _require_explicit_toolchain_sources(request)

    try:
        result = characterize(
            CharacterizationRequest(
                distro=request.distro,
                static_package=Path(request.static_interface_package),
                ros2_root=Path(request.ros2_root),
                ros2cs_source=ros2cs_source,
                ros2cs_install=ros2cs_install,
                r2fu_source=r2fu_source,
                build_root=Path(request.build_root),
                generator=request.generator,
                workspace_name="candidate",
            ),
            replace_existing=True,
        )
    except CharacterizationError as exc:
        raise CandidateBuildError("repair-controlled-typesupport-build") from exc
    candidate_root = candidate_package_root(request)
    if candidate_root.exists():
        shutil.rmtree(candidate_root)
    candidate_root.mkdir(parents=True, exist_ok=True)
    _package_metadata(request, candidate_root)
    _write_candidate_texts(candidate_root, _repo_root(request), request.distro)

    native_root = candidate_root / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64"
    managed_target = _managed_package_assembly_path(candidate_root)
    _copy_file(result.managed_assembly, managed_target)
    native_libraries = _candidate_native_paths(request)
    if not native_libraries:
        raise CandidateBuildError("repair-generated-native-typesupport-closure")
    for native in native_libraries:
        _copy_file(native, native_root / native.name)

    _run_unity_plugin_importer(request, managed_target, input_is_directory=False)
    _run_unity_plugin_importer(request, native_root, input_is_directory=True)
    managed_evidence = _load_json(
        _phase181_distro_root(request) / "candidate" / "e" / "managed.json",
        "repair-managed-characterization-evidence",
    )
    _write_candidate_manifest(request, candidate_root, interface_digest, managed_evidence, native_libraries)
    _write_inventory(candidate_root)

    try:
        validate_addon(
            AddonValidationRequest(
                distro=request.distro,
                addon_package=candidate_root,
                static_interface_package=Path(request.static_interface_package),
                base_runtime_package=Path(request.base_runtime_package),
                require_rmws=("rmw_fastrtps_cpp", "rmw_zenoh_cpp") if request.distro == "lyrical" else (),
            )
        )
    except Exception as exc:
        raise CandidateBuildError("repair-candidate-typesupport-validation") from exc
    evidence = _phase181_distro_root(request) / "candidate" / "e" / "candidate-validation.json"
    _write_utf8_lf(
        evidence,
        json.dumps(
            {
                "schemaVersion": 1,
                "distro": request.distro,
                "interfaceDigest": interface_digest,
                "candidatePackageSha256": normalized_json_sha256(
                    json.loads((candidate_root / "RuntimeSupport" / "typesupport-inventory.json").read_text(encoding="utf-8"))
                ),
                "validated": True,
            },
            sort_keys=True,
            indent=2,
        ) + "\n",
    )
    return CandidateBuildResult(request.distro, candidate_root, interface_digest)

__all__ = [name for name in globals() if not name.startswith("__")]
