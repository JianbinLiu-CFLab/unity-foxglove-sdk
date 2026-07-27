#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Reversible Foxglove CLI installer for Phase184-H tooling provenance."""

from __future__ import annotations

import argparse
import contextlib
import dataclasses
import datetime as dt
import json
import ntpath
import os
import pathlib
import re
import shutil
import subprocess
import sys
import threading
import time
import uuid
import urllib.request
from collections.abc import Callable, Mapping, Sequence
from typing import Any

from Scripts.smoke.foxrun import (
    phase184_foxglove_desktop_live_protocol as protocol,
)


RELEASE_ENDPOINT = (
    "https://api.github.com/repos/foxglove/foxglove-cli/releases/latest"
)
REPOSITORY_ROOT = pathlib.Path(__file__).resolve().parents[3]
DEFAULT_RECEIPT_PATH = (
    REPOSITORY_ROOT
    / "build"
    / "phase184"
    / "tooling"
    / "foxglove-cli-install-receipt.json"
)

NO_PREVIOUS_SHA256 = "0" * 64
MAX_RELEASE_BYTES = 256 * 1024
MAX_RELEASE_ASSETS = 256
MAX_DOWNLOAD_BYTES = 256 * 1024 * 1024
MAX_COMMAND_OUTPUT_BYTES = 4096
COMMAND_TIMEOUT_SECONDS = 30
NETWORK_TIMEOUT_SECONDS = 60
_BACKUP_HASH_CHARACTERS = 12
_MAX_BACKUP_REVISION_CHARACTERS = 48
_MAX_WINDOWS_PATH_CHARACTERS = 32767


def _fail(message: object) -> protocol.AcceptanceFailure:
    return protocol.AcceptanceFailure(protocol.FAIL_CLI_PROVENANCE, message)


@dataclasses.dataclass(frozen=True)
class ReleaseAsset:
    release_tag: str
    release_version: str
    asset_url: str


@dataclasses.dataclass(frozen=True)
class _BoundedProcessResult:
    returncode: int
    stdout: bytes
    stderr: bytes


@dataclasses.dataclass(frozen=True)
class InstallerDependencies:
    release_fetcher: Callable[[str], object]
    downloader: Callable[[str, str], None]
    command_runner: Callable[[str, tuple[str, ...]], str]
    command_resolver: Callable[[], str]
    clock: Callable[[], dt.datetime]
    atomic_replacer: Callable[[str, str], None]
    filesystem: Any


class LocalFilesystem:
    """Small production filesystem seam used by the transaction."""

    @staticmethod
    def _path(path: str) -> pathlib.Path:
        return pathlib.Path(path)

    def ensure_parent(self, path: str) -> None:
        self._path(path).parent.mkdir(parents=True, exist_ok=True)

    def exists(self, path: str) -> bool:
        return self._path(path).is_file()

    def size(self, path: str) -> int:
        return self._path(path).stat().st_size

    def sha256(self, path: str) -> str:
        return protocol.sha256_file(self._path(path))

    def new_sibling_temp(self, target: str, purpose: str) -> str:
        safe_purpose = re.sub(r"[^a-z0-9-]+", "-", purpose.lower()).strip("-")
        if not safe_purpose:
            raise _fail("CLI temporary-file purpose is invalid.")
        directory = ntpath.dirname(target)
        filename = ntpath.basename(target)
        temporary = ntpath.join(
            directory,
            f".{filename}.{safe_purpose}.{uuid.uuid4().hex}.tmp",
        )
        if len(temporary) > _MAX_WINDOWS_PATH_CHARACTERS:
            raise _fail("CLI sibling temporary path is too long.")
        return temporary

    def copy_exclusive(self, source: str, destination: str) -> None:
        destination_path = self._path(destination)
        destination_path.parent.mkdir(parents=True, exist_ok=True)
        with self._path(source).open("rb") as input_stream:
            with destination_path.open("xb") as output_stream:
                shutil.copyfileobj(
                    input_stream,
                    output_stream,
                    length=1024 * 1024,
                )
                output_stream.flush()
                os.fsync(output_stream.fileno())

    def publish_exclusive(self, source: str, destination: str) -> None:
        os.rename(self._path(source), self._path(destination))

    def remove(self, path: str) -> None:
        with contextlib.suppress(FileNotFoundError):
            self._path(path).unlink()

    def write_receipt(
        self,
        path: str,
        payload: Mapping[str, object],
    ) -> None:
        protocol.write_json_atomic(self._path(path), payload)

    def load_receipt(self, path: str) -> dict[str, object]:
        return protocol.load_cli_receipt(self._path(path))


def _validated_windows_path(path: object, label: str) -> str:
    try:
        value = os.fspath(path)
    except TypeError as exc:
        raise _fail(f"{label} must be one absolute Windows path.") from exc
    protocol.windows_path_key(value, label=label)
    return ntpath.normpath(value)


def _resolve_receipt_path(path: object) -> str:
    try:
        value = os.fspath(path)
    except TypeError as exc:
        raise _fail(
            "Foxglove CLI receipt path must be a Windows path."
        ) from exc
    if (
        not isinstance(value, str)
        or not value
        or len(value) > _MAX_WINDOWS_PATH_CHARACTERS
        or "\x00" in value
        or "\r" in value
        or "\n" in value
    ):
        raise _fail("Foxglove CLI receipt path must be a Windows path.")

    drive, _ = ntpath.splitdrive(value)
    if ntpath.isabs(value):
        return _validated_windows_path(value, "Foxglove CLI receipt path")
    if drive:
        raise _fail(
            "Foxglove CLI receipt path must be absolute or repository-relative."
        )

    repository_root = _validated_windows_path(
        REPOSITORY_ROOT,
        "Foxglove CLI repository root",
    )
    resolved = _validated_windows_path(
        ntpath.join(repository_root, value),
        "Foxglove CLI receipt path",
    )
    try:
        common = ntpath.commonpath(
            (
                protocol.windows_path_key(
                    repository_root,
                    label="Foxglove CLI repository root",
                ),
                protocol.windows_path_key(
                    resolved,
                    label="Foxglove CLI receipt path",
                ),
            )
        )
    except ValueError as exc:
        raise _fail(
            "Relative Foxglove CLI receipt path is outside the repository."
        ) from exc
    if common != protocol.windows_path_key(
        repository_root,
        label="Foxglove CLI repository root",
    ):
        raise _fail(
            "Relative Foxglove CLI receipt path is outside the repository."
        )
    return resolved


def _require_distinct_windows_paths(
    paths: Sequence[tuple[str, object]],
) -> None:
    seen: set[str] = set()
    for label, path in paths:
        key = protocol.windows_path_key(path, label=label)
        if key in seen:
            raise _fail(
                "Foxglove CLI transaction paths must be Windows-distinct."
            )
        seen.add(key)


def _revision_slug(revision: object) -> str:
    if not isinstance(revision, str):
        return "unknown"
    candidate = revision.strip()
    candidate = re.sub(r"[^A-Za-z0-9._-]+", "-", candidate)
    candidate = re.sub(r"[-_.]{2,}", "-", candidate).strip("-_.")
    if not candidate:
        return "unknown"
    return candidate[:_MAX_BACKUP_REVISION_CHARACTERS]


def build_backup_path(
    install_path: os.PathLike[str] | str,
    previous_revision: object,
    previous_sha256: object,
) -> str:
    """Build one deterministic revision/hash-qualified sibling backup path."""

    target = _validated_windows_path(install_path, "Foxglove CLI install path")
    digest = protocol.validate_sha256(previous_sha256)
    directory = ntpath.dirname(target)
    filename = ntpath.basename(target)
    stem, extension = ntpath.splitext(filename)
    if not stem:
        raise _fail("Foxglove CLI install filename is invalid.")
    backup = ntpath.join(
        directory,
        (
            f"{stem}.{_revision_slug(previous_revision)}-"
            f"{digest[:_BACKUP_HASH_CHARACTERS]}{extension}"
        ),
    )
    if len(backup) > _MAX_WINDOWS_PATH_CHARACTERS:
        raise _fail("Foxglove CLI backup path is too long.")
    if protocol.windows_paths_equal(backup, target):
        raise _fail("Foxglove CLI backup path must differ from the install path.")
    return backup


def select_release_asset(release: object) -> ReleaseAsset:
    """Select exactly one official Windows amd64 asset from one release."""

    if not isinstance(release, Mapping):
        raise _fail("Foxglove CLI release response is invalid.")
    release_tag = release.get("tag_name")
    release_version = protocol.normalize_semantic_version(release_tag)
    assets = release.get("assets")
    if (
        not isinstance(assets, list)
        or len(assets) > MAX_RELEASE_ASSETS
    ):
        raise _fail("Foxglove CLI release assets are invalid.")

    matches: list[Mapping[str, object]] = []
    for asset in assets:
        if (
            isinstance(asset, Mapping)
            and asset.get("name") == protocol.CLI_ASSET_NAME
        ):
            matches.append(asset)
    if len(matches) != 1:
        raise _fail("Foxglove CLI release must contain exactly one Windows asset.")

    asset_url = matches[0].get("browser_download_url")
    protocol.validate_official_asset_url(
        asset_url,
        expected_release_version=release_version,
    )
    return ReleaseAsset(
        release_tag=str(release_tag),
        release_version=release_version,
        asset_url=str(asset_url),
    )


def _fetch_release_production(endpoint: str) -> object:
    if endpoint != RELEASE_ENDPOINT:
        raise _fail("Foxglove CLI release endpoint is not official.")
    request = urllib.request.Request(
        endpoint,
        headers={
            "Accept": "application/vnd.github+json",
            "User-Agent": "Unity2Foxglove-Phase184H",
        },
        method="GET",
    )
    try:
        with urllib.request.urlopen(
            request,
            timeout=NETWORK_TIMEOUT_SECONDS,
        ) as response:
            raw = response.read(MAX_RELEASE_BYTES + 1)
    except (OSError, ValueError) as exc:
        raise _fail("Foxglove CLI release metadata could not be fetched.") from exc
    if not raw or len(raw) > MAX_RELEASE_BYTES:
        raise _fail("Foxglove CLI release metadata size is invalid.")
    try:
        return json.loads(raw.decode("utf-8"))
    except (UnicodeError, ValueError, RecursionError) as exc:
        raise _fail("Foxglove CLI release metadata is malformed.") from exc


def _download_production(asset_url: str, destination: str) -> None:
    protocol.validate_official_asset_url(asset_url)
    request = urllib.request.Request(
        asset_url,
        headers={"User-Agent": "Unity2Foxglove-Phase184H"},
        method="GET",
    )
    destination_path = pathlib.Path(destination)
    total = 0
    try:
        with urllib.request.urlopen(
            request,
            timeout=NETWORK_TIMEOUT_SECONDS,
        ) as response:
            with destination_path.open("xb") as output_stream:
                while True:
                    chunk = response.read(min(1024 * 1024, MAX_DOWNLOAD_BYTES - total + 1))
                    if not chunk:
                        break
                    total += len(chunk)
                    if total > MAX_DOWNLOAD_BYTES:
                        raise _fail("Foxglove CLI download exceeds the size bound.")
                    output_stream.write(chunk)
                output_stream.flush()
                os.fsync(output_stream.fileno())
    except protocol.AcceptanceFailure:
        with contextlib.suppress(OSError):
            destination_path.unlink()
        raise
    except (OSError, ValueError) as exc:
        with contextlib.suppress(OSError):
            destination_path.unlink()
        raise _fail("Foxglove CLI asset download failed.") from exc
    if total < 1:
        with contextlib.suppress(OSError):
            destination_path.unlink()
        raise _fail("Foxglove CLI asset download is empty.")


def _terminate_and_reap(process: subprocess.Popen[bytes]) -> None:
    if process.poll() is None:
        with contextlib.suppress(OSError):
            process.terminate()
        try:
            process.wait(timeout=1)
        except subprocess.TimeoutExpired:
            with contextlib.suppress(OSError):
                process.kill()
            try:
                process.wait(timeout=5)
            except (OSError, subprocess.SubprocessError) as exc:
                raise _fail(
                    "Bounded Foxglove CLI helper could not be reaped."
                ) from exc
        except OSError as exc:
            raise _fail(
                "Bounded Foxglove CLI helper could not be reaped."
            ) from exc
    else:
        try:
            process.wait(timeout=0)
        except (OSError, subprocess.SubprocessError) as exc:
            raise _fail(
                "Bounded Foxglove CLI helper could not be reaped."
            ) from exc


def _run_bounded_process(
    command: Sequence[str],
    *,
    timeout_seconds: float = COMMAND_TIMEOUT_SECONDS,
) -> _BoundedProcessResult:
    """Run one helper while retaining at most one bounded buffer per stream."""

    if (
        not command
        or any(not isinstance(argument, str) or not argument for argument in command)
        or not isinstance(timeout_seconds, (int, float))
        or timeout_seconds <= 0
    ):
        raise _fail("Bounded Foxglove CLI helper command is invalid.")

    try:
        process = subprocess.Popen(
            list(command),
            shell=False,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
    except (OSError, subprocess.SubprocessError) as exc:
        raise _fail("Bounded Foxglove CLI helper could not start.") from exc
    if process.stdout is None or process.stderr is None:
        _terminate_and_reap(process)
        raise _fail("Bounded Foxglove CLI helper pipes are unavailable.")

    stdout_buffer = bytearray()
    stderr_buffer = bytearray()
    overflow = threading.Event()
    reader_failed = threading.Event()

    def drain(stream: Any, output: bytearray) -> None:
        read_chunk = getattr(stream, "read1", stream.read)
        try:
            while True:
                chunk = read_chunk(4096)
                if not chunk:
                    return
                remaining = MAX_COMMAND_OUTPUT_BYTES - len(output)
                if remaining > 0:
                    output.extend(chunk[:remaining])
                if len(chunk) > remaining:
                    overflow.set()
                    return
        except (OSError, ValueError):
            reader_failed.set()
        finally:
            with contextlib.suppress(OSError, ValueError):
                stream.close()

    readers = (
        threading.Thread(
            target=drain,
            args=(process.stdout, stdout_buffer),
            name="foxglove-cli-stdout",
            daemon=True,
        ),
        threading.Thread(
            target=drain,
            args=(process.stderr, stderr_buffer),
            name="foxglove-cli-stderr",
            daemon=True,
        ),
    )
    for reader in readers:
        reader.start()

    timed_out = False
    cleanup_failure: Exception | None = None
    deadline = time.monotonic() + float(timeout_seconds)
    try:
        while process.poll() is None:
            if overflow.is_set():
                break
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                timed_out = True
                break
            overflow.wait(min(remaining, 0.01))
        if process.poll() is None:
            _terminate_and_reap(process)
        else:
            process.wait(timeout=0)
    except (KeyboardInterrupt, SystemExit):
        try:
            _terminate_and_reap(process)
        except Exception:
            pass
        raise
    except Exception as exc:
        cleanup_failure = exc
        try:
            _terminate_and_reap(process)
        except Exception:
            pass
    finally:
        for reader in readers:
            reader.join(timeout=1)
        for stream in (process.stdout, process.stderr):
            with contextlib.suppress(OSError, ValueError):
                stream.close()
        for reader in readers:
            reader.join(timeout=1)

    if cleanup_failure is not None:
        if isinstance(cleanup_failure, protocol.AcceptanceFailure):
            raise cleanup_failure
        raise _fail("Bounded Foxglove CLI helper failed.") from cleanup_failure
    if any(reader.is_alive() for reader in readers) or reader_failed.is_set():
        raise _fail("Bounded Foxglove CLI helper output could not be drained.")
    if timed_out:
        raise _fail("Bounded Foxglove CLI helper timed out.")
    if overflow.is_set():
        raise _fail("Bounded Foxglove CLI helper output exceeded the size bound.")
    if process.returncode is None:
        raise _fail("Bounded Foxglove CLI helper was not reaped.")
    return _BoundedProcessResult(
        returncode=process.returncode,
        stdout=bytes(stdout_buffer),
        stderr=bytes(stderr_buffer),
    )


def _run_command_production(
    executable: str,
    arguments: tuple[str, ...],
) -> str:
    if arguments != ("version",):
        raise _fail("Foxglove CLI command is not permitted.")
    try:
        completed = _run_bounded_process(
            [executable, *arguments],
            timeout_seconds=COMMAND_TIMEOUT_SECONDS,
        )
    except protocol.AcceptanceFailure:
        raise
    except (OSError, subprocess.SubprocessError) as exc:
        raise _fail("Foxglove CLI version command could not run.") from exc
    if completed.returncode != 0:
        raise _fail("Foxglove CLI version command failed.")
    if (
        not completed.stdout
        or len(completed.stdout) > MAX_COMMAND_OUTPUT_BYTES
    ):
        raise _fail("Foxglove CLI version output size is invalid.")
    try:
        return completed.stdout.decode("utf-8")
    except UnicodeError as exc:
        raise _fail("Foxglove CLI version output is not UTF-8.") from exc


def _resolve_command_production() -> str:
    command = (
        "$resolved = Get-Command foxglove -CommandType Application "
        "-ErrorAction Stop; [Console]::Out.Write($resolved.Source)"
    )
    try:
        completed = _run_bounded_process(
            [
                "powershell.exe",
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                command,
            ],
            timeout_seconds=COMMAND_TIMEOUT_SECONDS,
        )
    except protocol.AcceptanceFailure:
        raise
    except (OSError, subprocess.SubprocessError) as exc:
        raise _fail("Fresh PowerShell Foxglove CLI resolution failed.") from exc
    if completed.returncode != 0:
        raise _fail("Fresh PowerShell Foxglove CLI resolution failed.")
    if (
        not completed.stdout
        or len(completed.stdout) > MAX_COMMAND_OUTPUT_BYTES
    ):
        raise _fail("Fresh PowerShell Foxglove CLI resolution is invalid.")
    try:
        resolved = completed.stdout.decode("utf-8").strip()
    except UnicodeError as exc:
        raise _fail("Fresh PowerShell Foxglove CLI resolution is invalid.") from exc
    if not resolved or "\r" in resolved or "\n" in resolved:
        raise _fail("Fresh PowerShell Foxglove CLI resolution is ambiguous.")
    return resolved


def _atomic_replace_production(source: str, destination: str) -> None:
    os.replace(pathlib.Path(source), pathlib.Path(destination))


def _production_dependencies() -> InstallerDependencies:
    if os.name != "nt":
        raise _fail("Foxglove CLI installation is supported only on Windows.")
    return InstallerDependencies(
        release_fetcher=_fetch_release_production,
        downloader=_download_production,
        command_runner=_run_command_production,
        command_resolver=_resolve_command_production,
        clock=lambda: dt.datetime.now(dt.timezone.utc),
        atomic_replacer=_atomic_replace_production,
        filesystem=LocalFilesystem(),
    )


def _run_version(
    path: str,
    dependencies: InstallerDependencies,
) -> str:
    try:
        raw_version = dependencies.command_runner(path, ("version",))
    except protocol.AcceptanceFailure:
        raise
    except Exception as exc:
        raise _fail("Foxglove CLI version command failed.") from exc
    return protocol.normalize_semantic_version(raw_version)


def _installed_utc(clock: Callable[[], dt.datetime]) -> str:
    try:
        value = clock()
    except Exception as exc:
        raise _fail("Foxglove CLI installation clock failed.") from exc
    if (
        not isinstance(value, dt.datetime)
        or value.tzinfo is None
        or value.utcoffset() is None
    ):
        raise _fail("Foxglove CLI installation clock must be timezone-aware.")
    utc = value.astimezone(dt.timezone.utc)
    return utc.isoformat().replace("+00:00", "Z")


def _remove_quietly(filesystem: Any, path: str | None) -> None:
    if path is None:
        return
    try:
        filesystem.remove(path)
    except Exception:
        pass


def _prepare_backup(
    install_path: str,
    dependencies: InstallerDependencies,
    previous_revision: object | None,
    reserved_paths: Sequence[tuple[str, object]],
) -> tuple[str, str, bool, bool]:
    filesystem = dependencies.filesystem
    if not filesystem.exists(install_path):
        backup_path = build_backup_path(
            install_path,
            "none",
            NO_PREVIOUS_SHA256,
        )
        _require_distinct_windows_paths(
            (*reserved_paths, ("Foxglove CLI backup path", backup_path))
        )
        return NO_PREVIOUS_SHA256, backup_path, False, False

    previous_sha256 = protocol.validate_sha256(filesystem.sha256(install_path))
    revision = previous_revision
    if revision is None:
        try:
            revision = dependencies.command_runner(
                install_path,
                ("version",),
            )
        except Exception:
            revision = "unknown"
    backup_path = build_backup_path(
        install_path,
        revision,
        previous_sha256,
    )
    _require_distinct_windows_paths(
        (*reserved_paths, ("Foxglove CLI backup path", backup_path))
    )
    if filesystem.exists(backup_path):
        if filesystem.sha256(backup_path) != previous_sha256:
            raise _fail(
                "Foxglove CLI backup path already contains a different binary."
            )
        return previous_sha256, backup_path, True, False

    backup_temp = filesystem.new_sibling_temp(backup_path, "backup")
    _require_distinct_windows_paths(
        (
            *reserved_paths,
            ("Foxglove CLI backup path", backup_path),
            ("Foxglove CLI backup temporary path", backup_temp),
        )
    )
    if ntpath.dirname(
        protocol.windows_path_key(
            backup_temp,
            label="Foxglove CLI backup temporary path",
        )
    ) != ntpath.dirname(
        protocol.windows_path_key(
            backup_path,
            label="Foxglove CLI backup path",
        )
    ):
        raise _fail("Foxglove CLI backup temporary must be a sibling.")

    temp_owned = False
    backup_published = False
    try:
        temp_owned = True
        try:
            filesystem.copy_exclusive(install_path, backup_temp)
        except FileExistsError as exc:
            temp_owned = False
            raise _fail(
                "Foxglove CLI backup temporary path already exists."
            ) from exc
        except Exception as exc:
            raise _fail(
                "Existing Foxglove CLI could not be preserved."
            ) from exc
        if filesystem.sha256(backup_temp) != previous_sha256:
            raise _fail("Preserved Foxglove CLI backup hash does not match.")
        try:
            filesystem.publish_exclusive(backup_temp, backup_path)
        except FileExistsError as exc:
            raise _fail(
                "Foxglove CLI backup path was claimed before publication."
            ) from exc
        except Exception as exc:
            raise _fail(
                "Foxglove CLI backup could not be published."
            ) from exc
        backup_published = True
        temp_owned = False
    except BaseException:
        if temp_owned:
            try:
                filesystem.remove(backup_temp)
                if filesystem.exists(backup_temp):
                    raise _fail(
                        "Owned Foxglove CLI backup temporary was not removed."
                    )
            except Exception as cleanup_exc:
                raise _fail(
                    "Owned Foxglove CLI backup temporary cleanup failed."
                ) from cleanup_exc
        raise
    return previous_sha256, backup_path, True, backup_published


def _restore_previous_binary(
    install_path: str,
    backup_path: str,
    previous_sha256: str,
    had_previous: bool,
    dependencies: InstallerDependencies,
) -> None:
    filesystem = dependencies.filesystem
    if not had_previous:
        filesystem.remove(install_path)
        if filesystem.exists(install_path):
            raise _fail("New Foxglove CLI could not be removed during rollback.")
        return

    restore_temp = filesystem.new_sibling_temp(install_path, "rollback")
    try:
        filesystem.copy_exclusive(backup_path, restore_temp)
        if filesystem.sha256(restore_temp) != previous_sha256:
            raise _fail("Foxglove CLI rollback copy hash does not match.")
        dependencies.atomic_replacer(restore_temp, install_path)
        if (
            not filesystem.exists(install_path)
            or filesystem.sha256(install_path) != previous_sha256
        ):
            raise _fail("Foxglove CLI rollback verification failed.")
    finally:
        _remove_quietly(filesystem, restore_temp)


def _verify_installed_identity(
    install_path: str,
    release_version: str,
    download_version: str,
    download_sha256: str,
    dependencies: InstallerDependencies,
) -> tuple[str, str]:
    filesystem = dependencies.filesystem
    installed_version = _run_version(install_path, dependencies)
    installed_sha256 = protocol.validate_sha256(
        filesystem.sha256(install_path)
    )
    if (
        installed_version != release_version
        or installed_version != download_version
        or installed_sha256 != download_sha256
    ):
        raise _fail(
            "Installed Foxglove CLI version or hash does not match the download."
        )

    try:
        resolved_path = dependencies.command_resolver()
    except protocol.AcceptanceFailure:
        raise
    except Exception as exc:
        raise _fail("Fresh PowerShell Foxglove CLI resolution failed.") from exc
    if not protocol.windows_paths_equal(resolved_path, install_path):
        raise _fail(
            "Fresh PowerShell Foxglove CLI path does not match the install target."
        )
    resolved_version = _run_version(resolved_path, dependencies)
    resolved_sha256 = protocol.validate_sha256(
        filesystem.sha256(resolved_path)
    )
    if (
        resolved_version != release_version
        or resolved_version != download_version
        or resolved_version != installed_version
        or resolved_sha256 != download_sha256
        or resolved_sha256 != installed_sha256
    ):
        raise _fail(
            "Freshly resolved Foxglove CLI version or hash does not match."
        )
    return installed_version, installed_sha256


def _restore_previous_receipt(
    receipt_path: str,
    receipt_preexisted: bool,
    receipt_rollback_temp: str | None,
    receipt_previous_sha256: str | None,
    dependencies: InstallerDependencies,
) -> None:
    filesystem = dependencies.filesystem
    if not receipt_preexisted:
        filesystem.remove(receipt_path)
        if filesystem.exists(receipt_path):
            raise _fail("Invalid Foxglove CLI receipt could not be removed.")
        return

    if (
        receipt_rollback_temp is None
        or receipt_previous_sha256 is None
        or not filesystem.exists(receipt_rollback_temp)
    ):
        raise _fail("Previous Foxglove CLI receipt backup is unavailable.")
    try:
        dependencies.atomic_replacer(receipt_rollback_temp, receipt_path)
    except Exception as exc:
        try:
            restored = (
                filesystem.exists(receipt_path)
                and filesystem.sha256(receipt_path)
                == receipt_previous_sha256
            )
        except Exception:
            restored = False
        if not restored:
            raise _fail(
                "Previous Foxglove CLI receipt could not be restored."
            ) from exc
    if (
        not filesystem.exists(receipt_path)
        or filesystem.sha256(receipt_path) != receipt_previous_sha256
    ):
        raise _fail("Previous Foxglove CLI receipt restoration did not verify.")


def _coerce_failure(exc: Exception) -> protocol.AcceptanceFailure:
    if isinstance(exc, protocol.AcceptanceFailure):
        return exc
    return _fail("Foxglove CLI installation failed.")


def install_cli(
    install_path: os.PathLike[str] | str,
    receipt_path: os.PathLike[str] | str,
    dependencies: InstallerDependencies,
    *,
    previous_revision: object | None = None,
) -> dict[str, object]:
    """Install, verify, receipt, and revalidate one official Foxglove CLI."""

    target = _validated_windows_path(
        install_path,
        "Foxglove CLI install path",
    )
    receipt_destination = _resolve_receipt_path(receipt_path)
    _require_distinct_windows_paths(
        (
            ("Foxglove CLI install path", target),
            ("Foxglove CLI receipt path", receipt_destination),
        )
    )

    filesystem = dependencies.filesystem
    download_temp: str | None = None
    receipt_rollback_temp: str | None = None
    receipt_previous_sha256: str | None = None
    retain_receipt_rollback = False
    previous_sha256 = NO_PREVIOUS_SHA256
    backup_path = build_backup_path(target, "none", NO_PREVIOUS_SHA256)
    had_previous = False
    created_backup = False
    replaced = False
    receipt_write_attempted = False
    receipt_preexisted = False
    mutation_transaction_started = False

    try:
        try:
            release = select_release_asset(
                dependencies.release_fetcher(RELEASE_ENDPOINT)
            )
            filesystem.ensure_parent(target)
            download_temp = filesystem.new_sibling_temp(target, "download")
            transaction_paths = (
                ("Foxglove CLI install path", target),
                ("Foxglove CLI receipt path", receipt_destination),
                ("Foxglove CLI download path", download_temp),
            )
            _require_distinct_windows_paths(transaction_paths)
            if ntpath.dirname(
                protocol.windows_path_key(
                    download_temp,
                    label="Foxglove CLI download path",
                )
            ) != ntpath.dirname(
                protocol.windows_path_key(
                    target,
                    label="Foxglove CLI install path",
                )
            ):
                raise _fail(
                    "Foxglove CLI download path must be a sibling temporary."
                )
            dependencies.downloader(release.asset_url, download_temp)
            if (
                not filesystem.exists(download_temp)
                or filesystem.size(download_temp) < 1
                or filesystem.size(download_temp) > MAX_DOWNLOAD_BYTES
            ):
                raise _fail("Downloaded Foxglove CLI size is invalid.")

            download_sha256 = protocol.validate_sha256(
                filesystem.sha256(download_temp)
            )
            download_version = _run_version(download_temp, dependencies)
            if download_version != release.release_version:
                raise _fail(
                    "Downloaded Foxglove CLI version does not match the release."
                )

            (
                previous_sha256,
                backup_path,
                had_previous,
                created_backup,
            ) = _prepare_backup(
                target,
                dependencies,
                previous_revision,
                transaction_paths,
            )
            transaction_paths = (
                *transaction_paths,
                ("Foxglove CLI backup path", backup_path),
            )
        except Exception as exc:
            if created_backup:
                _remove_quietly(filesystem, backup_path)
            if isinstance(exc, protocol.AcceptanceFailure):
                raise
            raise _coerce_failure(exc) from exc

        try:
            mutation_transaction_started = True
            replaced = True
            dependencies.atomic_replacer(download_temp, target)

            installed_version, installed_sha256 = _verify_installed_identity(
                target,
                release.release_version,
                download_version,
                download_sha256,
                dependencies,
            )

            receipt = {
                "schemaVersion": protocol.CLI_RECEIPT_SCHEMA_VERSION,
                "releaseTag": release.release_tag,
                "releaseVersion": release.release_version,
                "architecture": protocol.CLI_ARCHITECTURE,
                "assetName": protocol.CLI_ASSET_NAME,
                "assetUrl": release.asset_url,
                "downloadSha256": download_sha256,
                "downloadVersion": download_version,
                "installedPath": target,
                "installedSha256": installed_sha256,
                "installedVersion": installed_version,
                "previousSha256": previous_sha256,
                "backupPath": backup_path,
                "installedUtc": _installed_utc(dependencies.clock),
            }
            protocol.validate_cli_receipt(
                receipt,
                target,
                installed_version,
                installed_sha256,
            )
            receipt_preexisted = filesystem.exists(receipt_destination)
            if receipt_preexisted:
                receipt_previous_sha256 = protocol.validate_sha256(
                    filesystem.sha256(receipt_destination)
                )
                receipt_rollback_temp = filesystem.new_sibling_temp(
                    receipt_destination,
                    "receipt-rollback",
                )
                _require_distinct_windows_paths(
                    (
                        *transaction_paths,
                        (
                            "Foxglove CLI receipt rollback path",
                            receipt_rollback_temp,
                        ),
                    )
                )
                try:
                    filesystem.copy_exclusive(
                        receipt_destination,
                        receipt_rollback_temp,
                    )
                except Exception as exc:
                    raise _fail(
                        "Previous Foxglove CLI receipt could not be preserved."
                    ) from exc
                if (
                    filesystem.sha256(receipt_rollback_temp)
                    != receipt_previous_sha256
                ):
                    raise _fail(
                        "Previous Foxglove CLI receipt backup hash does not match."
                    )
            receipt_write_attempted = True
            filesystem.write_receipt(receipt_destination, receipt)
            (
                post_write_version,
                post_write_sha256,
            ) = _verify_installed_identity(
                target,
                release.release_version,
                download_version,
                download_sha256,
                dependencies,
            )
            reloaded = filesystem.load_receipt(receipt_destination)
            validated = protocol.validate_cli_receipt(
                reloaded,
                target,
                post_write_version,
                post_write_sha256,
            )
            if validated != receipt:
                raise _fail("Reloaded Foxglove CLI receipt is not exact.")
            _remove_quietly(filesystem, receipt_rollback_temp)
            receipt_rollback_temp = None
            return validated
        except BaseException as exc:
            binary_rollback_error: BaseException | None = None
            receipt_rollback_error: BaseException | None = None
            if replaced:
                try:
                    _restore_previous_binary(
                        target,
                        backup_path,
                        previous_sha256,
                        had_previous,
                        dependencies,
                    )
                except BaseException as rollback_exc:
                    binary_rollback_error = rollback_exc
            if receipt_write_attempted:
                try:
                    _restore_previous_receipt(
                        receipt_destination,
                        receipt_preexisted,
                        receipt_rollback_temp,
                        receipt_previous_sha256,
                        dependencies,
                    )
                    _remove_quietly(filesystem, receipt_rollback_temp)
                    receipt_rollback_temp = None
                except BaseException as rollback_exc:
                    receipt_rollback_error = rollback_exc
                    retain_receipt_rollback = True
            if created_backup and binary_rollback_error is None:
                _remove_quietly(filesystem, backup_path)
            if (
                binary_rollback_error is not None
                or receipt_rollback_error is not None
            ):
                raise _fail(
                    "Foxglove CLI installation failed and rollback did not complete."
                ) from (binary_rollback_error or receipt_rollback_error)
            if isinstance(exc, protocol.AcceptanceFailure):
                raise
            if isinstance(exc, Exception):
                raise _coerce_failure(exc) from exc
            raise
    finally:
        _remove_quietly(filesystem, download_temp)
        if not retain_receipt_rollback:
            _remove_quietly(filesystem, receipt_rollback_temp)
        if not mutation_transaction_started and created_backup:
            _remove_quietly(filesystem, backup_path)


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Install and provenance-check the official Windows amd64 "
            "Foxglove CLI."
        )
    )
    parser.add_argument("--install-path", required=True)
    parser.add_argument("--receipt", default=str(DEFAULT_RECEIPT_PATH))
    return parser.parse_args(argv)


def main(
    argv: Sequence[str] | None = None,
    dependencies: InstallerDependencies | None = None,
) -> int:
    args = parse_args(argv)
    active_dependencies = dependencies or _production_dependencies()
    install_cli(
        args.install_path,
        args.receipt,
        active_dependencies,
    )
    return 0


def _entrypoint() -> int:
    try:
        return main()
    except protocol.AcceptanceFailure as exc:
        print(str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(_entrypoint())
