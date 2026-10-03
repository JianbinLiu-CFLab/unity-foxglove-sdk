from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_cli_install.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class FakeEnvironment:
    """Represent the fake environment contract."""

    def __init__(
        self,
        physical_root: pathlib.Path,
        *,
        existing: bytes | None = None,
        install_path: str = INSTALL_PATH,
    ):
        """Initialize the fake environment."""

        self.events: list[tuple] = []
        self.fs = MappedFilesystem(physical_root, self.events)
        self.fetch_urls: list[str] = []
        self.release = {
            "tag_name": "v1.2.3",
            "assets": [
                {
                    "name": "foxglove-linux-amd64",
                    "browser_download_url": "https://example.invalid/ignored",
                },
                official_asset(protocol.CLI_ASSET_NAME, OFFICIAL_ASSET_URL),
            ],
        }
        self.download_bytes = NEW_BYTES
        self.download_version = "v1.2.3"
        self.installed_version = "1.2.3"
        self.resolved_version = "v1.2.3"
        self.old_revision = "dev/184-local"
        self.install_path = install_path
        self.resolved_path = install_path
        self.fail_resolver = False
        self.resolved_execution_pending = False
        self.command_failure: Exception | None = None
        self.command_environments: list[dict[str, str] | None] = []
        self.resolver_environments: list[dict[str, str] | None] = []
        self.after_command_hook = None
        self.after_resolve_hook = None
        self.raise_after_replace = False
        self.replacement_interruption: BaseException | None = None
        self.rollback_failure: BaseException | None = None
        self.after_replace_hook = None
        self.process_environment = {
            "SystemRoot": r"C:\Windows",
            "PATH": r"C:\Windows\System32",
            "TEMP": r"C:\Temp",
            "USERPROFILE": r"C:\Users\RealProfileWithCredentials",
            "GITHUB_TOKEN": "secret",
            "FOXGLOVE_API_KEY": "secret",
            "PHASE184G_TOKEN": "p184g_secret",
            "ROS_DISTRO": "jazzy",
            "RMW_IMPLEMENTATION": "rmw_fastrtps_cpp",
            "UNRELATED": "discarded",
        }
        self.active_leases = 0
        self.lease_snapshot_count = 0
        self.before_lease_snapshot_hook = None
        if existing is not None:
            self.fs.write_bytes(install_path, existing)

    def fetch_release(self, endpoint: str):
        """Handle the fetch release step."""

        self.fetch_urls.append(endpoint)
        self.events.append(("fetch-release", endpoint))
        return self.release

    def download(self, asset_url: str, destination: str) -> None:
        """Handle the download step."""

        self.events.append(
            ("download", asset_url, ntpath.normpath(destination))
        )
        self.fs.write_bytes(destination, self.download_bytes, exclusive=True)

    def run_command(
        self,
        executable: str,
        arguments: tuple[str, ...],
        environment: dict[str, str] | None = None,
    ) -> str:
        """Run command."""

        normalized = ntpath.normpath(executable)
        self.events.append(("run", normalized, tuple(arguments)))
        self.command_environments.append(
            None if environment is None else dict(environment)
        )
        self.assert_version_command(arguments)
        if self.command_failure is not None:
            raise self.command_failure
        payload = self.fs.read_bytes(executable)
        if payload == OLD_BYTES:
            return self.old_revision
        if ".download." in normalized:
            return self.download_version
        result = self.installed_version
        if (
            normalized == ntpath.normpath(self.resolved_path)
            and self.resolved_execution_pending
        ):
            self.resolved_execution_pending = False
            result = self.resolved_version
        if self.after_command_hook is not None:
            hook = self.after_command_hook
            self.after_command_hook = None
            hook()
        return result

    @staticmethod
    def assert_version_command(arguments: tuple[str, ...]) -> None:
        """Handle the assert version command step."""

        if tuple(arguments) != ("version",):
            raise AssertionError("Only the exact version command is permitted.")

    def resolve_command(
        self,
        environment: dict[str, str] | None = None,
    ) -> str:
        """Resolve command."""

        self.events.append(
            (
                "resolve",
                "Get-Command foxglove -CommandType Application",
            )
        )
        self.resolver_environments.append(
            None if environment is None else dict(environment)
        )
        if self.fail_resolver:
            raise RuntimeError("synthetic resolver failure")
        if self.after_resolve_hook is not None:
            hook = self.after_resolve_hook
            self.after_resolve_hook = None
            hook()
        self.resolved_execution_pending = True
        return self.resolved_path

    class _ExecutableLease:
        """Represent the executable lease contract."""

        def __init__(self, environment, path: str):
            """Initialize the executable lease."""

            self.environment = environment
            self.path = path
            self.active = False

        def __enter__(self):
            """Enter the executable lease context."""

            self.active = True
            self.environment.active_leases += 1
            self.environment.events.append(
                ("lease-open", ntpath.normpath(self.path))
            )
            return self

        def __exit__(self, exc_type, exc, traceback):
            """Exit the executable lease context without suppressing failures."""

            del exc_type, exc, traceback
            if self.active:
                self.active = False
                self.environment.active_leases -= 1
                self.environment.events.append(
                    ("lease-close", ntpath.normpath(self.path))
                )
            return False

        def snapshot(self):
            """Handle the snapshot step."""

            self.environment.lease_snapshot_count += 1
            if self.environment.before_lease_snapshot_hook is not None:
                hook = self.environment.before_lease_snapshot_hook
                self.environment.before_lease_snapshot_hook = None
                hook()
            volume, file_id = self.environment.fs.file_identity(self.path)
            self.environment.events.append(
                ("lease-snapshot", ntpath.normpath(self.path))
            )
            return installer.ExecutableSnapshot(
                identity=installer.ExecutableFileIdentity(
                    volume_serial=volume,
                    file_id=file_id,
                ),
                sha256=protocol.validate_sha256(
                    self.environment.fs.sha256(self.path)
                ),
            )

        def path_identity(self):
            """Handle the path identity step."""

            volume, file_id = self.environment.fs.file_identity(self.path)
            self.environment.events.append(
                ("lease-path-identity", ntpath.normpath(self.path))
            )
            return installer.ExecutableFileIdentity(
                volume_serial=volume,
                file_id=file_id,
            )

    def executable_lease(self, path: str):
        """Handle the executable lease step."""

        return self._ExecutableLease(self, path)

    def atomic_replace(self, source: str, destination: str) -> None:
        """Handle the atomic replace step."""

        self.events.append(
            (
                "replace",
                ntpath.normpath(source),
                ntpath.normpath(destination),
            )
        )
        self.fs.atomic_replace(source, destination)
        if self.after_replace_hook is not None and ".download." in source:
            hook = self.after_replace_hook
            self.after_replace_hook = None
            hook(source, destination)
        if self.raise_after_replace and ".download." in source:
            self.raise_after_replace = False
            raise RuntimeError("synthetic post-replace failure")
        if self.replacement_interruption is not None and ".download." in source:
            interruption = self.replacement_interruption
            self.replacement_interruption = None
            raise interruption
        if self.rollback_failure is not None and ".rollback." in source:
            failure = self.rollback_failure
            self.rollback_failure = None
            raise failure

    def dependencies(self):
        """Handle the dependencies step."""

        arguments = {
            "release_fetcher": self.fetch_release,
            "downloader": self.download,
            "command_runner": self.run_command,
            "command_resolver": self.resolve_command,
            "clock": lambda: dt.datetime(
                2026,
                7,
                27,
                12,
                34,
                56,
                tzinfo=dt.timezone.utc,
            ),
            "atomic_replacer": self.atomic_replace,
            "filesystem": self.fs,
        }
        dependency_fields = {
            field.name
            for field in dataclasses.fields(installer.InstallerDependencies)
        }
        if "process_environment" in dependency_fields:
            arguments["process_environment"] = self.process_environment
        if "executable_lease_factory" in dependency_fields:
            arguments["executable_lease_factory"] = self.executable_lease
        return installer.InstallerDependencies(
            **arguments,
        )


__all__ = [name for name in globals() if not name.startswith("__")]
