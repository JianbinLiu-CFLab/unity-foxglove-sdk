from __future__ import annotations
from .class_coordinator_harness_support import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_desktop_live_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _CoordinatorHarness_fixtures:
    class _DesktopExecutableLease:
        """Represent the desktop executable lease contract."""

        def __init__(self, harness):
            """Initialize the desktop executable lease."""

            self.harness = harness
            self.active = False

        def __enter__(self):
            """Enter the desktop executable lease context."""

            self.active = True
            self.harness.desktop_lease_active = True
            self.harness.events.append("desktop-lease:open")
            return self

        def __exit__(self, exc_type, exc, traceback):
            """Exit the desktop executable lease context without suppressing failures."""

            del exc_type, exc, traceback
            if self.active:
                self.active = False
                self.harness.desktop_lease_active = False
                self.harness.events.append("desktop-lease:close")
            return False

        def _identity(self):
            """Handle the identity step."""

            info = self.harness.desktop.stat()
            return cli_install.ExecutableFileIdentity(
                volume_serial=int(info.st_dev),
                file_id=int(info.st_ino),
            )

        def snapshot(self):
            """Handle the snapshot step."""

            self.harness.desktop_lease_snapshot_count += 1
            count = self.harness.desktop_lease_snapshot_count
            if (
                self.harness.mode == "desktop-updater-before-launch"
                and count == 4
            ):
                self.harness.desktop.write_bytes(
                    b"desktop updater before launch"
                )
            if (
                self.harness.mode == "desktop-updater-after-launch"
                and count == 5
            ):
                self.harness.desktop.write_bytes(
                    b"desktop updater after launch"
                )
            if (
                self.harness.mode == "desktop-lease-interrupt"
                and count == 4
            ):
                raise KeyboardInterrupt("injected lease interruption")
            self.harness.events.append("desktop-lease:snapshot")
            return cli_install.ExecutableSnapshot(
                identity=self._identity(),
                sha256=self.harness.hash_file(self.harness.desktop),
            )

        def path_identity(self):
            """Handle the path identity step."""

            self.harness.events.append("desktop-lease:path-identity")
            return self._identity()
    def make_desktop_lease(self, _path):
        """Build desktop lease."""

        if self.mode == "desktop-reparse":
            raise live_protocol.AcceptanceFailure(
                live_protocol.FAIL_DESKTOP_PREFLIGHT,
                "injected reparse component",
            )
        return self._DesktopExecutableLease(self)
    def make_owner(self, desktop_executable):
        """Build owner."""

        if self.mode == "job-create-fail":
            self.events.append("job:create-fail")
            raise job_owner.OwnershipFailure(
                job_owner.FAIL_JOB_CREATE,
                "injected outer Job creation failure",
            )
        self.owner = FakeJobOwner(self, pathlib.Path(desktop_executable))
        return self.owner
    def verify_identity_exits(self, identities, timeout_seconds):
        """Handle the verify identity exits step."""

        frozen = tuple(identities)
        self.events.append("identity-exit:verify")
        self.exit_verifier_inputs.append(frozen)
        assert (
            timeout_seconds
            == coordinator.IDENTITY_EXIT_TIMEOUT_SECONDS
        )
        if self.owner is not None and not self.owner.closed:
            raise AssertionError("identity exits checked before Job close")
        residual = (
            (frozen[-1],)
            if self.mode == "residual-owned-process" and frozen
            else ()
        )
        exited = tuple(
            identity for identity in frozen if identity not in residual
        )
        return coordinator.IdentityExitVerification(
            exited=exited,
            residual=residual,
        )
    def dependencies(self):
        """Handle the dependencies step."""

        arguments = {
            "repository_root": self.repository,
            "platform_name": "nt",
            "environment": {
                "SystemRoot": r"C:\Windows",
                "PATH": r"C:\Windows\System32",
                "TEMP": str(self.repository / "temp"),
                "GITHUB_TOKEN": "secret",
                "PHASE184G_TOKEN": self.token,
                "ROS_DISTRO": "jazzy",
            },
            "clock": self.clock,
            "sleep": self.clock.sleep,
            "utc_now": lambda: dt.datetime(
                2026,
                7,
                27,
                15,
                30,
                45,
                tzinfo=dt.timezone.utc,
            ),
            "nonce": lambda: "a1b2c3d4e5",
            "is_file": lambda path: pathlib.Path(path).is_file(),
            "path_exists": self.path_exists,
            "make_directory": lambda path: pathlib.Path(path).mkdir(
                parents=True,
                exist_ok=False,
            ),
            "verify_cli": self.verify_cli,
            "sha256_file": self.hash_file,
            "read_desktop_file_version": self.read_desktop_version,
            "read_uri_handler_command": self.read_uri_handler,
            "parse_windows_command_line": coordinator.parse_windows_command_line,
            "read_repository_head": lambda _root: "a" * 40,
            "read_windows_version": lambda: "Microsoft Windows 11 10.0.26100",
            "reserve_port": lambda requested: self._reserve_port(requested),
            "port_is_bindable": lambda host, port: self._port_is_bindable(
                host,
                port,
            ),
            "job_owner_factory": self.make_owner,
            "verify_identities_exited": self.verify_identity_exits,
            "read_log_lines": self.read_log_lines,
            "coordinator_logs_within_bound": (
                lambda _paths, _bound: self.mode
                != "coordinator-log-overflow"
            ),
            "load_json_snapshot": self.load_json_snapshot,
            "write_json_atomic": self.write_json_atomic,
            "remove_owned_file": self.remove_owned_file,
        }
        dependency_fields = {
            field.name
            for field in dataclasses.fields(
                coordinator.CoordinatorDependencies
            )
        }
        if "desktop_executable_lease_factory" in dependency_fields:
            arguments["desktop_executable_lease_factory"] = (
                self.make_desktop_lease
            )
        return coordinator.CoordinatorDependencies(
            **arguments,
        )
    def path_exists(self, path):
        """Handle the path exists step."""

        candidate = pathlib.Path(path)
        if (
            self.mode == "base-ready-path-error"
            and candidate == self.base_ready
        ):
            raise OSError("injected readiness path failure")
        if (
            self.mode == "delayed-base-ready"
            and candidate == self.base_ready
            and not candidate.exists()
            and self.base_ready_at is not None
            and self.clock.value >= self.base_ready_at
        ):
            self.write_base_ready()
        return candidate.exists()
    def _reserve_port(self, requested):
        """Handle the reserve port step."""

        self.events.append("port:reserve")
        if self.mode == "busy-port":
            raise live_protocol.AcceptanceFailure(
                live_protocol.FAIL_DESKTOP_PREFLIGHT,
                "injected busy explicit loopback port",
            )
        self.assert_loopback_port(requested)
        return FakeReservation(self, self.port)
    def assert_loopback_port(self, requested):
        """Handle the assert loopback port step."""

        if requested is not None:
            assert requested == self.port
    def _port_is_bindable(self, host, port):
        """Handle the port is bindable step."""

        self.events.append("port:probe")
        return (
            host == "127.0.0.1"
            and port == self.port
            and self.mode != "cleanup-port-busy"
        )
class CoordinatorHarness(_CoordinatorHarness_support, _CoordinatorHarness_fixtures):
    """Represent the coordinator harness contract."""
    pass


__all__ = [name for name in globals() if not name.startswith("__")]
