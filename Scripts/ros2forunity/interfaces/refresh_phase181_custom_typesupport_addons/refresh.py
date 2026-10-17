#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from . import foundation as _foundation
from . import inspection as _inspection
from . import commands as _commands
from .foundation import *
from .inspection import *
from .commands import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "refresh_phase181_custom_typesupport_addons.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def run_refresh(
    request: _foundation.AddonRefreshRequest,
    *,
    runner: Callable[..., subprocess.CompletedProcess] | None = None,
) -> tuple[_foundation.AddonRefreshState, ...]:
    """Run the bounded refresh lifecycle, rebuilding only stale packages."""

    root = Path(request.root).resolve()
    if not root.is_dir() or not (root / "Packages").is_dir() or not (root / "Scripts").is_dir():
        raise _foundation.AddonRefreshError("provide-unity2foxglove-repository-root")
    distros = tuple(dict.fromkeys(request.distros))
    if not distros or any(distro not in _foundation.SUPPORTED_DISTROS for distro in distros):
        raise _foundation.AddonRefreshError("select-supported-ros-distro")
    selected_runtime = _inspection.require_single_active_runtime(root)
    print("[phase181-addons] active runtime: " + selected_runtime, flush=True)

    states: list[_foundation.AddonRefreshState] = []
    for distro in distros:
        state = _inspection.inspect_addon_state(root, distro)
        states.append(state)
        if state.current:
            print("[phase181-addons] " + distro + ": current; validating.", flush=True)
            _commands._run_child(_commands.validation_command(root, distro), root=root, runner=runner)
            continue
        reason_text = ", ".join(state.reasons)
        if not request.apply:
            print("[phase181-addons] " + distro + ": stale (" + reason_text + "); rerun with --apply.", flush=True)
            continue
        candidate = _inspection.inspect_candidate_state(root, distro)
        if candidate.current:
            print(
                "[phase181-addons] " + distro + ": reusing matching validated candidate; synchronizing.",
                flush=True,
            )
        else:
            print(
                "[phase181-addons] " + distro + ": stale (" + reason_text
                + "); checking that the matching ros2cs install is idle and complete.",
                flush=True,
            )
            toolchain = _inspection._require_apply_toolchain(request, distro)
            _, ros2cs_install, _, _ = toolchain
            toolchain_snapshot = _inspection._ros2cs_install_snapshot(ros2cs_install)
            print(
                "[phase181-addons] " + distro
                + ": ros2cs install is complete; building isolated candidate. This can take several minutes.",
                flush=True,
            )
            _commands._run_child(
                _commands.build_command(request, distro, toolchain=toolchain),
                root=root,
                runner=runner,
            )
            if _inspection._ros2cs_install_snapshot(ros2cs_install) != toolchain_snapshot:
                raise _foundation.AddonRefreshError("wait-for-stable-ros2cs-install-and-rebuild-candidate")
            print("[phase181-addons] " + distro + ": candidate validated; synchronizing.", flush=True)
        _commands._run_child(_commands.sync_command(root, distro), root=root, runner=runner)
        print("[phase181-addons] " + distro + ": synchronized; validating tracked package.", flush=True)
        _commands._run_child(_commands.validation_command(root, distro), root=root, runner=runner)
    return tuple(states)

__all__ = ['_foundation', '_inspection', '_commands', '_Phase192Path', '_PHASE192_FACADE_FILE', 'run_refresh']
