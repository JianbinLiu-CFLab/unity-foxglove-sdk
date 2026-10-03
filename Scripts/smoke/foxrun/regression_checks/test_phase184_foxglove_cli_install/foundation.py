#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regressions for the reversible Phase184-H Foxglove CLI installer."""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_cli_install.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
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
import urllib.request
from unittest import mock
from Scripts.phase192.source_layout import read_split_source
from Scripts.smoke.foxrun import phase184_foxglove_desktop_live_protocol as protocol
try:
    from Scripts.smoke.foxrun import phase184_foxglove_cli_install as installer
except ImportError:
    installer = None
ROOT = pathlib.Path(__file__).resolve().parents[4]
MODULE_PATH = ROOT / "Scripts" / "smoke" / "foxrun" / "phase184_foxglove_cli_install.py"
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
def read_installer_source() -> str:
    """Read the facade and exported installer package sections."""

    return read_split_source(MODULE_PATH)
def sha256_bytes(payload: bytes) -> str:
    """Handle the SHA-256 bytes step."""

    return hashlib.sha256(payload).hexdigest().upper()
def official_asset(
    name: str,
    url: str,
    payload: bytes = NEW_BYTES,
) -> dict[str, object]:
    """Return one GitHub release asset with independent content metadata."""

    return {
        "name": name,
        "browser_download_url": url,
        "size": len(payload),
        "digest": "sha256:" + hashlib.sha256(payload).hexdigest(),
    }
def extended_windows_path(path: str) -> str:
    """Handle the extended windows path step."""

    normalized = ntpath.normpath(path)
    if normalized.startswith("\\\\"):
        return "\\\\?\\UNC\\" + normalized[2:]
    return "\\\\?\\" + normalized
def ordinary_windows_path(path: str) -> str:
    """Handle the ordinary windows path step."""

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
        """Initialize the mapped filesystem."""

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
        """Handle the physical step."""

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
        """Handle the ensure parent step."""

        self.events.append(("ensure-parent", ntpath.normpath(path)))
        self.physical(path).parent.mkdir(parents=True, exist_ok=True)

    def exists(self, path: str) -> bool:
        """Handle the exists step."""

        return self.physical(path).is_file()

    def size(self, path: str) -> int:
        """Handle the size step."""

        return self.physical(path).stat().st_size

    def sha256(self, path: str) -> str:
        """Handle the SHA-256 step."""

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
        """Handle the new sibling temp step."""

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
        """Handle the copy exclusive step."""

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
        """Handle the publish exclusive step."""

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
        """Handle the remove step."""

        self.events.append(("remove", ntpath.normpath(path)))
        try:
            self.physical(path).unlink()
        except FileNotFoundError:
            pass

    def write_receipt(self, path: str, payload: dict[str, object]) -> None:
        """Write receipt."""

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
        """Load receipt."""

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
        """Write bytes."""

        physical = self.physical(path)
        physical.parent.mkdir(parents=True, exist_ok=True)
        if exclusive:
            with physical.open("xb") as stream:
                stream.write(payload)
        else:
            physical.write_bytes(payload)

    def read_bytes(self, path: str) -> bytes:
        """Read bytes."""

        return self.physical(path).read_bytes()

    def file_identity(self, path: str) -> tuple[int, int]:
        """Handle the file identity step."""

        info = self.physical(path).stat()
        return int(info.st_dev), int(info.st_ino)

    def atomic_replace(self, source: str, destination: str) -> None:
        """Handle the atomic replace step."""

        physical_source = self.physical(source)
        physical_destination = self.physical(destination)
        physical_destination.parent.mkdir(parents=True, exist_ok=True)
        physical_source.replace(physical_destination)


__all__ = [name for name in globals() if not name.startswith("__")]
