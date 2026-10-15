#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Phase184 WindowsJobOwner implementation section."""

from __future__ import annotations
from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase184_windows_job_owner.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
from .foundation import *

class _WindowsJobOwnerCleanup:
    """Compatibility implementation section."""

    def request_owned_desktop_close(
        self,
        *,
        grace_seconds: float = 10.0,
        reject_external: bool = True,
    ) -> CloseSummary:
        """Post WM_CLOSE to owned Desktop windows, then hard-close the Job."""

        grace = _validate_timeout(grace_seconds)
        requested: tuple[ProcessIdentity, ...] = ()
        graceful: list[ProcessIdentity] = []
        forced: list[ProcessIdentity] = []
        try:
            external = self.external_processes(self._desktop_executable)
            if external and reject_external:
                raise _fail(
                    FAIL_DESKTOP_HANDOFF,
                    "Desktop close rejected an exact-path process outside the Job.",
                )
            requested = tuple(
                identity
                for identity in self.members()
                if _same_path(identity.executable, self._desktop_executable)
            )
            if requested:
                try:
                    api, job_handle = self._require_active()
                    self._api.post_close_to_top_level_windows(
                        requested,
                        job_handle,
                    )
                except Exception:
                    raise _fail(
                        FAIL_PROCESS_OWNERSHIP,
                        "A Desktop window process failed exact Job identity validation.",
                    ) from None

            deadline = time.monotonic() + grace
            for identity in requested:
                remaining = max(0.0, deadline - time.monotonic())
                handle = self._root_handle(identity)
                try:
                    exited = (
                        bool(self._api.wait_process(handle, remaining))
                        if handle is not None
                        else bool(self._api.wait_identity(identity, remaining))
                    )
                except Exception:
                    exited = False
                (graceful if exited else forced).append(identity)
            return CloseSummary(
                requested=requested,
                graceful=tuple(graceful),
                forced=tuple(forced),
            )
        finally:
            self.close()

    def close(self) -> None:
        """Close the sole Job handle once, then release retained root handles."""

        if self._closed:
            return
        self._closed = True
        api = self._api
        job_handle = self._job_handle
        self._job_handle = None
        root_handles = tuple(dict.fromkeys(self._roots.values()))
        self._roots.clear()
        self._root_handoff_policies.clear()
        self._root_exit_codes.clear()
        owned_handles = (
            ((job_handle,) if job_handle is not None else ())
            + root_handles
        )
        handles = (
            tuple(dict.fromkeys(owned_handles))
            if api is not None
            else ()
        )
        cleanup_failed = False
        for handle in handles:
            try:
                api.close_handle(handle)
            except Exception:
                cleanup_failed = True
        if cleanup_failed:
            raise _fail(
                FAIL_CLEANUP,
                "Windows Job ownership handle cleanup did not complete.",
            ) from None

    def __enter__(self) -> "WindowsJobOwner":
        """Enter the windows job owner context."""

        self._require_active()
        return self

    def __exit__(self, _type, _value, _traceback) -> None:
        """Exit the windows job owner context without suppressing failures."""

        self.close()

del _Phase192Path

__all__ = ['_WindowsJobOwnerCleanup']
