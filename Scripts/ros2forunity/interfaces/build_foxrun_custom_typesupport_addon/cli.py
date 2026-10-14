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

import argparse
import json
import sys
from pathlib import Path
from typing import Sequence

from .models import *
from .serialization import *
from .repair import *
from .unity import *
from .build import *

def _default_repo_root() -> Path:
    """Return the repository root inferred from this checked-in interface tool."""
    return Path(__file__).resolve().parents[3]


def _load_toolchain_provenance(request: CandidateBuildRequest) -> Path:
    """Require the preflight attestation that binds this candidate to its inputs."""
    path = _phase181_distro_root(request) / "provenance" / "toolchain.json"
    if not path.is_file():
        raise CandidateBuildError("run-toolchain-preflight")
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise CandidateBuildError("repair-toolchain-provenance") from exc
    identity = payload.get("inputIdentity")
    roots = identity.get("roots") if isinstance(identity, dict) else None
    required_roots = {"ros2Root", "ros2csSource", "r2fuSource"}
    if request.ros2cs_install is not None:
        required_roots.add("ros2csInstall")
    if (
        payload.get("schemaVersion") != 2
        or not isinstance(identity, dict)
        or not isinstance(roots, dict)
        or not required_roots.issubset(roots)
    ):
        raise CandidateBuildError("repair-toolchain-provenance")
    return path


def parse_args(argv: Sequence[str] | None = None) -> tuple[CandidateBuildRequest, bool, bool, bool]:
    """Parse a candidate command without inventing external operator toolchain paths."""
    root = _default_repo_root()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--distro", required=True, choices=("humble", "jazzy", "lyrical"))
    parser.add_argument("--static-interface-package", type=Path, default=root / "Packages" / STATIC_INTERFACE_PACKAGE_ID)
    parser.add_argument("--base-runtime-package", type=Path)
    parser.add_argument("--ros2-root", type=Path)
    parser.add_argument("--ros2cs-source", type=Path)
    parser.add_argument("--ros2cs-install", type=Path)
    parser.add_argument("--r2fu-source", type=Path)
    parser.add_argument("--build-root", type=Path, default=root / "build")
    parser.add_argument("--generator", default="Ninja")
    parser.add_argument("--unity", type=Path)
    parser.add_argument("--check-source", action="store_true")
    parser.add_argument("--repair-tracked-catalog", action="store_true")
    parser.add_argument("--repair-tracked-license-eol", action="store_true")
    args = parser.parse_args(argv)
    if sum((args.check_source, args.repair_tracked_catalog, args.repair_tracked_license_eol)) > 1:
        parser.error("--check-source, --repair-tracked-catalog, and --repair-tracked-license-eol are mutually exclusive")
    ros2_root = args.ros2_root or root / "ros2-windows" / ("ros2_" + args.distro)
    ros2cs_install = args.ros2cs_install
    if ros2cs_install is None and args.ros2cs_source is not None:
        ros2cs_install = args.ros2cs_source / ("install-" + args.distro)
    return (
        CandidateBuildRequest(
            distro=args.distro,
            static_interface_package=args.static_interface_package,
            base_runtime_package=args.base_runtime_package or root / "Packages" / base_runtime_package_id(args.distro),
            ros2_root=ros2_root,
            ros2cs_source=args.ros2cs_source,
            ros2cs_install=ros2cs_install,
            r2fu_source=args.r2fu_source,
            build_root=args.build_root,
            generator=args.generator,
            unity_executable=args.unity,
            repo_root=root,
        ),
        args.check_source,
        args.repair_tracked_catalog,
        args.repair_tracked_license_eol,
    )


def main(argv: Sequence[str] | None = None) -> int:
    """Run source-only validation, catalog repair, or a controlled candidate build."""
    request, check_source_only, repair_tracked_catalog, repair_tracked_license_eol = parse_args(argv)
    try:
        if repair_tracked_license_eol:
            changed = _repair_tracked_addon_license_eol(request)
            print("PASS:", request.distro, "canonicalized-license" if changed else "license-already-canonical")
            return 0
        if repair_tracked_catalog:
            catalog = _repair_tracked_addon_catalog(request)
            print("PASS:", request.distro, catalog)
            return 0
        result = build_candidate(request, check_source_only=check_source_only)
    except CandidateBuildError as exc:
        print(str(exc), file=sys.stderr)
        return 1
    print("PASS:", result.distro, result.candidate_package, result.interface_digest)
    return 0


__all__ = [name for name in globals() if not name.startswith("__")]
