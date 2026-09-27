from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_desktop_live_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class FakeJobOwner:
    """Represent the fake job owner contract."""

    def __init__(self, harness, desktop_executable: pathlib.Path):
        """Initialize the fake job owner."""

        self.harness = harness
        self.desktop_executable = str(desktop_executable)
        self.base_identity = job_owner.ProcessIdentity(
            pid=101,
            creation_time_100ns=13_400_000_000_000_101,
            executable=str(pathlib.Path(sys.executable)),
        )
        self.desktop_identity = job_owner.ProcessIdentity(
            pid=202,
            creation_time_100ns=13_400_000_000_000_202,
            executable=self.desktop_executable,
        )
        self.desktop_member = job_owner.ProcessIdentity(
            pid=203,
            creation_time_100ns=13_400_000_000_000_203,
            executable=self.desktop_executable,
        )
        self.late_member = job_owner.ProcessIdentity(
            pid=303,
            creation_time_100ns=13_400_000_000_000_303,
            executable=self.desktop_executable,
        )
        self.external_identity = job_owner.ProcessIdentity(
            pid=404,
            creation_time_100ns=13_400_000_000_000_404,
            executable=self.desktop_executable,
        )
        self.base_launched = False
        self.desktop_launched = False
        self.base_exited = False
        self.closed = False
        self._recorded_external: list[job_owner.ProcessIdentity] = []
        self.harness.events.append("job:create")

    @property
    def recorded_external_processes(self):
        """Handle the recorded external processes step."""

        return tuple(self._recorded_external)

    def require_no_external_processes(self, executable=None) -> None:
        """Require no external processes."""

        self.harness.events.append("job:preflight-no-external")
        if self.harness.mode == "preexisting":
            self._recorded_external.append(self.external_identity)
            raise job_owner.OwnershipFailure(
                job_owner.FAIL_DESKTOP_PREFLIGHT,
                "pre-existing exact-path process",
            )

    def launch_suspended_owned(
        self,
        application_path,
        arguments,
        *,
        cwd,
        environment,
        stdout_log,
        stderr_log,
        handoff_policy=None,
    ):
        """Handle the launch suspended owned step."""

        record = {
            "application": str(application_path),
            "arguments": tuple(arguments),
            "cwd": str(cwd),
            "environment": dict(environment),
            "stdout": str(stdout_log),
            "stderr": str(stderr_log),
            "policy": handoff_policy,
        }
        if str(application_path) == str(pathlib.Path(sys.executable)):
            self.harness.events.append("launch:base")
            self.harness.launches.append(("base", record))
            self.base_launched = True
            self.harness.create_base_run()
            return self.base_identity
        if self.harness.mode == "desktop-start-fail":
            raise job_owner.OwnershipFailure(
                job_owner.FAIL_PROCESS_CREATE,
                "injected Desktop start failure",
            )
        self.harness.events.append("launch:desktop")
        self.harness.events.append(
            (
                "desktop:launch-lease-active"
                if self.harness.desktop_lease_active
                else "desktop:launch-lease-inactive"
            )
        )
        self.harness.launches.append(("desktop", record))
        self.desktop_launched = True
        if self.harness.mode == "desktop-identity-mismatch":
            return job_owner.ProcessIdentity(
                pid=202,
                creation_time_100ns=13_400_000_000_000_202,
                executable=r"D:\Other\Foxglove.exe",
            )
        return self.desktop_identity

    def members(self):
        """Handle the members step."""

        if self.closed:
            raise AssertionError("closed Job handle was queried")
        self.harness.events.append("job:members")
        members = []
        if self.base_launched and not self.base_exited:
            members.append(self.base_identity)
        if self.desktop_launched and not self.closed:
            if self.harness.mode != "root-absent":
                members.append(self.desktop_identity)
            members.append(self.desktop_member)
            if (
                self.harness.mode == "late-spawn"
                and self.harness.barrier.is_file()
            ):
                members.append(self.late_member)
        return tuple(members)

    def external_processes(self, executable=None):
        """Handle the external processes step."""

        self.harness.events.append("job:external-scan")
        if (
            self.harness.mode == "external-after-desktop"
            and self.desktop_launched
        ) or (
            self.harness.mode == "late-external"
            and self.harness.barrier.is_file()
        ):
            if not self._recorded_external:
                self._recorded_external.append(self.external_identity)
            return tuple(self._recorded_external)
        return ()

    def require_owned_identity(self, identity):
        """Require owned identity."""

        if (
            self.harness.mode
            in {"external-after-desktop", "late-external"}
            and identity == self.desktop_identity
        ):
            if self.external_processes():
                raise job_owner.OwnershipFailure(
                    job_owner.FAIL_DESKTOP_HANDOFF,
                    "single-instance handoff",
                )
        return identity

    def poll(self, identity):
        """Handle the poll step."""

        if identity != self.base_identity:
            return None
        if self.harness.mode == "base-exit-nonzero":
            self.base_exited = True
            return 7
        if self.harness.second_seen:
            self.harness.write_base_summary()
            self.base_exited = True
            return 0
        return None

    def request_owned_desktop_close(
        self,
        *,
        grace_seconds=10.0,
        reject_external=True,
    ):
        """Handle the request owned desktop close step."""

        self.harness.events.append("desktop:close-request")
        if self.harness.mode == "external-during-close":
            self._recorded_external.append(self.external_identity)
            self.close()
            raise job_owner.OwnershipFailure(
                job_owner.FAIL_DESKTOP_HANDOFF,
                "external process during close",
            )
        self.closed = True
        self.harness.events.append("job:close")
        return job_owner.CloseSummary(
            requested=(self.desktop_identity, self.desktop_member),
            graceful=(self.desktop_identity,),
            forced=(self.desktop_member,),
        )

    def close(self) -> None:
        """Close all resources owned by this helper."""

        if not self.closed:
            self.closed = True
            self.harness.events.append("job:close")


__all__ = [name for name in globals() if not name.startswith("__")]
