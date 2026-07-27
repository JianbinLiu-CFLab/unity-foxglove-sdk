#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Pure provenance protocol for Phase184-H Foxglove tooling acceptance."""

from __future__ import annotations

import contextlib
import datetime as dt
import hashlib
import json
import ntpath
import os
import pathlib
import re
import uuid
from typing import Any, Mapping
from urllib.parse import urlsplit


CLI_RECEIPT_SCHEMA_VERSION = 1
CLI_ARCHITECTURE = "windows-amd64"
CLI_ASSET_NAME = "foxglove-windows-amd64.exe"
CLI_RECEIPT_KEYS = frozenset(
    {
        "schemaVersion",
        "releaseTag",
        "releaseVersion",
        "architecture",
        "assetName",
        "assetUrl",
        "downloadSha256",
        "downloadVersion",
        "installedPath",
        "installedSha256",
        "installedVersion",
        "previousSha256",
        "backupPath",
        "installedUtc",
    }
)

MAX_DIAGNOSTIC_CHARACTERS = 512
MAX_RECEIPT_BYTES = 32 * 1024

FAIL_CLI_PROVENANCE = "FAIL_CLI_PROVENANCE"
FAIL_DESKTOP_PREFLIGHT = "FAIL_DESKTOP_PREFLIGHT"
FAIL_DESKTOP_START = "FAIL_DESKTOP_START"
FAIL_DESKTOP_IDENTITY = "FAIL_DESKTOP_IDENTITY"
FAIL_DESKTOP_CONNECTION = "FAIL_DESKTOP_CONNECTION"
FAIL_FOXRUN_CHILD = "FAIL_FOXRUN_CHILD"
FAIL_EVIDENCE = "FAIL_EVIDENCE"
FAIL_CLEANUP = "FAIL_CLEANUP"

TERMINAL_FAILURE_CODES = frozenset(
    {
        FAIL_CLI_PROVENANCE,
        FAIL_DESKTOP_PREFLIGHT,
        FAIL_DESKTOP_START,
        FAIL_DESKTOP_IDENTITY,
        FAIL_DESKTOP_CONNECTION,
        FAIL_FOXRUN_CHILD,
        FAIL_EVIDENCE,
        FAIL_CLEANUP,
    }
)

_SEMANTIC_VERSION = re.compile(
    r"\Av?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\Z"
)
_UPPER_SHA256 = re.compile(r"\A[0-9A-F]{64}\Z")
_UTC_TIMESTAMP = re.compile(
    r"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T"
    r"[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,6})?Z\Z"
)
_MAX_VERSION_CHARACTERS = 64
_MAX_URL_CHARACTERS = 2048
_MAX_WINDOWS_PATH_CHARACTERS = 32767
_HASH_CHUNK_BYTES = 1024 * 1024


def _bounded_message(message: object) -> str:
    if isinstance(message, str):
        text = message
    else:
        text = type(message).__name__
    text = text.replace("\r", " ").replace("\n", " ")
    text = re.sub(r"[ \t]+", " ", text).strip()
    if not text:
        text = "Unspecified acceptance failure."
    if len(text) > MAX_DIAGNOSTIC_CHARACTERS:
        text = text[: MAX_DIAGNOSTIC_CHARACTERS - 1] + "\N{HORIZONTAL ELLIPSIS}"
    return text


class AcceptanceFailure(RuntimeError):
    """Machine-classifiable Phase184-H failure with a bounded diagnostic."""

    def __init__(self, code: str, bounded_message: object):
        if code not in TERMINAL_FAILURE_CODES:
            raise ValueError("Unknown Phase184-H terminal failure code.")
        self.code = code
        self.message = _bounded_message(bounded_message)
        super().__init__(f"{self.code}: {self.message}")


def _fail(message: object) -> AcceptanceFailure:
    return AcceptanceFailure(FAIL_CLI_PROVENANCE, message)


def normalize_semantic_version(value: object) -> str:
    """Return one canonical stable major.minor.patch version."""

    if not isinstance(value, str):
        raise _fail("CLI version must be one stable semantic version.")
    candidate = value.strip()
    if not candidate or len(candidate) > _MAX_VERSION_CHARACTERS:
        raise _fail("CLI version must be one stable semantic version.")
    match = _SEMANTIC_VERSION.fullmatch(candidate)
    if match is None:
        raise _fail("CLI version must be one stable semantic version.")
    return ".".join(match.groups())


def validate_sha256(value: object) -> str:
    """Validate and return one canonical uppercase SHA-256 digest."""

    if not isinstance(value, str) or _UPPER_SHA256.fullmatch(value) is None:
        raise _fail("SHA-256 must be exactly 64 uppercase hexadecimal characters.")
    return value


def sha256_file(path: os.PathLike[str] | str) -> str:
    """Stream a file into one canonical uppercase SHA-256 digest."""

    digest = hashlib.sha256()
    try:
        with pathlib.Path(path).open("rb") as stream:
            while chunk := stream.read(_HASH_CHUNK_BYTES):
                digest.update(chunk)
    except (OSError, TypeError, ValueError) as exc:
        raise _fail("CLI file could not be read for SHA-256.") from exc
    return digest.hexdigest().upper()


def validate_official_asset_url(
    asset_url: object,
    *,
    expected_release_version: object | None = None,
) -> str:
    """Validate the exact official GitHub Windows release-asset route."""

    if (
        not isinstance(asset_url, str)
        or not asset_url
        or len(asset_url) > _MAX_URL_CHARACTERS
        or asset_url != asset_url.strip()
    ):
        raise _fail("Foxglove CLI asset URL is invalid.")
    try:
        parsed = urlsplit(asset_url)
    except ValueError as exc:
        raise _fail("Foxglove CLI asset URL is invalid.") from exc
    if (
        parsed.scheme != "https"
        or parsed.netloc != "github.com"
        or parsed.query
        or parsed.fragment
    ):
        raise _fail("Foxglove CLI asset URL is not an official GitHub release asset.")

    segments = parsed.path.split("/")
    if (
        len(segments) != 7
        or segments[:5]
        != ["", "foxglove", "foxglove-cli", "releases", "download"]
        or segments[6] != CLI_ASSET_NAME
    ):
        raise _fail("Foxglove CLI asset URL is not the exact Windows asset.")

    asset_release_version = normalize_semantic_version(segments[5])
    if expected_release_version is not None:
        expected = normalize_semantic_version(expected_release_version)
        if asset_release_version != expected:
            raise _fail("Foxglove CLI asset URL release does not match the receipt.")
    return asset_release_version


def _require_receipt_string(
    receipt: Mapping[str, Any],
    key: str,
    *,
    maximum: int,
) -> str:
    value = receipt[key]
    if (
        not isinstance(value, str)
        or not value
        or len(value) > maximum
        or "\x00" in value
    ):
        raise _fail(f"CLI receipt field {key} is invalid.")
    return value


def _windows_path_key(
    value: object,
    label: str,
    *,
    allow_pathlike: bool,
) -> str:
    if allow_pathlike:
        try:
            value = os.fspath(value)
        except TypeError as exc:
            raise _fail(f"{label} must be an absolute Windows path.") from exc
    if (
        not isinstance(value, str)
        or not value
        or len(value) > _MAX_WINDOWS_PATH_CHARACTERS
        or "\x00" in value
        or "\r" in value
        or "\n" in value
    ):
        raise _fail(f"{label} must be an absolute Windows path.")

    ordinary = value.replace("/", "\\")
    folded = ordinary.casefold()
    if folded.startswith("\\\\?\\unc\\"):
        ordinary = "\\\\" + ordinary[8:]
    elif folded.startswith("\\\\?\\"):
        ordinary = ordinary[4:]

    drive, tail = ntpath.splitdrive(ordinary)
    if not drive or not tail.startswith("\\") or not ntpath.isabs(ordinary):
        raise _fail(f"{label} must be an absolute Windows drive or UNC path.")
    if drive.startswith("\\\\"):
        unc_parts = drive[2:].split("\\")
        if (
            len(unc_parts) != 2
            or not all(unc_parts)
            or unc_parts[0] in {".", "?"}
        ):
            raise _fail(f"{label} must be an absolute Windows drive or UNC path.")
    elif re.fullmatch(r"[A-Za-z]:", drive) is None:
        raise _fail(f"{label} must be an absolute Windows drive or UNC path.")
    return ntpath.normcase(ntpath.normpath(ordinary))


def windows_path_key(
    value: os.PathLike[str] | str,
    *,
    label: str = "Windows path",
) -> str:
    """Return one identity key for ordinary or extended drive/UNC paths."""

    return _windows_path_key(value, label, allow_pathlike=True)


def windows_paths_equal(
    left: os.PathLike[str] | str,
    right: os.PathLike[str] | str,
) -> bool:
    """Compare two strictly absolute Windows paths by canonical identity."""

    return windows_path_key(left) == windows_path_key(right)


def _normalize_windows_path(
    value: object,
    label: str,
    *,
    allow_pathlike: bool,
) -> str:
    """Compatibility wrapper around the canonical public path identity."""

    return _windows_path_key(value, label, allow_pathlike=allow_pathlike)


def _validate_installed_utc(value: object) -> str:
    if not isinstance(value, str) or _UTC_TIMESTAMP.fullmatch(value) is None:
        raise _fail("CLI receipt installedUtc must be a canonical UTC timestamp.")
    parse_value = value[:-1] + "+00:00"
    try:
        parsed = dt.datetime.fromisoformat(parse_value)
    except ValueError as exc:
        raise _fail("CLI receipt installedUtc must be a valid UTC timestamp.") from exc
    if parsed.utcoffset() != dt.timedelta(0):
        raise _fail("CLI receipt installedUtc must be a UTC timestamp.")
    return value


def _validate_receipt(receipt: object) -> tuple[dict[str, object], str, str, str]:
    if not isinstance(receipt, Mapping):
        raise _fail("CLI receipt root must be an object.")
    try:
        actual_keys = frozenset(receipt)
    except (TypeError, ValueError) as exc:
        raise _fail("CLI receipt keys are invalid.") from exc
    if actual_keys != CLI_RECEIPT_KEYS:
        raise _fail("CLI receipt keys do not match schemaVersion 1.")

    if (
        isinstance(receipt["schemaVersion"], bool)
        or not isinstance(receipt["schemaVersion"], int)
        or receipt["schemaVersion"] != CLI_RECEIPT_SCHEMA_VERSION
    ):
        raise _fail("CLI receipt schemaVersion is unsupported.")
    if receipt["architecture"] != CLI_ARCHITECTURE:
        raise _fail("CLI receipt architecture is not windows-amd64.")
    if receipt["assetName"] != CLI_ASSET_NAME:
        raise _fail("CLI receipt assetName is not the exact Windows asset.")

    release_tag = _require_receipt_string(
        receipt,
        "releaseTag",
        maximum=_MAX_VERSION_CHARACTERS,
    )
    release_version_text = _require_receipt_string(
        receipt,
        "releaseVersion",
        maximum=_MAX_VERSION_CHARACTERS,
    )
    download_version_text = _require_receipt_string(
        receipt,
        "downloadVersion",
        maximum=_MAX_VERSION_CHARACTERS,
    )
    installed_version_text = _require_receipt_string(
        receipt,
        "installedVersion",
        maximum=_MAX_VERSION_CHARACTERS,
    )
    release_version = normalize_semantic_version(release_tag)
    versions = {
        release_version,
        normalize_semantic_version(release_version_text),
        normalize_semantic_version(download_version_text),
        normalize_semantic_version(installed_version_text),
    }
    if len(versions) != 1:
        raise _fail("CLI receipt release and executable versions do not match.")

    asset_url = _require_receipt_string(
        receipt,
        "assetUrl",
        maximum=_MAX_URL_CHARACTERS,
    )
    validate_official_asset_url(
        asset_url,
        expected_release_version=release_version,
    )

    download_sha256 = validate_sha256(receipt["downloadSha256"])
    installed_sha256 = validate_sha256(receipt["installedSha256"])
    validate_sha256(receipt["previousSha256"])
    if download_sha256 != installed_sha256:
        raise _fail("Downloaded and installed Foxglove CLI hashes do not match.")

    installed_path = _normalize_windows_path(
        _require_receipt_string(
            receipt,
            "installedPath",
            maximum=_MAX_WINDOWS_PATH_CHARACTERS,
        ),
        "CLI receipt installedPath",
        allow_pathlike=False,
    )
    backup_path = _normalize_windows_path(
        _require_receipt_string(
            receipt,
            "backupPath",
            maximum=_MAX_WINDOWS_PATH_CHARACTERS,
        ),
        "CLI receipt backupPath",
        allow_pathlike=False,
    )
    if installed_path == backup_path:
        raise _fail("CLI receipt backupPath must differ from installedPath.")
    _validate_installed_utc(receipt["installedUtc"])

    return dict(receipt), release_version, installed_path, installed_sha256


def validate_cli_receipt(
    receipt: object,
    installed_path: os.PathLike[str] | str,
    installed_version: object,
    installed_sha256: object,
) -> dict[str, object]:
    """Validate a receipt against the exact currently installed CLI."""

    validated, receipt_version, receipt_path, receipt_sha256 = _validate_receipt(
        receipt
    )
    live_path = _normalize_windows_path(
        installed_path,
        "Installed Foxglove CLI path",
        allow_pathlike=True,
    )
    live_version = normalize_semantic_version(installed_version)
    live_sha256 = validate_sha256(installed_sha256)
    if live_path != receipt_path:
        raise _fail("Installed Foxglove CLI path does not match the receipt.")
    if live_version != receipt_version:
        raise _fail("Installed Foxglove CLI version does not match the receipt.")
    if live_sha256 != receipt_sha256:
        raise _fail("Installed Foxglove CLI hash does not match the receipt.")
    return validated


class _DuplicateJsonKey(ValueError):
    pass


def _unique_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise _DuplicateJsonKey("Duplicate JSON key.")
        result[key] = value
    return result


def load_json_bounded(
    path: os.PathLike[str] | str,
    *,
    max_bytes: int = MAX_RECEIPT_BYTES,
) -> object:
    """Load one UTF-8 JSON document without reading beyond its fixed bound."""

    if (
        isinstance(max_bytes, bool)
        or not isinstance(max_bytes, int)
        or max_bytes < 1
        or max_bytes > MAX_RECEIPT_BYTES
    ):
        raise _fail("JSON size bound is invalid.")
    try:
        with pathlib.Path(path).open("rb") as stream:
            raw = stream.read(max_bytes + 1)
    except (OSError, TypeError, ValueError) as exc:
        raise _fail("CLI receipt is unavailable.") from exc
    if not raw or len(raw) > max_bytes:
        raise _fail("CLI receipt size is invalid.")
    try:
        text = raw.decode("utf-8")
        return json.loads(text, object_pairs_hook=_unique_object)
    except (UnicodeError, ValueError, RecursionError) as exc:
        raise _fail("CLI receipt JSON is malformed.") from exc


def load_cli_receipt(path: os.PathLike[str] | str) -> dict[str, object]:
    """Load and intrinsically validate one bounded CLI installation receipt."""

    receipt = load_json_bounded(path)
    validated, _, _, _ = _validate_receipt(receipt)
    return validated


def write_json_atomic(
    destination: os.PathLike[str] | str,
    payload: Mapping[str, Any],
    *,
    max_bytes: int = MAX_RECEIPT_BYTES,
) -> None:
    """Write deterministic bounded JSON through an atomic sibling replace."""

    if (
        isinstance(max_bytes, bool)
        or not isinstance(max_bytes, int)
        or max_bytes < 1
        or max_bytes > MAX_RECEIPT_BYTES
    ):
        raise _fail("JSON size bound is invalid.")
    if not isinstance(payload, Mapping):
        raise _fail("JSON payload must be an object.")
    try:
        serialized = (
            json.dumps(
                dict(payload),
                allow_nan=False,
                ensure_ascii=True,
                separators=(",", ":"),
                sort_keys=True,
            )
            + "\n"
        ).encode("utf-8")
    except (TypeError, ValueError) as exc:
        raise _fail("JSON payload is not serializable.") from exc
    if len(serialized) > max_bytes:
        raise _fail("JSON payload exceeds the fixed size bound.")

    try:
        target = pathlib.Path(destination)
        target.parent.mkdir(parents=True, exist_ok=True)
    except (OSError, TypeError, ValueError) as exc:
        raise _fail("JSON destination is invalid.") from exc
    temporary = target.with_name(f"{target.name}.{uuid.uuid4().hex}.tmp")
    try:
        with temporary.open("xb") as stream:
            stream.write(serialized)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, target)
    except OSError as exc:
        raise _fail("JSON document could not be written atomically.") from exc
    finally:
        with contextlib.suppress(OSError):
            temporary.unlink()
