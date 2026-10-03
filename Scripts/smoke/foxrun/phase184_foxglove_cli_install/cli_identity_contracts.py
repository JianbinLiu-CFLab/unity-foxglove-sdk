#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Reversible Foxglove CLI installer for Phase184-H tooling provenance."""

from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_foxglove_cli_install.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

import argparse
import contextlib
import ctypes
import dataclasses
import datetime as dt
import hashlib
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
import urllib.parse
import urllib.request
from collections.abc import Callable, Mapping, Sequence
from ctypes import wintypes
from typing import Any

_REAL_POPEN_TYPE = subprocess.Popen

SCRIPT_PATH = pathlib.Path(__file__).resolve()
REPOSITORY_ROOT = SCRIPT_PATH.parents[3]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))

from Scripts.smoke.foxrun import (
    phase184_foxglove_desktop_live_protocol as protocol,
)


RELEASE_ENDPOINT = (
    "https://api.github.com/repos/foxglove/foxglove-cli/releases/latest"
)
DEFAULT_RECEIPT_PATH = (
    REPOSITORY_ROOT
    / "build"
    / "phase184"
    / "tooling"
    / "foxglove-cli-install-receipt.json"
)
CLI_ISOLATED_USERPROFILE = str(
    REPOSITORY_ROOT
    / "build"
    / "phase184"
    / "tooling"
    / "isolated-cli-user-profile"
)

NO_PREVIOUS_SHA256 = "0" * 64
MAX_RELEASE_BYTES = 256 * 1024
MAX_RELEASE_ASSETS = 256
MAX_DOWNLOAD_BYTES = 256 * 1024 * 1024
MAX_COMMAND_OUTPUT_BYTES = 4096
MAX_VERIFIED_CLI_DOCUMENT_BYTES = 16 * 1024
COMMAND_TIMEOUT_SECONDS = 30
NETWORK_TIMEOUT_SECONDS = 60
GITHUB_API_VERSION = "2022-11-28"
_BACKUP_HASH_CHARACTERS = 12
_MAX_BACKUP_REVISION_CHARACTERS = 48
_MAX_WINDOWS_PATH_CHARACTERS = 32767
_MAX_DOWNLOAD_HOP_URL_CHARACTERS = 8192
_GITHUB_ASSET_DIGEST = re.compile(r"^sha256:([0-9a-f]{64})$")
_OFFICIAL_DOWNLOAD_HOSTS = frozenset(
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    }
)
_MINIMAL_PROCESS_ENVIRONMENT_NAMES = frozenset(
    {
        "COMSPEC",
        "NUMBER_OF_PROCESSORS",
        "PATH",
        "PATHEXT",
        "PROCESSOR_ARCHITECTURE",
        "SYSTEMROOT",
        "TEMP",
        "TMP",
        "WINDIR",
    }
)


def _fail(message: object) -> protocol.AcceptanceFailure:
    """Return a stable fail-closed CLI provenance error."""
    return protocol.AcceptanceFailure(protocol.FAIL_CLI_PROVENANCE, message)


@dataclasses.dataclass(frozen=True)
class ReleaseAsset:
    """Official release metadata for the selected Windows CLI asset."""

    release_tag: str
    release_version: str
    asset_name: str
    asset_url: str
    asset_size: int
    asset_sha256: str


@dataclasses.dataclass(frozen=True)
class VerifiedCliIdentity:
    """Immutable, summary-safe identity for one verified CLI installation."""

    installed_path: str
    installed_version: str
    installed_sha256: str
    release_tag: str
    asset_url: str
    architecture: str
    receipt_path: str

    def __post_init__(self) -> None:
        """Normalize and validate the externally supplied installation identity."""

        installed_path = _validated_windows_path(
            self.installed_path,
            "Verified Foxglove CLI installed path",
        )
        receipt_path = _validated_windows_path(
            self.receipt_path,
            "Verified Foxglove CLI receipt path",
        )
        installed_version = protocol.normalize_semantic_version(
            self.installed_version
        )
        installed_sha256 = protocol.validate_sha256(
            self.installed_sha256
        )
        if (
            not isinstance(self.release_tag, str)
            or self.release_tag != self.release_tag.strip()
            or "\r" in self.release_tag
            or "\n" in self.release_tag
            or protocol.normalize_semantic_version(self.release_tag)
            != installed_version
        ):
            raise _fail("Verified Foxglove CLI release tag is invalid.")
        protocol.validate_official_asset_url(
            self.asset_url,
            expected_release_version=installed_version,
        )
        if self.architecture != protocol.CLI_ARCHITECTURE:
            raise _fail("Verified Foxglove CLI architecture is invalid.")

        object.__setattr__(self, "installed_path", installed_path)
        object.__setattr__(self, "receipt_path", receipt_path)
        object.__setattr__(self, "installed_version", installed_version)
        object.__setattr__(self, "installed_sha256", installed_sha256)
        document = self._document()
        encoded = json.dumps(
            document,
            allow_nan=False,
            ensure_ascii=True,
            separators=(",", ":"),
            sort_keys=True,
        ).encode("utf-8")
        if len(encoded) > MAX_VERIFIED_CLI_DOCUMENT_BYTES:
            raise _fail("Verified Foxglove CLI identity document is too large.")

    def _document(self) -> dict[str, str]:
        """Build the validated identity document."""

        return {
            "architecture": self.architecture,
            "assetUrl": self.asset_url,
            "installedPath": self.installed_path,
            "installedSha256": self.installed_sha256,
            "installedVersion": self.installed_version,
            "receiptPath": self.receipt_path,
            "releaseTag": self.release_tag,
        }

    def to_document(self) -> dict[str, str]:
        """Return a bounded public summary with no rollback-only receipt data."""

        return self._document()


@dataclasses.dataclass(frozen=True, slots=True)
class ExecutableFileIdentity:
    """Stable Windows file identity captured from an open executable handle."""

    volume_serial: int
    file_id: int

    def __post_init__(self) -> None:
        """Reject malformed Windows file identities."""

        if (
            isinstance(self.volume_serial, bool)
            or not isinstance(self.volume_serial, int)
            or self.volume_serial < 0
            or isinstance(self.file_id, bool)
            or not isinstance(self.file_id, int)
            or self.file_id < 0
        ):
            raise _fail("Executable file identity is invalid.")


@dataclasses.dataclass(frozen=True, slots=True)
class ExecutableSnapshot:
    """One identity plus content digest from the held read-only lease."""

    identity: ExecutableFileIdentity
    sha256: str

    def __post_init__(self) -> None:
        """Validate the leased executable identity and digest."""

        if not isinstance(self.identity, ExecutableFileIdentity):
            raise _fail("Executable snapshot identity is invalid.")
        object.__setattr__(
            self,
            "sha256",
            protocol.validate_sha256(self.sha256),
        )


@dataclasses.dataclass(frozen=True)
class _BoundedProcessResult:
    """Captured result from one output-bounded child process."""

    returncode: int
    stdout: bytes
    stderr: bytes


@dataclasses.dataclass(frozen=True)
class InstallerDependencies:
    """Injectable side effects used by the reversible installer transaction."""

    release_fetcher: Callable[[str], object]
    downloader: Callable[[str, str], None]
    command_runner: Callable[
        [str, tuple[str, ...], Mapping[str, str]],
        str,
    ]
    command_resolver: Callable[[Mapping[str, str]], str]
    clock: Callable[[], dt.datetime]
    atomic_replacer: Callable[[str, str], None]
    filesystem: Any
    process_environment: Mapping[str, str]
    executable_lease_factory: Callable[[str], Any]


__all__ = [name for name in globals() if not name.startswith("__")]
