#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regressions for the reversible Phase184-H Foxglove CLI installer."""

from __future__ import annotations

import dataclasses
import datetime as dt
import hashlib
import io
import inspect
import json
import ntpath
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile
import types
import unittest
from unittest import mock

from Scripts.smoke.foxrun import phase184_foxglove_desktop_live_protocol as protocol

try:
    from Scripts.smoke.foxrun import phase184_foxglove_cli_install as installer
except ImportError:
    installer = None


ROOT = pathlib.Path(__file__).resolve().parents[4]
OFFICIAL_ASSET_URL = (
    "https://github.com/foxglove/foxglove-cli/releases/download/"
    "v1.2.3/foxglove-windows-amd64.exe"
)
CURRENT_OFFICIAL_ASSET_NAME = "foxglove-windows-amd64"
CURRENT_OFFICIAL_ASSET_URL = (
    "https://github.com/foxglove/foxglove-cli/releases/download/"
    f"v1.2.3/{CURRENT_OFFICIAL_ASSET_NAME}"
)
INSTALL_PATH = r"C:\Phase184Tests\go\bin\foxglove.exe"
UNC_INSTALL_PATH = r"\\phase184-server\share\go\bin\foxglove.exe"
RECEIPT_PATH = r"C:\Phase184Tests\receipts\foxglove-cli.json"
RELATIVE_RECEIPT_PATH = (
    "build/phase184/tooling/foxglove-cli-install-receipt.json"
)
NEW_BYTES = b"official foxglove cli 1.2.3"
OLD_BYTES = b"local foxglove development build"


def sha256_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest().upper()


def extended_windows_path(path: str) -> str:
    normalized = ntpath.normpath(path)
    if normalized.startswith("\\\\"):
        return "\\\\?\\UNC\\" + normalized[2:]
    return "\\\\?\\" + normalized


def ordinary_windows_path(path: str) -> str:
    normalized = ntpath.normpath(path)
    lowered = normalized.lower()
    if lowered.startswith("\\\\?\\unc\\"):
        return "\\\\" + normalized[8:]
    if lowered.startswith("\\\\?\\"):
        return normalized[4:]
    return normalized


class MappedFilesystem:
    """Maps pure Windows paths into one disposable physical test root."""

    def __init__(self, physical_root: pathlib.Path, events: list[tuple]):
        self.physical_root = physical_root
        self.events = events
        self._temporary_counter = 0
        self.forced_temps: dict[str, str] = {}
        self.corrupt_loaded_receipt = False
        self.raise_after_receipt_write = False
        self.after_receipt_write = None
        self.receipt_write_interruption: BaseException | None = None
        self.backup_copy_interruption: BaseException | None = None
        self.backup_hash_interruption: BaseException | None = None
        self.backup_temp_paths: set[str] = set()
        self.backup_publication_competitor: bytes | None = None
        self.backup_publication_interruption: BaseException | None = None
        self.backup_publication_interrupt_after = False

    def physical(self, virtual_path: str) -> pathlib.Path:
        normalized = ntpath.normcase(
            ordinary_windows_path(str(virtual_path))
        )
        drive, tail = ntpath.splitdrive(normalized)
        drive_name = (drive.rstrip(":\\/") or "unc").replace("\\", "_")
        parts = [
            part
            for part in tail.replace("\\", "/").split("/")
            if part not in ("", ".")
        ]
        return self.physical_root / drive_name / pathlib.Path(*parts)

    def ensure_parent(self, path: str) -> None:
        self.events.append(("ensure-parent", ntpath.normpath(path)))
        self.physical(path).parent.mkdir(parents=True, exist_ok=True)

    def exists(self, path: str) -> bool:
        return self.physical(path).is_file()

    def size(self, path: str) -> int:
        return self.physical(path).stat().st_size

    def sha256(self, path: str) -> str:
        self.events.append(("sha256", ntpath.normpath(path)))
        digest = protocol.sha256_file(self.physical(path))
        if (
            self.backup_hash_interruption is not None
            and any(
                protocol.windows_paths_equal(path, backup_temp)
                for backup_temp in self.backup_temp_paths
            )
        ):
            interruption = self.backup_hash_interruption
            self.backup_hash_interruption = None
            raise interruption
        return digest

    def new_sibling_temp(self, target: str, purpose: str) -> str:
        if purpose in self.forced_temps:
            temporary = self.forced_temps[purpose]
            self.events.append(("temp", purpose, ntpath.normpath(temporary)))
            return temporary
        self._temporary_counter += 1
        directory = ntpath.dirname(target)
        filename = ntpath.basename(target)
        temporary = ntpath.join(
            directory,
            f".{filename}.{purpose}.{self._temporary_counter:02d}.tmp",
        )
        if purpose == "backup":
            self.backup_temp_paths.add(temporary)
        self.events.append(("temp", purpose, ntpath.normpath(temporary)))
        return temporary

    def copy_exclusive(self, source: str, destination: str) -> None:
        self.events.append(
            (
                "copy-exclusive",
                ntpath.normpath(source),
                ntpath.normpath(destination),
            )
        )
        physical_destination = self.physical(destination)
        physical_destination.parent.mkdir(parents=True, exist_ok=True)
        with self.physical(source).open("rb") as input_stream:
            with physical_destination.open("xb") as output_stream:
                if self.backup_copy_interruption is not None:
                    output_stream.write(input_stream.read(1))
                    interruption = self.backup_copy_interruption
                    self.backup_copy_interruption = None
                    raise interruption
                shutil.copyfileobj(input_stream, output_stream)

    def publish_exclusive(self, source: str, destination: str) -> None:
        self.events.append(
            (
                "publish-exclusive",
                ntpath.normpath(source),
                ntpath.normpath(destination),
            )
        )
        if self.backup_publication_competitor is not None:
            competitor = self.backup_publication_competitor
            self.backup_publication_competitor = None
            self.write_bytes(destination, competitor, exclusive=True)
        if (
            self.backup_publication_interruption is not None
            and not self.backup_publication_interrupt_after
        ):
            interruption = self.backup_publication_interruption
            self.backup_publication_interruption = None
            raise interruption
        physical_source = self.physical(source)
        physical_destination = self.physical(destination)
        physical_destination.parent.mkdir(parents=True, exist_ok=True)
        os.link(physical_source, physical_destination)
        physical_source.unlink()
        if self.backup_publication_interruption is not None:
            interruption = self.backup_publication_interruption
            self.backup_publication_interruption = None
            raise interruption

    def remove(self, path: str) -> None:
        self.events.append(("remove", ntpath.normpath(path)))
        try:
            self.physical(path).unlink()
        except FileNotFoundError:
            pass

    def write_receipt(self, path: str, payload: dict[str, object]) -> None:
        self.events.append(("write-receipt", ntpath.normpath(path)))
        protocol.write_json_atomic(self.physical(path), payload)
        if self.after_receipt_write is not None:
            self.after_receipt_write(path, payload)
        if self.raise_after_receipt_write:
            raise RuntimeError("synthetic receipt writer failure")
        if self.receipt_write_interruption is not None:
            interruption = self.receipt_write_interruption
            self.receipt_write_interruption = None
            raise interruption

    def load_receipt(self, path: str) -> dict[str, object]:
        self.events.append(("load-receipt", ntpath.normpath(path)))
        loaded = protocol.load_cli_receipt(self.physical(path))
        if self.corrupt_loaded_receipt:
            loaded["installedVersion"] = "9.9.9"
        return loaded

    def write_bytes(
        self,
        path: str,
        payload: bytes,
        *,
        exclusive: bool = False,
    ) -> None:
        physical = self.physical(path)
        physical.parent.mkdir(parents=True, exist_ok=True)
        if exclusive:
            with physical.open("xb") as stream:
                stream.write(payload)
        else:
            physical.write_bytes(payload)

    def read_bytes(self, path: str) -> bytes:
        return self.physical(path).read_bytes()

    def file_identity(self, path: str) -> tuple[int, int]:
        info = self.physical(path).stat()
        return int(info.st_dev), int(info.st_ino)

    def atomic_replace(self, source: str, destination: str) -> None:
        physical_source = self.physical(source)
        physical_destination = self.physical(destination)
        physical_destination.parent.mkdir(parents=True, exist_ok=True)
        physical_source.replace(physical_destination)


class FakeEnvironment:
    def __init__(
        self,
        physical_root: pathlib.Path,
        *,
        existing: bytes | None = None,
        install_path: str = INSTALL_PATH,
    ):
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
                {
                    "name": protocol.CLI_ASSET_NAME,
                    "browser_download_url": OFFICIAL_ASSET_URL,
                },
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
        self.fetch_urls.append(endpoint)
        self.events.append(("fetch-release", endpoint))
        return self.release

    def download(self, asset_url: str, destination: str) -> None:
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
        if tuple(arguments) != ("version",):
            raise AssertionError("Only the exact version command is permitted.")

    def resolve_command(
        self,
        environment: dict[str, str] | None = None,
    ) -> str:
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
        def __init__(self, environment, path: str):
            self.environment = environment
            self.path = path
            self.active = False

        def __enter__(self):
            self.active = True
            self.environment.active_leases += 1
            self.environment.events.append(
                ("lease-open", ntpath.normpath(self.path))
            )
            return self

        def __exit__(self, exc_type, exc, traceback):
            del exc_type, exc, traceback
            if self.active:
                self.active = False
                self.environment.active_leases -= 1
                self.environment.events.append(
                    ("lease-close", ntpath.normpath(self.path))
                )
            return False

        def snapshot(self):
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
            volume, file_id = self.environment.fs.file_identity(self.path)
            self.environment.events.append(
                ("lease-path-identity", ntpath.normpath(self.path))
            )
            return installer.ExecutableFileIdentity(
                volume_serial=volume,
                file_id=file_id,
            )

    def executable_lease(self, path: str):
        return self._ExecutableLease(self, path)

    def atomic_replace(self, source: str, destination: str) -> None:
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


class Phase184FoxgloveCliInstallTests(unittest.TestCase):
    def setUp(self):
        if installer is None:
            self.fail("phase184_foxglove_cli_install is not implemented")
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.physical_root = pathlib.Path(self.temporary.name)

    def assert_provenance_failure(self, callback) -> protocol.AcceptanceFailure:
        with self.assertRaises(protocol.AcceptanceFailure) as raised:
            callback()
        self.assertEqual(protocol.FAIL_CLI_PROVENANCE, raised.exception.code)
        self.assertLessEqual(
            len(raised.exception.message),
            protocol.MAX_DIAGNOSTIC_CHARACTERS,
        )
        return raised.exception

    def verify_cli(self, *args, **kwargs):
        verifier = getattr(
            installer,
            "verify_installed_cli_provenance",
            None,
        )
        self.assertIsNotNone(
            verifier,
            "the public read-only CLI provenance verifier is required",
        )
        return verifier(*args, **kwargs)

    def prepare_verifier_fixture(
        self,
        environment: FakeEnvironment,
        *,
        binary_bytes: bytes = NEW_BYTES,
        receipt_argument: str = RECEIPT_PATH,
        receipt_overrides: dict[str, object] | None = None,
    ) -> tuple[str, dict[str, object]]:
        environment.fs.write_bytes(INSTALL_PATH, binary_bytes)
        receipt_destination = installer._resolve_receipt_path(
            receipt_argument
        )
        digest = sha256_bytes(binary_bytes)
        receipt: dict[str, object] = {
            "schemaVersion": protocol.CLI_RECEIPT_SCHEMA_VERSION,
            "releaseTag": "v1.2.3",
            "releaseVersion": "1.2.3",
            "architecture": protocol.CLI_ARCHITECTURE,
            "assetName": protocol.CLI_ASSET_NAME,
            "assetUrl": OFFICIAL_ASSET_URL,
            "downloadSha256": digest,
            "downloadVersion": "1.2.3",
            "installedPath": INSTALL_PATH,
            "installedSha256": digest,
            "installedVersion": "1.2.3",
            "previousSha256": installer.NO_PREVIOUS_SHA256,
            "backupPath": (
                r"C:\Phase184Tests\backup\foxglove.previous.exe"
            ),
            "installedUtc": "2026-07-27T12:34:56Z",
        }
        if receipt_overrides is not None:
            receipt.update(receipt_overrides)
        environment.fs.write_receipt(receipt_destination, receipt)
        environment.events.clear()
        environment.fetch_urls.clear()
        return receipt_destination, receipt

    def assert_verifier_read_only(self, environment: FakeEnvironment) -> None:
        mutation_events = {
            "copy-exclusive",
            "download",
            "ensure-parent",
            "publish-exclusive",
            "remove",
            "replace",
            "temp",
            "write-receipt",
        }
        self.assertFalse(
            any(event[0] in mutation_events for event in environment.events)
        )
        self.assertEqual([], environment.fetch_urls)

    def test_cli_contract_locks_endpoint_required_path_and_default_receipt(self):
        self.assertEqual(
            "https://api.github.com/repos/foxglove/foxglove-cli/releases/latest",
            installer.RELEASE_ENDPOINT,
        )
        parsed = installer.parse_args(["--install-path", INSTALL_PATH])
        self.assertEqual(INSTALL_PATH, parsed.install_path)
        self.assertEqual(
            ROOT
            / "build"
            / "phase184"
            / "tooling"
            / "foxglove-cli-install-receipt.json",
            pathlib.Path(parsed.receipt),
        )
        explicit = installer.parse_args(
            ["--install-path", INSTALL_PATH, "--receipt", RECEIPT_PATH]
        )
        self.assertEqual(RECEIPT_PATH, explicit.receipt)
        with (
            mock.patch("sys.stderr", new=io.StringIO()),
            self.assertRaises(SystemExit),
        ):
            installer.parse_args([])

    def test_direct_script_entrypoint_bootstraps_repository_imports(self):
        environment = dict(os.environ)
        environment.pop("PYTHONHOME", None)
        environment.pop("PYTHONPATH", None)
        script = (
            ROOT
            / "Scripts"
            / "smoke"
            / "foxrun"
            / "phase184_foxglove_cli_install.py"
        )

        completed = subprocess.run(
            [sys.executable, str(script), "--help"],
            cwd=ROOT,
            env=environment,
            capture_output=True,
            text=True,
            timeout=30,
            check=False,
        )

        self.assertEqual(
            0,
            completed.returncode,
            completed.stdout + completed.stderr,
        )
        self.assertIn("--install-path", completed.stdout)
        self.assertNotIn("ModuleNotFoundError", completed.stderr)

    def test_read_only_verifier_returns_immutable_exact_identity_and_document(self):
        environment = FakeEnvironment(self.physical_root)
        secret_backup = (
            r"C:\Phase184Tests\DO_NOT_REPORT_SECRET\foxglove.previous.exe"
        )
        receipt_destination, receipt = self.prepare_verifier_fixture(
            environment,
            receipt_overrides={"backupPath": secret_backup},
        )

        identity = self.verify_cli(
            INSTALL_PATH,
            RECEIPT_PATH,
            dependencies=environment.dependencies(),
        )

        identity_type = getattr(installer, "VerifiedCliIdentity", None)
        self.assertIsNotNone(identity_type)
        self.assertIsInstance(identity, identity_type)
        self.assertEqual(INSTALL_PATH, identity.installed_path)
        self.assertEqual("1.2.3", identity.installed_version)
        self.assertEqual(
            receipt["installedSha256"],
            identity.installed_sha256,
        )
        self.assertEqual("v1.2.3", identity.release_tag)
        self.assertEqual(OFFICIAL_ASSET_URL, identity.asset_url)
        self.assertEqual(protocol.CLI_ARCHITECTURE, identity.architecture)
        self.assertEqual(receipt_destination, identity.receipt_path)
        with self.assertRaises(dataclasses.FrozenInstanceError):
            setattr(identity, "installed_version", "9.9.9")

        document = identity.to_document()
        self.assertEqual(
            {
                "architecture": protocol.CLI_ARCHITECTURE,
                "assetUrl": OFFICIAL_ASSET_URL,
                "installedPath": INSTALL_PATH,
                "installedSha256": sha256_bytes(NEW_BYTES),
                "installedVersion": "1.2.3",
                "receiptPath": receipt_destination,
                "releaseTag": "v1.2.3",
            },
            document,
        )
        serialized = json.dumps(
            document,
            ensure_ascii=True,
            sort_keys=True,
        ).encode("utf-8")
        self.assertLessEqual(
            len(serialized),
            installer.MAX_VERIFIED_CLI_DOCUMENT_BYTES,
        )
        self.assertNotIn("DO_NOT_REPORT_SECRET", serialized.decode("utf-8"))
        self.assertNotIn("previousSha256", document)
        self.assertNotIn("backupPath", document)
        self.assertNotIn("installedUtc", document)
        self.assertEqual(
            2,
            sum(
                1
                for event in environment.events
                if event
                == ("run", ntpath.normpath(INSTALL_PATH), ("version",))
            ),
        )
        self.assertEqual(
            1,
            sum(1 for event in environment.events if event[0] == "resolve"),
        )
        self.assert_verifier_read_only(environment)

    def test_read_only_verifier_accepts_scoped_relative_and_default_receipts(self):
        cases = (
            ("relative", RELATIVE_RECEIPT_PATH),
            ("default", str(installer.DEFAULT_RECEIPT_PATH)),
        )
        for name, receipt_argument in cases:
            with self.subTest(name=name):
                environment = FakeEnvironment(self.physical_root / name)
                receipt_destination, _ = self.prepare_verifier_fixture(
                    environment,
                    receipt_argument=receipt_argument,
                )

                identity = self.verify_cli(
                    INSTALL_PATH,
                    receipt_argument,
                    dependencies=environment.dependencies(),
                )

                self.assertEqual(receipt_destination, identity.receipt_path)
                self.assert_verifier_read_only(environment)

    def test_read_only_verifier_rejects_stale_dev_and_live_mismatches(self):
        cases = (
            "stale-path",
            "stale-hash",
            "stale-version",
            "dev-version",
            "resolver-path",
            "resolved-version",
            "resolved-hash",
            "installed-command",
            "resolver-command",
        )
        for case in cases:
            with self.subTest(case=case):
                environment = FakeEnvironment(self.physical_root / case)
                binary_bytes = OLD_BYTES if case == "dev-version" else NEW_BYTES
                receipt_overrides: dict[str, object] = {}
                if case == "stale-path":
                    receipt_overrides["installedPath"] = (
                        r"C:\Phase184Tests\stale\foxglove.exe"
                    )
                elif case == "stale-hash":
                    stale_hash = sha256_bytes(OLD_BYTES)
                    receipt_overrides.update(
                        {
                            "downloadSha256": stale_hash,
                            "installedSha256": stale_hash,
                        }
                    )
                self.prepare_verifier_fixture(
                    environment,
                    binary_bytes=binary_bytes,
                    receipt_overrides=receipt_overrides,
                )
                if case == "stale-version":
                    environment.installed_version = "9.9.9"
                elif case == "resolver-path":
                    environment.resolved_path = (
                        r"C:\Phase184Tests\other\foxglove.exe"
                    )
                    environment.fs.write_bytes(
                        environment.resolved_path,
                        NEW_BYTES,
                    )
                elif case == "resolved-version":
                    environment.resolved_version = "9.9.9"
                elif case == "resolved-hash":
                    environment.after_resolve_hook = (
                        lambda environment=environment:
                        environment.fs.write_bytes(
                            INSTALL_PATH,
                            b"resolved target changed",
                        )
                    )
                elif case == "installed-command":
                    environment.command_failure = RuntimeError(
                        "synthetic command failure with secret detail"
                    )
                elif case == "resolver-command":
                    environment.fail_resolver = True

                failure = self.assert_provenance_failure(
                    lambda environment=environment: self.verify_cli(
                        INSTALL_PATH,
                        RECEIPT_PATH,
                        dependencies=environment.dependencies(),
                    )
                )

                self.assertNotIn("secret detail", failure.message)
                self.assert_verifier_read_only(environment)

    def test_verifier_rejects_swapped_or_stale_candidate_before_execution(self):
        cases = ("swapped-bytes", "stale-path", "stale-hash")
        for case in cases:
            with self.subTest(case=case):
                environment = FakeEnvironment(self.physical_root / case)
                overrides: dict[str, object] = {}
                if case == "stale-path":
                    overrides["installedPath"] = (
                        r"C:\Phase184Tests\stale\foxglove.exe"
                    )
                elif case == "stale-hash":
                    overrides.update(
                        {
                            "downloadSha256": sha256_bytes(OLD_BYTES),
                            "installedSha256": sha256_bytes(OLD_BYTES),
                        }
                    )
                self.prepare_verifier_fixture(
                    environment,
                    receipt_overrides=overrides,
                )
                if case == "swapped-bytes":
                    environment.fs.write_bytes(INSTALL_PATH, OLD_BYTES)
                    environment.events.clear()

                self.assert_provenance_failure(
                    lambda environment=environment: self.verify_cli(
                        INSTALL_PATH,
                        RECEIPT_PATH,
                        dependencies=environment.dependencies(),
                    )
                )

                self.assertFalse(
                    any(event[0] == "run" for event in environment.events)
                )
                self.assert_verifier_read_only(environment)

    def test_candidate_version_children_receive_only_explicit_minimal_environment(self):
        environment = FakeEnvironment(self.physical_root)
        self.prepare_verifier_fixture(environment)

        identity = self.verify_cli(
            INSTALL_PATH,
            RECEIPT_PATH,
            dependencies=environment.dependencies(),
        )

        self.assertEqual("1.2.3", identity.installed_version)
        expected = {
            "PATH": r"C:\Windows\System32",
            "SystemRoot": r"C:\Windows",
            "TEMP": r"C:\Temp",
        }
        self.assertEqual(
            [expected, expected],
            environment.command_environments,
        )
        self.assertEqual([expected], environment.resolver_environments)
        serialized = json.dumps(
            environment.command_environments
            + environment.resolver_environments,
            sort_keys=True,
        )
        for forbidden in (
            "GITHUB_TOKEN",
            "FOXGLOVE_API_KEY",
            "PHASE184G_TOKEN",
            "ROS_DISTRO",
            "RMW_IMPLEMENTATION",
            "UNRELATED",
        ):
            self.assertNotIn(forbidden, serialized)

    def test_resolver_mutation_is_hash_rejected_before_resolved_execution(self):
        environment = FakeEnvironment(self.physical_root)
        self.prepare_verifier_fixture(environment)
        environment.after_resolve_hook = lambda: environment.fs.write_bytes(
            INSTALL_PATH,
            b"resolver swapped executable",
        )

        self.assert_provenance_failure(
            lambda: self.verify_cli(
                INSTALL_PATH,
                RECEIPT_PATH,
                dependencies=environment.dependencies(),
            )
        )

        runs = [
            event
            for event in environment.events
            if event[0] == "run"
        ]
        self.assertEqual(1, len(runs))
        self.assertEqual(0, environment.active_leases)
        self.assertEqual(
            1,
            sum(1 for event in environment.events if event[0] == "lease-close"),
        )

    def test_verifier_holds_read_only_lease_across_pre_post_hash_and_execution(self):
        environment = FakeEnvironment(self.physical_root)
        self.prepare_verifier_fixture(environment)

        self.verify_cli(
            INSTALL_PATH,
            RECEIPT_PATH,
            dependencies=environment.dependencies(),
        )

        event_names = [event[0] for event in environment.events]
        self.assertEqual(1, event_names.count("lease-open"))
        self.assertEqual(1, event_names.count("lease-close"))
        self.assertGreaterEqual(event_names.count("lease-snapshot"), 4)
        self.assertLess(
            event_names.index("lease-open"),
            event_names.index("run"),
        )
        self.assertGreater(
            event_names.index("lease-close"),
            max(
                index
                for index, name in enumerate(event_names)
                if name == "run"
            ),
        )
        self.assertEqual(0, environment.active_leases)
        self.assert_verifier_read_only(environment)

    def test_version_execution_mutation_fails_and_releases_lease(self):
        environment = FakeEnvironment(self.physical_root)
        self.prepare_verifier_fixture(environment)
        environment.after_command_hook = lambda: environment.fs.write_bytes(
            INSTALL_PATH,
            b"mutated while version child returned",
        )

        self.assert_provenance_failure(
            lambda: self.verify_cli(
                INSTALL_PATH,
                RECEIPT_PATH,
                dependencies=environment.dependencies(),
            )
        )

        self.assertEqual(0, environment.active_leases)
        self.assertEqual(
            1,
            sum(1 for event in environment.events if event[0] == "lease-close"),
        )
        self.assertFalse(
            any(event[0] == "resolve" for event in environment.events)
        )

    def test_read_only_verifier_rejects_missing_and_malformed_receipts(self):
        for case in ("missing", "malformed-json", "extra-key"):
            with self.subTest(case=case):
                environment = FakeEnvironment(
                    self.physical_root / case,
                    existing=NEW_BYTES,
                )
                if case == "malformed-json":
                    environment.fs.write_bytes(RECEIPT_PATH, b"{not-json")
                elif case == "extra-key":
                    _, receipt = self.prepare_verifier_fixture(environment)
                    receipt["unexpected"] = "rejected"
                    environment.fs.write_receipt(RECEIPT_PATH, receipt)
                environment.events.clear()

                self.assert_provenance_failure(
                    lambda environment=environment: self.verify_cli(
                        INSTALL_PATH,
                        RECEIPT_PATH,
                        dependencies=environment.dependencies(),
                    )
                )

                self.assertFalse(
                    any(event[0] == "run" for event in environment.events)
                )
                self.assert_verifier_read_only(environment)

    def test_read_only_verifier_production_rejects_non_windows_before_io(self):
        with (
            mock.patch.object(installer.os, "name", "posix"),
            mock.patch(
                "urllib.request.urlopen",
                side_effect=AssertionError("must fail before network"),
            ),
        ):
            self.assert_provenance_failure(
                lambda: self.verify_cli(
                    INSTALL_PATH,
                    RECEIPT_PATH,
                )
            )

    def test_release_selection_requires_one_exact_official_asset_and_matching_tag(self):
        valid = {
            "tag_name": "v1.2.3",
            "assets": [
                {
                    "name": "foxglove-linux-amd64",
                    "browser_download_url": "https://example.invalid/ignored",
                },
                {
                    "name": CURRENT_OFFICIAL_ASSET_NAME,
                    "browser_download_url": CURRENT_OFFICIAL_ASSET_URL,
                },
                {
                    "name": "foxglove-windows-arm64",
                    "browser_download_url": (
                        "https://github.com/foxglove/foxglove-cli/releases/"
                        "download/v1.2.3/foxglove-windows-arm64"
                    ),
                },
            ],
        }
        selected = installer.select_release_asset(valid)
        self.assertEqual("v1.2.3", selected.release_tag)
        self.assertEqual("1.2.3", selected.release_version)
        self.assertEqual(CURRENT_OFFICIAL_ASSET_NAME, selected.asset_name)
        self.assertEqual(CURRENT_OFFICIAL_ASSET_URL, selected.asset_url)

        invalid_releases = (
            {"tag_name": "latest", "assets": valid["assets"]},
            {"tag_name": "v1.2.3", "assets": []},
            {
                "tag_name": "v1.2.3",
                "assets": [
                    {
                        "name": protocol.CLI_ASSET_NAME,
                        "browser_download_url": OFFICIAL_ASSET_URL,
                    },
                    {
                        "name": CURRENT_OFFICIAL_ASSET_NAME,
                        "browser_download_url": CURRENT_OFFICIAL_ASSET_URL,
                    },
                ],
            },
            {
                "tag_name": "v1.2.3",
                "assets": [
                    {
                        "name": protocol.CLI_ASSET_NAME,
                        "browser_download_url": (
                            "https://evil.invalid/foxglove-windows-amd64.exe"
                        ),
                    }
                ],
            },
            {
                "tag_name": "v1.2.3",
                "assets": [
                    {
                        "name": CURRENT_OFFICIAL_ASSET_NAME,
                        "browser_download_url": OFFICIAL_ASSET_URL,
                    }
                ],
            },
            {
                "tag_name": "v1.2.4",
                "assets": [
                    {
                        "name": protocol.CLI_ASSET_NAME,
                        "browser_download_url": OFFICIAL_ASSET_URL,
                    }
                ],
            },
        )
        for release in invalid_releases:
            with self.subTest(release=release):
                self.assert_provenance_failure(
                    lambda release=release: installer.select_release_asset(release)
                )

    def test_successful_new_install_verifies_fresh_resolution_then_writes_receipt(self):
        environment = FakeEnvironment(self.physical_root)
        with mock.patch(
            "urllib.request.urlopen",
            side_effect=AssertionError("tests must not reach the network"),
        ):
            result = installer.main(
                [
                    "--install-path",
                    INSTALL_PATH,
                    "--receipt",
                    RECEIPT_PATH,
                ],
                dependencies=environment.dependencies(),
            )

        self.assertEqual(0, result)
        self.assertEqual([installer.RELEASE_ENDPOINT], environment.fetch_urls)
        self.assertEqual(NEW_BYTES, environment.fs.read_bytes(INSTALL_PATH))
        receipt = environment.fs.load_receipt(RECEIPT_PATH)
        installed_hash = sha256_bytes(NEW_BYTES)
        self.assertEqual(
            {
                "schemaVersion": protocol.CLI_RECEIPT_SCHEMA_VERSION,
                "releaseTag": "v1.2.3",
                "releaseVersion": "1.2.3",
                "architecture": protocol.CLI_ARCHITECTURE,
                "assetName": protocol.CLI_ASSET_NAME,
                "assetUrl": OFFICIAL_ASSET_URL,
                "downloadSha256": installed_hash,
                "downloadVersion": "1.2.3",
                "installedPath": INSTALL_PATH,
                "installedSha256": installed_hash,
                "installedVersion": "1.2.3",
                "previousSha256": installer.NO_PREVIOUS_SHA256,
                "backupPath": installer.build_backup_path(
                    INSTALL_PATH,
                    "none",
                    installer.NO_PREVIOUS_SHA256,
                ),
                "installedUtc": "2026-07-27T12:34:56Z",
            },
            receipt,
        )

        replace_index = next(
            index
            for index, event in enumerate(environment.events)
            if event[0] == "replace"
            and event[2] == ntpath.normpath(INSTALL_PATH)
        )
        exact_run_index = next(
            index
            for index, event in enumerate(environment.events)
            if index > replace_index
            and event == ("run", ntpath.normpath(INSTALL_PATH), ("version",))
        )
        resolve_indexes = [
            index
            for index, event in enumerate(environment.events)
            if event
            == ("resolve", "Get-Command foxglove -CommandType Application")
        ]
        write_index = next(
            index
            for index, event in enumerate(environment.events)
            if event[0] == "write-receipt"
        )
        post_write_run_index = next(
            index
            for index, event in enumerate(environment.events)
            if index > write_index
            and event == ("run", ntpath.normpath(INSTALL_PATH), ("version",))
        )
        load_index = next(
            index
            for index, event in enumerate(environment.events)
            if event[0] == "load-receipt"
        )
        self.assertEqual(2, len(resolve_indexes))
        self.assertLess(exact_run_index, resolve_indexes[0])
        self.assertLess(resolve_indexes[0], write_index)
        self.assertLess(write_index, post_write_run_index)
        self.assertLess(post_write_run_index, resolve_indexes[1])
        self.assertLess(resolve_indexes[1], load_index)

    def test_install_receipt_records_the_exact_selected_official_asset_alias(self):
        environment = FakeEnvironment(self.physical_root)
        environment.release = {
            "tag_name": "v1.2.3",
            "assets": [
                {
                    "name": CURRENT_OFFICIAL_ASSET_NAME,
                    "browser_download_url": CURRENT_OFFICIAL_ASSET_URL,
                },
                {
                    "name": "foxglove-windows-arm64",
                    "browser_download_url": (
                        "https://github.com/foxglove/foxglove-cli/releases/"
                        "download/v1.2.3/foxglove-windows-arm64"
                    ),
                },
            ],
        }

        installer.main(
            ["--install-path", INSTALL_PATH, "--receipt", RECEIPT_PATH],
            dependencies=environment.dependencies(),
        )

        receipt = environment.fs.load_receipt(RECEIPT_PATH)
        self.assertEqual(CURRENT_OFFICIAL_ASSET_NAME, receipt["assetName"])
        self.assertEqual(CURRENT_OFFICIAL_ASSET_URL, receipt["assetUrl"])

    def test_replacement_preserves_revision_and_hash_qualified_backup(self):
        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        installer.main(
            ["--install-path", INSTALL_PATH, "--receipt", RECEIPT_PATH],
            dependencies=environment.dependencies(),
        )

        old_hash = sha256_bytes(OLD_BYTES)
        expected_backup = installer.build_backup_path(
            INSTALL_PATH,
            environment.old_revision,
            old_hash,
        )
        self.assertIn("dev-184-local", ntpath.basename(expected_backup))
        self.assertIn(old_hash[:12], ntpath.basename(expected_backup))
        self.assertEqual(OLD_BYTES, environment.fs.read_bytes(expected_backup))
        receipt = environment.fs.load_receipt(RECEIPT_PATH)
        self.assertEqual(old_hash, receipt["previousSha256"])
        self.assertEqual(expected_backup, receipt["backupPath"])

    def test_supplied_old_revision_changes_only_backup_name(self):
        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        receipt = installer.install_cli(
            INSTALL_PATH,
            RECEIPT_PATH,
            environment.dependencies(),
            previous_revision="supplied revision",
        )
        expected_backup = installer.build_backup_path(
            INSTALL_PATH,
            "supplied revision",
            sha256_bytes(OLD_BYTES),
        )
        self.assertEqual(expected_backup, receipt["backupPath"])
        self.assertEqual("1.2.3", receipt["releaseVersion"])
        replacement_index = next(
            index
            for index, event in enumerate(environment.events)
            if event[0] == "replace"
        )
        self.assertFalse(
            any(
                event
                == ("run", ntpath.normpath(INSTALL_PATH), ("version",))
                for event in environment.events[:replacement_index]
            )
        )

    def test_backup_creation_interruptions_clean_owned_temp_and_reraise(self):
        interruption_factories = (
            ("keyboard", lambda: KeyboardInterrupt("synthetic interrupt")),
            ("system-exit", lambda: SystemExit(75)),
        )
        failure_points = (
            "copy",
            "hash",
            "publication-before",
            "publication-after",
        )
        for failure_point in failure_points:
            for interruption_name, factory in interruption_factories:
                with self.subTest(
                    failure_point=failure_point,
                    interruption=interruption_name,
                ):
                    environment = FakeEnvironment(
                        self.physical_root
                        / f"{failure_point}-{interruption_name}",
                        existing=OLD_BYTES,
                    )
                    old_hash = sha256_bytes(OLD_BYTES)
                    owned_backup = installer.build_backup_path(
                        INSTALL_PATH,
                        environment.old_revision,
                        old_hash,
                    )
                    unrelated_bytes = b"unrelated preserved backup"
                    unrelated_backup = installer.build_backup_path(
                        INSTALL_PATH,
                        "unrelated-backup",
                        sha256_bytes(unrelated_bytes),
                    )
                    environment.fs.write_bytes(
                        unrelated_backup,
                        unrelated_bytes,
                    )
                    interruption = factory()
                    if failure_point == "copy":
                        environment.fs.backup_copy_interruption = interruption
                    elif failure_point == "hash":
                        environment.fs.backup_hash_interruption = interruption
                    elif failure_point.startswith("publication"):
                        environment.fs.backup_publication_interruption = (
                            interruption
                        )
                        environment.fs.backup_publication_interrupt_after = (
                            failure_point == "publication-after"
                        )

                    with self.assertRaises(type(interruption)) as raised:
                        installer.main(
                            [
                                "--install-path",
                                INSTALL_PATH,
                                "--receipt",
                                RECEIPT_PATH,
                            ],
                            dependencies=environment.dependencies(),
                        )

                    self.assertIs(interruption, raised.exception)
                    self.assertEqual(
                        OLD_BYTES,
                        environment.fs.read_bytes(INSTALL_PATH),
                    )
                    if failure_point == "publication-after":
                        self.assertEqual(
                            OLD_BYTES,
                            environment.fs.read_bytes(owned_backup),
                        )
                    else:
                        self.assertFalse(
                            environment.fs.exists(owned_backup)
                        )
                    self.assertEqual(
                        unrelated_bytes,
                        environment.fs.read_bytes(unrelated_backup),
                    )
                    backup_temps = [
                        event[2]
                        for event in environment.events
                        if event[0:2] == ("temp", "backup")
                    ]
                    self.assertEqual(1, len(backup_temps))
                    self.assertFalse(
                        environment.fs.exists(backup_temps[0])
                    )
                    self.assertFalse(environment.fs.exists(RECEIPT_PATH))
                    self.assertFalse(
                        any(
                            event[0] in {"replace", "write-receipt"}
                            for event in environment.events
                        )
                    )

    def test_competitor_winning_backup_publication_is_never_unlinked(self):
        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        old_hash = sha256_bytes(OLD_BYTES)
        deterministic_backup = installer.build_backup_path(
            INSTALL_PATH,
            environment.old_revision,
            old_hash,
        )
        competitor_bytes = b"competitor-owned backup"
        environment.fs.backup_publication_competitor = competitor_bytes

        failure = self.assert_provenance_failure(
            lambda: installer.main(
                [
                    "--install-path",
                    INSTALL_PATH,
                    "--receipt",
                    RECEIPT_PATH,
                ],
                dependencies=environment.dependencies(),
            )
        )

        self.assertIn("backup", failure.message.lower())
        self.assertEqual(OLD_BYTES, environment.fs.read_bytes(INSTALL_PATH))
        self.assertEqual(
            competitor_bytes,
            environment.fs.read_bytes(deterministic_backup),
        )
        backup_temps = [
            event[2]
            for event in environment.events
            if event[0:2] == ("temp", "backup")
        ]
        self.assertEqual(1, len(backup_temps))
        self.assertFalse(environment.fs.exists(backup_temps[0]))
        self.assertFalse(environment.fs.exists(RECEIPT_PATH))
        self.assertEqual(
            1,
            sum(
                1
                for event in environment.events
                if event[0] == "publish-exclusive"
            ),
        )
        self.assertFalse(
            any(event[0] == "replace" for event in environment.events)
        )

    def test_local_backup_publication_has_no_overwrite_semantics(self):
        publisher = getattr(
            installer.LocalFilesystem(),
            "publish_exclusive",
            None,
        )
        self.assertIsNotNone(
            publisher,
            "production backup publication requires a no-overwrite primitive",
        )
        if publisher is None:
            return

        source = self.physical_root / "owned-backup.tmp"
        destination = self.physical_root / "competitor-backup.exe"
        source.write_bytes(OLD_BYTES)
        destination.write_bytes(b"competitor")

        with self.assertRaises(FileExistsError):
            publisher(str(source), str(destination))

        self.assertEqual(OLD_BYTES, source.read_bytes())
        self.assertEqual(b"competitor", destination.read_bytes())

    def test_backup_collision_never_overwrites_different_existing_file(self):
        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        old_hash = sha256_bytes(OLD_BYTES)
        collision = installer.build_backup_path(
            INSTALL_PATH,
            "supplied revision",
            old_hash,
        )
        environment.fs.write_bytes(collision, b"different backup")

        self.assert_provenance_failure(
            lambda: installer.install_cli(
                INSTALL_PATH,
                RECEIPT_PATH,
                environment.dependencies(),
                previous_revision="supplied revision",
            )
        )
        self.assertEqual(OLD_BYTES, environment.fs.read_bytes(INSTALL_PATH))
        self.assertEqual(b"different backup", environment.fs.read_bytes(collision))
        self.assertFalse(environment.fs.exists(RECEIPT_PATH))

    def test_receipt_windows_alias_of_install_target_fails_before_mutation(self):
        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        receipt_alias = INSTALL_PATH.lower().replace("\\", "/")

        self.assert_provenance_failure(
            lambda: installer.main(
                ["--install-path", INSTALL_PATH, "--receipt", receipt_alias],
                dependencies=environment.dependencies(),
            )
        )
        self.assertEqual(OLD_BYTES, environment.fs.read_bytes(INSTALL_PATH))
        self.assertFalse(
            any(
                event[0] in {"replace", "write-receipt"}
                for event in environment.events
            )
        )

    def test_receipt_windows_alias_of_backup_fails_without_overwriting_either_file(self):
        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        old_hash = sha256_bytes(OLD_BYTES)
        backup_path = installer.build_backup_path(
            INSTALL_PATH,
            "supplied revision",
            old_hash,
        )
        environment.fs.write_bytes(backup_path, OLD_BYTES)
        receipt_alias = backup_path.lower().replace("\\", "/")

        self.assert_provenance_failure(
            lambda: installer.install_cli(
                INSTALL_PATH,
                receipt_alias,
                environment.dependencies(),
                previous_revision="supplied revision",
            )
        )
        self.assertEqual(OLD_BYTES, environment.fs.read_bytes(INSTALL_PATH))
        self.assertEqual(OLD_BYTES, environment.fs.read_bytes(backup_path))
        self.assertFalse(
            any(
                event[0] in {"replace", "write-receipt"}
                for event in environment.events
            )
        )

    def test_extended_receipt_alias_of_install_target_rejects_drive_and_unc_forms(self):
        variants = (
            ("drive-ordinary-target", INSTALL_PATH, extended_windows_path(INSTALL_PATH)),
            ("drive-extended-target", extended_windows_path(INSTALL_PATH), INSTALL_PATH),
            (
                "unc-ordinary-target",
                UNC_INSTALL_PATH,
                extended_windows_path(UNC_INSTALL_PATH),
            ),
            (
                "unc-extended-target",
                extended_windows_path(UNC_INSTALL_PATH),
                UNC_INSTALL_PATH,
            ),
        )
        for name, install_path, receipt_alias in variants:
            with self.subTest(name=name):
                environment = FakeEnvironment(
                    self.physical_root / name,
                    existing=OLD_BYTES,
                    install_path=install_path,
                )
                self.assert_provenance_failure(
                    lambda environment=environment,
                    install_path=install_path,
                    receipt_alias=receipt_alias: installer.main(
                        [
                            "--install-path",
                            install_path,
                            "--receipt",
                            receipt_alias,
                        ],
                        dependencies=environment.dependencies(),
                    )
                )
                self.assertEqual(
                    OLD_BYTES,
                    environment.fs.read_bytes(install_path),
                )
                self.assertFalse(
                    any(
                        event[0] in {"replace", "write-receipt"}
                        for event in environment.events
                    )
                )

    def test_extended_receipt_alias_of_backup_rejects_drive_and_unc_forms(self):
        variants = (
            ("drive", INSTALL_PATH),
            ("drive-extended-target", extended_windows_path(INSTALL_PATH)),
            ("unc", UNC_INSTALL_PATH),
            ("unc-extended-target", extended_windows_path(UNC_INSTALL_PATH)),
        )
        for name, install_path in variants:
            with self.subTest(name=name):
                environment = FakeEnvironment(
                    self.physical_root / name,
                    existing=OLD_BYTES,
                    install_path=install_path,
                )
                backup_path = installer.build_backup_path(
                    install_path,
                    "supplied revision",
                    sha256_bytes(OLD_BYTES),
                )
                environment.fs.write_bytes(backup_path, OLD_BYTES)
                if backup_path.lower().startswith("\\\\?\\"):
                    receipt_alias = ordinary_windows_path(backup_path)
                else:
                    receipt_alias = extended_windows_path(backup_path)

                self.assert_provenance_failure(
                    lambda environment=environment,
                    install_path=install_path,
                    receipt_alias=receipt_alias: installer.install_cli(
                        install_path,
                        receipt_alias,
                        environment.dependencies(),
                        previous_revision="supplied revision",
                    )
                )
                self.assertEqual(
                    OLD_BYTES,
                    environment.fs.read_bytes(install_path),
                )
                self.assertEqual(
                    OLD_BYTES,
                    environment.fs.read_bytes(backup_path),
                )
                self.assertFalse(
                    any(
                        event[0] in {"replace", "write-receipt"}
                        for event in environment.events
                    )
                )

    def test_extended_receipt_alias_of_download_temp_rejects_drive_and_unc_forms(self):
        for name, install_path in (
            ("drive", INSTALL_PATH),
            ("unc", UNC_INSTALL_PATH),
        ):
            with self.subTest(name=name):
                environment = FakeEnvironment(
                    self.physical_root / name,
                    existing=OLD_BYTES,
                    install_path=install_path,
                )
                download_temp = ntpath.join(
                    ntpath.dirname(install_path),
                    ".foxglove.forced-download.tmp",
                )
                environment.fs.forced_temps["download"] = download_temp
                receipt_alias = extended_windows_path(download_temp)

                self.assert_provenance_failure(
                    lambda environment=environment,
                    install_path=install_path,
                    receipt_alias=receipt_alias: installer.main(
                        [
                            "--install-path",
                            install_path,
                            "--receipt",
                            receipt_alias,
                        ],
                        dependencies=environment.dependencies(),
                    )
                )
                self.assertEqual(
                    OLD_BYTES,
                    environment.fs.read_bytes(install_path),
                )
                self.assertFalse(
                    any(
                        event[0] in {"download", "replace", "write-receipt"}
                        for event in environment.events
                    )
                )

    def test_relative_receipt_resolves_against_repository_root_for_locked_command(self):
        repository_root = pathlib.PureWindowsPath(r"C:\Phase184Repository")
        expected_receipt = ntpath.normpath(
            ntpath.join(str(repository_root), RELATIVE_RECEIPT_PATH)
        )
        environment = FakeEnvironment(self.physical_root)

        with mock.patch.object(
            installer,
            "REPOSITORY_ROOT",
            repository_root,
        ):
            result = installer.main(
                [
                    "--install-path",
                    INSTALL_PATH,
                    "--receipt",
                    RELATIVE_RECEIPT_PATH,
                ],
                dependencies=environment.dependencies(),
            )

        self.assertEqual(0, result)
        self.assertTrue(environment.fs.exists(expected_receipt))
        self.assertEqual(
            expected_receipt,
            next(
                event[1]
                for event in environment.events
                if event[0] == "write-receipt"
            ),
        )
        self.assertFalse(environment.fs.exists(RELATIVE_RECEIPT_PATH))

    def test_download_version_failure_happens_before_replacement(self):
        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        environment.download_version = "9.9.9"

        self.assert_provenance_failure(
            lambda: installer.main(
                ["--install-path", INSTALL_PATH, "--receipt", RECEIPT_PATH],
                dependencies=environment.dependencies(),
            )
        )
        self.assertEqual(OLD_BYTES, environment.fs.read_bytes(INSTALL_PATH))
        self.assertFalse(any(event[0] == "replace" for event in environment.events))
        self.assertFalse(environment.fs.exists(RECEIPT_PATH))

    def test_post_replace_path_version_and_hash_failures_restore_old_binary(self):
        cases = (
            "resolver",
            "installed-version",
            "resolved-version",
            "hash",
            "replace-raises-after-mutation",
        )
        for case in cases:
            with self.subTest(case=case):
                case_root = self.physical_root / case
                environment = FakeEnvironment(case_root, existing=OLD_BYTES)
                if case == "resolver":
                    environment.resolved_path = (
                        r"C:\Phase184Tests\other\foxglove.exe"
                    )
                    environment.fs.write_bytes(
                        environment.resolved_path,
                        NEW_BYTES,
                    )
                elif case == "installed-version":
                    environment.installed_version = "9.9.9"
                elif case == "resolved-version":
                    environment.resolved_version = "9.9.9"
                else:
                    if case == "hash":
                        def tamper_after_replace(_source, destination):
                            environment.fs.write_bytes(destination, b"tampered")

                        environment.after_replace_hook = tamper_after_replace
                    else:
                        environment.raise_after_replace = True

                self.assert_provenance_failure(
                    lambda environment=environment: installer.main(
                        [
                            "--install-path",
                            INSTALL_PATH,
                            "--receipt",
                            RECEIPT_PATH,
                        ],
                        dependencies=environment.dependencies(),
                    )
                )
                self.assertEqual(
                    OLD_BYTES,
                    environment.fs.read_bytes(INSTALL_PATH),
                )
                self.assertFalse(environment.fs.exists(RECEIPT_PATH))

    def test_post_replace_failure_for_new_install_removes_new_target(self):
        environment = FakeEnvironment(self.physical_root)
        environment.fail_resolver = True
        self.assert_provenance_failure(
            lambda: installer.main(
                ["--install-path", INSTALL_PATH, "--receipt", RECEIPT_PATH],
                dependencies=environment.dependencies(),
            )
        )
        self.assertFalse(environment.fs.exists(INSTALL_PATH))
        self.assertFalse(environment.fs.exists(RECEIPT_PATH))

    def test_receipt_write_or_revalidation_failure_rolls_back_and_prevents_success(self):
        failure_points = (
            "writer",
            "revalidation",
            "target-mutated-after-write",
            "resolver-changed-after-write",
            "preexisting-receipt-restored",
        )
        for failure_point in failure_points:
            with self.subTest(failure_point=failure_point):
                environment = FakeEnvironment(
                    self.physical_root / failure_point,
                    existing=OLD_BYTES,
                )
                previous_receipt = None
                if failure_point == "writer":
                    environment.fs.raise_after_receipt_write = True
                elif failure_point == "revalidation":
                    environment.fs.corrupt_loaded_receipt = True
                elif failure_point == "target-mutated-after-write":
                    environment.fs.after_receipt_write = (
                        lambda _path, _payload: environment.fs.write_bytes(
                            INSTALL_PATH,
                            b"post-write target mutation",
                        )
                    )
                elif failure_point == "resolver-changed-after-write":
                    alternate = r"C:\Phase184Tests\other\foxglove.exe"

                    def change_resolution(_path, _payload):
                        environment.resolved_path = alternate
                        environment.fs.write_bytes(alternate, NEW_BYTES)

                    environment.fs.after_receipt_write = change_resolution
                else:
                    previous_receipt = b"unrelated prior receipt bytes"
                    environment.fs.write_bytes(RECEIPT_PATH, previous_receipt)
                    environment.fs.after_receipt_write = (
                        lambda _path, _payload: environment.fs.write_bytes(
                            INSTALL_PATH,
                            b"post-write target mutation",
                        )
                    )
                self.assert_provenance_failure(
                    lambda environment=environment: installer.main(
                        [
                            "--install-path",
                            INSTALL_PATH,
                            "--receipt",
                            RECEIPT_PATH,
                        ],
                        dependencies=environment.dependencies(),
                    )
                )
                self.assertEqual(
                    OLD_BYTES,
                    environment.fs.read_bytes(INSTALL_PATH),
                )
                if previous_receipt is None:
                    self.assertFalse(environment.fs.exists(RECEIPT_PATH))
                else:
                    self.assertEqual(
                        previous_receipt,
                        environment.fs.read_bytes(RECEIPT_PATH),
                    )
                if failure_point == "revalidation":
                    self.assertTrue(
                        any(
                            event[0] == "load-receipt"
                            for event in environment.events
                        )
                    )

    def test_interruptions_after_binary_replacement_restore_and_reraise(self):
        interruption_factories = (
            ("keyboard", lambda: KeyboardInterrupt("synthetic interrupt")),
            ("system-exit", lambda: SystemExit(73)),
        )
        previous_receipt = b"pre-existing receipt"
        for name, factory in interruption_factories:
            with self.subTest(name=name):
                environment = FakeEnvironment(
                    self.physical_root / name,
                    existing=OLD_BYTES,
                )
                environment.fs.write_bytes(RECEIPT_PATH, previous_receipt)
                interruption = factory()
                environment.replacement_interruption = interruption

                with self.assertRaises(type(interruption)) as raised:
                    installer.main(
                        [
                            "--install-path",
                            INSTALL_PATH,
                            "--receipt",
                            RECEIPT_PATH,
                        ],
                        dependencies=environment.dependencies(),
                    )

                self.assertIs(interruption, raised.exception)
                if isinstance(interruption, SystemExit):
                    self.assertEqual(interruption.code, raised.exception.code)
                self.assertEqual(
                    OLD_BYTES,
                    environment.fs.read_bytes(INSTALL_PATH),
                )
                self.assertEqual(
                    previous_receipt,
                    environment.fs.read_bytes(RECEIPT_PATH),
                )
                self.assertGreaterEqual(
                    sum(
                        1
                        for event in environment.events
                        if event[0] == "replace"
                    ),
                    2,
                )

    def test_interruptions_after_receipt_write_restore_or_remove_receipt_and_reraise(self):
        interruption_factories = (
            ("keyboard", lambda: KeyboardInterrupt("synthetic interrupt")),
            ("system-exit", lambda: SystemExit(74)),
        )
        for interruption_name, factory in interruption_factories:
            for receipt_preexisted in (False, True):
                with self.subTest(
                    interruption=interruption_name,
                    receipt_preexisted=receipt_preexisted,
                ):
                    environment = FakeEnvironment(
                        self.physical_root
                        / f"{interruption_name}-{receipt_preexisted}",
                        existing=OLD_BYTES,
                    )
                    previous_receipt = b"pre-existing receipt"
                    if receipt_preexisted:
                        environment.fs.write_bytes(
                            RECEIPT_PATH,
                            previous_receipt,
                        )
                    interruption = factory()
                    environment.fs.receipt_write_interruption = interruption

                    with self.assertRaises(type(interruption)) as raised:
                        installer.main(
                            [
                                "--install-path",
                                INSTALL_PATH,
                                "--receipt",
                                RECEIPT_PATH,
                        ],
                        dependencies=environment.dependencies(),
                    )

                    self.assertIs(interruption, raised.exception)
                    if isinstance(interruption, SystemExit):
                        self.assertEqual(interruption.code, raised.exception.code)
                    self.assertEqual(
                        OLD_BYTES,
                        environment.fs.read_bytes(INSTALL_PATH),
                    )
                    if receipt_preexisted:
                        self.assertEqual(
                            previous_receipt,
                            environment.fs.read_bytes(RECEIPT_PATH),
                        )
                    else:
                        self.assertFalse(
                            environment.fs.exists(RECEIPT_PATH)
                        )

    def test_interruption_rollback_failure_is_bounded_and_receipt_recovery_is_independent(self):
        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        previous_receipt = b"pre-existing receipt"
        environment.fs.write_bytes(RECEIPT_PATH, previous_receipt)
        environment.fs.receipt_write_interruption = KeyboardInterrupt(
            "synthetic interrupt"
        )
        environment.rollback_failure = RuntimeError(
            "synthetic binary rollback failure"
        )

        try:
            failure = self.assert_provenance_failure(
                lambda: installer.main(
                    [
                        "--install-path",
                        INSTALL_PATH,
                        "--receipt",
                        RECEIPT_PATH,
                    ],
                    dependencies=environment.dependencies(),
                )
            )
        except (KeyboardInterrupt, SystemExit):
            self.fail("interruption bypassed rollback-failure classification")
        self.assertIn("rollback", failure.message.lower())
        self.assertEqual(
            previous_receipt,
            environment.fs.read_bytes(RECEIPT_PATH),
        )
        self.assertTrue(
            any(
                event[0] == "replace"
                and "receipt-rollback" in event[1]
                for event in environment.events
            )
        )

    def test_bounded_process_terminates_and_reaps_stdout_and_stderr_overflow(self):
        runner = getattr(installer, "_run_bounded_process", None)
        self.assertIsNotNone(
            runner,
            "production adapters require one shared bounded process runner",
        )
        if runner is None:
            return

        helper = self.physical_root / "oversized_output_helper.py"
        helper.write_text(
            "import sys, time\n"
            "stream = sys.stdout.buffer if sys.argv[1] == 'stdout' "
            "else sys.stderr.buffer\n"
            f"stream.write(b'x' * ({installer.MAX_COMMAND_OUTPUT_BYTES} + 1))\n"
            "stream.flush()\n"
            "time.sleep(30)\n",
            encoding="utf-8",
        )
        real_popen = subprocess.Popen
        for stream_name in ("stdout", "stderr"):
            with self.subTest(stream=stream_name):
                captured: list[subprocess.Popen] = []

                def capture_process(*args, **kwargs):
                    process = real_popen(*args, **kwargs)
                    captured.append(process)
                    return process

                with mock.patch.object(
                    installer.subprocess,
                    "Popen",
                    side_effect=capture_process,
                ):
                    self.assert_provenance_failure(
                        lambda stream_name=stream_name: runner(
                            [sys.executable, str(helper), stream_name],
                            timeout_seconds=5,
                        )
                    )

                self.assertEqual(1, len(captured))
                process = captured[0]
                self.assertIsNotNone(process.returncode)
                self.assertIsNotNone(process.poll())
                self.assertTrue(process.stdout.closed)
                self.assertTrue(process.stderr.closed)

    def test_bounded_process_preserves_bounded_output_exit_code_and_backs_both_adapters(self):
        runner = getattr(installer, "_run_bounded_process", None)
        result_type = getattr(installer, "_BoundedProcessResult", None)
        self.assertIsNotNone(runner)
        self.assertIsNotNone(result_type)
        if runner is None or result_type is None:
            return

        completed = runner(
            [
                sys.executable,
                "-c",
                (
                    "import sys; "
                    "sys.stdout.buffer.write(b'bounded-out'); "
                    "sys.stderr.buffer.write(b'bounded-err'); "
                    "raise SystemExit(7)"
                ),
            ],
            timeout_seconds=5,
        )
        self.assertEqual(7, completed.returncode)
        self.assertEqual(b"bounded-out", completed.stdout)
        self.assertEqual(b"bounded-err", completed.stderr)

        version_result = types.SimpleNamespace(
            returncode=0,
            stdout=b"1.2.3\n",
            stderr=b"bounded notice",
        )
        with mock.patch.object(
            installer,
            "_run_bounded_process",
            return_value=version_result,
        ) as bounded:
            self.assertEqual(
                "1.2.3\n",
                installer._run_command_production(
                    r"C:\Tools\foxglove.exe",
                    ("version",),
                ),
            )
            bounded.assert_called_once()

        resolver_result = types.SimpleNamespace(
            returncode=0,
            stdout=INSTALL_PATH.encode("utf-8"),
            stderr=b"",
        )
        with mock.patch.object(
            installer,
            "_run_bounded_process",
            return_value=resolver_result,
        ) as bounded:
            self.assertEqual(
                INSTALL_PATH,
                installer._resolve_command_production(),
            )
            bounded.assert_called_once()

    def test_bounded_process_timeout_terminates_and_reaps_child(self):
        runner = getattr(installer, "_run_bounded_process", None)
        self.assertIsNotNone(runner)
        if runner is None:
            return

        real_popen = subprocess.Popen
        captured: list[subprocess.Popen] = []

        def capture_process(*args, **kwargs):
            process = real_popen(*args, **kwargs)
            captured.append(process)
            return process

        with mock.patch.object(
            installer.subprocess,
            "Popen",
            side_effect=capture_process,
        ):
            failure = self.assert_provenance_failure(
                lambda: runner(
                    [
                        sys.executable,
                        "-c",
                        "import time; time.sleep(30)",
                    ],
                    timeout_seconds=0.05,
                )
            )

        self.assertIn("timed out", failure.message.lower())
        self.assertEqual(1, len(captured))
        process = captured[0]
        self.assertIsNotNone(process.returncode)
        self.assertIsNotNone(process.poll())
        self.assertTrue(process.stdout.closed)
        self.assertTrue(process.stderr.closed)

    def test_installer_delegates_windows_identity_to_protocol_helpers(self):
        source = inspect.getsource(installer)
        self.assertNotIn("def _path_key(", source)
        self.assertNotIn("startswith(\"\\\\\\\\?\\\\unc\\\\\")", source)
        self.assertIn("protocol.windows_path_key", source)
        self.assertIn("protocol.windows_paths_equal", source)

    def test_production_dependencies_fail_closed_off_windows_before_network(self):
        with (
            mock.patch.object(installer.os, "name", "posix"),
            mock.patch(
                "urllib.request.urlopen",
                side_effect=AssertionError("must fail before network"),
            ),
        ):
            self.assert_provenance_failure(
                lambda: installer.main(["--install-path", INSTALL_PATH])
            )

    def test_injected_run_never_mutates_real_user_path_and_keeps_diagnostics_stable(self):
        environment = FakeEnvironment(self.physical_root, existing=OLD_BYTES)
        environment.download_version = "secret\r\n" + ("x" * 4096)
        failure = self.assert_provenance_failure(
            lambda: installer.main(
                ["--install-path", INSTALL_PATH, "--receipt", RECEIPT_PATH],
                dependencies=environment.dependencies(),
            )
        )
        self.assertNotIn("secret", failure.message)
        flattened_events = repr(environment.events).lower()
        self.assertNotIn(r"c:\users\ljb\go\bin\foxglove.exe", flattened_events)
        self.assertFalse(
            (pathlib.Path.home() / "go" / "bin" / "foxglove.exe").is_relative_to(
                self.physical_root
            )
        )


if __name__ == "__main__":
    unittest.main()
